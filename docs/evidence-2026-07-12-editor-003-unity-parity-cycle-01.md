# 2026-07-12 EDITOR-003 Unity 6.5 对标循环 01：全局 chrome 与主题

taskIds: `EDITOR-003`
implementationCommit: `5f916a1e832c55535fbb567f0804ffb04ad2c630`
runSessionId: `local-20260712-editor003-unity-parity-cycle01`
evidenceState: `unity_parity_cycle_complete_task_still_active`

## 结论

本轮在同一台 Windows 主机上真实运行 Unity 6.5 `6000.5.3f1` 与 PixelEngine Editor，完成第一轮默认工作台差异确认，并由实现 commit `5f916a1e` 收敛全局主题、顶部 chrome、Play 状态、Layout 入口和底部状态栏。`EDITOR-003` 继续保持 `[~]`：Scene 工具、默认 Inspector 激活、tab 关闭语义、Project Picker、Inspector/Project/Console 细节、DPI/resize、Windows Graphics Capture 白屏以及完整 Build And Run 路线仍需后续循环，不能用本报告提前关闭整项。

## 同机 Unity 参照路线

参照环境为 `D:\Project\BallWorld`，Unity 窗口标题为 `BallWorld - Untitled - Windows, Mac, Linux - Unity 6.5 (6000.5.3f1) <DX12>`。通过真实 Windows 窗口输入完成：

1. 检查默认菜单、左侧 Scene 工具、中央 Play/Pause/Step、右侧 Layout、Scene/Hierarchy/Project/Inspector/Console 和底部状态区。
2. 在 Hierarchy 点击 `Main Camera`，确认蓝色行选择持续驱动 Inspector。
3. 打开 Inspector，确认 GameObject header、Tag/Layer/Static、Transform、Camera、Audio Listener、URP Camera Data 和 `Add Component` 的层级与字段密度。
4. 点击 Play，确认 Play 按钮蓝色活动态、Scene 切换为 Game View 工具语境且 Hierarchy/Inspector/Project 保持可用；随后再次点击 Play 干净返回 Edit。

Unity 窗口可由 Windows Graphics Capture 正常捕获。PixelEngine 的 framebuffer 内容正常，但相同捕获路径仍返回白色 client；该差异明确保留在后续窗口系统循环中。

## 本轮实现

- 把 `Unity6Dark` 从 4–6px 圆角、蓝黑高对比应用风格改为 0–2px 扁平中性灰主题，收紧 padding/spacing 和 docking separator，并统一 Unity 蓝 selection/accent。
- 移除顶栏常驻 `New Project` / `Open Project` / `Save Scene` / `Build` 宽按钮；这些真实行为继续保留在 File 菜单和快捷键，不损失功能。
- Play/Pause/Step 组不再受左侧按钮宽度影响，按 viewport 几何中心定位；Play/Paused 活动态从橙色改为 Unity 蓝，disabled/hover/active 仍有明确反馈。
- 顶栏右侧新增 `Layout`，当前只展示已真实接线的 `Reset Layout`，没有无效布局 preset 占位。
- 新增底部 viewport sidebar 状态栏，独立着色 Edit/Play/Pause/dirty 模式，并显示工程、场景和 GameObject 数量；状态不再挤在 Play 组三键后方。
- 新增 Unity 同序的 `Component` 顶级菜单，列表来自当前真实 Behaviour registry，选中 GameObject 后复用 `AddComponentToSelected`；无可用组件时只显示明确禁用诊断。
- 产品目标、plan/19 详细设计和 canonical `EDITOR-003` 完成口径已同步，明确“已支持表面完全对标、产品范围外不造 stub”的边界。

## 自动化验证

本机：Windows build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64。

| 命令 | 结果 |
|---|---|
| `dotnet test tests/PixelEngine.Editor.Tests/PixelEngine.Editor.Tests.csproj -c Release --no-restore -m:1` | 105/105 passed |
| `dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release --no-restore -m:1` | 110 passed；10 个显式 GL/ANGLE 环境 smoke skipped |
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-restore --disable-build-servers -m:1 --filter "FullyQualifiedName!~PerformanceHardeningToolingDisciplineTests"` | 538 passed；4 个环境门控 smoke skipped |
| `dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1` | 32 projects；0 warning；0 error |
| `pwsh -NoProfile -File tools/validate-task-catalog.ps1` | 80 canonical；49 done；5 open；1 active；25 blocked；valid |
| `git diff --cached --check` | passed；只暂存本轮 8 个实现/设计/测试文件 |

一次不带过滤器的 Hosting 尝试在外部发布工具测试组中无输出等待 6 分钟，因此主动终止且不计为通过；清理残留 `dotnet/testhost` 后，上表 canonical 非发行工具过滤器从干净进程状态完整通过。

## 1280×720 framebuffer 复核

可再生命令：

```powershell
apps/PixelEngine.Editor.Shell/bin/Release/net10.0/PixelEngine.Editor.Shell.exe `
  --project demo/PixelEngine.Demo `
  --window-ticks 24 `
  --capture-frame "$env:TEMP\pixelengine-editor003-chrome-v1.bmp" `
  --ephemeral-user-state `
  --no-reopen-last-project
```

输出 BMP SHA256：`CC4D4AE8BCBF00D005F174428020F28FBCD30D99A4A0F8CAFD80AA107E8454A4`。人工逐像素观感复核确认：Play 组保持顶栏中心；Layout 位于右侧；底栏完整可读；Scene、Hierarchy、Project、Inspector/Console dock 没有被 sidebar 覆盖；1280×720 无顶栏溢出或状态文本截断。BMP 为可再生临时 artifact，不作为唯一稳定证据。

## 下一轮差异

- Scene toolbar 仍使用 `W/E/R` 文本按钮，缺少 Unity 式图标、active tool、grid/snap 与 local/global 反馈。
- 默认右侧 dock 当前优先显示 Console，而 Unity 默认工作台优先 Inspector；ImGui tab 的常显大 `X` 与 Unity tab 密度不同。
- Hierarchy/Inspector 的 GameObject header、Transform 三列字段、组件 foldout 与 Add Component 视觉层级仍需真实点击收敛。
- Project/Console 行密度、图标、选择/过滤和 Project Picker 信息架构仍明显不同。
- Windows Graphics Capture 捕获 PixelEngine client 为白色，必须定位 present/composition 根因并修复，不能用内部 framebuffer 正常替代。
- 150%/200% DPI、resize、dock/undock、键盘焦点、IME、drag/drop、Play/Pause/Step 和 Build And Run 仍需完整真实窗口路线。
