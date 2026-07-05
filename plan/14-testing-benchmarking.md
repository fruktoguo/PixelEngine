# Plan 14 — 测试与基准（Testing & Benchmarking）

> 本文件定义 PixelEngine 的全部自动化验证设施：xUnit 单元 / 性质测试、BenchmarkDotNet 性能基准、以及把二者接入 CI 的门禁。权威依据：架构文档 §16.2（测试策略）、§17.3（反汇编与微基准守门）、§12.6/§12.7/§12.8（性能校验）、以及各被测子系统对应章节。开发宪法：`../AGENTS.md`（尤其 §7 测试与验证）。技术栈：`00-conventions-and-techstack.md`。
> 状态：`- [ ]` 未开始 / `- [x]` 完成 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。

---

## 1. 目标与范围

本文件交付「验证层」：四个 xUnit 测试工程（`PixelEngine.Simulation.Tests` / `PixelEngine.Physics.Tests` / `PixelEngine.Serialization.Tests` / `PixelEngine.Scripting.Tests`，见 plan/00 §5）、一个 `bench/PixelEngine.Benchmarks` BenchmarkDotNet 工程、以及在 CI 中执行它们并把性能回归当作 bug 拦截的门禁。它是 `AGENTS.md §7` 与架构 §16.2 的工程落地：CA 内核最易出 bug 的三处（chunk 边界 KeepAlive、单缓冲 parity、跨界反应）必须由性质测试钉死，性能结论必须由实测与反汇编证实而非臆断（§12.7、§17.3）。

被测对象是各 plan 已实现的子系统，本文件只负责「如何验证它们」，不重新设计任何子系统。因此本文件的范围是：测试工程的结构与命名约定、性质测试 / oracle 比对 / 快照回归的方法学、基准的测量方法学、以及 CI 门禁的判定规则与 6-RID 矩阵。每个 plan 文档自己的「验收标准」勾选项是该子系统的完成判据（`AGENTS.md §7`：每文档验收必须全勾才算完成）；本文件提供执行这些判据的自动化手段，并在 §6 给出测试条目↔各 plan 验收的映射。

明确不在本范围：子系统实现本身（在 plan 02–13）、6-RID 发行管线与 native dual-build 的构建脚本（在 plan 15，本文件只消费其产物跑 build+test）、性能优化手段本身（在 plan 16，本文件只提供度量与回归门禁）。本文件不得引入新选型，不得放宽任何不变式。

被测子系统与本文件测试设施的对应关系：CA 内核（plan 03，架构 §5/§7）→ `Simulation.Tests` 性质测试 + 内核基准；材质 / 反应 / 温度（plan 04，§7）→ `Simulation.Tests` 反应守恒与反应表测试 + 反应 cache 基准；粒子（plan 05，§7.6）→ `Simulation.Tests` handshake 测试 + 粒子积分基准；物理刚体（plan 06，§8）→ `Physics.Tests`；世界 / 流式 / 存档（plan 07，§3.4/§11）→ `Serialization.Tests` + 边界驻留测试；渲染（plan 08，§9）→ 纹理上传基准；脚本（plan 11，§17.2 + 项目引用模型）→ `Scripting.Tests`。

---

## 2. 技术栈与依赖

测试与基准框架沿用 plan/00 §4 定稿，本文件不另立选型：

- **xUnit**：`xunit`、`xunit.runner.visualstudio`、`Microsoft.NET.Test.Sdk`，承载单元 / 性质 / 快照回归测试。性质测试的随机用例生成用内核自带的 counter-based 确定性 RNG（架构 §6.2 的 RNG seam），不引入第三方 property-testing 库，以保证种子可复现、可入快照。
- **BenchmarkDotNet**：含 `[DisassemblyDiagnoser]`（守 bounds-check 消除 / SIMD 寄存器出现，§12.6/§17.3）、`[MemoryDiagnoser]`（守稳态零分配，§12.4）、`[ThreadingDiagnoser]`（核数缩放观测，§12.7）。基准工程以 Release + 发行默认运行时（CoreCLR 自包含 + R2R，§12.3）运行，绝不在 Debug 下出性能数字。
- **中央包版本管理（CPM）**：所有上述包版本集中于 `Directory.Packages.props`（plan/00 §6）。测试 / 基准工程继承 `Directory.Build.props` 的 `net10.0` / `Nullable` / `LangVersion`；但**测试工程的 `TreatWarningsAsErrors` 与零分配 / SIMD 分析器 error 提升不强加**（测试代码允许分配与 LINQ，避免把测试写法纪律与热路径纪律混淆）。
- **白盒可见性**：CA 内核的 parity 位、dirty rect、KeepAlive、chunk 驻留等内部状态需要被性质测试读取。被测工程通过 `[assembly: InternalsVisibleTo("PixelEngine.Simulation.Tests")]` 等暴露 `internal` 测试钩子（只读快照导出、确定性模式开关），**不为测试在公开 API 开后门、不暴露可变内部状态**。Demo 相关验证只走公开 API（dogfood，`AGENTS.md §0`），不属本文件四个工程。
- 依赖方向：测试 / 基准工程位于依赖图末端，可引用任意 `src/*` 工程与 `Interop`；它们绝不被 `src/*` 反向引用（plan/00 §5 依赖方向）。

---

## 3. 详细设计

### 3.1 确定性测试基座（性质测试与 oracle 的前提）

CA 实时 sim 默认非确定（架构 §6.1，多线程原地单缓冲随调度发散）。为得到可复现的测试，所有确定性断言都在内核的**确定性模式**下运行（架构 §6.2 的三处 seam：counter-based RNG、固定争用解析优先级、固定单线程扫描序）。测试基座 `DeterministicSimFixture` 负责：构造固定尺寸世界、注入固定种子、强制单线程 + 固定争用解析、推进 N 个 tick、导出**规范化快照**（仅 `Material` + 持久 `Flags`，剔除 parity / sleep 等瞬时位，架构 §11.3）。这与不变式 #6（每 tick 至多一步）一致——测试按 tick 步进，绝不追帧。

规范化快照以稳定字节序列化（material 用稳定 name 而非运行时 id，架构 §11.2，与存档同机制），落为 golden 文件存于 `tests/PixelEngine.Simulation.Tests/__golden__/`。回归测试比对当前快照与 golden 的逐 cell 等价；golden 更新需显式提交、code review 可见 diff。

### 3.2 守恒类性质测试的方法学

**质量守恒**（无反应场景，架构 §16.2、不变式覆盖 §5.4/§5.7）：构造仅含运动规则的世界（沙 / 水 / 气，无反应表项），随机扰动后推进若干 tick 至沉降，断言每材质的 cell 总数逐帧不变。关键覆盖是**跨 chunk 边界**：用例必须把活跃物质横跨 64×64 边界放置（含四角 2×2 chunk 交汇点），以捕获边界「吞像素 / 复制像素」bug（§5.5 KeepAlive、§5.8 32px halo）。守恒在 4-pass checkerboard 多线程与单线程两种调度下都须成立（总数是调度无关量，故此项可 bit 断言总数即便像素分布非确定）。

**反应质量守恒**（架构 §7.4、不变式 #4）：这是最关键项。针对双输出反应（`A+B→C+D`）与定向反应（directional flag），在 chunk 边界两侧各放一个输入，断言反应后产物计数严格守恒——**绝不允许两侧各执行一次导致物质翻倍，也不允许边界吞掉一侧导致丢失**。验证机制对应 §5.3 的 parity：反应发生时给两输入两输出打当前 parity，相邻 pass（barrier 后可见）扫到同一边界对因 parity 已等于本帧值而跳过。测试需覆盖：同 chunk 内反应、跨水平 / 垂直边界反应、四角交汇反应、概率 p=255（必发）与 0<p<255（统计期望）两档，以及 §7.4 的 datamined 例子（lava+water→rock+steam 等）的输入输出计数账本。

**确定性回归**（架构 §6.2、§16.2）：在确定性模式下，已知初态 + 固定种子 → 已知终态快照，逐 cell 比对 golden。覆盖 movement、reaction、temperature 相变各一组场景。此项保证「内核行为不被无意改动」，是重构的安全网。

### 3.3 movement 规则单测

针对架构 §5.6 的局部贪心规则，每条规则一个最小确定性场景：单柱沙一帧内自底向上坍塌成休止角堆（验证扫描序 §5.6）；水在平面找平至单层（验证液体水平铺开）；油 / 水分层后油在上（验证密度位移 §7.3，邻居 density < 我则 swap）；气体自底向上扩散并触顶（验证气体规则）。每个用例断言终态的精确 cell 布局（小网格，可逐 cell 写期望），并断言左 / 右选择逐帧交替不产生水平漂移偏置（运行偶数 tick 后质心 x 不漂）。

### 3.4 parity 防重复与 KeepAlive 边界专项

**parity 防重复**（架构 §5.3）：构造「一个 cell 在单遍扫描中可能被二次处理」的场景（如一柱自由下落沙），断言每 cell 每帧至多移动一次（通过对比「位移总量」与「活跃 cell 数 × 单步上限」，或在确定性模式下逐 cell 追踪 move 计数钩子）。同时断言 parity 是每帧翻转含义而非清零（不变式 #3）。

**KeepAlive 边界专项**（架构 §5.5，§2 标注「整个内核最易出 bug 的区域」）：独立测试类专攻边界唤醒。场景包括：雪崩在 chunk 边界处必须继续传播而非「死掉」（断言邻居 chunk 的 working dirty rect 被正确扩展、邻居被唤醒）；完全沉降后 dirty rect 必须收回、chunk 进 sleep（断言 rect 为空、sleeping 标记置位，§5.4 的 shrink）；边界写入恒在 32px halo 内（断言无写入越过 halo 抵达另一线程 chunk，§5.8）。配合 §3.6 的 border ring 驻留前提，断言跨界写入目标必驻留（架构 §3.4，不变式 #4）。

### 3.5 多线程 oracle 比对（统计性质，非 bit）

因多线程 sim 非确定（架构 §6.1），**不做 bit 比对**。oracle 是「同一初态在确定性单线程模式下推进得到的终态」；被测是「同一初态在 4-pass checkerboard 多线程下推进得到的终态」。比对的是**调度无关的统计 / 守恒性质**而非逐 cell 相等：每材质 cell 总数相等（守恒，硬断言）；无 cell 凭空出现 / 消失（总活跃质量相等）；宏观分布在容差内一致（如沿 y 轴的材质直方图、堆体高度、液面高度的相对误差 < 容差）；无边界伪影（边界列 / 行的材质计数与内部统计一致，捕获边界吞 / 复制）。该方法对应 §16.2「单线程结果作 oracle 比对统计性质」，是 R2（边界 / parity / 跨界反应 bug）的核心防线。

### 3.6 流式 / 驻留边界安全（与 KeepAlive 相邻）

针对架构 §3.4：构造「worker 在相位 4 对边界 chunk 做 KeepAlive / 移动写入，同时后台正在装 / 卸该方向 chunk」的时序，断言 border ring（驻留但默认 sleep 的 1-chunk 宽外圈）使任何 32px-halo 内跨界写入恒落在已驻留 chunk 上，结构性增删只在相位 2 单线程发生、不与 sim 相位并发。此项放在 `Simulation.Tests`（依赖 World 驻留 API），验证不变式 #4 的「border ring 保证跨界目标必驻留」。

### 3.7 physics 测试方法学

`Physics.Tests` 针对架构 §8 的管线，输入用合成 mask（圆 / 矩形 / L 形 / 带孔 / 退化细条），不依赖完整帧循环：

- **凸分解**（§8.2，PolyPartition）：对每个输入 mask，跑 marching-squares → DP → 凸分解，断言每片顶点数 ≤ 8（`B2_MAX_POLYGON_VERTICES`）、每片严格凸（叉积同号）、所有片的并集覆盖原 mask（无丢失面积，容差为 DP epsilon 量级）、且建 polygon 时 `radius=0`（锐利像素边缘，§8.2 修正）。退化输入须走三角化健壮回退而非崩溃。
- **inverse-sampling 栅格化水密性**（§8.3）：对刚体 mask 在一组角度（0°/30°/45°/任意非整数角，扫遍一圈）下做 inverse-sample 栅格化，断言结果连通、无内部空洞（对每个本应实心的 body-local 像素，其 world cell 必被填）、面积与原 mask 在 ±1px 边缘容差内一致。这直接证伪 forward sampling 的「旋转留洞」。
- **刚体破坏拆分守恒**（§8.4）：对一个刚体的 body-local mask 施加挖除使其断成 N 块，跑 CCL → 重建，断言得到 N 个新刚体、子体像素总数 = 父体剩余像素数（不丢不增）、父体线 / 角速度转移给子体、小于碎片下限的连通块转粒子而非建体（§8.2）。验证不可变 mask 作权威形状源、不被往返侵蚀（不变式 #5）。

### 3.8 serialization 测试方法学

`Serialization.Tests` 针对架构 §11，覆盖不变式 #8（material 以稳定字符串键入盘）：

- **save→load 逐 cell 等价**：构造含多材质 / 持久 flags（burning）/ lifetime / 温度子块 / 在飞粒子 / 刚体的世界，save 再 load，断言逐 cell 等价、瞬时位（parity/sleep）按规则被重置而非复原（§11.3）、粒子与刚体状态可续跑。覆盖 RLE+LZ4 压缩往返。
- **material 重映射**（§11.2，核心）：save 后修改 `materials.json`——重排材质顺序、在中间插入新材质、删除某材质——再 load 旧档，断言旧档按 name↔id 表正确重映射到当前 id，画面物质不漂移；被删材质的 cell 走声明的 fallback（如 `unknown_solid`/`Empty`）。这是 R15 的 CI 防线。
- **版本迁移链**：构造旧 `FormatVersion` 的存档字节，断言迁移链 v1→v2→…逐步升级到当前版本且语义正确；每个迁移为纯函数、可单测；name 基机制使「新增 / 重排材质」不触发迁移（仅字段布局 / 语义变更才迁移，§11.4）。

### 3.9 scripting 测试方法学

`Scripting.Tests` 针对架构 §17.2 与 plan 11 的「项目引用 + Roslyn ALC 热重载」模型：

- **热重载 unload/reload 正确**：加载脚本程序集到可回收 ALC，实例化一个 Behaviour，改源、Roslyn 重编译、卸载旧 ALC、加载新 ALC，断言新行为生效、旧实例被替换、世界状态（脚本作用的 sim 数据）在重载前后保持一致。
- **脚本异常隔离**：脚本在回调中抛异常，断言引擎主循环不崩溃、异常被捕获并上报诊断、其余子系统继续运行（库代码不吞异常但宿主隔离脚本，`AGENTS.md §4` 错误处理）。
- **ALC 可回收 / 无泄漏**：反复 load→unload 多轮，对旧 ALC 持 `WeakReference`，`GC.Collect` + 等待终结后断言 `WeakReference` 不再存活（ALC 真被回收），并断言多轮后托管堆稳态不增长（无每轮泄漏）。这验证 §17.4 的「id 稳定 + ALC 可回收」是 Unity 式迭代的基石。

### 3.10 BenchmarkDotNet 基准方法学

`bench/PixelEngine.Benchmarks`（架构 §17.3）以实测替代估算（§12.7/§12.8）。每个基准固定输入规模、报告中位数 + 分布、在 Release/R2R 下运行：

- **cells/frame 吞吐**：满激活混沌液体（最坏）与典型 dirty-rect 场景（多数静止）两档，报告活跃 cell 更新吞吐（cells/s 与每帧可处理活跃 cell 数），落实 §12.8 的量级目标为目标机实测，**不当设计保证**。
- **每核加速曲线**：固定工作集，worker 数从 1 扫到物理核数，报告加速比曲线，回填 §1.4/§12.7/§12.8（架构明令「实测替代估算」，瓶颈按延迟 + 分支而非带宽）。含活跃 chunk 少时单线程回退阈值的实测点（R7）。
- **纹理上传**：全帧 `glTexSubImage2D` + 2-PBO ping-pong vs dirty-rect 子上传 vs（可选）persistent-mapped，BGRA8 直 memcpy 路径（§9.2），报告上传耗时与 CPU 拷贝成本。
- **反应 cache-miss**：紧凑 per-material 反应列表的查表（§7.4），惰性材质早退 vs 有反应材质命中，报告 cache-miss 率（依据实测而非表字节数定档，R12）。
- **GC 停顿**：稳态帧循环基准配 `[MemoryDiagnoser]`，断言 Gen0/1/2 分配为 0（不变式 / §12.4 零分配）；并以基准对比 Workstation+Concurrent vs Server GC 的最坏停顿，供 §12.4 实测定档（不预设）。
- **粒子积分**：5万–20万活跃粒子的弹道积分 + swap-remove（§7.6），报告每帧积分耗时，证实 C# 60fps 舒适。

### 3.11 反汇编守门与 CI 门禁

**反汇编守门**（架构 §12.6/§17.3）：对最内层邻居访问、SIMD stencil 等热方法，用 `[DisassemblyDiagnoser]` 产出汇编，由一个解析守门断言 `RNGCHKFAIL`（bounds-check 跳转）不出现、且热 SIMD 方法中 ymm/zmm 寄存器出现（证实 light-up）。守门失败 = 构建失败。这把 §3「校验而非臆断」变成自动门。

**性能回归门禁**（`AGENTS.md §7`：回归即视为 bug）：每个基准维护一份 baseline（提交在仓库或 CI 缓存），CI 跑基准与 baseline 比对，超过阈值（建议中位数劣化 > 5%，按噪声实测定档）即判失败。baseline 更新须显式提交、可审查。

**6-RID build+test**（plan/00 §3、架构 §15）：CI 矩阵对 `win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64` 全部执行 build；测试在有原生 runner 的 RID（win-x64 / linux-x64 / osx-x64，及可得的 arm64 runner）上执行 `dotnet test`，其余 arm64 至少 build-verify（消费 plan 15 的 dual-build 产物）。CI 同时验证 CoreCLR/R2R（动态库）与 NativeAOT（静态库）两条路径，捕获 R5「debug 正常 publish 崩」。`TreatWarningsAsErrors` 在 CI 对 `src/*` 开启。此 6-RID **build/test 矩阵刻意保留、不随 plan/15 win-first 发行门控收敛**（cross 用 build-only）——它是 dormant RID（linux/osx 四 RID）的编译保证后盾（synthesis orderingAndDependencies 硬约束 #5、plan/16 note）；本轮 win-first 只参数化「发行资产计数」的激活集断言（见 §3.15），不缩减本矩阵。

### 3.12 破坏模型 / 武器 / 可玩循环测试方法学（demo-playability，plan/03/04/05/06/13）

被测新语义是 per-cell **Damage(byte) SoA 平面**（承 plan/03，单缓冲原地累计伤害；部分设计稿记为 Integrity lane，落地命名以 plan/03 为准）与其上的破坏 / 武器 / 可玩循环。测试分布在 `Simulation.Tests`（内核 `ApplyDamage` 与世界效果）、`Serialization.Tests`（Damage 存档往返）、`Physics.Tests`（RigidOwned 路由）、`Demo.Tests`（武器目录、可玩循环、facade）四处，全部走确定性基座与公开 API，不为破坏在热路径开后门。破坏是安全相位的**离散编辑**、不受 32px halo 约束，测试不误引不变式 #4 去约束破坏半径。

**抗性差异化破坏**（plan/04 §A.3）：同一 `DamageCircle(damage=X)` 下断言 sand/dirt（Hardness 低、MaxIntegrity 0/小）立即碎抛、stone（Hardness 高、MaxIntegrity 大）需多次累计或大当量、metal 小当量近免疫——差异全来自 materials.json 数值（改表即改结果，用固定表断言分档边界）。覆盖 `Damage += effective`、`Damage*IntegrityScale ≥ MaxIntegrity` 触发破坏、`MaxIntegrity==0` 即时破坏三条路径。

**Damage 归零转碎块 handshake**：cell 破坏时断言转 `RubbleTarget`（by name 解析守 #8）或 Empty、Damage 清 0、打本帧 parity、标 dirty、跨界 KeepAlive、按 `DebrisCount` 入粒子抛射请求、按 `MineYield` 发一次采集事件；写 cell 前必查 `CellFlags.RigidOwned`；写新 cell 时 Damage lane 归 0。

**Dispersion clamp 合规**（不变式 #4）：断言加载期 `Dispersion` 被 clamp 到 `[0, EngineConstants.MoveCap]`，液体单 CA 步水平位移 ≤ 32px；FlowRate 为 Dispersion 语义别名、不新增 lane（守 #4）。

**DamageCircle / DamageBeam 边界质量守恒**：把 `DamageCircle` / `DamageBeam` 作用区横跨 64×64 chunk 边界与 2×2 四角交汇，断言破坏产生的「Empty 化 + RubbleTarget + 碎屑粒子」总质量守恒——不在边界翻倍、不吞侧、不凭空产出（与 §3.2 守恒同机制）。`DamageBeam` 沿束逐 cell 断言按 Hardness 的**激光烧穿速率**（木 / 冰快、metal 慢 + 熔点相变），`Excavator` 断言圆形擦除的**挖掘速率**与半径。

**RigidOwned 像素破坏经 IRigidDamageSink 路由**（守 #5）：对 `CellFlags.RigidOwned` 命中的刚体像素调破坏路径，断言**绝不在刚体像素累加 Damage**，而是经 `IRigidDamageSink.OnOwnedCellDamaged` 路由触发刚体形状重建（不可变 mask 作权威源、CCL 拆分），与 §3.7 刚体破坏拆分守恒联动。

**weapons.json 加载校验**：断言 `content/weapons.json` 经引擎公开 Content/Config API（**非 Demo 直接 `JsonSerializer`**，守 `HostingProjectDisciplineTests` 纪律）反序列化为 `WeaponCatalog`、六类 kind 全字段解析、非法 / 缺字段给明确诊断、hudColor 与材质 name 引用解析。

**可玩循环胜负判定**（plan/13 §3.17，熔岩矿洞逃生）：headless 驱动 `GameDirector` 状态机，断言集齐 K=6 crystal（`MineYield` 采集事件计数）+ 抵达充能出口 → Won；玩家死亡 / 熔岩淹没通路 → Lost；分数（用时 + 剩余弹药 + 未受伤）计算正确；`RisingLavaHazard` 上涨速率数据驱动。

**Explode 语义迁移断言更新**：既有 `ExplosiveTool` / `PlayableProjectile` 的「无条件抛射半径内全部 cell」断言迁移为「破坏驱动（Empty 化 + 碎屑，抗性生效）」新语义（与 plan/05 联动），旧翻倍 / 无条件抛射断言随之改写，不留失效断言。

**Damage 平面存档往返逐 cell 等价**（plan/07 契约）：扩展 `SaveLoadRoundTripTests` 覆盖 Damage lane——按 plan/07 裁定为持久 lane 则 save→load 后逐 cell Damage 等价；bump `SaveFormatVersion` 后旧档迁移 Damage=0、material remap 缺失 fallback 后 Damage 清 0；进 `ChunkSnapshot`/`ChunkCodec`（RLE 段）往返一致。

### 3.13 standalone-editor .scene v1/v2 往返与物化测试方法学（plan/19，配合 plan/18 Hosting）

`.scene` schema 升到 `FormatVersion=2`（`EngineSceneEntityDocument` 增 `ParentId` 与 `Transform` TRS 块、字段支持 `Vector2`）。测试落 `Serialization.Tests`（文档往返）与 `Hosting.Tests`（物化 / writer / 壳冒烟）；writer 为 Hosting 新增 `SaveSceneDocument(EngineSceneDocument, path)`。

**v2 往返等价**：构造含层级（ParentId 链）、每实体 Transform（X/Y/RotationRadians/ScaleX/ScaleY）、含 Vector2 字段的 authoring 文档，`SaveSceneDocument` 写盘 → `Load` → 逐字段等价（按 StableId 升序稳定排序），读→写→读三段一致。

**v1 兼容升级**：喂 `FormatVersion=1` 旧场景字节（无 ParentId / 无 Transform），断言加载为「无 ParentId 视为根、无 Transform 用默认 TRS」，另存升级为 v2 且语义不丢；纳入 §3.8 版本迁移链（保 v1 兼容）。

**authoring→运行时物化**：断言 authoring 父子层级物化时**烘焙世界 TRS**，运行时 `Scripting.Scene` 保持**扁平 DOD**（无 live 父子变换传播、不污染热路径，此为对 plan/11 热路径的刻意取舍）；`ConvertValue` 扩展的 Vector2/MaterialId 字段绑定还原正确；authoring StableId→运行时 `Entity.Id` 映射稳定（拾取联动与快照回滚不错位）；Undo/Redo 命令栈（创建 / 删除 / 重父 / 重命名 / 复制 / 加删组件 / 改字段 / 改 TRS）往返一致。

**prefab 物化**：prefab 资产实例化、override 记录 / Revert、编辑资产传播到实例、嵌套展开与 override 传播物化正确。

**shell 短跑冒烟与 editor-window 证据迁移**：`apps/PixelEngine.Editor.Shell` 的 `--window-ticks`/scripted-probe 入口 headless / 有限 tick 跑「打开工程→Edit 装配→AttachWindow→进入 Play→退出回滚→保存场景」，产出 `editor_enabled`/`editor_running`/`editor_panels`/`editor_bridge_frames` 证据，与从 Demo `--editor` 迁移前**等价**（原 `PerformanceHardeningToolingDisciplineTests` 锁定断言与 `tools/demo-manual-acceptance-preflight.ps1` hudMenuEditorVideo scope 等价迁移到 shell 入口，不打红既有证据链）。

### 3.14 in-editor-build（PlayerBuildService）测试方法学（plan/19 §D，消费 plan/15 §3.11 契约）

`PlayerBuildService` 归 plan/19 shell 程序集（读 Hosting `EngineProject`，位于 Hosting 之上无循环）。测试针对其子进程编排与设置校验，不真起完整 publish（用 fixture NDJSON 脚本 / stub `build-player`）：

**NDJSON 解析**：喂 schema=`pixelengine.build/v1` 的逐行 NDJSON，断言解析为 `BuildProgressEvent`（Kind/Phase/Percent/Level/Message/Timestamp）、五阶段 `Native/Publish/Verify/Package/Audit/Done` 映射、半行 / 乱序 / 非 NDJSON 原始行归入当前阶段 info、stderr→error 级；`build-result.json` + exit code 合成 `BuildResult`，非 0 且无结果清单时回退末尾输出 + exit code。

**取消**：`CancellationToken` 触发 `Process.Kill(entireProcessTree:true)` 杀 dotnet/publish 子树，断言取消后无残留污染、重跑成功（半成品由 plan/15 脚本下次清理）。

**BuildTargetSettings 校验**：`Normalize()` 断言唯一启动场景、启动∈入包、至少一入包、路径 / 产物名非空、非法组合被拒并给红字提示；`PreflightAsync` 缺 .NET SDK / pwsh 时给明确可执行诊断（绝不静默）。

**audit「player 包无 Editor/ImGui」断言**：在 fixture player 产物上跑 `audit-release-artifacts`，断言 `app/` 内出现 `PixelEngine.Editor.dll` 或编辑器专属面板闭包即 fail、干净 player 包通过；**允许玩家 HUD 所需的 `Hexa.NET.ImGui`**——拒绝的是编辑器专属 `Hexa.NET.ImGuizmo*`/`ImPlot*` 与 `PixelEngine.Editor.dll`（撤销早期「拒绝 ImGui」的不可满足表述）。dev-audit（保 pdb、结构存在性 + player-only）与严格 audit-release-artifacts 分流。该断言标 **blocked-on-req1**：以 GUI 宿主中性化重构（新增 `PixelEngine.Gui` 中性 host + Hosting 删 Editor 硬引用 + `PixelEnginePlayerBuild` 剥离）落地为前置，未落地前无法转绿。

### 3.15 win-first RID 门控参数化与项目纪律测试更新（plan/15 §2.1/§3.10）

**RID 门控参数化**：`PerformanceHardeningToolingDisciplineTests` 现硬编码的 `package_count=12`/`rids`/`uploaded_asset_count=13` 期望参数化为随 `tools/release-rids.json` 单一激活集派生（Windows-only 激活 = 4 包 + `SHA256SUMS` = 5 资产；含 win-arm64 与不含两分支各断言）。此参数化**必须与 plan/15 §2.1/§3.10 门控实现同提交**（硬耦合，否则「激活集全绿」被自身单测判红）；并断言 dormant RID（linux/osx 四 RID）保留矩阵位、翻 `active:true` 即恢复的 dry-run 回归。**边界**：`ci.yml`/plan/16 与本文件 §3.11/§4.7 的 6-RID build/test 矩阵刻意保留不收敛（见 §3.11 末段）。

**HostingProjectDisciplineTests 更新**（req1 解耦，注意 Demo **无**对 Editor 的直接 ProjectReference，耦合是传递闭包）：断言 Demo 去 `using PixelEngine.Editor` 与 `--editor` 路径、Demo 对 Editor 无直接 / 传递闭包引用、`DemoProgram.cs` 改用 `PixelEngine.Gui` 中性 ImGui host（玩家 HUD 经中性 `IGuiContext`，取代经 Editor 的 `ScriptGuiContext`→`HexaImGuiBackend` 传递链）、Hosting 删除对 `PixelEngine.Editor` 的硬 `ProjectReference` 改暴露抽象 GUI/相位[10] 钩子由编辑器壳注入；维持既有「Demo 不直接 `System.Text.Json`」纪律——weapons.json / 材质内容经引擎公开 Content/Config API 加载。

### 3.16 html-ui（PixelEngine.UI）headless 测试方法学（plan/20）

`PixelEngine.UI` 后端选型（RmlUi 子集主 / Ultralight 可选 / `ManagedFallbackBackend` 纯托管基线）与不变式 #10 处置待用户拍板，故测试针对**不依赖 GL、可 headless 的层**，GL 上传 / 光栅化不在自动化断言内（措辞纠正：**不测 GL 上传**）：

**headless 布局**：对 `ManagedFallbackBackend` 基线与 RmlUi 子集契约跑布局引擎，断言盒模型 / 流式排布 / 尺寸解析确定且可复现。**data-model 绑定**：C#↔UI 桥双向绑定——数据变更驱动 UI、UI 事件（`UiEvent`）回灌脚本经相位安全队列，断言绑定值与事件路由正确。**输入三级仲裁**：断言编辑器 > HTML UI > 游戏 的输入捕获优先级（`WantCaptureMouse` / 键盘焦点仲裁），Play 让位、编辑器共存不打架。**脏矩形区域计算 / 合并**：纯几何断言脏矩形收集与合并（相邻 / 重叠区域合并、最小包围），供相位 10 仅脏 / 动画时上传——只测区域计算正确性，不触 GL。**UI 逻辑相位零分配基准**：`bench/PixelEngine.Benchmarks` 增 UI `Update(dt)`（相位 0/1）稳态零分配基准（`[MemoryDiagnoser]`），断言 sim 降 30Hz 时 UI 按渲染 cadence 推进不产生每帧托管分配（相位 10 光栅化 / 合成尖刺只掉渲染帧不违 #6，属 plan/16 覆盖）。后端选定前 RmlUi/Ultralight 专属路径测试标 blocked。

---

## 4. 实现清单

### 4.1 测试 / 基准工程骨架

- [x] 建 `tests/PixelEngine.Simulation.Tests`、`tests/PixelEngine.Physics.Tests`、`tests/PixelEngine.Serialization.Tests`、`tests/PixelEngine.Scripting.Tests` 四工程，引用 xUnit 三包，继承 `Directory.Build.props`，CPM 管版本（plan/00 §4/§6）。
- [x] 建 `bench/PixelEngine.Benchmarks`，引用 BenchmarkDotNet（含 `[DisassemblyDiagnoser]`/`[MemoryDiagnoser]`/`[ThreadingDiagnoser]`），Release/R2R 运行配置。
- [x] 各被测工程加 `InternalsVisibleTo` 暴露只读测试钩子（确定性模式开关、快照导出、dirty-rect/parity/KeepAlive 状态读取）；不暴露可变内部状态。
- [x] `DeterministicSimFixture` + 规范化快照序列化器（material 用稳定 name，剔除瞬时位）+ golden 文件目录 `__golden__/`（被测子系统：CA 内核 §6.2/§11.3）。

### 4.2 Simulation.Tests — CA 内核性质测试（§16.2，最重要）

- [x] `MassConservationTests`：无反应运动场景每材质 cell 总数逐帧不变；含跨 64×64 边界与 2×2 四角交汇用例；单线程与多线程两调度均成立（CA 内核 §5.4/§5.7，捕获边界吞 / 复制）。
- [x] `ReactionConservationTests`：双输出 / 定向反应在 chunk 边界产物计数严格守恒、不翻倍 / 不丢失；覆盖同 chunk、水平边界、垂直边界、2x2 四角交汇附近的 p=255 不翻倍 / 不丢失路径，并补齐 0<p<255 统计与 §7.4 datamined lava+water→rock+steam 账本守恒矩阵（CA 内核 §7.4、不变式 #4）。
- [x] `DeterministicRegressionTests`：确定性模式下已知初态 + 固定种子 → golden 终态逐 cell 比对，覆盖 movement/reaction/temperature 各一组（CA 内核 §6.2）。
- [x] `MovementRuleTests`：单柱沙一帧坍塌成休止角；水找平单层；油浮于水（密度位移）；气上升触顶扩散；偶数 tick 后无水平漂移偏置（CA 内核 §5.6/§7.3）。
- [x] `ParityClockTests`：每 cell 每帧至多一次移动 / 反应；parity 每帧翻转含义而非清零（CA 内核 §5.3、不变式 #3）。
- [x] `KeepAliveBoundaryTests`：边界雪崩正确跨界传播、邻居 incoming dirty 在帧边界唤醒为 current dirty 后继续传播；沉降后 rect 收回、chunk 进 sleep；写入恒在 32px halo 内不越界（CA 内核 §5.5/§5.4/§5.8、不变式 #4）。
- [x] `MultithreadOracleTests`：多线程终态 vs 单线程 oracle 比对统计 / 守恒性质（每材质总数、宏观直方图、堆 / 液面高度容差内一致、无边界伪影），非 bit 比对（CA 内核 §5.7/§6.1/§16.2）。
- [x] `ReactionTableTests`：`[tag]` 加载期展开为具体材质对正确、概率 0–100→0–255 映射边界、有序对去重（min 归一）、惰性材质 `ReactionCount==0` 早退（材质 / 反应 §7.4）。
- [x] `ResidencyBoundaryTests`：border ring 使 32px-halo 跨界写入恒落驻留 chunk；KeepAlive 唤醒 border 后补齐新的外圈 border；结构性增删只在相位 2 单线程、不与 sim 相位并发（World 驻留 §3.4、不变式 #4）。

### 4.3 Physics.Tests（§8）

- [x] `ConvexDecompositionTests`：每片顶点 ≤ 8 且严格凸、并集覆盖原 mask（容差 = DP epsilon）、`radius=0`；退化输入走三角化回退不崩（物理 §8.2）。
- [x] `MarchingSquaresContourTests`：二值 mask 边界产出像素分辨率闭合折线（16 case 正确、含孔 / 多连通）（物理 §8.2）。
- [x] `InverseSamplingRasterizationTests`：刚体 mask 在一圈角度（含任意非整数角）下 inverse-sample 栅格化水密无洞、面积 ±1px 容差（物理 §8.3）。
- [x] `RigidBodySplitConservationTests`：挖断成 N 块后 CCL→重建得 N 体、像素总数守恒、父体速度转移子体、碎片下限以下转粒子（物理 §8.4、不变式 #5）。

### 4.4 Serialization.Tests（§11）

- [x] `SaveLoadRoundTripTests`：含持久 flags/lifetime/温度子块/在飞粒子/刚体的世界 save→load 逐 cell 等价、瞬时位被重置、RLE+LZ4 往返（序列化 §11.3）。
- [x] `MaterialRemapTests`：改 `materials.json` 顺序 / 中间插入 / 删除材质后旧档按 name↔id 表正确重映射、被删材质走 fallback（序列化 §11.2、不变式 #8、R15）。
- [x] `VersionMigrationTests`：旧 `FormatVersion` 经迁移链逐步升级到当前且语义正确；每迁移为纯函数可单测；新增 / 重排材质不触发迁移（序列化 §11.4）。

### 4.5 Scripting.Tests（§17.2、plan 11 项目引用 + ALC 热重载）

- [x] `HotReloadTests`：改源 → Roslyn 重编译 → 卸旧 ALC / 加新 ALC，新行为生效、旧实例替换、世界状态一致；`HotReloadTests` 覆盖同实体 Behaviour 替换、公开字段状态恢复与旧 ALC 回收，`HotReloadServiceTests` 覆盖编译失败保留旧实例、watcher、ApplyFailed 回滚与失败后继续重载（脚本 §17.2）。
- [x] `ScriptExceptionIsolationTests`：脚本回调抛异常不崩主循环、被捕获上报诊断、其余子系统继续；独立覆盖 Behaviour.OnUpdate、ISystem.OnFrame 与 ISystem.OnSimTick 异常隔离（脚本，`AGENTS.md §4`）。
- [x] `AlcCollectibilityTests`：多轮 load→unload 后旧 ALC 的 `WeakReference` 经 GC 不再存活、托管堆稳态不增长（脚本 §17.4，无内存泄漏）。

### 4.6 Benchmarks（§17.3）

- [x] `CellThroughputBenchmark`：满激活混沌液体 + 典型 dirty-rect 两档活跃 cell 吞吐，落实 §12.8 量级（CA 内核 §12.8）。
- [x] `CoreScalingBenchmark`：worker 1→物理核数加速曲线 + 单线程回退阈值实测点，回填 §12.7；`CoreAllocationBenchmarks.JobSystemParallelRangeMultiWorker` / `JobSystemParallelRangeRawMultiWorker` 补充 JobSystem 派发热路径零分配证据（CA 内核 §12.7、R7、Box2D task bridge §14.2）。
- [x] `TextureUploadBenchmark`：全帧 PBO vs dirty-rect 子上传 vs persistent-mapped，BGRA8 直 memcpy（渲染 §9.2）。
- [x] `ReactionLookupBenchmark`：紧凑 per-material 反应列表查表 cache-miss 率、惰性早退 vs 命中（材质 / 反应 §7.4、R12）。
- [x] `GcPauseBenchmark`：稳态帧循环配 `[MemoryDiagnoser]` 断言零分配；Workstation vs Server GC 最坏停顿对比供 §12.4 定档（性能 §12.4）。
- [x] `ParticleIntegrationBenchmark`：5万–20万粒子弹道积分 + swap-remove 每帧耗时（粒子 §7.6）。

### 4.7 反汇编守门与 CI 门禁

- [x] `DisassemblyGuard`：用 `[DisassemblyDiagnoser]` 产出热方法汇编，解析断言 `RNGCHKFAIL` 不出现、热 SIMD 方法 ymm/zmm 出现，失败即构建失败（性能 §12.6/§17.3）。
- [x] 性能回归门禁：每基准维护可审查 baseline，CI 比对超阈值（建议 >5%，按噪声实测定档）即判失败（`AGENTS.md §7`）。
- [x] 6-RID 矩阵：六 RID 全 build；有 runner 的 RID 跑 `dotnet test`，其余 arm64 至少 build-verify；同时验证 CoreCLR/R2R（动态）与 NativeAOT（静态）两路径（架构 §15、R5、消费 plan 15 产物）。
- [x] CI 工作流接线：`dotnet build -c Release` → `dotnet test`（四工程）→ 反汇编守门 → 基准回归门禁；`src/*` 开 `TreatWarningsAsErrors`（plan/00 §1，与 plan 01 CI 骨架对接）。

### 4.8 demo-playability — 破坏 / 武器 / 可玩循环（plan/03/04/05/06/13）

- [ ] `CellDamageResistanceTests`（Simulation.Tests）：同 `DamageCircle` 下 sand/dirt 即碎、stone 需累计 / 大当量、metal 小当量近免疫；Damage 累加与 `MaxIntegrity` 触发、`MaxIntegrity==0` 即时破坏三路径（plan/04 §A.3）。
- [ ] `CellDamageRubbleHandshakeTests`（Simulation.Tests）：破坏转 `RubbleTarget`/Empty + Damage 清 0 + parity + dirty + 跨界 KeepAlive + `DebrisCount` 碎屑请求 + `MineYield` 采集事件；写 cell 前查 `CellFlags.RigidOwned`；写新 cell 时 Damage 归 0（plan/03/05）。
- [ ] `DispersionMoveCapTests`（Simulation.Tests）：`Dispersion` 加载期 clamp 到 `[0,MoveCap]`，液体单步水平位移 ≤ 32px（不变式 #4）。
- [ ] `WorldEffectBoundaryConservationTests`（Simulation.Tests）：`DamageCircle`/`DamageBeam` 跨 chunk 边界与四角交汇质量守恒（Empty+RubbleTarget+碎屑总量守恒，不翻倍 / 吞侧 / 凭空产出）（plan/05）。
- [ ] `DamageBeamBurnThroughTests` / `ExcavatorRateTests`（Simulation.Tests）：激光沿束按 Hardness 烧穿速率（木/冰快、metal 慢 + 熔点相变）、挖掘圆形擦除半径 / 速率（plan/13 §C）。
- [ ] `RigidOwnedDamageRoutingTests`（Physics.Tests / Simulation.Tests）：`CellFlags.RigidOwned` 像素破坏经 `IRigidDamageSink.OnOwnedCellDamaged` 路由触发形状重建、绝不在刚体像素累加 Damage（守 #5，联动 §4.3 拆分守恒）。
- [ ] `WeaponCatalogLoadTests`（Demo.Tests）：`content/weapons.json` 经引擎 Content/Config API 加载、六 kind 全字段、非法 / 缺字段诊断、hudColor / 材质 name 解析（非直接 `JsonSerializer`）。
- [ ] `GameDirectorOutcomeTests`（Demo.Tests）：集齐 K=6 crystal + 抵达出口 → Won、死亡 / 熔岩淹没 → Lost、分数计算、`RisingLavaHazard` 上涨速率数据驱动（plan/13 §3.17 熔岩矿洞逃生）。
- [ ] `ExplodeSemanticsMigrationTests`（Demo.Tests / Simulation.Tests）：`ExplosiveTool`/`PlayableProjectile` 断言从「无条件抛射」迁移到「破坏驱动 + 碎屑抗性生效」新语义，旧断言改写不留失效项（plan/05 联动）。
- [ ] `SaveLoadRoundTripTests` 扩展（Serialization.Tests）：Damage byte 平面存档往返逐 cell 等价（持久 lane，按 plan/07 契约）、`SaveFormatVersion` bump 后旧档 Damage=0 迁移、material remap 缺失 fallback 后 Damage 清 0、进 `ChunkSnapshot`/`ChunkCodec` RLE 段一致。

### 4.9 standalone-editor — .scene v1/v2 往返 / 物化 / prefab（plan/19，配合 plan/18）

- [ ] `SceneDocumentRoundTripTests`（Serialization.Tests）：`FormatVersion=2` 含 ParentId+Transform+Vector2 的 `SaveSceneDocument`→`Load` 逐字段等价、按 StableId 稳定排序、读→写→读一致。
- [ ] `SceneVersionUpgradeTests`（Serialization.Tests）：v1 旧场景加载（无 ParentId=根 / 无 Transform 默认）并升级另存 v2，纳入版本迁移链（§3.8，保 v1 兼容）。
- [ ] `SceneMaterializationTests`（Hosting.Tests）：authoring 父子层级物化烘焙世界 TRS、运行时 `Scripting.Scene` 扁平 DOD 无 live 传播、Vector2/MaterialId 字段还原、StableId→`Entity.Id` 映射稳定、Undo/Redo 命令栈往返一致。
- [ ] `PrefabMaterializationTests`（Hosting.Tests / 壳测试）：prefab 实例化 / override 记录 / Revert / 资产传播到实例 / 嵌套展开与 override 传播物化正确（plan/19 §4.10）。
- [ ] shell 冒烟证据迁移（Hosting.Tests / 壳测试）：`apps/PixelEngine.Editor.Shell` `--window-ticks`/scripted-probe 产出 `editor_enabled`/`editor_running`/`editor_panels`/`editor_bridge_frames`，等价迁移原 Demo `--editor` 证据链（§3.13）。

### 4.10 in-editor-build — PlayerBuildService（plan/19 §D，消费 plan/15 §3.11）

- [ ] `PlayerBuildNdjsonParseTests`（壳构建测试）：schema=`pixelengine.build/v1` 逐行解析为 `BuildProgressEvent`、五阶段映射、半行 / 乱序 / 非 NDJSON 归当前阶段、stderr→error、`build-result.json`+exit code 合成 `BuildResult` 及无结果回退。
- [ ] `PlayerBuildCancellationTests`（壳构建测试）：`CancellationToken`→`Process.Kill(entireProcessTree:true)` 杀子树、无残留、重跑成功。
- [ ] `BuildTargetSettingsValidationTests`（壳构建测试）：`Normalize()` 唯一启动场景 / 启动∈入包 / 至少一入包 / 路径 / 产物名非空 / 非法组合被拒；`PreflightAsync` 缺 SDK/pwsh 诊断。
- [ ] `PlayerPackageAuditTests`（build/packaging 纪律）：fixture player 产物 `app/` 含 `PixelEngine.Editor.dll` 或编辑器专属 `Hexa.NET.ImGuizmo*`/`ImPlot*` 即 fail、允许玩家 HUD 的 `Hexa.NET.ImGui`、干净包通过；dev-audit vs 严格 audit 分流。标 [!] blocked-on-req1（GUI 中性化 + Hosting 去 Editor 硬引用 + `PixelEnginePlayerBuild` 剥离前置）。

### 4.11 win-first RID 门控参数化与项目纪律更新（plan/15 §2.1/§3.10）

- [x] `PerformanceHardeningToolingDisciplineTests` / `HostingProjectDisciplineTests` 参数化：`package_count`/`rids`/`uploaded_asset_count` 随 `tools/release-rids.json` 激活集派生（当前 win-x64+win-arm64 → 4 包 + 5 资产，`-ExcludeWinArm64` → 2 包 + 3 资产），与 plan/15 门控实现闭合；dormant RID 保留位 + 翻 `active` dry-run 回归；6-RID build/test 矩阵刻意保留不收敛（§4.7 既有 6-RID 断言不缩减）。
- [x] `HostingProjectDisciplineTests` 更新：Demo 去 `using PixelEngine.Editor` 与 `--editor`、Demo 对 Editor 无直接 / 传递闭包引用、`DemoProgram` 改用 `PixelEngine.Gui` 中性 host、Hosting 删 Editor 硬 `ProjectReference` 改条件 / 抽象注入；维持 Demo 不直接 `System.Text.Json`（weapons/材质经引擎 Content/Config API）。

### 4.12 html-ui — PixelEngine.UI headless（plan/20）

- [ ] 建 `tests/PixelEngine.UI.Tests`（xUnit 三包、CPM 锁版本、继承 `Directory.Build.props`），针对不依赖 GL 的 headless 层。
- [ ] `UiLayoutTests`：`ManagedFallbackBackend` 基线 + RmlUi 子集契约的盒模型 / 流式排布 / 尺寸解析确定可复现。
- [ ] `UiDataModelBindingTests`：C#↔UI 桥双向绑定、`UiEvent` 回灌走相位安全队列。
- [ ] `UiInputArbitrationTests`：编辑器 > HTML UI > 游戏 三级输入仲裁、Play 让位、编辑器共存。
- [ ] `UiDirtyRectMergeTests`：脏矩形收集 / 合并（相邻 / 重叠合并、最小包围）纯几何正确性，**不测 GL 上传**。
- [ ] `UiLogicPhaseAllocationBenchmark`（bench）：UI `Update(dt)` 相位 0/1 稳态零分配（`[MemoryDiagnoser]`），sim 降 30Hz 时按渲染 cadence 推进不每帧分配。标 [!] blocked：后端 / #10 处置拍板前 RmlUi/Ultralight 专属路径测试待补。

---

## 5. 验收标准

- [x] 四个测试工程与基准工程建立、被 `PixelEngine.sln` 包含、CPM 锁版本、`dotnet test` 与 `dotnet run --project bench/...` 均可执行（plan/00 §4/§6）。
- [x] `MassConservationTests` 全绿：含跨 chunk 边界与四角用例，单 / 多线程均守恒，能复现并拦截人为注入的「边界吞 / 复制像素」回归（架构 §16.2、R2）。
- [x] `ReactionConservationTests` 全绿：双输出 / 定向反应在所有边界配置下产物计数严格守恒，能拦截人为注入的「边界翻倍 / 丢失」回归（架构 §7.4、不变式 #4、R2）。
- [x] `DeterministicRegressionTests` 全绿且 golden 稳定：确定性模式下重复运行 bit 一致，golden 更新有可审查 diff（架构 §6.2）。
- [x] `MovementRuleTests` 全绿：单柱沙坍塌 / 水找平 / 油浮水 / 气上升终态精确匹配、无水平漂移偏置（架构 §5.6/§7.3）。
- [x] `ParityClockTests`、`KeepAliveBoundaryTests`、`ResidencyBoundaryTests` 全绿：`ParityClockTests` 已覆盖每帧至多一次与 parity 翻转；`KeepAliveBoundaryTests` 已覆盖边界雪崩传播、dirty 收回 sleep 与 32px halo；`ResidencyBoundaryTests` 已覆盖 World border ring 驻留、KeepAlive 唤醒补外圈与相位 2 结构性摘除边界（架构 §5.3/§5.5/§5.8/§3.4、不变式 #3/#4）。
- [x] `MultithreadOracleTests` 全绿：多线程终态相对单线程 oracle 的守恒量精确相等、统计量在容差内一致，无边界伪影（架构 §16.2，非 bit 比对）。
- [x] `ReactionTableTests` 全绿：tag 展开 / 概率映射 / 去重 / 惰性早退正确（架构 §7.4）。
- [x] `ConvexDecompositionTests`、`MarchingSquaresContourTests`、`InverseSamplingRasterizationTests`、`RigidBodySplitConservationTests` 全绿：每片 ≤8 顶点且凸且覆盖原 mask、radius=0、任意角栅格化水密无洞、破坏拆分守恒且速度转移（架构 §8.2/§8.3/§8.4、不变式 #5）。
- [x] `SaveLoadRoundTripTests`、`MaterialRemapTests`、`VersionMigrationTests` 全绿：逐 cell 等价、改 materials.json 顺序 / 增删后旧档正确重映射、迁移链正确（架构 §11、不变式 #8、R15）。
- [x] `HotReloadTests`、`ScriptExceptionIsolationTests`、`AlcCollectibilityTests` 全绿：热重载行为正确、异常隔离不崩、ALC 经 GC 可回收且无泄漏（架构 §17.2/§17.4）。
- [x] 六个基准可产出报告：cells/frame、每核加速曲线、纹理上传、GC 停顿、粒子积分与反应查表延迟已有 Short 报告并已回填 §1.4/§12.7/§12.8；CI regression baseline 已覆盖 GC smoke、CA FullActive/TypicalDirtyRect、Reaction direct lookup 与 20 万粒子积分，并支持按 benchmark 参数行匹配；JobSystem `ParallelRange` / `ParallelRangeRaw` 多 worker 派发零分配见 `docs/benchmark-reports/2026-07-03-jobsystem-parallelrange-zero-allocation.md`，其余普通基准见 `docs/benchmark-reports/2026-07-02-plan14-short.md`。
- [!] 反应 cache-miss / branch-misprediction 硬件计数器报告仍需管理员 ETW Kernel Session / 专用 runner 才能产出真实 `Cache Misses` / `Branch Mispredictions` 列（架构 §12.7/§17.3，实测替代估算）。`tools/hardware-counter-preflight.ps1` 已能显式报告非管理员 ETW 阻塞并在专用 runner 检查所需列，`PerformanceHardeningToolingDisciplineTests.HardwareCounterPreflightWritesHostBoundaryReport` 已锁定当前宿主的 non-admin / non-Windows / ready 边界报告；当前本机会话不能据此勾选硬件计数器验收。
- [x] `GcPauseBenchmark` 的 `[MemoryDiagnoser]` 报告稳态帧循环 Gen0/1/2 分配为 0（架构 §12.4、零分配纪律）。
- [x] `DisassemblyGuard` 全绿：热方法无 `RNGCHKFAIL`、热 SIMD 方法出现 ymm/zmm（架构 §12.6/§17.3）。
- [x] 性能回归门禁生效：故意劣化某热方法会使 CI 判失败（`AGENTS.md §7`，回归即 bug）。
- [!] 6-RID build 全绿；可得 RID 的 `dotnet test` 全绿；CoreCLR/R2R 与 NativeAOT 两路径均验证通过；阻塞：workflow 已接线，本地无法证明 GitHub Actions 6-RID hosted runner 全绿，需真实 CI 运行结果（架构 §15、R5）。当前 `.github/workflows/ci.yml` 会上传 `ci-evidence-*` artifacts，并在 `ci-evidence` 汇总 job 中生成 manifest 后调用 `tools/ci-matrix-evidence-preflight.ps1`；缺 manifest 为 `blocked_missing_ci_manifest`，schema/JSON 错误为 `blocked_invalid_ci_evidence`，缺 RID/channel scope、hash 或 markdown 字段不匹配为 `blocked_missing_ci_scope_evidence`，`PerformanceHardeningToolingDisciplineTests.CiMatrixEvidencePreflightRejectsWinArm64TestsRanMasquerade` 已锁定 win-arm64 build-only 不能伪装成真实 arm64 测试，`PerformanceHardeningToolingDisciplineTests.CiMatrixEvidencePreflightRequiresExplicitTestsRanField` 已锁定 `testsRan` 必须显式存在而不能靠缺字段默认 false，`PerformanceHardeningToolingDisciplineTests.CiMatrixEvidencePreflightRejectsMismatchedRunIdentity` 已锁定 benchmark/build/verify 报告必须与 `workflow_run` 的 `run_id` / `sha` 同源，`PerformanceHardeningToolingDisciplineTests.CiMatrixEvidencePreflightRejectsMismatchedRunnerIdentity` 已锁定每个 RID/job 的 manifest 与 markdown `runner` 必须匹配预期 GitHub hosted runner，`PerformanceHardeningToolingDisciplineTests.CiMatrixEvidencePreflightRejectsWrongWorkflowMetadata` 已锁定 workflow-run 报告必须来自 `CI` workflow、`push`/`pull_request` 事件、有效 `run_attempt` 与分支/PR ref，避免其它 workflow 或手工拼接报告冒充 CI 矩阵证据；证据齐全也仅为 `ci_matrix_evidence_attached_pending_review`，仍需人工确认对应 GitHub Actions run 的 job 结论。
- [x] §6 的「测试条目↔各 plan 验收」映射完整：各 plan 文档「验收标准」中需自动化的条目都有本文件对应测试 / 基准支撑（`AGENTS.md §7`）；`PerformanceHardeningToolingDisciplineTests.PlanReadmeIndexesAllEvidencePreflightStatusesAsNonPassing` 已锁定 `plan/README.md` 的证据 / 预检状态索引，覆盖硬件计数器本地预检与各外部 evidence manifest 入口，确保 blocked / pending / probe-only / 本地检查状态不会被误当作验收通过。
- [ ] demo-playability 测试全绿：抗性差异化破坏分档、Damage 归零转碎块 handshake、Dispersion ≤ 32px、`DamageCircle`/`DamageBeam` 边界质量守恒、RigidOwned 经 `IRigidDamageSink` 路由不累加 Damage、weapons.json 经 Content API 加载、可玩循环胜负 / 分数、Damage 平面存档往返逐 cell 等价、Explode 语义迁移断言更新（plan/03/04/05/06/07/13）。
- [ ] standalone-editor 测试全绿：`.scene` v2（ParentId/Transform/Vector2）往返逐字段等价、v1 兼容升级、authoring→运行时物化（世界 TRS 烘焙 + 扁平 DOD）、prefab 嵌套 / override 传播、shell `--window-ticks` 证据等价迁移（plan/19/18）。
- [ ] in-editor-build 测试全绿：`PlayerBuildService` NDJSON 解析 / `build-result` 合成 / 取消杀子树无残留、`BuildTargetSettings` 校验、`Preflight` 诊断；audit「player 包 `app/` 无 `PixelEngine.Editor.dll` 与编辑器专属 ImGuizmo/ImPlot」断言（允许玩家 HUD 的 `Hexa.NET.ImGui`；标 blocked-on-req1，解耦落地后转绿）。
- [x] win-first 参数化生效：`PerformanceHardeningToolingDisciplineTests` / `HostingProjectDisciplineTests` 随激活集断言包 / 资产数（含 / 不含 win-arm64），与 plan/15 门控闭合且激活集计数不被自身单测判红；6-RID build/test 矩阵保留 dormant 编译保证不收敛；`HostingProjectDisciplineTests` 锁定 Demo 去 Editor / 用 Gui 中性 host / Hosting 去 Editor 硬引用 / Demo 不直接 STJ。外部 release workflow / 产物 / GitHub Release 证据仍归 plan/15 与 plan/17 对应阻塞项，不在本自动化参数化验收内。
- [ ] html-ui 测试全绿（headless 部分）：布局 / data-model 绑定 / 三级输入仲裁 / 脏矩形合并正确、UI 逻辑相位零分配基准；GL 上传 / 光栅化不在自动化断言内，后端 / #10 处置拍板前专属路径标 [!] blocked（plan/20）。

---

## 6. 依赖关系

本文件依赖几乎全部前置 plan，因为测试 / 基准消费各子系统的实现与公开 API：

- 强前置：plan 01（解决方案 / CI 骨架，本文件的 CI 门禁接其工作流）、plan 02（Core：确定性 RNG seam、线程池、诊断——确定性测试基座与基准的地基）。
- 被测子系统：plan 03（CA 内核，对应 §4.2 全部）、plan 04（材质 / 反应 / 温度，§4.2 反应类）、plan 05（粒子，粒子积分基准）、plan 06（物理，§4.3）、plan 07（世界 / 流式 / 存档，§4.4 + 驻留边界）、plan 08（渲染，纹理上传基准）、plan 11（脚本，§4.5）。
- plan 15（打包 / 6-RID / dual-build）：本文件的 6-RID build+test 门禁消费其产物；二者在「CI 验证两路径」上协同，本文件不重复其构建脚本。
- plan 16（性能加固）：本文件提供度量与回归门禁，plan 16 提供优化手段；plan 16 的每项加固以本文件的基准 + 反汇编守门为验收工具。
- plan 17（路线图）：每个里程碑（M0–M10）的验收依赖本文件对应测试 / 基准存在并通过；M0 即建 BenchmarkDotNet 基准与质量守恒测试（架构 §18/§19 R1/R2）。

被测↔本文件测试映射（供 `AGENTS.md §7` 逐项核对）：

- plan 01 验收 → 解决方案 / 项目引用 / CPM / `Directory.Build.props` / native 骨架 / CI 矩阵纪律由 `dotnet build PixelEngine.sln -c Release`、`dotnet test`、Demo / bench smoke、项目纪律测试与 §4.7 的 6-RID build+test 门禁覆盖。
- plan 02 → `PixelEngine.Core.Tests`（数学、内存、JobSystem、RNG / 事件 / 时钟 / 诊断，含 `ParallelRange` / `ParallelRangeRaw` 多 worker 零分配守门）+ `CoreAllocationBenchmarks` / `CoreScalingBenchmark` / `GcPauseBenchmark` + 反汇编守门。
- plan 03 → `MassConservationTests` / `KeepAliveBoundaryTests` / `ParityClockTests` / `MovementRuleTests` / `MultithreadOracleTests` / `DirtyRectLifecycleTests` / `SimulationPhaseInterfaceTests` / `SimulationProjectDisciplineTests` + `CellThroughputBenchmark` / `SimulationAllocationBenchmarks`。
- plan 04 → `MaterialTableTests` / `ReactionTableTests` / `ReactionEngineTests` / `ReactionConservationTests` / `TemperatureFieldTests` / `MaterialCustomUpdateTests` / `SimulationReactionLifetimeTests` + `ReactionAndTemperatureBenchmarks`。
- plan 05 → `ParticleSystemTests` / `ParticleLifecycleTests` / `ParticleHandshakeTests` + `ParticleIntegrationBenchmark` / `ParticleSystemAllocationBenchmarks` / `ParticleHandshakeBenchmarks`。
- plan 06 → `PixelEngine.Physics.Tests` 全部 + `PhysicsBenchmarks`。
- plan 07 → `PixelEngine.World.Tests` + `PixelEngine.Serialization.Tests` + `ResidencyBoundaryTests`。
- plan 08 → `PixelEngine.Rendering.Tests` 的渲染窗口 / 管线 / 上传 / 光照 / 粒子 / 项目纪律测试 + `TextureUploadBenchmark` / `RenderingUploadBenchmarks` / `RenderingAllocationBenchmarks` / `PaletteBgraConversionBenchmarks`。
- plan 09 → `ComputeCapabilityGateTests` / `GpuComputeCapabilityTests` / `GpuComputeDispatchGridTests` / `GpuComputeBloomPipelineTests` / `GpuRadianceCascadePipelineTests` / `GpuParticleRendererContractTests` / `GpuAirSmokePipelineTests` / `GpuComputeProfilerTests` / `RenderPipelineContractTests` / `RenderWindowIntegrationTests` 的 compute smoke 与等价性用例。
- plan 10 → `PixelEngine.Audio.Tests` 全部 + `MaterialContentLoaderTests` + `AudioDispatchBenchmarks`。
- plan 11 → `PixelEngine.Scripting.Tests` 全部 + `PixelEngine.Hosting.Tests` 中脚本相位 / 输入 / 相机 / 光照同步与 Demo runtime scripting 用例。
- plan 12 → `PixelEngine.Editor.Tests` 全部 + Hosting 的相位 / 诊断 / editor play session 相关测试，并复用 plan 07/11 的存档与脚本 Inspector 后端测试。
- plan 13 → `PixelEngine.Demo.Tests` 的 headless 场景装配 / 内容加载 / 脚本注册测试 + Demo 公开 API / 项目引用纪律检查 + §4.8 的 `WeaponCatalogLoadTests` / `GameDirectorOutcomeTests` / `ExplodeSemanticsMigrationTests`（武器库、熔岩矿洞逃生胜负、Explode 语义迁移）；真实窗口视觉、听感、手感、通关演示等人工或已标 `- [!]` 阻塞的验收不计入自动化映射缺口。
- plan 14 → 本文件 §4.1–§4.12 与 §5 自验收（测试工程、bench、反汇编守门、性能回归、CI 门禁、demo-playability / standalone-editor / in-editor-build / win-first / html-ui 增补）。
- plan 15 → §4.7 的 6-RID build+test、R2R / NativeAOT 双路径 publish smoke、trim/AOT 分析器、native 依赖与内容包加载校验消费其发行产物 + §4.10 `PlayerPackageAuditTests`（player-only audit）+ §4.11 `PerformanceHardeningToolingDisciplineTests` RID 门控参数化（随 `tools/release-rids.json` 激活集）；codesign / notarization / SHA256 发布件校验归 release CI 步骤。
- plan 16 → BenchmarkDotNet `[MemoryDiagnoser]` / `[ThreadingDiagnoser]` / `[DisassemblyDiagnoser]`、`GcPauseBenchmark`、cells/frame / 多核加速 / 纹理上传 / 反应 / 粒子 / 物理 / 音频基准、GPU capability / diagnostics contract、dirty-rect / overload / residency 边界测试与性能回归门禁共同覆盖 + §4.12 `UiLogicPhaseAllocationBenchmark`（HTML UI 逻辑相位零分配）；纯架构自审项保留为人工复核。
- plan 18 → `PixelEngine.Hosting.Tests` 的 §4.9 `SceneMaterializationTests` / prefab / shell 冒烟证据（窗口 / GL 所有权解耦、`SaveSceneDocument` writer、authoring↔运行时物化边界、editor-window 证据迁移）。
- plan 19 → §4.9 standalone-editor（`.scene` v1/v2 往返、物化、prefab、shell `--window-ticks` 证据）+ §4.10 in-editor-build（`PlayerBuildService` NDJSON / 取消、`BuildTargetSettings` 校验、player-only audit）。
- plan 20 → §4.12 `tests/PixelEngine.UI.Tests`（headless 布局 / data-model 绑定 / 三级输入仲裁 / 脏矩形合并 + UI 逻辑相位零分配基准）；GL 上传 / 光栅化 / 后端选型（#10 处置）为人工 / blocked，不计入自动化映射缺口。

本文件不被任何 `src/*` 工程反向依赖（plan/00 §5 依赖方向）。

---

## 7. 提交节点

按 `AGENTS.md §6`，每完成一个节点用中文 git 提交（type 前缀英文，scope 用 `test`/`build`）：

- [x] `test(core): 建立四测试工程+基准工程骨架与确定性测试基座`（对应 §4.1）。
- [x] `test(sim): CA 内核质量/反应守恒与 parity/KeepAlive/oracle 性质测试`（对应 §4.2）。
- [x] `test(physics): 凸分解/inverse-sampling/刚体拆分守恒测试`（对应 §4.3）。
- [x] `test(serialization): save-load 往返/材质重映射/版本迁移测试`（对应 §4.4）。
- [x] `test(script): 热重载/异常隔离/ALC 可回收测试`（对应 §4.5）。
- [x] `test(bench): cells-frame/加速曲线/上传/反应/GC/粒子基准`（对应 §4.6）。
- [x] `build(ci): 反汇编守门+性能回归门禁+6-RID build-test`（对应 §4.7）。
- [ ] `test(sim): 破坏模型(Damage lane/抗性差异/边界守恒/RigidOwned 路由/Dispersion clamp)与武器/可玩循环测试`（对应 §4.8）。
- [ ] `test(serialization): .scene v1/v2 往返与 Damage 平面存档往返逐 cell 等价测试`（对应 §4.8/§4.9）。
- [ ] `test(editor): PlayerBuildService NDJSON/取消/BuildTargetSettings 校验与 player-only audit 断言`（对应 §4.9/§4.10）。
- [ ] `test(build): RID 门控发行计数参数化 + HostingProjectDisciplineTests 解耦纪律更新`（对应 §4.11）。
- [ ] `test(ui): PixelEngine.UI headless 布局/绑定/输入仲裁/脏矩形与 UI 逻辑相位零分配基准`（对应 §4.12）。
