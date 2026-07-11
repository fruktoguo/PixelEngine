# EDITOR-009 Unity-like 运行、场景编辑与工作台交互证据

Evidence Index: `editor-009-unity-interaction-20260711`

## 修复结论

- Game View 发布的 `CurrentViewportTexture` 已包含 world、玩家/玩法 overlay、脚本 `OnGui` 与 Game UI；Editor chrome 仍只绘制到窗口 framebuffer，不再出现“地图在动但玩家和 HUD 不见”的裸世界纹理。
- Game View 把键盘焦点与实际图像 hover 分开管理，并把指针按 letterbox、DPI 与纹理尺寸映射到 runtime viewport。Game View 自身产生的 ImGui capture 不再反向吞掉 WASD、鼠标与 Game UI 输入。
- `lava-mine.scene` 使用带 StableId、ParentId、Transform 与 marker Behaviour 的真实 `Player`、`Goal` 子 GameObject；Hierarchy 与 Scene View 均可选中，Inspector 可编辑，ImGuizmo 连续拖动只提交一条 Undo，并正确执行父级 world-to-local 转换与 `.scene` 保存往返。
- Play Hierarchy 的实体与刚体行可选择；runtime entity Inspector 显示 Transform、组件字段与 public property，并把 Play 期间修改作为可恢复临时编辑。停止运行时即使 Inspector 已关闭，也会恢复并清理临时修改。
- Project Window 直接呈现 Content 与 ScriptSource 的真实目录，支持 breadcrumb、网格/列表、缩略图缩放、稳定矢量类型图标与真实 PNG/JPEG/BMP/TGA 图片缩略图。缩略图使用纯托管 StbImageSharp，具有签名失效、LRU、尺寸上限、损坏图片降级和 GL texture 完整释放。
- Console 提供 Clear、Collapse、Clear on Play、Error Pause、Log/Warning/Error 计数过滤、搜索、选择详情、复制和打开源码；顶部 Play/Pause/Step 改为矢量图标，Play/Paused 时 Play 图标保持活动色并承担 Stop 语义。
- Demo HUD 默认收敛到 640x360 Game View 右侧的紧凑非交互区域，出生区玩家视觉不再被 HUD 遮挡；诊断信息仍可通过 `ShowDiagnostics` 显式展开。

## 真实窗口与 GPU 证据

- `artifacts/editor-009-gameview-final.bmp`：最终代码上的真实 Editor Play 截图，左侧玩家视觉可见，右侧紧凑 HUD、活动 Play 图标、authoring Player/Goal 与 runtime entity 同时存在。
- 保持 Play 的 scripted Game View probe：`completed=True`、`input_registered=True`、`play_entered=True`、`start_x=51.000`、`end_x=53.083`、`player_moved=True`、`visual_commands=6`、`render_overlay_commands=6`、`remained_in_play=True`。
- `artifacts/editor-009-drag-manual.bmp`：真实 Windows 交互选择 Hierarchy/Player 后，Scene View 出现 Move gizmo，Inspector 显示 Transform 与 `PlayerSpawnPoint`。
- `artifacts/editor-009-textures-manual.bmp`：真实 Windows Project/Content/textures 网格显示 19 个磁盘 PNG 缩略图。
- `artifacts/editor-009-project-check/project-grid-wide-final.bmp`：1920x1080 默认工作台、Scene marker、真实双根目录、文件夹图标与 Unity-like Console。
- 真实 OpenGL readback 验证 `CurrentViewportTexture` 的玩家黄色 overlay；另一条 readback 验证 Game UI 写入 runtime surface、Editor UI 只写 window surface，均无 GL error。
- 所有手工交互使用 `artifacts/editor-009-drag-project` 隔离工程，未改动用户的 `project.pixelproj` 与 `lava-mine-copy.scene`。

## 自动化验证

- `PixelEngine.Editor.Tests`：105/105 passed。
- `PixelEngine.Scripting.Tests`：93/93 passed。
- `PixelEngine.UI.Tests`：110 passed / 10 native 条件 skipped。
- `PixelEngine.Demo.Tests`：134 passed / 1 native 条件 skipped。
- `PixelEngine.Hosting.Tests`：592 passed / 4 native 条件 skipped。
- `PixelEngine.Rendering.Tests`：177 passed / 22 native 条件 skipped；本次关键真实 GL runtime/editor surface、玩家 overlay 与脚本 HUD readback 显式启用后 4/4 passed。
- 对旧最终输出的六个公开程序集执行反射 API 对比：public/protected 签名、参数名、默认值与接口均为 0 missing/changed；`ScriptFieldDescriptor` 与 `UiPresentContext` 的字段、尺寸和偏移保持一致。
- `tools/validate-task-catalog.ps1`：Task catalog valid，78 个 canonical task 中 1 个 active（本任务收口前状态）。
- `git diff --check`：通过；仅用户自有 `project.pixelproj` 的既有 CRLF 提示，无 whitespace error。

## 正式输出

- 实现提交 `92933b8451f5c348507ccfa6ca9edc988777757a` 在 detached clean worktree 中完成首轮官方发行验证：`win-x64`、Release、R2R、RmlUi。
- Editor 默认工作台 probe：`completed=True`、`succeeded=True`、22 个必需面板、脚本创建/热重载/挂载成功、Scene 保存、Play 进出与 Build Player 全部成功。
- Demo 窗口 probe：正式 `PixelEngine Demo.exe` 完成 80/80 tick，输出 `window_frame_probe`，RmlUi/Rendering/Input/Physics 均实际接入。
- `tools/verify-final-output.ps1` 独立审计通过，`SHA256SUMS` 覆盖 271 个文件。任务完成提交后按同一流程重新生成正式输出，最终包 `_验证记录/manifest.json` 的 `gitCommit` 必须与最终 HEAD 精确一致，不允许 `AllowCommitMismatch`。
