# 2026-07-18 PERF-003 列占用索引证据

taskIds: `PERF-003`
commit: `5de61121cf8c59646b90099543de8a4fa12f8fcb`
runSessionId: `local-20260718-perf003-column-occupancy`
runIdentityStatus: `captured_commit_bound_local_formal_benchmark`
evidenceState: `local_formal_benchmark_target_gap_statistically_separated`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

`5de61121` 为每个 64x64 chunk 增加 64 列 x 2 个 32-bit half 的派生占用位图。垂直 movement 在首格为空后，不再逐格读取最多 31 个 strided `Material` cell，而是用 mask 与 `TrailingZeroCount` 定位 `MoveCap` 内首个非空 cell。位图只占 512B/chunk，即 0.125B/cell，不进入存档，也不替代 CPU Material SoA 权威状态。

提交态 B13 full-active 为 9.266ms、`Allocated -`，相对同 fixture 的 detached control A10 10.887ms 改善 14.89%；两份 99.9% CI 为 9.106-9.426ms 与 10.134-11.640ms，不重叠。A10 full-active 有明显双峰，launch means 为 12.167/9.606ms；B13 为 9.444/9.089ms。即使用较慢 candidate launch 对较快 control launch，方向仍改善约 1.70%，但不把单 launch 差值当正式效应量。

B13 按 mean 折算约 1,870,739 cells/8ms，按 99.9% upper CI 9.426ms 折算约 1,838,985 cells/8ms，仍低于 2M 下限 6.46% / 8.05%。因此本节点是机制和本机性能进展，不关闭 `PERF-003`，也不替代六 RID 代表硬件与产品级 frame p99 证据。

独立 Typical Dirty 同时从 A10 的 20.061us 降到 B13 的 14.506us，mean 改善 27.69%；99.9% CI 为 19.462-20.661us / 13.897-15.114us，均为零分配。该 workload 只验证 16x16 dirty 初态的一帧成本，不代表 full-active 达标。

## 数据结构与并发合同

- 低 32 行和高 32 行分别存于独立 `uint` word。checkerboard 同 pass 中，向南和向北的合法 32px halo 写不会对同一 word 做并发 read-modify-write。
- `Chunk.SetMaterialAt`、`NeighborWindow` 的 set/swap/movement、reaction、custom update 与 lifetime 路径在 Material 的空/非空状态变化时增量维护位图。
- `MaterialBuffer` 的内部 raw alias 获取会保守地把位图标为失效；下一次进入 active CA 前从权威 Material SoA 重建。`SetCurrentDirty` / `SetWorkingDirty` 也保证公开 bulk 写入边界先同步索引。
- scheduler 只为实际 active neighborhood 的 center 与 south chunk 调用 `EnsureColumnOccupancy`；sleeping 远区不扫描 Material SoA，也不制造每帧分配。
- 查询最多读取 center/south 两个 chunk 的连续 half-word；仍遵守 `MoveCap=32`、中心/南邻寻址、parity、RigidOwned damage、dirty 与跨 chunk KeepAlive 语义。

并发回归连续 100 轮在同一 checkerboard pass 内同时驱动北侧 powder 写入中间 chunk 的低 half、南侧 gas 写入高 half，确认无位图 lost update。边界测试覆盖列 0/31/32/63、raw alias 失效与重建、reset、跨 chunk south obstacle、同 pass movement 后立即查询，以及 CellGrid/NeighborWindow 的 set/swap。

## BenchmarkDotNet 提交态对照

两种 workload 都使用 2 launches x 10 warmups x 20 measured iterations。B13 从干净 `5de61121` 运行；A10 在 detached `75859635` 上只把 `TypicalDirtyCellThroughputBenchmark.FramesPerInvoke` 从 8,192 调整为与提交态相同的 12,288，未改 production code。该 fixture-only 对齐使 typical workload 超过 BenchmarkDotNet 最小迭代时间，并保持两边每 operation 语义相同。

### Full-active 2M

`FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames` 一次计时推进 16 个互不共享的 2,166,784-cell full-dirty kernel。

| Run | Source | Mean | StdDev | Median | 99.9% CI | N | Launch means | mValue | Code size | Allocated |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| A10 | detached `75859635` + fixture-only parity patch | 10.887ms | 1.339ms | 10.823ms | 10.134-11.640ms | 40 | 12.167 / 9.606ms | 3.8 | 15,204B | `-` |
| B13 | clean `5de61121` | 9.266ms | 0.284ms | 9.304ms | 9.106-9.426ms | 40 | 9.444 / 9.089ms | 2.8 | 15,020B | `-` |

A10 双峰被完整保留，不挑选较慢簇放大收益；B13 也保留全部 40 个 measured samples。正式结论使用完整报告 mean/CI，launch means 只用于透明展示主机波动。

### Typical Dirty 16x16

| Run | Source | Mean | StdDev | Median | 99.9% CI | N | Launch means | mValue | Code size | Allocated |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| A10 | detached `75859635` + fixture-only parity patch | 20.061us | 1.034us | 20.298us | 19.462-20.661us | 38 | 20.716 / 19.406us | 2.14 | 24,911B | `-` |
| B13 | clean `5de61121` | 14.506us | 1.082us | 14.540us | 13.897-15.114us | 40 | 15.052 / 13.959us | 2.67 | 20,174B | `-` |

## JIT 与 EventPipe

Release、`DOTNET_ReadyToRun=0`、`DOTNET_TieredCompilation=0`、`DOTNET_JitDisasm=PixelEngine.Simulation.ChunkUpdater:TryMoveDown*` 的 FullOpts 汇编为：

| 方法 | FullOpts code size | 作用 |
|---|---:|---|
| `TryMoveDown` | 2,480B | 首格读取、密度/方向分派与 empty-column helper 边界 |
| `TryMoveDownThroughEmptyColumn` | 2,911B | 位图定位、目标语义复核与 movement 提交 |

提交态短 EventPipe 使用 1 launch x 3 warmups x 3 measured iterations，仅作调用图诊断。跨线程 inclusive frame time 中，`UpdateChunk` 约 12,011.63ms，`TryMoveDownThroughEmptyColumn` 约 2,726.38ms；`TryFindFirstOccupiedBelow` 与 `FindFirstOccupiedInColumn` 仅约 56.38/44.45ms。`RebuildColumnOccupancy` 的约 335.52ms 主要包含 benchmark 初态批量 raw-alias 写入后的 setup 重建，不能当作稳态每帧成本；active scheduler 的 `EnsureColumnOccupancy` inclusive 约 2.00ms。

## 已拒绝的探索方案

以下未提交实验均已完整回退，不属于 B13：固定 two-word mapping 特化使 full-active 到 9.407ms；用 `Unsafe.Add` 消除查询 bounds check 的相邻对照为 9.706ms，对恢复 bounds 的 9.284ms；known-occupied target 特化的正式 B12 为 9.430ms，差于同代码基线 B11 9.071ms；无 loop 的 flat query 让 helper 从 2,912B 膨胀到 3,025B。它们说明当前剩余成本不应靠未经证明的 code-size 扩张继续堆叠。

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
    -Artifacts 'artifacts/perf-003-5de61121-column-occupancy-b13-20260718' `
    -BenchmarkDotNetArgs $bdn

$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_JitDisasm = 'PixelEngine.Simulation.ChunkUpdater:TryMoveDown*'
$env:DOTNET_JitDisasmAssemblies = 'PixelEngine.Simulation'
$env:DOTNET_JitDisasmDiffable = '1'

dotnet test tests/PixelEngine.Simulation.Tests/PixelEngine.Simulation.Tests.csproj `
    -c Release --no-build --no-restore `
    --filter 'FullyQualifiedName~SimulationMovementTests.StepCaClampsPowderVerticalCollapseToMoveCap'
```

EventPipe 诊断在同一 commit 上追加 `--launchCount 1 --warmupCount 3 --iterationCount 3 --profiler EP`。

## 原始报告与 SHA256

`artifacts/` 是可再生原始输出；本文件与 Evidence Index 是长期稳定登记。

| Run | 原始文件 | SHA256 |
|---|---|---|
| A10 combined log | `artifacts/perf-003-75859635-column-control-a10-20260718/BenchmarkRun-20260718-103440.log` | `600ED246598274B5AA37C0202B3778B4052B8310C67CC697D6CC9219A7311AE1` |
| A10 full-active | `artifacts/perf-003-75859635-column-control-a10-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `84AB32A2F72A51D112C312AA5027D1DB6C6147B1281D2EAEDBEFF2CD0C02826F` |
| A10 typical | `artifacts/perf-003-75859635-column-control-a10-20260718/results/PixelEngine.Benchmarks.TypicalDirtyCellThroughputBenchmark-report-github.md` | `38F68FC82F4E79F142438CB35E5D57767E599F710C27BF8C557A18EAC553665C` |
| B13 combined log | `artifacts/perf-003-5de61121-column-occupancy-b13-20260718/BenchmarkRun-20260718-140112.log` | `A6F604E2819F726EEEC3D337344B4F96E8C81C73A0E5709DC0339E9B6BBC905C` |
| B13 full-active | `artifacts/perf-003-5de61121-column-occupancy-b13-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `235DB8923BAAC1E489301D327AC073F52D9B44E3BF53671DBD61185C30315D40` |
| B13 typical | `artifacts/perf-003-5de61121-column-occupancy-b13-20260718/results/PixelEngine.Benchmarks.TypicalDirtyCellThroughputBenchmark-report-github.md` | `CD19AA038F6AD57115CDC730B5299E97D13F31667F72745A5223934E179E89BE` |
| B13 JIT | `artifacts/perf-003-5de61121-column-occupancy-jit-20260718/ChunkUpdater.TryMoveDown.column.asm.txt` | `629E54456CE8CA32B4FEAA58E8AD23C470FDEA78CE7472273818D6394A8208C2` |
| B13 EventPipe log | `artifacts/perf-003-5de61121-column-occupancy-eventpipe-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-20260718-140957.log` | `B10ACE942D2A5CC5259BBEEC98BBA8E445B4758A745AFB8A080B797D788D686A` |
| B13 EventPipe trace | `artifacts/perf-003-5de61121-column-occupancy-eventpipe-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames-20260718-141153.nettrace` | `EC6F6ECA7F2C09C53B73BAE1D1B1AC8E24B1A091F5958776B7715E5646C74D8C` |
| B13 speedscope | `artifacts/perf-003-5de61121-column-occupancy-eventpipe-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames-20260718-141153.speedscope.json` | `311A6D9B2CC495E2FA415D3FC2D091D15353F3218A1308EDDEA2067EAD13C9E5` |

## 正确性与剩余阻塞

- `PixelEngine.Simulation.Tests`：204 passed / 0 skipped / 0 failed；另连续 10 轮共 2,040 次通过。
- `PerformanceHardening*`：159 passed / 0 skipped / 0 failed；包含列位图结构、raw alias 边界、scheduler active-only 重建和 benchmark fixture 静态门禁。
- 相关 World / Serialization / Physics 回归分别为 44 / 55 / 82 passed；`PixelEngine.sln` Release + `TreatWarningsAsErrors=true` 为 0 warning / 0 error。
- task catalog、Evidence Index、目标文件 format、Schema 与 `git diff --check` 在证据提交前重新验证。
- 本机 B13 仍低于 2M/8ms，且只有 win-x64 / AVX2。缺其余五个 RID 代表硬件、elevated ETW cache/branch counters、AVX-512 净损和玩家包 60s/3600-frame phase p99，因此本报告不能关闭 `PERF-003`、`PERF-008` 或 `PERF-009`。
