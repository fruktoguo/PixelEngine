# Plan 05 — 自由粒子与生命周期（free particles & lifecycle）

> 范围锚点：本文件定义**离网格的自由粒子系统**与 **cell↔particle handshake**，归属 `PixelEngine.Simulation`（命名空间 `PixelEngine.Simulation.Particles`）。网格 cell 的 CA 规则在 `plan/03-simulation-kernel.md`；粒子的**渲染合成**在 `plan/08-rendering.md`（CPU stamp）与 `plan/09-gpu-compute.md`（GPU point-sprite）；材质定义（含密度、是否发光）在 `plan/04-materials-reactions-temperature.md`。
> 权威设计依据：架构文档 `docs/PixelEngine-架构与需求设计.md` §7.6（grid cell vs free particle）、§3.3（帧相位 3/7/9）、§9.3（粒子合成）、§19 R13（粒子泄漏）。技术栈：`plan/00`。开发宪法：`AGENTS.md`。
> 状态：`- [ ]` 未开始 / `- [x]` 完成 / `- [~]` 进行中 / `- [!]` 阻塞。

---

## 1. 目标与范围

本文件交付一套**完整、零稳态分配、可并行**的自由粒子子系统，使「冲击/爆炸把 cell 抛为带速度的飞行粒子，粒子落定后重新沉积为 cell」这一 Noita 标志性双向转换（架构 §1.2 第五点）端到端成立。free particle 是离网格 agent，带浮点位置 + 速度、做弹道运动、飞行时**不参与 CA**（架构 §7.6）。

**在范围内**：`Particle` struct 与其 ~20 字节内存布局；连续缓冲 + active-count 存储与 swap-remove 池化；并行弹道积分；cell→particle 抛射（相位 7）；particle→cell 沉积（相位 3）；每粒子硬性 max-lifetime 与「无处沉积则杀死」泄漏回退（架构 R13，强制项）；规模 5 万–20 万活跃粒子 60fps 的数据布局与迭代约束；向渲染（plan/08、plan/09）、编辑器（plan/12）、音频（plan/10）、序列化（plan/07）暴露的**只读数据与接口**。

**明确不在范围内**（在别处实现，本文仅定义接口或引用）：粒子在屏幕上的实际绘制（CPU stamp + emissive 在 plan/08 相位 9；GPU point-sprite 批绘在 plan/09，本文只暴露数据缓冲，不写任何渲染代码）；CA movement/reaction（plan/03、plan/04）；`MaterialDef` 字段定义本身（plan/04，本文只按 `ushort` material id 只读查询密度与 emissive 标志）；爆炸的玩法触发逻辑（Demo plan/13，本文只提供入队 API）。

**本文严格遵守的不变式**（`AGENTS.md §1`，任一冲突即停并上报）：颜色不入 cell（§1.7，粒子也只存 `colorVariant` 噪声种子，RGBA 在渲染相位生成）；相位顺序而非锁避免竞争（§1，沉积在相位 3、抛射在相位 7，与 CA 相位 4 分离）；稳态帧零托管分配（§3，缓冲预分配、swap-remove、无 per-particle GC）；能并行就并行（§2，弹道积分走持久线程池 JobSystem，不用 `Parallel.For`）；material 运行时 id 仅作索引、入盘用 name（§1.8，序列化由 plan/07 负责重映射，本文只暴露在飞粒子的枚举/重建接口）。

---

## 2. 技术栈与依赖

- 运行时/语言：.NET 10 LTS / C# 14；本子系统位于 `PixelEngine.Simulation`（已开 `AllowUnsafeBlocks`、`Optimize`，`plan/00 §1`）。
- **依赖 `PixelEngine.Core`（plan/02）**：
  - 连续缓冲与池化：用 plan/02 的 pinned 缓冲原语（`GC.AllocateArray<T>(n, pinned:true)` / `NativeMemory` 封装；与 `SoaBuffer` 同源的 POH 后端）承载粒子数组——见 §3.2 关于「AoS `Particle[]` 而非 `SoaBuffer` 列布局」的理由（架构 §7.6 强制 `Span<Particle>` 连续 AoS 迭代）。
  - 并行：`JobSystem`（持久 worker pool + barrier）做弹道积分的 index-range 分区派发（`plan/00 §3`、架构 §12.7）。
  - 常量：新增项进 `EngineConstants`（`plan/00 §7` 常量集中）：`ParticleCapacityDefault`、`ParticleGravityPerTick`、`ParticleDepositSpeedEpsilon`、`ParticleMaxLifetimeTicks`、`ParticleEjectMaxPerTick`。
  - RNG：每 chunk 可种子化 RNG（爆炸散布抖动、colorVariant），走 Core RNG 的确定性 seam（架构 §6.2）。
  - 诊断/事件总线：粒子计数等分项注册到 Core 诊断（`plan/00 §7`）；沉积/爆炸音频事件写入 Core 事件总线（架构 §10.2，供 plan/10 消费）。
- **依赖 `PixelEngine.Simulation` 内 CellGrid / SimulationKernel（plan/03）**：`CellGrid` 只负责只读材质/cell-type 采样与材质属性查询；相位 3/7 的写网格、current dirty 与边界 KeepAlive 一律经 `SimulationKernel.DepositCell` / `ReadAndClearCell` / `MarkDirty`。若 plan/03 未提供等价相位 API，标 `- [!] 阻塞` 并上报，不在本子系统私自绕过相位入口直写 SoA 数组。
- **依赖 `MaterialDef`（plan/04，只读）**：按 material id 查 `Density`（密度位移）与 emissive 标志（供渲染判定，本文只透传 id，不在本文做发光判定）。
- **被依赖（消费方，本文只暴露接口）**：plan/08（相位 9 CPU stamp + emissive）、plan/09（GPU point-sprite 直接映射 pinned 缓冲）、plan/12（编辑器粒子计数 + 轨迹叠层）、plan/10（音频事件）、plan/07（在飞粒子序列化/反序列化）。
- 不引入任何新 NuGet 依赖；不与 `plan/00 §4` 选型表冲突。

---

## 3. 详细设计

### 3.1 `Particle` struct（~20 字节，架构 §7.6）

AoS 紧凑值类型，字段与字节预算严格对齐架构 §7.6：

```csharp
namespace PixelEngine.Simulation.Particles;

/// <summary>离网格的自由粒子。弹道运动，飞行时不参与 CA（架构 §7.6）。</summary>
public struct Particle            // sizeof == 20（4×float + ushort + 2×byte，自然对齐到 4 的倍数）
{
    public float  X, Y;           // 世界 cell 坐标（浮点亚像素）；y 轴向下（plan/00 §7）
    public float  Vx, Vy;         // 速度，单位 cell/tick；重力沿 +Y（向下）
    public ushort Material;       // 运行时 material id（仅索引，入盘用 name，§1.8 / plan/07）
    public byte   ColorVariant;   // 色噪声种子，对齐 §7.1 colorVariant（颜色不入数据，§1.7）
    public byte   Life;           // 剩余寿命（tick），倒计时；0 触发 R13 杀死回退
}
```

- 坐标系：cell 整数坐标为权威，粒子持浮点亚像素位置；`y` 向下、重力 `Vy += g`（`g>0`，`plan/00 §7`）。
- `sizeof(Particle)` 必须恒为 20：用 xUnit + `Unsafe.SizeOf<Particle>()` 断言（见 §5），不允许编译器 padding 膨胀；如未来加字段需评审（`AGENTS.md §3`）。
- 不存 RGBA：渲染色由 material 纹理采样 + `ColorVariant` 噪声在渲染相位生成（§1.7、架构 §9.3）。

### 3.2 存储与池化：`ParticleSystem`（连续缓冲 + active-count + swap-remove）

```csharp
public sealed class ParticleSystem
{
    private readonly Particle[] _particles;   // 固定容量 pinned 连续缓冲（POH，plan/02 原语）
    private int _activeCount;                  // [0, _capacity)，活跃前缀长度
    private readonly int _capacity;

    public int ActiveCount => _activeCount;
    public int Capacity => _capacity;

    /// <summary>活跃粒子的连续视图，供并行积分与渲染只读迭代（无虚调用，架构 §7.6）。</summary>
    public Span<Particle> Active => _particles.AsSpan(0, _activeCount);
    public ReadOnlySpan<Particle> ActiveReadOnly => _particles.AsSpan(0, _activeCount);
}
```

- **为何 AoS `Particle[]` 而非 `SoaBuffer`**：架构 §7.6 明确要求「连续数组 + active-count、`Span<Particle>` 上无虚调用迭代、swap-remove」。粒子热路径是**整粒子读改写**（积分同时碰 4 个浮点 + life），AoS 单粒子落在一条 cache line 内最优；SoA 列布局（plan/02 `SoaBuffer`，服务 cell 的单字段遍历 §7.1）在此会拆散同一粒子字段、增加多流。故采用 plan/02 的 **pinned 缓冲原语**（与 `SoaBuffer` 同源的 POH/`NativeMemory` 后端）承载单条 `Particle[]`，而非 `SoaBuffer` 的列分解。pinned 还使 plan/09 可零拷贝映射为 GPU vertex stream。
- **池化 = 活跃前缀 + swap-remove，无独立 free-list**：spawn 写入 `_particles[_activeCount++]`；death/deposit 时 `_particles[i] = _particles[--_activeCount]`（swap-remove）。缓冲一次性预分配，**零 per-particle GC 分配**（架构 §7.6、`AGENTS.md §3`）。
- 容量满策略：`TrySpawn` 返回 `false`、丢弃该次抛射并计入 `DroppedThisTick` 统计；**绝不动态扩容/分配**（稳态零分配）。容量 `ParticleCapacityDefault` 默认 ≥ 262144（覆盖 §1 的 20 万上限 + 余量），可配。
- spawn API：
```csharp
public bool TrySpawn(in ParticleSpawn spawn);     // 单粒子，满则 false
```

### 3.3 弹道积分（相位 3a，并行，架构 §7.6 / §12.7）

弹道公式恒为 `X += Vx; Y += Vy; Vy += g`（架构 §7.6）。积分**飞行时绝不读写网格、绝不触发 CA**；仅写各粒子自己的槽位 → 天然 data-parallel。

- 方法：`void IntegrateAndAdvance(JobSystem jobs, CellGrid grid)`（相位 3 的子相位 a）。
- 并行：把 `[0, _activeCount)` 按 index-range 切块派发到 plan/02 `JobSystem`（持久线程池，**非 `Parallel.For`**，`AGENTS.md §3`）；每块在 `Span<Particle>` 子段上顺序迭代，消除 bounds-check（`ref`/`Unsafe.Add` 漫游，架构 §12.6）。
- 每粒子在 3a 内完成：积分新位置、`Life--`、沿「旧整数 cell → 新整数 cell」用 **DDA 整数射线步进**只读采样网格（`grid.GetCellType`），把结果归类为 `Flying` / `WantsDeposit(landingX,landingY)` / `Dead`，结果写入并行无冲突的 per-particle outcome（编码进粒子 flags 临时区或独立 `byte[] _outcome`，索引与粒子对齐）。**3a 不写网格、不 swap-remove**（避免并行结构变更竞争）。
- DDA 步进必要性：粒子可能一帧位移 >1 cell（爆炸高速），逐整数 cell 采样防穿透（tunneling）；落点取碰撞前最后一个可通行 cell。注意：自由粒子离网格、**不受 32px move cap 约束**（move cap 是 CA 网格不变式 §5.8）；穿透由 DDA 而非 move cap 处理。

### 3.4 cell→particle 抛射（相位 7，架构 §3.3 / §7.6）

把本帧爆炸/冲击的 cell 转为飞行粒子。请求在相位 1（游戏逻辑）入队，相位 7（CA 之后）执行——放 CA 之后避免刚抛出的粒子本帧又被 CA 误处理（架构 §3.3）。

```csharp
public readonly struct EjectionRequest
{
    public readonly int   CenterX, CenterY;   // 爆炸/冲击中心（cell）
    public readonly int   Radius;             // 影响半径（cell）
    public readonly float ImpulseSpeed;       // 径向基础速度（cell/tick）
    public readonly float ImpulseJitter;      // 速度随机散布（RNG）
    public readonly EjectMask Mask;           // 可被抛射的 cell-type 掩码（powder/liquid/gas/…）
}
[Flags] public enum EjectMask : byte { Powder=1, Liquid=2, Gas=4, Fire=8, Solid=16 }

public void RequestEjection(in EjectionRequest req);   // 相位 1 入队（有界队列）
public void RunEjectionPass(SimulationKernel kernel, CellGrid grid); // 相位 7 抽干并执行
```

单粒子抛射动作（架构 §7.6）：读 cell 材质 → 先从池 `TrySpawn` 预留粒子槽 → 成功后经 `SimulationKernel.ReadAndClearCell` 把源 cell 写 `Empty`（并标 dirty/KeepAlive）→ 拷材质 + `ColorVariant` → 按冲量设速度（径向方向 × `ImpulseSpeed` + RNG 抖动）。容量满时不清源 cell，避免质量丢失。`RunEjectionPass` 单线程顺序执行（写网格 + 改 active-count，避免竞争；爆炸事件有界，`ParticleEjectMaxPerTick` 封顶防尖刺）。每次抛射可向事件总线投 explosion/impact 音频事件（架构 §10.2，plan/10 消费）。

### 3.5 particle→cell 沉积（相位 3b，架构 §3.3 / §7.6）

承接 3a 标记为 `WantsDeposit` 的粒子，把它写回网格。放 CA 之前（相位 3），使新沉积像素本帧即被 CA 看见（架构 §3.3）。沉积**写网格 + swap-remove**，故单线程顺序执行（落定粒子通常远少于活跃粒子，开销低且天然无竞争）。

- 触发条件（3a 判定）：DDA 命中 solid/blocked 目标，或速度量级 `≤ ParticleDepositSpeedEpsilon`（≈0）。
- 方法：`void ResolveDeposits(SimulationKernel kernel, CellGrid grid)`。每个候选粒子在落点 `(ix,iy)`：
  - 目标 `Empty` → `kernel.DepositCell(ix,iy, material, persistentFlags)` 写回材质并唤醒 CA → swap-remove 释放粒子。
  - 目标被占（非 Empty）→ 按密度处置：若粒子材质 `Density >` 目标 cell 材质 `Density`，作密度位移（与目标 swap，遵 plan/03/§7.3 密度约定）；否则在 von Neumann 邻居中找一个 `Empty` cell 沉积；若邻居也无空位 → 见下回退。
  - **回退（R13，强制项，架构 §19）**：无处沉积时——若 `Life > 0` 则保持为短命粒子（继续飞，`Life` 已在 3a 递减）；若 `Life == 0` 则**直接杀死**（swap-remove，计入 `KilledByLifetimeThisTick`）。这条「无处沉积则杀死」+ §3.6 的硬性 max-lifetime 共同杜绝迷途粒子泄漏。
  - 落点所在 chunk 非驻留（理论上 border ring 保证驻留，架构 §3.4）→ 视为无处沉积，走回退。
- 沉积成功可投 impact 音频事件（按沉积速度选样本，架构 §10.2）。

### 3.6 生命周期与泄漏防护（架构 R13，强制项）

- 每粒子 `Life`（byte）在 3a 每 tick 递减；spawn 时初始化为 `min(materialDefaultLifetime, ParticleMaxLifetimeTicks)`。
- **硬性 max-lifetime**：`Life` 归 0 的粒子在 3a 标 `Dead`，3b（或专门 compaction）swap-remove 杀死，无条件，不依赖是否找到沉积点。
- 与 §3.5 的「无处沉积则杀死」回退共同构成 R13 双保险：任何粒子要么沉积、要么寿命到期被杀，不存在永久飞行的泄漏路径。计入 `KilledByLifetimeThisTick` 诊断以便回归监控。

### 3.7 确定性 seam（架构 §6.2）

架构 §6.2 要求确定性模式下「粒子积分用定点或固定 round 模式」。本子系统预留 seam（默认关闭、非确定高性能）：积分与 RNG 经 Core 的确定性策略开关；确定模式下重力/积分走固定 round、抖动走 counter-based RNG。seam 成本低、不启用时零开销；不在本文实现确定性数值，仅留接口位（与 §6.2 一致）。

### 3.8 渲染接口（供 plan/08 CPU stamp / plan/09 GPU，本文不实现渲染）

仅暴露只读数据，渲染逻辑全在 plan/08/09：

```csharp
public interface IParticleReadback
{
    int ActiveCount { get; }
    ReadOnlySpan<Particle> Particles { get; }   // 活跃前缀，pinned 连续，零拷贝
}
```

- 相位 9（plan/08）：CPU 按 `round(X),round(Y)` 把粒子 stamp 进 render buffer；**发光粒子另写 emissive buffer**——发光与否由 `Material` 查 `MaterialDef` 的 emissive 标志（plan/04）决定，本文只透传 `Material` 与 `ColorVariant`，不做发光判定、不做绘制（架构 §9.3）。
- plan/09：同一 pinned 缓冲可直接映射为 GPU point-sprite vertex stream（零拷贝），本文保证缓冲 pinned、布局稳定（20B/粒子）。
- 合成顺序、emissive、bloom 全在 plan/08/09；本文不引入任何 GL/渲染类型。

### 3.9 编辑器/诊断接口（供 plan/12）

```csharp
public readonly struct ParticleSystemStats
{
    public readonly int ActiveCount, Capacity;
    public readonly int SpawnedThisTick, DepositedThisTick, KilledByLifetimeThisTick, DroppedThisTick;
}
public ParticleSystemStats Stats { get; }
```

- 每 tick 更新并注册到 Core 诊断（`plan/00 §7`），供 plan/12 性能 HUD 与架构 §17.1 overlay 显示「自由粒子数」。
- 轨迹叠层（架构 §17.2「自由粒子轨迹」）：plan/12 直接消费 `ActiveReadOnly`（位置 + 速度向量）绘制叠层；本文只保证只读 span 可用，不在本文画叠层（叠层渲染属 plan/12）。

### 3.10 音频与序列化接口（供 plan/10 / plan/07，本文只投递/暴露）

- 音频：抛射（explosion/impact）与高速沉积（impact）向 Core 事件总线投粗粒度事件（架构 §10.2、§10.3），限频去重由 plan/10 负责；本文只入队。
- 序列化：暴露 `ReadOnlySpan<Particle> ActiveReadOnly`（保存在飞粒子，架构 §11.3）与 `void RestoreFrom(ReadOnlySpan<Particle> saved)`（读档重建，material id 由 plan/07 经 name↔id 重映射后传入，§1.8）。本文不实现磁盘格式。

### 3.11 帧相位集成小结

| 相位 | 动作 | 线程 | 网格读写 |
|---|---|---|---|
| [1] Game Logic | `RequestEjection` 入队爆炸/冲击 | 单/粗并行 | 无 |
| [3a] 积分推进 | `IntegrateAndAdvance`：弹道积分 + `Life--` + DDA 采样归类 | **并行(JobSystem)** | 只读采样 |
| [3b] 沉积 | `ResolveDeposits`：写回/位移/回退 + swap-remove | 单线程 | 写 + dirty |
| [4] CA | （CA 看见本帧新沉积像素） | — | — |
| [7] 抛射 | `RunEjectionPass`：读 cell→确认 spawn→写 Empty→设速 | 单线程 | 写 + dirty |
| [9] 渲染合成 | plan/08 消费 `IParticleReadback`（本文不实现） | 并行 | 无 |

---

## 4. 实现清单

- [x] 建 `PixelEngine.Simulation/Particles/` 目录与命名空间 `PixelEngine.Simulation.Particles`；项目已开 `AllowUnsafeBlocks`（plan/00 §1）。
- [x] 定义 `struct Particle`（§3.1）：字段 `float X,Y,Vx,Vy` + `ushort Material` + `byte ColorVariant` + `byte Life`；XML 中文注释；坐标系 y 向下、重力沿 +Y（plan/00 §7、架构 §7.6）。
- [x] 在 `EngineConstants`（plan/02/Core）新增：`ParticleCapacityDefault`（≥262144）、`ParticleGravityPerTick`、`ParticleDepositSpeedEpsilon`、`ParticleMaxLifetimeTicks`、`ParticleEjectMaxPerTick`（plan/00 §7 常量集中）。
- [x] 实现 `ParticleSystem`（§3.2）：用 plan/02 pinned 缓冲原语分配单条 `Particle[] _particles`（POH，零稳态分配）；`_activeCount`/`_capacity`；`Active`/`ActiveReadOnly` 返回活跃前缀 `Span`/`ReadOnlySpan`（架构 §7.6 无虚调用迭代）。
- [x] 实现 `bool TrySpawn(in ParticleSpawn)`：写 `_particles[_activeCount++]`；满则返回 `false` 且 `DroppedThisTick++`，**不扩容、不分配**。
- [x] 实现 swap-remove 释放语义（`_particles[i] = _particles[--_activeCount]`），供沉积/杀死复用，零分配。
- [x] 实现 `IntegrateAndAdvance(JobSystem, CellGrid)`（**相位 3a**，架构 §7.6/§12.7）：`X+=Vx; Y+=Vy; Vy+=g`；`Life--`；DDA 整数射线步进只读 `grid.GetCellType` 归类 `Flying`/`WantsDeposit`/`Dead`；结果写 per-particle outcome；**只写自身槽位、不写网格、不 swap-remove**。
- [x] 用 plan/02 `JobSystem` 做 `[0,_activeCount)` index-range 分区并行（**非 `Parallel.For`**，AGENTS §3）；子段内 `ref`/`Unsafe.Add` 漫游消除 bounds-check（架构 §12.6）。
- [x] 定义 `EjectionRequest` struct 与 `[Flags] EjectMask`（§3.4）；`RequestEjection`（相位 1，有界队列入队）。
- [x] 实现 `RunEjectionPass(SimulationKernel, CellGrid)`（**相位 7**，架构 §3.3/§7.6）：对半径内匹配 `Mask` 的 cell：读材质→`TrySpawn` 预留粒子槽→`SimulationKernel.ReadAndClearCell` 写 Empty + current dirty/KeepAlive→拷 material/`ColorVariant`→按径向冲量 + RNG 抖动设 `Vx,Vy`；`ParticleEjectMaxPerTick` 封顶；单线程顺序；容量满不清源 cell。
- [x] 实现 `ResolveDeposits(SimulationKernel, CellGrid)`（**相位 3b**，架构 §3.3/§7.6）：`Empty`→`SimulationKernel.DepositCell` 写回材质 + current dirty/KeepAlive + swap-remove；被占→按 `MaterialPropsTable.Density` 位移或挪相邻 `Empty`；皆不可→走 R13 回退；单线程。
- [ ] 实现 R13 回退（§3.5/§3.6，**强制项**，架构 §19）：无处沉积且 `Life==0` 杀死、`Life>0` 保持短命；并实现硬性 max-lifetime：`Life==0` 无条件 swap-remove；二者均计 `KilledByLifetimeThisTick`。
- [x] 沉积/抛射写网格处恒经 `SimulationKernel.DepositCell` / `ReadAndClearCell` 写 current dirty 与边界 KeepAlive（架构 §7.6）；落点非驻留 chunk 走回退（border ring 兜底，架构 §3.4）。
- [ ] 实现确定性 seam（§3.7，架构 §6.2）：积分/RNG 经 Core 确定性开关；默认关闭、零开销，不实现确定性数值。
- [ ] 实现 `IParticleReadback`（§3.8）：`ActiveCount` + `ReadOnlySpan<Particle> Particles`（pinned、零拷贝），供 plan/08 相位 9 与 plan/09 GPU；**本文不写任何渲染代码**。
- [ ] 实现 `ParticleSystemStats` + `Stats`（§3.9）：每 tick 更新 active/spawned/deposited/killed/dropped，注册 Core 诊断，供 plan/12 与架构 §17.1 overlay。
- [ ] 实现音频事件投递（§3.10）：抛射 explosion/impact、高速沉积 impact 写 Core 事件总线（架构 §10.2），仅入队、限频去重交 plan/10。
- [ ] 实现序列化接口（§3.10）：`ActiveReadOnly` 导出在飞粒子 + `RestoreFrom(ReadOnlySpan<Particle>)` 重建（material id 由 plan/07 重映射后传入，架构 §11.3/§1.8）。
- [x] 在 `PixelEngine.Simulation.Tests` 加测试（§5）。
- [x] 在 `PixelEngine.Benchmarks` 加粒子池化基准（BenchmarkDotNet + `[MemoryDiagnoser]`，AGENTS §3/§7），覆盖节点 1 的 `TrySpawn` + swap-remove 零分配。
- [x] 在 `PixelEngine.Benchmarks` 加粒子积分/沉积基准（BenchmarkDotNet + `[DisassemblyDiagnoser]`，AGENTS §3/§7）。

## 5. 验收标准

- [x] `Unsafe.SizeOf<Particle>() == 20`（xUnit 断言，架构 §7.6 字节预算）。
- [ ] spawn→飞行→沉积全流程：稳态帧内 **零托管堆分配**（BenchmarkDotNet `MemoryDiagnoser` 测得 Gen0/Alloc = 0，AGENTS §3）。
- [x] swap-remove 正确性：随机 spawn/kill 序列后，活跃前缀无空洞、无重复、`ActiveCount` 与实际存活数一致（性质测试）。
- [x] 弹道积分正确：`X+=Vx; Y+=Vy; Vy+=g` 逐 tick 数值符合解析弹道；飞行期间 CellGrid **逐 cell 不变**（粒子飞行不写网格、不参与 CA，架构 §7.6）。
- [x] 并行积分 = 单线程积分：同初态下 `JobSystem` 多线程积分结果与单线程逐位等价（积分无跨粒子依赖，可 bit 比对）。
- [x] cell→particle 抛射（相位 7）：源 cell 变 `Empty` 且标 dirty，粒子继承材质/色、速度方向为径向、数量受 `ParticleEjectMaxPerTick` 限制。
- [x] particle→cell 沉积（相位 3）：`Empty` 目标写回材质且 chunk 被标 dirty 唤醒 CA；被占目标按密度位移或挪相邻空 cell；落点 cell 材质 == 粒子材质。
- [x] 质量守恒（无杀死路径）：cell→particle→cell 往返后，参与材质的 cell 总数守恒（不凭空增减；与架构 §16.2 边界质量守恒精神一致）。
- [ ] **R13 无泄漏（强制）**：构造「持续抛射但无沉积空间」压力场景跑足够 tick，活跃粒子数有界收敛、不单调增长；所有迷途粒子最终被 max-lifetime 或「无处沉积则杀死」回退清除（`KilledByLifetimeThisTick` 反映）。
- [ ] 规模：5 万 / 10 万 / 20 万活跃粒子积分 + 沉积在目标机 BenchmarkDotNet 测得单 tick 成本留有 60fps 余量（架构 §7.6，数值实测为准、不预设）。
- [ ] 无虚调用迭代：`Active`/`ActiveReadOnly` 迭代反汇编无虚分派、无 bounds-check（`DOTNET_JitDisasm` 确认 `RNGCHKFAIL` 消失，架构 §12.6）。
- [ ] 渲染接口可用：plan/08 能经 `IParticleReadback` 只读取得活跃粒子并 stamp（本文侧仅验证接口暴露与 pinned 稳定，不验证渲染像素）。
- [ ] 诊断接口可用：`Stats` 各计数随 tick 正确更新并出现在 Core 诊断/编辑器 HUD（plan/12）。
- [ ] 本文所有公开 API 带完整中文 XML 注释（脚本 IntelliSense 依赖，AGENTS §4）。

## 6. 依赖关系

- **前置（必须先完成）**：plan/02（Core：pinned 缓冲/`Pool`/`JobSystem`/`EngineConstants`/RNG/诊断/事件总线）；plan/03（CellGrid：材质/cell-type 采样、SimulationKernel 相位 3/7 写网格入口、chunk 驻留与 border ring）。
- **软依赖（接口对齐，可并行推进）**：plan/04（`MaterialDef.Density` 与 emissive 标志，按 id 只读）。
- **被依赖（消费本文接口）**：plan/08（相位 9 CPU stamp + emissive）、plan/09（GPU point-sprite，零拷贝映射 pinned 缓冲）、plan/12（粒子计数 + 轨迹叠层）、plan/10（音频事件）、plan/07（在飞粒子序列化/重映射）。
- 执行顺序（plan/README）：03 → **05** → 04 → 07，渲染 08 可并行起步但其粒子合成需本文接口先定。
- 阻塞处置：若 plan/03 未提供等价相位写入 / 只读采样 API，标 `- [!] 阻塞：原因` 上报，不私自绕过 SimulationKernel 相位入口直写 SoA（AGENTS §1、§5）。

## 7. 提交节点

- [x] 节点 1：`feat(sim): 实现 Particle struct(20B) 与 ParticleSystem 连续缓冲池(swap-remove,零分配)` —— 完成 §3.1/§3.2 与对应实现清单、`sizeof==20`/零分配/swap-remove 验收项。
- [x] 节点 2：`feat(sim): 实现并行弹道积分与 cell↔particle handshake(相位3/7)` —— 完成 §3.3/§3.4/§3.5 与抛射/沉积/并行积分等价性验收项。
- 节点 3：`feat(sim): 实现粒子生命周期与 R13 无泄漏回退(max-lifetime+无处沉积则杀死)` —— 完成 §3.6 与 R13 无泄漏验收项（强制项）。
- 节点 4：`feat(sim): 暴露粒子渲染/编辑器/音频/序列化接口(IParticleReadback,Stats)` —— 完成 §3.8–§3.10 与接口可用、诊断验收项。
- 每节点完成即按 `AGENTS.md §6` 用中文 git 提交，提交正文标注对应 plan 条目与架构 §。
