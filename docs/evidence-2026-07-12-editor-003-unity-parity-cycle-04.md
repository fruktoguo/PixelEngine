# 2026-07-12 EDITOR-003 Unity 6.5 对标循环 04：Project / Console

taskIds: `EDITOR-003`
implementationCommit: `9e06b434b94f8f43755b1e58e750132bc79697a1`
baseImplementationCommit: `56922aa3cd5d8cdda35eb769e456914f9f91de55`
runSessionId: `local-20260712-editor003-unity-parity-cycle04`
evidenceState: `unity_parity_cycle_complete_task_still_active`

## 结论

本轮以同机 Unity 6.5 `6000.5.3f1` 最大化 Project / Console 窗口为直接参照，完成 PixelEngine Project 与 Console 默认 chrome、信息密度和宽窄响应式交互的收敛。Project 的高频入口、双栏导航、单行资产列表与底栏层级已接近 Unity；Console 在窄停靠时保持一行，在宽面板时展开 `Collapse / Clear on Play / Error Pause`，日志列表与详情区稳定分隔。所有被移入 popup/context menu 的创建、导入、刷新、排序、rename/delete、筛选及日志行为仍真实可达。`EDITOR-003` 继续保持 `[~]`：Project Picker、Windows Graphics Capture 白屏、连续 DPI/resize/dock/undock、IME/drag-drop，以及完整 author→play→edit→build→run 真实输入路线仍需后续循环。

## 同机 Unity 参照

- Unity Project 最大化态常驻 `+`、搜索/过滤、左侧 Favorites/Assets/Packages、右侧 breadcrumb 与内容区、底部缩放；refresh/import/rename/delete 不长期占据顶栏。
- Unity Console 最大化态使用单行 `Clear / Collapse / Clear on Play / Error Pause`、搜索和 Log/Warning/Error 计数，主体为日志列表并在下方显示选中详情。
- PixelEngine 只映射已有产品语义：Project 根为 `Content` / `ScriptSource`，不伪造 Unity 的 Packages/Favorites；Console 保留已有自动滚动和错误暂停行为，并根据可用宽度折叠低频选项。

## 本轮实现

- Project 默认改为紧凑 list，`+` popup 汇总 Create 与 Import；搜索、类型过滤、grid/list、options 保持单行，Refresh 与 Sort 进入 options。
- Project 左侧树与右侧 breadcrumb/content 使用更接近 Unity 的列宽和命名；资产/文件夹列表保持单行，路径、用途、摘要、badge 与计数迁入 tooltip 或底栏。
- Project 主体显式预留 footer 高度，窄停靠下 `items` 计数与 grid thumbnail slider 不再被内部 child 裁切；拖放、双击、右键和 selection hover 顺序保持正确。
- Console 窄停靠使用 `Clear / C / … / Search / severity counts` 单行布局；宽度达到阈值后直接展开 `Collapse / Clear on Play / Error Pause`，不需要固定拆成多层工具栏。
- Console 日志行去除重复时间前缀，主体按 68%/32% 划分日志与详情；空态位于列表内部，选中详情始终拥有稳定区域；severity button 同时表达过滤状态与计数。

## 自动化验证

本机：Windows build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64。

| 命令 | 结果 |
|---|---|
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release -m:1 --no-restore -v:minimal --blame-hang-timeout 3m --blame-hang-dump-type none` | 668 passed；4 个显式环境 smoke skipped；0 failed；Blame 确认全部测试完成 |
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release -m:1 --no-restore -v:minimal --filter "FullyQualifiedName~AssetBrowser\|FullyQualifiedName~EditorConsole"` | 23/23 passed |
| `dotnet test tests/PixelEngine.Editor.Tests/PixelEngine.Editor.Tests.csproj -c Release -m:1 --no-restore -v:minimal` | 105/105 passed |
| `dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release -m:1 --no-restore -v:minimal` | 110 passed；10 个显式 GL/ANGLE 环境 smoke skipped |
| `dotnet build PixelEngine.sln -c Release -m:1 --no-restore -v:minimal` | 0 warning；0 error |
| `pwsh -NoProfile -File tools/validate-task-catalog.ps1` | 80 canonical；49 done；5 open；1 active；25 blocked；valid |
| `git diff --cached --check` | 实现提交 passed；只含本轮 4 个实现/测试/设计文件 |

## 真实窗口输入与 framebuffer 复核

同机 Unity 参照窗口：`BallWorld - Untitled - Windows, Mac, Linux - Unity 6.5 (6000.5.3f1) <DX12>`。真实激活并最大化 Project 与 Console，复核其默认工具栏、双栏/详情结构及底栏信息层级。

PixelEngine Project 默认帧：

- `$env:TEMP\pixelengine-editor003-project-v4d.bmp`
- 1280×720，3,686,454 bytes
- SHA256 `718D984D61AA7E83108CE756701CCC7C5B414678D3B18184F76040F711E9361E`
- 复核：默认 295px 高窄停靠中，`+ / Search / All / grid / list / …` 全部单行可见；Project/Content/ScriptSource 双栏无重叠；右侧 scrollbar 与底部 `2 items` 同时完整显示。

PixelEngine Console 窄停靠帧：

- `$env:TEMP\pixelengine-editor003-console-compact-v4e.bmp`
- 1280×720，3,686,454 bytes
- SHA256 `84CB31258F9539DA8FE7369FAB817403239040B7250BA2DE839C40AFC6C502C5`
- 真实窗口点击 Console tab 后正常 `Alt+F4` 写出；`Clear / C / … / Search / 6 / 0 / 0` 在约 319px 宽度内无裁切，日志列表与空详情区边界稳定。

PixelEngine Console 最大化响应式帧：

- `$env:TEMP\pixelengine-editor003-console-wide-v4d.bmp`
- 3840×2054，31,549,494 bytes
- SHA256 `8A455B9B62F66C08DE4509A9AF428676CBC8CC92A8DCC6FDE3D3C36ACB2EAB22`
- 通过真实 `Win+Up` 最大化后点击 Console tab；工具栏自动展开 `Clear / Collapse / Clear on Play / Error Pause / … / Search / severity counts`，仍为单行，列表与详情区没有覆盖。

Windows Graphics Capture 对 PixelEngine client 仍返回白色主面与 overlay 组合，内部 framebuffer 正常；本轮继续显式保留该差异，未用内部 capture-frame 冒充 WGC 已修复。

## 下一轮差异

- Project Picker 的首次启动信息架构、Recent 卡片、Create/Open 路径、validation 与错误恢复仍需对标 Unity Hub/Project Browser 语义。
- Windows Graphics Capture 白屏仍需从 swapchain/composition/window capture 路径定位并修复。
- 150%/200% DPI、跨屏连续 resize、dock/undock、键盘焦点、IME 与外部 drag/drop 尚未形成真实输入闭环。
- Play/Pause/Step、Undo/Redo、Scene/Game 切换、外部脚本、Prefab、Settings、Build And Run 与失败恢复仍需在同一工程完整复走。
