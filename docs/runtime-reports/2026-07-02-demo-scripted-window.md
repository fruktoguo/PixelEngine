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

## 结论

该短跑证明真实窗口模式下，窗口创建、Silk 输入采样、脚本输入覆盖、脚本相机坐标转换、材质笔刷、爆破工具、自由粒子、脚本点光、Physics 刚体拆分、音频事件抽取、HUD 组件绑定、暂停菜单打开与渲染相位可以在同一运行态链路中自动触发并自然退出。

通关触发窗口探针额外证明真实窗口相位中 `GoalTrigger` 可以把玩家进入出口区域转换为 `goal_reached=True`，并伴随粒子、点光与音频事件抽取。

玩家生命窗口探针额外证明真实窗口相位中 `PlayerHealth` 可以进入受伤路径，`damage_events=70` 且 `player_health=12.50`，并伴随粒子与音频事件抽取；真实材质 AABB 采样本身由 `PlayerHealthSamplesHazardsEmitsFeedbackAndRespawns` 覆盖。

相机跟随窗口探针额外证明真实窗口相位中 `CameraFollow` 可以在玩家移动后更新脚本相机中心，`ScriptCameraSynchronizer` 可以把脚本快照同步为 Rendering `CameraState`：本次有效相机采样 79 帧，`player_x_range=(303.00,314.85)`，`camera_x_range=(303.00,336.29)`，`render_origin_x_range=(143.00,176.29)`，并输出 `camera_followed=True` 与 `render_camera_synced=True`。

这些验证不等同于人工窗口验收：它们不证明 HUD 像素布局、鼠标真实设备手感、音频听感、视觉 bloom/fog 质量、完整通关路线或开发态热重载体验。`max_audio_played=0` 表示本次只证明事件进入音频相位并被抽取，不证明实际可听播放；主线短跑的 `goal_reached=False` 表示固定输入脚本未覆盖完整通关路线，探针场景也不替代从出生点抵达出口的玩法验收；旧主线短跑的 `avg_tick_ms=71.66` 与 `last_profile_ms=31.27` 是空世界渲染快路径修复前的样本，不代表当前空场景帧预算，后者见 `docs/runtime-reports/2026-07-02-demo-window-smoke.md`。
