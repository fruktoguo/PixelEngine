# 2026-07-10 PERF-006 resident chunk 批量快照证据

taskIds: `PERF-006`
implementationCommit: `ff221659d513688659b00388d44d8b19964f5cc0`
testGateCommit: `ef04492d6d6657753ce80459f2df026cbe08b51d`
benchmarkRunId: `local-20260710-perf006-resident-chunk-batch-committed`
runIdentityStatus: `captured`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

PERF-006 已完成：

- `ResidentChunkMap.AddRange` 预验证批次唯一性、一次扩容 Dictionary，并只重建一次 `ResidentChunks` snapshot；`RemoveRange` 与 `Clear` 对称地避免批量摘除/清空的 O(n²) snapshot rebuild。
- snapshot、WorldStreamer request/prepared/loaded/detached scratch 均改为指数扩容；单元素批次走 `Add` 快速路径，避免小批次引入额外校验容器。
- `WorldStreamer.ApplyPrepared` 批量插入后台加载 chunk，`SubmitUnloads` 批量摘除；存档加载和 Hosting 的固定/procedural 初始驻留也使用 batch API。
- 读档覆盖现有世界时 `WorldSaveService.ClearWorld` 通过一次 `ResidentChunkMap.Clear` 清空 live map；metadata 仍逐条移除，保持既有 residency 语义。

新增 1/16/64/256 chunk 的 xUnit 测试断言每批只重建一次 snapshot，WorldStreamer 集成测试同时验证 live count、ResidencyTable、MemoryBudget 和 Cached 状态。

## 可复现命令

```powershell
dotnet build PixelEngine.sln -c Release --no-restore
dotnet test tests/PixelEngine.World.Tests/PixelEngine.World.Tests.csproj -c Release --no-restore
dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-restore
dotnet test PixelEngine.sln -c Release --no-build --no-restore -m:1
pwsh -NoProfile -File tools/run-benchmark.ps1 -Artifacts artifacts/perf-006-resident-chunk-batch-committed -BenchmarkDotNetArgs @("--filter=*ResidentChunkMapBenchmarks*", "--job=short", "--warmupCount=1", "--iterationCount=5")
pwsh tools/validate-task-catalog.ps1
pwsh tools/validate-evidence-index.ps1
git diff --check
```

## BenchmarkDotNet 结果

BDN `Mean`/`StdDev`/`Median`/`Allocated` 来自提交后原始报告。p99 是从同一日志按方法和 `ChunkCount` 分组、排序 BDN `WorkloadResult` 后线性插值得到；只使用 BDN 保留的 measured workload 样本，未把被移除的 outlier 混入。

| 方法 | ChunkCount | Mean | StdDev | Median | p99 | 有效样本 | Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|
| `AddIndividually` | 1 | 2.456 μs | 0.219 μs | 2.450 μs | 2.850 μs | 97 | 216 B |
| `AddRange` | 1 | 2.393 μs | 0.168 μs | 2.400 μs | 2.709 μs | 92 | 216 B |
| `AddIndividually` | 16 | 9.715 μs | 1.202 μs | 10.000 μs | 12.902 μs | 99 | 1,592 B |
| `AddRange` | 16 | 5.261 μs | 0.258 μs | 5.300 μs | 5.700 μs | 67 | 1,096 B |
| `AddIndividually` | 64 | 55.714 μs | 2.038 μs | 55.350 μs | 61.905 μs | 66 | 10,168 B |
| `AddRange` | 64 | 13.163 μs | 2.170 μs | 12.050 μs | 17.630 μs | 100 | 4,072 B |
| `AddIndividually` | 256 | 392.628 μs | 295.342 μs | 549.350 μs | 1,022.556 μs | 100 | 47,784 B |
| `AddRange` | 256 | 36.021 μs | 0.518 μs | 36.200 μs | 36.687 μs | 14 | 16,264 B |

相对逐个 Add，AddRange 的 Mean 加速约为：1 chunk `1.03×`、16 chunk `1.85×`、64 chunk `4.23×`、256 chunk `10.90×`；256 chunk 分配降至基线的 34%。1 chunk 走快速路径，分配和延迟与逐个 Add 持平。

BDN 是本机短基准，用于证明 PERF-006 的相对复杂度、分配和尖峰改善，不替代 plan/16 的目标硬件长跑与最终帧预算证据。

## 正确性与回归

- `PixelEngine.World.Tests`：38 passed、0 failed；包含 Map batch add/remove、WorldStreamer 1/16/64/256 批量加载、流式装卸、存档往返、LRU/内存预算和长距离平移。
- `PixelEngine.Hosting.Tests`：452 passed、4 skipped、0 failed；4 个跳过项为显式 GL/window smoke，静态性能门禁已同步当前 scratch 与 movement helper 实现。
- 其余 solution 测试与上述两组构成：`1483 passed、34 skipped、0 failed`。
- `dotnet build PixelEngine.sln -c Release --no-restore`：0 warnings、0 errors。
- 原始 BDN Markdown 报告：`artifacts/perf-006-resident-chunk-batch-committed/results/PixelEngine.Benchmarks.ResidentChunkMapBenchmarks-report-github.md`
  - SHA256: `4168EF8290ECDA15E5FA9E0B4A848280074EC142B87DD1AF3A6BF216557DC8E7`
- 原始 BDN 日志：`artifacts/perf-006-resident-chunk-batch-committed/PixelEngine.Benchmarks.ResidentChunkMapBenchmarks-20260710-101817.log`
  - SHA256: `80FA058A52E8A6E8FD4D78157F84A5D4B06D0672086DC140B307AEA4E659CCC2`

