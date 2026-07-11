# CI-002 Windows 托管门禁修复与远端恢复证据

Evidence Index: `ci-002-local-remediation-20260711`

## 结论与边界

- CI 修复源码已提交为 `97d7c0b99cedbd907f8b82a716684aec320f4a47`。本轮把 standard hosted Windows 的 build/test/TRX 聚合、disassembly guard 与 benchmark regression 同专用真实 GPU smoke 分离，并对两条链路都采用 fail-closed 合同。
- 用户授权后，`main` 的 `0a766c459b31ab29002d3fb00ac8e889aa100bc3` 已推送并触发 GitHub Actions run `29165262888`。该 run 的 Windows benchmark/disassembly、win-x64 verify-publish 与 win-arm64 build-only 均成功；win-x64 build-test 在 1774 项测试中暴露 8 个 hosted Windows tooling contract 失败。因此 `CI-002` 恢复为 `[~]`，当前继续修复并重跑，本文不把部分成功冒充首次全绿。
- `TEST-003` 仍保持 `[!]`：当前仓库没有注册满足交互桌面、Desktop GL 3.3+、真实 ANGLE/GLES 3.0+ 与隔离标签要求的 Windows x64 self-hosted runner。
- 2026-07-12 已在 detached clean worktree 中直接 checkout 完整实现提交 `97d7c0b9`，初始化该提交锁定的全部递归 submodule 后，按 Windows CI 顺序重新执行 native build、solution build、13-TRX test aggregate、disassembly guard 与正式 benchmark regression。新本地摘要直接绑定 `97d7c0b9`，取代首轮 pre-commit 工作树结果；它仍是本地证据，不是 GitHub Actions artifact。

## 历次远端失败与修复范围

前次 GitHub Actions run `29149667017` 绑定旧提交 `10884980beab725c913ea89b10a2957d675372b4`。Windows 路径暴露三类问题：托管测试子进程的 UTF-8/PowerShell/Git Bash 差异、benchmark 模糊 filter 导致同一类型重复执行，以及 `windows-latest` 没有可用 WGL 图形上下文。当前实现据此完成：

- 用统一 UTF-8 test-process helper 承载 Windows PowerShell，并确定性解析 Git Bash；覆盖 EditorShell build、Hosting project discipline 与 performance tooling 测试。
- benchmark baseline 以 `benchmarkType + method + parameters` 精确匹配，拒绝旧 `rowContains`，相同 filter 只执行一次且缺行、多行或阈值超限均失败。
- standard CI 为 13 个预期测试程序集持久化逐程序集 TRX，聚合器校验计数、唯一 run identity、最低总数与 job 状态；native GPU 项在普通测试中明确为 NotExecuted，并声明由独立 workflow 承担。
- 新增仅允许可信完整 SHA 手工 dispatch 的 `native-gpu-smoke.yml`，要求带 `pixelengine-wgl-angle` 与 `pixelengine-native-smoke` 标签的 Windows x64 self-hosted runner；preflight、TRX、图形 marker、runner/SHA identity 与最终 SHA256SUMS 均失败闭合。
- native smoke 要求四个项目全部存在并对 TRX Counters 与逐条结果对账；任何失败、跳过、未执行、空执行、runner 非零、SHA 漂移、Desktop GL/ANGLE 能力缺失都会失败。

### 当前 main 首次远端恢复 run

GitHub Actions run `29165262888`（workflow=`CI`、event=`push`、attempt=`1`、SHA=`0a766c459b31ab29002d3fb00ac8e889aa100bc3`）提供了第一份绑定当前主线的真实 hosted Windows 结果：

| Windows job | 结果 |
|---|---|
| benchmark-guard (win-x64) | success；native build、solution build、disassembly guard、正式 benchmark regression 与 evidence upload 全部成功 |
| verify-publish (win-x64) | success |
| build-test (win-arm64, build-only) | success；test step 按合同明确 skipped |
| build-test (win-x64) | native/solution build 成功；13/13 TRX；1774 total、1737 executed、1729 passed、8 failed、37 NotExecuted；job failure |

下载产物中的 `benchmark-guard.md` SHA256 为 `83fc046c1c748392ff8f94a5ffbd6d5e8b5322b772bc0477723e615ba618436b`，`build-test-win-x64.md` SHA256 为 `a4b411d62f555a54fc9b09de2ff6481df40bf23c817cd94850029cef5e6680e2`；二者都显式绑定同一 run id 与完整 SHA。

8 个失败全部属于 Hosting tooling fixture，而不是引擎运行行为：4 个 native-smoke 合同测试直接 `pwsh -File`，绕过统一 UTF-8 helper 后无法稳定匹配中文失败诊断；4 个 Bash release-audit fixture 在 hosted Windows 检测到原生 `python3`，其 text-mode `print()` 输出 CRLF，末尾 `\r` 污染 ZIP entry identity。当前修复让 native-smoke fixture 统一使用 UTF-8/PlainText PowerShell bootstrap，并让 ZIP listing 生产者输出二进制 LF、消费者防御性剥离末尾 CR；另以真实 Bash + CRLF `python3` shim 新增回归测试。非 Windows job 的现有失败继续由 `CI-003` 跟踪，不混入 `CI-002` 的 Windows 首次全绿结论。

修复提交前的 Windows 本地回归结果：Hosting Tests 以 warnings-as-errors 构建成功（0 warning / 0 error）；远端失败切片与两项新增平台边界测试共 26/26 passed；Hosting Tests 全量 666 total、662 passed、4 个图形条件项 skipped、0 failed；`bash -n tools/audit-release-artifacts.sh` 通过。这组结果只证明修复已具备推送条件，最终仍以新完整 SHA 的 hosted Windows artifact 为准。

## 本地验证

验证主机：Microsoft Windows 11 专业版 build 26100；AMD Ryzen 7 5800X（8C/16T）；AMD Radeon RX 7900 XT driver `32.0.31021.5001`；.NET SDK `10.0.108`；win-x64。

干净 worktree 的源码与 submodule identity：

- PixelEngine：`97d7c0b99cedbd907f8b82a716684aec320f4a47`
- Box2D：`8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`
- FreeType：`0a0221a1347e2f1e07c395263540026e9a0aa7c7`；递归 `dlg`：`395ccad2c1e0daae535c4d20bb0a3f2424648e17`
- RmlUi：`22b93ae968dab2713a57780408513d8859bb9503`

| 验证 | 结果 |
|---|---|
| `tools/build-native.ps1 -Rid win-x64 -Configuration Release` | 退出码 0；Box2D shared/static、FreeType、RmlUi/UI native 完整构建；65.5 秒 |
| `dotnet build PixelEngine.sln -c Release` | 32 projects，0 warning / 0 error；56.1 秒 |
| `dotnet test PixelEngine.sln -c Release --no-build --logger trx` | 13/13 TRX；1774 total；1737 executed/passed；0 failed；37 native GPU scope NotExecuted；327.1 秒 |
| `tools/summarize-ci-test-results.ps1` | runner=`local-windows-clean-worktree`；local run id=`1`；SHA=`97d7c0b9…f4a47`；最低 1492 门槛满足；conclusion=`success` |
| `PixelEngine.Hosting.Tests` 最终全量 | 664 total；660 passed；4 native 条件项未执行；0 failed |
| `NativeSmokeToolingTests` | 20/20 passed |
| `NativeGpuCiContractTests` | 7/7 passed |
| `tools/disassembly-guard.ps1` | 退出码 0；143.6 秒；1 份 asm 被检查；`SIMD required: True` |
| PowerShell parser、PyYAML 与 actionlint v1.7.12 | 新增/修改脚本及两个 workflow 全部通过 |
| `tools/validate-task-catalog.ps1` | 80 canonical tasks；48 done、5 open、1 active、26 blocked；52 required |
| `tools/validate-target-hardware-matrix.ps1` | 6 RIDs；active=win-arm64,win-x64；native GPU smoke=`external_required/missing` |
| `git diff --check` | 通过 |

干净 worktree 的 TRX 聚合 Markdown SHA256 为 `7c75329b6aae85a3d59d8923f2287718e0eb84ad7f61001decd4c786aa6d50e8`；local run id `1` 只是满足同一聚合器数值合同的本地标识，runner 字段明确为 `local-windows-clean-worktree`，不得冒充 GitHub run。原始 TRX 与生成目录是 volatile 输出，稳定结论以本文为准。

## 正式 benchmark regression

在同一个 `97d7c0b9` clean worktree 执行 `pwsh tools/benchmark-regression.ps1 -BaselinePath bench/PixelEngine.Benchmarks/baselines/ci-baseline.json -Artifacts artifacts/benchmark-regression-clean-97d7`，真实 BenchmarkDotNet 总耗时 507.0 秒。四个唯一 filter 各执行一次，五个精确合同项均实际匹配并低于门槛：

| 合同项 | 实测 Mean | 门槛 |
|---|---:|---:|
| `GcPauseBenchmark.SteadyPoolRentReturn` | 7.070 ns | 1,000,000 ns |
| `CellThroughputBenchmark.StepJobSystem(Profile=FullActiveLiquid)` | 16.0507 ms | 100 ms |
| `CellThroughputBenchmark.StepJobSystem(Profile=TypicalDirtyRect)` | 315.800 us | 5 ms |
| `ReactionLookupBenchmark.FindDirect` | 3.413 ns | 100 ns |
| `ParticleIntegrationBenchmark.IntegrateFlyingParticles(Count=200000)` | 2.1362 ms | 10 ms |

对应 BDN Markdown SHA256：GC `975f4ba05818f431665a6de9794288af9a6a6ef321dfcdd2b18f1035d1009165`；Cell `70fb3c7014b53bde740fcca33025bb77eead57a367799b92393437846b1ff0ba`；Reaction `a6c8e98613f22f02bc7601ff129e4e464ef0177903ffad4fa2defd2ff36d3a6c`；Particle `634f1a9adf906bb69dd992df6e1f5e43c5c64b28fa640c9f120bd9e20ee7f3ef`。disassembly asm SHA256 为 `8e2d03a1c2a8eccda7419ffe47ec451516c04e58f90e3f3086f54549e3076206`。这组 CI guard 是回归门禁，不替代 `PERF-*` 正式目标硬件基准。

## 真实 GPU 边界

- 本机真实 Desktop GL 测试通过，图形 capability marker 已进入 TRX。
- 本机所谓 `GlEs30Angle` 实际返回 AMD 原生 GLES，`IsAngle=false`；新合同正确拒绝把它记成 ANGLE 证据。没有生成或声称虚假的 ANGLE pass。
- GitHub 仓库当前没有可用的专用 self-hosted GPU runner，因此 `TEST-003` 只能等待外部 runner 条件，不回退到 standard hosted runner。

## 解除 `CI-002` 的精确条件

把本轮 hosted Windows fixture 修复提交并推送后，对新的完整 SHA 运行 GitHub Actions。只有 win-x64 build/test、13 个程序集 TRX 聚合、disassembly guard 与 benchmark regression jobs 全部成功，且 artifact/report 的 run id、attempt 与完整 commit SHA 同源，才能将 `CI-002` 改为 `[x]`。专用真实 GPU 结果仍由 `TEST-003` 独立闭合；非 Windows 长期矩阵失败由 `CI-003` 管理，本文不声称其已修复。
