# 2026-07-18 PERF-003 独立初态 2M CA 对照证据

taskIds: `PERF-003`
implementationCommit: `e020f47681fb66a272aa80caf70d100b0c717ba9`
benchmarkContractCommit: `5a98c98819e6952aa9505090392cea05ba973fb0`
baselineCommit: `5a98c98819e6952aa9505090392cea05ba973fb0`
benchmarkRunIds: `local-20260718-perf003-independent-baseline-a2`、`local-20260718-perf003-independent-optimized-b3`
runIdentityStatus: `captured`
evidenceState: `local_formal_benchmark_target_gap`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

本轮先修正测量合同，再评估通用 CA 热路径；没有把连续帧 dirty 收缩或单次不足 100ms 的 workload 冒充 2M full-active 证据：

- `FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames` 持有 16 份互不共享可变网格的 kernel。每次 iteration 在计时外把每份网格恢复到相同 frame/parity，并重新填满 23×23 active chunks，即每帧 `2,166,784` 个 full-dirty cell。
- 每个 kernel 在计时内只推进一帧，`OperationsPerInvoke=16` 只负责折算单帧耗时；后续帧的 dirty 收缩不能减少被测工作量。即使达到 8ms 目标，单次 workload 也仍为 128ms。本轮两份正式报告均没有 `MinIterationTime` warning。
- 目标性能 evidence preflight 现在要求原始报告出现该完整 benchmark 名与 `FullActive2M`，旧 `CellThroughputBenchmark.StepJobSystem(FullActiveLiquid)` 的 262K 场景即使伪填 `activeCellsPerFrame>=2000000` 也会 fail-closed。
- 生产优化在 `NeighborWindow` 为中心 target 直接复用 slot 4 SoA 基址；`ChunkUpdater` 对 chunk 内部下对角用 source local 直接得到 target local，边界仍走原 3×3 halo、MoveCap 与 KeepAlive 路径。RigidOwned 通知、单缓冲 swap、parity、lifetime 和 damage 语义不变。

在相同硬件、相同 16 个独立初态、相同 8 worker 和固定 `2 launches × 10 warmups × 20 measured iterations` 下，对两个干净提交执行相邻 A→B：

| 顺序 | Commit | Mean | StdDev | Median | 99.9% CI | N | Lock contentions | Allocated |
|---:|---|---:|---:|---:|---:|---:|---:|---:|
| A | baseline `5a98c988` | 12.152ms | 0.293ms | 12.126ms | 11.985–12.319ms | 39 | 21.8125 | `-` |
| B | optimized `e020f476` | 11.634ms | 0.304ms | 11.675ms | 11.463–11.805ms | 40 | 21.8750 | `-` |

两份 99.9% CI 不重叠，本机相邻提交改善约 4.263%，优化后约 `5.369ns/active cell`。但 `2,166,784 / 11.634ms` 折算 8ms 只有约 `1,489,967 active cells`，仍比 2M 下限少 25.50%。因此本报告只证明当前机器上的通用热路径进展；`PERF-003` 必须继续保持阻塞，不能用本机单 RID、最好样本或旧连续帧报告宣称完成。

提交前的探索性 A-B-A-B 还得到 A1 `11.643ms`、B1 `11.370ms`、A2 `12.152ms`、B2 `11.266ms`，两组相邻 A→B 方向一致，但跨进程基线漂移明显。为保证 provenance，本索引条目只采用可检出的 `5a98c988` A2 与 `e020f476` B3，不把未提交候选运行登记为稳定证据。

## 可复现命令

基线使用指向 `5a98c98819e6952aa9505090392cea05ba973fb0` 的 detached worktree，优化后使用干净提交 `e020f47681fb66a272aa80caf70d100b0c717ba9`。两边执行相同命令：

```powershell
$bdn = @(
    '--filter', '*FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames*',
    '--launchCount', '2',
    '--warmupCount', '10',
    '--iterationCount', '20')

& .\tools\run-benchmark.ps1 `
    -Artifacts 'artifacts/perf-003-<commit>-independent-20260718' `
    -BenchmarkDotNetArgs $bdn
```

报告中的 job 均明确记录 `InvocationCount=1`、`IterationCount=20`、`LaunchCount=2`、`UnrollFactor=1`、`WarmupCount=10`，每个 measured workload 含 16 个独立 full-dirty frame。两轮均成功执行 1 个 benchmark，完整保留 39/40 个有效样本且没有托管分配。

## 原始报告与哈希

`artifacts/` 是可再生原始输出；本文件与 Evidence Index 是长期稳定登记。

| Run | 原始文件 | SHA256 |
|---|---|---|
| baseline A2 | `artifacts/perf-003-5a98c988-independent-a2-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `8CB4A23A7677864A8B20658955CF848EF7F89FAAF922CB89C63D8BEEBAAB99A1` |
| baseline A2 | `artifacts/perf-003-5a98c988-independent-a2-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-20260718-063137.log` | `ED5F9B7A7E9A913F2EFBA390C765B4EF5CB509983EE2A9E0E32A0D71D9A11783` |
| optimized B3 | `artifacts/perf-003-e020f476-independent-b3-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `9C3495AC523F357C462A51A3F471D8B9A0D1D35502059E581391A195E2759C83` |
| optimized B3 | `artifacts/perf-003-e020f476-independent-b3-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-20260718-063940.log` | `E524C75887D434130A5EF9D477C63A3F32C6D01F7E7E084C7241D02FC71099ED` |

## 正确性与边界

- `PixelEngine.Simulation.Tests`：197 passed / 0 skipped / 0 failed，覆盖质量守恒、checkerboard、parity、跨 chunk movement、KeepAlive、reaction 与 dirty 生命周期。
- `PerformanceHardeningToolingDisciplineTests`：128 passed / 0 skipped / 0 failed，覆盖独立初态 benchmark 结构、目标 evidence preflight、旧 262K 报告拒绝和既有工具边界。
- `PerformanceHardeningHotPathDisciplineTests` 与新增热路径合同定向回归：2 passed / 0 skipped / 0 failed。
- `PixelEngine.sln` Release + `TreatWarningsAsErrors=true`：0 warning / 0 error。
- 本机只有 win-x64 / AVX2；缺其余五个 RID 的代表硬件、elevated ETW cache/branch counters、AVX-512 净损和玩家包真实窗口 60s/3600-frame phase p99。因此本报告不能关闭 `PERF-003`、`PERF-008` 或 `PERF-009`。
