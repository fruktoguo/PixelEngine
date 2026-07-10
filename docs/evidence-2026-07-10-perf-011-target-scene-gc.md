# 2026-07-10 PERF-011 真实内容热点、GC 与逻辑/音频预算证据

taskIds: `PERF-011`
implementationCommit: `54aabef69938d02dade3b8a03df3f2ff25e290ec`
gcConfigurationCommit: `1e48faa0962f70d1f8fbae09a47a3ab284dc764b`
primaryRunSessionId: `perf-011-lava-mine-reaction-temperature-workstation-steady`
companionRunSessionIds: `perf-011-lava-mine-reaction-temperature-server-steady`; `perf-011-window-production-gc`; `perf-011-render-frame-context`
runIdentityStatus: `captured`
evidenceState: `local_formal_target_scene_blocked_external_counters_and_target_hardware`
hardware: `Microsoft Windows 11 build 26100; AMD Ryzen 7 5800X 8c/16t; AMD Radeon RX 7900 XT; .NET SDK 10.0.108; .NET 10.0.8; win-x64`

## 结论

本轮完成了可在仓库内复现的真实内容负载与本机生产配置定档，但 **PERF-011 仍未完成最终验收**：

- 新增 640×360 常驻世界的 `lava-mine.scene` 基准。每个 measured iteration 都重建真实内容、Physics、Scripting 与 NullAudio 组合，预热 3 tick 后测量 1 tick；布局覆盖 7 组 reaction hit、7 组真实查表 miss，以及 ice/water 冷热/lava/metal/sand 六类温度 stencil。
- Workstation+Concurrent 在相同真实内容 fixture 上同时优于 Server+Concurrent 的 hit 与 miss 均值；结合 GC microbenchmark，唯一生产配置定为 **Workstation GC + Concurrent GC + `SustainedLowLatency`**。Demo、Editor Shell、local/package 游戏模板已显式固化 `ServerGarbageCollection=false`、`ConcurrentGarbageCollection=true`，Hosting 默认延迟模式保持 `SustainedLowLatency`。
- 最终本机 audio-stress 真实窗口短跑中，logic+audio p99 为 0.953 ms；主线程稳态分配 p50 为 0 B。该短跑只有 80 个 measured frame / 1.319 s，不能代替玩家包目标硬件 60 s / 3,600 frame 长跑。
- 短跑没有发生 Gen0/Gen1/Gen2 collection，`runtime_gc_pause_observed=false`、`runtime_gc_info_events=0`。这表示“本次没有观测到 GC pause”，**不表示 pause=0**，也不能替代触发真实回收后的 pause 分布。
- cache miss / branch misprediction 的 ETW 采集在当前非管理员会话被明确阻断；目标硬件 performance manifest 也不存在。因此 canonical task 转为 `[!]`，而不是以本机结果伪装完成。

## 真实内容 reaction / temperature BenchmarkDotNet

两组进程都使用 Concurrent GC，区别仅为 Workstation / Server；`MemoryDiagnoser` 的 `Allocated` 均为 `-`。p50/p95/p99 由原始日志的 `WorkloadResult` 每操作样本排序后按 `ceil(N×p)-1` 取值；BenchmarkDotNet 已剔除的 outlier 不重新加入。

| GC | 场景 | 样本数 | Mean | p50 | p95 | p99 | Max | Allocated |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| Workstation+Concurrent | reaction hit + temperature | 98 | 5.502 ms | 5.56 ms | 6.38 ms | 7.09 ms | 7.09 ms | 0 B |
| Workstation+Concurrent | reaction miss + temperature | 100 | 5.289 ms | 5.30 ms | 6.54 ms | 6.66 ms | 6.87 ms | 0 B |
| Server+Concurrent | reaction hit + temperature | 99 | 5.867 ms | 5.97 ms | 7.30 ms | 7.62 ms | 7.62 ms | 0 B |
| Server+Concurrent | reaction miss + temperature | 96 | 6.085 ms | 6.21 ms | 6.86 ms | 7.55 ms | 7.55 ms | 0 B |

按 Mean 计算，Workstation 在 hit 上比 Server 快 6.22%，在 miss 上快 13.08%。原始稳定报告与日志哈希：

| Run | 稳定报告 SHA256 | 原始日志 SHA256 |
|---|---|---|
| Workstation | `3da801947c6f478081d1fff3a089d84e5df45c1166a02d478c6565bd28608eb7` | `f23ed87b5a2122b270b5958a375d9afe6880ec31b962ff94f98e1ba3fa19e9a8` |
| Server | `a4b228188613b2f909b469688051b2dccf35a0e53fd092b35497dd21dac214f0` | `749e28386a9574514a62de9b1614b3525c041d87b3648aeff9c3b34f5b597433` |

## GC microbenchmark 与生产配置

`GcPauseBenchmark` 两个进程都使用 `SustainedLowLatency`，并分别由进程启动环境选择 Workstation+Concurrent 与 Server+Concurrent：

| GC | SteadySimulationTick | SteadyPoolRentReturn | Allocated / GC collections |
|---|---:|---:|---|
| Workstation+Concurrent | 2.666992 µs | 4.506 ns | 0 B / 0,0,0 |
| Server+Concurrent | 2.917139 µs | 4.384 ns | 0 B / 0,0,0 |

Workstation 的 simulation tick Mean 低 8.57%；pool 差异仅 0.122 ns，不改变结论。两组都没有触发 GC，因此本表只支持 throughput / allocation 定档，不提供 pause=0 结论。原始 Markdown SHA256 分别为 `38d73a4b108dfaa35bb16296133ba079d95bca3f23398f3894e785ab3efb1fed` 与 `4c45ec6375611bd63fe7ba2fc0055aa6d680d07f4178eb45ca1851933461f30a`。

`1e48faa0` 后生成的 Demo 与 Editor Shell runtimeconfig 均包含：

```json
{
  "System.GC.Concurrent": true,
  "System.GC.Server": false
}
```

## 逻辑 / 音频真实窗口短跑

当前提交上的命令：

```powershell
dotnet run --project demo/PixelEngine.Demo/PixelEngine.Demo.csproj -c Release --no-build -- `
  --no-hot-reload --no-vsync --window-ticks 120 --scripted-window-demo `
  --scene scenes/lava-mine-audio-probe.scene `
  --content demo/PixelEngine.Demo/content `
  --log-dir artifacts/perf-011-window-production-gc
```

40 warmup + 80 measured frame 的关键结果：

| 指标 | Avg | p50 | p95 | p99 | Max |
|---|---:|---:|---:|---:|---:|
| wall | 16.491 ms | 16.143 ms | 20.782 ms | 28.651 ms | 28.651 ms |
| game logic | 0.118 ms | 0.092 ms | 0.195 ms | 0.948 ms | 0.948 ms |
| audio dispatch | 0.011 ms | 0.005 ms | 0.024 ms | 0.240 ms | 0.240 ms |
| logic + audio | 0.129 ms | 0.100 ms | 0.228 ms | 0.953 ms | 0.953 ms |
| temperature | 1.408 ms | 1.168 ms | 2.024 ms | 14.405 ms | 14.405 ms |
| thread allocated | 114.7 B | 0 B | 296 B | 2,800 B | 2,800 B |

音频 probe 确认 64 个 stress event 全部入队，最大单帧 drained=64，one-shot 已播放、ambient 已激活，且限流/合并路径被观测。分配尖峰来自离散物理容量/接触事件；GUI、render-frame context、logic/audio 固定 action 的稳态路径均已归零。补充的 `BuildRenderBufferWithFrameContext` MemoryDiagnoser 为 39.35 ns/op、Allocated `-`，稳定报告 SHA256 为 `8b64123b08b95d2fe3e8314095da3e195e6ba5b3646ca3e3ffaf74db60bab090`。

## 实际修复

- `TemperatureField` 只物化活跃块及 cardinal halo，复用 conduct-active set 与有上限的 block recycle pool。
- `StaticTerrainColliders`、Marching Squares、inverse stamp 扩容与刚体 stamp 容量改为持久 scratch / 预分配，真实 Lava Mine measured tick 的 MemoryDiagnoser 为 0 B。
- 新增 `GuiTextBuffer` 与 span GUI API；game/editor `ScriptGuiContext` 复用上下文和 UTF-8 scratch；`PlayableHud` 不再创建插值字符串、帧图字符串或材质 id 字符串。
- 新增 `Scene.TryGetFirstComponent<T>`，避免 runtime 为查找单个可选 Behaviour 每帧创建完整 Inspector snapshot。
- `GuiRenderBridge` 构造时缓存脚本 GUI delegate，消除每帧约 64 B method-group 分配。
- `RenderFrameContext` 改为 readonly struct，并以 `in` 贯穿 builder，消除每帧 80 B context 对象。
- 窗口 probe 新增 game logic、temperature、world streaming、audio、logic+audio、线程分配、GC collection 与已观测 pause 统计；只有 collection count 变化时才读取 `GC.GetGCMemoryInfo()`，避免诊断本身制造稳态分配。

## 验证

- `dotnet build PixelEngine.sln -c Release --no-restore`：0 warning、0 error。
- Hosting.Tests：452 passed、4 explicit smoke skipped、0 failed。
- 受影响项目：Simulation 195、Physics 82、Rendering 176、Scripting 89、UI 106、Editor 83、Demo 132 passed；Rendering/UI/Demo 仅保留 20/9/1 个显式 GL/native smoke skip。
- GC 配置新增定向测试：模板/生成器 6 passed，用户入口契约 1 passed。
- 隔离 BDN：`BuildRenderBufferWithFrameContext` executed benchmarks=1、Allocated 0 B；首次直接运行因 `.claude/worktrees` 同名项目得到 NA，已拒绝该无效结果并改用 `tools/run-benchmark.ps1`。

## 阻塞与解除条件

```powershell
pwsh tools/hardware-counter-preflight.ps1 `
  -Artifacts artifacts/perf-011-hardware-counters `
  -Filter '*LavaMineReactionTemperatureBenchmarks*' -AllowBlocked
# blocked_non_admin

pwsh tools/performance-target-evidence-preflight.ps1 `
  -Artifacts artifacts/perf-011-target-evidence-preflight -AllowBlocked
# blocked_missing_target_performance_manifest
```

直接启用 BenchmarkDotNet hardware counters 也返回：`Must be elevated (Admin) to use ETW Kernel Session`。解除 PERF-011 阻塞必须同时满足：

1. 在 elevated ETW 或等价权限 runner 上对同一真实内容 hit/miss fixture 采集 `Cache Misses` 与 `Branch Mispredictions`，保留报告、commit、run id 与 SHA256；
2. 在代表性玩家目标硬件上运行当前生产 GC 配置的玩家包真实窗口至少 60 s / 3,600 measured frame，覆盖真实 lava-mine 温度/反应与 audio stress，报告 allocation、实际 GC pause、logic/audio p99、timeline、固定 tick 不追帧与降级观测；
3. 将同源 manifest 交给 `performance-target-evidence-preflight`，状态达到 `target_performance_evidence_attached_pending_review` 后再人工复核。该状态本身仍不自动等于完成。
