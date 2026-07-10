# 2026-07-10 PERF-005 刚体破坏重建池化证据

taskIds: `PERF-005`
implementationCommit: `bb5fb05432db904320e57691e6fcd5fc1069deb3`
benchmarkRunId: `local-20260710-perf005-rigidbody-rebuild-pool`
runIdentityStatus: `captured`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

PERF-005 的破坏重建暂存已改为服务生命周期内复用，并在 Apply 完成或异常路径清理 transient 状态：

- `RigidBodyDestruction` 持久复用 damage `Dictionary`、局部 `HashSet` 池、work item、rebuild plan、worker hit 计数和每 worker 的几何 scratch。
- `TraceScratch` 持久复用 Marching Squares 的 `List`、边索引 `Dictionary`、used 标记、轮廓/简化点和 contour range 缓冲；worker 间仍保持独占，未引入锁。
- CCL、凸分解、碎片粒子和父子刚体守恒路径保持原有语义；`ChildBodyPlan` 在 Apply 后或异常时归还暂存的凸片数组并清空引用。

相对 PERF-001 基线，`Allocated` 从 166,240 B / 3,147,384 B 降至 74,808 B / 981,208 B，分别减少 55.00% / 68.82%。剩余分配主要来自本次重建产生的不可变 `BodyLocalMask`、子刚体和 Box2D/PhysicsWorld 状态；本任务的验收目标是移除高频编排暂存的重复分配，不宣称整个破坏操作为零分配。

## 可复现命令

```powershell
dotnet test tests/PixelEngine.Physics.Tests/PixelEngine.Physics.Tests.csproj -c Release --no-restore
dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PerformanceHardeningMemoryDisciplineTests"
pwsh -NoProfile -File tools/run-benchmark.ps1 -Artifacts artifacts/perf-005-rebuild-final -BenchmarkDotNetArgs @("--filter=*PhysicsBenchmarks.RigidBodyDestructionRebuildDirty*", "--job=short", "--warmupCount=1", "--iterationCount=5")
pwsh tools/validate-task-catalog.ps1
pwsh tools/validate-evidence-index.ps1
git diff --check
```

## BenchmarkDotNet 结果

BDN 报告保留 `Mean`、`StdDev`、`Median` 和 MemoryDiagnoser 的 `Allocated`。p99 是从同一原始日志中、按参数分组并排序 BDN `WorkloadResult` 样本后，以线性插值计算；样本是 BDN 去除异常值后的有效 workload 样本，不把被 BDN 标记移除的 outlier 混入 p99。

| DamagedBodyCount | Mean | StdDev | Median | p95 | p99 | 有效样本 | Allocated |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 635.8 μs | 469.4 μs | 420.3 μs | 1.4026 ms | 1.6362 ms | 99 | 74,808 B |
| 16 | 2.3541 ms | 489.0 μs | 2.2534 ms | 3.1095 ms | 3.9098 ms | 88 | 981,208 B |

本机短基准用于 PERF-005 的相对分配/延迟尖峰校准，不替代 plan/16 要求的目标硬件长跑和最终物理帧预算证据。

## 与 PERF-001 基线对照

| 场景 | PERF-001 Allocated | PERF-005 Allocated | 减少 |
|---|---:|---:|---:|
| `DamagedBodyCount=1` | 166,240 B | 74,808 B | 55.00% |
| `DamagedBodyCount=16` | 3,147,384 B | 981,208 B | 68.82% |

基线来源：`docs/evidence-2026-07-10-perf-001-benchmark-baseline.md`，对应原始 PERF-001 benchmark 运行；新旧运行均为本机 Ryzen 7 5800X / .NET 10 Release BDN，数据只用于方向性比较。

## 正确性与证据文件

- `PixelEngine.Physics.Tests`：80 passed、0 failed；包含 CCL、Marching Squares、Douglas-Peucker、凸分解、碎片转粒子、拆分像素守恒、速度转移、inverse-sampling 和 sleeping body 回归。
- `PerformanceHardeningMemoryDisciplineTests`：7 passed、0 failed；静态门禁确认 worker scratch 使用持久 pinned 数组，几何点缓冲经 `TraceScratch` 复用，ArrayPool 暂存存在归还路径。
- `dotnet build PixelEngine.sln -c Release --no-restore`：0 warnings、0 errors。
- 原始 BDN Markdown 报告：`artifacts/perf-005-rebuild-final/results/PixelEngine.Benchmarks.PhysicsBenchmarks-report-github.md`
  - SHA256: `5B296A7B5764A39CC70A530BDA941C00A7BC594B3EF9C236E34408807C65979A`
- 原始 BDN 日志：`artifacts/perf-005-rebuild-final/PixelEngine.Benchmarks.PhysicsBenchmarks-20260710-095538.log`
  - SHA256: `994BC7F5EC0FEAD09858F373967363FB1E8BA4CCBD3F323CB9BEC1B4E5A139D8`

