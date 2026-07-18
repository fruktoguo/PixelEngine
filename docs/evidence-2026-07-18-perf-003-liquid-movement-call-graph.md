# 2026-07-18 PERF-003 液体 movement 调用图证据

taskIds: `PERF-003`
commit: `9d9a9dc8f24737d8df0b4f8d0dec1cf015d7143b`
runSessionId: `local-20260718-perf003-liquid-movement-call-graph`
runIdentityStatus: `captured`
evidenceState: `local_formal_benchmark_target_gap_not_statistically_separated`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

`9d9a9dc8` 把 liquid movement 从 `UpdateChunk -> TryMoveLiquid -> TryMovePowder` 扁平为 `UpdateChunk -> TryMovePowder`，仅在 powder movement 失败且 `dispersion != 0` 时调用水平扩散 helper。水平 helper 显式 `NoInlining`，避免 JIT 把大块水平扫描复制进最内层 dispatch。

clean baseline A5 为 10.284ms，clean optimized B6 为 10.253ms；两轮均为 0 B、`mValue=2`。B6 mean 改善 0.301%，但两份 99.9% CI 大范围重叠，因此本报告不把吞吐差异描述为统计显著。按 B6 mean 折算约 1,690,654 cells/8ms，按 99.9% upper CI 10.412ms 折算约 1,664,836 cells/8ms，仍分别低于 2M 下限 15.467% / 16.758%；`PERF-003` 继续保持阻塞。

## JIT 调用图

使用 Release、`DOTNET_ReadyToRun=0`、`DOTNET_TieredCompilation=0` 与 `DOTNET_JitDisasm=PixelEngine.Simulation.ChunkUpdater:UpdateChunk` 检查 FullOpts 汇编：

| 版本 | `UpdateChunk` code size | movement call 边界 | 结论 |
|---|---:|---|---|
| baseline `3d584af0` | 3,149B | `call TryMoveLiquid`，其内部再 `call TryMovePowder` | 每个 liquid 多一层 wrapper call |
| 未门控扁平候选 | 6,264B | JIT 把水平扩散链内联进 `UpdateChunk` | 拒绝：instruction footprint 近乎翻倍 |
| 最终 `NoInlining` 候选 | 3,254B | 直接 `call TryMovePowder`；非零 dispersion 才 `call TryMoveLiquidHorizontal` | 保留：仅增加 105B，移除 wrapper call |

现有静态门禁要求 liquid dispatch 直接出现 `TryMovePowder` 与 `dispersion != 0 && TryMoveLiquidHorizontal`，要求 horizontal helper 紧邻 `MethodImplOptions.NoInlining`，并拒绝旧 `private static bool TryMoveLiquid(` 重新出现。

代码提交后从 `9d9a9dc8` production 源重新生成的 FullOpts 文件仍为 3,254B，且 SHA256 与 pre-commit `NoInlining` 候选完全一致；长期引用以下提交态文件。

## BenchmarkDotNet 对照

所有运行均使用 `FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames`，一次计时推进 16 个互不共享的 2,166,784-cell full-dirty kernel；job 固定为 2 launches × 10 warmups × 20 measured iterations。

| Run | 来源 | Mean | StdDev | Median | 99.9% CI | N | Launch means | Code size | Allocated | 身份 |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| E1 | pre-commit candidate | 10.199ms | 0.358ms | 10.220ms | 9.997–10.400ms | 40 | 9.985 / 10.413ms | 15,204B | `-` | 探索性 |
| A5 | clean baseline `3d584af0` | 10.284ms | 0.245ms | 10.268ms | 10.140–10.429ms | 37 | 10.233 / 10.334ms | 15,192B | `-` | 可检出提交 |
| E2 | pre-commit candidate | 10.204ms | 0.288ms | 10.131ms | 10.042–10.367ms | 40 | 10.252 / 10.157ms | 15,204B | `-` | 探索性 |
| B6 | clean optimized `9d9a9dc8` | 10.253ms | 0.273ms | 10.308ms | 10.095–10.412ms | 38 | 10.130 / 10.377ms | 15,121B | `-` | 可检出提交 |

E1→A5→E2 的夹心 aggregate mean 两侧均低于 baseline，因而候选没有因单次反向跑而被误保留；但逐 launch 方向并不完全一致，且 B6 对 A5 只有 0.031ms 差异。E1/E2 只说明候选筛选过程，不参与 canonical throughput 计算；长期事实只采用 A5/B6 clean commit，并明确 CI 未分离。

## 可复现命令

```powershell
$bdn = @(
    '--filter', '*FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames*',
    '--launchCount', '2',
    '--warmupCount', '10',
    '--iterationCount', '20')

& .\tools\run-benchmark.ps1 `
    -Artifacts 'artifacts/perf-003-<commit>-liquid-call-20260718' `
    -BenchmarkDotNetArgs $bdn

$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_JitDisasm = 'PixelEngine.Simulation.ChunkUpdater:UpdateChunk'
$env:DOTNET_JitDisasmAssemblies = 'PixelEngine.Simulation'
$env:DOTNET_JitDisasmDiffable = '1'
$env:DOTNET_JitStdOutFile = '<absolute-artifact-path>'

dotnet test tests/PixelEngine.Simulation.Tests/PixelEngine.Simulation.Tests.csproj `
    -c Release --no-build --no-restore `
    --filter 'FullyQualifiedName~SimulationMovementTests.StepCaUsesParityToPreventMovedLiquidFromMovingTwice'
```

## 原始报告与 SHA256

`artifacts/` 是可再生原始输出；本文件与 Evidence Index 是长期稳定登记。

| Run | 原始文件 | SHA256 |
|---|---|---|
| A5 | `artifacts/perf-003-3d584af0-liquid-call-baseline-a5-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `B82C8F80DFAE6BBA8319DDEFC36E75A608F303AC8DEB0370A7F408305E6A924D` |
| A5 | `artifacts/perf-003-3d584af0-liquid-call-baseline-a5-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-20260718-075622.log` | `394F2112955D45DA30CD597B64F642B09C66296E20D804FF4729F8B539CDA958` |
| E1 | `artifacts/perf-003-liquid-call-flatten-e1-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `70DAA914269AA8E13DC20B084AF10F8A54BD948B56474A2E08C5F62629C12256` |
| E2 | `artifacts/perf-003-liquid-call-flatten-e2-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `22926950562ECC2C5B6EC2F130E0DE4F291A56F6F380CF03D718BE15C0181F61` |
| B6 | `artifacts/perf-003-9d9a9dc8-liquid-call-b6-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `413685E94A174B8B4E1877B838C0596D4B9C36117D30250CE57CD10017191C5F` |
| B6 | `artifacts/perf-003-9d9a9dc8-liquid-call-b6-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-20260718-081025.log` | `F928EDEB982BFC5462FC748249EC3DFED5B58CAFC5EE39857EED30C1600F352E` |
| baseline JIT | `artifacts/perf-003-chunk-updater-jitdisasm-20260718/ChunkUpdater.UpdateChunk.asm.txt` | `F21442E73DD4E8E53CD39DA3A2A854133364104F2A1B7D6396251EED777B45AD` |
| rejected JIT | `artifacts/perf-003-chunk-updater-jitdisasm-20260718/ChunkUpdater.UpdateChunk.flattened.asm.txt` | `E10C06741DD8E34FF92969D9ED14EEF7178C8AFC2C6CE1288761F996138D0E35` |
| final JIT | `artifacts/perf-003-chunk-updater-jitdisasm-20260718/ChunkUpdater.UpdateChunk.9d9a9dc8.asm.txt` | `D487C80FF3641B6B69CBE2171B39BF8500A45380F575CECC9C91B1FD72076F08` |

## 正确性与剩余阻塞

- `PixelEngine.Simulation.Tests`：198 passed / 0 skipped / 0 failed，覆盖 powder/liquid/gas、零与非零 dispersion、MoveCap、质量守恒、parity、checkerboard 和跨 chunk 行为。
- `PerformanceHardeningToolingDisciplineTests`：128 passed / 0 skipped / 0 failed；另有 2/2 movement/JIT 调用图定向门禁通过。
- `PixelEngine.sln` Release + `TreatWarningsAsErrors=true`：0 warning / 0 error；目标文件 `dotnet format --verify-no-changes` 通过。
- 本机仍只有 win-x64 / AVX2，且 A5/B6 吞吐 CI 未分离；缺其余五个 RID 代表硬件、elevated ETW cache/branch counters、AVX-512 净损和玩家包 60s/3600-frame phase p99。因此本报告不能关闭 `PERF-003`、`PERF-008` 或 `PERF-009`。
