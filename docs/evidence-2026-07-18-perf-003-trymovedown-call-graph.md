# 2026-07-18 PERF-003 TryMoveDown 调用图证据

taskIds: `PERF-003`
commit: `c18bc33db9cbdcc65866d41bace90477dd62b58b`
runSessionId: `local-20260718-perf003-trymovedown-call-graph`
runIdentityStatus: `captured`
evidenceState: `local_formal_benchmark_target_gap_statistically_separated`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

`c18bc33d` 将 `TryMoveDown` 的 32px 空列扫描隔离到 `NoInlining` 的 `TryMoveDownThroughEmptyColumn`。首个下方 cell 非空时，密集材质路径不再携带整段扫描循环；首格为空时仍按原顺序扫描至 `MoveCap`，保留 center/south 寻址、密度置换、parity、RigidOwned damage、dirty 与跨 chunk KeepAlive 语义。

提交态 full-active B9 为 9.841ms，clean baseline A8 为 10.435ms，mean 改善 5.69%；99.9% CI 分别为 9.641–10.041ms 与 10.287–10.584ms，不重叠。按 B9 mean 折算约 1,761,434 cells/8ms，按 upper CI 10.041ms 折算约 1,726,349 cells/8ms，仍分别低于 2M 下限 11.93% / 13.68%，因此 `PERF-003` 继续阻塞。

独立 Typical Dirty 基准也从 22.105us 降到 20.133us，mean 改善 8.92%，两份 99.9% CI 不重叠。该基准只证明 16x16 dirty 初态的单帧回归，不替代 full-active 目标或真实产品 frame budget。

## 独立 Typical Dirty 基准合同

旧 `CellThroughputBenchmark.StepJobSystem(TypicalDirtyRect)` 单帧只有约 0.2ms，BenchmarkDotNet 会报告 `MinIterationTime`，且连续推进同一 kernel 会让后续 dirty 收缩。`1eefa4a3` 新增 `TypicalDirtyCellThroughputBenchmark`：

- 一次计时推进 8,192 个独立 kernel，每个 kernel 只推进一帧，`OperationsPerInvoke=8192`；正式运行的最短 workload 大于 140ms。
- 每帧恢复同一个 `(24..39, 24..39)` 交错 sand/water 初态、frame index 与 parity，不修改原 Typical Dirty 场景位置。
- 每帧独占 center 与 south 两个可写 chunk；其余 7 个空 guard chunk 只读共享，以控制基准进程内存。
- `IterationSetup` 与 `IterationCleanup` 都检查共享 guard 的四组 SoA、current/working/incoming dirty、state 与 parity；任何未来跨界写入都会 fail-closed。

512 与 4,096 帧 smoke 分别仍只有约 15–18ms、70.939ms，均被拒绝；8,192 帧 smoke 的三个 workload 为 158.942–208.735ms，首次消除 `MinIterationTime` warning 后才提交基准。

## JIT 调用图

使用 Release、`DOTNET_ReadyToRun=0`、`DOTNET_TieredCompilation=0` 与 `DOTNET_JitDisasm=PixelEngine.Simulation.ChunkUpdater:TryMoveDown*` 检查 FullOpts 汇编：

| 版本 | 方法 | FullOpts code size | 结论 |
|---|---|---:|---|
| baseline `1eefa4a3` | `TryMoveDown` | 3,936B | 首格碰撞与 32px 空列扫描在同一方法 |
| optimized `c18bc33d` | `TryMoveDown` | 1,967B | 首格非空路径的 instruction footprint 减半 |
| optimized `c18bc33d` | `TryMoveDownThroughEmptyColumn` | 1,972B | 空列扫描保持独立且显式 `NoInlining` |

拆分前后总 native code size 基本相同；收益来自密集路径不再携带 cold loop，而不是删减 movement 语义。静态门禁要求 `TryMoveDown` 的空分支调用 helper，并要求 helper 紧邻 `MethodImplOptions.NoInlining`。

## BenchmarkDotNet 提交态对照

两类 workload 均使用 2 launches × 10 warmups × 20 measured iterations。A8 在临时 detached worktree 精确检出 `1eefa4a3`；B9 直接从干净 `c18bc33d` 执行。

### Full-active 2M

`FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames` 一次计时推进 16 个互不共享的 2,166,784-cell full-dirty kernel。

| Run | Commit | Mean | StdDev | Median | 99.9% CI | N | Launch means | mValue | Code size | Allocated |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| A8 | `1eefa4a3` | 10.435ms | 0.260ms | 10.445ms | 10.287–10.584ms | 39 | 10.53 / 10.33ms | 2 | 15,242B | `-` |
| B9 | `c18bc33d` | 9.841ms | 0.350ms | 9.808ms | 9.641–10.041ms | 39 | 10.03 / 9.64ms | 2 | 15,242B | `-` |

A8 与 B9 的第一个 launch 原始 GC 行都记录 32 bytes / 16 operations，第二个 launch 为 0，均无 GC collection；BenchmarkDotNet 汇总列为 `Allocated -`。该对称微量 process/diagnoser 记录没有被隐藏，也不作为候选新增稳态分配；专门的零分配验收仍由 `PERF-004` 的 allocation tests 与报告负责。

### Typical Dirty 16x16

| Run | Commit | Mean | StdDev | Median | 99.9% CI | N | Launch means | mValue | Code size | Allocated |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| A7 | `1eefa4a3` | 22.105us | 1.313us | 22.244us | 21.367–22.844us | 40 | 22.56 / 21.65us | 3.60 | 25,064B | `-` |
| B9 | `c18bc33d` | 20.133us | 1.319us | 20.234us | 19.392–20.875us | 40 | 20.06 / 20.21us | 2.93 | 20,043B | `-` |

A7/B9 都显示多峰，因此不挑选单个快簇；这里采用完整 40 个 measured samples。即便按较慢 optimized launch 20.21us 对较快 baseline launch 21.65us，方向仍为改善。

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
    -Artifacts 'artifacts/perf-003-<commit>-trymovedown-20260718' `
    -BenchmarkDotNetArgs $bdn

$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_JitDisasm = 'PixelEngine.Simulation.ChunkUpdater:TryMoveDown*'
$env:DOTNET_JitDisasmAssemblies = 'PixelEngine.Simulation'
$env:DOTNET_JitDisasmDiffable = '1'
$env:DOTNET_JitStdOutFile = '<absolute-artifact-path>'

dotnet test tests/PixelEngine.Simulation.Tests/PixelEngine.Simulation.Tests.csproj `
    -c Release --no-build --no-restore `
    --filter 'FullyQualifiedName~SimulationMovementTests.StepCaClampsPowderVerticalCollapseToMoveCap'
```

baseline 命令在 `git worktree add --detach <temp> 1eefa4a3` 创建的临时 worktree 中执行；报告复制完成后使用 `git worktree remove --force <temp>` 清理。B9 运行开始时 `git status --short` 为空。

## 原始报告与 SHA256

`artifacts/` 是可再生原始输出；本文件与 Evidence Index 是长期稳定登记。

| Run | 原始文件 | SHA256 |
|---|---|---|
| A8 full-active | `artifacts/perf-003-1eefa4a3-fullactive-baseline-a8-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `74A24467874AB6DB8872BDFCCBCF0BB83B17725FF860BF2C0CABCCCA29636BDD` |
| A8 full-active | `artifacts/perf-003-1eefa4a3-fullactive-baseline-a8-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-20260718-091401.log` | `94EC3AF1E880208B916B417C78F2699CF89CB564F3DCC604561419E10EFAFE4F` |
| A7 typical | `artifacts/perf-003-1eefa4a3-typical-baseline-a7-20260718/results/PixelEngine.Benchmarks.TypicalDirtyCellThroughputBenchmark-report-github.md` | `6169196B4FD4DC9F8D63ACF06D827F0DDCC67839BC8CB4432003A7D503A0F813` |
| A7 typical | `artifacts/perf-003-1eefa4a3-typical-baseline-a7-20260718/PixelEngine.Benchmarks.TypicalDirtyCellThroughputBenchmark-20260718-085212.log` | `81C17858F3C70716C80C35B1D708ABFA30CA614743F6E34FF7F281B2A380AFEE` |
| B9 full-active | `artifacts/perf-003-c18bc33d-trymovedown-split-b9-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `A4A1DB39E9E7A3648D524A2D05F060A95D775A543BC0011D95C980766F5C3C97` |
| B9 typical | `artifacts/perf-003-c18bc33d-trymovedown-split-b9-20260718/results/PixelEngine.Benchmarks.TypicalDirtyCellThroughputBenchmark-report-github.md` | `3B9BC247C18E1C11979109B8B190FA63C4A57469B43AF985403B3D21C2BC4B62` |
| B9 combined log | `artifacts/perf-003-c18bc33d-trymovedown-split-b9-20260718/BenchmarkRun-20260718-091643.log` | `90A2B4EBE40240338BBF1D87B6FDA8C6C91693D6BB930852D2728272B920FC1B` |
| baseline JIT | `artifacts/perf-003-1eefa4a3-trymovedown-jit-20260718/ChunkUpdater.TryMoveDown.asm.txt` | `3EC93EF7D6BA2E21E98A115387F4AB6C8382E64756F9B1069E6E640EAEB832F8` |
| B9 JIT | `artifacts/perf-003-c18bc33d-trymovedown-jit-20260718/ChunkUpdater.TryMoveDown.split.asm.txt` | `F4900F5FB03D27845B5285077922B9F1A0A6903C4B88ED9193E73CA1A8E20842` |

## 正确性与剩余阻塞

- `PixelEngine.Simulation.Tests`：198 passed / 0 skipped / 0 failed，覆盖 powder/liquid/gas、MoveCap、障碍前停靠、密度置换、parity、质量守恒和跨 chunk KeepAlive。
- `PerformanceHardeningToolingDisciplineTests`：129 passed / 0 skipped / 0 failed；调用图静态门禁要求空列 helper 与 `NoInlining` 边界。
- `PixelEngine.sln` Release + `TreatWarningsAsErrors=true`：0 warning / 0 error；目标文件 `dotnet format --verify-no-changes`、task catalog 与 `git diff --check` 均通过。
- 本机 B9 仍低于 2M/8ms，且只有 win-x64 / AVX2；缺其余五个 RID 代表硬件、elevated ETW cache/branch counters、AVX-512 净损和玩家包 60s/3600-frame phase p99。因此本报告不能关闭 `PERF-003`、`PERF-008` 或 `PERF-009`。
