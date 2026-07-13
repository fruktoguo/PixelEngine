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

- [!] `EDITOR-003` 完成默认工作台 author→play→edit→build→run 产品验收。2026-07-12 已在 Windows 真实运行 Unity 6.5 `6000.5.3f1`、Unity Hub 3.19.4 与当前 Editor，建立默认布局、Hierarchy→Inspector、Play Mode、Project/Console、Project Browser 和窗口捕获的差异基线，并完成全局 chrome、Scene 工具/dock、Hierarchy/Inspector、Project/Console、Project Browser、Windows 捕获/平台输入、外部 file-drop/布局恢复、Scene/IME/Build/有序拖拽八轮收敛；Hierarchy 的 Scene Visibility/Picking 已与 GameObject active 正确分离，Project/Console 的宽窄信息层级、Project Browser 的 Recent/New/Open/收藏路径，同 HWND desktop GL→D3D11/DXGI WGC 呈现，以及真实窗口 `WM_DROPFILES → Silk → Project → manifest/磁盘/Console` 平台链路均已验证。当前进程已确认 per-monitor DPI aware、150% DPI、resize 重建、真实中文 IME candidate/focus 清理、Project/Inspector undock→move→redock、Reset Layout 与跨 session persistence，以及完整 Play/Pause/Step/Build And Run→独立 Player 路线。阻塞：当前机器上可复现的本地代码与布局问题已清零；解除条件为不同物理 DPI/200% 显示器跨屏复走、Explorer→Editor 人工 pointer drag，以及独立 reviewer 的最终 Unity 差异矩阵复走，现有自动化不得冒充这些外部证据。
  - 优先级：P1。
  - 验收：Hierarchy/Inspector/Scene View/gizmo/Undo/Redo/Console/Prefab/Settings/外部脚本编辑/Build And Run 可理解、可恢复；720p 和高 DPI 无标签截断或工具区溢出。对 PixelEngine 已支持的编辑器表面，以 Windows Unity 6.5 `6000.5.3f1` 默认工作台为交互与视觉参照：菜单/顶栏/底部状态栏、Play/Pause/Step 状态、面板停靠与切换、选择高亮、Hierarchy→Inspector 联动、Scene/Game 模式切换、Project/Console 密度、Project Picker、resize/DPI/键盘鼠标焦点以及系统录屏捕获均须达到相同心智模型和等价反馈；不得用未接线按钮或不支持的 Unity 子系统占位冒充对标。
  - 完成证据：必须同时包含可重跑自动化、PixelEngine framebuffer 证据、真实窗口输入路线和与同机 Unity 参照图的逐项差异表；仅 source-contract 测试、静态截图或“观感接近”均不能关闭本任务。每轮若仍发现可复现差异，保持 `[~]` 并继续修复。
  - 当前切片验收：`Edit > Preferences...` 在无工程和已打开工程时均可用；设置按类别导航而非平铺；UI Scale 支持 75%–200% 并持久化，150% 同时缩放字体、ImGui 样式和 Shell 固定尺寸；外部脚本编辑器与布局保存从 Project Settings 迁到用户级 Preferences，旧工程字段仅作兼容迁移；自动化覆盖读写、归一化、菜单接线与高 DPI 布局边界。
  - 已完成证据：实现 commit `8d8598fc`；Editor 84/84、UI 106/106、Hosting 非发行工具回归 344/344、solution Release build 0 warning/0 error；150% Preferences 与默认核心工作台真实窗口 framebuffer probe 见 `docs/evidence-2026-07-10-editor-preferences-ui-scale.md`。该材料不替代人工点击和完整路线。
  - Unity 对标循环 01：实现 commit `5f916a1e` 已完成 Unity 6.5 同机参照、扁平紧凑主题、中央 Play 组、右侧 Layout、底部状态栏与真实 Component 菜单；Editor 105/105、UI 110 passed / 10 native 条件 skipped、Hosting 非发行工具回归 538 passed / 4 skipped、solution Release build 0 warning / 0 error，1280×720 framebuffer SHA256 与剩余差异见 `docs/evidence-2026-07-12-editor-003-unity-parity-cycle-01.md`。任务保持 `[~]`。
  - Unity 对标循环 02：实现 commit `dfd83868` 已完成 Scene 图标工具栏、operation/local-global/grid 真实状态、Inspector 默认焦点、核心页签无常驻关闭按钮和 Window 菜单双向显隐；Editor 105/105、UI 110 passed / 10 native 条件 skipped、Hosting 非发行工具回归 539 passed / 4 skipped、solution Release build 0 warning / 0 error，1280×720 framebuffer SHA256 与剩余差异见 `docs/evidence-2026-07-12-editor-003-unity-parity-cycle-02.md`。任务保持 `[~]`。
  - Unity 对标循环 03：实现 commits `36b14c6f`、`3de287d8` 已完成 Hierarchy 搜索/图标、真实 Scene Visibility/Picking 与 Inspector active/name/2D Transform/Behaviour/Add Component，并纠正 visibility 与 runtime active 语义；证据与剩余差异见 `docs/evidence-2026-07-12-editor-003-unity-parity-cycle-03.md`。任务保持 `[~]`。
  - Unity 对标循环 04：实现 commit `9e06b434` 已完成 Project 单行 chrome/双栏导航/紧凑列表与 Console 宽窄响应式工具栏、severity 计数、日志详情分区；真实窗口、framebuffer、自动化与剩余差异见 `docs/evidence-2026-07-12-editor-003-unity-parity-cycle-04.md`。任务保持 `[~]`。
  - Unity 对标循环 05：实现 commit `8964b088` 已完成全工作区 Project Browser、Recent 搜索/收藏/移除/缺失状态与真实 New/Open 页面；Unity Hub 同机参照、真实输入、持久化 JSON、framebuffer 与剩余差异见 `docs/evidence-2026-07-12-editor-003-unity-parity-cycle-05.md`。任务保持 `[~]`。
  - Unity 对标循环 06：实现 commit `83c55fe0`、RmlUi native commit `1b69207f` 已完成同 HWND desktop GL→D3D11/DXGI GPU 呈现、managed ImGui renderer、焦点/系统光标/IME 平台桥与调用方 FBO 恢复；solution 1,770 passed / 39 显式环境 smoke skipped、native GL 10/10、D3D debug presenter 1/1、Release build 0 warning / 0 error，Unity/旧 WGL/ANGLE/新 DXGI WGC 对照、resize SHA256 与剩余差异见 `docs/evidence-2026-07-12-editor-003-unity-parity-cycle-06.md`。任务保持 `[~]`。
  - Unity 对标循环 07：实现 commit `afcaaa8a` 已完成真实窗口系统 file-drop 生命周期、可见 Project/folder DPI 命中、全类型双根与目录递归导入、冲突/部分失败诊断、manifest 事务回滚，以及 Reset Layout 的 Inspector 默认焦点与跨 session persistence；solution 1,777 passed / 40 显式环境 smoke skipped、native Windows drop 1/1、Release build 0 warning / 0 error。WGC SHA256、Computer Use 安全边界与未冒充通过的 Explorer/undock 差异见 `docs/evidence-2026-07-12-editor-003-unity-parity-cycle-07.md`。任务保持 `[~]`。
  - Unity 对标循环 08：闭合 Scene 普通 selection 与显式 Brush 的输入仲裁、全局 Undo/Redo、同 HWND 多 ImGui context 的中文 IME 生命周期，以及默认小停靠区中始终可达的 Build/Build And Run footer；补齐 `File > Build Settings...` / `Ctrl+Shift+B` 与 `File > Build And Run` / `Ctrl+B` 的真实命令路径，并以真实窗口完成 Play/Pause/Step、中文候选窗切焦点、Build And Run 与独立 Player 启动。修复按钮回调无条件重采样 `mouse.Position` 覆盖快速拖拽起点、同帧 release 过早清空和第二次 press 越过待发 release 的顺序缺陷；Project/Inspector 的 undock→浮动移动→redock、退出重启布局恢复和 Reset Layout 已用 Computer Use + WGC 通过。双屏均为 150%/144 DPI，窗口跨显示器后继续呈现且 monitor handle 确认切换，但无不同 DPI/200% 目标硬件，不能冒充该路线通过。Release build 32 projects、0 warning/0 error；13 个测试项目合计 1,800 passed / 40 个显式环境 smoke skipped / 0 failed。证据见 `docs/evidence-2026-07-12-editor-003-unity-parity-cycle-08.md`；任务在 Explorer 人工跨窗口拖入、不同 DPI 硬件和最终差异矩阵清零前继续保持 `[~]`。
  - Unity 对标循环 09：实现 commit `06c6100f` 已将五块硬编码示意替换为显式 authoring-world provider 驱动的权威 640×360 cell 纹理，Demo 运行时与编辑态复用同一份确定性铺设逻辑；content hash 相同的场景重投影会保留 Brush 修改，变化/移除才重建或清场，空 GameObject marker 编码 rotation/scale，Editor 在 Scripting 前挂载真实或显式无声 Audio 后端。真实窗口已直接看见实际地形，`B` 以 `empty` 写入 13 个 cell 并立即出现像素洞，W/E/R、约 18 秒 Play、Stop 恢复均通过且画刷结果保留；Release build 32 projects、0 warning/0 error，13 个测试项目最终为 1,808 passed / 40 个显式环境 smoke skipped / 0 failed。证据见 `docs/evidence-2026-07-12-editor-003-unity-parity-cycle-09.md`；Explorer 人工跨窗口拖入、不同 DPI/200% 硬件和最终差异矩阵仍未清零，任务继续保持 `[~]`。
  - Unity 对标循环 10：实现 commits `7ac5385a`、`b2fe1764`、`dc1e3fa8`、`01784bc3`、`d2e6bde7`、`96baecfb` 已闭合 Scene 内容越过工具栏、Play→Stop→Play 生命周期泄漏、Game UI viewport 层级、Transform label drag、组件 enable/fold 命中区、Player/Goal 的 W/E/R Scene 操纵、Scene 内浮动/左右停靠 Brush，以及 Project 纹理/音频/脚本预览；主动巡检继续修复 Window panel focus、窄 Project 单栏、native dirty close、菜单 probe 假绿与工具测试管道死锁。真实 Windows 输入、两张 1029×720 framebuffer、默认工作台/build-player 探针均通过；Release build 32 projects、0 warning/0 error，13 个测试项目 1,835 passed / 40 个显式环境 smoke skipped / 0 failed。证据见 `docs/evidence-2026-07-13-editor-003-unity-parity-cycle-10.md`。不同 DPI/200% 硬件、Explorer 人工跨窗口拖入与独立 reviewer 仍未满足，任务继续保持 `[~]`；本轮完成后从 detached clean worktree 更新并验证 `最终输出`。
  - Unity 对标循环 11：实现 commits `d2f65ff5`、`6ec279b8` 已补齐 Play/Paused authoring 写隔离、嵌套 Play 快照保护、200%/窄窗口 Project Picker 与 Preferences 响应式布局、自定义编辑器草稿校验和 Reset Layout 错误恢复；定向回归 43/43、109/109、EditorApp 18/18，Release build 0 warning/0 error，1280×720 与 800×600 的 200% UI Scale 真实窗口 framebuffer 均通过。证据见 `docs/evidence-2026-07-13-editor-003-unity-parity-cycle-11.md`；任务转为 `[!]`，只等待已明确列出的外部解除条件。

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

- [~] `UI-004` 建立场景级 Web Canvas / CanvasScaler、多 Canvas runtime 与 Unity-like Game View 呈现工作流。2026-07-13 已以 commit `10659e60` 完成提交节点一的设计/schema/公开 API/Player WindowMode 合同，以 commit `0576be9c` 完成提交节点二的三模式 CanvasScaler resolver、帧边界 display metrics source、ManagedFallback/RmlUi 同源 layout/raster/input/IME metrics 与真实 desktop GL framebuffer smoke，以 commit `c85fee2e` 完成提交节点三的 `.scene` v3、StableId/prefab/primary 规则、多 Canvas registry、脚本兼容桥与真实窗口动态重配，并以 commit `e584a0bd` 完成提交节点四的 Inspector Canvas/Scaler authoring、Undo/Redo、Prefab override/诊断和同 GL context 真实 XHTML/CSS/字体/图片 Scene 无副作用离屏预览/hot refresh；提交节点五已在本提交完成固定 world→presentation→完整 Web UI pipeline、Game View preset/Fit/25%–200%/crop/pan/maximize/Maximize On Play、pending→committed→texture revision、presentation UI/world gameplay 两阶段输入与 IME 双向映射，以及三种 Player WindowMode 的 settings/build/package/首帧启动链。最终代码 Hosting 789 passed / 7 个显式环境条件 skipped、Rendering 189 passed / 26 skipped、Demo 137 passed / 1 skipped，真实 desktop GL presentation/资源状态与 Hosting multi-Canvas smoke 4/4，Solution Release build 0 warning / 0 error；当前进入提交节点六：Demo dogfood、真实窗口/native 证据、canonical 完成状态与 detached clean-worktree `最终输出/`。
  - 优先级：P0。
  - 依赖：`BASE-013`、`BASE-014`、`EDITOR-009`。
  - 设计来源：`docs/PixelEngine-核心目标与产品定位.md` §5/§6.3；`plan/20-interactive-html-ui.md` §1.1；`plan/19-standalone-editor-app.md` §Web Canvas 与 Game View 呈现设计补充；`plan/18-hosting-runtime.md` §1.1；`plan/11-scripting-system.md` §3.10/§3.11。
  - 实现范围：`.scene` v3 提供可序列化的 `Canvas (Web)` / `Canvas Scaler` 内建组件、v1/v2 implicit primary Canvas 兼容（Editor 只读预览 + 显式 Convert，不静默改盘）和多 Canvas 确定性排序/输入；effective `UiCanvasId` 由 GameObject StableId 派生，duplicate/paste/prefab 双实例/嵌套实例必须 remap，Prefab asset 不保存 primary，重复 StableId/多个 enabled primary 阻止 Save/Play；无 Scaler 使用明确默认值，孤立 Scaler inactive，disabled/all-disabled primary 不复活 implicit Canvas；公开 `UiCanvasId/Handle` 与带 Canvas 参数的脚本 API，同时永久保留旧 API 到 primary Canvas 的兼容转发；`UiCanvasScaleResolver` 完整支持 Constant Pixel Size、Scale With Screen Size（Match Width Or Height/Expand/Shrink）和 Constant Physical Size（真实 DPI/明确 fallback）；RmlUi/ManagedFallback 使用同一 logical/render/input metrics；`PlayerWindowMode { Windowed, MaximizedWindow, BorderlessFullscreen }` 贯穿 Player Settings、build 与打包启动。
  - Editor 验收：Hierarchy/Inspector 可创建、选择、Undo/Redo、Prefab override 与保存 Canvas/Scaler，并清楚显示 derived Canvas id、默认/孤立 Scaler、disabled primary、Primary None 与冲突修复诊断；Scene View 使用真实 XHTML/CSS/字体/图片和同一 GL context 显示无副作用 authoring preview，不是占位框或独立 OS 窗口；Edit 不合成 runtime UI，Play/Paused 才合成，Stop→Play 不保留旧屏栈、focus、capture、composition 或 backend 状态。
  - Game View / Player 验收：严格拆分固定 Internal World Resolution、Presentation/Game Screen Resolution、Editor Display Rect；顶部支持 Player Default、Free Aspect、常用比例、固定/自定义分辨率、Fit/百分比 Scale、crop/pan、最大化/还原和 Maximize On Play；4:3/portrait/1080p preset 不改变 640×360 等 world/camera aspect、可见范围或内部像素数，而以 centered letterbox 呈现；Web UI 覆盖完整 presentation，gameplay 只使用 world content rect。Editor panel 最大化不改写 Player OS 窗口模式；Player 的 Windowed/Maximized Window/Borderless Fullscreen 在首帧前生效，且 Width/Height 仍是 presentation 尺寸与 Windowed 初始尺寸；窄窗口/高 DPI 下低频项进入溢出菜单而不截断。
  - 自动化：CanvasScaler 三模式公式、非法/非有限值、raw physical DPI fallback、跨 monitor revision；v1/v2→v3、multi-canvas/primary/排序/roundtrip、同 prefab 双实例 id remap、duplicate primary 清除、重复 primary 拒绝、无/孤立 Scaler、disabled/all-disabled；脚本兼容 overload 与 event 来源；ManagedFallback/RmlUi metrics conformance；真实 XHTML authoring preview 无 action 副作用；固定 internal 16:9→4:3/portrait presentation centered letterbox，presentation/UI 与 world/gameplay 两阶段输入（UI 可命中 letterbox、透明未处理输入不漏给 gameplay），Game View Fit/crop/pan 与 pointer/scroll/IME 双向映射；pending→committed→texture revision 不混帧；toolbar 控件激活阻断 gameplay、最大化/还原不改 dock ini；Player WindowMode 旧 schema 迁移、settings store/build result/打包启动/首帧窗口/audit 全链；Edit/Play/Paused/Stop、resize/DPI、preset/Scale/maximize-on-play 和连续两次 Play 生命周期；稳态 update/composite/input 零托管分配。
  - 证据要求：UI/Hosting/Rendering/Editor/Demo 定向与全量 Release 测试、Solution Release build 0 warning/0 error；同一 commit 的 Windows 真实窗口截图/录屏至少覆盖 16:9、4:3、portrait、固定 1920×1080、150%/200% DPI、跨屏、Scene HTML preview、Game View maximize-on-play、三种 Player WindowMode 与 Play→Stop→Play；ManagedFallback 与 RmlUi 均需可重跑 framebuffer/输入证据；完成后从 detached clean worktree 重建并独立验证 `最终输出/`，本地自动化不得冒充缺失的真实窗口/native 证据。
- 提交节点：一，设计/schema/公开 API/Player WindowMode 合同；二，CanvasScaler resolver、display metrics source 与后端 metrics；三，`.scene` v3、StableId/prefab/primary 规则、多 Canvas registry 与兼容桥；四，Inspector/Scene authoring preview；五，固定 world→presentation pipeline、Game View toolbar/maximize、revision 与两阶段输入/IME 映射、Player 窗口启动链；六，Demo dogfood、真实证据、canonical 完成状态与 clean-worktree `最终输出/`。每个节点按 `AGENTS.md §6` 单独中文提交，不攒成一次大提交。
  - 提交节点六本地实现（2026-07-14）：commit `ebbc9d91` 已把默认 Demo 升级为显式三 Canvas / 三种 CanvasScaler、RmlUi Player、Web Canvas 与旧脚本 GUI 互斥、Esc 暂停恢复、多 Canvas probe 和 ManagedFallback 窗口命名空间；Solution Release build 0 warning / 0 error，UI 155 passed / 11 skipped、Hosting 789 passed / 7 skipped、Demo 140 passed / 1 skipped，desktop GL native UI 12 passed / 4 个 ANGLE-only skipped、Hosting 3/3、Demo 1/1。commit 绑定 framebuffer、SHA256、运行摘要与 Windows 激活失败边界见 `docs/evidence-2026-07-14-ui-004-web-canvas-player-dogfood.md`。真实输入矩阵、不同物理 DPI/跨屏、三种 Player WindowMode、外部 reviewer 与 detached clean-worktree `最终输出/` 尚未闭合，任务保持 `[~]`。

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
