# 2026-07-10 PERF-002 CPU RenderBuffer 优化证据

taskIds: `PERF-002`
commit: `17a88e67242ecc713832f1804f6ddc093b70dffe`
runSessionId: `local-20260710-perf002-render-buffer`
runIdentityStatus: `captured`
hardware: `Microsoft Windows 11 专业版 build 26100; AMD Ryzen 7 5800X 8c/16t; AMD Radeon RX 7900 XT driver 32.0.31021.5001; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

PERF-002 的本地实现与验收闭合。普通 Demo 的同场景、同分辨率、同硬件窗口短跑中，`render_buffer_avg_ms` 从 15.362ms 降至 7.475ms，p99 从 22.644ms 降至 13.079ms；整帧 CPU work 平均从 24.077ms 降至 16.121ms，窗口进入约 60 FPS 区间。

实现包含四个互补的热路径修正：

- `RenderFrameContext.ForceRebuild` 区分相机/CPU 粒子擦除等 render-only 变化和可复用稳定帧，`RenderBufferBuilder` 按 camera、材质表、resident chunk 引用及 dirty 元数据复用 render/aux buffer。
- Full 样式档在 `CellsPerPixel=1/2/4/8` 的整数放大档使用 `BuildRowsStyledZoom`，每个世界 cell 完整采样一次，再填充对应屏幕重复区间；温度 glow、纹理、动态样式和 aux 输出不被跳过。
- 带活动温度 block 的 chunk 才退回温度标量采样，其他 chunk 保留 palette/style 快路径；没有纹理 provider 时，带 `TextureId` 的静态材质按 `BaseColorBGRA` 参与快路径。
- 1:1 样式描边检查对当前 chunk 内的左右/上邻居直接读取 SoA，只在 chunk 边界查找相邻 chunk；跨界缺失邻居仍按原语义视为边界。

## 可复现命令

以下命令均在 `17a88e67242ecc713832f1804f6ddc093b70dffe` 的工作树执行；优化前窗口基线来自直接父提交 `3e97c7c0`，场景、内部视口和硬件相同。

```powershell
dotnet build PixelEngine.sln -c Release --no-restore -m:1
dotnet test PixelEngine.sln -c Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"
dotnet run --project demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-build -- --no-hot-reload --window-ticks 120 --no-vsync --content demo/PixelEngine.Demo/content --log-dir artifacts/perf-002-boundary-fast-window
pwsh -NoProfile -File tools/run-benchmark.ps1 -Project "bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj" -Artifacts "artifacts/perf-002-render-buffer-final" -BenchmarkDotNetArgs @('--filter=*RenderBufferViewportBenchmarks*','--launchCount=1','--warmupCount=3','--iterationCount=5')
pwsh -NoProfile -File tools/validate-task-catalog.ps1
git diff --check
```

BenchmarkDotNet 使用 `tools/run-benchmark.ps1` 的隔离工作副本，避免仓库内 `.claude/worktrees` 的同名项目干扰 benchmark discovery。原始日志位于 volatile `artifacts/perf-002-render-buffer-final/`，本报告是长期证据。

## 同场景窗口前后对比

两次都是 120 ticks、无 VSync、同一默认 playable scene；窗口内部 CPU render buffer 为 `720x480`，测量窗口为 40 warmup + 80 measured frames。

| 指标 | 优化前 `3e97c7c0` | 优化后 `17a88e67` | 变化 |
|---|---:|---:|---:|
| `render_buffer_avg_ms` | 15.362 | 7.475 | -51.3% |
| `render_buffer_p99_ms` | 22.644 | 13.079 | -42.2% |
| `wall_avg_ms` | 24.218 | 16.270 | -32.8% |
| `cpu_work_avg_ms` | 24.077 | 16.121 | -33.1% |
| `effective_fps` | 未在基线摘要单独记录 | 65.3 | 达到 60Hz 区间 |

优化后同一窗口摘要还记录：`render_buffer_p50_ms=7.095`、`render_buffer_p95_ms=8.723`、`gpu_frame_avg_ms=0.109`、`lighting_avg_ms=0.032`、`bloom_avg_ms=0.023`，以及 `active_cells_avg=0`、`active_chunks_avg=0`。因此本次对比覆盖的是原任务指出的静止/典型 Demo render-buffer 瓶颈，而不是借 active CA 负载变化掩盖结果。

## 1280x720 RenderBuffer BenchmarkDotNet

`RenderBufferViewportBenchmarks` 构造 20x12 resident chunks，覆盖完整 1280x720 视口；`BuildStaticInvalidatedFrame` 强制走全量重建，`ReuseStaticFrame` 在同一 render/aux buffer 上验证无 dirty 的稳定帧复用。

| Benchmark | Mean | StdDev | 测量样本 | Allocated |
|---|---:|---:|---:|---:|
| `BuildStaticInvalidatedFrame` | 6.458ms | 0.298ms | 65 | `-` |
| `ReuseStaticFrame` | 3.137us | 0.069us | 19 | `-` |

BDN 环境为 .NET 10.0.8、X64 RyuJIT x86-64-v3、AVX2、Concurrent Workstation GC。BDN 保留一个 `MultimodalDistribution` 提示和各 benchmark 一个被移除的 outlier；没有 `Generate Exception`、`NA`、空 workload result 或 benchmark 进程失败。`MemoryDiagnoser` 两条路径均报告 `Allocated -`。

## 正确性与工程门禁

- `PixelEngine.Rendering.Tests`：175 passed、20 个既有 OpenGL/native 条件 skip、0 failed；新增稳定帧复用、强制重建、无 provider 的 textured static material 快路径和 styled zoom 像素/aux 等价回归。
- `PixelEngine.Hosting.Tests`：452 passed、4 skipped、0 failed；其中 `EnginePhaseDriverTests` 22 项和性能纪律筛选 17 项已单独复核。
- solution 全量测试：1472 passed、34 skipped、0 failed；其余程序集均无失败。
- `dotnet build PixelEngine.sln -c Release --no-restore -m:1`：32 projects、0 warnings、0 errors。
- `pwsh tools/validate-task-catalog.ps1`：72 canonical tasks，35 done、13 open、1 active、23 blocked；唯一 active 仍为 PERF-002，等待本报告/index/state 三个提交节点闭合。
- `git diff --check`：clean。

## 边界

本报告证明的是当前 Ryzen 7 5800X / Windows-first Demo 的本地校准和代码正确性；它不冒充 `PERF-008` 所需的目标硬件 60 秒/3600 帧长跑，也不关闭 `PERF-003` full-active CA、硬件计数器或 AVX-512 证据。架构文档中的目标硬件最终帧预算仍由独立性能证据任务负责；本任务的结果是普通 Demo render-buffer 不再单独造成明显掉帧，且 1280x720 强制重建和稳定复用路径均已形成可复现零分配基准。
