# 2026-07-18 PERF-003 无 build-server 污染基线

taskIds: `PERF-003`
commit: `5594da0e2dab52a344252c81957f588b43be005a`
runSessionId: `local-20260718-perf003-runner-clean-b18`
runIdentityStatus: `captured_commit_bound_local_formal_benchmark`
evidenceState: `local_formal_benchmark_target_gap_runner_isolated`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

当前提交 B18 的独立 full-active 基准为 8.946ms、`Allocated -`，99.9% CI 为 8.834-9.059ms；Typical Dirty 为 12.416us、`Allocated -`，99.9% CI 为 12.045-12.788us。两项均使用 2 launches x 10 warmups x 20 measured iterations，并在 benchmark 结束后确认没有残留的 MSBuild node-mode 进程或 `VBCSCompiler`。

2,166,784 个实际活跃 cell 按 B18 mean 折算约 1,937,656 cells/8ms，按 99.9% upper CI 折算约 1,913,655 cells/8ms，仍低于 2M 下限 3.12% / 4.32%。达到 2M/8ms 对该固定场景要求不超过 8.667ms，而 B18 的 lower CI 仍为 8.834ms。因此本报告更新本机可信基线，但不关闭 `PERF-003`。

## Runner 根因与修复

旧 `tools/run-benchmark.ps1` 只让 BenchmarkDotNet 生成项目使用 `--nodeReuse:false /p:UseSharedCompilation=false`，外层 `dotnet run` 仍使用默认持久 build servers。实测一次正式运行会留下约 13 个命令行为 `MSBuild.dll ... /nodemode:1 /nodeReuse:true` 的进程和一个 `VBCSCompiler`；后者在一次受污染运行中累计约 133 秒 CPU。相同源码因而在相邻正式运行中从 8.912ms/12.97us 漂移到 9.660ms/16.09us，不能作为代码效应。

commit `a8c7cffb` 做了两项 fail-closed 修复：

- 隔离复制前执行 `dotnet build-server shutdown`，关闭既有 MSBuild/Roslyn servers；
- 外层 `dotnet run` 传入 `--disable-build-servers`，防止 runner 自己重新创建持久节点。

工具 smoke 正常执行 1 launch x 1 warmup x 3 iterations，结束后按 Win32 process command line 查询 `/nodemode` 与 `VBCSCompiler` 均为 0。`PerformanceHardeningToolingDisciplineTests` 同时锁定 shutdown 与 `--disable-build-servers` 两个入口，完整 `PerformanceHardening` 回归为 159/159。

此前 B13 的列占用位图机制、正确性、JIT 与 EventPipe 结论仍有效；但旧 runner 下的绝对 timing 仅保留为历史数据，不再优先于本报告的 B18 当前基线。

## B18 正式结果

两种 workload 都从 clean `5594da0e` 运行；该提交的 production CA 与 `8ab31a4f` 字节一致，只保留 runner 修复及对无稳定收益实验的显式撤回历史。

| Workload | Mean | StdDev | 99.9% CI | N | Launch means | mValue | Code size | Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| Full-active 2,166,784 cells | 8.946ms | 0.196ms | 8.834-9.059ms | 39 | 9.034 / 8.854ms | 2.00 | 15,016B | `-` |
| Typical Dirty 16x16 | 12.416us | 0.660us | 12.045-12.788us | 40 | 12.366 / 12.468us | 2.00 | 28,027B | `-` |

Full-active 有一个高侧 outlier 被 BDN 移除，因此 N=39；Typical Dirty 报告一个低侧 outlier hint，但 40 个 measured samples 均保留。表中 launch means 从原始 `WorkloadResult` 逐 launch 重算，不用单个快簇替代完整汇总。

## 已拒绝的 lifetime 专门化

commits `3a6855db`、`386cd127` 曾对满宽 dirty 区预扫 Lifetime SoA，并用值类型 mode 生成 lifetime-aware/free 两份 `UpdateChunkCore`。FullOpts 反汇编证明机制真实存在：wrapper 441B，aware core 3,275B，free core 2,783B；只有 aware core 含 `ILifetimeSink.OnExpired` 调用，入口内联后 checkerboard worker 直接调用两份 core。

但 clean runner 下的 candidate-control-candidate 序列没有可复现收益：

| Run | Source | Full-active | 99.9% CI | Typical Dirty | 99.9% CI |
|---|---|---:|---:|---:|---:|
| candidate B16 | `a8c7cffb`，含 lifetime mode | 9.396ms | 9.212-9.581ms | 14.866us | 14.320-15.413us |
| control | detached `8ab31a4f` + runner-only patch | 9.525ms | 9.234-9.816ms | 12.773us | 12.319-13.227us |
| candidate B17 | `a8c7cffb`，含 lifetime mode | 9.604ms | 9.488-9.721ms | 12.329us | 11.906-12.753us |

full-active candidate 一次比 control 快 1.35%，另一次慢 0.83%，区间均不能证明稳定改善；Typical Dirty 又在两个 candidate run 间出现 20.6% 的均值漂移。该实现增加约 100 行 production 复杂度，却不能建立稳定效应，因此在 `5594da0e` 明确撤回。B18 比上述三轮更快属于主机运行簇差异，不能重新归因给已撤回的代码。

## 可复现命令

```powershell
$bdn = @(
    '--filter',
    '*TypicalDirtyCellThroughputBenchmark*',
    '*FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames*',
    '--launchCount', '2',
    '--warmupCount', '10',
    '--iterationCount', '20')

& .\tools\run-benchmark.ps1 `
    -Artifacts 'artifacts/perf-003-5594da0e-runner-clean-b18-20260718' `
    -BenchmarkDotNetArgs $bdn
```

验证命令包括：

```powershell
dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1 /p:TreatWarningsAsErrors=true
dotnet test tests/PixelEngine.Simulation.Tests/PixelEngine.Simulation.Tests.csproj -c Release --no-build --no-restore --disable-build-servers -m:1
dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-build --no-restore --disable-build-servers -m:1 --filter 'FullyQualifiedName~PerformanceHardening'
pwsh -NoProfile -File tools/validate-task-catalog.ps1
pwsh -NoProfile -File tools/validate-evidence-index.ps1
git diff --check
```

## 原始报告与 SHA256

`artifacts/` 是可再生原始输出；本文件与 Evidence Index 是长期稳定登记。

| 内容 | 原始文件 | SHA256 |
|---|---|---|
| B18 combined log | `artifacts/perf-003-5594da0e-runner-clean-b18-20260718/BenchmarkRun-20260718-180209.log` | `4E431A4D2A8629FFBEDB2B8FF775E5F4023C43308466EC6CF38502AC959B8524` |
| B18 full-active report | `artifacts/perf-003-5594da0e-runner-clean-b18-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `BCCA90562EB755EED5916E67AF2E04BCAB224BE90880FC1BAC3131C4797969DC` |
| B18 Typical Dirty report | `artifacts/perf-003-5594da0e-runner-clean-b18-20260718/results/PixelEngine.Benchmarks.TypicalDirtyCellThroughputBenchmark-report-github.md` | `C3FE0FA4A1D68B9549AA0D0379120F702B441E6E249F13BAC4C2812784923172` |
| clean control log | `artifacts/perf-003-8ab31a4f-lifetime-control-clean-b16-20260718/BenchmarkRun-20260718-174118.log` | `780265544C5DD28C8471EEC48418978F601B1956991F84EE7E690F0B8F814561` |
| rejected candidate B16 | `artifacts/perf-003-a8c7cffb-lifetime-mode-clean-b16-20260718/BenchmarkRun-20260718-173310.log` | `606493079D0657E0B0F31E89ECA28A2E3AE3063F3F4981CC419CA5EAACBAA30C` |
| rejected candidate B17 | `artifacts/perf-003-a8c7cffb-lifetime-mode-clean-b17-20260718/BenchmarkRun-20260718-174657.log` | `CA1E05EEA4864C80F617C5DCB0CDA8C689D0C2E1D8ECA4945347C83B468E6B4D` |
| rejected mode cores JIT | `artifacts/perf-003-lifetime-mode-dispatch-jit-20260718/ChunkUpdater.UpdateChunkCore.asm.txt` | `4002B1C276AAB689F0BB33CFC3ABA562A79BD331458BFC04DD12895D784D149E` |
| rejected inlined worker JIT | `artifacts/perf-003-lifetime-mode-inline-candidate-jit-20260718/CheckerboardScheduler.UpdateActiveBucketRange.asm.txt` | `9F9483464F032234E633D5F471186C707D9A820192A49EE348912A211C10E9D3` |
| runner no-server smoke | `artifacts/perf-runner-disable-build-servers-smoke-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-20260718-172040.log` | `6DF59C731FAE7726DA2EAC9604AE2B1B29E75DBFC37CFB089B2DBAF70B7DB54F` |

## 正确性与剩余阻塞

- 撤回后的 `PixelEngine.sln` Release + warnings-as-errors 为 0 warning / 0 error。
- `PixelEngine.Simulation.Tests` 为 204/204；runner 修复后的 `PerformanceHardening*` 为 159/159。
- task catalog 仍为 82 个 canonical task、0 active；`PERF-003` 保持 `[!]`。
- 当前只有 win-x64 / AVX2 本机数据，仍缺其余五个 RID 代表硬件、elevated ETW cache/branch counters、AVX-512 净损和玩家包 60s/3600-frame phase p99。B18 也仍低于本机 2M/8ms 下限，不能据此解锁 `PERF-008`、`PERF-009` 或候选版本冻结。
