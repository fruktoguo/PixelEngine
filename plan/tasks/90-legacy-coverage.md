# 旧计划 Checkbox 覆盖表

本文件说明如何把 `plan/00`–`plan/20` 的 1692 个历史 checkbox 迁移到 canonical task。原 checkbox 不删除，继续保存设计上下文和历史证据；从本次迁移起，它们不再更新 live 状态。

## 覆盖原则

- 旧 `[x]` 归并到一个或多个 `BASE-*`/`SCOPE-*` 能力包，不逐条复制测试历史。
- 旧 `[ ]`/`[!]` 按文档和职责归并到 current task family、`OPT-*` deferred backlog，或转为无状态治理规则。
- 一个 current task 可以覆盖多个旧 checkbox；同一债务不能在多个 current task 中重复拥有状态。
- 旧计划里“若未来……”的条件语句、preflight 状态词定义和总状态摘要不再是任务。
- 详细机器快照和 task-family 关系在 `source-coverage.json`。它记录每份旧文档有序 checkbox 原文的 SHA256，而不是伪造一对一语义归属；`tools/validate-task-catalog.ps1` 会检测内容/数量漂移、未知 ID、重复 ID 和 44 项必做序列遗漏。

## 审计快照

| 旧计划 | 完成 | 未开始 | 进行中 | 阻塞 | Canonical 覆盖重点 |
|---|---:|---:|---:|---:|---|
| 00 技术栈 | 23 | 2 | 0 | 7 | `BASE-001`、`SCOPE-*`、`EVID-003`、optional/RID |
| 01 工程骨架 | 91 | 0 | 0 | 6 | `BASE-001`、`CI-*`、`REL-*` |
| 02 Core | 87 | 2 | 0 | 6 | `BASE-002`、`ARCH-001`、硬件/GC/SIMD |
| 03 Simulation | 124 | 2 | 0 | 6 | `BASE-003`、`ARCH-002`、`PERF-003/004/008/009` |
| 04 材质/温度 | 97 | 2 | 0 | 6 | `BASE-004`、`PERF-011`、Demo 产品验收 |
| 05 粒子 | 69 | 2 | 0 | 6 | `BASE-005`、`PERF-010`、Demo 粒子验收 |
| 06 Physics | 111 | 0 | 0 | 0 | `BASE-006`；新增审计任务 `ARCH-003`、`PERF-005` |
| 07 World | 73 | 0 | 0 | 0 | `BASE-007`；新增审计任务 `PERF-006` |
| 08 Rendering | 101 | 0 | 0 | 0 | `BASE-008`；新增审计任务 `PERF-002` |
| 09 GPU | 43 | 0 | 0 | 2 | GL baseline、`OPT-001`、`PERF-010` |
| 10 Audio | 50 | 0 | 0 | 0 | `BASE-009`、Demo 听感、泄漏/预算 |
| 11 Scripting | 94 | 0 | 0 | 0 | `BASE-011`、`ARCH-004`、`DEMO-005` |
| 12 Editor 面板 | 96 | 0 | 0 | 0 | `BASE-012`、`EDITOR-*` |
| 13 Demo | 42 | 0 | 0 | 22 | `BASE-015`、`SCOPE-006`、`DEMO-*`；代码完成与人工验收拆分 |
| 14 测试/基准 | 102 | 0 | 0 | 2 | `BASE-016`、`CI-*`、`TEST-*`、性能/证据 |
| 15 发行 | 49 | 13 | 0 | 23 | `BASE-017`、`SCOPE-001/003`、`REL-*`、optional RID/backend |
| 16 性能 | 49 | 14 | 0 | 27 | `PERF-001`–`PERF-012`、`EVID-003` |
| 17 路线图 | 14 | 1 | 0 | 3 | 历史里程碑映射；live gate 迁移到本目录 |
| 18 Hosting | 40 | 0 | 0 | 9 | `BASE-010`、Editor/泄漏/帧预算 |
| 19 Editor Shell | 77 | 3 | 0 | 9 | `BASE-013`、`EDITOR-*`、IME、文档、当前制品 |
| 20 HTML UI | 66 | 3 | 0 | 16 | `BASE-014`、`UI-*`、`OPT-002/003`、UI native release |
| **合计** | **1498** | **44** | **0** | **150** | **1692 个历史 checkbox 全部保留并按文档归并** |

## 被转为治理规则的旧条目

以下内容继续有效，但不再占用任务状态：

- 新增依赖/native/RID 时必须同步评审和更新矩阵。
- `pending_review`、`local_probe_only`、`scripted_probe_only`、`process_smoke_only` 的含义和禁止冒充完成规则。
- dormant RID/profile 激活时必须同时更新真相源、CI、release、许可和证据。
- 完成一个 canonical task 后立即中文提交，不积攒跨节点修改。
- 证据必须绑定当前 commit/run/session，hash 可复核且保存在稳定路径。

## 被新口径取代的历史声明

- “解决方案恰好 18 个项目”已被当前 32 项目结构取代。
- “CI workflow 已完成”只保留为文件已编写；实际可执行状态由 `CI-001`/`CI-002` 管理。
- Demo 的“引水成桥/坍塌木桥”旧产品路线被 `SCOPE-006` 的横向熔岩逃生路线取代，但仍可保留为机制测试。
- ComputeSharp/Ultralight/macOS dormant release 不再作为 Windows-first 1.0 的硬阻塞。
- M13 表示自动化结构闭合，不等于 Editor 人工 UX 已完成。

## 映射边界

`completedCapabilityIds` 表示一份旧计划的完成项可由哪些基线能力包概括，`followUpTaskIds` 表示与该文档剩余债务或本轮审计发现相关的任务；二者都不是逐条 checkbox 所有权。完全不依附旧计划、只服务新目录治理的 `EVID-001` 和 `PLAN-001` 列在 `catalogOnlyTaskIds`。全部必做任务另由 `requiredExecutionStages` 校验，避免“有 task 但不在主路径”的遗漏。
