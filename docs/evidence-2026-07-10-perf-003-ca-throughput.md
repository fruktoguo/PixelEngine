# 2026-07-10 PERF-003 CA 吞吐与 2M profile 证据

taskIds: `PERF-003`
implementationCommit: `ff90cf44c0e8c4364120e246d858bfc56c330af1`
hotpathImplementationCommit: `0bab515c17d05790d161b2231a7c7a7e09d07c94`
benchmarkRunId: `local-20260710-perf003-full-active-2m-8workers`
runIdentityStatus: `captured`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

本轮完成了不改变 CA 架构不变式的通用 movement 热路径优化，并把 `CellThroughputBenchmark` 从仅有 262,144 active cells 的夹具扩展为独立的 `FullActive2M` profile。优化包含：

- `ChunkUpdater` 将中心 source 的稳定 `localOffset` 贯穿 powder/liquid/gas movement，避免提交阶段重复解析 source slot/local。
- 垂直扫描在中心 chunk 内直接复用 slot 4 SoA 基址，并携带已解析的 target slot/local 到最终提交，避免“探测后再寻址”。
- `dispersion=0` 的液体跳过两个必然失败的水平扫描；`WorkingDirty` 已是 `DirtyRect.Full` 时跳过重复 union。
- 保留单缓冲原地更新、4-pass checkerboard、parity、防密度错误置换、RigidOwned damage、Damage 清零、KeepAlive 和 32px MoveCap；`DirtyRectLifecycleTests` 新增 Full dirty 合并回归。

在代表本机 8 physical cores 的 8 worker 配置下运行 `FullActive2M`：23×23 active chunks，即 2,166,784 active cells；`StepJobSystem` 平均 `12.965ms`，StdDev `0.792ms`，96 个有效 workload 样本，`Allocated -`。折算为 8ms 预算约 `1,337,005 active cells`，仍低于 2M 下限，不能将 PERF-003 标记为完成，也不足以单凭本机结果正式重校准架构目标。

同一轨道的 262,144-cell 当前基线为 `StepJobSystem 4.122ms`（旧 4-worker 夹具，约 508,770 cells/8ms）；该小 profile 仅用于局部回归，不能替代 2M profile。8-worker 结果表明，使用全部代表物理核后吞吐明显改善，但距离原目标仍约 1.5×，需要目标硬件、产品场景和降级策略共同决定是否继续优化或重校准。

## 可复现命令

以下 benchmark 命令在 `ff90cf44c0e8c4364120e246d858bfc56c330af1` 执行；`tools/run-benchmark.ps1` 使用隔离工作副本，防止仓库内同名 worktree 干扰 BDN discovery。

```powershell
dotnet build bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj -c Release --no-restore -m:1
pwsh -NoProfile -File tools/run-benchmark.ps1 -Project "bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj" -Artifacts "artifacts/perf-003-full-active-2m-8workers-committed" -BenchmarkDotNetArgs @('--filter=*CellThroughputBenchmark.StepJobSystem*FullActive2M*','--launchCount=1','--warmupCount=3','--iterationCount=5')
dotnet test tests/PixelEngine.Simulation.Tests/PixelEngine.Simulation.Tests.csproj -c Release --no-restore -m:1
pwsh -NoProfile -File tools/validate-task-catalog.ps1
git diff --check
```

## BenchmarkDotNet 原始结果

环境为 BenchmarkDotNet `0.15.8`、X64 RyuJIT `x86-64-v3`、AVX2、Concurrent Workstation GC。原始报告包含 `MinIterationTime` warning，但没有 `Generate Exception`、NA、空 workload result 或进程失败；`MemoryDiagnoser` 报告无托管分配。

| Benchmark | Profile | Mean | StdDev | 有效样本 | Allocated |
|---|---|---:|---:|---:|---:|
| `CellThroughputBenchmark.StepJobSystem` | `FullActive2M` | 12.965ms | 0.792ms | 96 | `-` |

原始 artifacts：

- Markdown 报告：`artifacts/perf-003-full-active-2m-8workers-committed/results/PixelEngine.Benchmarks.CellThroughputBenchmark-report-github.md`
  - SHA256: `F9A70996AD54FECAAFFF472ACA23206CA40D71FA796EB3E0FEAA259686276ECB`
- BenchmarkDotNet 日志：`artifacts/perf-003-full-active-2m-8workers-committed/PixelEngine.Benchmarks.CellThroughputBenchmark-20260710-085432.log`
  - SHA256: `22AC403753D5221391EB779BCB30522EF742BE9439102B69B3511C4E2E2E3E53`

## 正确性与边界

- `PixelEngine.Simulation.Tests`：192 passed、0 failed；覆盖 movement/parity、checkerboard、质量守恒、KeepAlive、反应和 dirty 生命周期。
- Simulation 与 Benchmark 项目 Release build：0 warnings、0 errors。
- 本机 Ryzen 7 5800X 只有 AVX2；没有 AVX-512 降频净损、elevated ETW Cache Misses/Branch Mispredictions、其他 5 个 RID 的代表硬件或真实玩家包 60 秒/3600 帧证据。
- 因此本报告是 PERF-003 的本机正式规模校准与实现证据，不是 2–4M cells/8ms 达标证据；任务仍需保持阻塞，后续应在目标硬件证据可用或产品指标决策冻结后继续。
