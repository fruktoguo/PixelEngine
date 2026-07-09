# PixelEngine 主任务目录

本目录是 PixelEngine **唯一的执行状态真相源**。`plan/00`–`plan/20` 保留子系统详细设计、历史实现清单和验收证据；它们不再承担当前任务看板职责。开始开发、选择下一项、更新状态和判断是否完成时，先读本文件，再进入对应任务轨道。

## 1. 当前结论

审计快照：`2026-07-10`，Git `179efc3a`。

- 旧计划共有 21 份编号文档、1692 个 checkbox：1498 完成、44 未开始、0 进行中、150 阻塞。
- 归并后的 canonical catalog 共 72 项：25 完成、23 未开始、0 进行中、24 阻塞；其中 4 个 `OPT-*` 为不阻塞 1.0 的可选项。
- 这些 checkbox 混合了实现任务、验收标准、重复状态摘要、外部证据、条件规则和可选后端，机械完成率 88.5% 不代表产品完成度。
- 核心引擎、M13 Editor 结构、主要 Demo/UI 自动化和本地构建测试已经形成可运行基线。
- 当前主线卡点是 CI 从未真正执行、普通窗口帧预算不达标、full-active CA 吞吐远低于目标，以及 M14 真实产品验收和 M15 发行证据尚未闭合。

## 2. 文档职责

| 层级 | 文档 | 职责 |
|---|---|---|
| 开发宪法 | `../../AGENTS.md` | 不变式、工程纪律、提交和验证规则 |
| 产品目标 | `../../docs/PixelEngine-核心目标与产品定位.md` | 产品边界和成熟度定义 |
| 技术架构 | `../../docs/PixelEngine-架构与需求设计.md` | 技术设计与架构依据 |
| 详细设计 | `../00-*.md`–`../20-*.md` | 子系统设计、历史清单、可重跑证据 |
| **执行真相源** | **本目录** | 唯一任务状态、依赖、优先级和完成条件 |
| 旧状态覆盖 | `90-legacy-coverage.md`、`source-coverage.json` | 证明旧计划没有遗漏，并把重复项映射到唯一任务 |
| 新任务模板 | `TASK-TEMPLATE.md` | 约束新增 task 的 ID、状态、依赖和验收格式 |

发生冲突时，优先级为：`AGENTS.md` / 架构不变式 > 产品目标 > 子系统详细设计 > 本任务目录。若任务与上层文档冲突，先修文档和任务定义，再写代码。

## 3. 任务状态规则

每个任务 ID 在整个目录中只能出现一次状态 checkbox：

- `[ ]`：尚未开始；是否已 ready 由任务依赖决定，不能仅凭状态判断可立即执行。
- `[~]`：正在执行；全目录同一时刻原则上只有一个主任务为此状态。
- `[!]`：被明确依赖、外部设备、人工 reviewer、凭据或架构决策阻塞；必须写明解除条件。
- `[x]`：实现、测试、验收和要求的证据全部完成。

任务正文中的验收条件使用普通列表，不再创建第二组 checkbox。`pending_review`、`local_probe_only`、`scripted_probe_only`、`process_smoke_only`、`ready`、`counters_present` 均不是完成态。

## 4. 轨道目录

| 轨道 | 文档 | 当前职责 |
|---|---|---|
| 完成基线 | `10-completed-baseline.md` | 归并 1498 个历史 `[x]`，明确哪些能力可依赖 |
| 范围决策 | `20-scope-decisions.md` | 统一 Windows-first、ComputeSharp、Ultralight、M14/M15 口径 |
| 正确性与架构 | `30-correctness-architecture.md` | 修复边界泄漏、故障传播和公开 API 风险 |
| 性能 | `40-performance.md` | render-buffer、CA、零分配、流式和目标硬件证据 |
| 产品验收与文档 | `50-product-editor-ui-demo.md` | Editor、透明 UI、IME、Demo 真实体验与入门文档 |
| 验证与发行 | `60-validation-release.md` | CI、native smoke、coverage、泄漏、玩家包和 Release |
| 证据合同 | `70-evidence-contracts.md` | 9 类 preflight 的 manifest、scope、阈值和非完成状态 |
| 旧计划覆盖 | `90-legacy-coverage.md` | 旧 checkbox 到 canonical task 的完整映射 |

## 5. 主执行顺序

| 阶段 | 目标 | 必做任务（按依赖优先） |
|---|---|---|
| A | 建立可信控制面 | `PLAN-001`、`EVID-001`、`EVID-003`、`CI-001`、`TEST-001`、`CI-002`、`TEST-002`、`PERF-001` |
| B | M14 正确性与性能加固 | `ARCH-001`–`ARCH-005`、`PERF-002`–`PERF-007`、`PERF-011`、`DOC-002` |
| C | 冻结可验收候选版本 | `REL-001` |
| D | 闭合 M15 产品与目标环境证据 | `PERF-008`–`PERF-010`、`PERF-012`、`EVID-002`、`EDITOR-001`–`EDITOR-003`、`UI-001`–`UI-003`、`DEMO-001`–`DEMO-005`、`DOC-001`、`CI-003`、`REL-004`、`REL-005` |
| E | 确定性打包与正式发行 | `REL-002`、`REL-003` |

M14 的 required implementation/automation 基线由 `BASE-013`–`BASE-015` 记录，阶段 B 负责修正审计发现后再冻结候选版本。真实窗口 reviewer、目标硬件、native leak、远端矩阵和 tag Release 均属于 M15，不能反向阻塞 M14 的能力口径。

阶段间按 A→B→C→D→E 推进；阶段内只有依赖满足的任务才 ready，可安全并行。`source-coverage.json.requiredExecutionStages` 机器校验 44 个必做任务无遗漏、无重复；可选 `OPT-*` 不阻塞 Windows-first 1.0。

## 6. 完成定义

一个任务只有同时满足以下条件才能改为 `[x]`：

- 任务范围内的实现和文档同步完成，没有 stub、假实现或 TODO-later 冒充结果。
- 所列自动化测试通过；性能敏感任务有可复现 benchmark，而非单次 Dry 数据。
- 需要人工、真实窗口、目标硬件或远端服务的任务，已附当前 commit 同源证据。
- 证据位于可长期保留的位置；临时 `scratch/`、被清理的 `artifacts/` 路径不能作为唯一依据。
- 完成对应提交节点并使用中文提交；提交正文写明 canonical task ID 和详细设计来源。

## 7. 维护流程

1. 从本文件按顺序选择首个未完成且依赖已满足的任务。
2. 把该任务唯一状态改为 `[~]`，不要在旧计划中新增并行状态 checkbox。
3. 实现时按任务的“设计来源”读取 `plan/00`–`plan/20` 对应章节。
4. 验证通过后记录稳定证据路径，把任务改为 `[x]`，同步必要的详细设计。
5. 运行任务目录校验：

```pwsh
pwsh tools/validate-task-catalog.ps1
```

6. 按 `AGENTS.md §6` 提交；推荐正文追加：

```text
对应任务: PERF-002
设计依据: plan/08-rendering.md §相关章节；plan/16-performance-hardening.md
```

## 8. 当前验证基线

- `dotnet build PixelEngine.sln -c Release --disable-build-servers -m:1`：32/32 项目成功，0 warning / 0 error。
- 13 个测试项目：1492 passed / 0 failed / 0 skipped。
- Editor Shell 当前 HEAD 40 帧短跑成功；Demo 当前 HEAD 80 帧短跑成功。
- 上述结果是 Windows 本地基线，不替代 `CI-002`、`CI-003`、目标硬件或人工验收。

## 9. Evidence 索引

- 稳定证据索引：[`docs/evidence-index.md`](../../docs/evidence-index.md)；机器可读版本为 [`docs/evidence-index.json`](../../docs/evidence-index.json)。
- 索引校验：`pwsh tools/validate-evidence-index.ps1`。该校验会重算报告 SHA256、拒绝 volatile output 路径，并要求历史报告显式声明未记录的 run/session identity。
- Windows-first 目标矩阵：[`tools/target-hardware-matrix.json`](../../tools/target-hardware-matrix.json) 与 [`docs/target-hardware-matrix.md`](../../docs/target-hardware-matrix.md)；一致性校验：`pwsh tools/validate-target-hardware-matrix.ps1`。
