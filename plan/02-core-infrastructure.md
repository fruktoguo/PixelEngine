# Plan 02 — PixelEngine.Core 基础设施

> 本文档定义 `src/PixelEngine.Core/` 的完整实现计划。Core 是整个解决方案的依赖底座（依赖方向见 plan/00 §5：`… → Interop → Core`，Core 不反向依赖任何项目），是**无「像素」语义的可复用基础设施**：数学、内存、持久线程池、确定性 RNG、无锁事件总线、帧时钟、诊断/计时、编译期常量。
> 权威依据：`docs/PixelEngine-架构与需求设计.md`（下称「架构」）、`AGENTS.md`、`plan/00-conventions-and-techstack.md`。
> 状态约定：`- [ ]` 未开始 / `- [x]` 完成 / `- [~]` 进行中 / `- [!]` 阻塞。

---

## 1. 目标与范围

本文档交付 `PixelEngine.Core` 程序集，目标是把所有子系统都要复用、却又与「cell / material / chunk 像素语义」无关的底层能力一次性实现到位（AGENTS.md §2：无 MVP、无占位、无临时实现）。Core 提供数据结构与机制，**不解释这些数据的含义**——它知道「一段连续 `unmanaged` 内存」「一批 `[0,itemCount)` 的区间任务」「一个固定步长的帧节拍」，但不知道什么是沙、什么是反应。架构 §3.1 对 Core 的定义即「它不知道『像素』是什么」，本文严格遵守这一边界。

在范围内：数学（`Vector2`/`Vector2i`/`Transform2D`/`AABB`/`RectI`/可选 `Fixed`/`Mathx`，优先复用 `System.Numerics`，架构 §6.2）；内存（POH 与 NativeMemory 缓冲封装、`SoaBuffer` SoA 脚手架、`Pool<T>`、`ArrayPool` 封装、双缓冲容器，AGENTS.md §3 零分配纪律）；持久线程池 `JobSystem`（固定 worker + 帧 barrier，同时服务 CA 4-pass checkerboard 与 Box2D task-callback 桥，架构 §5.7、§14.2、§12.7）；确定性 RNG（每 chunk 可种子化、可切换 counter-based 纯函数式，架构 §6.2）；无锁事件总线（SPSC/MPSC ring buffer，架构 §10.2）；固定步长 + 时间膨胀帧时钟（架构 §4，落地不变式 #6）；分项计时与计数器（架构 §17.1 HUD、§4.3 过载降级数据源）；`EngineConstants` 编译期常量集中（plan/00 §7）。

明确不在范围内（留给后续 plan，本文不得越界实现）：CellGrid/Chunk/dirty-rect/checkerboard 的**业务逻辑**（plan/03，Core 只提供其依赖的 `JobSystem`/`SoaBuffer`/`RectI`/`EngineConstants`）；Box2D `[LibraryImport]` 绑定与 `[UnmanagedCallersOnly]` 回调本体（plan/01/06 在 Interop，Core 只提供其派发用的 `JobSystem.ParallelRange`）；过载降级的**决策顺序**（架构 §4.3，归 Hosting，Core 只提供检测数据）；任何 Silk.NET / Box2D / Roslyn 依赖（Core 仅依赖 BCL）。

---

## 2. 技术栈与依赖

运行时与语言遵循 plan/00 §1：.NET 10 LTS、C# 14、`Nullable=enable`、`ImplicitUsings=enable`、file-scoped namespace。Core 属热路径项目，按 plan/00 §6 开启 `<AllowUnsafeBlocks>true`、Release `<Optimize>true>`，并把零分配/SIMD 相关分析器提升为 error；CI 开 `TreatWarningsAsErrors`（AGENTS.md §4）。

依赖面：**仅 BCL**。`System.Numerics`（`Vector2`/`Vector<T>`，plan/00 §4 数学/SIMD 选型）、`System.Runtime.Intrinsics`、`System.Runtime.CompilerServices.Unsafe`、`System.Runtime.InteropServices.NativeMemory`、`System.Buffers.ArrayPool`、`System.Threading`。**不引用任何 NuGet 包，不引用任何其它 PixelEngine 项目**（这是依赖底座的硬约束，违背即架构 §3.1/plan/00 §5 冲突）。互操作纪律（AGENTS.md §3、架构 §14.3）：Core 自身无 native 调用，但其暴露给 Interop 的回调派发面（`JobSystem.ParallelRange` 的函数指针重载）必须 blittable、不抛、可被 `delegate* unmanaged` 经 IL `calli` 调用，且**绝不**标 `[SuppressGCTransition]`（架构 §14.2 明确：task 桥回调每 Step 多次重入托管，必须走正常 GC 转换）。

命名空间划分（均在 `PixelEngine.Core.*` 下）：`PixelEngine.Core`（`EngineConstants`、`DeterminismMode`）、`.Mathematics`、`.Memory`、`.Threading`、`.Random`、`.Events`、`.Time`、`.Diagnostics`。

性能纪律（AGENTS.md §3，全程强制）：稳态帧循环内 Core 的所有 API 必须零托管堆分配；热结构体 `readonly struct` + `in` 传参；per-worker / per-channel 元数据填充到 `CacheLineBytes=64` 防 false sharing（架构 §12.7）；公开 API 全部带中文 XML 文档注释（脚本 IntelliSense 依赖，AGENTS.md §4）。

---

## 3. 详细设计

### 3.1 数学（`PixelEngine.Core.Mathematics`）

优先复用 `System.Numerics.Vector2`（float 2D 向量）作为浮点向量类型，不自造；Core 仅补 BCL 缺失的整数向量、2D 变换、整数矩形与可选定点数（架构 §6.2、plan/00 §4）。

`Vector2i`（整数 2D 向量，世界以 cell 整数坐标为权威，plan/00 §7）：`readonly struct Vector2i : IEquatable<Vector2i>`，字段 `int X, Y`；`static readonly Zero/One/UnitX/UnitY`；运算符 `+ - *(scalar) ==/!=`；`int ManhattanLength`；`static Vector2i Min/Max(Vector2i,Vector2i)`；`Vector2 ToVector2()`；`static Vector2i Floor(Vector2)`、`static Vector2i Round(Vector2)`；`override int GetHashCode()`（供上层 chunk hash-map 键复用，散列质量要好）。

`Transform2D`（2D 刚体变换，旋转以 cos/sin 存储，对齐 Box2D `b2Rot` 约定以便 Interop 零成本互转，架构 §8.2/§8.3）：`readonly struct`，字段 `Vector2 Position; float Cos, Sin`；构造 `Transform2D(Vector2 pos, float radians)` 与 `Transform2D(Vector2 pos, float cos, float sin)`；`static readonly Identity`；`Vector2 TransformPoint(Vector2 local)`、`Vector2 InverseTransformPoint(Vector2 world)`（架构 §8.3 inverse-sampling 的数学基元，由 Physics 调用做水密栅格化）、`Vector2 TransformDirection(Vector2 dir)`、`float Angle { get; }`。

`AABB`（浮点包围盒）：`readonly struct`，`Vector2 Min, Max`；`Vector2 Center/Extents`；`bool Contains(Vector2)`、`bool Intersects(in AABB)`、`AABB Union(in AABB)`、`AABB Expand(float margin)`、`RectI ToRectI()`（floor/ceil 到整数 cell 边界）。

`RectI`（整数矩形，半开区间 `[Min,Max)`；上层 dirty rectangle 的 grow/shrink 直接用它，架构 §5.4）：`struct RectI : IEquatable<RectI>`，字段 `int MinX,MinY,MaxX,MaxY`；`bool IsEmpty`、`int Width/Height/Area`；`static RectI Empty`；`void Encapsulate(int x,int y)`（grow，dirty-rect 扩张）、`void Encapsulate(in RectI)`、`void ExpandClamped(int padding, in RectI bounds)`（按 `DirtyRectPadding` 扩张并钳制在 chunk 边界内，架构 §5.4/§5.5）；`bool Contains(int,int)`、`bool Intersects(in RectI)`、`RectI Intersection(in RectI)`、`static RectI FromBounds(int,int,int,int)`。

`Fixed`（可选定点数，确定性模式专用，架构 §6.2/§6.4）：`readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>`，Q32.32 `long Raw` 后端；`const int FractionalBits=32`；`static readonly Zero/One/Half`；`static Fixed FromInt(int)`、`static Fixed FromRaw(long)`、显式 `Fixed(float)`/`float(Fixed)` 转换（仅用于初始化/调试，非确定性热路径）；确定性的 `+ - * /` 与比较运算符；`static Fixed Sqrt(Fixed)`；`int ToInt()`（截断）、`int RoundToInt()`（固定 round 模式，架构 §6.2「定点或固定 round 模式」）。注：默认非确定性高性能模式不使用 `Fixed`，它是 §6.2 三处变化点中「热场/粒子积分定点化」的预留 seam。

`Mathx`（标量工具，含负数正确的 chunk 坐标分解）：`static class`；`int FloorDiv(int a,int b)`、`int Mod(int a,int b)`（floored modulo，cell→chunk 坐标与 chunk-local 取模在负世界坐标下必须正确，否则 hash-map 键错乱）、`int CeilDiv(int,int)`、`int Clamp(int,int,int)`、`float Clamp01(float)`、`float Lerp(float,float,float)`、`int NextPowerOfTwo(int)`、`bool IsPowerOfTwo(int)`、`int Log2Int(int)`。

### 3.2 内存（`PixelEngine.Core.Memory`）

落地 AGENTS.md §3 / plan/00 §4「跨界缓冲内存 = POH / NativeMemory」与零分配纪律，为 sim/physics/render 跨界零拷贝（架构 §14.3）提供承载体。

`PinnedBuffer<T>`（POH 封装，`GC.AllocateArray<T>(n, pinned:true)`）：`sealed class PinnedBuffer<T> : IDisposable where T:unmanaged`；构造 `PinnedBuffer(int length)`；`int Length`、`Span<T> Span`、`Memory<T> Memory`、`unsafe T* Pointer`（地址在对象存活期稳定，可安全传给 native）、`ref T GetReference()`（`MemoryMarshal.GetArrayDataReference`，供 §3.2 指针漫游消除 bounds-check，架构 §12.6）、`ref T this[int]`、`void Clear()`、`void Dispose()`。POH 数组本身受 GC 管理且无碎片化、无 per-call pin（plan/00 §4 备注）。

`NativeBuffer<T>`（`NativeMemory.Alloc`/`AllocZeroed` 封装）：`sealed unsafe class NativeBuffer<T> : IDisposable where T:unmanaged`；构造 `NativeBuffer(int length, bool zeroed=true)`；`int Length`、`T* Pointer`、`Span<T> Span`、`ref T GetReference()`、`ref T this[int]`、`void Clear()`、`void Dispose()`（`NativeMemory.Free`）；带 finalizer 兜底防泄漏。用于完全脱离 GC 堆的大缓冲。

`SoaBuffer` + `SoaColumn<T>`（SoA 脚手架，架构 §7.1「sim 热数据一律 SoA」，但本类不含像素语义）：`enum MemoryKind { Poh, Native }`；`sealed class SoaColumn<T> where T:unmanaged`（单列数组，POH 或 Native 后端，暴露 `Span<T>`/`ref T GetReference()`/`T* Pointer`）；`abstract class SoaBuffer : IDisposable`，`int Count { get; protected set; }`、`int Capacity { get; }`、`protected SoaColumn<T> DefineColumn<T>(MemoryKind)`（子类在构造期声明各列）、`void EnsureCapacity(int)`（所有列同步扩容并保留数据）、`void Clear()`、`void Dispose()`。CellGrid（plan/03）与 free-particle 池（plan/03）将派生它，Core 只管「多列等长数组的统一容量与生命周期」。

`Pool<T>`（对象池，AGENTS.md §3）：`sealed class Pool<T> where T:class`；构造 `Pool(Func<T> factory, Action<T>? onRent=null, Action<T>? onReturn=null, int preallocate=0, int maxRetained=-1)`；`T Rent()`、`void Return(T)`、`int CountInactive`。线程安全策略：默认单线程使用（帧循环主线程）；并发需求由调用方用 `WorkerLocal<Pool<T>>`（见 §3.3）隔离，避免锁进热路径。

`RentedArray<T>`（`ArrayPool<T>.Shared` 封装，using-scope 自动归还）：`readonly struct RentedArray<T> : IDisposable`；`T[] Array`、`int Length`、`Span<T> Span`；`static RentedArray<T> Rent(int minLength, bool clear=false)`、`void Dispose()`（归还）。用于离线/边界处的临时缓冲（如序列化字节准备、形状重建 scratch），不进 per-cell 内层。

`DoubleBuffer<TBuffer>`（双缓冲容器，供 sim/physics/render 跨界零拷贝，帧相位边界翻转）：`sealed class DoubleBuffer<TBuffer> where TBuffer:class`；构造 `DoubleBuffer(Func<TBuffer> factory)`；`TBuffer Front { get; }`（消费者只读，如 render 相位 9/10）、`TBuffer Back { get; }`（生产者写，如 sim/physics）、`void Swap()`（在相位边界单线程翻转，架构 §3.2 相位顺序避免竞争）。注意与 dirty-rect 的 working/current 双缓冲（架构 §5.4，属 Simulation）概念同源但本类是通用容器。

### 3.3 持久线程池 `JobSystem`（`PixelEngine.Core.Threading`）

引擎并行的唯一入口，**同时服务 CA 4-pass checkerboard（架构 §5.7，相位 4）与 Box2D task-callback 桥（架构 §14.2，相位 8c）**，二者因相位顺序错开而共用同一组持久 worker（架构 §12.7）。它**不是每帧 `Parallel.For`**——固定 worker 线程常驻，每次派发即一次 fork-join barrier，避免 60fps 细粒度工作下的分区/委托开销（架构 §5.7、§12.7）。

`sealed class JobSystem : IDisposable`：
- 构造 `JobSystem(int workerCount=0)`（0 → 取物理核数；每物理核一线程，架构 §12.7），`int WorkerCount { get; }`。
- 区间 fork-join（Box2D 桥与通用 parallel-for 用）：`void ParallelRange(int itemCount, int minRange, RangeJob body, object? context=null)`——把 `[0,itemCount)` 切成 ≥`minRange` 的区间派发，阻塞至全部完成（= barrier）。`delegate void RangeJob(int start, int end, int workerIndex, object? context)`。Box2D 的 `enqueueTask` 收到 `itemCount`/`minRange` 即转调此方法（架构 §14.2）。
- 区间 fork-join 的零分配低层重载（供 Interop task 桥免去 delegate 分配，经 `delegate* unmanaged` calli 回调 native `b2TaskCallback`）：`unsafe void ParallelRangeRaw(int itemCount, int minRange, delegate*<int,int,int,void*,void> body, void* context)`。
- chunk-task fork-join（CA checkerboard 每 pass 用）：`void ParallelFor<TState>(ReadOnlySpan<TState> items, ChunkJob<TState> body) where TState:struct`，`delegate void ChunkJob<TState>(in TState item, int workerIndex)`。Simulation 每帧调用四次（pass A/B/C/D），调用之间天然 barrier（架构 §5.7）。
- 显式异步句柄（架构 §14.2「后续若要重叠可改真正异步 join」的 seam）：`JobHandle Schedule(int itemCount, int minRange, RangeJob body, object? context)`、`void Wait(in JobHandle)`；`readonly struct JobHandle`。
- 单线程回退（架构 R7：活跃任务少时利用率差、barrier 主导）：`int SingleThreadThreshold { get; set; }`——当 `itemCount`/`items.Length` 低于阈值时在调用线程内联执行，跳过派发开销。
- 稳定 workerIndex：每 worker 线程持有固定 `workerIndex ∈ [0,WorkerCount)`，经 body 参数传出。这是 Box2D task 桥的硬需求（架构 §14.2「以稳定的 workerIndex 回调」、R14「分配稳定 workerIndex」），也是 false-sharing 填充的索引依据。
- `void Dispose()`：joinable 关闭所有 worker。

`WorkerLocal<T>`（per-worker 槽位，cache-line 填充防 false sharing，架构 §12.7）：`sealed class WorkerLocal<T> where T:class`；构造 `WorkerLocal(JobSystem jobs, Func<int,T> factory)`；`T this[int workerIndex] { get; }`（各槽按 `CacheLineBytes=64` 填充对齐）、`IReadOnlyList<T> Slots`。用于 worker 私有累加器（dirty-rect 合并、事件 SPSC 队列、per-worker 池），在相位末单线程合并。

barrier 语义说明：本设计不暴露独立 `Barrier` 原语——每个 `ParallelRange`/`ParallelFor`/`Wait` 调用即一个完整 fork-join barrier；CA 的「pass 间 barrier」由「四次连续调用」自然形成（架构 §5.7「4 遍之间用 barrier 分隔」）。确定性/lockstep 模式下 Box2D 桥设 `workerCount=1` 串行（架构 §6.4/§14.2），由 Interop 决定，Core 仅按传入参数执行。

### 3.4 确定性 RNG（`PixelEngine.Core.Random`）

架构 §6.2 三处变化点之一（RNG），必须从一开始就抽象为可替换策略：默认高性能有状态 RNG + 可切换 counter-based 纯函数式 RNG（每 chunk 可种子化）。

- `enum DeterminismMode { HighPerformance, Deterministic }`（定义在 `PixelEngine.Core` 根命名空间，统一 §6.2 三处 seam 的总开关）。
- `interface IRandomSource`：`uint NextUInt()`、`int NextInt(int maxExclusive)`、`float NextFloat()`（`[0,1)`）。
- 默认有状态实现 `struct Pcg32 : IRandomSource`：构造 `Pcg32(ulong seed, ulong stream)`；高吞吐，供非确定性模式的逐帧左右交替偏置等（架构 §5.6）。
- counter-based 纯函数式（无可变状态、可精确重演，架构 §6.2/§6.3「初态快照 + 输入流重演」）：`static class CounterRng`，`uint Hash(uint seed, int x, int y, uint counter)`（squirrel/PCG-hash 风格雪崩混合）、`float ToFloat01(uint bits)`、`uint NextUInt(uint seed, int x, int y, ref uint counter)`。
- 每 chunk 种子化工厂：`static class RngFactory`，`Pcg32 ForChunk(ulong worldSeed, int chunkX, int chunkY, uint frame)`、`IRandomSource CreateDefault(ulong seed)`、`IRandomSource CreateDeterministic(ulong seed)`。Simulation 按 `DeterminismMode` 选择实现（架构 §6.2 代价：部分性能/并行度）。

### 3.5 无锁事件总线（`PixelEngine.Core.Events`）

架构 §10.2：sim 不直接播声/触发玩法，而是把粗粒度事件写进无锁 ring buffer，由音频/玩法/编辑器每帧消费；**事件消费（混音等）绝不进 sim 热循环**，sim 侧只做廉价 enqueue。

- `sealed class RingBuffer<T> where T:unmanaged`（SPSC 无锁，单生产者单消费者）：构造 `RingBuffer(int capacityPow2)`；`bool TryEnqueue(in T)`、`bool TryDequeue(out T)`、`int DrainTo(Span<T> dest)`、`int Count`。每 worker 持一个（经 `WorkerLocal`），相位末单消费者 drain。
- `sealed class MpscRingBuffer<T> where T:unmanaged`（多生产者单消费者，CAS tail）：`bool TryEnqueue(in T)`、`int DrainTo(Span<T> dest)`、`int Count`。用于多 CA worker 直接投递、单线程消费的简化场景。
- `sealed class EventBus`：构造 `EventBus(int capacityPerChannel)`；`MpscRingBuffer<T> Channel<T>() where T:unmanaged`（按类型获取/创建通道）。Core 只定义**通用传输**，事件载荷（如音频 cue 事件，架构 §10.2/§7.3 `AudioCueSet`）由消费层定义，对 Core 透明（`where T:unmanaged` 约束，零字符串/零装箱）。容量满时 `TryEnqueue` 返回 false 并由调用方计入限频去重（架构 §10.2「每帧上限 N 个、相近坐标合并」）。

### 3.6 帧时钟（`PixelEngine.Core.Time`）—— 不变式 #6 落地点

架构 §4：固定逻辑步长 `dt=1/60` + 时间膨胀，**绝不追帧**（不变式 #6）。允许的唯一解耦是 sim 降频（30Hz）而 render 不降（架构 §4.2）。

`sealed class FrameClock`：
- 构造 `FrameClock(double simHz = EngineConstants.DefaultSimHz)`。
- `double Dt { get; }`（= `1/SimHz`，固定；**绝不被放大以补步**，不变式 #6 的硬保证）、`double SimHz { get; set; }`（60 默认；降级时切 30，架构 §4.2/§4.3 第四级）、`long FrameIndex { get; }`、`long SimTickIndex { get; }`（已执行的 sim tick 数，降频时 < 帧数）。
- `FrameTiming BeginFrame(double realDeltaSeconds)`：每渲染帧调用一次，返回本帧时序决策。`readonly struct FrameTiming { double Dt; bool RunSim; bool RunPhysics; long FrameIndex; long SimTickIndex; }`。
- `bool RunSimThisFrame { get; }`（30Hz 时每两帧才 true，架构 §4.2「每两个渲染帧执行一次相位 3–8」）、`double TimeScale { get; }`（过载时 <1 的时间膨胀因子，架构 §4.1）。

关键不变式落地（必须在实现处写「为什么」注释引用架构 §4）：**绝无** `while (accumulator >= dt) Step();` 形态的追帧循环；每帧 `RunSim/RunPhysics` 至多触发一次 step，CA/physics/particle 三者用同一 `Dt`、1:1 绑定同一 sim tick（架构 §4.1）；真实帧超 16.6ms 时整体放慢而非补步（避免 death spiral，架构 §2 挑战七、§4.1）。Demo 玩法的「带上限小型 accumulator」（架构 §4.4）属 Demo 层，Core 不提供、也绝不让任何 accumulator 驱动 sim 额外步。

### 3.7 诊断 / 分项计时（`PixelEngine.Core.Diagnostics`）

为编辑器性能 HUD（架构 §17.1）与过载降级（架构 §4.3）提供**数据**；降级**决策顺序**归 Hosting（本文不实现，仅提供检测原语）。

- `enum FramePhase : byte`，按架构 §3.3 的 12 相位定义：`InputAndTime=0, GameLogic=1, ResidencyApply=2, ParticleToCell=3, CaSimulation=4, Temperature=5, DirtyRectSwap=6, CellToParticle=7, PhysicsSync=8, BuildRenderBuffer=9, GpuUploadRender=10, WorldStreaming=11`。
- `enum FrameSubPhase : byte`，覆盖 HUD 需要的细分（架构 §17.1「CA pass A–D / physics step / 形状重建」）：`CaPassA, CaPassB, CaPassC, CaPassD, PhysicsStep, ShapeRebuild, GpuUpload, AudioDispatch`。
- `sealed class FrameProfiler`：`ProfilerScope Measure(FramePhase phase)`（返回 `readonly struct ProfilerScope : IDisposable`，using-scope 零分配，基于 `Stopwatch.GetTimestamp`）、`void Record(FramePhase, double ms)`、`void RecordSub(FrameSubPhase, double ms)`、`void BeginFrame()`、`void EndFrame()`、`ReadOnlySpan<double> LastFrame { get; }`（每相位 ms）、`double Average(FramePhase, int window)`（近 N 帧环形平均）。
- `sealed class EngineCounters`（架构 §17.1 HUD 计数项；plan/00 §93 各子系统注册）：`long ActiveChunks, ActiveCells, FreeParticles, RigidBodies, ResidentChunks`、`long ResidentMemoryBytes`、`double SimHz`；提供线程安全的 worker 累加入口（`WorkerLocal<long[]>` 风格或 `Interlocked`），相位末合并。
- `sealed class BudgetMonitor`（架构 §4.3 降级的检测数据源，不含策略）：构造 `BudgetMonitor(double budgetMs, int sustainWindow)`；`void Submit(double frameMs)`、`bool IsSustainedOverBudget { get; }`、`int ConsecutiveOverBudgetFrames { get; }`。Hosting 据此按 §4.3 五级顺序降级，Core 只回答「是否连续超预算」。

### 3.8 编译期常量 `EngineConstants`（`PixelEngine.Core`）

plan/00 §7 明确要求把编译期常量**集中到 `PixelEngine.Core` 的 `EngineConstants`**（便于 JIT 优化与统一调参）。这是 Core「无像素语义」边界的唯一受规约豁免点：常量在此集中声明，但 Core 代码本身不据其解释 cell 内容。

`static class EngineConstants`：`ChunkSize=64`（架构 §5.1）、`ChunkSizeLog2=6`、`ChunkArea=ChunkSize*ChunkSize`、`MoveCap=32`（架构 §5.8，半个 chunk）、`HaloSize=32`（= MoveCap，架构 §5.7）、`PhysicsPixelsPerMeter=16`（架构 §8.1，1 物理单位=16px）、`MetersPerPixel=1f/16f`、`TempFieldDownscale=4`（架构 §7.5，CELL=4）、`DefaultSimHz=60.0`（架构 §4.1）、`SimHzDownscaled=30.0`（架构 §4.2）、`DirtyRectPadding=2`（架构 §5.4，可调）、`SingleThreadChunkThreshold=4`（架构 §5.7，活跃 chunk 低阈值回退）、`BorderRingWidth=1`（架构 §3.4，单位 chunk）、`CacheLineBytes=64`（架构 §12.7 false-sharing 填充）。全部 `const`，附 XML 注释引用对应架构 §。

---

## 4. 实现清单

> 命名到文件/类型/方法级；标注架构 § 与帧相位（架构 §3.3）。所有公开 API 带中文 XML 注释（AGENTS.md §4）。稳态帧循环零分配（AGENTS.md §3）。

### 4.1 项目骨架
- [x] 建 `src/PixelEngine.Core/PixelEngine.Core.csproj`，继承 `Directory.Build.props`，开 `<AllowUnsafeBlocks>true`，**无任何 ProjectReference、无任何 PackageReference（仅 BCL）**（plan/00 §5/§6）。
- [x] 建命名空间骨架文件夹 `Mathematics/Memory/Threading/Random/Events/Time/Diagnostics/`。

### 4.2 数学（`Mathematics/`，架构 §6.2/§8.2/§8.3，plan/00 §7）
- [x] `Vector2i.cs`：`readonly struct Vector2i`，含 `X/Y`、`Zero/One/UnitX/UnitY`、`+ - *(scalar) ==/!=`、`ManhattanLength`、`Min/Max`、`ToVector2`、`Floor/Round(Vector2)`、`GetHashCode`。
- [x] `Transform2D.cs`：`readonly struct Transform2D`（cos/sin 旋转，对齐 `b2Rot`），含两个构造、`Identity`、`TransformPoint`、`InverseTransformPoint`（架构 §8.3 inverse-sampling 基元）、`TransformDirection`、`Angle`。
- [x] `AABB.cs`：`readonly struct AABB`，含 `Center/Extents`、`Contains`、`Intersects`、`Union`、`Expand`、`ToRectI`。
- [x] `RectI.cs`：`struct RectI`，含 `IsEmpty/Width/Height/Area`、`Empty`、`Encapsulate(int,int)`/`Encapsulate(in RectI)`、`ExpandClamped`、`Contains`、`Intersects`、`Intersection`、`FromBounds`（供上层 dirty-rect grow/shrink，架构 §5.4/§5.5）。
- [x] `Fixed.cs`：`readonly struct Fixed`（Q32.32），含 `Raw/FractionalBits`、`Zero/One/Half`、`FromInt/FromRaw`、显式 float 互转、确定性 `+ - * /` 与比较、`Sqrt`、`ToInt/RoundToInt`（架构 §6.2 确定性 seam）。
- [x] `Mathx.cs`：`FloorDiv`、`Mod`（floored，负坐标 chunk 分解正确）、`CeilDiv`、`Clamp(int)`、`Clamp01`、`Lerp`、`NextPowerOfTwo`、`IsPowerOfTwo`、`Log2Int`。

### 4.3 内存（`Memory/`，架构 §7.1/§12.6/§14.3，AGENTS.md §3）
- [x] `PinnedBuffer.cs`：`PinnedBuffer<T>`（`GC.AllocateArray(pinned:true)`），含 `Length/Span/Memory/Pointer/GetReference/this[]/Clear/Dispose`。
- [x] `NativeBuffer.cs`：`NativeBuffer<T>`（`NativeMemory.Alloc/AllocZeroed`），含 `Length/Pointer/Span/GetReference/this[]/Clear/Dispose` + finalizer。
- [x] `SoaColumn.cs` + `SoaBuffer.cs`：`MemoryKind` 枚举、`SoaColumn<T>`、`abstract SoaBuffer`（`Count/Capacity/DefineColumn<T>/EnsureCapacity/Clear/Dispose`），供 CellGrid/粒子池派生（架构 §7.1）。
- [x] `Pool.cs`：`Pool<T>`（`factory/onRent/onReturn/preallocate/maxRetained`、`Rent/Return/CountInactive`）。
- [x] `RentedArray.cs`：`readonly struct RentedArray<T>`（`ArrayPool<T>.Shared` 封装，`Rent/Dispose/Array/Length/Span`）。
- [x] `DoubleBuffer.cs`：`DoubleBuffer<TBuffer>`（`Front/Back/Swap`，相位边界翻转，架构 §3.2，相位 6/9/10 用）。

### 4.4 持久线程池（`Threading/`，架构 §5.7/§12.7/§14.2，R7/R14）
- [x] `JobSystem.cs`：固定 worker + 帧 barrier 的 fork-join 调度器；`WorkerCount`、`ParallelRange(itemCount,minRange,RangeJob,context)`、`ParallelFor<TState>(ReadOnlySpan<TState>,ChunkJob)`、`Schedule/Wait/JobHandle`、`SingleThreadThreshold`（R7 回退）、稳定 `workerIndex`、`Dispose`。**非每帧 Parallel.For**。
- [x] `JobSystem.Raw.cs`：`unsafe ParallelRangeRaw(itemCount,minRange, delegate*<int,int,int,void*,void>, void*)` 零分配重载，供 Interop Box2D task 桥经 `calli` 回调 native `b2TaskCallback`（架构 §14.2，相位 8c）；确保**不**标 `[SuppressGCTransition]`（R14）。
- [x] `RangeJob`/`ChunkJob<TState>` 委托与 `JobHandle` 结构定义。
- [x] `WorkerLocal.cs`：`WorkerLocal<T>`（64 字节 cache-line 填充防 false sharing，架构 §12.7），`this[workerIndex]/Slots`。
- [x] 在实现处写「为什么持久池而非 Parallel.For」「workerIndex 稳定性对 Box2D 桥的意义」注释，引用架构 §5.7/§14.2。

### 4.5 确定性 RNG（`Random/`，架构 §6.2/§6.3）
- [x] `DeterminismMode.cs`：`enum DeterminismMode { HighPerformance, Deterministic }`（置于 `PixelEngine.Core` 根命名空间）。
- [x] `IRandomSource.cs`：`NextUInt/NextInt/NextFloat`。
- [x] `Pcg32.cs`：`struct Pcg32 : IRandomSource`（有状态高吞吐，默认模式）。
- [x] `CounterRng.cs`：`static CounterRng`（`Hash(seed,x,y,counter)`、`ToFloat01`、`NextUInt(...,ref counter)`，纯函数式确定性）。
- [x] `RngFactory.cs`：`ForChunk(worldSeed,chunkX,chunkY,frame)`、`CreateDefault`、`CreateDeterministic`（每 chunk 可种子化 + 模式切换 seam，架构 §6.2）。

### 4.6 事件总线（`Events/`，架构 §10.2）
- [x] `RingBuffer.cs`：`RingBuffer<T> where T:unmanaged`（SPSC 无锁，`TryEnqueue/TryDequeue/DrainTo/Count`）。
- [x] `MpscRingBuffer.cs`：`MpscRingBuffer<T>`（多生产者单消费者，CAS tail，`TryEnqueue/DrainTo/Count`）。
- [x] `EventBus.cs`：`EventBus`（`Channel<T>()` 按类型获取通道，`unmanaged` 约束零装箱）。在注释强调消费绝不进 sim 热循环（架构 §10.2），生产侧仅廉价 enqueue（计入「音频派发 ≤1ms」，架构 §1.4/§10.3）。

### 4.7 帧时钟（`Time/`，架构 §4，不变式 #6）
- [x] `FrameClock.cs`：`FrameClock`，含 `Dt`（固定、绝不放大）、`SimHz`（60/30 切换）、`FrameIndex/SimTickIndex`、`BeginFrame(realDeltaSeconds)`、`FrameTiming`、`RunSimThisFrame`、`TimeScale`。
- [x] `FrameTiming.cs`：`readonly struct FrameTiming { Dt; RunSim; RunPhysics; FrameIndex; SimTickIndex; }`。
- [x] 在实现处写明「**绝无追帧 accumulator**，每帧至多一步，过载即时间膨胀」注释，引用架构 §4.1 与不变式 #6（驱动相位 0，架构 §3.3）。

### 4.8 诊断 / 计时（`Diagnostics/`，架构 §17.1/§4.3，plan/00 §93）
- [x] `FramePhase.cs` + `FrameSubPhase.cs`：按架构 §3.3 的 12 相位与 §17.1 细分定义枚举。
- [x] `FrameProfiler.cs`：`Measure(phase)→ProfilerScope`（`IDisposable` 零分配）、`Record/RecordSub/BeginFrame/EndFrame/LastFrame/Average`（基于 `Stopwatch.GetTimestamp`）。
- [x] `EngineCounters.cs`：`ActiveChunks/ActiveCells/FreeParticles/RigidBodies/ResidentChunks/ResidentMemoryBytes/SimHz`（架构 §17.1）+ worker 累加合并入口。
- [x] `BudgetMonitor.cs`：`BudgetMonitor(budgetMs,sustainWindow)`、`Submit/IsSustainedOverBudget/ConsecutiveOverBudgetFrames`（架构 §4.3 降级数据源，不含策略）。

### 4.9 编译期常量（`EngineConstants.cs`，plan/00 §7）
- [x] `EngineConstants.cs`：`ChunkSize/ChunkSizeLog2/ChunkArea/MoveCap/HaloSize/PhysicsPixelsPerMeter/MetersPerPixel/TempFieldDownscale/DefaultSimHz/SimHzDownscaled/DirtyRectPadding/SingleThreadChunkThreshold/BorderRingWidth/CacheLineBytes`，逐项 XML 注释引用架构 §。

---

## 5. 验收标准

> 全部勾选方算本文档完成（AGENTS.md §7）。性能敏感项按 AGENTS.md §3「校验而非臆断」用 BenchmarkDotNet + 反汇编证实。

### 5.1 编译与依赖
- [x] `dotnet build src/PixelEngine.Core -c Release` 零警告（`TreatWarningsAsErrors`）通过。
- [x] Core 的 `.csproj` 无任何 `ProjectReference`、无任何 `PackageReference`（仅 BCL）；依赖方向不被破坏（plan/00 §5/§8）。
- [x] 全部公开类型/方法带中文 XML 文档注释，`dotnet build` 无缺注释告警（AGENTS.md §4）。

### 5.2 数学正确性
- [x] `Mathx.FloorDiv/Mod` 在负坐标下与「floored 除法/模」数学定义一致（xUnit 覆盖 `x∈[-130,130]`、`b=64`），保证 cell→chunk 分解正确。
- [x] `Transform2D.InverseTransformPoint(TransformPoint(p)) ≈ p`（含任意旋转，误差 < 1e-5），为架构 §8.3 inverse-sampling 水密性背书。
- [x] `RectI.Encapsulate`/`ExpandClamped` 在 chunk 边界（`bounds=[0,64)`）下不越界、padding 正确（架构 §5.4）。
- [x] `Fixed` 四则与比较在相同输入下跨平台逐位一致（同一进程内确定性单测；为架构 §6.2/§6.4 确定性模式背书）。
- [x] 优先复用 `System.Numerics.Vector2`，未重复造浮点向量（代码审查确认）。

### 5.3 内存与零分配
- [x] `PinnedBuffer<T>.Pointer` 地址在 GC 压力下保持稳定（强制 `GC.Collect` 后地址不变）。
- [x] `NativeBuffer<T>` 分配/释放无泄漏（压力测试 + finalizer 兜底验证）。
- [x] `Pool<T>.Rent/Return`、`RentedArray<T>` 在稳态循环（≥10^6 次）**零 Gen0 分配**（BenchmarkDotNet `[MemoryDiagnoser]` 证实 `Allocated == 0`）。
- [x] `DoubleBuffer<T>.Swap` 仅交换引用、不拷贝、零分配。
- [x] `SoaBuffer.EnsureCapacity` 扩容后各列等长且原数据保留。

### 5.4 JobSystem
- [x] `ParallelRange`/`ParallelFor` 的 `workerIndex` 在整个生命周期对每个物理 worker 稳定且 `∈[0,WorkerCount)`（架构 §14.2、R14）。
- [x] 每次派发构成完整 barrier：返回后所有区间/任务确已完成（并发正确性测试，含数据竞争探测）。
- [x] 活跃任务数 < `SingleThreadThreshold` 时回退单线程、无派发开销（架构 R7，BenchmarkDotNet 对比确认）。
- [x] `ParallelRange` 与 `ParallelRangeRaw` 多 worker 稳态派发零托管分配；Raw path 可经 `delegate* unmanaged` 回调被 native 函数指针消费（模拟 Box2D `b2TaskCallback` 签名验证，架构 §14.2），见 `docs/benchmark-reports/2026-07-03-jobsystem-parallelrange-zero-allocation.md`。
- [x] 稳态 `ParallelFor` 调用零分配（缓存委托/无闭包捕获，BenchmarkDotNet 证实）；确认**非** `Parallel.For`（代码审查）。
- [x] `WorkerLocal<T>` 槽位间隔 ≥ 64 字节（反射/布局校验 false-sharing 填充，架构 §12.7）。

### 5.5 RNG / 事件 / 时钟
- [x] `CounterRng.Hash` 雪崩性达标（位独立性统计测试），同 `(seed,x,y,counter)` 恒定输出（纯函数式，架构 §6.2）。
- [x] `RngFactory.ForChunk` 不同 chunk 坐标产生统计独立流（架构 §5.6 左右交替偏置依赖）。
- [x] `RingBuffer`/`MpscRingBuffer` 在多生产者并发下不丢/不重/不撕裂（压力测试），`TryEnqueue` 满时返回 false（限频依据，架构 §10.2）；稳态零分配。
- [x] `FrameClock`：真实帧时长翻倍时 `Dt` 不变、每帧至多一次 `RunSim`（**无追帧**），`TimeScale<1`（时间膨胀，架构 §4.1、不变式 #6）。
- [x] `FrameClock` 30Hz 模式下每两渲染帧 `RunSimThisFrame` 为 true 一次（架构 §4.2）。

### 5.6 诊断与常量
- [x] `FrameProfiler.Measure` using-scope 零分配（BenchmarkDotNet 证实），覆盖 §3.3 全部 12 相位 + §17.1 细分。
- [x] `EngineCounters` 暴露架构 §17.1 全部计数项（活跃 chunk/cell/粒子/刚体/常驻 chunk/内存/sim 频率）。
- [x] `BudgetMonitor.IsSustainedOverBudget` 在连续 N 帧超预算后置位、回落后复位（架构 §4.3 数据源）。
- [x] `EngineConstants` 含 `ChunkSize=64/MoveCap=32/PhysicsPixelsPerMeter=16/TempFieldDownscale=4` 等全部常量且值与架构/plan-00 一致（plan/00 §7、§8）。

---

## 6. 依赖关系

上游（本文档的前置）：`plan/00`（技术栈/解决方案结构/`Directory.Build.props`/`Directory.Packages.props` 必须先就位）。Core 不依赖任何其它 plan 的产物。

下游（依赖本文档产物的后续）：`plan/01 Interop`（Box2D task 桥用 `JobSystem.ParallelRangeRaw`/`WorkerLocal`，架构 §14.2）；`plan/03 Simulation`（`SoaBuffer`/`RectI`/`JobSystem.ParallelFor`/`RngFactory`/`EngineConstants`/`DoubleBuffer`，架构 §5/§7）；`plan/06 Physics`（`Transform2D`/`AABB`/`JobSystem`，架构 §8）；Rendering/Audio/World/Serialization/Hosting/Editor（`FrameProfiler`/`EngineCounters`/`EventBus`/`FrameClock`/`BudgetMonitor`，架构 §17/§10/§4）。Hosting 据 `BudgetMonitor` 实现 §4.3 降级**顺序**（本文不实现）。

里程碑映射（架构 §18）：本文档是 M0「骨架 + 垂直切片 + 帧节奏」的基础设施支撑（`FrameClock` 落地 §4 帧节奏、`FrameProfiler` 落地帧计时 overlay）；`JobSystem` 多线程能力在 M2 被 checkerboard 全面消费；`Fixed`/`DeterminismMode`/`CounterRng` 是 M10 确定性模式开关的预留 seam（架构 §6.2）。

无与不变式/技术栈的冲突：Core 仅依赖 BCL（plan/00 §4/§5）；`FrameClock` 严格实现「固定步长 + 时间膨胀、不追帧」（不变式 #6）；`JobSystem` 为持久池而非 `Parallel.For`（AGENTS.md §3、不变式 #2 的调度承载）；`EngineConstants` 集中常量为 plan/00 §7 明确要求（Core「无像素语义」的受规约豁免，已在 §3.8 说明）。

---

## 7. 提交节点

按 AGENTS.md §6 中文小步提交，每节点完成即提交（`scope=core`）：

- [x] 节点 1：`feat(core): 建立 PixelEngine.Core 项目骨架与数学库`（§4.1 + §4.2，提交信息附「对应计划: plan/02 §实现清单 4.1–4.2」）。
- [x] 节点 2：`feat(core): 实现内存封装(POH/NativeMemory/SoA/池/双缓冲)`（§4.3）。
- [x] 节点 3：`feat(core): 实现持久线程池 JobSystem 与 WorkerLocal`（§4.4，含 Box2D task 桥派发面 §14.2）。
- [x] 节点 4：`feat(core): 实现确定性 RNG 与无锁事件总线`（§4.5 + §4.6）。
- [x] 节点 5：`feat(core): 实现固定步长时间膨胀帧时钟(不追帧)`（§4.7，落地不变式 #6）。
- [x] 节点 6：`feat(core): 实现分项计时/计数器诊断与 EngineConstants`（§4.8 + §4.9）。
- [x] 节点 7：`test(core): 补齐 Core 基础设施性质/零分配/并发测试`（§5 验收标准对应单测与 BenchmarkDotNet 门禁全绿）。
