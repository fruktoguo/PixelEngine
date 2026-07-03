# 2026-07-02 Demo 窗口长跑与释放审计

## 环境

- 平台：Windows / `win-x64`
- 配置：Release
- 内容目录：`demo/PixelEngine.Demo/content`
- 热重载：关闭
- 采样方式：PowerShell 外部启动 `dotnet run`，轮询进程工作集并等待进程自然退出。

## 非 Editor 窗口长跑

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-restore -- --no-hot-reload --window-ticks 3600 --content demo\PixelEngine.Demo\content --log-dir artifacts\window-longrun-logs
```

结果：

```text
exit=0 elapsed_ms=191521 peak_ws_mb=162.54
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=3600, requested=3600。
```

## Editor 窗口长跑

```pwsh
dotnet run --project demo\PixelEngine.Demo\PixelEngine.Demo.csproj -c Release --no-restore -- --editor --no-hot-reload --window-ticks 1200 --content demo\PixelEngine.Demo\content --log-dir artifacts\editor-window-longrun-logs
```

结果：

```text
exit=0 elapsed_ms=62080 peak_ws_mb=162.22
PixelEngine.Demo 0.1.0.0
RID: win-x64
内容包已加载：18 个材质，22 条反应，19 个音频 clip，Physics 已接入。
脚本程序集已注册；热重载已由参数关闭。
脚本运行时已接入 Hosting/Simulation 后端。
窗口运行时已接入 Rendering/Input 后端。
窗口短跑完成：frames=1200, requested=1200。
```

## 结论

本机 `win-x64` Release 窗口路径已验证有限长跑后自然退出：非 Editor 路径 3600 tick，Editor 路径 1200 tick。两条路径都装配 Content、Simulation、Physics、Audio、Scripting、Rendering 与 Input；Editor 路径额外装配 EditorRenderBridge 与 Hexa ImGui OpenGL3 后端。

该报告只覆盖本机进程级退出码、运行耗时和峰值工作集；不替代 6-RID runner、专用 native leak detector、GPU driver 资源审计、OpenAL/Box2D 工具级泄漏报告或人工玩法验收。

## Native Leak 预检入口

为避免进程级长跑被误当作 native 泄漏验收，新增统一预检脚本：

```pwsh
./tools/native-leak-preflight.ps1 -RunProcessSmoke -IncludeEditor
```

未传入 `-DetectorReportPath` 时，脚本会写出 `artifacts/native-leak-preflight/native-leak-preflight.md`，状态为 `process_smoke_only` 或 `blocked_missing_detector`，并以非零退出；仅用于本地记录 smoke 报告时可追加 `-AllowBlocked`。只有附带专用 detector 报告时才可能进入 `detector_report_attached_pending_review`，且该单报告必须声明 `detector`、`conclusion=no_leaks`、`scopes=GL; OpenAL; Box2D; ALC`，并给出 `glObjectsLiveAfterShutdown=0`、`openAlObjectsLiveAfterShutdown=0`、`box2DBodiesLiveAfterShutdown=0`、`alcLoadContextsAliveAfterUnload=0`；任意文本文件或缺机器可读覆盖字段的报告会被拒绝为 `blocked_invalid_native_leak_evidence`。即使进入 pending review，仍需人工确认报告确实来自可信 detector 并覆盖 GL、OpenAL、Box2D 与 ALC 释放路径无泄漏。

更严格的工具级证据入口是 `-EvidenceManifestPath`。manifest 需要使用 `schemaVersion: 1`，顶层声明 `detectorRunId` 与 `gitCommit`，只允许 `gl`、`openal`、`box2d` 与 `alc` 四类 scope，并为各自 report 文件声明同一个 `detectorRunId` / `gitCommit`、`detector` 与 `sha256`；脚本会重新计算文件 SHA256 并比对。每份 detector report 除 `scope`、`detector`、`detectorRunId`、`gitCommit`、`conclusion=no_leaks` 外，还必须声明对应释放后 live-object 计数为 0：`glObjectsLiveAfterShutdown`、`openAlObjectsLiveAfterShutdown`、`box2DBodiesLiveAfterShutdown`、`alcLoadContextsAliveAfterUnload`。OpenAL 后端已暴露 `LiveSourceCount`、`LiveBufferCount` 与 `LiveObjectCount`，供本机 detector 在 `AudioSystem.Shutdown()` 后采集 `openAlObjectsLiveAfterShutdown`；Physics 已暴露 `PhysicsSystem.LiveBodyCount`，计入动态刚体与静态地形 collider，供 detector 在 owned world shutdown 后采集 `box2DBodiesLiveAfterShutdown`；Scripting 已暴露 `ScriptHotReloadController.CollectAndCountUnloadedLoadContextsAlive()`，供 detector 在旧脚本 ALC 调用 `Unload()` 并完整 GC 后采集 `alcLoadContextsAliveAfterUnload`。四类证据齐全且来自同一个 detector run / git commit 时状态为 `detector_evidence_attached_pending_review`，仍不自动代表无泄漏验收通过；未传 `-AllowBlocked` 时保持非零退出，避免把 pending review 当成 plan/18 验收完成。缺任一 scope/report/hash 时状态为 `blocked_missing_scope_evidence`；JSON、schema、未知 scope、run/commit 不同源、hash 不匹配或 live-object 计数缺失/非 0 时状态为 `blocked_invalid_native_leak_evidence`。
