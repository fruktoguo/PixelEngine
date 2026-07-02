# 2026-07-02 Demo 脚本化窗口短跑验证

## 环境

- 平台：Windows / `win-x64`
- 配置：Release
- 内容目录：`demo/PixelEngine.Demo/content`
- 热重载：关闭
- 输入方式：Demo 专用 `--scripted-window-demo`，在真实窗口短跑中向 `ScriptInputApi` 注入固定键鼠脚本；窗口、Silk 输入采样、Rendering、Audio、Physics 与 Scripting 仍按真实窗口相位链路装配。

## 命令

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-restore -- --no-hot-reload --window-ticks 80 --scripted-window-demo --content demo\PixelEngine.Demo\content --log-dir artifacts\scripted-window-demo-logs\runtime
```

通关触发窗口探针使用独立的探针场景，让玩家出生点与出口触发区重叠；它只验证真实窗口相位中的 `GoalTrigger` 链路，不代表完整路线玩法验收：

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-restore -- --no-hot-reload --window-ticks 40 --scripted-window-demo --content demo\PixelEngine.Demo\content --scene scenes\lava-mine-goal-probe.scene --log-dir artifacts\scripted-window-goal-probe-logs\runtime
```

玩家生命窗口探针使用独立的探针场景，通过 `LevelDirector.BuildSpawnHazardProbe` 显式把 `PlayerHealth` 放入伤害路径；headless 单元测试已覆盖真实材质 AABB 采样，本探针只补真实窗口相位链路中的伤害、粒子与音频事件抽取证据：

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-build -- --no-hot-reload --window-ticks 80 --scripted-window-demo --content demo\PixelEngine.Demo\content --scene scenes\lava-mine-health-probe.scene --log-dir artifacts\scripted-window-health-probe-logs\runtime-4
```

相机跟随窗口探针使用独立的探针场景，把 `LevelDirector.CameraZoom` 设为 4，让窗口视口小于关卡边界；它只验证真实窗口相位中玩家移动、`CameraFollow`、`ScriptCameraSynchronizer` 与 Rendering `CameraState` 的状态链路，不代表人工观感流畅度验收：

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-build -- --no-hot-reload --window-ticks 80 --scripted-window-demo --content demo\PixelEngine.Demo\content --scene scenes\lava-mine-camera-probe.scene --log-dir artifacts\scripted-window-camera-probe-logs\runtime-3
```

反应与温度相变窗口探针使用独立的空脚本场景，由 Demo 专用探针在真实窗口相位中布置玻璃容器内的材质样本，并在 CA/Temperature 后统计材质变化；它只验证真实窗口相位链路中的材质 before/after 变化，不代表反应和相变的视觉质量人工验收：

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-build -- --no-hot-reload --window-ticks 180 --scripted-window-demo --content demo\PixelEngine.Demo\content --scene scenes\lava-mine-reaction-probe.scene --log-dir artifacts\scripted-window-reaction-probe-logs\runtime-7
```

音频窗口探针使用独立的空脚本场景，由 Demo 专用探针在真实窗口相位中向 Core 音频事件 ring 注入 stone explosion、water splash 与 lava ambient 事件，并追加 64 个高密度 water splash 压力事件；它只验证材质 cue 解析、事件派发、one-shot voice、ambient voice 与高密度事件限流，不代表真实设备听感验收：

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-build -- --no-hot-reload --window-ticks 30 --scripted-window-demo --content demo\PixelEngine.Demo\content --scene scenes\lava-mine-audio-probe.scene --log-dir artifacts\scripted-window-audio-probe-logs\runtime-3
```

粒子与光照窗口探针使用独立的空脚本场景，由 Demo 专用探针在真实窗口相位中一次性生成短寿命 fire 粒子，并发出点光与 fog reveal 请求；它只验证粒子 lifecycle、点光同步和 fog-of-war reveal 数据，不代表粒子/bloom/fog 的视觉质量人工验收：

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-build -- --no-hot-reload --window-ticks 120 --scripted-window-demo --content demo\PixelEngine.Demo\content --scene scenes\lava-mine-particle-light-probe.scene --log-dir artifacts\scripted-window-particle-light-probe-logs\runtime-2
```

## 结果

退出码：0。

关键输出：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
脚本化窗口输入已启用。
窗口运行时已接入 Rendering/Input 后端。
libpng warning: iCCP: known incorrect sRGB profile
libpng warning: iCCP: known incorrect sRGB profile
libpng warning: iCCP: known incorrect sRGB profile
libpng warning: iCCP: known incorrect sRGB profile
窗口短跑完成：frames=80, requested=80。
窗口短跑耗时：elapsed_ms=5732.84, avg_tick_ms=71.66, last_profile_ms=31.27。
脚本化窗口输入摘要：frames=80, brush_material=stone, brush_radius=5, painted_material=13, explosions=1, last_explosion=(90.00,240.00), particles=32, max_particles=61, lights=2, max_lights=4, physics_destroyed=0, physics_created=0, max_physics_destroyed=2, max_physics_created=2, audio_played=0, audio_drained=0, max_audio_played=0, max_audio_drained=20, audio_loaded=19, hud_blocked=none, pause_open=True, goal_reached=False。
```

通关触发窗口探针关键输出：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
脚本化窗口输入已启用。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=40, requested=40。
窗口短跑耗时：elapsed_ms=3976.97, avg_tick_ms=99.43, last_profile_ms=38.30。
脚本化窗口输入摘要：frames=40, brush_material=stone, brush_radius=5, painted_material=13, explosions=1, last_explosion=(90.00,240.00), particles=34, max_particles=97, lights=2, max_lights=4, physics_destroyed=0, physics_created=0, max_physics_destroyed=2, max_physics_created=2, audio_played=0, audio_drained=2, max_audio_played=0, max_audio_drained=15, audio_loaded=19, hud_blocked=none, pause_open=False, goal_reached=True。
```

玩家生命窗口探针关键输出：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
脚本化窗口输入已启用。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=80, requested=80。
窗口短跑耗时：elapsed_ms=4477.54, avg_tick_ms=55.97, last_profile_ms=3.86。
窗口短跑最慢相位：main_top=GpuUploadRender=3.80, sub_top=GpuUpload=3.63。
脚本化窗口输入摘要：frames=80, brush_material=stone, brush_radius=5, painted_material=13, explosions=1, last_explosion=(90.00,240.00), particles=34, max_particles=71, lights=2, max_lights=4, physics_destroyed=0, physics_created=0, max_physics_destroyed=2, max_physics_created=2, audio_played=0, audio_drained=0, max_audio_played=0, max_audio_drained=19, audio_loaded=19, hud_blocked=none, pause_open=True, goal_reached=False, player_health=12.50, damage_events=70, respawns=0, spawn_probe=True, player=(260.00,288.00,6.00,12.00), player_center_material=13。
```

相机跟随窗口探针关键输出：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
脚本化窗口输入已启用。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=80, requested=80。
窗口短跑耗时：elapsed_ms=4838.13, avg_tick_ms=60.48, last_profile_ms=4.81。
窗口短跑最慢相位：main_top=GpuUploadRender=4.34, sub_top=GpuUpload=4.00。
脚本化窗口输入摘要：frames=80, brush_material=stone, brush_radius=5, painted_material=13, explosions=1, last_explosion=(90.00,241.24), particles=29, max_particles=61, lights=2, max_lights=4, physics_destroyed=0, physics_created=0, max_physics_destroyed=2, max_physics_created=2, audio_played=0, audio_drained=0, max_audio_played=0, max_audio_drained=22, audio_loaded=19, hud_blocked=none, pause_open=True, goal_reached=False, player_health=100.00, damage_events=0, respawns=0, spawn_probe=False, player=(311.85,273.20,6.00,12.00), player_center=(314.85,279.20), camera_center=(314.87,270.00), camera_zoom=4.00, camera_samples=79, camera_followed=True, render_camera_synced=True, player_x_range=(303.00,314.85), camera_x_range=(303.00,336.29), render_origin_x_range=(143.00,176.29), render_camera=(154.87,180.00,0.250,1280x720), player_center_material=0。
```

反应与温度相变窗口探针关键输出：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
脚本化窗口输入已启用。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=180, requested=180。
窗口短跑耗时：elapsed_ms=6881.72, avg_tick_ms=38.23, last_profile_ms=30.40。
窗口短跑最慢相位：main_top=BuildRenderBuffer=23.24, sub_top=RenderBufferBuild=22.78。
脚本化窗口输入摘要：frames=180, brush_material=<missing>, brush_radius=0, painted_material=0, explosions=0, last_explosion=(0.00,0.00), particles=2052, max_particles=5003, lights=0, max_lights=0, physics_destroyed=0, physics_created=0, max_physics_destroyed=0, max_physics_created=0, audio_played=0, audio_drained=0, max_audio_played=0, max_audio_drained=316, audio_loaded=19, hud_blocked=none, pause_open=<missing>, goal_reached=<missing>, player_health=0.00, damage_events=0, respawns=0, spawn_probe=<missing>, player=(0.00,0.00,0.00,0.00), player_center=(0.00,0.00), camera_center=(320.00,180.00), camera_zoom=1.00, camera_samples=0, camera_followed=False, render_camera_synced=True, player_x_range=(0.00,0.00), camera_x_range=(0.00,0.00), render_origin_x_range=(0.00,0.00), render_camera=(-320.00,-180.00,1.000,1280x720), reaction_probe_initialized=True, reactions_observed=True, phase_transitions_observed=True, reaction_cases=(lava_water=True;molten_water=True;water_fire=True;fire_wood=True;fire_oil=True;acid=True;steam_condense=True), phase_cases=(ice_melted=True;water_boiled=True;water_froze=True;lava_cooled=True;metal_melted=True;sand_glassed=True), probe_counts=(water=444;lava=0;stone=1086;steam=600;fire=1;smoke=954;wood=36;oil=0;acid=432;acid_gas=430;ice=1336;metal=644;molten_metal=7;sand=15;glass=1335), player_center_material=0。
```

音频窗口探针关键输出：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
脚本化窗口输入已启用。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=30, requested=30。
窗口短跑耗时：elapsed_ms=1221.22, avg_tick_ms=40.71, last_profile_ms=6.99。
窗口短跑最慢相位：main_top=GpuUploadRender=3.89, sub_top=GpuUpload=3.57。
脚本化窗口输入摘要：frames=30, brush_material=<missing>, brush_radius=0, painted_material=0, explosions=0, last_explosion=(0.00,0.00), particles=0, max_particles=0, lights=0, max_lights=0, physics_destroyed=0, physics_created=0, max_physics_destroyed=0, max_physics_created=0, audio_played=0, audio_drained=0, max_audio_played=2, max_audio_drained=64, audio_loaded=19, hud_blocked=none, pause_open=<missing>, goal_reached=<missing>, player_health=0.00, damage_events=0, respawns=0, spawn_probe=<missing>, player=(0.00,0.00,0.00,0.00), player_center=(0.00,0.00), camera_center=(320.00,180.00), camera_zoom=1.00, camera_samples=0, camera_followed=False, render_camera_synced=True, player_x_range=(0.00,0.00), camera_x_range=(0.00,0.00), render_origin_x_range=(0.00,0.00), render_camera=(-320.00,-180.00,1.000,1280x720), audio_probe_initialized=True, audio_probe_enqueued=True, audio_probe_stress_enqueued=64, audio_probe_one_shot_played=True, audio_probe_ambient_activated=True, audio_probe_limited=True, audio_probe_max_drained=64, audio_probe_max_coalesced=0, audio_probe_max_dropped=64, audio_probe_max_played=2, audio_probe_max_active_voices=2, audio_probe_max_active_ambient=1, player_center_material=0。
```

粒子与光照窗口探针关键输出：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
脚本化窗口输入已启用。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=120, requested=120。
窗口短跑耗时：elapsed_ms=4759.61, avg_tick_ms=39.67, last_profile_ms=27.43。
窗口短跑最慢相位：main_top=BuildRenderBuffer=21.30, sub_top=RenderBufferBuild=21.28。
脚本化窗口输入摘要：frames=120, brush_material=<missing>, brush_radius=0, painted_material=0, explosions=0, last_explosion=(0.00,0.00), particles=0, max_particles=96, lights=0, max_lights=1, physics_destroyed=0, physics_created=0, max_physics_destroyed=0, max_physics_created=0, audio_played=0, audio_drained=0, max_audio_played=0, max_audio_drained=3, audio_loaded=19, hud_blocked=none, pause_open=<missing>, goal_reached=<missing>, player_health=0.00, damage_events=0, respawns=0, spawn_probe=<missing>, player=(0.00,0.00,0.00,0.00), player_center=(0.00,0.00), camera_center=(320.00,180.00), camera_zoom=1.00, camera_samples=0, camera_followed=False, render_camera_synced=True, player_x_range=(0.00,0.00), camera_x_range=(0.00,0.00), render_origin_x_range=(0.00,0.00), render_camera=(-320.00,-180.00,1.000,1280x720), particle_light_probe_initialized=True, particle_light_probe_spawned=96, particle_light_probe_max_active=96, particle_light_probe_tail_max=0, particle_light_probe_last_active=0, particle_light_probe_lifetime_kill=True, particle_light_probe_depleted=True, particle_light_probe_light_observed=True, particle_light_probe_fog_alpha=220, particle_light_probe_lighting_synced=True, player_center_material=0。
```

## 结论

该短跑证明真实窗口模式下，窗口创建、Silk 输入采样、脚本输入覆盖、脚本相机坐标转换、材质笔刷、爆破工具、自由粒子、脚本点光、Physics 刚体拆分、音频事件抽取、HUD 组件绑定、暂停菜单打开与渲染相位可以在同一运行态链路中自动触发并自然退出。

通关触发窗口探针额外证明真实窗口相位中 `GoalTrigger` 可以把玩家进入出口区域转换为 `goal_reached=True`，并伴随粒子、点光与音频事件抽取。

玩家生命窗口探针额外证明真实窗口相位中 `PlayerHealth` 可以进入受伤路径，`damage_events=70` 且 `player_health=12.50`，并伴随粒子与音频事件抽取；真实材质 AABB 采样本身由 `PlayerHealthSamplesHazardsEmitsFeedbackAndRespawns` 覆盖。

相机跟随窗口探针额外证明真实窗口相位中 `CameraFollow` 可以在玩家移动后更新脚本相机中心，`ScriptCameraSynchronizer` 可以把脚本快照同步为 Rendering `CameraState`：本次有效相机采样 79 帧，`player_x_range=(303.00,314.85)`，`camera_x_range=(303.00,336.29)`，`render_origin_x_range=(143.00,176.29)`，并输出 `camera_followed=True` 与 `render_camera_synced=True`。

反应与温度相变窗口探针额外证明真实窗口相位中，已加载 `ReactionTable` 与 `TemperatureField.ApplyPhaseTransitions` 会在 CA/Temperature 后产生目标材质变化：`reactions_observed=True` 覆盖熔岩遇水、熔融金属遇水、水灭火、火烧木、火烧油、酸腐蚀与蒸汽冷凝；`phase_transitions_observed=True` 覆盖冰融化、水沸腾、水冻结、熔岩冷却、金属熔化与沙烤玻璃。

音频窗口探针额外证明真实窗口相位中，`content/audio/cues.json` 与 `materials.json` 的 cue 映射可以把材质音频事件解析为已加载 clip，并交给后端 source：`audio_probe_one_shot_played=True` 覆盖 stone explosion 与 water splash 的 one-shot voice，`audio_probe_ambient_activated=True` 覆盖 lava ambient loop 激活。高密度样本额外证明窗口态音频派发会对满屏 splash 事件限流：`audio_probe_stress_enqueued=64`、`audio_probe_limited=True` 与 `audio_probe_max_dropped=64`。

粒子与光照窗口探针额外证明真实窗口相位中，短寿命 fire 粒子会进入粒子系统并按 lifetime 退场：`particle_light_probe_spawned=96`、`particle_light_probe_max_active=96`、`particle_light_probe_tail_max=0`、`particle_light_probe_last_active=0`、`particle_light_probe_lifetime_kill=True` 与 `particle_light_probe_depleted=True`。同一探针还证明脚本点光与 fog reveal 请求会同步到 Rendering 可消费状态：`particle_light_probe_light_observed=True`、`particle_light_probe_fog_alpha=220` 与 `particle_light_probe_lighting_synced=True`。

这些验证不等同于人工窗口验收：它们不证明 HUD 像素布局、鼠标真实设备手感、音频听感、反应/相变视觉质量、视觉 bloom/fog 质量、完整通关路线或开发态热重载体验。主线短跑的 `goal_reached=False` 表示固定输入脚本未覆盖完整通关路线，探针场景也不替代从出生点抵达出口的玩法验收；旧主线短跑的 `avg_tick_ms=71.66` 与 `last_profile_ms=31.27` 是空世界渲染快路径修复前的样本，不代表当前空场景帧预算，后者见 `docs/runtime-reports/2026-07-02-demo-window-smoke.md`。
