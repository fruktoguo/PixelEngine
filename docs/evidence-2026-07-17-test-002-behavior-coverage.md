# TEST-002 行为 coverage 与源码纪律测试分层证据

## 结论

实现与本次正式运行绑定 commit `e95a49c6caa3708620f3ac3eaeeaf40c2d0f6136`，run ID 为 `local-20260717-test002-final-e95a49c6`。`tools/run-coverage.ps1` 从干净工作树重新执行完整 solution 测试，再以 `FullyQualifiedName!~DisciplineTests` 重跑行为层并采集 Coverlet JSON/Cobertura；最终 17/17 个 `src/PixelEngine.*` 程序集均满足各自 line/branch 最低阈值和最小可观测分母，门禁结论为 success。

完整 TRX 共 2324 项：行为层 2083 项，其中 2036 passed、47 个已有 native/GPU 环境条件 NotExecuted、0 failed；源码纪律层 241 passed、0 NotExecuted、0 failed。行为 coverage 重跑得到完全相同的 2083/2036/47/0 计数，证明 `*DisciplineTests` 未混入 instrumentation。历史验收提到的 166 个源码字符串断言不再能用测试数量掩盖生产运行路径缺口。

## 环境与命令

- Windows 11 专业版 build 26100，AMD Ryzen 7 5800X，AMD Radeon RX 7900 XT driver `32.0.31021.5001`。
- .NET SDK `10.0.108`，Microsoft.NETCore.App `10.0.8`，win-x64。
- 正式命令：`pwsh -NoProfile -File tools/run-coverage.ps1 -Configuration Release -OutputRoot artifacts/test002-coverage-final-e95a49c6 -SkipBuild -NoRestore -RunId local-20260717-test002-final-e95a49c6`。
- 实现门禁：`dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1 -p:TreatWarningsAsErrors=true` 为 0 warning / 0 error；`go run github.com/rhysd/actionlint/cmd/actionlint@v1.7.12 .github/workflows/ci.yml` 通过；`CoverageAggregationTests|CoveragePolicyDisciplineTests` 6/6 通过。

## 分层与聚合边界

- 完整 solution TRX 是行为/纪律分类和所有测试终态的权威输入；纪律层仍必须执行并全绿，只是不贡献生产程序集 coverage。
- 原始报告由 14 个 testhost 生成。VSTest 同时保留 attachment 原件与 deployment 副本，物理 `coverage.json` 为 28 份；聚合器以 SHA256 去重后要求恰好 14 份唯一报告。
- Line identity 为 assembly + source file + line；branch identity 额外包含 class、method、IL offset/end offset、path 与 ordinal。聚合不相加 Cobertura 百分比。
- `coverage.runsettings` 已从原始报告排除 `bin/obj` 与 source-generated 文件，本次 `excludedGeneratedFileCount=0`；聚合器仍保留第二层拒绝逻辑，负向测试证明 generated-only 程序集不能通过。
- `tools/coverage-policy.json` 精确登记 17 个源程序集，并同时约束最低百分比和最小 lines/branches valid；删除 PDB、扩大 exclusion 或漏加载程序集不能靠缩小分母提高结果。

## 程序集结果

| Assembly | Lines | Line % / minimum | Branches | Branch % / minimum |
|---|---:|---:|---:|---:|
| PixelEngine.Audio | 1182/1335 | 88.54 / 88.00 | 378/517 | 73.11 / 73.00 |
| PixelEngine.Content | 338/365 | 92.60 / 92.00 | 104/138 | 75.36 / 75.00 |
| PixelEngine.Core | 835/1111 | 75.16 / 75.00 | 193/344 | 56.10 / 56.00 |
| PixelEngine.Editor | 3539/6430 | 55.04 / 55.00 | 1318/3212 | 41.03 / 41.00 |
| PixelEngine.Editor.Automation.Client | 1208/1486 | 81.29 / 81.00 | 522/849 | 61.48 / 61.00 |
| PixelEngine.Editor.Automation.Protocol | 997/1635 | 60.98 / 60.00 | 92/140 | 65.71 / 65.00 |
| PixelEngine.Editor.Automation.Server | 4290/5219 | 82.20 / 82.00 | 1339/2166 | 61.82 / 61.00 |
| PixelEngine.Gui | 807/2035 | 39.66 / 39.00 | 239/867 | 27.57 / 27.00 |
| PixelEngine.Hosting | 4801/6522 | 73.61 / 73.00 | 1602/2706 | 59.20 / 59.00 |
| PixelEngine.Interop | 101/145 | 69.66 / 69.00 | 28/64 | 43.75 / 43.00 |
| PixelEngine.Physics | 2210/2387 | 92.58 / 92.00 | 874/1097 | 79.67 / 78.00 |
| PixelEngine.Rendering | 2414/6103 | 39.55 / 39.00 | 875/2096 | 41.75 / 41.00 |
| PixelEngine.Scripting | 2552/2829 | 90.21 / 90.00 | 890/1221 | 72.89 / 72.00 |
| PixelEngine.Serialization | 801/882 | 90.82 / 90.00 | 224/304 | 73.68 / 73.00 |
| PixelEngine.Simulation | 3497/3756 | 93.10 / 93.00 | 1170/1435 | 81.53 / 81.00 |
| PixelEngine.UI | 2226/3315 | 67.15 / 67.00 | 976/1760 | 55.45 / 55.00 |
| PixelEngine.World | 1241/1346 | 92.20 / 92.00 | 423/562 | 75.27 / 75.00 |

该表故意保留低覆盖程序集：`PixelEngine.Gui` line 39.66% / branch 27.57%，`PixelEngine.Rendering` line 39.55% / branch 41.75%。TEST-002 建立的是可审计 baseline 与回退门禁，不把低值隐藏在 solution 平均数中，也不伪称已达到任意行业通用百分比。

## 哈希与可重跑输出

- Policy：`tools/coverage-policy.json`，SHA256 `e4d862d288f1e9b75ff8577f4ab6689b324b5710355182f657f47e0ca85e3a00`。
- 机器 summary：`artifacts/test002-coverage-final-e95a49c6/report/coverage-summary.json`，SHA256 `db06c9d36f9cda0aa2949d6f47fa36aae44a06f67aa94fc4b23f81b5b1cbc975`。
- 人类 summary：`artifacts/test002-coverage-final-e95a49c6/report/coverage-summary.md`，SHA256 `306b3802b4f6c7747665b97c82f51be9ce769df17e6c3ce7b8a0e81d23099c1d`。
- `artifacts/` 中的 TRX、JSON、Cobertura 与 summary 是可清理的原始运行输出；长期证据是本文、版本化 policy/脚本/测试以及 Evidence Index 中重算的本文 SHA256。

## CI 边界

`.github/workflows/ci.yml` 已在标准 `build-test(win-x64)` 完整测试后复用同一 TRX，重跑行为 coverage，并以 `ci-evidence-coverage-win-x64` 上传原始与聚合报告；coverage 失败会直接使矩阵失败。当前证据证明本地 commit-bound 门禁和 workflow 语义已通过 actionlint，不冒充尚未触发的新远端 GitHub Actions run；TEST-002 的验收是收集、分层、报告与最低阈值能力，远端多 RID 与真实 native GPU 结论仍分别属于 CI-003/TEST-003。
