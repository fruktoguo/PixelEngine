# 2026-07-10 EDITOR-003 Editor Preferences 与 150% UI Scale 自动化证据

taskIds: `EDITOR-003`
implementationCommit: `8d8598fc8dc391ad0b26e5b14c5b6a269c26bb58`
runSessionId: `local-20260710-editor-preferences-150`
evidenceState: `automated_preferences_slice_complete_pending_human_review`

## 结论

实现 commit `8d8598fc` 已补齐 Unity-like `Edit > Preferences...` 用户级窗口和 75%–200% UI Scale。150% 会同时作用于启动字体 atlas、运行时字体倍率、ImGui style metrics、顶部工具栏、Project Picker 与 Preferences 尺寸；无工程和已打开工程使用同一份 `%APPDATA%/PixelEngine/editor-preferences.json`，打开工程时不会从 150% 跳回 100%。

原来默认展开的辅助窗口已改为默认隐藏：工作台首次布局只显示 Hierarchy、Scene / Game View、Inspector、Project / Console；Profiler、Settings、Simulation、UI Manifest 与各调参窗口保留在分类后的 `Window` 菜单中按需打开。`Reset Layout` 恢复注册时默认可见性，不再调用 Show All。

本报告只闭合 Preferences / UI Scale 自动化切片，不替代 EDITOR-003 要求的人工 author→play→edit→build→run 完整路线、真实鼠标键盘点击或 reviewer 结论。

## 设置作用域与行为

- Preferences 使用版本化 JSON、同目录临时文件、WriteThrough、flush-to-disk 与原子 move；损坏文件回退默认值并保留可见诊断。
- `Save layout on exit` 已真实接入两套 ImGui backend；禁用时不写布局，启用时只写显式 AppData layout path，同时关闭 ImGui 默认 `imgui.ini`，避免双重布局存储。
- `External script editor` 从 Project Settings 移到用户级 External Tools；脚本 opener 不再读取 `ProjectSettingsDto`。旧工程中的自定义 executable command 不会被静默迁移或执行，必须由用户在 Preferences 中重新确认。
- Shortcuts 页面读取真实命令表；Ctrl+S、Ctrl+Shift+S、Ctrl+Z、Ctrl+Y、Ctrl+D、Ctrl+P、Ctrl+, 已接入菜单和全局命令调度，不再只是帮助文案。当前未实现可重绑定系统，因此没有伪造按键编辑 UI。
- 当前没有稳定 PanelId / string catalog，本切片没有暴露无效语言下拉框；语言切换仍需独立本地化设计。

## 自动化验证

| 命令 | 结果 |
|---|---|
| `dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1` | 32 projects，0 warning，0 error |
| `dotnet test tests/PixelEngine.Editor.Tests/PixelEngine.Editor.Tests.csproj -c Release --no-restore --disable-build-servers -m:1` | 84/84 passed |
| `dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release --no-restore --disable-build-servers -m:1` | 106 passed，9 个显式 GL/ANGLE 环境 smoke skipped |
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-restore --disable-build-servers -m:1 --filter "FullyQualifiedName~HostingProjectDisciplineTests|FullyQualifiedName~EditorPreferencesTests|FullyQualifiedName~EditorScriptAssetOpenServiceTests|FullyQualifiedName~EditorShellSettingsStoresAndPanelsRoundTripHostingDtos"` | 69/69 passed |
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-restore --disable-build-servers -m:1 --filter "FullyQualifiedName!~PerformanceHardeningToolingDisciplineTests"` | 344 passed，4 个环境门控 smoke skipped |

Hosting 不带过滤的 475 项命令在 304 秒工具上限到达时，仍在执行会反复启动 PowerShell / 打包审计的 `PerformanceHardeningToolingDisciplineTests`，该轮没有完成结果，因此不计为通过；排除这组独立发行工具测试后的 348 项 Hosting 回归完整结束并通过。

`EditorPreferencesTests.UiScaleContextStateDoesNotCompoundAcrossFrames` 额外锁定同一 150% 倍率连续应用 12 帧不会重复调用 `ScaleAllSizes` 产生指数膨胀，并验证切回 100% 后 style 恢复基线、字体相对 1.5x atlas 使用 2/3 显示倍率。

## 真实窗口 scripted probe

本机环境：Windows build 26100、win-x64、.NET SDK 10.0.108。探针通过环境变量把 Preferences 与 layout 指向 `artifacts/` 隔离路径，没有修改用户现有 AppData 设置。

### Preferences 150%

```powershell
$env:PIXELENGINE_EDITOR_PREFERENCES_PATH=(Resolve-Path 'artifacts/editor-preferences-150.json').Path
$env:PIXELENGINE_EDITOR_LAYOUT_PATH=(Join-Path (Resolve-Path 'artifacts').Path 'editor-preferences-layout.ini')
dotnet run --project apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj -c Release --no-build -- --scripted-preferences-probe --window-ticks 12 --capture-frame artifacts/editor-preferences-150.bmp
```

结构化输出：`preferences_visible=True`、`ui_scale_percent=150`、`window_pos=25,24`、`window_size=1230x672`、`navigation_visible=True`。1280×720 framebuffer 截图人工复核可见完整 Preferences 标题、Appearance / General / External Tools / Shortcuts 左侧分类、150% slider 与中文说明，无裁切；该 BMP 是可再生 artifact，不作为唯一稳定证据。

### 默认工作台 150%

```powershell
$env:PIXELENGINE_EDITOR_PREFERENCES_PATH=(Resolve-Path 'artifacts/editor-preferences-150.json').Path
$env:PIXELENGINE_EDITOR_LAYOUT_PATH=(Join-Path (Resolve-Path 'artifacts').Path 'editor-workbench-150-layout.ini')
dotnet run --project apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj -c Release --no-build -- --project demo/PixelEngine.Demo --window-ticks 20 --capture-frame artifacts/editor-workbench-150.bmp
```

输出为 `editor_running=True`、`editor_panels=23`、`editor_bridge_frames=20`、`render_camera_synced=True`。截图人工复核只显示核心六窗口：Hierarchy、Scene / Game View、Inspector、Project / Console；底部不再出现 Settings / Profiler / Simulation / 调参窗口堆叠。

### 布局保存开关

- `saveLayoutOnExit=false` 的两次窗口 probe 结束后，隔离 layout path 均不存在。
- `saveLayoutOnExit=true` 的 4 帧 probe 结束后，`editor-save-layout-proof.ini` 存在且为 2085 bytes。

## 未闭合边界

Windows Computer Use 的 native pipe 本轮不可用（`os error 2`），因此没有伪造鼠标点击、快捷键手感或 4K 实机 reviewer 结论；窗口验证使用正式 EditorShell EXE 路径、真实 OpenGL framebuffer capture 与结构化 probe。EDITOR-003 仍需人工完整路线、目标 4K 显示器上的实际 100%↔150% 操作、菜单点击和可读性评审后才能关闭。
