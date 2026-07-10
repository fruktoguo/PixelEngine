# 2026-07-10 DOC-002 旧计划口径与证据路径审计

taskIds: `DOC-002`
snapshotCommit: `179efc3a90f95fbf5222cc1ac55e63b6841c0022`
catalogMigrationCommit: `5af1541fd4ad365e83ad11e39125838ed5f685d5`
auditBaseCommit: `55223db1`
evidenceState: `documentation_audit_with_rerunnable_replacements`

## 结论

`plan/00`–`plan/20` 的 21 份编号文档继续保存 1692 条历史 checkbox，但不再承担 live 状态。每份文档都统一声明：历史行冻结自 `179efc3a`，canonical 迁移基线是 `5af1541f`；当前状态只能从 `plan/tasks/` 读取，证据等级只能从稳定 Evidence Index 读取。

本轮校正了四类容易被误读的口径：

- 工程数量：早期 M0 的 18 项目是历史结构；当前仓库与 `PixelEngine.sln` 均为 32 个 `.csproj`。项目数是可重算事实，不再靠复制数字维持。
- Demo 路线：唯一正式验收路线是横向熔岩矿洞逃生；旧“引水成石桥 / 坍塌木桥”只保留为机制测试，不再是产品完成条件。
- Demo 兼容组件：`ObjectiveCrystal`、`crystal` 和采集型 `MineYield` 是旧任务 dogfood；当前路线仍可复用通用 `MineYield` 事件和 water 材质解法，但它们不是水晶 / 水位目标。
- CI 状态：workflow 源码、preflight、actionlint 或本地测试通过，只证明本地自动化合同；没有 GitHub run id / commit SHA 的远端 workflow 结果时，不得写成 CI 全绿。
- 临时证据：`artifacts/`、`BenchmarkDotNet.Artifacts/`、`scratch/` 中的截图、日志和报告均为可再生输出。旧 checkbox 为保持快照哈希而保留原路径，但这些路径已由本报告中的稳定报告或可重跑命令取代，不再是唯一证据。

## 为什么保留旧 checkbox 原文

`plan/tasks/source-coverage.json` 把每份旧计划的有序 checkbox 文本绑定到快照 commit `179efc3a` 的 SHA256；`tools/validate-task-catalog.ps1` 同时校验当前文件与该 commit。直接改写旧行会破坏迁移可追溯性。因此本轮采用两层校正：

1. 21 份旧计划顶部统一增加冻结基线、证据等级和替代报告说明。
2. 对项目数、Demo、CI、性能、Editor 和 UI 的高风险段落增加专项校正块；旧行仍可追溯，但不再能被当成 live 声明。

校验器现在要求每份旧计划都包含统一的 `DOC-002 历史证据口径` 标记，防止后续文档重新退化为第二套状态看板。

## 校正账本

| 范围 | 旧表述风险 | 当前口径 | 稳定替代 / 证据等级 |
|---|---|---|---|
| `plan/01` | 18 项目、32 项目数字可能随仓库结构漂移 | 18 是 M0 历史；当前数量由仓库与 solution 交集重算 | 下方“工程结构重算”；`current_repository_structure` |
| `plan/01`、`plan/14` | “CI 管线 / 门禁完成”可能被读成远端全绿 | 只表示 workflow 或本地门禁实现；远端状态由 `CI-002`/`CI-003` 管理 | `docs/evidence-2026-07-10-ci-001-workflow-validation.md`；`local_static_validation_complete` |
| `plan/13` | §5 仍保留“引水成石桥 / 坍塌木桥”验收行 | 该行已被 `SCOPE-006` 取代，只是历史机制测试 | `plan/tasks/20-scope-decisions.md`、`BASE-015`；`canonical_scope_decision` |
| `plan/13` | `ObjectiveCrystal` / `crystal` / `MineYield` 容易被读成默认目标 | 仅旧任务 dogfood 消费采集语义；当前路线把 Excavator / MineYield 当通用开路与事件能力 | `SCOPE-006`、`BASE-015`；`optional_legacy_dogfood` |
| `plan/13` | `artifacts/...bmp` 被误当长期截图证据 | 路径只记录历史本地 capture；产品完成仍需 `DEMO-*` 人工材料 | `docs/runtime-reports/2026-07-02-demo-scripted-window.md` 与下方 Demo 重跑命令；`scripted_probe_only` |
| `plan/04` | `BenchmarkDotNet.Artifacts/...` 是会被清理的报告 | 保留同一行的 BDN 重跑命令；零分配正式边界由稳定 PERF 报告管理 | `docs/evidence-2026-07-10-perf-004-zero-allocation.md`；`complete_local_zero_allocation` |
| `plan/16` | 多个 `artifacts/benchmark-run-ca-*` Dry 目录被写成优化证据 | Dry 目录只证明入口；正式规模数据与目标缺口以稳定报告为准 | `docs/evidence-2026-07-10-perf-003-ca-throughput.md`；`local_formal_benchmark_target_gap` |
| `plan/17`、`plan/19` | Editor 截图、build-result 与玩家包目录会被清理 | 只保留自动化基线和重跑入口；不替代 M15 人工 UX | `BASE-013`、下方 Editor 重跑命令；`automated_baseline_only` |
| `plan/20` | `{SCRATCH}/...log`、`scratch/...log` 与 DPI 截图会失效 | 日志路径全部降为历史线索；测试过滤器和 native build 命令是复现入口 | `BASE-014`、下方 UI/IME 重跑命令；`automated_contract_only` |
| `plan/14` | “6-RID 矩阵”旧 `[x]` 可能被读成 hosted runner 全绿 | 仅表示 workflow 矩阵接线 / 规则覆盖；真实远端矩阵仍由 `CI-003` 管理 | CI-001 稳定报告；`local_static_validation_complete` |
| `plan/15` | `artifacts/publish`、`artifacts/package` 出现在命令中 | 它们是明确的生成输出参数，不是证据链接 | `tools/build-player.ps1` / `tools/audit-release-artifacts.ps1` 可重跑；发行完成仍由 `REL-*` 管理 |

## 可重跑命令

### 工程结构重算

```powershell
$repoProjects = @(rg --files -g '*.csproj' | ForEach-Object { $_.Replace('\', '/') } | Sort-Object)
$solutionProjects = @(dotnet sln PixelEngine.sln list | Where-Object { $_ -match '\.csproj$' } | ForEach-Object { $_.Trim().Replace('\', '/') } | Sort-Object)
Compare-Object $repoProjects $solutionProjects
"repo=$($repoProjects.Count) solution=$($solutionProjects.Count)"
```

本轮输出为 `repo=32 solution=32`，`Compare-Object` 无差异。未来新增项目时应重跑，而不是继续复制“32”作为永久常量。

### Demo 当前路线合同

```powershell
dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~LavaMineSceneBuildsDirectSideScrollingLavaRoute|FullyQualifiedName~DefaultPlayableWorldBuildsSideScrollingLavaRoute|FullyQualifiedName~DemoDefaultHudAndResultTextDescribesSideScrollingGoalNotLegacyMission"
```

需要重做机器窗口 capture 时使用新的输出目录；生成的 BMP 仍只属于 scripted probe：

```powershell
dotnet run --project demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-restore -- --no-hot-reload --window-ticks 430 --scripted-window-demo --content demo/PixelEngine.Demo/content --capture-frame artifacts/doc-002-rerun/demo.bmp --log-dir artifacts/doc-002-rerun/runtime
```

### CI 本地静态合同

```powershell
python -c "import yaml, pathlib; yaml.safe_load(pathlib.Path('.github/workflows/ci.yml').read_text(encoding='utf-8'))"
$env:GOSUMDB = 'off'
go run github.com/rhysd/actionlint/cmd/actionlint@v1.7.12 .github/workflows/ci.yml
```

这两条命令不能产生 GitHub run id，也不能关闭 `CI-002` 或 `CI-003`。

### Editor 自动化底座

```powershell
dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~DemoRuntimeScriptingTests|FullyQualifiedName~EditorShellProjectTests|FullyQualifiedName~EditorConsoleStoreTests|FullyQualifiedName~HostingProjectDisciplineTests"
dotnet run --project apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj -c Release --no-restore -- --scripted-default-workbench-probe --window-ticks 80 --capture-frame artifacts/doc-002-rerun/editor.bmp
```

### UI / IME 自动化合同

```powershell
pwsh -NoProfile -File tools/build-native.ps1 -Rid win-x64 -Configuration Release
dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~RmlUiGlBootstrapSmokeTests|FullyQualifiedName~UiInputRouterTests|FullyQualifiedName~BackendConformanceTests|FullyQualifiedName~RmlUiNativeProfileGateTests|FullyQualifiedName~ManagedFallbackBackendTests"
dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~WindowsImeCompositionReaderTests|FullyQualifiedName~EditorShellGameViewContractTests"
```

这些命令只证明自动化合同，不替代真实平台候选窗位置、composition 手感或人工 UX。

## 验证

```powershell
pwsh -NoProfile -File tools/validate-task-catalog.ps1
pwsh -NoProfile -File tools/validate-evidence-index.ps1
git diff --check
```

本报告不把任何 scripted probe、local test、Dry benchmark 或临时 capture 升格为远端 CI、目标硬件、人工体验或发行完成证据。
