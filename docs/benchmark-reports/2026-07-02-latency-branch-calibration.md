# 2026-07-02 延迟与分支校准记录

本记录覆盖 plan/16 §4.11。目标机为本机 Windows 11 / AMD Ryzen 7 5800X / .NET 10.0.8，BenchmarkDotNet v0.15.8。Benchmark 入口已在 `PIXELENGINE_BENCH_HARDWARE_COUNTERS=1` 时同时请求 `HardwareCounter.CacheMisses` 与 `HardwareCounter.BranchMispredictions`，用于后续按 cache miss / branch misprediction 分析热点，而不是按理论带宽下结论。

硬件计数器命令：

```pwsh
$env:PIXELENGINE_BENCH_HARDWARE_COUNTERS='1'; dotnet run --project bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj -c Release --no-build -- --filter "*ReactionLookupBenchmark.FindDirect*" --artifacts artifacts/hardware-counters --job short --warmupCount 1 --iterationCount 1 --exporters markdown
```

当前结果：BenchmarkDotNet 验证阶段提示 `Must be elevated (Admin) to use ETW Kernel Session (required for Hardware Counters and EtwProfiler).`，因此本机非管理员会话无法产出 Cache Misses / Branch Mispredictions 列。结论是工具链已经接入 cache-miss 与 branch-misprediction 计数器，但 §4.11 的实际硬件计数器分析需要管理员 PowerShell 或专用 CI runner 才能完成。

为避免该阻塞被静默跳过，新增硬件计数器预检脚本：

```pwsh
./tools/hardware-counter-preflight.ps1 -RunBenchmark
```

非 Windows runner 会写出 `blocked_non_windows` 报告；非管理员 Windows 会话会写出 `artifacts/hardware-counters/hardware-counter-preflight.md`，状态为 `blocked_non_admin`，并以非零退出；管理员 PowerShell 或专用 runner 会继续运行 BenchmarkDotNet，并检查 markdown 报告是否实际包含 `Cache Misses` 与 `Branch Mispredictions` 列。仅用于本地记录阻塞报告时可追加 `-AllowBlocked`，该模式不得作为硬件计数器验收依据。

多核曲线命令：

```pwsh
dotnet run --project bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj -c Release --no-build -- --filter "*CoreScalingBenchmark.ParallelRangeSum*" --artifacts artifacts/core-scaling --job short --warmupCount 1 --iterationCount 3 --exporters markdown
```

| WorkerCount | ItemCount | Mean | Allocated | 备注 |
|---:|---:|---:|---:|---|
| 1 | 65,536 | 18.58 us | 0 B/op | 小任务单线程最快。 |
| 2 | 65,536 | 38.18 us | 118 B/op | 调度开销超过收益。 |
| 4 | 65,536 | 33.67 us | 124 B/op | 仍慢于单线程。 |
| 0 | 65,536 | 166.33 us | 136 B/op | 自动 worker 在小任务下开销最大。 |
| 1 | 1,048,576 | 294.64 us | 0 B/op | 大任务单线程基线。 |
| 2 | 1,048,576 | 355.32 us | 136 B/op | 本次短跑噪声下慢于单线程。 |
| 4 | 1,048,576 | 169.66 us | 136 B/op | 对 1 worker 约 1.74x。 |
| 0 | 1,048,576 | 190.14 us | 136 B/op | 自动 worker 对 1 worker 约 1.55x。 |

结论：本机短跑不支持“核数越多必然越快”的预设。64K 小任务应优先单线程或更高回退阈值；1M 大任务 4 worker 有收益，但 2 worker 结果受调度与系统噪声影响明显。该数据只能代表当前 5800X 目标机，不满足 plan/16 要求的 6 RID 代表硬件 cells/frame 校准。
