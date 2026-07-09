# 2026-07-10 PERF-001 正式性能基线

taskIds: `PERF-001`  
commit: `9548d549fac5fee8d0bc0de80db08d3c2079c7c7`  
runSessionId: `local-20260710-perf001-bdn`  
runIdentityStatus: `captured`  
hardware: `Microsoft Windows 11 专业版 build 26100; AMD Ryzen 7 5800X 8c/16t; AMD Radeon RX 7900 XT driver 32.0.31021.5001; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 运行协议

本报告替换旧的 `NA`、ColdStart 和单次迭代数据。四组正式运行均使用以下 BDN 参数：

```text
--launchCount 1 --warmupCount 3 --iterationCount 5
```

`bench/PixelEngine.Benchmarks/Program.cs` 同时启用 `MemoryDiagnoser`、`ThreadingDiagnoser` 和 `DisassemblyDiagnoser(maxDepth: 3)`；未启用硬件计数器环境变量。运行脚本为 `tools/run-benchmark.ps1`，每组先复制仓库到隔离工作区，再以 Release 配置执行并把原始输出复制到 `artifacts/`。

运行命令：

```powershell
$bdn = @('--filter','*CellThroughputBenchmark*','--launchCount','1','--warmupCount','3','--iterationCount','5')
./tools/run-benchmark.ps1 -Artifacts artifacts/perf-001-cell-fixed -BenchmarkDotNetArgs $bdn

$bdn = @('--filter','*RenderingAllocationBenchmarks*','--launchCount','1','--warmupCount','3','--iterationCount','5')
./tools/run-benchmark.ps1 -Artifacts artifacts/perf-001-render -BenchmarkDotNetArgs $bdn

$bdn = @('--filter','*PhysicsBenchmarks*','--launchCount','1','--warmupCount','3','--iterationCount','5')
./tools/run-benchmark.ps1 -Artifacts artifacts/perf-001-physics -BenchmarkDotNetArgs $bdn

$bdn = @('--filter','*ParticleIntegrationBenchmark*','--launchCount','1','--warmupCount','3','--iterationCount','5')
./tools/run-benchmark.ps1 -Artifacts artifacts/perf-001-particles -BenchmarkDotNetArgs $bdn

$bdn = @('--filter','*GameUiAllocationBenchmarks*','--launchCount','1','--warmupCount','3','--iterationCount','5')
./tools/run-benchmark.ps1 -Artifacts artifacts/perf-001-ui -BenchmarkDotNetArgs $bdn
```

CA 基准的 `IterationSetup` 同时重置 chunk SoA/dirty 元数据和 `SimulationKernel.FrameIndex/CurrentParity`；该夹具修正已在本报告绑定的 commit 中提交。每个 benchmark 都实际执行，合计 29 个 benchmark：CA 6、render 2、physics 8、粒子 9、UI 4。

## 环境身份

所有结果均来自同一台本机和同一组运行时：

```text
BenchmarkDotNet v0.15.8
Windows 11 10.0.26100.8457 / 24H2
AMD Ryzen 7 5800X 4.20GHz; 1 CPU; 16 logical / 8 physical cores
.NET SDK 10.0.108; .NET 10.0.8
X64 RyuJIT x86-64-v3; Concurrent Workstation GC
RID: win-x64
```

机器上同时存在 AMD Radeon RX 7900 XT 和虚拟显示适配器；本轮 benchmark 是 CPU/托管基线，不测 GPU，不把显示适配器状态解释为 GPU 性能结论。

## 结果

BDN 表中的 `Mean` 是每次 benchmark operation 的均值，`StdDev` 是 measured iteration 的标准差；`n` 为 BDN 处理 outlier 后保留的样本数。`Allocated=-` 表示 MemoryDiagnoser 未报告该 operation 的托管分配。

### CA throughput

`FullActiveLiquid` 为 8×8 active chunks，即 262,144 cells；`TypicalDirtyRect` 为单 chunk 16×16 dirty 区域；`FullStaticSleeping` 的 active cells 为 0。

| Profile | Active cells | Single-thread Mean ± StdDev | JobSystem Mean ± StdDev | n | Job/Single |
|---|---:|---:|---:|---:|---:|
| FullActiveLiquid | 262,144 | 30,415.40 ± 395.043 µs | 13,563.66 ± 1,053.244 µs | 5 / 5 | 0.45 |
| FullStaticSleeping | 0 | 24.35 ± 0.420 µs | 22.98 ± 0.694 µs | 4 / 5 | 0.94 |
| TypicalDirtyRect | 256 | 331.40 ± 56.891 µs | 296.12 ± 10.229 µs | 5 / 4 | 0.91 |

### Render buffer

| Benchmark | Mean ± StdDev | n | Allocated |
|---|---:|---:|---:|
| BuildRenderBuffer | 5,274.8 ± 123.61 ns | 5 | - |
| StampParticles | 762.8 ± 27.30 ns | 5 | - |

### Physics

| Benchmark | Parameter | Mean ± StdDev | n | Allocated |
|---|---:|---:|---:|---:|
| RigidBodyDestructionRebuildDirty | DamagedBodyCount=1 | 1,179.8 ± 37.51 µs | 4 | 166,240 B |
| Box2DTaskBridgeStepWorker1 | DamagedBodyCount=1 | 2,102.6 ± 123.83 µs | 5 | - |
| Box2DTaskBridgeStepWorker4 | DamagedBodyCount=1 | 794.0 ± 47.28 µs | 5 | - |
| PhysicsSystemSyncStepSteadyState | DamagedBodyCount=1 | 947.2 ± 17.40 µs | 5 | 14 B |
| RigidBodyDestructionRebuildDirty | DamagedBodyCount=16 | 17,407.0 ± 3,644.45 µs | 5 | 3,147,384 B |
| Box2DTaskBridgeStepWorker1 | DamagedBodyCount=16 | 2,107.1 ± 59.75 µs | 5 | - |
| Box2DTaskBridgeStepWorker4 | DamagedBodyCount=16 | 810.5 ± 54.97 µs | 5 | - |
| PhysicsSystemSyncStepSteadyState | DamagedBodyCount=16 | 889.0 ± 17.04 µs | 5 | 14 B |

### 粒子

| Benchmark | Count | Mean ± StdDev | n | Allocated |
|---|---:|---:|---:|---:|
| IntegrateFlyingParticles | 50,000 | 439.48 ± 23.097 µs | 4 | - |
| IntegrateAndResolveDeposits | 50,000 | 1,582.12 ± 59.219 µs | 4 | - |
| ReadActivePrefix | 50,000 | 45.75 ± 1.405 µs | 5 | - |
| IntegrateFlyingParticles | 100,000 | 794.33 ± 8.016 µs | 4 | - |
| IntegrateAndResolveDeposits | 100,000 | 2,792.78 ± 9.637 µs | 5 | - |
| ReadActivePrefix | 100,000 | 87.47 ± 2.955 µs | 4 | - |
| IntegrateFlyingParticles | 200,000 | 1,531.70 ± 38.017 µs | 4 | - |
| IntegrateAndResolveDeposits | 200,000 | 5,611.73 ± 64.722 µs | 4 | - |
| ReadActivePrefix | 200,000 | 158.00 ± 4.082 µs | 4 | - |

### UI allocation

| Benchmark | Mean ± StdDev | n | Allocated |
|---|---:|---:|---:|
| RunStaticUiPhaseFrame | 1,859.336 ± 61.6059 ns | 5 | - |
| CompositeCleanFrameSkip | 1.835 ± 0.0759 ns | 5 | - |
| DrawGuiCleanFrameSkip | 1.796 ± 0.0477 ns | 4 | - |
| PumpIdleInput | 10.843 ± 0.6367 ns | 5 | - |

## 解释与边界

- CA FullActive 的当前基线是 262,144 cells / 30.415 ms（单线程）或 13.564 ms（JobSystem）；这证明当前实现仍未达到原架构目标，不能提前关闭 `PERF-003`。
- Render 结果是现有 microbenchmark fixture 的 render-buffer/stamp 热路径，不是 720p/1080p Demo 全帧；不能提前关闭 `PERF-002`。
- Physics dirty rebuild 的 1/16 body 分配和高方差是后续 `PERF-005` 的真实输入，不在本任务中掩盖或优化。
- BDN 对单 operation CA、physics rebuild、粒子和其它短操作保留了 `MinIterationTime` < 100 ms warning；这是逐帧/单调用语义下的测量限制。报告保留 BDN 原始 warning，未用重复调用改变被测语义。
- BDN 还报告了少量 outlier，表中 `n` 如实反映 outlier 处理后的样本数。所有组均有 3 次 warmup 和 5 次配置 measured iteration，未出现空结果、`Generate Exception`、`No Workload Results` 或 benchmark 进程失败。

## 原始结果引用

原始 BDN 输出位于 volatile `artifacts/`，以下 hash 用于本次运行的追溯；稳定证据以本报告和 evidence index 为准。

| Group | Raw report | SHA256 |
|---|---|---|
| CA | `artifacts/perf-001-cell-fixed/results/PixelEngine.Benchmarks.CellThroughputBenchmark-report-github.md` | `cb6f2aca7ff025bbeaa3e3b870ada32c3fa5351443dc9a068e0df7a34e923836` |
| Render | `artifacts/perf-001-render/results/PixelEngine.Benchmarks.RenderingAllocationBenchmarks-report-github.md` | `f223df77fbdae6ba020e97199f933dc595f0cea54821d27b4e119b54f8451e17` |
| Physics | `artifacts/perf-001-physics/results/PixelEngine.Benchmarks.PhysicsBenchmarks-report-github.md` | `238b2f7e5dc15c7309c6c5f195e132d6e03763f056fabc0afb14efec46a3f760` |
| 粒子 | `artifacts/perf-001-particles/results/PixelEngine.Benchmarks.ParticleIntegrationBenchmark-report-github.md` | `01aa312fee2161a5a35548cdcee885a4dfd5aafea7c1bf0c3a7a8fa025796e11` |
| UI | `artifacts/perf-001-ui/results/PixelEngine.Benchmarks.GameUiAllocationBenchmarks-report-github.md` | `c28bd9e1a3d471f770955e9af6595cf1cd2a952e53b461f0f7242562049fd5ea` |

## 验证

任务目录、报告路径、索引 SHA256 和工作区差异在本任务收口前再次执行验证。未执行 push、远端 workflow、release 或签名操作。
