# 验证、证据与发行任务

本轨道负责把“本地代码能跑”升级为“当前 commit 可持续验证和发行”。外部证据必须绑定同一 commit/run，不能由不同时间的报告拼接。

## P0：恢复 CI

- [x] `CI-001` 修复 `.github/workflows/ci.yml` 第 68、73 行非法 `shell: ${{ runner.os ... }}` context，使 workflow 能创建实际 jobs。
  - 优先级：P0。
  - 依赖：无。
  - 设计来源：`plan/01-project-setup.md`；`plan/14-testing-benchmarking.md`。
  - 验收：`actionlint`/GitHub validator 通过；push 后 workflow 不再 0-job；修复不能缩减计划矩阵来规避错误。
  - 证据：`.github/workflows/ci.yml` 已改为 Windows/non-Windows 静态 shell steps；PyYAML 解析通过；`go run github.com/rhysd/actionlint/cmd/actionlint@v1.7.12 .github/workflows/ci.yml` 通过；静态矩阵保持 6 个 build/test RID、4 个 verify-publish RID，`win-arm64` 仍为 build-only。首次远端 run 留给 `CI-002`，未以本地证据冒充远端成功。

- [x] `CI-002` 取得当前 HEAD 的 Windows build/test/benchmark guard 首次远端全绿。
  - 优先级：P0。
  - 恢复说明：2026-07-11 已在当前候选 HEAD 恢复执行；首次远端 run 暴露 hosted Windows 子进程编码/Bash 解析、benchmark 重复 filter 与 standard runner 无 WGL 三类问题，按普通 CI 与专用 GPU smoke 的真实能力边界修正。
  - 验收：win-x64 build、1492+ tests、disassembly guard、benchmark regression 实际执行；artifact/report 绑定 run id 和 commit SHA，并持久化逐程序集 TRX 与聚合计数。该任务只证明 standard hosted Windows 的 build/test/benchmark/disassembly，不声称独立 native GPU smoke 已执行。
  - 本地修复证据：`docs/evidence-2026-07-11-ci-002-local-remediation.md`；实现提交 `97d7c0b9` 已在 detached clean worktree 中按同一 SHA 完成 Windows native/solution build、13-TRX 聚合、disassembly guard 与正式 benchmark regression，全部通过。
  - 远端证据：GitHub Actions run `29166221230`、attempt `1`、SHA `1148ca8732d4c7645ef4cfed1bb30352d8449d70`；`build-test (win-x64)`、`benchmark-guard (win-x64)`、`verify-publish (win-x64)` 与 `build-test (win-arm64, build-only)` 全部成功。win-x64 artifact 持久化 13/13 TRX，1776 total / 1739 executed / 1739 passed / 0 failed / 37 个 native GPU scope NotExecuted，最低 1492 门槛满足；benchmark artifact 同 run/SHA 记录 disassembly 与正式 regression success。该 workflow 的 Linux/macOS 长期矩阵失败仍由 `CI-003` 跟踪，不反向否定本任务明确限定的 Windows 首绿。

- [!] `CI-003` 取得长期 6-RID build/test 与 4-RID verify-publish 矩阵证据。阻塞：`CI-002` 已完成，但当前本地主线尚未获准 push，因而没有当前 commit 的远端矩阵；对应 hosted runner/native toolchain 也必须实际可用。解除条件：明确授权并 push 当前提交，取得同一完整 SHA 的 6-RID build/test、4-RID verify-publish、benchmark guard 与 `ci-evidence` 全绿结果。
  - 优先级：P1。
  - 验收：矩阵与 `SCOPE-001` 一致；win-arm64 build-only 不伪装测试；manifest/hash/runner identity 通过 preflight 和人工复核。
  - 2026-07-17 本地修复节点：最新远端 run `29308458346`（SHA `3a980bf4`）的全部 build/test、verify-publish 与 benchmark job 均在 recursive Checkout 失败，根因是 RmlUi gitlink `1b69207f` 只存在于本机、配置 fork 无法获取。当前节点把 gitlink 固定到 fork 已公开分支 tip `22b93ae9`，并将 8 行 framebuffer 集成改动迁入 `native/ui_native/rmlui-patches.json` 管理的 SHA256 base+patch overlay；CMake 对 commit、基础文件、patch、路径与 `git apply --check` 全部 fail-closed，win-x64 clean native build 已真实链接 overlay。该本地结果不冒充尚未触发的远端矩阵。

## 测试质量

- [x] `TEST-001` 把 GL/ANGLE/native smoke 从“环境变量未开即返回并计为通过”改为显式 skipped/trait/job，并在 CI 中至少有一个真实执行 job。
  - 优先级：P1。
  - 依赖：`CI-001`。
  - 验收：普通单测报告能区分未执行；native-smoke job 开启要求的环境并报告真实用例数；失败不能静默降级成 pass。
  - 设计来源：`plan/14-testing-benchmarking.md` §3.11、§4.7；`plan/20-interactive-html-ui.md` §3–§4；`.github/workflows/ci.yml`。
  - 证据：历史实现报告 `docs/evidence-2026-07-10-test-001-native-smoke.md`；普通测试把无环境用例明确记为 NotExecuted；当前 `run-native-smoke.ps1` v2 对四项目、TRX/逐条结果、Desktop GL 与真实 ANGLE identity 全部 fail-closed，普通 hosted CI 与 `.github/workflows/native-gpu-smoke.yml` 专用图形 job 已拆分。冻结候选 SHA 的真实远端 GPU run 不沿用本条历史结论，单独由 `TEST-003` 阻塞管理。

- [x] `TEST-002` 建立 coverage 收集、报告和最低阈值，区分行为测试与源码纪律测试。
  - 优先级：P2。
  - 依赖：`CI-002`。
  - 验收：覆盖率按 src 程序集发布；阈值可审查；不以 166 个源码字符串断言掩盖运行时路径缺口。
  - 证据：实现 commit `e95a49c6`；稳定报告 `docs/evidence-2026-07-17-test-002-behavior-coverage.md`。正式 run `local-20260717-test002-final-e95a49c6` 在同一 commit 上得到 2036 behavior passed / 47 个显式环境 NotExecuted / 0 failed、241 source-discipline passed / 0 NotExecuted / 0 failed，14 份唯一 Coverlet JSON/Cobertura 精确聚合为 17/17 个 `src` 程序集 line/branch 门禁通过；Solution Release build 0 warning / 0 error，actionlint v1.7.12 与 6 条聚合/CI 负向合同测试通过。

- [!] `TEST-003` 取得冻结候选 SHA 的专用 Windows native GPU smoke 远端全绿证据。阻塞：当前 public personal 仓库没有注册任何具备交互桌面、Desktop GL 3.3+ 与 ANGLE/GLES3 的隔离 runner；standard `windows-latest` 无 WGL，不能冒充 GPU runner。解除条件：注册带 `pixelengine-wgl-angle` / `pixelengine-native-smoke` labels 的可信 Windows x64 runner，合入专用 workflow 后仅对同仓库可信完整 SHA 手工 dispatch，并取得所有发现用例 passed、failed/skipped/not-executed 均为 0 的 artifact。
  - 优先级：P1。
  - 依赖：`REL-001`、`UI-003`。
  - 设计来源：`plan/14-testing-benchmarking.md` §3.11、§4.7；`tools/target-hardware-matrix.json`；GitHub self-hosted runner 安全边界。
  - 验收：runner/CPU/GPU/driver/OS/交互 session、Desktop GL 与 ANGLE context、run id/attempt/候选 SHA、逐项目 TRX/日志/汇总及 SHA256 同源；缺 runner、空执行、缺项目、任何失败/跳过/未执行、SHA 漂移或无真实图形上下文均失败，artifact 上传不得改变 job 结论。
  - 本地边界证据：`docs/evidence-2026-07-11-ci-002-local-remediation.md` 记录 Desktop GL marker 通过、本机原生 GLES 被正确拒绝冒充 ANGLE，以及远端专用 runner 仍缺失。

## 证据可追溯性

- [x] `EVID-001` 建立稳定 evidence index，记录 task ID、commit、run/session id、硬件、命令、报告路径和 SHA256。
  - 优先级：P1。
  - 依赖：`PLAN-001`。
  - 设计来源：`plan/14-testing-benchmarking.md` §3.10–§3.11、§5–§6；`plan/15-build-packaging-distribution.md` §1、§5–§6；`plan/17-roadmap-execution-order.md` §2、§4；`plan/tasks/70-evidence-contracts.md`。
  - 验收：已完成任务不再只引用会被清理的 `scratch/`/临时 `artifacts/`；历史报告明确标记旧 commit；preflight 输出可追踪到原始材料。
  - 证据：`docs/evidence-index.json`、`docs/evidence-index.md`、`tools/validate-evidence-index.ps1`；entry / task 数量由 `pwsh tools/validate-evidence-index.ps1` 实时重算，不在本条复制固定值；`TaskEvidenceCatalogIndexesAllEvidencePreflightStatusesAsNonPassing` 通过 1/1。

- [!] `EVID-002` 完成 GL/OpenAL/Box2D/ALC 的外部 native leak detector。阻塞：需要冻结候选 commit、detector 环境和四类同源报告。
  - 优先级：P1。
  - 验收：shutdown 后对应 live-object/ALC 计数为 0；不是 process smoke；manifest/hash/commit 同源并经人工复核。

- [x] `EVID-003` 固定 Windows-first baseline 与长期兼容矩阵使用的目标硬件/runner 清单。
  - 优先级：P1。
  - 依赖：`SCOPE-001`。
  - 设计来源：`plan/00-conventions-and-techstack.md` §3–§4；`plan/15-build-packaging-distribution.md` §2、§6；`plan/16-performance-hardening.md` §4、§6；`.github/workflows/ci.yml`；`tools/release-rids.json`。
  - 验收：每个 required/conditional RID 明确 CPU、GPU、OS、driver、runner、管理员/签名权限和可执行的 benchmark/smoke；性能与发行任务只引用该清单，不再写模糊“目标硬件”。
  - 证据：`tools/target-hardware-matrix.json`、`docs/target-hardware-matrix.md`、`tools/validate-target-hardware-matrix.ps1`、历史报告 `docs/evidence-2026-07-10-target-hardware-matrix.md`；schema v2 已把 hosted build/test 与 external native GPU runner 拆分，当前校验输出 `Target hardware matrix valid: 6 RIDs; active=win-arm64,win-x64; conditional=win-arm64; observed_local=win-x64; native_gpu_smoke=external_required/missing.`。

## 玩家包与 Release

- [!] `REL-001` 从冻结候选 HEAD 重建 Editor 和 win-x64 R2R/AOT 玩家包，替换当前绑定 `a3716e86` 的最终输出。阻塞：`ARCH-001`–`ARCH-005`、`PERF-002`、`PERF-004`–`PERF-007` 与 `DOC-002` 已完成；当前候选仍缺同一 SHA 的远端标准 CI 结果，以及 `PERF-003` / `PERF-011` 的外部硬件、权限或指标决策证据。三项闭合后才能冻结候选 HEAD；不等待 M15 人工体验任务。
  - 优先级：P1。
  - 验收：build-result、launcher、content、NOTICE、SHA256、player-only audit、窗口 smoke 全部通过；manifest 绑定当前 HEAD。

- [!] `REL-002` 完成 active RID × channel 的确定性 package/hash/audit。阻塞：`REL-001`、`REL-005`、`TEST-003` 未完成。
  - 优先级：P1。
  - 依赖：`REL-001`、`REL-005`、`TEST-003`。
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

- [x] `REL-006` 交付可选择安装目录的 Windows PixelEngine Editor 安装包，并统一用户可见产品身份。
  - 优先级：P1。
  - 依赖：`BASE-013`、`BASE-017`。
  - 设计来源：`plan/15-build-packaging-distribution.md` §Editor Windows 安装交付合同；`plan/19-standalone-editor-app.md` §用户可见产品身份与安装入口。
  - 验收：透明 SVG 品牌源与多尺寸 ICO 同源；Editor 的产品名、文件描述、窗口/快捷方式和用户入口统一为 `PixelEngine`，发布入口为 `PixelEngine.exe`，内部源码目录与 namespace 可继续保留 `PixelEngine.Editor.Shell`；固定工具链从 native build 和 win-x64 自包含 R2R publish 生成单一 MSI，安装向导允许自定义目录，创建当前用户开始菜单与桌面快捷方式，并登记可升级、可修复、可卸载的 Add/Remove Programs 项；构建脚本与 verifier 拒绝缺入口、缺图标、非自包含 payload、错误 MSI 元数据或旧 `*.Shell.exe` 用户入口；在含空格和非 ASCII 的隔离目录完成真实静默安装、Editor 启动 probe、快捷方式/卸载登记核对与卸载零残留，并把安装包及 SHA256 manifest 写入 `最终输出/安装器`。本机未签名 MSI 只用于开发测试，不冒充 `REL-002`/`REL-003` 的签名、确定性或 GitHub Release 证据。
  - 提交节点：一，canonical task 与 plan/15、plan/19 安装/命名合同；二，SVG/ICO、Editor 产品身份及所有本机最终输出入口迁移；三，MSI 工程、构建/verifier 与自动化测试；四，当前实现提交同源的真实安装/启动/卸载证据、最终输出和 canonical 完成状态。每个节点按 `AGENTS.md §6` 单独中文提交。
  - 完成证据：干净实现提交 `d12ade9d18bf352166aacdb131d44f1f5fe91f25` 生成 `最终输出/安装器/PixelEngine-Setup-0.1.0-win-x64.msi`，SHA256 `8b111acff406e70461ce15126556ecdb197a222d5dc36e64751587dbb5ad7ccb`；277 个 File 行、2 个快捷方式与 2 卷内嵌 CAB 静态验证通过。含空格/中文自定义路径的 install / installed Editor / uninstall 退出码均为 0，产品状态 `-1 -> 5 -> -1`，目录、快捷方式和产品登记零残留。稳定报告：`docs/evidence-2026-07-20-rel-006-windows-installer.md`。

## 本次文档迁移

- [x] `PLAN-001` 建立 canonical task 目录、迁移旧 checkbox、更新入口与开发规范，并用自动校验保证覆盖完整。
  - 优先级：P0。
  - 依赖：无。
  - 验收：任务 ID 唯一；旧 21 份计划计数和映射完整；README/AGENTS/plan17 指向本目录；validator 全绿；无旧 checkbox 被删除而失去追溯。
