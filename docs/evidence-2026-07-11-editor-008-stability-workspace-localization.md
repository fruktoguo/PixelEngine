# EDITOR-008 稳定性、运行态、工作区与本地化证据

Evidence Index: `editor-008-stability-workspace-localization-20260711`

## 修复结论

- UI Scale 不再在活动 ImGui frame 内修改 style。缩放统一在 `NewFrame` 前按相对比例应用，字体使用启动 atlas scale 与 `FontScaleMain` 的稳定比值，避免累计放大和 native 闪退。
- ImGui 在布局变更后最多每 2 秒持久化一次 ini；正常退出仍执行最终保存。布局版本 sidecar 负责迁移旧工作区，跨分辨率不兼容布局会回到安全默认值。
- Engine 增加持久 `Paused` 模式。Play 中点击 Step 会先暂停、执行恰好一个 forced sim tick、保留临时 Play 快照并回到 Paused；Resume 和 Stop 分别继续运行与恢复编辑快照。
- Play 自动聚焦 Game View。输入按 Scene/Game viewport focus 与 Play/Paused 状态路由；Hierarchy 在编辑态显示 procedural 出生点/目标点，在 Play/Paused 显示脚本实体与刚体，并以首个 Behaviour 类型命名实体。
- 默认 dock 工作区按用户参考图重排为左侧 Scene/Game 主区、中右 Hierarchy + Project、最右 Inspector + Console。
- Windows 原生标题栏使用 DWM 深色 caption/text/border，保留系统拖拽、缩放与 Snap Layout；ImGui 工作区更新为 `PixelEngine Modern Dark` 主题。
- Editor Shell 加入 formatVersion=1 的外置 JSON 语言包，内置 `en-US`、`zh-CN`，用户目录同 locale 可覆盖内置包；Preferences 可即时切换并保存语言。

## 缺陷复现与修复证据

- 原 Step 崩溃日志：`StepOnce 只能从编辑模式触发`，调用链来自 Play 状态仍启用的工具栏 Step。
- 修复后真实窗口 scripted probe：24 ticks，`scripted_play_entered=True`、`scripted_play_paused=True`、`scripted_play_stepped=True`、`scripted_play_resumed=True`、`scripted_play_exited=True`，进程 exit code 0。
- 新增 marker 首轮验证发现窄 dock 首帧的负宽度 clamp，日志精确定位 `SceneViewPanel.DrawMarkers`；加入合法上下界后同一 18/24 tick 真实窗口路线均 exit code 0。
- 140% UI Scale 真实窗口长跑：240 ticks，包含 Play/Pause/Step/Resume/Stop、保存、关闭和重新打开工程，exit code 0；`editor-shell-imgui.ini` 在运行期间已生成，非仅退出保存。
- 截图：`artifacts/editor-008-play.bmp`（编辑态默认布局与生成点位）、`artifacts/editor-008-runtime.bmp`（Game View、玩家 HUD 与运行世界）。

## 自动化验证

- `dotnet build PixelEngine.sln -c Release --no-restore`：0 warning / 0 error。
- `PixelEngine.Editor.Tests`：97/97 passed（新增语言包覆盖、运行时层级名称覆盖）。
- `PixelEngine.UI.Tests`：106 passed / 9 native GL 条件 skipped。
- Hosting 定向回归：108/108 passed，覆盖 EngineExecutionMode、Game View contract、layout migration、preferences、scene authoring 与 hosting discipline。
- `tools/validate-task-catalog.ps1`：Task catalog valid。

全量 Hosting 回归曾启动，但既有窗口测试进程超过 8 分钟未退出且未给出失败；本证据不将该次运行计为通过，EDITOR-008 仅采用上述 108 项定向回归作为验收证据。
