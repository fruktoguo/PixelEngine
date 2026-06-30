# Plan 03 — Simulation Kernel（`PixelEngine.Simulation` CA 细胞自动机内核）

> 范围锚点：本文件只覆盖 **falling-sand 内核机制本身**——CellGrid SoA 数据结构、Chunk、单缓冲原地更新 + parity、4-pass checkerboard 调度、32px move cap、movement rules、KeepAlive 跨界唤醒、dirty-rect grow/shrink/swap/sleep，以及帧相位 3/4/6/7 的内核接口与「何时调用反应/温度/生命周期」的钩子。
> **明确不在本文件范围**：材质属性语义（`Density`/`Dispersion`/`Type` 等字段的定义与加载）在 `plan/04`；自由粒子池与积分在 `plan/05`；反应表数据与 `[tag]` 展开、温度场扩散在 `plan/04`（本文件只定义「何时调用反应执行」的钩子，不定义表）；chunk 驻留/流式装卸/border ring/内存上限在 `plan/07`（本文件定义 `Chunk` 与 `CellGrid` 的数据结构与**本地访问**，并通过 `IChunkSource` seam 消费 plan/07 提供的驻留集合）。
> 权威依据：`docs/PixelEngine-架构与需求设计.md`（下称「架构」），开发宪法 `AGENTS.md`，技术栈锚文档 `plan/00`。引用格式 `§x.y` 指架构章节，`[相位 N]` 指架构 §3.3 的帧相位。
> 状态约定：`- [ ]` 未开始 / `- [x]` 完成并自测通过 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。

---

## 1. 目标与范围

本文件定义 `PixelEngine.Simulation` 程序集的 **CA 内核**：一套以 64×64 chunk 为单位、单缓冲原地、parity 防重、4-pass checkerboard 无锁并行、per-chunk dirty rectangle 驱动「静止像素近零成本」的 falling-sand 模拟核心。它是引擎心脏，也是性能纪律最严、最易出 bug 的地方（架构 §5、§2 挑战一/二/三、§19 R1/R2）。

目标是一次到位实现完整能力（`AGENTS.md §2`）：

内核必须满足四大相互强化的基石（不变式 #1）——64×64 chunk + 单缓冲原地更新 + per-chunk dirty rectangle + 32px move cap——并以 4-pass checkerboard 实现 CA 多线程无锁调度（不变式 #2），用每帧翻转的 parity 时钟位防止 cell 一帧移动/反应两次（不变式 #3），保证一切跨界写入恒在 32px halo 内（不变式 #4）。内核主体留在 C#（架构 §14.1），热路径零托管分配（`AGENTS.md §3`），数据布局一律 SoA（架构 §7.1），靠 `Span<T>`/`ref` 指针漫游 + unsafe 消除 bounds-check（架构 §12.6）。

本文件交付的公开能力面向：(a) `plan/05` 粒子系统（消费相位 3/7 的 cell↔particle 入口）；(b) `plan/04` 材质/反应/温度（实现本文件定义的 `IReactionExecutor` 与 `MaterialPropsTable`，并在相位 5 读写温度场）；(c) `plan/06` 物理（在相位 8 读写网格，本文件保证相位顺序隔离）；(d) `plan/07` 世界（实现 `IChunkSource`，提供驻留 chunk 集合与 border ring 保证）；(e) `plan/08` 渲染（只读消费 `Material`/`Flags`/dirty rect）。

不做之列（防越界）：不定义材质字段语义、不加载 JSON、不展开 `[tag]`、不存储反应表、不实现温度扩散数学、不实现自由粒子积分、不实现 chunk 磁盘格式与驻留增删、不存储渲染颜色（不变式 #7，颜色由渲染相位采样）。

---

## 2. 技术栈与依赖

与 `plan/00` 技术栈定稿一致，不另立选型：

- 运行时/语言：.NET 10 LTS / C# 14；`PixelEngine.Simulation.csproj` 开 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`、`<Nullable>enable</Nullable>`、file-scoped namespace、Release `<Optimize>true</Optimize>`（继承 `Directory.Build.props`，plan/00 §6）。
- 内存：SoA 热数组用 `GC.AllocateArray<T>(len, pinned:true)`（POH）分配，保证不被 GC 移动、可安全长期持有 `ref`/指针漫游（plan/00 §4 跨界缓冲内存、架构 §12.6）。chunk 池化复用，稳态零分配。
- SIMD：内核运动步是数据相关 gather/scatter，**不向量化**（架构 §12.5、§14.1 明确「sand 更新 SIMD 无收益」）；仅 dirty-flag 扫描/批量 fill 等规则 pass 可选 `System.Runtime.Intrinsics`，但本文件主体走标量 + bounds-check 消除。
- 多线程：消费 `plan/02`（`PixelEngine.Core`）的 **持久线程池 `JobSystem` + 帧 barrier**（架构 §5.7、§12.7），**不用 `Parallel.For`**。CA 与 Box2D task 桥共用该池，靠相位顺序错开（架构 §14.2）。
- 常量：`ChunkSize=64`、`MoveCap=32`、`DirtyRectPadding`（默认 2，可调）、`SingleThreadChunkThreshold`（活跃 chunk 回退阈值，可调）等集中在 `PixelEngine.Core` 的 `EngineConstants`（plan/00 §7），编译期常量便于 JIT 优化。
- RNG：消费 Core 的每-chunk 可种子化 RNG（架构 §6.2、§3.1），用于 powder 左右偏置交替与 gas 抖动；确定性模式 seam 由 Core 提供（架构 §6.2），本内核只通过接口取随机数、不内嵌平台相关浮点。
- 诊断：向 Core 诊断/计时器注册分项耗时（pass A–D、dirty-swap、particle 入口），供编辑器 HUD 与过载降级（架构 §4.3、§17.1）。

依赖方向（plan/00 §5，绝不反向）：`Simulation → Core`。Simulation **不**依赖 Rendering/Physics/Content/World；对后四者的协作一律通过本文件定义的 **seam 接口**（`IChunkSource`、`IReactionExecutor`、`MaterialPropsTable`、`ILifetimeSink`）由上层注入实现，避免循环依赖。`CellType` 枚举定义在本程序集（基础 sim 概念），`plan/04` 的 `MaterialDef.Type` 引用它。

上游依赖（必须先完成）：`plan/01`（项目骨架/CPM）、`plan/02`（Core：`JobSystem`、barrier、POH 内存封装、RNG、`EngineConstants`、诊断）。下游消费者：`plan/04`、`plan/05`、`plan/06`、`plan/07`、`plan/08`、`plan/14`。

---

## 3. 详细设计

### 3.1 坐标系与寻址（`ChunkCoord.cs`、`CellAddressing`）

世界以 cell 整数坐标为权威，y 轴向下（屏幕坐标），CA「bottom-up」扫描指世界 y **递减**方向（plan/00 §7、架构 §5.6）。1 cell = 1 像素。

- `readonly struct ChunkCoord(int X, int Y)`：chunk 坐标，等值/哈希用于 plan/07 的 hash-map 键。
- 世界坐标 `(wx, wy)` → chunk 坐标：`cx = wx >> 6`、`cy = wy >> 6`（算术右移，负坐标正确向下取整，`ChunkSize=64` 为 2 的幂）。
- 世界坐标 → chunk 内本地索引：`local = (wy & 63) * 64 + (wx & 63)`，范围 `[0, 4095]`。
- 这些都是 `static` 内联辅助（`[MethodImpl(AggressiveInlining)]`），集中在 `CellAddressing`，便于 JIT 把位运算折叠进热循环。

### 3.2 CellGrid SoA 与 Chunk 数据结构（`CellGrid.cs`、`Chunk.cs`）

**SoA 布局**（架构 §7.1，不变式 #7「颜色不入 cell」）。每 `Chunk` 拥有三条与 cell 一一对应的本地数组，长度恒 `4096`，POH 分配：

- `ushort[] Material`（4096 × 2B = 8KB）：材质 id；运行时数值 id 仅作索引（架构 §11.2、不变式 #8）。`0` 约定为 `Empty`。
- `byte[] Flags`（4KB）：bit0=parity 时钟位、bit1=settled/sleep、bit2=burning、bit3=freefalling、**bit4=RigidOwned（刚体占用,语义由 `plan/06` 拥有）**、bit5–7 备用（架构 §7.1）。持久位/瞬时位区分由 `plan/07` 存档消费，本内核只读写运行时语义。**契约(plan/06)**：当 movement/reaction 将覆盖/消耗一个置了 `RigidOwned` 的 cell（即「CA 挖得动刚体」）时，内核须经 `IRigidDamageSink.OnOwnedCellDamaged(wx,wy)` 钩子上报,由 `plan/06` 入 `RigidDamageQueue` 触发刚体形状重建；钩子为空实现时退化为普通 CA 行为。
- `byte[] Lifetime`（4KB）：fire/gas 倒计时（架构 §7.1）。
- **注意**：`Temperature`（1/4 分辨率热场）与 `Render`（BGRA8）**不在本文件**——温度场属相位 5（plan/04），render buffer 属相位 9（plan/08）。本内核既不分配也不存储它们（不变式 #7）。

每 chunk sim 态 ≈ 16KB（架构 §12.2），加元数据 ~18–20KB；与 plan/07 内存上限预算一致。

`CellGrid`（`CellGrid.cs`）是**按世界坐标索引**的访问门面，背后路由到 `IChunkSource` 解析出的 `Chunk`：

- 冷路径/编辑/测试 API：`ushort GetMaterial(int wx, int wy)`、`void SetMaterial(int wx,int wy,ushort id)`、`ref byte FlagsAt(...)` 等，做一次 chunk 解析 + 本地索引；用于相位 3/7 入口、编辑器、测试 oracle。
- 热路径访问**不走** `CellGrid`，而走 §3.3 的 `NeighborWindow`（消除 per-cell chunk 解析与 bounds-check）。
- `CellGrid` 持有对 `MaterialPropsTable` 的只读引用，供查 `Density`/`Dispersion`/`Type`。

`Chunk`（`Chunk.cs`）字段：

- `ChunkCoord Coord`；`ushort[] Material; byte[] Flags; byte[] Lifetime;`（POH）。
- dirty rectangle **双缓冲**（架构 §5.4）：`DirtyRect _current`（本帧迭代范围）、`DirtyRect _working`（本帧累积下帧范围）。
- KeepAlive 入站槽：`DirtyRect[] _incoming`（长度 8，每方向一个，见 §3.7）。
- `ChunkState State`（`Awake`/`Sleeping`）；`byte Parity`（最近一次被处理的帧 parity，调试用）。
- 缓存的 `ref`/指针基址封装（`GetMaterialBase()` 等用 `MemoryMarshal.GetArrayDataReference`）。

`readonly struct DirtyRect`（值语义，本地坐标 0..63，闭区间，`Min>Max` 表示空）：`MinX,MinY,MaxX,MaxY`；方法 `bool IsEmpty`、`DirtyRect Union(int lx,int ly,int padding)`（钳制到 `[0,63]`）、`DirtyRect Union(DirtyRect)`、`static DirtyRect Empty`、`static DirtyRect Full`。`DirtyRect` 不可变，grow 返回新值写回字段（无堆分配，纯值拷贝）。

### 3.3 NeighborWindow：3×3 chunk 本地漫游（`NeighborWindow.cs`）

热路径要求「unsafe/Span/ref 漫游消除 bounds-check」（架构 §12.6）。每个 per-chunk task 在更新前构造一个 `ref struct NeighborWindow`，缓存中心 chunk 及其 8 邻居的数组基址：

- 字段：`ref ushort _matBase0..8`、`ref byte _flagsBase0..8`、`ref byte _lifeBase0..8`（9 槽，slot = `(dcy+1)*3 + (dcx+1)`，`dcx,dcy ∈ {-1,0,1}`），中心 chunk 世界基 `_baseCx,_baseCy`。8 邻居由 border ring 保证驻留（架构 §3.4），构造期从 `IChunkSource` 解析一次。
- 世界坐标访问：`GetMaterial(int wx,int wy)` → `dcx=(wx>>6)-_baseCx`、`dcy=(wy>>6)-_baseCy`（均 ∈ {-1,0,1}，因 move cap≤32 保证目标必在 3×3 窗口内）；`slot=(dcy+1)*3+(dcx+1)`；`local=(wy&63)*64+(wx&63)`；`Unsafe.Add(ref SelectBase(slot), local)`。slot 0..8、local 0..4095 均为编译期可证范围，配合 `Unsafe.Add` 消除 `RNGCHKFAIL`（用 Disasmo/`DOTNET_JitDisasm` 验证，架构 §12.6、§17.3）。
- `Swap(int wx1,int wy1,int wx2,int wy2)`：交换两 cell 的 `Material`+相关 `Flags`/`Lifetime`，是 movement 的原子操作。
- 写入时返回/记录目标所属 slot，供 KeepAlive 判定（§3.7）：若目标 slot≠中心（4），说明跨界写入，触发对应方向 KeepAlive。
- `NeighborWindow` 为 `ref struct`，栈上构造、不逃逸、零分配；一个 worker 处理一个 chunk 期间持有一个窗口。

### 3.4 单缓冲原地更新 + parity 时钟位（不变式 #3，架构 §5.2/§5.3）

**单缓冲原地**：直接改 `Chunk` 的 SoA 数组，不做整世界 double buffer（double buffer 会每帧改写全世界、毁掉 dirty-rect，架构 §5.2）。dirty rect 自身是双缓冲（working/current，§3.2），cell 数据是单缓冲。

**parity 时钟位**（`CellFlags.Parity = 1<<0`）：

- `SimulationKernel._currentParity`（`byte`，0/1）每个被执行的 sim tick 在 `StepCa` 开头翻转其「本帧值」。**不清扫** 200 万 cell（架构 §5.3）。
- 扫描时：若 `(flags & Parity) == _currentParityBit` 则该 cell 本帧已处理，**跳过**。
- 处理（移动/反应）某 cell 时，把其（及移动目标、反应双输入双输出）的 parity 位**写为本帧值**，强制「每 cell 每帧至多一次移动/反应」。
- 移动后 cell 落在已扫描区（bottom-up 下 powder 落向更大 y，已扫过）或同行前方（液体水平铺开，靠 parity 兜底），parity 是顺序相关性的最终保险。
- **跨 chunk 反应防重复**（架构 §5.3/§7.4）：反应给两输入两输出都打当前 parity；相邻 chunk 在后续 pass（barrier 保证可见性）扫到同一边界对时，因 parity 已等于本帧值而跳过——保证「谁先扫到谁执行、另一侧跳过」，绝不双侧各执行致物质翻倍。

### 3.5 4-pass checkerboard 调度（不变式 #2，架构 §5.7，`CheckerboardScheduler.cs`）

把 chunk 当 2×2 super-grid，按 `(cx&1, cy&1)` 分 4 个 parity 桶 `{(0,0),(1,0),(0,1),(1,1)}`，每帧 4 遍、遍间 barrier：

- 每帧 `StepCa`：从 `IChunkSource` 取驻留 chunk，过滤出 `Awake` 者，按 `(cx&1,cy&1)` 装入 4 个**预分配复用**的桶（`Chunk[][] _buckets` + `int[] _counts`，清零而非重建，稳态零分配）。
- pass A→D 顺序处理 4 桶；**每遍内**经 `JobSystem` 把「每 chunk 一个 task」派发到持久线程池并行执行 `ChunkUpdater.UpdateChunk`；遍末 `JobSystem.Barrier()`（架构 §5.7、§12.7）。
- **无锁论证**：同遍内任意两个被更新 chunk 相距整 64px；每 chunk 写区域 = 自身 64×64 + 各方向 ≤32px halo；64−32>0 ⇒ 同遍两线程写区域**永不重叠**（含边界 cell 与跨界反应写入，架构 §5.7/§5.8/§7.4）。**绝不在 cell 级加锁**。
- **单线程回退**：`awakeCount < EngineConstants.SingleThreadChunkThreshold` 时，不派发 task、不设 barrier，在调用线程按同样的 4-pass parity 顺序串行处理全部 4 桶（保持与多线程一致的跨界写入行为，便于 oracle 比对，架构 §5.7、§16.2）。
- 有效并行度 ≈ awakeCount/4；活跃 chunk 极少时 barrier 开销主导，故回退（架构 §19 R7）。
- 每遍向 Core 诊断登记耗时（pass A–D 分项，架构 §17.1）。

### 3.6 movement rules 与扫描顺序（架构 §5.6/§7.2，`CellMovement.cs`）

每 chunk task 内 `ChunkUpdater.UpdateChunk` 对 `_current` dirty rect 做 **bottom-up 扫描**（y 从 `MaxY` 递减到 `MinY`，使一柱沙在一遍内自然坍塌，架构 §5.6），每行内按 x 遍历。对每个非 Empty、parity 未达本帧值的 cell，按 `MaterialPropsTable.Type[material]` 选规则集（架构 §7.2 六值枚举 `CellType { Empty, Solid, Powder, Liquid, Gas, Fire }`）：

- **Powder**（`PowderMover`）：候选偏移有序 `下(0,+1)`→`左下(-1,+1)`→`右下(+1,+1)`，与第一个「目标更轻/空」（`Density[target] < Density[self]`，`Empty` 视密度 0）的 cell `Swap`。左下/右下的**先后顺序逐帧交替偏置**（按 `_currentParity` 或 per-row 计数翻转）消除左/右倾。平地**不**水平铺开 ⇒ 休止角（angle of repose）自然涌现（架构 §5.6/§1.2）。
- **Liquid**（`LiquidMover`）：先跑 powder 规则（下/左下/右下，密度位移实现油浮水沉，架构 §7.3）；若不能下落则追加**水平铺开/dispersion**：沿当前帧偏置方向连续探测最多 `min(Dispersion[material], MoveCap)` 格，移动到最远可达空/更轻位置（density 分层）。**没有全局压力场**，找平由水平流动 emergent（架构 §5.6、§19 R11）。
- **Gas**（`GasMover`）：liquid 的上下翻转——候选 `上(0,-1)`→`上对角`→`侧向铺开`，带随机抖动（Core RNG）使其扩散而非干净柱状（架构 §5.6）。
- **Fire/Energy**：不参与 swap 移动，由 lifetime 递减 + 反应传播（架构 §5.6、§7.4）；其「上升」观感可由数据驱动反应/粒子（plan/04/05）实现，本内核仅做 lifetime + reaction 钩子。
- **Empty/Solid/Static**：不移动（`LiquidStatic` 等语义字段在 plan/04，本内核只读 `Type`）。

movement 消费的 `Density`/`Dispersion`/`Type` **字段定义在 plan/04**，本文件只通过 `MaterialPropsTable`（§3.9）只读消费。任何成功移动/swap 都：写双方 parity 为本帧值、grow 双方所在 chunk 的 `_working` rect（§3.7）、按需触发 KeepAlive（§3.7）。

### 3.7 32px move cap 与 KeepAlive 跨界唤醒（不变式 #4/#5，架构 §5.5/§5.8）

**32px move cap**（`EngineConstants.MoveCap=32`，架构 §5.8）：单次更新中任一像素净位移 ≤ 32px。powder 每帧 1 格天然满足；liquid dispersion 显式 `min(Dispersion, MoveCap)` 钳制；任何 mover 的目标探测都不超出中心 chunk 各方向 32px halo。**这是 checkerboard 无锁成立的承重墙**：halo(32)<chunk 间隔(64)，故同遍两线程写区域不重叠（§3.5）。反应只触 von Neumann 邻居（距离 1），远在 halo 内，跨界反应写入同样安全（不变式 #4、架构 §5.8/§7.4）。

**KeepAlive 跨界唤醒**（架构 §5.5，**全内核最易出 bug 处**，专项测试见 §3.11 与 plan/14）：dirty rect 被钳在 chunk 边界内，故在 chunk 边缘移动的 cell、或把（移动/反应）输出写入邻居 chunk 的操作，必须唤醒邻居，否则雪崩在缝隙处死掉。

无竞争设计——**每邻居一个专属入站槽**（`Chunk._incoming[8]`）：

- 当中心 chunk 的更新触及/写入方向 `d` 的邻居时，把被触碰的边/角 cell 折算为**邻居本地坐标**，`Union` 进**邻居** chunk 的 `_incoming[opposite(d)]` 槽（即中心写邻居「朝向中心」那一侧的槽）。
- **无锁正确性**：每个 chunk 的某个 `_incoming[k]` 槽，其唯一可能的写者是 `k` 方向上那一个确定的邻居 chunk；不同邻居写不同槽（不同数组元素，必要时 cache-line 填充防 false sharing）；且每个邻居在其唯一所属 pass 内由单线程顺序写——故任一槽在任一时刻至多一个 writer，普通（非原子）`Union` 即安全。这避免了对邻居 `_working` 的并发竞争（多个同遍对角/正交邻居可能同时唤醒同一目标，若直接写其 `_working` 会 race）。
- 合并：在相位 6 swap（§3.8）单线程阶段，每 chunk 把 8 个 `_incoming` 槽并入 `_current` 并清空槽；非空则保持/转入 `Awake`。
- border ring（架构 §3.4，plan/07 实现）保证 KeepAlive 目标 chunk 必驻留——本内核**假定** `IChunkSource` 已提供 3×3 邻居驻留，若解析失败视为 plan/07 契约破坏（断言 + 诊断）。
- 跨界 **数据写入**（cell 真的移动进邻居）：经 `NeighborWindow` 直接写邻居数组（合法，因 32px halo 内、同遍不重叠），并同时对该邻居触发 KeepAlive 与 `_working` grow（邻居在别的 pass，写其 `_incoming` 槽安全）。

### 3.8 dirty-rect grow/shrink、swap 与 sleep（架构 §5.4，`Chunk` + 相位 6）

- **grow**：任何 `set/move/react` 触及 cell `(lx,ly)` ⇒ `chunk._working = chunk._working.Union(lx, ly, EngineConstants.DirtyRectPadding)`（padding 默认 2，可调；使被改动 cell 的邻居下帧被重检，架构 §5.4）。
- **shrink**：`_working` 每帧从零累积本帧实际活动；不累计即自然收缩。
- **swap（相位 6，`SwapDirtyRects`，单线程帧边界）**：对每驻留 chunk：`_current = _working.Union(merge(_incoming))`；`_working = DirtyRect.Empty`；清空 `_incoming`；若 `_current.IsEmpty` ⇒ `State = Sleeping`（chunk 进入零成本休眠），否则 `Awake`。**dirty rect 双缓冲、cell 单缓冲**（架构 §5.4）。
- sleeping chunk 下帧不入桶、不迭代，成本≈0（架构 §5.4「全屏 60fps 首要原因」）。被 KeepAlive 或相位 3 沉积唤醒后重新进入调度。

### 3.9 反应/温度/生命周期钩子（架构 §7.4/§7.5，seam，不定义表）

本内核只定义**何时调用**，不含反应表/温度数学（在 plan/04）：

- `MaterialPropsTable`（`MaterialPropsTable.cs`）：内核消费的**热 SoA 只读视图**，由 plan/04（Content）在加载期构建并注入。字段（按材质 id 索引）：`CellType[] Type`、`byte[] Density`、`byte[] Dispersion`、`int[] ReactionStart`、`byte[] ReactionCount`、`ushort[] DefaultLifetime`。**字段语义全在 plan/04 定义**，本内核只读其值驱动 movement 与反应钩子；SoA 而非传完整 `MaterialDef` 是为 cache 友好（避免把未用字段拉进 cache line，架构 §7.1）。
- `IReactionExecutor`（`IReactionExecutor.cs`，seam）：`bool TryReact(ref NeighborWindow w, int wx1,int wy1,ushort matA, int wx2,int wy2,ushort matB, byte parityBit)`。movement 处理完一个 cell 后，若 `ReactionCount[material] > 0`，内核对其 4 个 von Neumann 邻居成对调用 `TryReact`；执行体（plan/04）写输出并对两输入两输出打 parity（§3.4 防重复），写入恒在 halo 内（§3.7）。惰性材质 `ReactionCount==0` 一次比较早退、不触反应数据（架构 §7.4）。每 cell 每帧至多一次反应（parity 保证）。
- **lifetime 递减**：内核对 `Lifetime[local] > 0` 的 cell 每被执行 tick 递减 1；归零时调用 `ILifetimeSink.OnExpired(ref NeighborWindow, wx, wy, material)`（seam，plan/04 决定转何材质/生成何粒子）。本内核不内嵌相变/转材质逻辑。
- **温度相变（melt/freeze/boil）属相位 5**（plan/04），本内核**不**调用；只保证相位 4 与相位 5 的相位顺序隔离（架构 §3.3）。

### 3.10 帧相位集成接口（架构 §3.3，`SimulationKernel.cs`）

`SimulationKernel` 暴露各相位入口，由 `plan/13`/Hosting 的主循环按序调用；本文件只实现内核侧接口：

- **[相位 3] particle→cell 沉积入口**：`void DepositCell(int wx,int wy,ushort material,byte persistentFlags)`、`void MarkDirty(int wx,int wy)`。供 plan/05 把落定粒子写回网格并 grow 所在 chunk dirty rect + 唤醒（边界则 KeepAlive）。放在 CA 之前，使新沉积 cell 本帧即被 CA 看见（架构 §3.3）。
- **[相位 4] CA pass A–D**：`void StepCa(JobSystem jobs)`（翻转 parity → 装桶 → 4-pass checkerboard / 单线程回退，§3.5）。
- **[相位 6] dirty-rect swap**：`void SwapDirtyRects()`（§3.8，单线程帧边界）。
- **[相位 7] cell→particle 抛射入口**：`ushort ReadAndClearCell(int wx,int wy,out byte flags,out byte lifetime)`（读材质 → 写 `Empty` → grow dirty + 唤醒）。供 plan/05 取材质设速生成粒子。放在 CA 之后，避免刚抛粒子本帧被 CA 误处理（架构 §3.3）。
- 这些接口是内核与 particle 系统（plan/05）、物理（plan/06，相位 8 在 swap 后读写网格）的契约边界；相位 5（温度）/相位 8（物理）由各自 plan 在相位顺序内插入，本内核不直接调用，仅保证不与其在同相位竞争（架构 §3.3 关键约束）。

### 3.11 测试钩子（专项，引用 plan/14）

KeepAlive/parity/跨界反应是最易错区（架构 §19 R2），本内核须导出**可观测测试钩子**（`internal` + `InternalsVisibleTo` 给 `PixelEngine.Simulation.Tests`）：

- `CountNonEmptyCells()`（全网格质量守恒计数）、`SnapshotChunk(ChunkCoord)`（导出 SoA 快照供逐 cell 比对）。
- KeepAlive 计数器与「被唤醒边界 chunk 列表」导出（§17.2 叠层 + plan/14 边界传播测试）。
- 强制单线程 + 固定 RNG/parity 的确定性模式开关（架构 §6.2），供 oracle 比对多线程统计性质（架构 §16.2）。
- 反应守恒探针：记录每帧反应执行次数与边界反应对，供「双输出/定向反应在边界不翻倍/不丢失」测试（架构 §7.4、§16.2）。具体测试用例在 plan/14。

---

## 4. 实现清单

### 4.1 寻址与数据结构
- [x] `ChunkCoord.cs`：`readonly struct ChunkCoord(int X,int Y)` + `IEquatable`/`GetHashCode`（供 plan/07 hash-map 键）。（§3.1）
- [x] `CellAddressing`：`static` 内联 `WorldToChunk`、`LocalIndex(wx,wy)`、`ChunkOf(wx)`，全用 `>>6`/`&63`，`[MethodImpl(AggressiveInlining)]`。（§3.1）
- [x] `CellType.cs`：`enum CellType : byte { Empty, Solid, Powder, Liquid, Gas, Fire }`（架构 §7.2，plan/04 `MaterialDef.Type` 引用）。（§3.6）
- [x] `CellFlags.cs`：常量 `Parity=1<<0,Settled=1<<1,Burning=1<<2,FreeFalling=1<<3,RigidOwned=1<<4`（bit5–7 备用）；内联辅助 `HasParity/SetParity/MatchesFrame`。（§3.4）
- [x] `IRigidDamageSink` 钩子；`CellGrid` 写入覆盖 `RigidOwned` cell 时调用。movement/reaction 覆盖调用随对应节点接入（plan/06 契约,空实现退化为普通 CA）。（§3.4）
- [x] `DirtyRect`：`readonly struct`，闭区间本地坐标，`IsEmpty/Union(lx,ly,pad)/Union(DirtyRect)/Empty/Full`，钳制 `[0,63]`，零分配值语义。（§3.2）
- [x] `Chunk.cs`：`Material/Flags/Lifetime` POH 数组（`GC.AllocateArray(pinned:true)`），`Coord`、`_current/_working`、`_incoming[8]`、`ChunkState`、基址封装（`MemoryMarshal.GetArrayDataReference`）。（§3.2）
- [x] `Chunk` 池化复用接口（`Reset(ChunkCoord)`/归还），稳态零分配；供 plan/07 驻留装卸复用。（§3.2）
- [x] `CellGrid.cs`：世界坐标门面 `GetMaterial/SetMaterial/FlagsAt/...`，路由 `IChunkSource`；持 `MaterialPropsTable` 只读引用。（§3.2）
- [x] `IChunkSource.cs`（seam）：`bool TryGetChunk(ChunkCoord,out Chunk)`、`ReadOnlySpan<Chunk> ResidentChunks`、`ResolveNeighborhood(..., out ChunkNeighborhood)`（3×3 驻留，由 plan/07 实现，border ring 保证）。（§3.2/§3.7）

### 4.2 NeighborWindow 漫游
- [x] `NeighborWindow.cs`：`ref struct`，9 槽 `ref` 基址（Material/Flags/Lifetime），中心世界基 `_baseCx/_baseCy`。（§3.3）
- [x] `NeighborWindow` 构造：从 `IChunkSource.ResolveNeighborhood` 一次性取 3×3 基址，零分配。（§3.3）
- [x] `GetMaterial/SetMaterial/GetFlags/SetFlags/GetLifetime` 世界坐标访问，用 `Unsafe.Add(ref SelectBase(slot),local)` 消除 bounds-check。（§3.3）
- [x] `Swap(wx1,wy1,wx2,wy2)`：交换 Material+Flags(保留瞬时位策略)+Lifetime，返回是否跨界（供 KeepAlive）。（§3.3）
- [x] Disasmo/`DOTNET_JitDisasm` 验证热访问 `RNGCHKFAIL` 消失（架构 §12.6/§17.3）。（§3.3）

### 4.3 单缓冲 + parity
- [x] `SimulationKernel._currentParity` + 每 `StepCa` 翻转本帧 parity 值（不清扫，架构 §5.3）。（§3.4）
- [x] 扫描跳过 `(flags&Parity)==currentBit` 的 cell（本帧已处理）。（§3.4）
- [x] 移动时把源/目标 cell parity 写为本帧值。（§3.4）
- [ ] 反应时把双输入/双输出 parity 写为本帧值。（§3.4/§3.9）
- [ ] 跨 chunk 边界对：反应执行打 parity，后续 pass 因 parity 跳过（防双侧翻倍，架构 §7.4）。（§3.4）

### 4.4 4-pass checkerboard 调度
- [x] `CheckerboardScheduler.cs`：4 个预分配复用桶 `Chunk[][]+int[] counts`，按 `(cx&1,cy&1)` 装 awake chunk，清零复用（零分配）。（§3.5）
- [x] pass A→D：每遍经 `JobSystem` per-chunk task 并行 `ChunkUpdater.UpdateChunk`，遍末 `JobSystem.Barrier()`。（§3.5）
- [x] 无锁不依赖任何 cell 级锁（代码审查 + 注释引用 §5.7/§5.8）。（§3.5）
- [x] 单线程回退：`awakeCount<SingleThreadChunkThreshold` 时同序串行 4-pass、无 barrier。（§3.5）
- [x] 每 pass 向 Core 诊断登记分项耗时（pass A–D，架构 §17.1）。（§3.5）

### 4.5 movement rules
- [x] `ChunkUpdater.UpdateChunk`：bottom-up（y 自 `MaxY` 递减）扫描 `_current`，按 `Type` 分派 mover；跳过 Empty/parity 已达本帧。（§3.6）
- [x] `PowderMover`：候选 下/左下/右下，密度位移 `Density[t]<Density[s]`，左右偏置逐帧交替；平地不铺开（休止角）。（§3.6）
- [x] `LiquidMover`：先 powder 规则；再水平铺开 `min(Dispersion,MoveCap)`，密度分层，无全局压力场。（§3.6）
- [x] `GasMover`：上/上对角/侧向铺开 + Core RNG 抖动（上升扩散）。（§3.6）
- [x] Fire/Energy：不 swap；Empty/Solid 不动。（§3.6）
- [ ] Fire/Energy lifetime + 反应钩子。（§3.6/§3.9）
- [x] 成功移动后：写双方 parity、grow 双方 chunk `_working`。（§3.6/§3.8）
- [ ] 成功跨界移动后按需 KeepAlive。（§3.6/§3.7）
- [x] movement 只读 `MaterialPropsTable` 的 `Density/Dispersion/Type`（不在本程序集定义其语义）。（§3.6）

### 4.6 32px move cap 与 KeepAlive
- [x] 所有 mover 目标探测钳制在 `MoveCap=32`（halo）内；liquid dispersion 显式 `min(Dispersion,MoveCap)`。（§3.7）
- [x] 跨界写入经 `NeighborWindow` 写邻居数组（halo 内合法），经 incoming KeepAlive 唤醒邻居，避免并行写邻居 `_working` 竞争。（§3.7）
- [x] `Chunk._incoming[8]` 入站槽 + `opposite(dir)` 折算邻居本地坐标 `Union`（每槽单写者，无锁）。（§3.7）
- [x] cache-line 填充 `_incoming`/per-chunk 调度元数据防 false sharing（架构 §12.7）。（§3.7）
- [x] movement 跨界写恒在 32px halo 内（断言 + 注释引用不变式 #4）。（§3.7）
- [ ] 反应跨界写恒在 von Neumann halo 内（断言 + 注释引用不变式 #4）。（§3.7/§3.9）
- [x] `IChunkSource` 解析 3×3 邻居失败时断言 + 诊断（border ring 契约，架构 §3.4）。（§3.7）

### 4.7 dirty-rect 生命周期
- [x] grow：`set/move/react` 触及即 `Union(lx,ly,DirtyRectPadding)`（padding 可调）。（§3.8）
- [x] `SwapDirtyRects`（相位 6，单线程）：`_current=_working∪merge(_incoming)`；`_working=Empty`；清 `_incoming`；空则 `Sleeping`。（§3.8）
- [x] sleeping chunk 不入桶、不迭代（零成本）；唤醒后重入调度。（§3.8）

### 4.8 钩子（不定义表）
- [x] `MaterialPropsTable.cs`：SoA 只读视图（`Type/Density/Dispersion/ReactionStart/ReactionCount/DefaultLifetime`），由 plan/04 注入。（§3.9）
- [ ] `IReactionExecutor.cs`（seam）：`TryReact(...)`；movement 后对 4 von Neumann 邻居对调用（`ReactionCount>0` 才调，惰性早退）。（§3.9）
- [ ] lifetime：`Lifetime>0` 每 tick 递减；归零调 `ILifetimeSink.OnExpired`（seam）。（§3.9）
- [ ] 不内嵌相变/温度数学（相位 5 在 plan/04），仅保证相位隔离。（§3.9）

### 4.9 帧相位接口
- [ ] `SimulationKernel.DepositCell/MarkDirty`（相位 3 入口，供 plan/05）。（§3.10）
- [ ] `SimulationKernel.StepCa(JobSystem)`（相位 4）。（§3.10）
- [ ] `SimulationKernel.SwapDirtyRects()`（相位 6）。（§3.10）
- [ ] `SimulationKernel.ReadAndClearCell(...)`（相位 7 入口，供 plan/05）。（§3.10）
- [ ] 公开 API 全带中文 XML 文档注释（脚本 IntelliSense 依赖，`AGENTS.md §4`、plan/00 §7）。（§3.10）

### 4.10 测试钩子与诊断
- [ ] `InternalsVisibleTo("PixelEngine.Simulation.Tests")`；`CountNonEmptyCells/SnapshotChunk`。（§3.11）
- [ ] KeepAlive 计数器 + 被唤醒边界 chunk 列表导出（§17.2 叠层 + plan/14）。（§3.11）
- [ ] 确定性模式开关（强制单线程 + 固定 RNG/parity 序，架构 §6.2），供 oracle 比对。（§3.11）
- [ ] 反应守恒探针（每帧反应次数 + 边界反应对记录）。（§3.11）
- [ ] 稳态帧零托管分配（`BenchmarkDotNet` `[MemoryDiagnoser]` 验证，`AGENTS.md §3`）。（§2/§3.5）

---

## 5. 验收标准

- [ ] **质量守恒（无反应）**：纯 movement 下全网格非空 cell 总数恒定，**含跨 chunk 边界**（捕获缝隙吞/复制像素，架构 §16.2，测试在 plan/14）。
- [ ] **质量守恒（跨界移动）**：一柱沙/一片水跨越 chunk 边界下落/铺开，无消失、无复制、无抖动（不变式 #4/#5、架构 §19 R2）。
- [ ] **parity 防重复**：单帧内任一 cell 至多移动一次、至多反应一次（构造「会被二次扫到」的场景验证 parity 跳过）。
- [ ] **反应守恒（边界）**：双输出/定向反应在 chunk 边界对上**不翻倍、不丢失**（谁先扫到谁执行、另一侧 parity 跳过，架构 §7.4/§16.2）。
- [ ] **KeepAlive 跨界唤醒**：雪崩/液体流跨 chunk 边界正确传播，不在缝隙处死掉；被触碰邻居下帧被重检（专项测试，架构 §5.5/§16.2）。
- [ ] **休止角**：粉末在平地堆出稳定休止角、不水平自流；左右偏置交替无单侧漂移（架构 §5.6）。
- [ ] **液体找平/分层**：水按 dispersion 水平找平；油浮于水（密度位移正确，架构 §5.6/§7.3）。
- [ ] **气体上升扩散**：气体上翻 + 抖动扩散，非干净柱状（架构 §5.6）。
- [ ] **bottom-up 一帧坍塌**：单柱沙在一遍扫描内自然坍塌到底（架构 §5.6）。
- [ ] **静止近零成本**：完全沉降区 chunk `_current` 收缩为空并 `Sleeping`，下帧不迭代；活跃成本随活跃 chunk 数而非屏幕面积缩放（架构 §5.4，HUD/基准验证）。
- [ ] **4-pass 无锁正确**：多线程结果与单线程 oracle 在统计性质上一致（质量守恒/堆形/分层）；无边界竞争损坏（架构 §16.2）。
- [ ] **单线程回退**：活跃 chunk 低于阈值时回退单线程，行为与多线程一致、无 barrier 浪费（架构 §5.7/§19 R7）。
- [ ] **32px move cap**：任一像素单 tick 净位移 ≤32px；liquid dispersion 被钳制；halo<间隔成立（不变式 #4，断言 + 测试）。
- [ ] **跨界写恒在 halo 内**：所有移动/反应跨界写入落在 32px halo（von Neumann 反应距离 1），无越界（不变式 #4）。
- [ ] **dirty-rect 双缓冲**：working/current 正确交换；grow padding 可调；空则 sleep、非空则 awake（架构 §5.4）。
- [ ] **相位接口正确**：相位 3 沉积本帧即被 CA 看见；相位 7 抛射不被本帧 CA 误处理；相位 6 在帧边界单线程交换（架构 §3.3）。
- [ ] **反应/lifetime 钩子**：`ReactionCount==0` 材质一次比较早退、不触反应数据；lifetime 归零调 `OnExpired`（架构 §7.4）。
- [ ] **bounds-check 消除**：热路径反汇编无 `RNGCHKFAIL`（Disasmo/`DOTNET_JitDisasm` 证实，架构 §12.6/§17.3）。
- [ ] **零分配**：稳态 `StepCa`+`SwapDirtyRects` 帧无托管堆分配（`[MemoryDiagnoser]`，`AGENTS.md §3`）。
- [ ] **不变式合规**：单缓冲（#3）、4-pass 无锁（#2）、四基石（#1）、跨界 halo（#4）、颜色不入 cell（#7）均被代码与注释落实，无违背。
- [ ] **依赖方向**：`Simulation` 仅引用 `Core`，不引用 Rendering/Physics/Content/World（ProjectReference 强制，plan/00 §5）。

---

## 6. 依赖关系

上游（必须先完成）：

- `plan/01`：解决方案/`PixelEngine.Simulation.csproj` 骨架、CPM、`Directory.Build.props`（`AllowUnsafeBlocks`/`Nullable`/分析器）。
- `plan/02`（`PixelEngine.Core`）：`JobSystem`（持久线程池）+ `Barrier`、POH/`NativeMemory` 内存封装、每-chunk 可种子化 RNG + 确定性 seam、`EngineConstants`（`ChunkSize/MoveCap/DirtyRectPadding/SingleThreadChunkThreshold`）、诊断/计时注册。

本文件向下游导出的 seam（由对应 plan 实现注入，**本文件不实现**）：

- `IChunkSource`（→ `plan/07`：chunk 驻留 hash-map、border ring 3×3 邻居驻留保证、流式装卸）。
- `MaterialPropsTable` + `IReactionExecutor` + `ILifetimeSink`（→ `plan/04`：材质字段语义、反应表与 `[tag]` 展开、温度相位 5、lifetime 转材质）。
- 相位 3/7 入口 `DepositCell`/`ReadAndClearCell`/`MarkDirty`（→ `plan/05`：自由粒子池与 cell↔particle handshake）。
- 相位 8 网格读写契约（→ `plan/06`：物理 erase/step/rasterize，相位顺序隔离由本内核保证）。
- 只读 `Material`/`Flags`/dirty rect（→ `plan/08`：渲染相位 9 重建 BGRA buffer，颜色不入 cell）。

测试/基准：`plan/14`（质量守恒、反应守恒、KeepAlive 边界传播、parity 防重、单线程 oracle 比对多线程、movement 规则单测、零分配与缩放曲线基准）。

里程碑映射（架构 §18）：本文件覆盖 **M1（chunk + dirty-rect + KeepAlive + border ring 雏形 + 质量守恒测试）** 与 **M2（持久线程池 + 4-pass checkerboard + 32px move cap + false-sharing 填充 + 单线程回退 + 每核加速曲线实测）** 的内核部分；为 M0 垂直切片提供单线程原地 + parity 核心。

---

## 7. 提交节点

按 `AGENTS.md §6` 每节点完成即用中文 git 提交（`type(scope): 中文简述`，scope=`sim`），不攒大堆。

- [x] **节点 1**：`feat(sim): CellGrid SoA 与 Chunk/DirtyRect 数据结构 + 寻址`（§4.1，对应清单 4.1 全部）。
  - `feat(sim): 实现 CellGrid(SoA)、Chunk、DirtyRect 与世界坐标寻址`
- [x] **节点 2**：`feat(sim): NeighborWindow 3x3 漫游与 bounds-check 消除`（§4.2）。
- [x] **节点 3**：`feat(sim): 单缓冲原地更新 + parity 时钟位 + bottom-up movement`（§4.3、§4.5 单线程路径，含 powder/liquid/gas）。
- [x] **节点 4**：`feat(sim): per-chunk dirty-rect grow/shrink/swap 与 sleep`（§4.7）。
- [x] **节点 5**：`feat(sim): KeepAlive 跨界唤醒（入站槽无锁）与 32px move cap`（§4.6）。
- [x] **节点 6**：`feat(sim): 4-pass checkerboard 多线程调度 + 单线程回退`（§4.4，接 plan/02 JobSystem）。
- [ ] **节点 7**：`feat(sim): 反应/lifetime 钩子 seam 与 MaterialPropsTable 消费`（§4.8）。
- [ ] **节点 8**：`feat(sim): 帧相位 3/4/6/7 内核接口与测试/诊断钩子`（§4.9、§4.10）。

每节点提交正文须注明「对应计划: plan/03-simulation-kernel.md §实现清单 第N项」与相关架构 `§x.y`。
