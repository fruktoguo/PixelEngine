# 2026-07-18 PERF-003 packed material lane 证据

taskIds: `PERF-003`
commit: `5355a5b924ed4f30b4144bea9714d6000959e181`
runSessionId: `local-20260718-perf003-packed-material-lane`
runIdentityStatus: `captured`
evidenceState: `local_formal_benchmark_target_gap_bimodal`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

`5355a5b9` 为每种材质派生一个 32-bit CA cell-update lane，一次读取 type、density、dispersion、reaction gate 与 custom-update gate；原 SoA 列仍是权威数据，新增存储按材质而不是按 cell 计费。`ChunkUpdater` 不再为每个活跃 cell 分别读取 `ReactionCountOf` 与 `PropertyFlagsOf`。

提交态 B4 的正式报告为 10.809ms、0 B，但两次 launch 分裂为 10.07ms 与 11.63ms，BenchmarkDotNet 明确报告 `MultimodalDistribution (mValue=3.67)`。相对同机干净基线 A4 的 11.344ms，mean 改善 4.716%，但两份 99.9% CI 有 0.067ms 重叠，不能把这一轮描述为统计上完全分离。按可追溯的提交态 B4 mean 折算，8ms 约处理 1,603,689 cells，仍比 2M 下限少 19.816%；`PERF-003` 必须保持阻塞。

提交前 D2 在相同生产代码上得到 10.274ms、两 launch 同方向和 0 B，相对 A4 改善 9.432%；但 D2 没有绑定可检出的 Git commit，只能解释候选选择，不能替代提交态证据或用于关闭 canonical task。

## 实现与正确性边界

- packed lane 布局为 type bits 0–7、density bits 8–15、dispersion bits 16–23、has-reaction bit 24、has-custom-update bit 25；每种材质增加 4 bytes，不改变 §7.1 的 per-cell 字节预算。
- `MaterialHotTable` 构造时从已验证的 SoA 列派生 lane；`MaterialTable.ReloadStable`、`RegisterCustomUpdate` 与 `MaterialPropsTable.Reload` 都通过整体替换 `Hot` 使派生值同步更新。
- lifetime sink 若在 movement 前改写材质，`ChunkUpdater` 会先重读 source material，再读取其 packed lane；movement 后 active cell 仍持有同一 source material。
- reaction executor 仍拥有反应表与具体规则；packed bit 只替代“reaction count 是否为零”的 gate。custom-update delegate 仍由 `MaterialTable.TryUpdate` 解析并执行。
- checkerboard、单缓冲 swap、parity、32px MoveCap、跨 chunk halo/KeepAlive、RigidOwned damage 与 dirty rectangle 路径均未改变。

## BenchmarkDotNet 对照

三轮均运行 `FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames`：一次计时依次推进 16 个互不共享的 2,166,784-cell full-dirty kernel，每个 kernel 只推进一帧；job 固定为 2 launches × 10 warmups × 20 measured iterations。

| Run | 来源 | Mean | StdDev | Median | 99.9% CI | N | Launch means | Allocated | 证据强度 |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| A4 | clean baseline `65d90eb7` | 11.344ms | 0.223ms | 11.357ms | 11.217–11.472ms | 39 | 11.37 / 11.32ms | `-` | 可检出提交 |
| D2 | pre-commit candidate | 10.274ms | 0.268ms | 10.244ms | 10.121–10.427ms | 39 | 10.33 / 10.22ms | `-` | 探索性，不进完成判据 |
| B4 | clean optimized `5355a5b9` | 10.809ms | 0.820ms | 10.463ms | 10.333–11.284ms | 38 | 10.07 / 11.63ms | `-` | 可检出提交，但双峰 |

B4 结束后的独立 15 秒系统采样为 mean 40.73% CPU、min 32.23%、max 55.00%，证明主机在运行后仍有明显外部负载，但不能回溯该负载与第二 launch 的精确时间重叠；结合双峰分布，必须把它登记为混杂风险。没有终止用户进程、提高 benchmark 优先级或改变 affinity，因为这些操作会破坏与 A4 的相同口径。B4 原始报告和双峰警告按原样保留，不挑选快 launch 计算 canonical 当前事实。

## EventPipe 机制检查

诊断运行使用 1 launch × 3 warmups × 3 measured iterations，并通过 `PIXELENGINE_BENCH_EVENTPIPE=1` 显式启用 profiler；短跑 mean 不进入吞吐结论。对 speedscope event stack 按相邻事件时间区间做结构化 inclusive 归因：

- 基线 trace 的 `ChunkUpdater` 栈包含 `TryRunCustomUpdate`、`TryReactVonNeumann`、`MaterialPropsTable.PropertyFlagsOf`、`MaterialPropsTable.ReactionCountOf` 及两个 `MaterialHotTable.*Unchecked` frame。
- packed-lane trace 中上述 frame 全部消失，只剩 `MaterialHotTable.CellUpdatePropertiesOfUnchecked`；这证明离散 gate 读取已从该热栈移除。采样时长不用于推导节省比例。
- packed-lane EventPipe 运行在代码提交前，后续生产源只增加说明性注释，运行时代码与 `5355a5b9` 相同；由于 trace 本身不具 Git commit 证明，仍只作为机制诊断，不升级为正式吞吐证据。

## 可复现命令

```powershell
$bdn = @(
    '--filter', '*FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames*',
    '--launchCount', '2',
    '--warmupCount', '10',
    '--iterationCount', '20')

& .\tools\run-benchmark.ps1 `
    -Artifacts 'artifacts/perf-003-<commit>-packed-20260718' `
    -BenchmarkDotNetArgs $bdn

$env:PIXELENGINE_BENCH_EVENTPIPE = '1'
$traceArgs = @(
    '--filter', '*FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames*',
    '--launchCount', '1',
    '--warmupCount', '3',
    '--iterationCount', '3')

& .\tools\run-benchmark.ps1 `
    -Artifacts 'artifacts/perf-003-packed-cell-update-eventpipe-20260718' `
    -BenchmarkDotNetArgs $traceArgs
```

## 原始报告与 SHA256

`artifacts/` 是可再生原始输出；本文件与 Evidence Index 是长期稳定登记。

| Run | 原始文件 | SHA256 |
|---|---|---|
| A4 | `artifacts/perf-003-65d90eb7-baseline-a4-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `E8D92D81CAFECF7BF969759EC12A9070E3B1578492781DEF1114A235C4790984` |
| A4 | `artifacts/perf-003-65d90eb7-baseline-a4-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-20260718-071011.log` | `E6E43DDA53180B385AC9A673105E1E476A471889CA7D93AAF25DAC243DF309EE` |
| D2 | `artifacts/perf-003-packed-cell-update-d2-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `218DD931CF05551CB02DB154CC6E41EDFDABD4BA6F93793FDFF9CA0F010C5D18` |
| D2 | `artifacts/perf-003-packed-cell-update-d2-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-20260718-071301.log` | `045DE204A2943C296505FF101F0A4DAB672D31327F5D9D80BD3215D9F3D03F74` |
| B4 | `artifacts/perf-003-5355a5b9-packed-b4-20260718/results/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-report-github.md` | `E5024B94811C439F0C41D836FDEE76144B16C40E03C8796D191C87A3231B5177` |
| B4 | `artifacts/perf-003-5355a5b9-packed-b4-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark-20260718-073149.log` | `05357E11C343826650F54AC8C122A8E0D13CBDFE60CC82F7DD51CB178E7BAFF2` |
| baseline trace | `artifacts/perf-003-e020f476-eventpipe-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames-20260718-065332.speedscope.json` | `580982E3ED4A463D1B0DB34E1D148821926F2555455799D196690260311DF700` |
| packed trace | `artifacts/perf-003-packed-cell-update-eventpipe-20260718/PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames-20260718-072157.speedscope.json` | `8CDABE8452F3A3189F6668BABD063286470DDF89CFE2EA4A0392C35389A5C405` |

## 验证与剩余阻塞

- `PixelEngine.Simulation.Tests`：198 passed / 0 skipped / 0 failed，含 packed decode、stable reload、custom-update binding、质量守恒、checkerboard、parity、reaction 与跨 chunk 行为。
- `PerformanceHardeningToolingDisciplineTests`：128 passed / 0 skipped / 0 failed；新增静态门禁禁止 `ChunkUpdater` 重新引入 `ReactionCountOf(material)` / `PropertyFlagsOf(material)`。
- `PixelEngine.sln` Release + `TreatWarningsAsErrors=true`：0 warning / 0 error；目标文件 `dotnet format --verify-no-changes` 通过。
- 本机仍只有 win-x64 / AVX2，且正式 B4 呈双峰并存在外部负载混杂风险；缺其余五个 RID 代表硬件、elevated ETW cache/branch counters、AVX-512 净损和玩家包 60s/3600-frame phase p99。因此本报告不能关闭 `PERF-003`、`PERF-008` 或 `PERF-009`。
