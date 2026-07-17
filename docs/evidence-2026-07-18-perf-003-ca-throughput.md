# 2026-07-18 PERF-003 CA 热路径与稳态 2M 对照证据

taskIds: `PERF-003`
implementationCommit: `fd0e3466267bbed78fa6fc0dd5a3bae11717c484`
benchmarkToolHardeningCommit: `d3dcfc0a04db120ec6883866ad9e1db3b2eed28c`
baselineCommit: `c2c2adb319e3c78055c1f376bd1769d2025c3105`
benchmarkRunIds: `local-20260718-perf003-baseline-a`、`local-20260718-perf003-optimized-a`、`local-20260718-perf003-baseline-b`、`local-20260718-perf003-optimized-b`
runIdentityStatus: `captured`
evidenceState: `local_formal_benchmark_target_gap`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

本轮在不改变单缓冲原地更新、4-pass checkerboard、parity、32px MoveCap、KeepAlive 和 CPU sim 权威的前提下继续收紧通用 CA movement 热路径：

- 中心 chunk 的 movement dirty 由逐 cell 重复 union 改为逐扫描行归并；源/同 chunk 目标的精确 padding bounds 在 `finally` 提交，跨 chunk 目标仍即时写 KeepAlive。
- 中心目标使用 source local + delta 直接得到 target local；垂直扫描把已验证的 target slot/local 直接传到 swap，不再二次寻址和重复 eligibility 读取。
- 正下方目标单独处理，空列扫描从 step 2 开始；私有 movement helper 的 32px 约束由调用结构保证并保留 Debug assertion，公开输入边界与 MoveCap 行为测试未放宽。
- EventPipe profiler 改为显式环境开关；`run-benchmark.ps1` 同时修正 `--name=value` 参数传递并对空 artifacts、空 report、CLI parser error 和 0 benchmark fail-closed。

在相同硬件、相同 `FullActive2M` 夹具、相同 8 worker 和固定 `2 launches × 15 warmups × 30 measured iterations` 下，按 baseline→optimized→baseline→optimized 交错运行：

| 顺序 | Commit | Mean | StdDev | Median | 99.9% CI | N | Lock contentions | Allocated |
|---:|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | baseline `c2c2adb3` | 12.303ms | 1.292ms | 12.540ms | 11.725–12.881ms | 60 | 21 | `-` |
| 2 | optimized `fd0e3466` | 11.664ms | 1.298ms | 11.814ms | 11.084–12.245ms | 60 | 19 | `-` |
| 3 | baseline `c2c2adb3` | 14.029ms | 1.858ms | 13.966ms | 13.198–14.860ms | 60 | 21 | `-` |
| 4 | optimized `fd0e3466` | 10.603ms | 1.065ms | 10.361ms | 10.127–11.080ms | 60 | 21 | `-` |

主机噪声显著，因此不把两组均值简单合并后的 15.44% 当作产品保证。采用最保守的“最慢 optimized 11.664ms 对最快 baseline 12.303ms”仍为 5.19% 改善；以下当前事实也只使用较慢 optimized 轮：2,166,784 active cells / 11.664ms，折算 8ms 约 `1,486,134 active cells`，仍比 2M 下限少 25.69%。PERF-003 必须继续保持阻塞，不能用个别 8–9ms 样本、最好轮或本机单 RID 结果宣称达标。

## 可复现命令

优化前使用指向 `c2c2adb319e3c78055c1f376bd1769d2025c3105` 的 detached 临时 worktree；优化后使用 `fd0e3466267bbed78fa6fc0dd5a3bae11717c484`。两边均由各自仓库中的 wrapper 再创建排除 `bin/obj/artifacts` 的隔离副本：

```powershell
& .\tools\run-benchmark.ps1 `
  -Project 'bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj' `
  -Artifacts 'artifacts/perf-003-<commit>-fullactive2m-steady-20260718' `
  -BenchmarkDotNetArgs @(
    '--filter', '*CellThroughputBenchmark.StepJobSystem*FullActive2M*',
    '--launchCount', '2',
    '--warmupCount', '15',
    '--iterationCount', '30')
```

报告中的 job 均明确记录 `InvocationCount=1`、`IterationCount=30`、`LaunchCount=2`、`UnrollFactor=1`、`WarmupCount=15`。BDN 保留 `MinIterationTime` warning，因为单次 CA step 小于建议的 100ms；每轮仍有 60 个 measured workload、完整分布和成功退出，且没有 `Generate Exception`、空 workload、0 benchmark 或托管分配。

## 原始报告与哈希

`artifacts/` 是可再生原始输出；本文件与 Evidence Index 是长期稳定登记。

| Run | 原始文件 | SHA256 |
|---|---|---|
| baseline A | `artifacts/perf-003-c2c2adb3-fullactive2m-steady-20260718/results/PixelEngine.Benchmarks.CellThroughputBenchmark-report-github.md` | `FCA4AFF9BD6DA3DE1556CE5AEE5CEB3D31164F5017A0322E297A4373FCC96FB3` |
| baseline A | `artifacts/perf-003-c2c2adb3-fullactive2m-steady-20260718/PixelEngine.Benchmarks.CellThroughputBenchmark-20260718-043711.log` | `CB2AEAE8329F9EEB5802B50206ED9D9E31D9FA7384979FD0AC868B9921E28E03` |
| optimized A | `artifacts/perf-003-fd0e3466-fullactive2m-steady-20260718/results/PixelEngine.Benchmarks.CellThroughputBenchmark-report-github.md` | `F60B3CDADB1A577CB1E45DA6320A5A788FDF4BAA0F51DDDAFF9D0751B04C1574` |
| optimized A | `artifacts/perf-003-fd0e3466-fullactive2m-steady-20260718/PixelEngine.Benchmarks.CellThroughputBenchmark-20260718-043942.log` | `8F7A6F2C2FC40E72AF212B1A3680739D9AFD322A0305CBFCD547354E2544477F` |
| baseline B | `artifacts/perf-003-c2c2adb3-fullactive2m-steady-repeat-20260718/results/PixelEngine.Benchmarks.CellThroughputBenchmark-report-github.md` | `A853341207E5254F261687BAC2F1614DF9AC318B84E5B98EE77C19BB61237448` |
| baseline B | `artifacts/perf-003-c2c2adb3-fullactive2m-steady-repeat-20260718/PixelEngine.Benchmarks.CellThroughputBenchmark-20260718-044204.log` | `C65270F1080852182C1803E7BAF6035A3F2640A15F2678349FA4CCDBB8C4EE7A` |
| optimized B | `artifacts/perf-003-fd0e3466-fullactive2m-steady-repeat-20260718/results/PixelEngine.Benchmarks.CellThroughputBenchmark-report-github.md` | `CD7EADE0B4D25FEE9452ED2653FCFFCC55AF804E62D56C3243BF7676D7EAACF6` |
| optimized B | `artifacts/perf-003-fd0e3466-fullactive2m-steady-repeat-20260718/PixelEngine.Benchmarks.CellThroughputBenchmark-20260718-044420.log` | `AB2AD6792FC67116993EE2DA67648AF2C9FB18A8A25C801D6C12EEB85AA0B883` |

## 正确性与边界

- `PixelEngine.Simulation.Tests`：197 passed / 0 skipped / 0 failed；新增“movement 已发生而 reaction 抛异常时仍提交精确 dirty”回归。
- 实现提交的 Solution Release build：0 warning / 0 error；14 个测试工程合计 2,286 passed / 48 环境门控 skipped / 0 failed，其中 Hosting 957 passed / 7 skipped。
- benchmark 四轮均为 0 B managed allocation；睡眠 chunk、typical dirty、KeepAlive、跨 chunk movement、质量守恒、parity 和 32px MoveCap 既有测试保持通过。
- 本机只有 win-x64 / AVX2；缺其余五个 RID 的代表硬件、elevated ETW cache/branch counters、AVX-512 净损和真实玩家包目标帧预算。因此本报告只能证明通用热路径的本机进展，不能关闭 PERF-003 或重校准产品指标。
