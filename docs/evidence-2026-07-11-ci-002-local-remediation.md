# CI-002 Windows 托管门禁本地修复证据

Evidence Index: `ci-002-local-remediation-20260711`

## 结论与边界

- CI 修复源码已提交为 `97d7c0b99cedbd907f8b82a716684aec320f4a47`。本轮把 standard hosted Windows 的 build/test/TRX 聚合、disassembly guard 与 benchmark regression 同专用真实 GPU smoke 分离，并对两条链路都采用 fail-closed 合同。
- `CI-002` 仍保持 `[~]`：当前提交尚未 push，也没有绑定当前完整 SHA 的 GitHub Actions Windows artifact，因此本文只登记“本地修复与验证完成”，不冒充远端首次全绿。
- `TEST-003` 仍保持 `[!]`：当前仓库没有注册满足交互桌面、Desktop GL 3.3+、真实 ANGLE/GLES 3.0+ 与隔离标签要求的 Windows x64 self-hosted runner。
- 本地测试与 benchmark 在补丁尚未提交、父提交为 `9dedb58e03ac8802a1627d63ee2ee6fd817d9135` 的工作树中执行；该工作树的补丁随后原样提交为 `97d7c0b9`。本地 TRX summary 因而如实保留父提交 SHA，不能作为 `97d7c0b9` 的远端同源证据。

## 前次远端失败与修复范围

前次 GitHub Actions run `29149667017` 绑定旧提交 `10884980beab725c913ea89b10a2957d675372b4`。Windows 路径暴露三类问题：托管测试子进程的 UTF-8/PowerShell/Git Bash 差异、benchmark 模糊 filter 导致同一类型重复执行，以及 `windows-latest` 没有可用 WGL 图形上下文。当前实现据此完成：

- 用统一 UTF-8 test-process helper 承载 Windows PowerShell，并确定性解析 Git Bash；覆盖 EditorShell build、Hosting project discipline 与 performance tooling 测试。
- benchmark baseline 以 `benchmarkType + method + parameters` 精确匹配，拒绝旧 `rowContains`，相同 filter 只执行一次且缺行、多行或阈值超限均失败。
- standard CI 为 13 个预期测试程序集持久化逐程序集 TRX，聚合器校验计数、唯一 run identity、最低总数与 job 状态；native GPU 项在普通测试中明确为 NotExecuted，并声明由独立 workflow 承担。
- 新增仅允许可信完整 SHA 手工 dispatch 的 `native-gpu-smoke.yml`，要求带 `pixelengine-wgl-angle` 与 `pixelengine-native-smoke` 标签的 Windows x64 self-hosted runner；preflight、TRX、图形 marker、runner/SHA identity 与最终 SHA256SUMS 均失败闭合。
- native smoke 要求四个项目全部存在并对 TRX Counters 与逐条结果对账；任何失败、跳过、未执行、空执行、runner 非零、SHA 漂移、Desktop GL/ANGLE 能力缺失都会失败。

## 本地验证

验证主机：Microsoft Windows 11 专业版 build 26100；AMD Ryzen 7 5800X（8C/16T）；AMD Radeon RX 7900 XT driver `32.0.31021.5001`；.NET SDK `10.0.108`；win-x64。

| 验证 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore -p:TreatWarningsAsErrors=true` | 32 projects，0 warning / 0 error |
| 13 个测试项目与 `tools/summarize-ci-test-results.ps1` | 1774 total；1737 executed/passed；0 failed；37 native GPU scope NotExecuted；13/13 TRX；最低 1492 门槛满足 |
| `PixelEngine.Hosting.Tests` 最终全量 | 664 total；660 passed；4 native 条件项未执行；0 failed |
| `NativeSmokeToolingTests` | 20/20 passed |
| `NativeGpuCiContractTests` | 7/7 passed |
| PowerShell parser、PyYAML 与 actionlint v1.7.12 | 新增/修改脚本及两个 workflow 全部通过 |
| `tools/validate-task-catalog.ps1` | 80 canonical tasks；48 done、5 open、1 active、26 blocked；52 required |
| `tools/validate-target-hardware-matrix.ps1` | 6 RIDs；active=win-arm64,win-x64；native GPU smoke=`external_required/missing` |
| `git diff --check` | 通过 |

本地 TRX 聚合的 volatile 原始输出位于 `artifacts/ci002-local-full-current/`；稳定结论以本文为准，原始目录不会被当成唯一长期证据。

## 正式 benchmark regression

执行 `pwsh tools/benchmark-regression.ps1 -BaselinePath bench/PixelEngine.Benchmarks/baselines/ci-baseline.json -Artifacts artifacts/benchmark-regression`，真实 BenchmarkDotNet 总耗时约 524.2 秒。五个唯一合同项均实际执行一次并低于门槛：

| 合同项 | 实测 Mean | 门槛 |
|---|---:|---:|
| `GcPauseBenchmark.SteadyPoolRentReturn` | 6.832 ns | 1,000,000 ns |
| `CellThroughputBenchmark.StepJobSystem(Profile=FullActiveLiquid)` | 16.249 ms | 100 ms |
| `CellThroughputBenchmark.StepJobSystem(Profile=TypicalDirtyRect)` | 286.500 us | 5 ms |
| `ReactionLookupBenchmark.FindDirect` | 3.390 ns | 100 ns |
| `ParticleIntegrationBenchmark.IntegrateFlyingParticles(Count=200000)` | 2.2231 ms | 10 ms |

这组 CI guard 是回归门禁，不替代 `PERF-*` 正式目标硬件基准。

## 真实 GPU 边界

- 本机真实 Desktop GL 测试通过，图形 capability marker 已进入 TRX。
- 本机所谓 `GlEs30Angle` 实际返回 AMD 原生 GLES，`IsAngle=false`；新合同正确拒绝把它记成 ANGLE 证据。没有生成或声称虚假的 ANGLE pass。
- GitHub 仓库当前没有可用的专用 self-hosted GPU runner，因此 `TEST-003` 只能等待外部 runner 条件，不回退到 standard hosted runner。

## 解除 `CI-002` 的精确条件

在用户明确授权 push 后，对包含本修复的当前完整 SHA 运行 GitHub Actions。只有 win-x64 build/test、13 个程序集 TRX 聚合、disassembly guard 与 benchmark regression jobs 全部成功，且 artifact/report 的 run id、attempt 与完整 commit SHA 同源，才能将 `CI-002` 改为 `[x]`。专用真实 GPU 结果仍由 `TEST-003` 独立闭合；非 Windows 长期矩阵失败由 `CI-003` 管理，本文不声称其已修复。
