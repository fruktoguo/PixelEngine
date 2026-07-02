# 2026-07-02 粒子渲染帧时间探针

## 目的

验证 Demo 已具备真实窗口下的高密度自由粒子 CPU stamp / GPU point-sprite 对比入口。该报告只记录本机 win-x64 短样本，不能替代 plan/09 要求的目标 GPU 硬件基准。

## 命令

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-build -- --no-hot-reload --window-ticks 8 --particle-frame-probe --particle-render-mode cpu --particle-count 100000 --particle-probe-warmup 2 --content demo\PixelEngine.Demo\content --log-dir artifacts\particle-frame-probe-cpu

dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-build -- --no-hot-reload --window-ticks 8 --particle-frame-probe --particle-render-mode gpu --particle-count 100000 --particle-probe-warmup 2 --content demo\PixelEngine.Demo\content --log-dir artifacts\particle-frame-probe-gpu
```

## 结果

CPU stamp：

```text
particle_frame_probe mode=cpu, gpu_available=False, requested_count=100000, active_count=100000, warmup_frames=2, measured_frames=6, wall_avg_ms=138.974, wall_p50_ms=70.020, wall_p95_ms=253.806, wall_max_ms=253.806, particle_stamp_avg_ms=0.827, particle_stamp_p50_ms=0.822, particle_stamp_p95_ms=0.876, particle_stamp_max_ms=0.876, gpu_particle_avg_ms=0.000
```

GPU point-sprite：

```text
particle_frame_probe mode=gpu, gpu_available=True, requested_count=100000, active_count=100000, warmup_frames=2, measured_frames=6, wall_avg_ms=141.610, wall_p50_ms=66.801, wall_p95_ms=287.641, wall_max_ms=287.641, particle_stamp_avg_ms=0.000, gpu_particle_avg_ms=0.013, gpu_particle_p50_ms=0.013, gpu_particle_p95_ms=0.014, gpu_particle_max_ms=0.014
```

## 结论

探针入口可用，并能在同一窗口链路中验证 10 万活跃粒子。GPU 模式已跳过 CPU stamp，且记录到 `GpuParticleDraw` 子相位；CPU 模式记录到 `ParticleStamp` 子相位。当前短样本只证明入口与子相位采样可用，总帧时间受窗口启动、BuildRenderBuffer 和桌面环境噪声影响，不能作为目标硬件“GPU 总帧时间优于 CPU stamp”的验收依据。

## GPU 粒子基准预检入口

新增 `tools/gpu-particle-benchmark-preflight.ps1` 作为 plan/09 目标硬件基准证据入口。无目标硬件 evidence manifest 时，默认报告 `blocked_missing_target_gpu_evidence`；加 `-RunProbe` 时会分别运行 `--particle-frame-probe --particle-render-mode cpu` 与 `--particle-frame-probe --particle-render-mode gpu`，并报告 `local_probe_only`。这两种状态都不是验收通过，只用于证明入口可执行与产出待审证据。

```pwsh
./tools/gpu-particle-benchmark-preflight.ps1 -RunProbe -AllowBlocked
```

目标硬件长基准完成后，可提供 `-EvidenceManifestPath <json>`。manifest 使用 `schemaVersion: 1`，并且只能包含 `targetHardwareReport`、`cpuProbeReport`、`gpuProbeReport`、`comparisonReport` 四个 evidence scope；每个 entry 必须声明 `path` 与 `sha256`，脚本会重新计算文件 SHA256 并比对。缺 scope 时报告 `blocked_missing_target_gpu_scope_evidence`，未知 scope 会被拒绝；证据齐全时报告 `target_gpu_evidence_attached_pending_review`，仍需人工确认 comparisonReport 是否满足 plan/09 的“高密度 GPU 总帧时间优于 CPU stamp”验收语义。
