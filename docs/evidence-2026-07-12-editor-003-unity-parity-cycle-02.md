# 2026-07-12 EDITOR-003 Unity 6.5 对标循环 02：Scene 工具与 dock 交互

taskIds: `EDITOR-003`
implementationCommit: `dfd838685a06f304e8939abb2411d39a53ead981`
runSessionId: `local-20260712-editor003-unity-parity-cycle02`
evidenceState: `unity_parity_cycle_complete_task_still_active`

## 结论

本轮继续以同机 Unity 6.5 `6000.5.3f1` 默认工作台为参照，收敛 Scene 工具栏、右侧默认焦点、核心 dock 页签和 Window 菜单的行为。1280×720 framebuffer 已确认默认选中 Inspector、Scene 预览 framing 正确、核心六面板不再显示常驻关闭按钮，且没有通过移除关闭按钮牺牲面板显隐能力。`EDITOR-003` 继续保持 `[~]`：Hierarchy/Inspector/Project/Console/Project Picker 的内容层级、Windows Graphics Capture 白屏、DPI/resize 与完整 Build And Run 真实输入路线仍需后续循环。

## 同机 Unity 参照差异

Unity 默认工作台的 Scene 工具具有紧凑图标、蓝色 active tool、Local/Global 空间反馈和可感知的 grid 状态；右侧默认显示 Inspector；主工作台页签不以常驻大 `X` 作为主要关闭入口。循环 01 的 PixelEngine 尚存在 `W/E/R` 文本按钮、Console 默认激活和各 dock node 常驻关闭按钮。本轮逐项修复这些差异，并保留快捷键和 Window 菜单恢复路径。

## 本轮实现

- Scene View 新增 Move/Rotate/Scale、Frame All、Frame Selected、Grid 的紧凑矢量图标；active/hover 使用 Unity 蓝反馈，不依赖外部位图资源。
- `W/E/R` 继续作为真实快捷键，工具按钮、快捷键与 `ImGuizmo` 共用同一 operation 状态；Local/Global 真实切换 gizmo mode，Grid 真实控制 Scene 网格绘制。
- 新建/临时布局在 dock 尺寸稳定后延迟聚焦 Inspector，避免首帧抢焦点导致 Scene 以错误尺寸 framing；已有用户布局不被强制覆盖。
- Unity 主题设置已选中与未选中 tab 的 close-button 阈值；核心 Scene、Game、Hierarchy、Inspector、Project、Console 面板改为无常驻关闭按钮的工作台页签。
- `EditorApp` 新增按标题读取/设置面板可见性的公开 API，并贯通 Shell host/session/app；Window 菜单展示真实勾选状态并可双向隐藏/恢复，因此视觉收敛没有删除功能。
- 新增工具状态、默认焦点、注册顺序、无 close-button、菜单显隐和主题 token 的行为及源码契约测试。

## 自动化验证

本机：Windows build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64。

| 命令 | 结果 |
|---|---|
| `dotnet test tests/PixelEngine.Editor.Tests/PixelEngine.Editor.Tests.csproj -c Release --no-restore -m:1` | 105/105 passed |
| `dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release --no-restore -m:1` | 110 passed；10 个显式 GL/ANGLE 环境 smoke skipped |
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --no-restore --disable-build-servers -m:1 --filter "FullyQualifiedName!~PerformanceHardeningToolingDisciplineTests"` | 539 passed；4 个环境门控 smoke skipped |
| `dotnet build PixelEngine.sln -c Release --no-restore --disable-build-servers -m:1` | 32 projects；0 warning；0 error |
| `pwsh -NoProfile -File tools/validate-task-catalog.ps1` | 80 canonical；49 done；5 open；1 active；25 blocked；valid |
| `git diff --cached --check` | passed；实现提交只含本轮 15 个实现/测试文件 |

## 1280×720 framebuffer 复核

可再生命令：

```powershell
apps/PixelEngine.Editor.Shell/bin/Release/net10.0/PixelEngine.Editor.Shell.exe `
  --project demo/PixelEngine.Demo `
  --window-ticks 24 `
  --capture-frame "$env:TEMP\pixelengine-editor003-workbench-v2g.bmp" `
  --ephemeral-user-state `
  --no-reopen-last-project
```

输出 BMP SHA256：`6DF1A86473E16B6CA9E3C1455B7C6BF63C5AAD5DF15E0F30BCA83DF7B88E0846`，大小 3,686,454 bytes。人工复核确认 Inspector 为默认选中 tab，Scene 预览完整显示，Move 与 Grid active 状态为蓝色，Local 可见，核心六面板无常驻 `X`，Project/Hierarchy/Inspector 边界及底栏均无裁切。BMP 是可再生临时 artifact，不作为唯一稳定证据。

## 下一轮差异

- Hierarchy 仍缺 Unity 式搜索、对象类型图标、visibility/picking 列和更紧凑的 create/row 结构。
- Inspector 仍缺 Unity 式 GameObject header、active checkbox、Tag/Layer/Static、Transform 三列字段、组件 foldout 与 Add Component 层级。
- Project 与 Console 的图标、过滤、列表/详情密度及选择反馈仍明显不同；Project Picker 信息架构仍需收敛。
- Windows Graphics Capture 捕获 PixelEngine client 仍为白色，必须定位真实 present/composition 根因。
- 150%/200% DPI、resize、dock/undock、键盘焦点、IME、drag/drop、Play/Pause/Step 与 Build And Run 仍需完整真实窗口路线。
