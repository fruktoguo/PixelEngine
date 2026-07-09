# 验证、证据与发行任务

本轨道负责把“本地代码能跑”升级为“当前 commit 可持续验证和发行”。外部证据必须绑定同一 commit/run，不能由不同时间的报告拼接。

## P0：恢复 CI

- [ ] `CI-001` 修复 `.github/workflows/ci.yml` 第 68、73 行非法 `shell: ${{ runner.os ... }}` context，使 workflow 能创建实际 jobs。
  - 优先级：P0。
  - 依赖：无。
  - 设计来源：`plan/01-project-setup.md`；`plan/14-testing-benchmarking.md`。
  - 验收：`actionlint`/GitHub validator 通过；push 后 workflow 不再 0-job；修复不能缩减计划矩阵来规避错误。

- [!] `CI-002` 取得当前 HEAD 的 Windows build/test/benchmark guard 首次远端全绿。阻塞：`CI-001` 未完成，且需要明确 push。
  - 优先级：P0。
  - 验收：win-x64 build、1492+ tests、disassembly guard、benchmark regression 实际执行；artifact/report 绑定 run id 和 commit SHA。

- [!] `CI-003` 取得长期 6-RID build/test 与 4-RID verify-publish 矩阵证据。阻塞：`CI-002` 未完成，且需要对应 hosted runner/native 构建可用。
  - 优先级：P1。
  - 验收：矩阵与 `SCOPE-001` 一致；win-arm64 build-only 不伪装测试；manifest/hash/runner identity 通过 preflight 和人工复核。

## 测试质量

- [ ] `TEST-001` 把 GL/ANGLE/native smoke 从“环境变量未开即返回并计为通过”改为显式 skipped/trait/job，并在 CI 中至少有一个真实执行 job。
  - 优先级：P1。
  - 依赖：`CI-001`。
  - 验收：普通单测报告能区分未执行；native-smoke job 开启要求的环境并报告真实用例数；失败不能静默降级成 pass。

- [ ] `TEST-002` 建立 coverage 收集、报告和最低阈值，区分行为测试与源码纪律测试。
  - 优先级：P2。
  - 依赖：`CI-002`。
  - 验收：覆盖率按 src 程序集发布；阈值可审查；不以 166 个源码字符串断言掩盖运行时路径缺口。

## 证据可追溯性

- [x] `EVID-001` 建立稳定 evidence index，记录 task ID、commit、run/session id、硬件、命令、报告路径和 SHA256。
  - 优先级：P1。
  - 依赖：`PLAN-001`。
  - 设计来源：`plan/14-testing-benchmarking.md` §3.10–§3.11、§5–§6；`plan/15-build-packaging-distribution.md` §1、§5–§6；`plan/17-roadmap-execution-order.md` §2、§4；`plan/tasks/70-evidence-contracts.md`。
  - 验收：已完成任务不再只引用会被清理的 `scratch/`/临时 `artifacts/`；历史报告明确标记旧 commit；preflight 输出可追踪到原始材料。
  - 证据：`docs/evidence-index.json`、`docs/evidence-index.md`、`tools/validate-evidence-index.ps1`；校验输出 `Evidence index valid: 17 entries, 45 referenced task IDs.`；`TaskEvidenceCatalogIndexesAllEvidencePreflightStatusesAsNonPassing` 通过 1/1。

- [!] `EVID-002` 完成 GL/OpenAL/Box2D/ALC 的外部 native leak detector。阻塞：需要冻结候选 commit、detector 环境和四类同源报告。
  - 优先级：P1。
  - 验收：shutdown 后对应 live-object/ALC 计数为 0；不是 process smoke；manifest/hash/commit 同源并经人工复核。

- [ ] `EVID-003` 固定 Windows-first baseline 与长期兼容矩阵使用的目标硬件/runner 清单。
  - 优先级：P1。
  - 依赖：`SCOPE-001`。
  - 验收：每个 required/conditional RID 明确 CPU、GPU、OS、driver、runner、管理员/签名权限和可执行的 benchmark/smoke；性能与发行任务只引用该清单，不再写模糊“目标硬件”。

## 玩家包与 Release

- [!] `REL-001` 从当前候选 HEAD 重建 Editor 和 win-x64 R2R/AOT 玩家包，替换当前绑定旧 `85e1914d` 的最终输出。阻塞：`CI-002`、`ARCH-001`–`ARCH-005`、`PERF-002`–`PERF-007` 尚未完成；不等待 M15 人工体验任务。
  - 优先级：P1。
  - 验收：build-result、launcher、content、NOTICE、SHA256、player-only audit、窗口 smoke 全部通过；manifest 绑定当前 HEAD。

- [!] `REL-002` 完成 active RID × channel 的确定性 package/hash/audit。阻塞：`REL-001`、`REL-005` 未完成。
  - 优先级：P1。
  - 验收：同输入两次产物 hash 一致；R2R 动态 Box2D、AOT 静态 Box2D、UI native gate 和 player-only 规则全部验证。

- [!] `REL-003` 完成 tag-triggered GitHub Release、资产上传和下载复核。阻塞：`CI-003`、`REL-002` 未完成，且需要 release tag/权限。
  - 优先级：P1。
  - 验收：tag/ref/version 一致；上传资产数、SHA256SUMS、browser download URL 和重新下载 hash 全部可验证。

- [!] `REL-004` 完成 win-arm64 active set 真机或可信 runner 的运行验证。阻塞：需要 win-arm64 设备/runner；若产品决策取消激活，应同步修改 `SCOPE-001` 后转为 deferred。
  - 优先级：P2。
  - 验收：不是 load-only；玩家包启动、native 加载、基本路线和退出均成功，报告绑定 release commit。

- [!] `REL-005` 闭合 active RID 的 Box2D dynamic/static 和 UI-native 发行边界。阻塞：`REL-001`、`PERF-012` 未完成。
  - 优先级：P1。
  - 验收：R2R 只携带动态 Box2D，AOT 静态链入且拒绝动态 Box2D；RmlUi native provenance/hash/NOTICE 正确；AOT 和 native 不可用时 ManagedFallback 可运行；inactive Ultralight 不得混入包。

## 本次文档迁移

- [x] `PLAN-001` 建立 canonical task 目录、迁移旧 checkbox、更新入口与开发规范，并用自动校验保证覆盖完整。
  - 优先级：P0。
  - 依赖：无。
  - 验收：任务 ID 唯一；旧 21 份计划计数和映射完整；README/AGENTS/plan17 指向本目录；validator 全绿；无旧 checkbox 被删除而失去追溯。
