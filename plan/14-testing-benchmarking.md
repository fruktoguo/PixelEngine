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

**6-RID build+test**（plan/00 §3、架构 §15）：CI 矩阵对 `win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64` 全部执行 build；测试在有原生 runner 的 RID（win-x64 / linux-x64 / osx-x64，及可得的 arm64 runner）上执行 `dotnet test`，其余 arm64 至少 build-verify（消费 plan 15 的 dual-build 产物）。CI 同时验证 CoreCLR/R2R（动态库）与 NativeAOT（静态库）两条路径，捕获 R5「debug 正常 publish 崩」。`TreatWarningsAsErrors` 在 CI 对 `src/*` 开启。

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

- [ ] `SaveLoadRoundTripTests`：含持久 flags/lifetime/温度子块/在飞粒子/刚体的世界 save→load 逐 cell 等价、瞬时位被重置、RLE+LZ4 往返（序列化 §11.3）。
- [ ] `MaterialRemapTests`：改 `materials.json` 顺序 / 中间插入 / 删除材质后旧档按 name↔id 表正确重映射、被删材质走 fallback（序列化 §11.2、不变式 #8、R15）。
- [ ] `VersionMigrationTests`：旧 `FormatVersion` 经迁移链逐步升级到当前且语义正确；每迁移为纯函数可单测；新增 / 重排材质不触发迁移（序列化 §11.4）。

### 4.5 Scripting.Tests（§17.2、plan 11 项目引用 + ALC 热重载）

- [x] `HotReloadTests`：改源 → Roslyn 重编译 → 卸旧 ALC / 加新 ALC，新行为生效、旧实例替换、世界状态一致；`HotReloadTests` 覆盖同实体 Behaviour 替换、公开字段状态恢复与旧 ALC 回收，`HotReloadServiceTests` 覆盖编译失败保留旧实例、watcher、ApplyFailed 回滚与失败后继续重载（脚本 §17.2）。
- [x] `ScriptExceptionIsolationTests`：脚本回调抛异常不崩主循环、被捕获上报诊断、其余子系统继续；独立覆盖 Behaviour.OnUpdate、ISystem.OnFrame 与 ISystem.OnSimTick 异常隔离（脚本，`AGENTS.md §4`）。
- [x] `AlcCollectibilityTests`：多轮 load→unload 后旧 ALC 的 `WeakReference` 经 GC 不再存活、托管堆稳态不增长（脚本 §17.4，无内存泄漏）。

### 4.6 Benchmarks（§17.3）

- [ ] `CellThroughputBenchmark`：满激活混沌液体 + 典型 dirty-rect 两档活跃 cell 吞吐，落实 §12.8 量级（CA 内核 §12.8）。
- [ ] `CoreScalingBenchmark`：worker 1→物理核数加速曲线 + 单线程回退阈值实测点，回填 §12.7（CA 内核 §12.7、R7）。
- [ ] `TextureUploadBenchmark`：全帧 PBO vs dirty-rect 子上传 vs persistent-mapped，BGRA8 直 memcpy（渲染 §9.2）。
- [ ] `ReactionLookupBenchmark`：紧凑 per-material 反应列表查表 cache-miss 率、惰性早退 vs 命中（材质 / 反应 §7.4、R12）。
- [ ] `GcPauseBenchmark`：稳态帧循环配 `[MemoryDiagnoser]` 断言零分配；Workstation vs Server GC 最坏停顿对比供 §12.4 定档（性能 §12.4）。
- [ ] `ParticleIntegrationBenchmark`：5万–20万粒子弹道积分 + swap-remove 每帧耗时（粒子 §7.6）。

### 4.7 反汇编守门与 CI 门禁

- [ ] `DisassemblyGuard`：用 `[DisassemblyDiagnoser]` 产出热方法汇编，解析断言 `RNGCHKFAIL` 不出现、热 SIMD 方法 ymm/zmm 出现，失败即构建失败（性能 §12.6/§17.3）。
- [ ] 性能回归门禁：每基准维护可审查 baseline，CI 比对超阈值（建议 >5%，按噪声实测定档）即判失败（`AGENTS.md §7`）。
- [ ] 6-RID 矩阵：六 RID 全 build；有 runner 的 RID 跑 `dotnet test`，其余 arm64 至少 build-verify；同时验证 CoreCLR/R2R（动态）与 NativeAOT（静态）两路径（架构 §15、R5、消费 plan 15 产物）。
- [ ] CI 工作流接线：`dotnet build -c Release` → `dotnet test`（四工程）→ 反汇编守门 → 基准回归门禁；`src/*` 开 `TreatWarningsAsErrors`（plan/00 §1，与 plan 01 CI 骨架对接）。

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
- [ ] `SaveLoadRoundTripTests`、`MaterialRemapTests`、`VersionMigrationTests` 全绿：逐 cell 等价、改 materials.json 顺序 / 增删后旧档正确重映射、迁移链正确（架构 §11、不变式 #8、R15）。
- [x] `HotReloadTests`、`ScriptExceptionIsolationTests`、`AlcCollectibilityTests` 全绿：热重载行为正确、异常隔离不崩、ALC 经 GC 可回收且无泄漏（架构 §17.2/§17.4）。
- [ ] 六个基准可产出报告：cells/frame、每核加速曲线、纹理上传、反应 cache-miss、GC 停顿、粒子积分均有实测数据，并已回填 §1.4/§12.7/§12.8 的目标（架构 §12.7/§17.3，实测替代估算）。
- [ ] `GcPauseBenchmark` 的 `[MemoryDiagnoser]` 报告稳态帧循环 Gen0/1/2 分配为 0（架构 §12.4、零分配纪律）。
- [ ] `DisassemblyGuard` 全绿：热方法无 `RNGCHKFAIL`、热 SIMD 方法出现 ymm/zmm（架构 §12.6/§17.3）。
- [ ] 性能回归门禁生效：故意劣化某热方法会使 CI 判失败（`AGENTS.md §7`，回归即 bug）。
- [ ] 6-RID build 全绿；可得 RID 的 `dotnet test` 全绿；CoreCLR/R2R 与 NativeAOT 两路径均验证通过（架构 §15、R5）。
- [ ] §6 的「测试条目↔各 plan 验收」映射完整：各 plan 文档「验收标准」中需自动化的条目都有本文件对应测试 / 基准支撑（`AGENTS.md §7`）。

---

## 6. 依赖关系

本文件依赖几乎全部前置 plan，因为测试 / 基准消费各子系统的实现与公开 API：

- 强前置：plan 01（解决方案 / CI 骨架，本文件的 CI 门禁接其工作流）、plan 02（Core：确定性 RNG seam、线程池、诊断——确定性测试基座与基准的地基）。
- 被测子系统：plan 03（CA 内核，对应 §4.2 全部）、plan 04（材质 / 反应 / 温度，§4.2 反应类）、plan 05（粒子，粒子积分基准）、plan 06（物理，§4.3）、plan 07（世界 / 流式 / 存档，§4.4 + 驻留边界）、plan 08（渲染，纹理上传基准）、plan 11（脚本，§4.5）。
- plan 15（打包 / 6-RID / dual-build）：本文件的 6-RID build+test 门禁消费其产物；二者在「CI 验证两路径」上协同，本文件不重复其构建脚本。
- plan 16（性能加固）：本文件提供度量与回归门禁，plan 16 提供优化手段；plan 16 的每项加固以本文件的基准 + 反汇编守门为验收工具。
- plan 17（路线图）：每个里程碑（M0–M10）的验收依赖本文件对应测试 / 基准存在并通过；M0 即建 BenchmarkDotNet 基准与质量守恒测试（架构 §18/§19 R1/R2）。

被测↔本文件测试映射（供 `AGENTS.md §7` 逐项核对）：plan 03 验收 → `MassConservationTests`/`KeepAliveBoundaryTests`/`ParityClockTests`/`MovementRuleTests`/`MultithreadOracleTests` + 内核基准；plan 04 → `ReactionConservationTests`/`ReactionTableTests` + 反应基准；plan 05 → 粒子积分基准 + handshake 用例；plan 06 → `Physics.Tests` 全部；plan 07 → `Serialization.Tests` 全部 + `ResidencyBoundaryTests`；plan 08 → 纹理上传基准；plan 11 → `Scripting.Tests` 全部。

本文件不被任何 `src/*` 工程反向依赖（plan/00 §5 依赖方向）。

---

## 7. 提交节点

按 `AGENTS.md §6`，每完成一个节点用中文 git 提交（type 前缀英文，scope 用 `test`/`build`）：

- [x] `test(core): 建立四测试工程+基准工程骨架与确定性测试基座`（对应 §4.1）。
- [x] `test(sim): CA 内核质量/反应守恒与 parity/KeepAlive/oracle 性质测试`（对应 §4.2）。
- [x] `test(physics): 凸分解/inverse-sampling/刚体拆分守恒测试`（对应 §4.3）。
- [ ] `test(serialization): save-load 往返/材质重映射/版本迁移测试`（对应 §4.4）。
- [x] `test(script): 热重载/异常隔离/ALC 可回收测试`（对应 §4.5）。
- [ ] `test(bench): cells-frame/加速曲线/上传/反应/GC/粒子基准`（对应 §4.6）。
- [ ] `build(ci): 反汇编守门+性能回归门禁+6-RID build-test`（对应 §4.7）。
