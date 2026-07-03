# Plan 16 — 跨切面性能加固审计（Performance Hardening Audit）

> 本文件是一份**横切所有子系统的性能加固审计清单**。它不新增子系统，而是把散落在 plan/02–plan/15 各处的「性能纪律」汇成一张**可逐项核对的审计表**，确认引擎在交付前确实做到了 AGENTS.md §2「能多线程就多线程、能省内存就省内存、能上 GPU 计算就上 GPU」与 §3「性能纪律（强制，非优化项）」。
> 权威依据：`../docs/PixelEngine-架构与需求设计.md`（下称「架构」，重点 §2、§5.4、§7.1、§12 全章、§17.3、§19 风险项）。技术栈以 `00-conventions-and-techstack.md` 为准，开发宪法 `../AGENTS.md`。
> 状态约定：`- [ ]` 未开始 / `- [x]` 完成并自测 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。
> 本文档**不引入任何与不变式或技术栈冲突的做法**；若审计中发现实现与本表冲突，按 AGENTS.md §1 停止并上报。

---

## 1. 目标与范围

本文档的目标是给「性能拉满」一个**可证伪、可勾选的验收边界**：每一条性能纪律都落成一个带勾选框的审计项，标注它涉及哪个子系统、对应哪份 plan 文档、依据架构哪一节，并给出「怎样算通过」的实测/反汇编判据。它解决一个工程现实——性能纪律在各子系统文档里是分散的、容易在「先跑通」的压力下被悄悄放弃；本表把它们集中成一道**交付前必须全绿的门禁**。

范围限定在**性能维度的横切确认**，覆盖十二个主题：SoA 数据布局落实、稳态零托管分配、多线程覆盖面、SIMD 落实点、bounds-check 消除验证、GC 策略实测定档、GPU 计算下放、dirty-rect 真生效、过载降级链、大世界内存上限、瓶颈按「延迟+分支」而非带宽分析、profiling 工具链。每一项都引用其对应子系统文档而**不重复其设计**——例如 checkerboard 调度的算法细节在 plan/03，本文只审计「它确实用了 plan/02 的持久线程池、确实在活跃 chunk 少时回退单线程、实测加速曲线确实落表」。

明确**不在本文范围**：功能正确性（属各子系统文档与 plan/14 的性质测试）、具体算法设计（属各子系统）、发行管线本身（属 plan/15，本文只审计其编译模式对 SIMD light-up 的影响）。本文也**不放松任何安全/正确性不变式**换性能：架构不变式 #9（CPU sim 权威，权威网格不下放 GPU）、#2（checkerboard 无锁，绝不 cell 级加锁）、#6（绝不追帧）在性能加固中均为硬约束，任何「为性能」的改动若触碰它们即为错误。

衡量「拉满」的总判据有三层，全部以**目标硬件实测**为准、不以估算为设计保证（架构 §12.7、§12.8、§17.3）：稳态帧循环零托管分配且无可感知 GC 停顿；CA sim ≤ 8ms、渲染+光照+后处理 ≤ 4ms、物理+形状重建 ≤ 3–4ms、逻辑+音频派发+余量 ≤ 1ms（架构 §1.4）；静止像素的边际成本经 dirty-rect 实测 ≈ 0（架构 §5.4，这是达标的根本，对应风险 R1）。

## 2. 技术栈与依赖

本文不引入新选型，沿用 plan/00 定稿。与性能加固直接相关的技术栈锚点如下，逐项作为审计依据而非重新决策：发行默认 **CoreCLR 自包含 + ReadyToRun（R2R composite）**，以同时拿到快启动、运行时 CPU 检测、Tier-1 重 JIT 与 Dynamic PGO，使 sim 热方法在运行时 light-up AVX2/AVX-512/AVX10.2（架构 §12.3、§13、风险 R3）；NativeAOT 仅作次级 per-RID 产物且必须显式 `IlcInstructionSet` 并反汇编验证，因其默认退化 SSE2 baseline 会静默砍 SIMD（架构 §2 挑战五）。

SIMD 用 `System.Numerics.Vector<T>` + `System.Runtime.Intrinsics`（Avx2/Avx512F/Avx10v2）+ 强制 scalar fallback，靠运行时 light-up，**不固定 ISA**；并发原语用 `PixelEngine.Core` 的持久线程池 + 帧屏障 barrier（plan/02），同一线程池亦服务 Box2D task 桥（架构 §14.2），**不用每帧 `Parallel.For`**（其分区/委托开销在 60fps 细粒度工作下浪费，架构 §5.7、§12.7）。内存用 `Span<T>`/`stackalloc`/`ArrayPool<T>`/对象池/`GC.AllocateArray(pinned:true)`（POH）/`NativeMemory`，跨界缓冲零拷贝双缓冲于 sim/physics/render（架构 §13、§14.3、AGENTS §3）。GPU 计算经 `PixelEngine.Rendering` 的 Silk.NET OpenGL（4.3+ compute，capability-gate）承载，仅光照/bloom/高密度粒子合成/可选非权威 sim pass（plan/09，架构 §9.5、不变式 #9）。

profiling 工具链：**BenchmarkDotNet**（含 `[DisassemblyDiagnoser]`）作 perf 门禁、`DOTNET_JitDisasm` 环境变量 + **Disasmo**/Rider 反汇编确认 `RNGCHKFAIL` 消失与 ymm/zmm 寄存器出现（架构 §12.6、§17.3、AGENTS §3、§7）。

依赖的子系统计划（本表逐项引用其性能契约，不复述其设计）：plan/02 Core（线程池/barrier/POH/NativeMemory/对象池/诊断计时）、plan/03 CA 内核（SoA/dirty-rect/checkerboard/parity）、plan/04 材质反应温度（紧凑反应表/温度 stencil）、plan/05 粒子（粒子池/积分）、plan/06 物理（CCL/形状重建/task 桥）、plan/07 世界与存档（内存上限 LRU/序列化字节准备）、plan/08 渲染（render buffer 构建/纹理流式）、plan/09 GPU 计算、plan/14 测试与基准（基准门禁/oracle）、plan/15 发行（R2R/AOT/ISA）。

## 3. 详细设计

本节按主题阐明每条性能纪律的**审计判据**——即「检查什么、用什么工具、什么结果算过」。设计本体在各子系统文档；这里只定义门禁。

**SoA 数据布局（架构 §7.1、§12.1，子系统 plan/03/04）。** 审计点是所有 sim 热数据为 struct-of-arrays：`Material`(ushort)、`Flags`(byte)、`Lifetime`(byte)、`Temperature`(1/4 分辨率 Half/float)、独立 `Render`(uint BGRA8) 各为独立连续数组，绝无把多字段打包进 AoS struct 进热循环。每 cell 字节预算须对账：sim 热态按 4B/cell 为约 7.9MiB（8.3MB，不含 render buffer）、加 render buffer 约 16.3MiB（17MB，1080p），每常驻 chunk ~18–20KB（架构 §12.2）。AoS 的 16 字节 `Cell` struct 仅允许出现在工具/编辑路径，不得进 CA pass。颜色不入 cell（不变式 #7）是 SoA 预算成立的前提，一并核对。

**稳态零托管分配（架构 §12.4、AGENTS §3，横切全子系统）。** 逐条热路径确认稳态帧内零托管堆分配：CA pass（相位 4）、粒子积分与沉积（相位 3/7）、render buffer 构建（相位 9）、反应 pass（相位 4 内）、温度 stencil（相位 5）、序列化字节准备（相位 11）。手段：`stackalloc` 小 scratch（<~1KB 且绝不在循环里）、`ArrayPool<T>.Shared`、对象/粒子池、POH/NativeMemory 长寿缓冲。热路径**禁** LINQ、捕获闭包、装箱、`params`、迭代器分配、字符串拼接（AGENTS §3）。判据：BenchmarkDotNet `MemoryDiagnoser` 报告稳态迭代 `Allocated == 0 B`；运行时 Gen0 计数在压测下长时间不增长。

**多线程覆盖面（架构 §5.7、§12.7、§14.2，子系统 plan/02/03/06/07/08，风险 R7/R14）。** 逐项确认下列工作并行化且走 plan/02 持久线程池（非每帧 `Parallel.For`）：CA 4-pass checkerboard（每遍 per-chunk task 派发，遍间 barrier，无锁——不变式 #2）；Box2D task 桥（`enqueueTask`/`finishTask` 把 Box2D 并行 for 派发到同一线程池，架构 §14.2，回调禁 `[SuppressGCTransition]`）；render buffer 构建（相位 9 按区块并行）；CCL 连通分量与形状重建（相位 8a 各刚体并行，off-thread）；粒子积分（`Span<Particle>` 分段并行）；温度 stencil（相位 5 行分块并行）；序列化字节准备（相位 11 后台线程，只碰离线字节缓冲不碰 live map）。统一回退纪律：活跃任务/活跃 chunk 低于阈值时回退单线程，避免 barrier 开销主导（架构 §5.7、§12.7，风险 R7）。per-thread 元数据填充到 64 字节 cache line 防 false sharing（架构 §12.7）。

**SIMD 落实点（架构 §12.5、§2 挑战三、风险 R11，子系统 plan/04/08）。** 仅在规则、分支统一的 pass 向量化：温度 5-point stencil 热扩散、palette→BGRA 转色与色混合、bulk fill/clear、dirty flag 扫描/popcount、（可选）流体压力松弛 Jacobi。起步 `Vector<T>`，最热 stencil 降 `System.Runtime.Intrinsics`（Avx2/Avx512F/Avx10v2）+ 必备 scalar fallback。**明确不向量化**：sand/liquid movement 内层是数据相关 gather/scatter（读邻居+条件 swap），无 SIMD 收益，强制留 scalar（架构 §2 挑战三、§12.5）——这是审计项而非遗漏。AVX-512/Vector512 须 gate on `Vector512.IsHardwareAccelerated` 并逐目标实测（部分 Skylake-X/Cascade/Cooper Lake 降频反变慢，架构 §12.5）。

**bounds-check 消除验证（架构 §12.6、§17.3、风险 R1，子系统 plan/03，工具 plan/14）。** 最内层邻居访问用 `ref T baseRef = MemoryMarshal.GetArrayDataReference(arr)` + `Unsafe.Add` 或 `fixed` 指针，沿 scanline 漫游 ref/指针避免重算 `y*width+x`；索引在 chunk 边界而非 per-cell 验证。判据（架构 §17.3）：`DOTNET_JitDisasm=<方法名>`/Disasmo/Rider 反汇编确认热方法内 `RNGCHKFAIL`（bounds-check 跳转）消失、SIMD 寄存器（ymm/zmm）在向量化 pass 出现；BenchmarkDotNet `[DisassemblyDiagnoser]` 作守门，反汇编结果纳入 plan/14 的回归基线。

**GC 策略实测定档（架构 §12.4、未决问题，子系统 plan/02/15）。** GC 模式**不预设**：M0 起用 BenchmarkDotNet 对比 Workstation+Concurrent vs Server GC，两者均配 `GCSettings.LatencyMode = SustainedLowLatency`，按实测最坏停顿定档（多核大 pinned 堆下 Server GC 常给更短最坏停顿）。零帧分配下两模式 Gen0 都极慢填满，故低风险但仍以数据定。跨界缓冲走 POH（`GC.AllocateArray(pinned:true)`）/`NativeMemory` 实现 sim/physics/render 零拷贝双缓冲；对象池/粒子池覆盖一切短命对象。关键段可 `GC.TryStartNoGCRegion`。

**GPU 计算下放（架构 §9.5、不变式 #9，子系统 plan/09）。** 下放 GPU：光照（emissive + fog-of-war + bloom + dither + gamma，可选 Radiance Cascades）、bloom 链、高密度粒子合成（可选 point-sprite 批绘）、**可选**非权威 sim pass（如空气/烟扩散，block/Margolus CA）。**权威网格留 CPU**（不变式 #9）：像素级碰撞需网格随时可读，不接受 GPU→CPU readback 卡流水线；任何 GPU sim pass 必须是非权威、可丢弃的。capability-gate compute（GL 4.3+），无 compute 时回退 CPU 路径或降级（与 §4.3 二级降级联动）。

**dirty-rect 真生效（架构 §5.4、风险 R1，子系统 plan/03）。** 这是全屏 60fps 的首要原因，必须验证而非假设：静止 chunk 的 working rect 收缩为空、chunk 进入 sleep、下帧零迭代；静止区边际成本经基准实测 ≈ 0（满屏静止场景帧时间应远低于满屏激活）。dirty rect 本身双缓冲（working/current），cell 单缓冲（不变式 #3）。KeepAlive 正确跨界传播但不导致 rect 收不回（架构 §5.5，风险 R2 的性能侧）。判据：BenchmarkDotNet 对比「满屏激活」vs「满屏静止」帧时间，后者应呈现近零 sim 成本；§17.2 的 dirty-rect 叠层目视确认 sleeping 区不再迭代。

**过载降级链（架构 §4.3、不变式 #6，子系统 plan/02 诊断 + 各子系统）。** 逐级实现并可勾选，由 Core 诊断计时器驱动（架构 §17.1）：第一级降低/关闭全分辨率热场（相位 5 改每 N 帧或仅接触式火传播）；第二级降低光照质量（关 Radiance Cascades/bloom，回退 fog-of-war+emissive）；第三级远离相机的活跃 chunk 降频（隔帧更新外圈）；第四级整体 sim 降 30Hz（相位 3–8 每两帧一次，render 仍每帧出帧，dt=1/30）；第五级兜底接受 <60fps 真实减速。**关键不变式 #6**：绝不追帧、无 accumulator 补步，最坏只低帧率不 death spiral。每帧成本上限类节流（形状重建每刚体每帧至多一次、CCL off-thread）须实施以防单帧尖刺带偏降级逻辑（架构 §4.3、§8.4、风险 R4）。

**大世界常驻内存上限（架构 §12.2，子系统 plan/07）。** 常驻 chunk 按 ~18–20KB 计，激活区 + border ring 设硬上限（建议 ≤512MB，可配置）与 LRU 驱逐目标（按到相机距离 + 闲置时长）；超上限即便在激活半径内也提前驱逐最远 sleeping chunk 落盘。render buffer 是屏幕大小非 per-chunk。磁盘增长靠 RLE+LZ4 压制（架构 §11.3、§12.2）。判据：长时间漫游压测下常驻内存稳定在上限内、不无界增长。

**瓶颈按延迟+分支而非带宽分析（架构 §12.7、§2 挑战三、风险 R1，工具 plan/14）。** 内核工作集 ~6MB 驻 L3、真实 DRAM 流量仅数 GB/s（远低于 ~50GB/s 带宽上限），瓶颈是内存延迟（散乱 L2/L3 访问）+ 数据相关 swap 的分支误预测，**不是带宽**。审计纪律：性能分析与优化围绕 cache-miss/分支误预测计数器（非带宽计），多核加速曲线在目标硬件实测（延迟约束代码常近线性缩放，仅 L3 争用时 sub-linear，故「5–6x sub-linear」估算无依据）；cells/frame 目标（量级 ~2–4M 全激活）必须 BenchmarkDotNet 在 6 RID 代表硬件确认、回填架构 §1.4/§12.8，**不当设计保证**（架构 §12.7、§12.8、§17.3，配合 plan/14）。

**profiling 工具链（架构 §17.3、§12.6、AGENTS §3/§7，子系统 plan/14）。** BenchmarkDotNet（`[MemoryDiagnoser]` 验零分配、`[DisassemblyDiagnoser]` 验 codegen）作 CI perf 门禁，回归即视为 bug；`DOTNET_JitDisasm` + Disasmo/Rider 反汇编人工核验；Core 常驻 debug overlay 报每相位耗时/活跃 chunk/活跃 cell/粒子数/刚体数/常驻内存/当前 sim 频率（架构 §17.1）作运行时眼睛。

## 4. 实现清单

> 这是性能加固的**审计表主体**。每项标注 [子系统/plan 文档 · 架构§]。完成并经判据验证后勾选。

### 4.1 SoA 数据布局
- [x] 确认 `Material`/`Flags`/`Lifetime`/`Temperature`/`Render` 均为独立连续数组（SoA），无 AoS struct 进 CA 热循环。[plan/03 · §7.1]
- [x] 对账每 cell 字节预算：sim 热态按 4B/cell 为约 7.9MiB（8.3MB，不含 render buffer），加 BGRA render buffer 约 15.8MiB（16.6MB），每常驻 chunk 估算 19,968B，已落在 18–20KB；新增 per-cell 字段必须重新评审。[plan/03/07 · §7.1/§12.2]
- [x] 确认颜色不入 cell（渲染色由材质纹理采样 + 温度 glow 在渲染相位生成）。[plan/08 · §7.1/不变式 #7]
- [x] 确认 AoS 16 字节 `Cell` 仅存在于工具/编辑路径，绝不进 sim pass。[plan/03/12 · §7.1]
- [x] `Temperature` 确为 1/4 分辨率（CELL=4）而非全分辨率每 cell。[plan/04 · §7.1/§7.5]

### 4.2 稳态零托管分配
- [x] CA pass（相位 4）稳态零分配，`MemoryDiagnoser` 报 `Allocated == 0 B`。[plan/03 · §12.4]
- [x] 粒子积分与沉积（相位 3/7）稳态零分配，粒子池 swap-remove 无 per-particle 分配。[plan/05 · §7.6/§12.4]
- [x] render buffer 构建（相位 9）稳态零分配。[plan/08 · §9.3/§12.4]
- [x] 反应 pass（相位 4 内）稳态零分配，tag 在加载期展开、运行时零字符串/字典。[plan/04 · §7.4]
- [x] 温度 stencil（相位 5）稳态零分配。[plan/04 · §7.5]
- [x] 序列化字节准备（相位 11）用 POH/ArrayPool 缓冲，稳态零分配。[plan/07 · §11.5]
- [x] 全热路径静态核查无 LINQ/捕获闭包/装箱/`params`/迭代器/字符串拼接（分析器提升为 error）。[全子系统 · AGENTS §3]

### 4.3 多线程覆盖面
- [x] CA 4-pass checkerboard 经 plan/02 持久线程池 per-chunk task 派发、遍间 barrier、无锁。[plan/03 · §5.7/不变式 #2]
- [x] Box2D task 桥把并行 for 派发到同一线程池，回调禁 `[SuppressGCTransition]`，分配稳定 workerIndex。[plan/06 · §14.2/风险 R14]
- [x] render buffer 构建并行（按区块），整数 zoom palette fast path 会复制重复屏幕行避免 2x/3x/4x 像素缩放下重复采样同一 world row；`BuildPaletteZoomFastPathCopiesRepeatedScreenRows` 已用计数型 `IChunkSource` 锁定 2x 默认相机只访问真实 world row。[plan/08 · §3.3 相位9]
- [x] CCL + 形状重建并行且 off-thread（相位 8a 各刚体）。[plan/06 · §8.4]
- [x] 粒子积分并行（`Span<Particle>` 分段）。[plan/05 · §7.6]
- [x] 温度 stencil 并行（行分块）。[plan/04 · §7.5]
- [x] 序列化字节准备在后台线程，只碰离线字节缓冲、不碰 live map。[plan/07 · §3.4/§11.5]
- [x] 全部并行工作走持久线程池，**无每帧 `Parallel.For`**。[plan/02 · §5.7/§12.7]
- [x] 活跃任务/活跃 chunk 低于阈值时回退单线程。[plan/02/03 · §5.7/风险 R7]
- [x] per-thread/per-chunk 元数据填充到 64 字节 cache line 防 false sharing。[plan/02 · §12.7]

### 4.4 SIMD 落实点
- [x] 温度 5-point stencil 向量化（Intrinsics + scalar fallback）。[plan/04 · §12.5]
- [x] palette→BGRA 转色与色混合向量化。[plan/08 · §12.5]
- [x] bulk fill/clear、dirty flag 扫描/popcount 向量化。[plan/03 · §12.5]
- [x] 全部向量化 pass 具备强制 scalar fallback，运行时 light-up、不固定 ISA。[全子系统 · §12.3/§12.5]
- [x] sand/liquid movement 内层**明确不向量化**（数据相关 gather/scatter），保留 scalar。[plan/03 · §2 挑战三/§12.5]
- [!] 阻塞：AVX-512 路径 gate on `Vector512.IsHardwareAccelerated` 并逐目标实测（防降频净变慢）。[plan/14 · §12.5] 当前本机 BenchmarkDotNet 诊断仅报告 AVX2（Ryzen 7 5800X），无 AVX-512/Vector512 硬件，不能实测 AVX-512 降频净损；需 AVX-512 目标机或 CI runner 证据后再判定。统一证据入口为 `tools/performance-target-evidence-preflight.ps1`，它要求 `avx512_downclock_net_loss` scope 与 SHA256 匹配，证据齐全也只进入 `target_performance_evidence_attached_pending_review`。

### 4.5 bounds-check 消除验证
- [x] 最内层邻居访问用 `MemoryMarshal.GetArrayDataReference` + `Unsafe.Add` 或 `fixed` 指针漫游。[plan/03 · §12.6]
- [x] 沿 scanline 漫游 ref/指针，避免每访问重算 `y*width+x`。[plan/03 · §12.6]
- [x] 反汇编确认热方法 `RNGCHKFAIL`（bounds-check）消失。[plan/14 · §12.6/§17.3]
- [x] 反汇编确认向量化 pass 出现 ymm/zmm 寄存器。[plan/14 · §12.6/§17.3]
- [x] BenchmarkDotNet `[DisassemblyDiagnoser]` 守门，反汇编基线纳入回归。[plan/14 · §17.3]

### 4.6 GC 策略
- [x] BenchmarkDotNet 实测对比 Workstation+Concurrent vs Server GC，按最坏停顿定档。[plan/02/14 · §12.4]
- [x] 两模式均配 `GCSettings.LatencyMode = SustainedLowLatency`，Hosting 通过 `EngineGcCoordinator` 串行化 GC latency mode 与 NoGCRegion 这两类进程级状态切换，避免并发 Engine 构建 / 临界帧互相踩踏。[plan/02 · §12.4]
- [x] 跨界缓冲走 POH/`NativeMemory` 零拷贝双缓冲（sim/physics/render）。[plan/02 · §13/§14.3]
- [x] 对象池/粒子池覆盖全部短命对象（particle/body/shape/scratch）。[plan/02/05/06 · §12.4]
- [x] 关键段按需 `GC.TryStartNoGCRegion`，成功进入后持有全局 GC 状态门直到 `EndNoGcRegion`，失败仍按原诊断路径记录并继续帧循环。[plan/02 · §12.4]
- [x] 压测下 Gen0 计数长时间不增长、无可感知 GC 停顿。[plan/14 · §1.4/§12.4]

### 4.7 GPU 计算下放
- [x] 光照（emissive/fog-of-war/bloom/dither/gamma）在 GPU。[plan/08/09 · §9.4]
- [x] bloom 链在 GPU。[plan/09 · §9.4]
- [x] 高密度粒子合成可走 GPU point-sprite 批绘（含 emissive pass）。[plan/09 · §9.3]
- [x] 可选非权威 sim pass（空气/烟扩散）下放 compute，**可丢弃、不回 readback**。[plan/09 · §9.5]
- [x] 权威网格留 CPU，无 GPU→CPU readback 卡流水线。[plan/03/09 · §9.5/不变式 #9]
- [x] compute 特性 capability-gate（GL 4.3+），缺失时 CPU 回退/降级。[plan/08/09 · §9.5]

### 4.8 dirty-rect 真生效
- [x] 静止 chunk working rect 收缩为空、进入 sleep、下帧零迭代。[plan/03 · §5.4]
- [x] 基准对比「满屏静止」vs「满屏激活」帧时间，前者 sim 成本 ≈ 0。[plan/14 · §5.4/风险 R1]
- [x] dirty rect 双缓冲（working/current）、cell 单缓冲。[plan/03 · §5.4/不变式 #3]
- [x] KeepAlive 跨界正确传播且不致 rect 收不回（无永久唤醒）。[plan/03 · §5.5/风险 R2]
- [x] §17.2 dirty-rect 叠层目视确认 sleeping 区不再迭代。[plan/12 · §17.2]

### 4.9 过载降级链（绝不追帧）
- [x] 一级：降低/关闭全分辨率热场（每 N 帧或仅接触式火传播）。[plan/04 · §4.3]
- [x] 二级：降低光照质量（关 RC/bloom，回退 fog-of-war+emissive）。[plan/08/09 · §4.3]
- [x] 三级：远离相机的活跃 chunk 降频（隔帧更新外圈）。[plan/03 · §4.3]
- [x] 四级：整体 sim 降 30Hz（相位 3–8 每两帧一次，render 每帧，dt=1/30）。[plan/03 · §4.2/§4.3]
- [x] 五级兜底：接受 <60fps 真实减速，**绝不 accumulator 追帧**。[plan/02 · §4.1/不变式 #6]
- [x] 降级由 Core 诊断计时器驱动（连续超预算帧触发）。[plan/02 · §4.3/§17.1]
- [x] 每帧成本上限节流落实（形状重建每刚体每帧≤1次、合并像素移除、CCL off-thread）。[plan/06 · §4.3/§8.4/风险 R4]

### 4.10 大世界内存上限
- [x] 常驻世界设硬上限（建议 ≤512MB，可配置）。[plan/07 · §12.2]
- [x] LRU 驱逐（到相机距离 + 闲置时长），超上限提前驱逐最远 sleeping chunk 落盘。[plan/07 · §12.2]
- [x] chunk payload RLE+LZ4 压制磁盘增长。[plan/07 · §11.3/§12.2]
- [x] 长时间漫游压测常驻内存稳定在上限内、不无界增长。[plan/14 · §12.2]

### 4.11 瓶颈按延迟+分支分析 + 目标硬件校准
- [!] 阻塞：性能分析围绕 cache-miss/分支误预测计数器，**不按带宽**结论。[plan/14 · §12.7/§2 挑战三] 已接入 `HardwareCounter.CacheMisses` 与 `HardwareCounter.BranchMispredictions`，并新增 `tools/hardware-counter-preflight.ps1` 生成 `blocked_non_admin`/计数器列检查报告；`PerformanceHardeningToolingDisciplineTests.HardwareCounterPreflightWritesHostBoundaryReport` 已锁定脚本在当前宿主只产出平台/权限边界报告、默认不运行 benchmark。当前非管理员会话仍被 BenchmarkDotNet 拦截，需要 elevated ETW Kernel Session 才能采集真实硬件计数器。目标性能总证据还必须经 `tools/performance-target-evidence-preflight.ps1` 校验 `hardware_counters_cache_branch` scope/hash，不能用本机短样本替代。
- [x] 多核加速曲线在目标硬件实测（不预设 sub-linear）。[plan/14 · §12.7]
- [!] 阻塞：cells/frame 目标在 6 RID 代表硬件用 BenchmarkDotNet 确认、回填架构 §1.4/§12.8。[plan/14/15 · §12.8/§17.3] 当前只有本机 win-x64 / Ryzen 7 5800X 短基准，缺少 win-arm64、linux-x64、linux-arm64、osx-x64、osx-arm64 代表硬件或 CI runner 实测。`tools/performance-target-evidence-preflight.ps1` 要求 `cells_frame/<rid>` 覆盖六个 RID，且 `cellsFrame.<rid>.benchmarkDotNet=true` 与 SHA256 同时匹配；`PerformanceHardeningToolingDisciplineTests.PerformanceTargetEvidencePreflightRejectsCellsFrameWithoutBenchmarkDotNet` 已锁定缺少 BenchmarkDotNet 语义确认的 RID 不能冒充 cells/frame 目标硬件实测。

### 4.12 profiling 工具链
- [x] BenchmarkDotNet 接入（`[MemoryDiagnoser]` + `[DisassemblyDiagnoser]`）作 CI perf 门禁。[plan/14 · §17.3]
- [x] `DOTNET_JitDisasm` + Disasmo/Rider 反汇编流程文档化、可复现。[plan/14 · §12.6/§17.3]
- [x] Core 常驻 debug overlay 报每相位耗时/活跃 chunk/cell/粒子/刚体/常驻内存/sim 频率。[plan/12 · §17.1]
- [x] 发行编译模式审计：默认 R2R（运行时 light-up）；AOT 次级必须显式 `IlcInstructionSet` 并反汇编验证 ymm/zmm。[plan/15 · §12.3/风险 R3]

## 5. 验收标准

> 全部勾选方算本文档完成（AGENTS §7）。验收以**实测/反汇编**为准，不以代码存在为准。

- [x] **SoA 全覆盖**：所有 sim 热数据 SoA，per-cell 字节预算按 4B/cell 对账通过，AoS 仅工具路径，颜色不入 cell。[§7.1/不变式 #7]
- [x] **稳态零分配**：CA/粒子/render buffer/反应/温度/序列化六条热路径 `MemoryDiagnoser` 全报 `0 B`；热路径静态核查无 LINQ/闭包/装箱/迭代器。[§12.4/AGENTS §3]
- [x] **多线程齐全**：CA checkerboard、Box2D task 桥、render buffer、CCL/形状重建、粒子积分、温度 stencil、序列化字节准备七项均经持久线程池并行；无每帧 `Parallel.For`；活跃任务少时单线程回退生效。[§5.7/§12.7/§14.2/风险 R7]
- [!] **SIMD 到位**：温度 stencil/色混合/bulk fill 等向量化且有 scalar fallback、运行时 light-up；sand movement 确认未向量化；AVX-512 gate 实测无降频净损。[§12.5/§2 挑战三] 阻塞于 §4.4 AVX-512 目标硬件实测；当前机器仅 AVX2，无法验证 Vector512 降频净损。`tools/performance-target-evidence-preflight.ps1` 只索引 `avx512_downclock_net_loss` 证据并进入 pending review，不自动勾选。
- [x] **bounds-check 消除**：热方法反汇编无 `RNGCHKFAIL`、向量化 pass 见 ymm/zmm；`[DisassemblyDiagnoser]` 守门基线建立。[§12.6/§17.3]
- [x] **GC 定档**：Workstation vs Server 实测定档完成；压测下 Gen0 不增长、无可感知停顿；跨界缓冲零拷贝、池化覆盖短命对象；Hosting GC 全局状态切换经 `EngineGcCoordinator` 串行化并由 `HostingGcStateChangesAreSerializedThroughCoordinator` 锁定。[§12.4]
- [x] **GPU 下放且权威留 CPU**：光照/bloom/高密度粒子/可选非权威 sim pass 在 GPU；权威网格在 CPU、无 readback 卡流水线；compute capability-gate 与回退生效。[§9.4/§9.5/不变式 #9]
- [x] **dirty-rect 真生效**：满屏静止场景 sim 成本实测 ≈ 0，叠层确认 sleeping 区零迭代；KeepAlive 无永久唤醒。[§5.4/风险 R1]
- [x] **过载降级链可逐级触发**：五级降级 + 节流全部实现并可由诊断计时器触发；压力下绝不进入 death spiral（不变式 #6）。[§4.2/§4.3]
- [x] **内存上限守住**：常驻世界稳定 ≤ 配置上限，LRU 驱逐 + RLE+LZ4 生效，长漫游不无界增长。[§12.2]
- [!] **延迟+分支校准**：瓶颈分析以 cache-miss/分支误预测为据；多核加速曲线与 cells/frame 在目标硬件实测落表、回填架构指标。[§12.7/§12.8/§17.3] 阻塞于 §4.11：`tools/hardware-counter-preflight.ps1` 已能显式报告非管理员 ETW 阻塞并在专用 runner 检查 `Cache Misses` / `Branch Mispredictions` 列，且脚本宿主边界由 `PerformanceHardeningToolingDisciplineTests.HardwareCounterPreflightWritesHostBoundaryReport` 覆盖；但当前会话仍无法采集真实硬件计数器，且缺少 6 RID 代表硬件 cells/frame 实测。统一 manifest 预检为 `tools/performance-target-evidence-preflight.ps1`，缺 manifest 为 `blocked_missing_target_performance_manifest`，schema/JSON 错误为 `blocked_invalid_target_performance_evidence`，缺 scope/hash 为 `blocked_missing_target_performance_scope_evidence`，证据齐全仅 `target_performance_evidence_attached_pending_review`。
- [x] **工具链门禁运行**：BenchmarkDotNet perf 门禁在 CI 跑、回归视为 bug；反汇编流程可复现；debug overlay 在线；发行编译模式审计通过。[§17.1/§17.3/§12.3]
- [!] **帧预算达标**：目标硬件实测 CA ≤8ms、渲染+光照+post ≤4ms、物理+重建 ≤3–4ms、逻辑+音频 ≤1ms（典型场景留余量）。[§1.4] 阻塞：当前已有 Short 报告显示 full-active CA 仍未达目标预算，且缺少目标硬件正式长跑。目标硬件正式长跑报告需通过 `tools/performance-target-evidence-preflight.ps1` 的 `frame_budget_target_hardware` scope/hash 校验后再人工复核。
- [x] **零冲突复核**：本表所有项与架构不变式（#2/#3/#6/#7/#9）及 plan/00 技术栈无冲突。[AGENTS §1] 复核证据：§4.3 保持 checkerboard + 持久线程池、无 cell 级锁；§4.5/§4.8 保持单缓冲 + dirty-rect + parity；§4.9 明确绝不 accumulator 追帧；§4.1/§4.7 确认颜色不入 cell、CPU sim 权威且 GPU pass 非权威无 readback；技术栈仍沿用 plan/00 的 .NET 10/C# 14/Intrinsics/Silk.NET/Box2D/BenchmarkDotNet。

## 6. 依赖关系

本文档是**横切审计**，逻辑上依赖被审计的全部子系统先成型（plan/02–plan/15），因此在 plan/README 的执行顺序中置于集成阶段末段（13 → 14 → 15 → 16）。但其**审计项必须自各子系统编码起就内建并随每个里程碑增量勾选**，而非交付前补做——性能纪律是「强制，非优化项」（AGENTS §3），事后加装代价极高。

具体上游依赖：plan/02 提供持久线程池/barrier/POH/NativeMemory/对象池/诊断计时（4.3/4.6/4.9/4.12 的基础设施）；plan/03 提供 SoA/dirty-rect/checkerboard/parity/指针漫游（4.1/4.3/4.5/4.8）；plan/04 提供紧凑反应表与温度 stencil（4.2/4.4 反应与温度项）；plan/05 提供粒子池与积分（4.2/4.3 粒子项）；plan/06 提供 CCL/形状重建/Box2D task 桥与节流（4.3/4.9）；plan/07 提供内存上限 LRU 与序列化字节准备（4.2/4.10）；plan/08+plan/09 提供 render buffer 构建/纹理流式/光照/bloom/GPU compute（4.2/4.7）；plan/14 提供 BenchmarkDotNet/oracle/反汇编门禁（4.5/4.11/4.12 的判据执行）；plan/15 提供 R2R/AOT/ISA 发行模式（4.12 编译模式审计）。

里程碑映射（架构 §18、plan/17）：4.1/4.5/4.8 自 M0–M1 起验证（垂直切片即建反汇编与静止区基准）；4.3/4.11 自 M2（多线程内核 + 每核加速曲线实测）；4.2/4.4 随 M3–M5（材质/反应/温度）；4.7 随 M5/M7（温度/光照）与 plan/09；4.9 随 M5–M7（降级目标逐级到位）；4.10 随 M9（流式/存档）；4.6/4.12 贯穿至 M10（GC 实测定档与全套基准落实）。本文档作为门禁在 M10 要求全绿。

下游：本文档无下游子系统依赖它产出代码；它产出的是**审计结论**，供 plan/17 路线图的提交节点核对与最终交付判定。

## 7. 提交节点

本文档不产出独立功能代码，其勾选项随对应子系统的实现提交分散落实；但有两个本文档专属的提交节点，按 AGENTS §6 用中文提交：

第一个提交节点——随 plan 骨架首提本审计清单文档本身：`docs(plan): 增加跨切面性能加固审计清单(plan/16)`，正文说明本文汇总各子系统性能纪律为可勾选门禁、引用架构 §12/§17.3 及风险 R1/R3/R4/R7。对应计划：plan/16 §实现清单 全表建立。

第二个提交节点——在 M10 集成调优阶段，当全表与 §5 验收标准逐项验证完成（实测/反汇编通过）后，提交审计结论：`perf(core): 性能加固审计全项通过(plan/16 验收)`，正文记录目标硬件实测的 cells/frame、每核加速曲线、GC 定档结论、关键热方法反汇编验证结果，并回填架构 §1.4/§12.8 的吞吐与帧预算实测值。对应计划：plan/16 §验收标准 全部勾选。

期间每当某子系统提交使其名下的审计项可勾选（如 plan/03 完成 bounds-check 消除并反汇编验证），在该子系统的提交中顺带勾选本表对应项，保持本审计表与实现同步（AGENTS §5），不攒到最后一次性勾选。
