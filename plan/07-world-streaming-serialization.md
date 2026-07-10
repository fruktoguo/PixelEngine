# Plan 07 — 世界管理、流式装卸与存档（PixelEngine.World + PixelEngine.Serialization）

> **状态迁移（2026-07-10）**：本文件保留详细设计与历史 checkbox；当前状态、顺序和完成条件以 [`plan/tasks/README.md`](tasks/README.md) 为唯一真相源。不要在本文件新增 live task；设计变化仍须同步到这里。

> **DOC-002 历史证据口径（2026-07-10）**：后文 checkbox 与“已通过/已完成”叙述冻结自旧计划快照 `179efc3a`，迁移基线为 `5af1541f`，均不构成 live 状态；证据等级以 [稳定 Evidence Index](../docs/evidence-index.md) 为准。未入索引的 `artifacts/`、`BenchmarkDotNet.Artifacts/`、`scratch/` 仅是可再生历史线索；替代报告与重跑命令见 [DOC-002 校正报告](../docs/evidence-2026-07-10-doc-002-legacy-plan-audit.md)。

> 范围：chunk 驻留与流式装卸、世界常驻内存管理、持久化 / 存档。权威依据：`../docs/PixelEngine-架构与需求设计.md`（下称架构文档）§3.4、§5.1、§11、§12.2、§6.3；不变式见 `../AGENTS.md §1`（重点 #4 跨界写入恒在 halo 内、#8 material 字符串键入盘）；技术栈以 `00-conventions-and-techstack.md` 为准。
> 状态约定：`- [x]` 已有源码、测试、工具、报告或 plan 证据；`- [ ]` 未完成目标；`- [!]` 阻塞、证据债、人工验收或外部环境限制。

---

## 1. 目标与范围

本文档实现引擎的「世界生命周期与磁盘往返」两个相邻子系统：`PixelEngine.World`（chunk 驻留集合的管理、激活半径与 border ring、相机 / 视口、常驻内存上限与 LRU 驱逐、流式装卸的调度与线程安全屏障）与 `PixelEngine.Serialization`（chunk 二进制格式 RLE+LZ4、world manifest、material name↔id 重映射、版本迁移、free particles / 刚体 / 温度场的落盘）。两者共同保证「世界可向任意方向无限扩展、玩家修改持久、常驻内存守上限、存档跨 materials.json 变更仍正确」。

明确在范围内：chunk 的驻留 hash-map 的生命周期管理（增删时机与线程安全，而非容器类型本身）、border ring、激活区计算、装卸屏障、LRU 驱逐与内存预算、chunk 磁盘格式与压缩、manifest 全局态、name↔id 重映射、版本迁移链、流式与存档两类持久化的区分。

明确不在范围内（由其它文档负责，本文档只引用其类型与契约）：`Chunk` 与 `CellGrid`（SoA 缓冲）的数据结构本身、checkerboard 调度、dirty rect、KeepAlive、parity（均属 plan/03，本文档只管理这些 chunk 的驻留生命周期与磁盘往返）；`MaterialDef` / `MaterialRegistry` / name→id 分配（属 plan/04，本文档消费其 name↔id 映射做重映射）；free particle 与刚体的运行时对象与重建逻辑（属 plan/05、plan/06，本文档只定义其落盘快照 DTO 与编解码）；渲染侧的相机投影与世界纹理（属 plan/08，本文档只提供驱动激活区的世界空间相机焦点）；测试用例的实现（属 plan/14，本文档定义被测契约并标注「引用 plan/14」）。

一步到位、无 MVP：流式与存档自始即多线程（后台 I/O 线程 + Core 线程池并行字节准备）、零稳态帧分配（流式缓冲走 `ArrayPool<byte>`）、name 基重映射与版本迁移从首版即内建——架构文档明确「事后补极难」（§11、文末）。v1 仅做粗粒度快照存档，不做帧级 rewind（§6.3）。

---

## 2. 技术栈与依赖

与 `00-conventions-and-techstack.md` 完全一致，本段不另立选型：

- 运行时 / 语言：.NET 10 LTS / C# 14，`Nullable=enable`、file-scoped namespace（00 §1）。`PixelEngine.World` 与 `PixelEngine.Serialization` **不在** `AllowUnsafeBlocks` 项目之列（00 §1 仅 sim/physics/interop/rendering 开 unsafe）；本文档全部用安全代码：`System.Runtime.InteropServices.MemoryMarshal.Cast<TFrom,TTo>` 做 `ushort[]`↔`byte` 的零拷贝重解释，`System.Buffers` 的 `ArrayPool<byte>` / `IBufferWriter<byte>` 做缓冲。
- 存档压缩：**K4os.Compression.LZ4**（00 §4「存档压缩 LZ4」）。chunk payload 走 RLE 后再过 LZ4 block（架构 §11.3）。
- 序列化数据格式：chunk 二进制为自研格式（00 §4「内容序列化」表注、§13「序列化 = 自研 chunk 二进制(RLE+LZ4)+name 基 material 重映射+版本迁移」）。注意：`System.Text.Json` 仅用于 materials/reactions/场景内容（plan/04），**不用于 chunk 热数据落盘**。manifest 的全局标量字段用紧凑二进制（与 chunk 同容器），不用 JSON。
- 磁盘 I/O：BCL `System.IO`（`FileStream` 以 `FileOptions.Asynchronous` 异步读写、`RandomAccess` 定位读写 region 文件）。
- 线程：磁盘读写在**专用 I/O 线程**（I/O bound，不占 CA 的 CPU worker）；RLE+LZ4 字节准备可派发到 `PixelEngine.Core` 的持久线程池并行（AGENTS §3「序列化字节准备并行」）。两者均在帧关键相位之外（相位 11）执行。
- 内存：流式离游缓冲走 `ArrayPool<byte>` 与对象池；常驻 chunk 的 SoA 缓冲由 plan/03 用 POH/NativeMemory 分配，本文档只管理其驻留与释放时机。

项目依赖方向（00 §5，绝不反向）：`World → {Serialization, Simulation(03), Content(04), Core(02)}`；`Serialization → {Content(04), Core(02)}`。World 调用 Serialization 做编解码与磁盘存取；Serialization 不反向依赖 World。两者都不被 Simulation 依赖（Simulation 的 `ChunkMap` 容器由 World 在帧边界驱动增删，见 §3.2）。

无新增 native 依赖（不变式 #10：native 面收敛到 Box2D 一个依赖；LZ4 为纯托管）。

---

## 3. 详细设计

### 3.1 两类持久化的区分（架构 §11.1）

引擎有两条独立但共享底层格式的持久化路径，必须从 API 层面区分：

**流式（streaming）**——运行时把超出激活半径的 chunk 卸到磁盘、重入时读回，对玩家透明、由相机移动隐式触发、持续发生（架构 §3.4、§5.1）。落点是世界目录下的 region 存储（`RegionFileStore`），单 chunk 粒度增量读写。

**存档（save）**——玩家显式触发或周期性把整个一致世界状态落盘为可重载的存档点（架构 §6.3 的粗粒度快照）。落点是一个 `WorldManifest` + 所有 chunk 已 flush 到 region 存储的一致快照。v1 不提供帧级 rewind / undo（§6.3）：单缓冲原地模型无历史帧，连续 undo 需每帧 delta 或快照、在 2M cell 规模与非确定模型下不可行；只提供「回到某存档点重载」。

两者共用同一 `ChunkCodec`（chunk 二进制）与同一 `MaterialRemap`（name↔id 重映射）；存档额外含 manifest（全局态）。区分体现在两套入口：`WorldStreamer`（透明流式）与 `WorldSaveService`（显式存档），见 §4。

### 3.2 chunk 驻留 hash-map 的生命周期（架构 §5.1，不变式 #1）

`ResidentChunkMap`（coord→`Chunk` 的驻留容器，以 chunk 坐标为键）是 World 对 plan/03 `IChunkSource` seam 的正式实现（它是不变式 #1 的承重墙、被 checkerboard 调度直接迭代）。本文档负责该容器的**驻留生命周期管理**：决定哪些 coord 应驻留、何时增删、增删的线程安全、以及驱逐。

为不侵入 plan/03 的 `Chunk` 类型，World 维护一张并行的 `ResidencyTable`（`Dictionary<ChunkCoord, ChunkResidencyInfo>`，键为 plan/03 定义的 `ChunkCoord`）保存 World 侧元数据：`ChunkResidencyState State`（`Active` / `Border` / `Cached` / `Detached`）、`long LastTouchedFrame`（LRU 时间戳）、`int ResidentBytes`（内存核算）、`bool DirtySinceLoad`（是否需回写）。`Cached` 表示 border 外但仍驻留的 sleeping chunk，可被 LRU 选择卸载；`ResidencyTable` 与 `ResidentChunkMap` 同步增删，但前者是 World 私有、后者是 live sim 容器。

读路径（相位 4，CA 多线程经 KeepAlive 查邻居 chunk）只读 `ChunkMap.TryGet`，不做结构性变更；写路径（增删）只在相位 2 单线程发生（见 §3.4）。borders 保证 KeepAlive 目标必驻留（§3.3），故相位 4 的并发查找永不命中正在被增删的 slot。

### 3.3 相机 / 视口、激活区与 border ring（架构 §3.4、§12.2，不变式 #4）

- `WorldCamera`：世界空间相机焦点，持有 `FocusWorld`（cell 坐标，`long X, long Y` 支持无限世界）与 `ViewportCells`（视口宽高，cell）。由相位 0/1（输入 / 玩法）更新；渲染侧投影矩阵属 plan/08，本类只提供驱动激活区计算的世界空间焦点，二者不重复。
- `ChunkCoord` / `ChunkRect`：chunk 坐标（plan/03 定义 `ChunkCoord`）与其闭包围盒 `ChunkRect`（`int MinCx,MinCy,MaxCx,MaxCy`，本文档定义，含 `Contains`、`Expand(int)`、`Iterate()`）。
- `ActivationPolicy`：从 `WorldCamera` + `WorldStreamingConfig` 计算三层 chunk 矩形：
  - **可见区**：视口覆盖的 chunk（仅诊断 / 渲染参考）。
  - **激活区 `ActiveRect`**：可见区 + `ActivationMarginChunks`（模拟边距），区内 chunk 被 CA 模拟（架构 §5.1「只模拟玩家附近 / 有活动 chunk」）。
  - **border ring `BorderRect`**：`ActiveRect` 外扩 `BorderRingWidth=1` chunk 的一圈（架构 §3.4）。border chunk **驻留但默认 sleep**（不主动模拟，dirty rect 为空），其唯一职责是接住激活区边缘 cell 在 32px-halo 内的跨界写入与 KeepAlive，保证不变式 #4「跨界写入恒在 halo 内、目标必驻留」。当某 border chunk 被写入 / 被 KeepAlive 唤醒，相位 2 把它的外圈邻居提升为新 border 并请求装载（§3.4）。
- `WorldStreamingConfig`（record，运行时可调，按目标机内存实测定档，架构 §12.2「均为可调参数」）：`ActivationMarginChunks`、`BorderRingWidth=1`、`ResidentMemoryCapBytes`（默认 512MB，§3.5）、`EvictionTargetBytes`、`RegionSizeChunks=32`、`MaxStreamOpsPerFrame`（节流，防单帧装卸尖刺）。

### 3.4 装卸屏障与线程安全（架构 §3.4，不变式 #4、风险 R16）

「concurrent hash-map」不足以保证安全：相位 4 的 worker 做 KeepAlive 写某邻居 chunk 时，若后台线程正卸载 / 刚装载该 chunk 即为数据竞争。采用**装卸屏障**而非并发容器（架构 §3.4 明确判定 [高]）：

**相位 11（后台，异步）**——`WorldStreamer` 的后台 I/O 线程只做两件事，**绝不触碰 live `ChunkMap`**：
1. 卸载准备：对相位 2 已**从 map 摘下并交付**的游离 `Chunk` 对象，调用 `ChunkCodec.Encode` 序列化进游离 `ArrayPool<byte>` 缓冲，再 `RegionFileStore.Write` 落盘，最后把该 `Chunk` 的 SoA 缓冲归还池 / 释放。
2. 装载准备：对相位 2 请求的待装载 coord，`RegionFileStore.Read` 读字节（缺失则交内容生成，Demo 侧）→ `ChunkCodec.Decode` 反序列化进**游离的** `Chunk` 对象（SoA 缓冲新分配），放入完成队列。

RLE+LZ4 的 CPU 字节准备可在 Core 线程池并行（多 chunk 并发编解码），磁盘读写在专用 I/O 线程串行；二者皆在 live map 之外的游离缓冲 / 对象上操作。

**相位 2（residency apply，单线程，帧边界）**——此时相位 4/8 已结束、下一帧未开始，对 live map 独占，所有结构性变更只在此发生（架构 §3.3 相位 [2]）：
1. 应用上一帧后台完成的装载：把游离 `Chunk` 插入 `ChunkMap` 与 `ResidencyTable`。新装载 chunk 的瞬时位被重置（§3.6），dirty rect 置为「需重新评估」使 CA 下帧检视。
2. 应用上一帧后台完成的卸载收尾：确认已落盘的 chunk 从 `ResidencyTable` 移除、内存核算回收。
3. 重算激活区 / border（`ActivationPolicy`）；`ResidencyPlanner` diff 出本帧待装载 coord 集与待卸载 chunk 集；把待卸载 chunk **当场从 `ChunkMap` 摘下**（变为 `Detached`）并连同待装载请求一起提交给后台队列。
4. border 提升：被唤醒的 border chunk 的外圈邻居加入待装载集。

**驱逐位置约束**：卸载 / 驱逐只发生在 **border 之外**（架构 §3.4），因此被摘下的 chunk 本帧绝不可能被任何相位 4 worker 经 halo / KeepAlive 触碰——这是屏障成立的关键，与 32px move cap（≤半 chunk）+ border ring 共同保证不变式 #4。

队列形态：相位 2 → 相位 11 用 `StreamingRequestQueue`（单生产者单消费者，相位 2 生产、I/O 线程消费）；相位 11 → 相位 2 用 `CompletedChunkQueue`（I/O 线程 / 池 worker 生产、相位 2 消费）。二者无锁 SPSC ring 或加最小锁的双缓冲交换（非热路径，正确性优先）。

要求**流式线程安全测试**（引用 plan/14、架构 §16.2「流式线程安全：KeepAlive 进正在装 / 卸的边界 chunk 无竞争」）：构造相机持续平移 + 边界持续活动的场景，断言无 chunk 在 `Detached` 期间被相位 4 读到、质量守恒跨装卸边界不变。

### 3.5 常驻内存上限与 LRU 驱逐（架构 §1.4、§12.2）

不可用单屏 ~17MB 数字。按 chunk 核算：每常驻 chunk 的 sim 态 = 64×64 × (2B Material + 1B Flags + 1B Lifetime) ≈ 16KB，加粗温度子块 ~1KB 与元数据，约 **~18–20KB/chunk**（架构 §12.2）。render buffer 是屏幕大小、非 per-chunk，不计入此预算。

**per-cell Damage 平面纳入后的预算修订（plan/03 引入持久破坏模型，§3.11、AGENTS §7.1）**：Cell SoA 新增 1B `Damage` 平面（每 cell 累计破坏度），核心 sim 态从 **4B/cell → 5B/cell（+25%）**，即 64×64 × (2B Material + 1B Flags + 1B Lifetime + 1B Damage) 从 16KB → **20KB/chunk**（不含温度子块与元数据）。`ChunkMemoryBudget` 的 per-chunk 核算基数须同步由 16KB 提升到 20KB，与 plan/03、plan/16 三处数字保持一致；`ResidentMemoryCapBytes` 默认值不变，但同一 cap 下常驻 chunk 上限相应下降约 20%，档位须按目标机实测复核。

`ChunkMemoryBudget` 跟踪 `ResidentBytes` 总和与 `ResidentMemoryCapBytes`（默认 512MB，可配）。`ResidencyPlanner` 在相位 2 执行驱逐：当 `ResidentBytes > Cap` 时，即便仍在激活半径内，也按 **LRU 评分**（`LastTouchedFrame` 越旧 + 到相机 chunk 距离越远，评分越高）选最该走的 **sleeping 且在 border 之外** 的 chunk 提前卸载，直到降到 `EvictionTargetBytes`（低于 cap 的水位，避免抖动）。永不驱逐激活区内正在活动的 chunk 或 border chunk。磁盘侧已探索区随游玩增长，靠 §3.6 的 RLE+LZ4 压制（均匀 chunk 压缩率极高）。

`LastTouchedFrame` 在 chunk 进入激活区 / 被 CA 标记 dirty / 被 KeepAlive 唤醒时更新（由 World 在相位 2 汇总上一帧的活动信号，不进相位 4 热循环逐 cell 写）。

### 3.6 chunk 二进制格式：RLE + LZ4（架构 §11.3，不变式 #8 配套）

`ChunkCodec.Encode(in ChunkSnapshot, IBufferWriter<byte>)` / `Decode(ReadOnlySpan<byte>, out ChunkSnapshot)`。`ChunkSnapshot` 是 Serialization 侧 DTO（持 `Material`、`Flags`、`Lifetime`、`Temperature` 子块的只读 / 可写 span），World 在相位 2/11 边界在 `Chunk`（plan/03）与 `ChunkSnapshot` 之间适配，使 Serialization 不直接依赖 Simulation 的 `Chunk` 内部布局。

落盘内容（架构 §11.3）：
- `Material`（`ushort[64*64]`）：先 `RleCodec` 行程编码（大片均匀区收益巨大），**入盘的是 name 基重映射所需的运行时 id 序列，但 id↔name 表在 manifest / 存档头**（§3.7）——chunk blob 内仍存运行时 id，靠头部表在读档时整体重映射。
- `Flags`（`byte[64*64]`）：只存**持久位**，瞬时位清零后再 RLE。`PersistentFlagMask` 常量 = bit2(burning) | 预留的 bit4–7 中被声明为持久的位；**bit0(parity)、bit1(settled/sleep)、bit3(freefalling/有速度) 为瞬时位，不入盘**（架构 §11.3「parity/sleep 等瞬时位读档时重置」、§7.1 Flags 布局）。
- `Lifetime`（`byte[64*64]`，fire/gas 倒计时）：RLE。
- `Temperature`：该 chunk 区域的粗热场子块（1/4 分辨率，16×16 个 `Half`，架构 §7.1/§7.5），原样或 RLE。

blob 结构：`ChunkBlobHeader`（magic、`FormatVersion`、coord、各段未压缩长度、压缩标志）+ 各段 RLE 后拼接再整体 **LZ4 block 压缩**（K4os），头部存未压缩总长以便预分配。`RleCodec` 对 `ushort` 与 `byte` 各一版（用 `MemoryMarshal.Cast` 统一字节视图）。

**读档时瞬时位重置规则**（Decode 后由 World 应用）：parity 位统一置为「与当前帧 parity 不等」使该 chunk cell 下帧必被检视一次；settled/sleep 清零、freefalling 清零；chunk 的 dirty rect 由 plan/03 在装入时按「全 chunk 需评估」初始化，确保装载不丢活动也不残留旧调度态。

要求**存档往返逐 cell 等价测试**（引用 plan/14、架构 §16.2）：Encode→Decode 后 `Material`/`Lifetime`/持久 `Flags`/`Temperature` 逐 cell 等价；瞬时位被正确重置。

### 3.7 material name↔id 稳定性与重映射（架构 §11.2，不变式 #8，核心）

运行时数值 id 由 plan/04 的 `MaterialRegistry` 在加载序分配，**绝不可入盘**；入盘的稳定键是 `MaterialDef.Name`（§7.3）。

- `MaterialNameTable`：存档 / region 容器头部写一张「保存时 id → name」表（`ushort id` ↔ `string name`）。
- `MaterialRemap.Build(MaterialNameTable saved, MaterialRegistry current, ushort fallbackId) → MaterialRemap`：按 current 的 name→id 重建 `savedId → currentId` 的 LUT（`ushort[]`），并对 chunk 的 `Material` 数组整体重映射。改 materials.json 顺序、增删材质都不再损坏存档（架构 §11.2）。
- **缺失材质 fallback**：存档引用的 name 在当前定义里已删除 → 映射到声明的 fallback（默认 `"unknown_solid"`，最终回退 `Empty`），并经 Core 诊断输出「N 个 cell 被重映射到 fallback」（与 §11.2 / 热重载 §17.4 同理）。
- 重映射时机：装载 chunk 时（相位 11 Decode 后，或相位 2 应用前）用当前 `MaterialRemap` 把 chunk `Material` id 整体 remap；整世界存档读档时对所有 chunk 统一 remap。

要求**存档往返 + 重映射测试**（引用 plan/14、架构 §16.2「改 materials.json 顺序 / 增删材质后旧档仍正确重映射」、风险 R15）：保存→改 materials.json 顺序 / 增删→读档，断言世界逐 cell 语义等价、删除材质走 fallback 且有诊断计数。

### 3.8 world manifest 与全局态持久化（架构 §11.3）

`WorldManifest`（紧凑二进制，非 JSON）落盘世界级全局态：`FormatVersion`、`WorldSeed`、`GameTimeTicks`（当前游戏时间）、`PlayerStateBlob`（`byte[]`，由 Demo/Hosting 提供的不透明玩家状态，本文档不解释其内容）、`MaterialNameTable`（§3.7）、`FreeParticleSnapshot[]`（在飞自由粒子：位置 / 速度 / 材质 / 寿命）、`RigidBodySnapshot[]`（刚体：id、不可变 body-local mask + 每像素材质、当前 transform、线 / 角速度）、chunk 索引（哪些 coord 在该存档点驻留 / 已落盘）。

`FreeParticleSnapshot` 与 `RigidBodySnapshot` 是 Serialization 侧 DTO（仅数据 + 编解码）；其运行时对象的快照导出与读档重建分别属 plan/05（particle 池）与 plan/06（CCL→Box2D 重建），本文档定义 DTO schema 与 `ManifestCodec` 的读写，并暴露 `IWorldStateSnapshotSource` / `IWorldStateSnapshotSink` 契约供那两个子系统实现（dogfood：Demo 不碰内部）。温度场不进 manifest——它随各 chunk 子块落盘（§3.6）。

整世界存档应在帧边界（相位 2 或专门暂停点）对一致快照执行，避免读到半更新网格（架构 §11.5）。`WorldSaveService.SaveAll`：在相位 2 先把所有 resident 且 `DirtySinceLoad` 的 chunk flush 到 region 存储，再写 `WorldManifest`，形成一致快照；`LoadAll` 反向。

### 3.9 版本迁移链（架构 §11.4）

存档头与 chunk blob 头均含显式 `FormatVersion`（int，集中于 `SaveFormatVersions` 常量）。读档时若版本低于当前，按注册的迁移链逐级升级（v1→v2→…）。`ISaveMigrator { int FromVersion; void Migrate(MigrationContext ctx); }`，`MigrationChain` 按版本顺序应用；每个迁移是结构化转换。name 基 material 机制使「新增 / 重排材质」**无需迁移**（§3.7）；只有改变某材质语义、字段布局或 blob 段结构才需迁移步骤。

要求**版本迁移链测试**（引用 plan/14、架构 §16.2）：用旧版本字节样本走链升级后读取正确。

### 3.10 磁盘布局

世界目录结构：`<world>/regions/r.<rx>.<ry>.rgn`（region 文件，每 `RegionSizeChunks=32`×32 个 chunk 一文件，文件内含 chunk 偏移索引 + 各 chunk blob，支持单 chunk 随机读写与原地覆盖 / 追加）；`<world>/manifest.bin`（最新 manifest）；`<world>/saves/<name>.pesave`（显式存档点：manifest 快照 + region 索引快照）。

`RegionFileStore : IChunkStore`（Serialization）：`bool TryRead(ChunkCoord, IBufferWriter<byte>)`、`void Write(ChunkCoord, ReadOnlySpan<byte>)`、`bool Exists(ChunkCoord)`、`void Delete(ChunkCoord)`。流式（§3.1）走 region 存储增量读写；存档（§3.8）在其上叠加 manifest 与一致性快照。所有磁盘写经临时文件 + 原子 rename 防半写损坏。

### 3.11 per-cell Damage 平面的持久化契约（持久破坏模型，plan/03/16 配套，不变式 #1/#5/#8）

plan/03 为 Cell 新增一条 per-cell `Damage`（`byte`）SoA 平面：单缓冲原地累加（守 #1，不双缓冲），记录每 cell 被武器 / 破坏 API 累计造成的破坏度；达到材质 `Integrity` 阈值时该 cell 被消费为 `DestroyedTarget`（语义在 plan/04/16，本文档不解释触发规则）。本文档负责该平面在**磁盘往返**中的落盘契约，判定如下（synthesis verdict 必须项）：

**Damage 是持久 lane，不是瞬时 lane。** 存档 / 流式往返**保留**累计破坏度——玩家挖开一半的墙、被激光烧蚀的岩层，卸载重入或读档后破坏进度不丢失，与 `Material`/`Lifetime` 同属需逐 cell 复原的持久数据。因此 Damage 段进入 `ChunkSnapshot` 与 `ChunkCodec` 编解码，**不**进入 `PersistentFlagMask` 的瞬时位重置规则（§3.6 的 parity/settled/freefalling 清零只作用于 `Flags`，与 Damage 无关）。

落盘细节：
- `ChunkSnapshot` 增只读 / 可写 `Span<byte> Damage` 视图（64×64），与 `Material`/`Flags`/`Lifetime`/`Temperature` 并列；World 在 `Chunk`↔`ChunkSnapshot` 适配时一并搬运 Damage 平面。
- `ChunkCodec.Encode` 在既有各段之后追加一个 **Damage RLE 段**（`RleCodec.EncodeU8`，破坏度大片为 0，均匀区 RLE 收益高），随整 blob 一起 LZ4；`Decode` 对称解出 Damage 段写回 `ChunkSnapshot.Damage`。段的未压缩长度进 `ChunkBlobHeader`（与其它段同机制）。
- **版本迁移**：新增 Damage 段使 blob 段结构变化，须 bump `SaveFormatVersions`（chunk blob 与存档头各自的当前版本 +1），并注册一个 `ISaveMigrator`：旧版本 blob 无 Damage 段，迁移时**缺省填 0**（旧世界视为无累计破坏），其余段原样透传。旧档读取不报错、逐 cell 语义等价（除 Damage 全 0 外）。
- **material 重映射不受影响、但 fallback 命中须清 Damage**：`MaterialRemap` 只重写 `Material` id 的 LUT（§3.7），Damage 段按 cell 位置原样透传、**不参与 id 重映射**。唯一例外：某 cell 的 saved material name 在当前定义里已删除、被映射到 fallback（§3.7）时，该 cell 的累计 Damage **清 0**——破坏度是相对原材质 `Integrity` 的累计量，材质已被替换后旧破坏度语义失效，保留会导致新材质凭空半损。fallback 命中的 Damage 清零计数与 §3.7 的重映射诊断一并输出。
- 单缓冲 / 相位纪律：Damage 平面的编解码只在相位 11 后台对游离 `Chunk`/`ChunkSnapshot` 进行（守 §3.4 屏障），装载后随 chunk 一并入 live map；本文档不写 cell Damage（写入是 plan/16 安全相位的离散编辑），只做磁盘往返的搬运与复原。

与 plan/14 挂钩：§5.3 的存档往返逐 cell 等价测试纳入 Damage 平面（Encode→Decode 后逐 cell 等价）；新增旧档→新档迁移测试断言旧档 Damage 缺省 0；重映射测试断言 fallback 命中 cell 的 Damage 被清 0。

---

## 4. 实现清单

> 命名空间：`PixelEngine.World` 与 `PixelEngine.Serialization`。所有公开类型带中文 XML 文档注释（00 §7、AGENTS §4）。相位号指架构 §3.3 帧循环。

### 4.1 World — 坐标、相机、激活区（架构 §3.4、§5.1、§12.2）

- [x] `ChunkRect`（readonly struct，本文档定义）：`int MinCx,MinCy,MaxCx,MaxCy`；`bool Contains(ChunkCoord)`、`ChunkRect Expand(int)`、`int Count`、`Iterate()` 枚举。（§3.3）
- [x] `WorldCamera`（class）：`long FocusX,FocusY`（cell）、`int ViewportCellsX,ViewportCellsY`；由相位 0/1 更新；`ChunkCoord FocusChunk`。仅提供激活区驱动焦点，不含渲染投影（plan/08）。（相位 0/1，§3.3）
- [x] `WorldStreamingConfig`（record）：`ActivationMarginChunks`、`BorderRingWidth=1`、`ResidentMemoryCapBytes=512*1024*1024`、`EvictionTargetBytes`、`RegionSizeChunks=32`、`MaxStreamOpsPerFrame`；运行时可调。（§3.3/§3.5）
- [x] `ActivationPolicy`（class）：`ComputeVisible(WorldCamera)`、`ComputeActive(WorldCamera,WorldStreamingConfig) → ChunkRect`、`ComputeBorder(ChunkRect active,WorldStreamingConfig) → ChunkRect`（外扩 1 圈）。border chunk 驻留但默认 sleep，保证 32px-halo 跨界写入恒落驻留 chunk（不变式 #4）。（相位 2，§3.3）

### 4.2 World — 驻留生命周期与装卸屏障（架构 §3.4，不变式 #4，R16）

- [x] `enum ChunkResidencyState { Active, Border, Cached, Detached }`；`struct ChunkResidencyInfo { ChunkResidencyState State; long LastTouchedFrame; int ResidentBytes; bool DirtySinceLoad; }`。（§3.2）
- [x] `ResidentChunkMap`（class）：World 对 plan/03 `IChunkSource` seam 的 live map 实现，结构性增删只在相位 2 发生，提供稳定 `ResidentChunks` 快照、`TryGetChunk`、`ResolveNeighborhood`。（§3.2/§3.4）
- [x] `ResidencyTable`（class）：`Dictionary<ChunkCoord,ChunkResidencyInfo>`，World 私有、与 `ResidentChunkMap` 同步增删；`TryGetInfo`、`Set`、`Remove`、`Touch(ChunkCoord,long frame)`、枚举。（§3.2/§3.5）
- [x] `ResidencyPlanner`（class）：`Plan(ChunkRect active, ChunkRect border, ResidencyTable table, ChunkMemoryBudget budget) → ResidencyPlan`；产出待装载 coord 集（进入 active∪border 但未驻留）、待卸载 chunk 集（border 之外 + 内存超限 LRU），含 border 提升项；受 `MaxStreamOpsPerFrame` 节流。（相位 2，§3.4/§3.5）
- [x] `ChunkMemoryBudget`（class）：`long ResidentBytes`、`long CapBytes`、`Add/Remove(int bytes)`、`bool OverCap`、`SelectEvictions(ResidencyTable, ChunkRect border, long target) → IReadOnlyList<ChunkCoord>`（LRU + 距离评分，仅选 sleeping 且 border 外）。（相位 2，§3.5）
- [x] `StreamingRequest`（struct：`enum Kind{Load,Unload}`、`ChunkCoord`、卸载时携游离 `Chunk` 句柄）；`StreamingRequestQueue`（SPSC，相位 2 生产 / I/O 线程消费）；`CompletedChunkQueue`（I/O 线程 / 池生产 / 相位 2 消费，携反序列化好的游离 `Chunk` 或卸载完成回执）。（§3.4）
- [x] `WorldStreamer`（class）：拥有后台 I/O 线程、两条队列、`RegionFileStore`、`ChunkCodec`、`MaterialRemap`。
  - [x] `ApplyPrepared(long frame)`：相位 2，应用 `CompletedChunkQueue`——插入已装载 chunk 到 `ChunkMap`+`ResidencyTable`（重置瞬时位、置 dirty-rect 待评估）、收尾已卸载 chunk（释放缓冲、移除记账）。（相位 2）
  - [x] `SubmitPlan(ResidencyPlan)`：相位 2，对待卸载 chunk **当场从 `ChunkMap` 摘下**置 `Detached` 并入 `StreamingRequestQueue`；待装载 coord 入队。**绝不在此做磁盘 I/O。**（相位 2，§3.4）
  - [x] `ProcessIo(CancellationToken)`：相位 11 后台循环，消费 `StreamingRequestQueue`：装载=`RegionFileStore.TryRead`→`ChunkCodec.Decode`→remap→游离 `Chunk`；卸载=`ChunkCodec.Encode`(游离 Chunk)→`RegionFileStore.Write`→释放；结果入 `CompletedChunkQueue`。**绝不触碰 live `ChunkMap`。**（相位 11，§3.4）
  - [x] RLE+LZ4 字节准备可派发 Core 线程池并行（多 chunk 并发），磁盘读写在 I/O 线程；缓冲走 `ArrayPool<byte>`。（AGENTS §3）
- [x] `WorldManager`（facade，class）：拥有 `WorldCamera`、`ActivationPolicy`、`ResidencyTable`、`ResidencyPlanner`、`ChunkMemoryBudget`、`WorldStreamer`；引用 plan/03 的 `ChunkMap`。
  - [x] `UpdateCamera(...)`（相位 0/1）。
  - [x] `NotifyBoundaryWakes(ReadOnlySpan<BoundaryWakeSnapshot>)`：相位 2 前汇总上一帧 CA KeepAlive / 边界唤醒，把被唤醒 border chunk 临时提升为 active，促使其新外圈 border 在下一次模拟前装载。（§3.4/§3.3，不变式 #4）
  - [x] `ApplyResidency(long frame)`（相位 2）：`WorldStreamer.ApplyPrepared` → 重算 active/border → `ResidencyPlanner.Plan` → border 提升 → `WorldStreamer.SubmitPlan`。结构性增删只在此发生（§3.4）。
  - [x] `RunStreaming(CancellationToken)`（相位 11，驱动 `WorldStreamer.ProcessIo`）。

### 4.3 Serialization — chunk 二进制（架构 §11.3，不变式 #8）

- [x] `ChunkSnapshot`（ref struct）：持 `Span<ushort> Material`、`Span<byte> Flags`、`Span<byte> Lifetime`、`Span<Half> Temperature` 视图；World 在 `Chunk`↔`ChunkSnapshot` 间适配（隔离 Serialization 不依赖 plan/03 `Chunk` 内部）。（§3.6）
- [x] `ChunkSnapshot` 增 `Span<byte> Damage` 视图（64×64，per-cell 累计破坏度）：与 `Material`/`Flags`/`Lifetime`/`Temperature` 并列；World 在 `Chunk`↔`ChunkSnapshot` 适配时一并搬运该平面，隔离 Serialization 不依赖 plan/03 `Chunk` Damage 布局。（§3.11，持久 lane）
- [x] `PersistentFlagMask`（const byte）= bit2(burning)；XML 注释列明 bit0 parity / bit1 settled-sleep / bit3 freefalling / bit4 rigid-owned 为瞬时位不入盘（架构 §7.1/§11.3）。（§3.6）
- [x] `RleCodec`（static）：`EncodeU16(ReadOnlySpan<ushort>, IBufferWriter<byte>)`/`DecodeU16`、`EncodeU8`/`DecodeU8`；行程编码大片均匀区。（§3.6）
- [x] `Lz4BlockCodec`（static，封装 K4os）：`Compress(ReadOnlySpan<byte>, IBufferWriter<byte>)`、`Decompress(ReadOnlySpan<byte>, Span<byte>)`，存未压缩长度便于预分配。（§3.6）
- [x] `ChunkBlobHeader`（struct）：magic、`FormatVersion`、`ChunkCoord`、各段未压缩长度、压缩标志。（§3.6/§3.9）
- [x] `ChunkCodec`（class）：`Encode(in ChunkSnapshot, IBufferWriter<byte>)`——Flags 先 `& PersistentFlagMask`，各段 RLE 后拼接再 LZ4；`Decode(ReadOnlySpan<byte>, ChunkSnapshot dst)`——LZ4 解→分段 RLE 解→瞬时位重置规则（parity 置异、settled/freefalling 清零）。（相位 11，§3.6）
- [x] `ChunkCodec` 增 **Damage RLE 段**：`Encode` 在既有各段之后追加 `RleCodec.EncodeU8(snapshot.Damage, ...)`（大片 0 破坏度 RLE 收益高），随整 blob 一起 LZ4；`Decode` 对称解出 Damage 段写回 `dst.Damage`；该段未压缩长度进 `ChunkBlobHeader`。Damage 属持久 lane，**不**参与瞬时位重置规则。（§3.11）

### 4.4 Serialization — material 重映射、manifest、迁移、磁盘（架构 §11.2/§11.3/§11.4）

- [x] `MaterialNameTable`（class）：`ushort id`↔`string name` 双向；`Write/Read`（紧凑二进制）。（§3.7）
- [x] `MaterialRemap`（class）：`Build(MaterialNameTable saved, MaterialRegistry current, ushort fallbackId)`；`ushort Map(ushort savedId)`；`RemapInPlace(Span<ushort> material)`；记 fallback 命中计数并经 Core 诊断输出。（§3.7，不变式 #8）
- [x] `MaterialRemap.RemapInPlace` 增 Damage 联动重载 `RemapInPlace(Span<ushort> material, Span<byte> damage)`：material id 按 LUT 重写，Damage 段随 cell 位置原样透传、**不参与 id 重映射**；仅对**映射到 fallback**（原 name 已删除）的 cell 将其 `Damage` 清 0（破坏度相对原材质 Integrity 失效），并记入 fallback 清零计数一并诊断。（§3.11，不变式 #8）
- [x] `FreeParticleSnapshot`（struct DTO：`float x,y,vx,vy; ushort material; byte colorVariant; byte life`，对齐 plan/05 §7.6）。（§3.8）
- [x] `RigidBodySnapshot`（DTO：`int id`、不可变 body-local mask（`byte[]` + 尺寸）+ 每像素 `ushort material`、`float posX,posY,rotCos,rotSin`、`float linVelX,linVelY,angVel`，足以由 plan/06 重建 Box2D 刚体）。（§3.8）
- [x] `IWorldStateSnapshotSource` / `IWorldStateSnapshotSink`（接口）：供 plan/05/06 导出 / 重建 particles 与 bodies；Serialization 只读写 DTO。（§3.8）
- [x] `WorldManifest`（class）+ `ManifestCodec`：`FormatVersion`、`WorldSeed`、`GameTimeTicks`、`PlayerStateBlob`、`MaterialNameTable`、`FreeParticleSnapshot[]`、`RigidBodySnapshot[]`、chunk 索引；紧凑二进制读写（非 JSON）。（§3.8）
- [x] `SaveFormatVersions`（const）；`ISaveMigrator { int FromVersion; void Migrate(MigrationContext); }`；`MigrationChain.Upgrade(stream/bytes, int fromVersion)` 逐级应用。（§3.9）
- [x] bump `SaveFormatVersions`（chunk blob 头与存档头当前版本各 +1，因 blob 段结构新增 Damage 段）；旧 v1 chunk blob 兼容读取时 Damage 段**缺省填 0**，v2 manifest 经迁移链升级版本戳，旧档读取不报错且逐 cell 语义等价（除 Damage 全 0）。（§3.11/§3.9）
- [x] `IChunkStore` 接口 + `RegionFileStore`（class）：`TryRead/Write/Exists/Delete(ChunkCoord)`；region 文件（32×32 chunk/文件）+ 文件内偏移索引；临时文件 + 原子 rename。（§3.10）
- [x] `WorldSaveService`（class）：`SaveAll(WorldManager, IWorldStateSnapshotSource, string savePath)`（相位 2/暂停点：flush dirty chunk→region→写 manifest 快照）；`LoadAll(string savePath, IWorldStateSnapshotSink)`（读 manifest→迁移链→remap→装载）。（相位 2，§3.8）

---

## 5. 验收标准

> 全部勾选方算本文档完成（AGENTS §7）。功能性测试实现在 plan/14，本文档断言其契约成立。

### 5.1 驻留、流式与线程安全

- [x] 相机平移使 chunk 进出激活区时，激活区内 chunk 被模拟、超出者卸载、重入者从磁盘读回且玩家修改持久（架构 §5.1）。
- [x] border ring 始终为激活区外宽 1 chunk、驻留且默认 sleep；激活区边缘 cell 的 32px-halo 跨界写入与 KeepAlive 目标恒落在驻留 chunk 上，无「写入落到非驻留邻居」的洞（不变式 #4、架构 §3.4）。
- [x] 结构性增删（`ChunkMap`/`ResidencyTable`）**只在相位 2 单线程**发生；相位 11 后台线程仅操作游离字节缓冲与游离 `Chunk`，经断言 / 测试证实从不触碰 live map（架构 §3.4）。
- [x] 驱逐 / 卸载只发生在 border 之外，被摘下的 chunk 本帧不被任何相位 4 worker 触碰。
- [x] **流式线程安全测试通过**（引用 plan/14）：相机持续平移 + 边界持续活动下，质量守恒跨装卸边界不变、无 `Detached` 期并发读（架构 §16.2、R16）。

### 5.2 内存上限与 LRU

- [x] `ChunkMemoryBudget` 以 ~18–20KB/chunk 核算常驻字节，不用单屏数字（架构 §12.2）。
- [x] 常驻字节超 `ResidentMemoryCapBytes`（默认 512MB，可配）时，即便仍在激活半径内，也按 LRU + 距离提前驱逐最远 sleeping（border 外）chunk 至 `EvictionTargetBytes`，且不抖动（架构 §1.4/§12.2）。
- [x] 激活区内活动 chunk 与 border chunk 永不被内存驱逐。
- [x] **纳入 Damage 平面后的 per-chunk 预算基数为 20KB**（核心 sim 态 4B/cell → 5B/cell，+25%）：`ChunkMemoryBudget` 核算基数由 16KB 提升到 20KB，与 plan/03、plan/16 三处数字一致；同一 `ResidentMemoryCapBytes` 下常驻 chunk 上限相应下调约 20%，档位按目标机实测复核（§3.5/§3.11，AGENTS §7.1 per-cell 字节预算评审）。

### 5.3 chunk 格式与位区分

- [x] **存档往返逐 cell 等价测试通过**（引用 plan/14）：`ChunkCodec.Encode→Decode` 后 `Material`/`Lifetime`/持久 `Flags`/`Temperature` 逐 cell 等价（架构 §16.2）。
- [x] 持久位（burning 等）入盘并读回；瞬时位（parity、settled/sleep、freefalling）不入盘，读档时按规则重置（架构 §11.3/§7.1）。
- [x] chunk payload 经 RLE+LZ4（K4os），均匀 chunk 压缩率高（架构 §11.3）。
- [x] **per-cell Damage 平面为持久 lane**：`ChunkCodec.Encode→Decode` 后 `Damage` 段逐 cell 等价（累计破坏度不丢），存档 / 流式往返均保留；Damage 不被瞬时位重置规则触及（§3.11，plan/14 逐 cell 等价测试纳入 Damage）。
- [x] **旧档→新档迁移**：`SaveFormatVersions` 已 bump，旧版本无 Damage 段的 blob 兼容读取后 Damage 缺省全 0、其余段逐 cell 等价，读取不报错（§3.11/§3.9，plan/14 迁移测试）。

### 5.4 material 稳定性与 manifest

- [x] 入盘只用 name；存档头 / region 头写 id↔name 表；运行时 id 绝不作为持久语义入盘（不变式 #8、架构 §11.2）。
- [x] **存档往返 + 重映射测试通过**（引用 plan/14）：改 materials.json 顺序、增删材质后旧档仍逐 cell 语义正确重映射；删除材质的 cell 走 fallback 并有诊断计数（架构 §16.2、R15）。
- [x] manifest 持久化 world seed / 版本 / 游戏时间 / 玩家态 / id↔name 表 / 在飞自由粒子 / 刚体（足以读档重建并续跑）；温度场随 chunk 子块落盘（架构 §11.3）。
- [x] 整世界存档在帧边界对一致快照执行，不读半更新网格（架构 §11.5）。
- [x] **重映射对 Damage 的处理正确**（引用 plan/14）：material id 重映射时 Damage 段按 cell 位置原样透传、不参与 id 映射；仅**映射到 fallback** 的 cell 的 Damage 被清 0 并计入 fallback 清零诊断（§3.11）。

### 5.5 版本迁移与两类持久化区分

- [x] **版本迁移链测试通过**（引用 plan/14）：旧 `FormatVersion` 存档经迁移链逐级升级后读取正确；新增 / 重排材质无需迁移（name 基机制，架构 §11.4）。
- [x] 流式（透明、隐式、持续）与存档（显式 / 周期、粗粒度快照）走分离入口（`WorldStreamer` vs `WorldSaveService`），共用 `ChunkCodec` 与 `MaterialRemap`（架构 §11.1）。
- [x] v1 不提供帧级 rewind / undo，仅粗粒度快照存档（架构 §6.3）。

### 5.6 工程纪律

- [x] 稳态帧循环内零托管堆分配：流式缓冲走 `ArrayPool<byte>` / 对象池，磁盘 I/O 与字节准备在相位 11 之外的帧关键路径无分配（AGENTS §3、架构 §12.4）。
- [x] `PixelEngine.World`/`PixelEngine.Serialization` 不开 `AllowUnsafeBlocks`，全安全代码（`MemoryMarshal.Cast`）（00 §1）。
- [x] 公开 API 全部带中文 XML 文档注释；依赖方向 `World → {Core, Simulation, Serialization}`、`Serialization → {Core, Simulation}`，无反向（00 §5）。

---

## 6. 依赖关系

前置（须先完成）：

- plan/02（Core）：持久线程池（并行字节准备）、事件总线 / 诊断（fallback 计数、流式 HUD）、`EngineConstants`（`ChunkSize=64`、`MoveCap=32`）、内存池封装。
- plan/03（Simulation）：`Chunk`、`CellGrid`（SoA）、`ChunkCoord`、`ChunkMap` 容器、dirty rect / sleep / parity 语义（本文档管理其驻留生命周期、读写其持久 / 瞬时位、在相位 2 驱动其增删）。
- plan/04（Content）：`MaterialDef.Name`、`MaterialRegistry`（name↔id），供 `MaterialRemap` 重映射。

被依赖 / 协作：

- plan/05（Particles）：实现 `IWorldStateSnapshotSource/Sink`，导出 / 重建 `FreeParticleSnapshot`。
- plan/06（Physics）：实现刚体快照导出 / 重建（`RigidBodySnapshot`→Box2D 复合刚体）。
- plan/08（Rendering）：消费 `WorldCamera` 世界空间焦点（渲染投影属 08，不重复）。
- plan/13（Demo）：提供 `PlayerStateBlob`、触发显式存档；缺失 region 时的内容生成。
- plan/14（Testing）：实现 §5 引用的流式线程安全、存档往返、name↔id 重映射、版本迁移链测试。
- plan/17（Roadmap）：本文档对应里程碑 **M9（存档 + 流式 + 打包）**，刻意置于 sim（M1–M3）与物理（M6）稳定之后。

无与不变式 / 技术栈的冲突：name 基入盘（#8）、border ring 保证 halo 内跨界写入（#4）、装卸屏障（§3.4）、LZ4（00 §4）、不开 unsafe（00 §1）均严格遵循。

---

## 7. 提交节点

按 AGENTS §6，每完成一节点立即用中文 git 提交（type 前缀英文，scope=`world`/`world,serialization`）：

- [x] `feat(world): 实现 chunk 驻留 hash-map 生命周期、相机/视口与激活区+border ring`（对应清单 §4.1、§4.2 前半）
- [x] `feat(world): 实现装卸屏障(相位2/11分离)与流式后台 I/O`（对应清单 §4.2、§3.4）
- [x] `feat(world): 实现常驻内存上限与 LRU 驱逐`（对应清单 §4.2 budget/planner、§3.5）
- [x] `feat(serialization): 实现 chunk 二进制 RLE+LZ4 与持久/瞬时位区分`（对应清单 §4.3、§3.6）
- [x] `feat(serialization): 实现 material name↔id 重映射与 fallback`（对应清单 §4.4 remap、§3.7）
- [x] `feat(serialization): 实现 world manifest、全局态持久化与磁盘 region 布局`（对应清单 §4.4 manifest/store、§3.8/§3.10）
- [x] `feat(serialization): 实现版本迁移链与显式存档/读档服务`（对应清单 §4.4 migration/save、§3.9）
- [x] `test(world,serialization): 接入流式线程安全/存档往返/重映射/迁移测试（plan/14）`（对应验收 §5，测试实现引用 plan/14）
- [x] `feat(serialization): 落盘 per-cell Damage 持久 lane（RLE 段+版本迁移+fallback 清零+预算修订）`（对应清单 §4.3/§4.4 Damage 项、§3.11，预算 §3.5；plan/14 逐 cell 等价/迁移/重映射测试）

> 每节点完成即勾选并提交；与架构文档 / 不变式冲突先改计划再改代码（AGENTS §5）。
