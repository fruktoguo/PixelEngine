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

## 当前提交复验

在 `1e02ce3 fix(sim): 修复窗口短跑边界调度崩溃` 后复跑窗口短跑，确认脚本 cell 同 tick 生效、固定 resident world guard chunk 调度过滤、带孔刚体 mask 外轮廓修复没有破坏真实窗口路径。

普通窗口复验命令：

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-restore -- --no-hot-reload --window-ticks 120 --content demo\PixelEngine.Demo\content --log-dir artifacts\window-smoke-logs-current
```

结果：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=120, requested=120。
```

Editor 窗口复验命令：

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-restore -- --editor --no-hot-reload --window-ticks 60 --content demo\PixelEngine.Demo\content --log-dir artifacts\editor-window-smoke-logs-current
```

结果：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=60, requested=60。
```

## 帧耗时指标采样

在 `DemoProgram` 的有限窗口短跑路径中增加 wall-clock tick 摘要后，复跑 30 tick 短样本，确认窗口运行态可以直接输出本轮总耗时、平均 tick 耗时与最后一帧诊断计时器合计。

命令：

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-restore -- --no-hot-reload --window-ticks 30 --content demo\PixelEngine.Demo\content --log-dir artifacts\window-metrics-smoke-logs\runtime
```

结果：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=30, requested=30。
窗口短跑耗时：elapsed_ms=3220.07, avg_tick_ms=107.34, last_profile_ms=49.20。
```

该样本只证明指标采集链路可用；`avg_tick_ms=107.34` 与 `last_profile_ms=49.20` 仍不满足稳定 60fps 帧预算，不能替代 plan/14 的正式运行态性能验收。

## 空场景窗口帧预算探针

新增 `empty-window-probe.scene`，该场景不物化 Demo Behaviour，只让 Demo 装配内容包、resident simulation world、Physics、Audio、Scripting 空 scene 与真实 Rendering/Input 窗口运行时，用于把“空场景稳定 60fps”与完整 Demo 关卡分开观察。

命令：

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-restore -- --no-hot-reload --window-ticks 120 --content demo\PixelEngine.Demo\content --scene scenes\empty-window-probe.scene --log-dir artifacts\empty-window-probe-logs\runtime
```

结果：

```text
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=120, requested=120。
窗口短跑耗时：elapsed_ms=4751.20, avg_tick_ms=39.60, last_profile_ms=31.24。
窗口短跑最慢相位：main_top=BuildRenderBuffer=25.52, sub_top=RenderBufferBuild=25.51。
```

该探针退出码为 0，证明空 scene 窗口链路可自然退出；但 `avg_tick_ms=39.60` 与 `last_profile_ms=31.24` 仍高于 16.67ms 帧预算，不能勾选稳定 60fps 验收。相位拆分显示当前瓶颈集中在 `BuildRenderBuffer` / `RenderBufferBuild`。

## 结论

本机真实窗口路径能装配 Content、Simulation、Physics、Audio、Scripting、Rendering 与 Input 后端，并稳定执行 120 个 Engine tick 后正常释放退出。Editor 窗口路径能额外装配 EditorRenderBridge 与 Hexa ImGui OpenGL3 后端，并执行 60 个 Engine tick 后正常退出。

该验证只证明窗口运行态 smoke、空场景窗口探针与 Editor 首帧 UI 后端初始化通过；不替代真实玩家输入手感、GUI 鼠标交互、完整关卡通关、视觉/音频听感、长跑 60fps 或 native 泄漏审计。
