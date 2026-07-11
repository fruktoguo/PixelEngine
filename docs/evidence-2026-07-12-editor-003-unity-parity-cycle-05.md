# 2026-07-12 EDITOR-003 Unity 6.5 对标循环 05：Project Browser

taskIds: `EDITOR-003`
implementationCommit: `8964b0880453ffa202409d3e805cbab25e52d287`
baseImplementationCommit: `9e06b434b94f8f43755b1e58e750132bc79697a1`
runSessionId: `local-20260712-editor003-unity-parity-cycle05`
evidenceState: `unity_parity_cycle_complete_task_still_active`

## 结论

本轮以同机 Unity Hub 3.19.4 的 Projects / New project 真实窗口为直接参照，将 PixelEngine 无工程启动面从浮动三页签工具窗改为全工作区 Project Browser。默认 Projects 页、最近工程表、搜索、Add/New 主次操作、收藏/移除持久化、缺失状态，以及带返回入口的 New/Open 二级页均已接到真实工程 API；未伪造 Unity Hub 的 Editor version、云仓库、模板商店或项目体积等 PixelEngine 尚不存在的能力。`EDITOR-003` 继续保持 `[~]`：Windows Graphics Capture 白屏、DPI/resize/dock/undock、键盘焦点、IME、外部 drag/drop 与完整 author→play→edit→build→run 路线仍需后续循环。

## 同机 Unity 参照

- Unity Hub Projects 页使用全窗口深色工作区、左侧导航、标题、Search、Add 下拉和高强调 New project；表格展示 favorite、名称、最近修改、Editor version、size/status 与行 options。
- Unity Hub New project 页使用返回入口、模板区和右侧工程设置，Create 位于设置列末端；Editor version、模板下载与云能力均对应 Hub 的真实安装/账号状态。
- PixelEngine 只映射已有产品语义：Recent 使用工程名、绝对路径、最近打开时间、favorite、Ready/Missing 与 options；New 只提供真实 `PixelEngine 2D` 模板、name、location、最终路径和校验；Add 只提供可执行的磁盘打开路径。

## 本轮实现

- 无工程时隐藏 Editor menu/play/status/dock chrome，窗口标题使用 `PixelEngine Hub`，Project Browser 覆盖完整 client；仍保留 Preferences 快捷键与 Settings 入口。
- Projects header 收敛为 `Search / Add / + New project`，Add 保持中性、New/Create/Open 使用蓝色主操作层级；窄窗口自动压缩搜索宽度而不拆成固定多层工具栏。
- Recent 表按名称和路径搜索，显示 favorite、名称/路径、相对最近打开时间、Ready/Missing 与行 options；缺失工程不可误开，但可取消收藏或从列表移除。
- `recent-projects.json` 新增向后兼容的 `favorite` 字段；重新打开工程保留收藏，切换收藏与移除都原子保存并把 I/O 失败写入 Console。
- New project 使用模板/设置双栏、location browse、最终路径预览和安全 validation；拒绝空名称、非法目录名、已有工程与非空目标。Open project 复用 native folder picker 和真实错误恢复。
- 有工程时 Project Browser 作为全区 overlay 并保留 `Back to Editor`，无项目默认从 Recent/Projects 页开始，不再强迫用户进入创建表单。

## 自动化验证

本机：Windows build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64。

| 命令 | 结果 |
|---|---|
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release -m:1 --no-restore -v:minimal --blame-hang-timeout 3m --blame-hang-dump-type none` | 672 passed；4 个显式环境 smoke skipped；0 failed；Blame 确认全部测试完成 |
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release -m:1 --no-restore -v:minimal --filter "FullyQualifiedName~ProjectPicker\|FullyQualifiedName~RecentProjects"` | 15/15 passed |
| `dotnet test tests/PixelEngine.Editor.Tests/PixelEngine.Editor.Tests.csproj -c Release -m:1 --no-restore -v:minimal` | 105/105 passed |
| `dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release -m:1 --no-restore -v:minimal` | 110 passed；10 个显式 GL/ANGLE 环境 smoke skipped |
| `dotnet build PixelEngine.sln -c Release -m:1 --no-restore -v:minimal` | 0 warning；0 error |
| `pwsh tools/validate-task-catalog.ps1` | 80 canonical；49 done；5 open；1 active；25 blocked；valid |
| `git diff --cached --check` | 实现提交 passed；只含本轮 8 个实现/测试/设计文件 |

## 真实窗口输入与 framebuffer 复核

同机 Unity 参照窗口：`Unity Hub 3.19.4` 与 `BallWorld - Untitled - Windows, Mac, Linux - Unity 6.5 (6000.5.3f1) <DX12>`。真实激活 Unity Hub Projects/New project 页面，复核其导航、表格字段、Add/New 主次层级、模板/设置分区和返回路径。

PixelEngine 空 Projects 帧：

- `$env:TEMP\pixelengine-editor003-picker-empty-v5g.bmp`
- 1280×720，3,686,454 bytes
- SHA256 `157CEF03C4A491738A62405A9C2765963FE77501BF7AB4CA294C9577C36D7A81`
- 复核：无 Editor menu/play/status 残留；左侧 Project Browser/Projects/Settings、右侧 Search/Add/蓝色 New 与空态创建/添加入口完整可见。

PixelEngine 真实 favorite Recent 帧：

- `$env:TEMP\pixelengine-editor003-picker-favorite-v5g.bmp`
- 1280×720，3,686,454 bytes
- SHA256 `B92AB964C26F7BA718A52396BD539793407538D557349E99D51B9AA5CCC36D27`
- 使用隔离 user-data 先真实打开 Demo，再从行 options 执行 `Add to favorites`；`recent-projects.json` 实测写入 `"favorite": true`。最终帧同时显示填充星标、工程名/路径、相对时间、Ready 与 options。

PixelEngine New project 实机导航帧：

- `$env:TEMP\pixelengine-editor003-picker-new-v5h.bmp`
- 1280×720，3,686,454 bytes
- SHA256 `4FF2ED617A67877AC18CD70FC7E046476D27239CDD1C37E9B0A9BDCF86DFFC15`
- 真实窗口激活后点击 header `+ New project`，等待页面切换并以 `Alt+F4` 触发退出前 framebuffer capture；返回入口、模板/设置双栏、name/location/final path、Browse 与蓝色 Create 全部可见且无重叠。

Windows Graphics Capture 对 PixelEngine client 仍返回白/黑主面与 Codex pet overlay 组合，内部 framebuffer 正常；本轮继续显式保留该差异，未用 `--capture-frame` 冒充系统录屏兼容已修复。

## 下一轮差异

- Windows Graphics Capture 白屏仍需从 swapchain/composition/window capture 路径定位并修复。
- 150%/200% DPI、跨屏连续 resize、dock/undock、键盘 focus、IME candidate anchor 与外部文件 drag/drop 尚未形成真实输入闭环。
- Play/Pause/Step、Undo/Redo、Scene/Game、Prefab、Settings、外部脚本、Build And Run 与失败恢复仍需在同一工程连续复走并与 Unity 逐项清零。
