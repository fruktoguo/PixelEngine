# 2026-07-12 EDITOR-003 Unity 6.5 对标循环 07：外部资产拖入与布局恢复

taskIds: `EDITOR-003`
implementationCommit: `afcaaa8ab9f04d31f9ac7fd78c2c7ea9f5bf863d`
baseImplementationCommit: `26ea94bfae1bc29413be9b5d06c2266536153f8c`
runSessionId: `local-20260712-editor003-unity-parity-cycle07`
evidenceState: `unity_parity_cycle_complete_task_still_active`

## 结论

本轮关闭了“Project 只有 ImGui 内部 payload、没有系统文件拖入”的实现缺口。`RenderWindow` 现在按生命周期转发 Silk `IWindow.FileDrop`；Editor 使用事件发生时的逻辑鼠标坐标和逐轴 framebuffer scale，命中上一完整帧记录的可见 Project window、目录树、breadcrumb 或文件夹 tile。Scene、Hierarchy、Inspector、隐藏 Project tab 与窗口外坐标都会明确拒绝，不会静默把文件导入当前目录。

外部文件与目录会按稳定顺序导入现有全部资产类型：Script 强制进入 `ScriptSource`，UI Screen 强制进入 runtime 可发现的 `Content/ui/screens`，其它资产进入 `Content` 或命中的兼容 Content folder。目录保留层级、逐目录隔离访问失败、跳过 reparse point；目标冲突沿用 Project 的确定性递增命名。UI Screen 创建/导入在资产 manifest 或 UI manifest 任一步失败时会删除目标文件并逐字节恢复两个 manifest，已用同名 screen id 冲突验证没有半写状态。批量成功、部分失败和拒绝原因同时进入 Project footer 与 `project-file-drop` Console category。

布局复核同时发现并修复了一个真实差异：旧的 `Layout > Reset Layout` 重建默认 dock tree 后会让 Console 成为活动 tab；现在 reset 后显式请求 Inspector，WGC 证明默认 Inspector 恢复，并在退出、使用同一隔离 UserData 重启后继续保持。

`EDITOR-003` 继续保持 `[~]`。Computer Use 的跨窗口 drag 安全边界禁止把 Explorer 中的文件拖到另一个目标窗口，同窗口原子 drag 也未产生可供 ImGui tab undock 使用的持续按压；因此本轮只把真实 Windows `WM_DROPFILES → Silk → Project → manifest/磁盘/Console` native smoke 记为平台链路通过，没有把它冒充 Explorer 人工拖拽，也没有宣称 tab undock/redock/splitter 已人工验收。200%/跨屏 DPI、真实中文 IME 与完整 author→play→edit→build→run 仍未清零。

## 本轮实现

- `RenderWindow.FilesDropped` 以 typed event 转发平台回调；Shell connector 在 Editor 销毁前先解除订阅，避免窗口退出后的悬挂 callback。
- Project 每帧只复用预分配的 hit-target list；不可见时立即清空。file-drop 是离散编辑动作，目录枚举、排序、字符串诊断与磁盘 I/O 不进入稳态 draw loop。
- 外部 drop 在处理前加载 Project 双根快照，消除“目录里第一个文件类型决定 Script/UI root”的顺序依赖；目录遍历按 root file、排序后的 child directory 深度优先确定性执行，并允许可访问文件在其它目录失败时继续导入。
- 类型映射覆盖 Material、Texture、Audio、Scene、Prefab、Script、UI Screen、Json 与 Other；数据源再次核对源扩展名、请求类型、目标 root 与整个 `ProjectRoot` 自身复制边界。
- 冲突使用 `name 2.ext`、`name 3.ext` 的既有 Project 规则；UI Screen 同名 id 等 manifest 失败会回滚目标文件、资产 manifest 与 UI manifest，回滚本身失败时保留原异常与回滚异常。
- Console 对完全成功记 Info，对部分成功/拒绝记 Warning；Project footer 使用同一汇总诊断，最多展示前三条细节并保留剩余数量。
- `ResetLayout()` 在重建 Unity-like 默认 dock tree 的同一动作中请求 Inspector focus，避免 Console 因旧 ini tab order 抢占默认右侧面板。

## 自动化验证

本机：Windows build 26100；AMD Ryzen 7 5800X；AMD Radeon RX 7900 XT；.NET SDK 10.0.108；win-x64；当前窗口 150% DPI。

| 命令 | 结果 |
|---|---|
| `$env:PIXELENGINE_RENDERING_GL_SMOKE='1'; dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~NativeWindowDropImportsExternalTextureIntoVisibleProjectPanel"` | 1/1 passed；真实 `RenderWindow` 绘制生产 Project panel 后构造 `HDROP`、发送 `WM_DROPFILES`，验证 Silk event、DPI 命中、磁盘文件与 Console |
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~ProjectBrowserUiImportRollsBackAllFilesWhenScreenIdConflicts"` | 1/1 passed；同名 UI screen id 失败后目标文件与两个 manifest 逐字节回滚 |
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~ProjectBrowserImportsAllFileKindsIntoCorrectRootsAndRejectsSelfDrop"` | 1/1 passed；全类型双根导入与整个 ProjectRoot 自身复制拒绝 |
| `dotnet test tests/PixelEngine.Editor.Tests/PixelEngine.Editor.Tests.csproj -c Release -m:1` | 109/109 passed；含分类、目录层级/双根、冲突、部分失败与 hit target |
| `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release -m:1` | 676 passed；5 个显式 native/环境 smoke skipped；0 failed |
| `dotnet test tests/PixelEngine.Rendering.Tests/PixelEngine.Rendering.Tests.csproj -c Release -m:1` | 182 passed；24 个显式 native/GPU smoke skipped；0 failed |
| `dotnet build PixelEngine.sln -c Release -m:1 --no-restore` | 32 projects；0 warning；0 error |
| `dotnet test PixelEngine.sln -c Release -m:1 --no-build --no-restore` | 1,777 passed；40 个显式 native/GPU/目标环境 smoke skipped；0 failed；exit code 0 |
| `pwsh tools/validate-task-catalog.ps1` | 80 canonical；49 done；5 open；1 active；25 blocked；valid |
| `git diff --cached --check` | passed；实现提交只含本轮 14 个设计、实现与测试文件 |

第一次 Hosting 全套通过后台 `Start-Process` 重定向日志运行时，Git for Windows `sha256sum` 曾在 5 个发行审计用例报告 `failed to set file descriptor text/binary mode: Bad file descriptor`。同组立即独立重跑 5/5 passed；随后直接前台 Hosting 全套 676 passed / 5 skipped，精确暂存状态的 solution 全套也 1,777 passed / 40 skipped。因此未用全局串行化或跳过测试掩盖一次性宿主句柄抖动，最终证据只采用两个 exit code 0 的前台结果。

## 真实窗口与 WGC 复核

所有图像均来自同一 HWND 的 Windows Graphics Capture，1284×767；Codex pet overlay 不会进入按窗口捕获的客户区。

| 状态 | 结果 | 证据 |
|---|---|---|
| 修复前 Reset Layout | 默认 dock tree 重建，但 Console 错误成为活动 tab；该帧只作缺陷证据 | `%TEMP%/pixelengine-editor003-cycle07-layout-reset-wgc.png`；216,513 bytes；SHA256 `F20FE25B6BB93C57C82B0A3F687224E938673D5AA9CD880538EAC4668BAB4654` |
| 修复后 Reset Layout | 右侧恢复 Inspector，Scene、Hierarchy、Project 与默认 splitter 均完整 | `%TEMP%/pixelengine-editor003-cycle07-layout-reset-inspector-wgc.png`；192,738 bytes；SHA256 `93334BFABF550FD029C46D7C31E74B5300791A35E6C4CF0327472EBFFF407E74` |
| 同一 UserData 重启 | 保存的默认 dock/layout 与 Inspector active tab 跨 session 恢复 | `%TEMP%/pixelengine-editor003-cycle07-layout-persisted-wgc.png`；192,358 bytes；SHA256 `32211DBB5D6C1E6A749CB8535B6B253623AD1A5A6F67F8CCE2499047C7B98848` |

通过 Computer Use 真实点击 `Layout > Reset Layout` 后捕获第二帧，再关闭 Editor、使用同一隔离 UserData 重启并捕获第三帧；测试根位于 `%TEMP%/pixelengine-editor003-cycle07-7b1b2f426a7346489140044f60b46ff1`，未修改用户默认布局。Explorer 最后恢复到操作前的 `C:\Program Files (x86)\Windows Kits\10`，Editor 进程正常退出且 stderr 为空。

## 未冒充通过的交互边界

- Computer Use 拒绝跨窗口 drag endpoint，因此没有把 Explorer 文件的真实指针手势记作通过；native smoke 验证的是同一真实窗口的 OS message/platform event 完整链路。
- Computer Use 的同窗口 atomic drag 没有为 ImGui tab 保持持续按压，Project/Inspector undock→move→redock 与 splitter 移动没有稳定复现；Reset Layout 与跨 session persistence 已单独通过。
- 当前只在 150% DPI 当前屏复核坐标换算；200% 与跨不同 DPI 屏幕连续移动尚需目标硬件。
- IME callback/caret form 已在循环 06 实现和自动化，本轮仍没有真实中文输入法 candidate window 截图。

## 下一轮差异

- 在允许跨窗口拖拽的人工 reviewer 环境完成 Explorer→Project 文件/目录 drop，并同时核对 Project footer、Console、manifest 与磁盘。
- 用可保持 pointer-down 的交互环境完成 tab undock、浮动移动、redock、splitter、退出重启与 Reset Layout 整条路线。
- 复走 200%/跨屏 DPI 和真实中文 IME composition/candidate。
- 连续执行 Hierarchy/Inspector/Scene/gizmo/Undo/Redo/Prefab/Settings/外部脚本编辑/Play/Pause/Step/Build And Run 与失败恢复，和 Unity 6.5 逐项清零后才关闭 `EDITOR-003`。
