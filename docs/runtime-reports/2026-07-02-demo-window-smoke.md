# 2026-07-02 Demo 窗口短跑验证

## 环境

- 平台：Windows / `win-x64`
- 配置：Release
- 内容目录：`demo/PixelEngine.Demo/content`
- 热重载：关闭

## 命令

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-restore -- --no-hot-reload --window-ticks 120 --content demo\PixelEngine.Demo\content --log-dir artifacts\window-smoke-logs
```

Editor 窗口入口：

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-restore -- --editor --no-hot-reload --window-ticks 60 --content demo\PixelEngine.Demo\content --log-dir artifacts\editor-window-smoke-logs
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
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=120, requested=120。
```

Editor 窗口关键输出：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=60, requested=60。
```

## 结论

本机真实窗口路径能装配 Content、Simulation、Physics、Audio、Scripting、Rendering 与 Input 后端，并稳定执行 120 个 Engine tick 后正常释放退出。Editor 窗口路径能额外装配 EditorRenderBridge 与 Hexa ImGui OpenGL3 后端，并执行 60 个 Engine tick 后正常退出。

该验证只证明窗口运行态 smoke 与 Editor 首帧 UI 后端初始化通过；不替代真实玩家输入手感、GUI 鼠标交互、完整关卡通关、视觉/音频听感、长跑 60fps 或 native 泄漏审计。
