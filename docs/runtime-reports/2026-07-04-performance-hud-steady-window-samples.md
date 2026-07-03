# 2026-07-04 性能 HUD 稳态窗口样本

目的：为 `plan/12` 性能 HUD 与 `plan/16` profiling 工具链补齐真实窗口静态 / 高活跃场景的稳态样本。两组样本均关闭 VSync，有限窗口短跑 720 帧，探针预热 120 帧，统计后 600 帧；数值来自 `FrameProfiler.LastSubFrame` 与 `EngineCounters` 的真实测量，不含 mock 或占位数据。

## 命令

```pwsh
dotnet run --project demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-build -- --no-hot-reload --window-ticks 720 --no-vsync --capture-frame artifacts\perf-hud-static-720.bmp
dotnet run --project demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-build -- --no-hot-reload --window-ticks 720 --no-vsync --particle-frame-probe --particle-render-mode gpu --particle-count 100000 --particle-probe-warmup 120 --particle-probe-run-id hud-slice2-dynamic --capture-frame artifacts\perf-hud-particles-720.bmp
```

## 稳态摘要

| 场景 | measured frames | active cells avg | free particles avg | wall avg / p99 / max ms | CPU work avg / p99 ms | GPU frame avg / p99 ms | present-wait avg / p99 ms | effective avg / p99 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| static | 600 | 0.000 | 12.402 | 20.249 / 28.053 / 31.461 | 20.115 / 27.981 | 0.111 / 0.114 | 0.129 / 0.501 | 20.117 / 27.983 |
| particle_GpuPointSprite | 600 | 0.000 | 99987.432 | 28.553 / 37.406 / 39.605 | 28.400 / 37.310 | 0.150 / 0.155 | 0.147 / 0.506 | 28.403 / 37.313 |

## 成本结构

| 场景 | CA avg / p99 ms | physics avg / p99 ms | render-buffer avg / p99 ms | upload avg / p99 ms | lighting avg / p99 ms | bloom avg / p99 ms | particle stamp avg / p99 ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| static | 0.662 / 1.616 | 0.011 / 0.022 | 10.003 / 13.561 | 1.737 / 2.322 | 0.029 / 0.062 | 0.021 / 0.043 | 0.000 / 0.001 |
| particle_GpuPointSprite | 0.530 / 1.181 | 0.011 / 0.022 | 10.800 / 14.263 | 1.700 / 2.197 | 1.045 / 1.670 | 0.023 / 0.080 | 0.000 / 0.000 |

## 判定

- 两组样本的 `present_wait_avg_ms` 都低于 0.15ms，关闭 VSync 后没有被 present/vsync 等待限制。
- `gpu_frame_avg_ms` 为 0.111ms / 0.150ms，远低于对应 `cpu_work_avg_ms` 20.115ms / 28.400ms；当前两帧型都应判为 CPU-bound，而不是 GPU-bound 或 vsync-bound。
- 高活跃场景的 `free_particles_avg` 约为 99,987，`wall_avg_ms` 比静态场景增加 8.304ms；其中 `lighting_avg_ms` 从 0.029ms 增至 1.045ms，`render_buffer_avg_ms` 从 10.003ms 增至 10.800ms。分析器可以把等待时间与 CPU/GPU 工作分开，并能看到负载变化主要反映在 CPU 工作及粒子/光照相关路径，而不是 present 等待。
- 本报告是本机 win-x64 / 当前 GPU 的真实窗口样本，只用于 `plan/12 §3.7` 与 `plan/16 §4.12` 的 HUD 口径验收；不替代目标硬件帧预算、6 RID、AVX-512、硬件计数器或 GPU 粒子目标硬件长基准。
