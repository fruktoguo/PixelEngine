# 2026-07-03 JobSystem ParallelRange 零分配证据

本报告补充 plan/02、plan/14 与 plan/16 的 JobSystem 派发热路径证据。目标机为本机 Windows 11 / AMD Ryzen 7 5800X / .NET 10.0.8，BenchmarkDotNet v0.15.8。命令使用 `Short` job、`WarmupCount=1`、`IterationCount=1`，用于确认本次修复后的多 worker 稳态派发不再分配托管对象；它不替代正式多迭代性能基线。

## 命令

```pwsh
dotnet build bench\PixelEngine.Benchmarks\PixelEngine.Benchmarks.csproj -c Release --no-restore
dotnet run --project bench\PixelEngine.Benchmarks\PixelEngine.Benchmarks.csproj -c Release --no-build -- --filter "*CoreAllocationBenchmarks.JobSystemParallelRange*" --job Short --warmupCount 1 --iterationCount 1 --exporters markdown
```

## BenchmarkDotNet 摘要

| Benchmark | Mean | Lock Contentions | Code Size | Allocated |
|---|---:|---:|---:|---:|
| `CoreAllocationBenchmarks.JobSystemParallelRangeMultiWorker` | 15.09 us | 0.2098 | 3,866 B | 0 B |
| `CoreAllocationBenchmarks.JobSystemParallelRangeRawMultiWorker` | 15.19 us | 0.2610 | 4,025 B | 0 B |

BenchmarkDotNet markdown 报告中的 `Allocated` 列以 `-` 表示 0 B/op；CSV 原始列明确为 `0 B`。报告位于 `BenchmarkDotNet.Artifacts\results\PixelEngine.Benchmarks.CoreAllocationBenchmarks-report-github.md` 与同名 `.csv`。

## 单测守门

`PixelEngine.Core.Tests.JobSystemTests` 额外锁定两条路径：

- `ParallelRangeMultiWorkerDispatchDoesNotAllocate`：先预热 reusable batch，再用 `GC.GetAllocatedBytesForCurrentThread()` 断言 `ParallelRange` 多 worker 稳态派发增量为 0。
- `ParallelRangeRawMultiWorkerDispatchDoesNotAllocate`：同样断言 Box2D task bridge 使用的 `ParallelRangeRaw` 多 worker 稳态派发增量为 0。

## 与旧报告的关系

`docs/benchmark-reports/2026-07-02-plan14-short.md` 与 `docs/benchmark-reports/2026-07-02-latency-branch-calibration.md` 中记录的 Core scaling 分配数字来自修复前的短跑。自本报告起，JobSystem `ParallelRange` / `ParallelRangeRaw` 派发分配证据以本报告为准；旧报告中的 CA 吞吐、纹理上传、反应查表、粒子积分、硬件计数器阻塞等结论不因本报告改变。
