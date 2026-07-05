# 2026-07-06 EditorShell 外部窗口 attach 探针

目标：验证 `apps/PixelEngine.Editor.Shell` 使用 shell 持有的唯一 `RenderWindow`，通过 `Engine.AttachWindowRuntime(window)` 装配 Engine；关闭工程时释放 Engine 但保留窗口，并可重建 session；Shell 侧 `IEditorHostExtension` 注册 EditorApp 与 GameObject 面板。

命令：

```pwsh
dotnet run --project apps\PixelEngine.Editor.Shell\PixelEngine.Editor.Shell.csproj -c Release -- --project <temp-editor-shell-project> --scene scenes/main.scene --window-ticks 90 --scripted-probe --log-directory <temp>\logs
```

临时工程包含 `project.pixelproj` 与空 `content/scenes/main.scene`。该探针创建真实窗口并运行有限帧，不使用人工验收视频，也不替代 `plan/18` 的 editor-window 人工复核项。

结果：

```text
frame_samples=90, editor_enabled=True, editor_running=True, editor_panels=17, editor_bridge_frames=48, render_camera_synced=True, scripted_play_entered=True, scripted_play_exited=True, scripted_scene_saved=True, scripted_project_closed=True, scripted_project_reopened=True, project_open=True, window_ticks=90
```

结论：机器探针证明 EditorShell 可在真实窗口中 attach Hosting 外部窗口、运行 Editor bridge、执行 Play/Edit 往返、关闭并重开工程 session。
