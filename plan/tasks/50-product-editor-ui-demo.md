# Editor、UI 与 Demo 产品闭合

本轨道不重复已经由自动化证明的能力。每项状态代表“真实用户工作流是否完整闭合”，因此当前多数任务等待真实设备和人工 reviewer，而不是继续堆 scripted probe。

## Unity-like Editor

- [x] `EDITOR-004` 建立 Editor workspace 恢复、转场数据安全与用户状态隔离。
  - 优先级：P0。
  - 依赖：`BASE-013`。
  - 设计来源：`plan/19-standalone-editor-app.md` §编辑器产品化修复（2026-07-11）。
  - 验收：无显式命令行工程时自动恢复最后一次成功打开的工程；场景优先级为 CLI override > 每工程 last scene > Project StartScene；打开/新建场景不再修改启动场景；New/Open Scene、切换/关闭工程、Exit 均受 Save/Don't Save/Cancel dirty guard 保护；缺失或损坏场景不会伪装为空场景；自动化默认使用隔离 user-data，不能污染真实 Recent/Layout/Workspace；Recent、Workspace、project 与 scene 关键写入采用原子替换且失败不破坏旧文件。
  - 证据：Workspace/Options/Transition/Picker/Project/Scene/Layout/Preferences 合并定向回归 77 passed / 1 native skipped；Hosting 非发行工具回归 389 passed / 4 native skipped；Editor 84/84；Editor Shell Release build 0 warning / 0 error；真实窗口 12 帧工程短跑通过；同一隔离 user-data 的连续两进程验证无参数启动自动恢复工程；显式打开 `empty-window-probe.scene` 后 workspace 恢复该编辑场景，但 `project.pixelproj` 仍保持 `scenes/lava-mine.scene`。

- [x] `EDITOR-005` 修正 Asset Database 的 Content/ScriptSource 根、缓存与增量刷新语义。
  - 优先级：P0。
  - 依赖：`EDITOR-004`。
  - 设计来源：`plan/19-standalone-editor-app.md` §编辑器产品化修复（2026-07-11）。
  - 验收：Project Window 能显示并创建到真正参与编译/热重载的 ScriptSource；Content 与 ScriptSource 均有稳定、不可越界的 logical root；只读查询不扫描磁盘、不解析全库、不写 manifest；外部变更可增量失效并刷新；空工程不会逐帧刷新；manifest 损坏可诊断恢复；选择以 stable asset id 为主并在移动/重命名后跟随。
  - 证据：Asset monitor / dual root / manifest refresh / scene settings 合并定向回归 45/45；Hosting 非发行工具回归 426 passed / 4 native-GL 条件 skipped；Editor 92/92；Editor Shell Release build 0 warning / 0 error；Demo 工程真实窗口 12 帧短跑通过并注册 23 个面板。自动化覆盖双 manifest、真正 ScriptSource 创建与源码打开、重叠物理根去重、查询零写入、watcher 批处理/溢出重扫、manifest 损坏隔离恢复、唯一签名 rename 身份延续、歧义拒绝猜测、外部文件/文件夹 rename 的引用与 Scene settings 同步，以及 stable asset/folder selection 随移动恢复。

- [x] `EDITOR-006` 重做 Project Window 信息架构、资源语义与主操作。
  - 优先级：P0。
  - 依赖：`EDITOR-005`。
  - 设计来源：`plan/19-standalone-editor-app.md` §编辑器产品化修复（2026-07-11）。
  - 验收：双栏 folder tree + breadcrumb + 直接子项导航可用；创建/导入/移动/删除收进明确工具栏或上下文菜单；类型、用途、启动/当前/测试资产 badge 与摘要可理解；Scene 双击直接打开且不改变 StartScene，Script 双击打开真正源码；搜索覆盖路径、类型、用途与摘要；Demo 的 materials/reactions/startup/weapons/audio/UI/font/probe 文件无需猜文件名即可理解用途。
  - 证据：Editor 全量 96/96；Hosting 非发行工具回归 428 passed / 4 native-GL 条件 skipped；Editor Shell Release build 0 warning / 0 error；Demo 工程真实窗口 12 帧通过并注册 23 个面板。自动化覆盖双根树、breadcrumb、直接子文件夹/资产、深层搜索结果、localized type/用途/摘要/动态 badge 搜索、Scene/Script 专用主操作、Content/ScriptSource 兼容创建与导入路由，以及 Demo materials/reactions/startup/weapons/audio Cue 与 clip/UI manifest 与 screen/font/probe/script 的语义 descriptor；Scene 打开复用统一 dirty guard，当前场景 badge 从 Session 内存实时计算，Project StartScene 只读且不被 Project Window 改写。

- [x] `EDITOR-007` 建立 Scene View 独立 authoring 可视化并让实例工程真实可见。
  - 优先级：P0。
  - 依赖：`EDITOR-005`、`EDITOR-006`。
  - 设计来源：`plan/19-standalone-editor-app.md` §编辑器产品化修复（2026-07-11）；架构 §17.4。
  - 验收：Scene View 与 Game View 不再共用运行时 camera/语义；Edit 模式显示声明式初始 world 或 procedural preview，并叠加网格、场景边界、对象 marker/name、Frame All/Frame Selected；Editor 与 Player 使用同一份项目脚本/世界来源，不保留同全名空壳 Behaviour；Demo 默认打开 lava-mine 时 Scene View 非空，empty/probe 场景有清晰测试标识且不会污染下次启动。
  - 证据：稳定报告 `docs/evidence-2026-07-11-editor-007-scene-authoring.md`（Evidence Index: `editor-007-scene-authoring-20260711`）；Scene authoring / Game View / shell discipline / scene materialization / project 合并定向回归 101 passed / 1 native 条件 skipped；Hosting 非 tooling 回归 434 passed / 4 native-GL 条件 skipped；Scripting 90/90、Editor 96/96、Demo 132 passed / 1 native-GL 条件 skipped；Editor Shell Release build 0 warning / 0 error。`SceneAuthoringPreviewTests` 覆盖 lava-mine 640×360 LevelDirector preview、对象/出生点/终点 marker、显式空 probe 标识、独立相机、Frame All/Selected、dock 1×1 首帧延迟 framing 与 Demo 完整脚本目录动态编译；真实窗口 12 帧截图显示场景边界、网格、洞穴/平台/熔岩示意及分离 marker，运行摘要为 `project_open=True`、`editor_panels=23`。Demo 完整 Behaviour 已迁入唯一 `scripts/` 源，Player 静态编译与 Editor Roslyn 热编译共用源码；空壳同名 Behaviour 已删除，脚本编译器对齐 SDK implicit usings/nullable，武器配置改为 Hosting 路径门控下的 AOT-safe 显式解析。

- [x] `EDITOR-008` 修复真实使用暴露的 Editor 稳定性、运行态可见性、本地化、工作区与视觉系统缺陷。
  - 优先级：P0。
  - 依赖：`EDITOR-004`–`EDITOR-007`。
  - 设计来源：2026-07-11 用户真实窗口反馈；`plan/19-standalone-editor-app.md`；架构 §17.4。
  - 验收：UI Scale 75%–200% 连续调整与长跑不崩溃且布局不发生指数缩放；Play/Pause/Step 状态机不抛异常并符合单步语义；Game View 在 Play 中显示玩家、HUD 与运行内容且接收正确 viewport 输入；Hierarchy/Scene View 能区分并显示 authoring 与 runtime/procedural 对象、出生点、目标点和关键 marker；布局周期保存、崩溃恢复、跨 DPI/分辨率归一化恢复，并以用户截图的 Scene 主区 + 右侧 Hierarchy/Inspector + 右下 Project 为默认工作区；Editor Shell 的原生标题栏跟随引擎深色配色且保留系统拖拽、缩放与 Snap Layout，工作区使用现代化 Unity-like 主题；Editor Shell 核心菜单、工具栏、Preferences、Hierarchy 与 Scene/Game 状态文案通过可外置语言包解析，内置简体中文与 English，并在 Preferences 中即时切换和持久化。
  - 证据要求：缩放/状态机/布局/语言包/运行态对象自动化；Demo 工程真实窗口 Play+Step+输入长跑与截图；Editor 全量、Hosting 定向、Release build 0 warning/0 error；重新生成并审计 `最终输出/`。
  - 证据：`docs/evidence-2026-07-11-editor-008-stability-workspace-localization.md`（Evidence Index: `editor-008-stability-workspace-localization-20260711`）；真实窗口 140% UI Scale 240 tick 长跑及 Play→Pause→Step→Resume→Stop 全链路 exit 0；Editor 97/97、UI 106 passed / 9 native GL 条件 skipped、Hosting 定向 108/108；Solution Release build 0 warning / 0 error；官方 `最终输出/` 已重新生成并通过独立审计。

- [x] `EDITOR-009` 闭合 Unity-like 运行、场景编辑、Project、Console 与播放工具栏交互。
  - 优先级：P0。
  - 依赖：`EDITOR-008`。
  - 设计来源：2026-07-11 用户第二轮真实窗口反馈与截图；`plan/12-editor-tooling-ui.md`；`plan/19-standalone-editor-app.md`；架构 §17.4。
  - 验收：Demo 在嵌入 Game View 中可稳定看到玩家视觉并以键鼠实际操控，输入焦点、viewport 映射和 UI capture 与独立 Player 一致；Hierarchy 的 authoring、procedural marker 与 Play runtime 实体均可选择并联动 Inspector/Scene/Game View，出生点与目标点可在 Hierarchy 或 Scene View 选中并用 Unity-like Move gizmo 拖拽，修改进入 Undo/Redo、dirty guard 与 `.scene` 序列化；Project Window 直接反映 Content/ScriptSource 真实目录，提供 Unity-like 文件夹树、breadcrumb、网格/列表切换、文件类型图标、图片缩略图、资产类型/状态与主操作；Console 提供 Unity-like Clear、Collapse、Clear on Play、Error Pause、日志/警告/错误计数过滤、可选行与完整详情；顶部使用 Unity-like Play/Pause/Step 图标与明确活动色，Play 中 Play 按钮承担 Stop 语义且状态实时正确；默认布局、间距、选中态、图标与字体保持统一现代深色视觉。
  - 证据要求：玩家可见/移动与 viewport 输入自动化及真实窗口截图；marker 选择/拖拽/Undo/保存往返；runtime selection/Inspector；真实目录、类型图标与纹理缩略图；Console 状态机；工具栏状态；Editor/UI/Hosting/Demo 定向回归、Solution Release build 0 warning / 0 error、官方 `最终输出/` 重生成与独立审计。
  - 证据：`docs/evidence-2026-07-11-editor-009-unity-interaction.md`（Evidence Index: `editor-009-unity-interaction-20260711`）；最终代码真实 Game View 探针确认玩家从 X=51.000 移至 X=53.083、玩家与 viewport overlay 各 6 条绘制命令且保持 Play；Editor 105/105、Scripting 93/93、Hosting 592 passed / 4 native 条件 skipped、UI 110 passed / 10 native 条件 skipped、Rendering 177 passed / 22 native 条件 skipped、Demo 134 passed / 1 native 条件 skipped，关键 GL 像素回读显式启用后 4/4；Solution Release build 0 warning / 0 error。官方 clean-worktree `win-x64/r2r/RmlUi` 输出已通过默认工作台构建探针、Demo 80 tick 真实窗口探针、271 项 SHA256 与独立审计。

- [x] `EDITOR-010` 建立 VS Code 默认脚本编辑与一键打开 C# 工程工作流。
  - 优先级：P0。
  - 依赖：`EDITOR-009`。
  - 设计来源：2026-07-11 用户补充反馈；`plan/11-scripting-system.md`；`plan/19-standalone-editor-app.md` §脚本外部编辑器；架构 §17.4。
  - 验收：Editor Preferences v1 的空外部编辑器安全迁移到 VS Code，v2 以明确 sentinel 区分 VS Code、Visual Studio、Rider、System Default 与自定义命令；Windows 上可靠探测 VS Code 的 PATH、User/System Installer 与注册表安装，脚本双击和 Console source location 默认复用工程窗口并定位到准确行列；`Assets > Open C# Project` 可直接打开当前工程，VS Code 打开工程根或根级 `.code-workspace` 而非孤立 solution，Visual Studio/Rider 优先复用真正包含当前 `.csproj` 的工程根/祖先 `.sln`；standalone 新工程没有项目文件时生成稳定、可解析 ScriptSource 与引擎引用、内容不变不重写的 `.csproj/.sln`。保留 system-default、自定义 executable command、`{file}` 及无 placeholder 自动追加的兼容行为；所有失败进入可见诊断与 Console。自动化覆盖迁移、IDE 探测、脚本定位、Demo 祖先 solution、standalone 工程生成/幂等、菜单与本地化；Hosting/Scripting 定向回归和 Solution Release build 0 warning / 0 error。
  - 证据：`docs/evidence-2026-07-11-editor-010-vscode-project-workflow.md`（Evidence Index: `editor-010-vscode-project-workflow-20260711`）；真实机 VS Code/Rider/Visual Studio 三 IDE 均解析到真实 executable；standalone SDK 工程真实 build 0 warning / 0 error；focused 77/77、FinalOutput 11/11、Hosting 624 passed / 4 native 条件 skipped；Solution Release build 0 warning / 0 error；官方 clean-worktree `win-x64/r2r/RmlUi` 输出通过工作台、Demo 80 tick、319 项 SHA256 与独立审计。

- [!] `EDITOR-001` 完成 Project Window 真实工作流验收。阻塞：需要真实窗口 reviewer 和当前 HEAD 录屏/报告。
  - 优先级：P1。
  - 依赖：`EDITOR-004`、`EDITOR-005`、`EDITOR-006`、`EDITOR-007`。
  - 设计来源：`plan/19-standalone-editor-app.md`。
  - 验收：文件夹浏览、创建、导入、搜索/过滤、选择→Inspector、drag/drop、move/rename、引用重写、删除确认、只读预览和错误恢复全部使用真实鼠标键盘走通。

- [!] `EDITOR-002` 完成 Game View 的 viewport、DPI、输入、透明 UI capture/pass-through 和 IME 坐标真实窗口验收。阻塞：需要真实 DPI/输入法环境和 reviewer。
  - 优先级：P1。
  - 验收：resize/DPI 切换无一帧旧坐标；面板空白区阻断；图像透明区透传；交互区捕获；IME caret/candidate 锚点位于实际控件。

- [!] `EDITOR-003` 完成默认工作台 author→play→edit→build→run 产品验收。阻塞：全局 Preferences 与 150% 高 DPI 自动化切片已完成，整项仍需要真实窗口完整路线和 reviewer。
  - 优先级：P1。
  - 验收：Hierarchy/Inspector/Scene View/gizmo/Undo/Redo/Console/Prefab/Settings/外部脚本编辑/Build And Run 可理解、可恢复；720p 和高 DPI 无标签截断或工具区溢出。
  - 当前切片验收：`Edit > Preferences...` 在无工程和已打开工程时均可用；设置按类别导航而非平铺；UI Scale 支持 75%–200% 并持久化，150% 同时缩放字体、ImGui 样式和 Shell 固定尺寸；外部脚本编辑器与布局保存从 Project Settings 迁到用户级 Preferences，旧工程字段仅作兼容迁移；自动化覆盖读写、归一化、菜单接线与高 DPI 布局边界。
  - 已完成证据：实现 commit `8d8598fc`；Editor 84/84、UI 106/106、Hosting 非发行工具回归 344/344、solution Release build 0 warning/0 error；150% Preferences 与默认核心工作台真实窗口 framebuffer probe 见 `docs/evidence-2026-07-10-editor-preferences-ui-scale.md`。该材料不替代人工点击和完整路线。

## Web-first 透明 UI

- [!] `UI-001` 完成 RmlUi/ManagedFallback 透明 UI 产品面验收。阻塞：需要真实窗口视频和 reviewer。
  - 优先级：P1。
  - 验收：world→game UI→editor overlay 顺序正确；透明区 alpha 正确；capture/pass-through 正确；HUD/menu/settings/pause/result 实际点击闭环。

- [!] `UI-002` 完成 Windows IMM32 composition、preedit、selection、candidate window 和 focus 清理产品验收。阻塞：需要真实中文输入法和 reviewer。
  - 优先级：P1。
  - 验收：输入、确认、取消、切焦点、resize/DPI、Game View 嵌入场景均无丢字、错位或残留；ManagedFallback/RmlUi 行为一致。

- [!] `UI-003` 完成 RmlUi desktop GL 与 ANGLE/GLES3 产品级透明合成、状态恢复和发行 smoke。阻塞：需要 ANGLE/GLES 真实窗口和稳定 native 产物。
  - 优先级：P2。
  - 验收：不是 create-renderer/load-only；真实 UI 文档能交互、合成、unload，GL 状态恢复且 fallback gate 正确。

## Showcase Demo Game

- [!] `DEMO-001` 完成角色控制、沙堆/RigidOwned 承载、刚体推动和武器输入手感验收。阻塞：需要真实键鼠/手柄体验 reviewer。
  - 优先级：P1。
  - 验收：跑跳/墙跳/coyote/buffer 可控；不穿不陷；刚体交互可预测；输入延迟和相机不造成眩晕或误操作。

- [!] `DEMO-002` 完成材质反应、地形破坏、刚体碎裂、粒子、fog、光照和 bloom 的完整路线视觉验收。阻塞：依赖 `PERF-002`/`PERF-003` 后的稳定帧率和 reviewer。
  - 优先级：P1。
  - 验收：展示点在实际路线中可见且可复现；无悬空静态岛、残影、调试污染、粒子泄漏或不可读反馈。

- [!] `DEMO-003` 完成材质音效、ambient、定位、限频和爆音/泄漏听感验收。阻塞：需要真实音频设备和 reviewer。
  - 优先级：P1。
  - 验收：impact/fire/splash/explosion/shatter/sizzle/corrosion 可辨识；空间定位合理；高密度事件不爆音；长跑无 source/buffer 泄漏。

- [!] `DEMO-004` 完成独立玩家包的主菜单→设置→开始→完整右出口路线→胜负→重开/退出闭环。阻塞：依赖 `REL-001` 当前 HEAD 玩家包和 reviewer。
  - 优先级：P1。
  - 验收：不启动 Editor；七个 UI screen 全部可用；正式 `lava-mine.scene` 可完成/失败；重开恢复任务、武器、刚体和 UI 基线。

- [!] `DEMO-005` 完成开发态真实窗口脚本热重载验收。阻塞：需要真实编辑器、外部代码编辑器和 reviewer。
  - 优先级：P2。
  - 验收：Play 中修改 Behaviour 源码，Roslyn+ALC 重载成功；场景、世界和公开字段状态按契约保留；编译错误可见且修复后恢复；旧 ALC 可回收。

## 对外文档

- [!] `DOC-001` 新增根 README、Getting Started 和一个从新建工程到 build-player 的最小完整教程。阻塞：文档骨架、玩家脚本链修复及本机干净工程 R2R 复验已完成；待 `EDITOR-003` 独立人工 reviewer 与 `REL-001` 冻结 candidate 后，按最终界面/命令从干净目录人工复走 Project Picker → Script → Play → Build And Run。
  - 优先级：P1。
  - 依赖：`EDITOR-003`、`REL-001` 的命令和界面稳定后收口；可先写骨架。
  - 验收：全新用户能安装前置、启动 Editor、创建脚本/场景、Play、构建并运行玩家包；命令在干净目录复验。

- [x] `DOC-002` 清理旧计划中的过时项目数、旧 Demo 路线、失效 artifacts/scratch 链接和“CI 已完成”等错误声明。
  - 优先级：P2。
  - 依赖：`PLAN-001`。
  - 验收：旧设计仍可追溯，但不再显示为 live 状态；历史结果标 commit/date/evidence level；失效路径改为可重跑命令或稳定报告。
  - 证据：`docs/evidence-2026-07-10-doc-002-legacy-plan-audit.md`；21 份旧计划均含冻结快照 / 迁移 commit / evidence-level 标记，1692 条旧 checkbox SHA256 未漂移，32/32 工程清单无差异，当前 Demo 路线合同 3/3 通过。
