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
窗口短跑完成：frames=80, requested=80。
窗口短跑耗时：elapsed_ms=4849.30, avg_tick_ms=60.62, last_profile_ms=35.95。
脚本化窗口输入摘要：frames=80, brush_material=stone, brush_radius=5, painted_material=13, explosions=1, last_explosion=(90.00,240.00), particles=31, max_particles=61, lights=2, max_lights=4, physics_destroyed=0, physics_created=0, max_physics_destroyed=2, max_physics_created=2。
```

## 结论

该短跑证明真实窗口模式下，窗口创建、Silk 输入采样、脚本输入覆盖、脚本相机坐标转换、材质笔刷、爆破工具、自由粒子、脚本点光、Physics 刚体拆分与渲染相位可以在同一运行态链路中自动触发并自然退出。

该验证不等同于人工窗口验收：它不证明 HUD 像素布局、鼠标真实设备手感、音频听感、视觉 bloom/fog 质量、完整通关路线或开发态热重载体验。`avg_tick_ms=60.62` 与 `last_profile_ms=35.95` 仍不满足稳定 60fps 帧预算。
