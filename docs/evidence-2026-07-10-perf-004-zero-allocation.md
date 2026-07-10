# 2026-07-10 PERF-004 零分配路径证据

taskIds: `PERF-004`
implementationCommit: `2551c2aa58077901e2b32cc39b45a2540f1d83f1`
benchmarkRunId: `local-20260710-perf004-zero-allocation`
runIdentityStatus: `captured`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

PERF-004 的两个实际分配点已消除：

- `SimulationKernel.EditRectAtInputPhase` 和 `ClearRectAtInputPhase` 移除捕获 `RowRunAction` lambda，改为显式 chunk/row-run 循环；原有跨 chunk 切片、RigidOwned damage 通知、SoA 清理和统一 dirty 标记语义保持不变。
- `PhysicsSystem.SyncStep` 的 erase 与 inverse-sample 阶段改为内联 `Stopwatch.GetTimestamp`、调用和 `RecordSub`，移除实例方法组到 `Func<int>` 的转换；计时顺序和 profiler 记录语义保持不变。

调用级 GC 计数和 BenchmarkDotNet `MemoryDiagnoser` 均证明稳态调用为 `0 B`，行为测试全绿，因此满足 PERF-004 验收条件。

## 可复现命令

以下 benchmark 使用 `tools/run-benchmark.ps1` 的隔离工作副本；实现提交前运行时工作树内容与 `2551c2aa` 提交字节一致，提交后未再修改相关源码。

```powershell
dotnet test tests/PixelEngine.Simulation.Tests/PixelEngine.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SimulationPhaseInterfaceTests"
dotnet test tests/PixelEngine.Physics.Tests/PixelEngine.Physics.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PhysicsSyncTests"
pwsh -NoProfile -File tools/run-benchmark.ps1 -Artifacts artifacts/perf-004-simulation-rect -BenchmarkDotNetArgs "--filter=*SimulationAllocationBenchmarks.*RectAtInputPhaseSteadyState*"
pwsh -NoProfile -File tools/run-benchmark.ps1 -Artifacts artifacts/perf-004-physics-sync -BenchmarkDotNetArgs "--filter=*PhysicsBenchmarks.PhysicsSystemSyncStepSteadyState*"
pwsh tools/validate-task-catalog.ps1
pwsh tools/validate-evidence-index.ps1
git diff --check
```

## BenchmarkDotNet 原始结果

环境为 BenchmarkDotNet `0.15.8`、X64 RyuJIT `x86-64-v3`、AVX2、Concurrent Workstation GC。报告中的 `Allocated -` 表示每次操作没有可观测托管分配。

| Benchmark | 场景 | Mean | StdDev | 有效样本 | Allocated |
|---|---|---:|---:|---:|---:|
| `SimulationAllocationBenchmarks.EditRectAtInputPhaseSteadyState` | 跨 4 个 chunk 的 66×66 矩形写入 | 36.733μs | 4.287μs | 96 | `-` |
| `SimulationAllocationBenchmarks.ClearRectAtInputPhaseSteadyState` | 跨 4 个 chunk 的 66×66 矩形清空 | 26.091μs | 2.374μs | 86 | `-` |
| `PhysicsBenchmarks.PhysicsSystemSyncStepSteadyState` | `DamagedBodyCount=1` | 1.0259ms | 28.05μs | 27 | `-` |
| `PhysicsBenchmarks.PhysicsSystemSyncStepSteadyState` | `DamagedBodyCount=16` | 977.7μs | 28.77μs | 30 | `-` |

模拟基准有 `MinIterationTime` 和多模态分布提示，但没有异常、空结果或 benchmark 进程失败；物理基准只有正常 outlier 提示，两个参数场景均完成有效 workload。

原始 artifacts：

- Simulation Markdown 报告：`artifacts/perf-004-simulation-rect/results/PixelEngine.Benchmarks.SimulationAllocationBenchmarks-report-github.md`
  - SHA256: `674A2679DC925EB98A2ABEEA36FD15750A24B3ABDDD431F5B36EDC10473AF023`
- Simulation BenchmarkDotNet 日志：`artifacts/perf-004-simulation-rect/PixelEngine.Benchmarks.SimulationAllocationBenchmarks-20260710-090952.log`
  - SHA256: `C863C10399815EE5C95A254DA18DDCBB48928FB14CBC745EF748D932B6B735AB`
- Physics 第二次完整重跑 Markdown 报告：`artifacts/perf-004-physics-sync/BenchmarkDotNet.Artifacts/results/PixelEngine.Benchmarks.PhysicsBenchmarks-report-github.md`
  - SHA256: `F837E808E39D1A8F0462A0319ECA50CF1ADD5FED9AAB39386BD6BC1143DD4227`
- Physics 第二次完整重跑 BenchmarkDotNet 日志：`artifacts/perf-004-physics-sync/BenchmarkDotNet.Artifacts/PixelEngine.Benchmarks.PhysicsBenchmarks-20260710-091417.log`
  - SHA256: `E7BD9FFC4A3CEB4C3B9F88F48B8B7427267214B33A11A3BA9137C7F3BE38E1B5`

## 正确性与分配测试

- `SimulationPhaseInterfaceTests`：12 passed、0 failed；包含跨 chunk 批量编辑行为和 `GC.GetAllocatedBytesForCurrentThread` 的 edit/clear 0 B 断言。
- `PhysicsSyncTests`：8 passed、0 failed；包含 `SyncStepSteadyStateDoesNotAllocateAfterWarmup` 的 0 B 断言，以及 erase、inverse-sampling、角色 proxy 和 damage queue 回归。
- 源码扫描确认 `SimulationKernel` 不再包含 `ForEachChunkRowRun`、`RowRunAction`，`PhysicsSystem` 不再包含 `Func<int>`/`Measure` 方法组包装。
- `validate-task-catalog.ps1`、`validate-evidence-index.ps1` 和 `git diff --check` 必须在证据索引更新后再次执行并保持通过。
