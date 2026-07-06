# Plan 19 — Unity-like Editor 独立应用（PixelEngine.Editor.Shell）

> 产品依据：`../docs/PixelEngine-核心目标与产品定位.md`。本文件是 Unity-like Editor 状态账本，负责独立编辑器壳、项目/资源/Hierarchy/Inspector/Scene View/Game View/Console/Settings/Prefab/Build Settings、EditorShell 与玩家包解耦，以及 M13 结构闭合、M14 UX Contract、M15 人工 UX 证据。
> 状态约定：`- [x]` 已有源码、测试、工具、报告或 plan 证据；`- [ ]` 未完成目标；`- [!]` 外部证据债、人工验收、硬件/native/发行/真实窗口阻塞。本文不再使用进行中状态，所有部分完成事项拆成已完成子项与未完成/阻塞子项。

---

## 1. 当前产品职责

- [x] **Unity-like Editor 产品面**：交付一个独立 EXE、独立顶层窗口、单进程 in-process 宿主 Engine 的编辑器应用，面向 Unity 用户提供 Project、Hierarchy、Inspector、Scene View、Game View、Console、Settings、Prefab 与 Build Settings 心智模型。
- [x] **Editor ImGui 面板层宿主**：编辑器产品面复用 `PixelEngine.Editor` 的 Editor ImGui 面板层，但面板层本体仍归 plan/12；本文件负责 Shell 顶层产品流、窗口生命周期、项目/session、authoring 与面板编排。
- [x] **Engine runtime 消费者**：EditorShell 只消费 Hosting 公开 API：窗口/GL attach、Edit/Play/Step、`.scene` writer、运行时物化、持久世界存读档、build-player 子进程入口。
- [x] **玩家包解耦边界**：玩家运行时与编辑器工具包分离；player 包不含 `PixelEngine.Editor.dll`、ImGuizmo/ImPlot 等编辑器专属闭包，允许玩家 HUD 所需 `Hexa.NET.ImGui` 核心。
- [x] **非职责边界**：本文件不实现 CA/Physics/Rendering/Audio、脚本编译、Editor ImGui 面板内部逻辑、Web-first UI 后端本体、Demo 玩法内容或 plan/15 打包脚本内部逻辑。

## 2. 状态总览 checklist

- [x] **M13 结构闭合**：`PixelEngine.Gui` 中性化、Hosting 去 Editor 硬引用、`apps/PixelEngine.Editor.Shell`、单窗口/单 GL、ProjectPicker、in-process Engine attach、Edit/Play/Step、GameObject authoring、`.scene` v2、Prefab、Build Settings 面板、build-player 子进程编排与 player-only audit 均已落地。
- [x] **M13 编排证据闭合**：shell scripted probe 已覆盖打开工程、Edit 装配、Play/Exit 回滚、保存场景、关闭工程、同窗口重开工程、编辑器内 Build/Build And Run、失败诊断、取消后重跑和设置持久化。
- [ ] **M14 Unity-like UX Contract 未完全产品化**：Project Window 资产语义、stable asset id/manifest、logical path、拖拽引用、资源移动/重命名引用保持、脚本双击外部编辑器、Project/Player/Build Settings 与 Hosting DTO 绑定仍需逐项 checklist 化和实现/证据对账。
- [ ] **M14 Settings 同源 schema 未闭合**：Project Settings / Player Settings / Build Settings 需要绑定 plan/18 的 `ProjectSettingsDto` / `PlayerSettingsDto` / `BuildProfileDto`，不能在 shell 内形成第二套孤岛 schema。
- [!] **M15 人工 UX 证据未闭合**：真实窗口完整路线视频、人工 UX 走查、脚本外部编辑器、资产引用稳定性、Project Window 拖拽/移动/重命名不破坏引用等需要人工/真实窗口证据；scripted probe 不能替代最终 UX 复核。

## 3. M13 结构完成 checklist

- [x] 新增 `src/PixelEngine.Gui` 中性程序集，承载 `HexaImGuiBackend`、中性 `IGuiContext` 运行时适配、`GuiFontManager` 与 GUI render bridge；`PixelEngine.Editor` 只保留编辑器专属面板、dockspace、ImGuizmo/ImPlot 绑定。
- [x] `PixelEngine.Hosting` 删除对 `PixelEngine.Editor` 的编译期引用，改为通过 `IEditorHostExtension` / 相位 [10] 钩子由 EditorShell 注入 Editor 面板宿主。
- [x] `demo/PixelEngine.Demo` 去除 `using PixelEngine.Editor` 与 `EnableEditor` 路径，玩家 HUD 通过 `PixelEngine.Gui` 中性 host 工作，玩家闭包不再传递到 Editor。
- [x] 新增 `apps/PixelEngine.Editor.Shell` 可执行项目，引用 `{Hosting, Editor, Gui}`，位于 Demo 同层；Shell 启动即创建唯一 `RenderWindow`、唯一 GL context 和中性 ImGui host。
- [x] ProjectPicker / `EditorProject` / `RecentProjectsStore` 已支持新建工程、打开工程、最近工程、`project.pixelproj` 读写、路径逃逸拒绝与 content/scenes 骨架生成。
- [x] `EditorProjectSession` 已通过 Hosting attach 外部窗口，Engine 不 own window，关闭/切换工程只释放 Engine 并保留 Shell 窗口/GL context。
- [x] `EditorMainMenuBar` 与默认 dock 布局已提供 File/Edit/GameObject/Window/Play/Help，默认显示 Hierarchy、Scene View、Inspector、Project、Console、Performance HUD、Build Settings 等工作区。
- [x] `EditorSceneModel` / `EditorGameObject` / `EditorComponentModel` 已成为 authoring 真相源；运行时保持扁平 DOD，父子层级在物化时烘焙为世界 TRS。
- [x] Hierarchy 已支持创建、删除、重命名、复制、启用位、拖拽重父、防环、选中联动与 Undo/Redo 命令栈。
- [x] Inspector 已支持 Name、Enabled、StableId、Transform TRS、组件列表、Add/Remove Component、字段反射编辑与命令栈回滚。
- [x] Scene View 已复用 viewport，支持 Edit 相机、ImGuizmo 平移/旋转/缩放、屏幕空间拾取、空对象 billboard 命中与 gizmo/画刷输入仲裁。
- [x] `.scene` schema 已升到 `FormatVersion=2`，包含 `ParentId` 与 Transform TRS；v1 兼容读取、writer 稳定排序、读写往返等价已由测试覆盖。
- [x] Prefab 已实现资产、实例、override、Revert、嵌套 prefab、override 传播与运行时展开物化。
- [x] Shell 已复用 plan/12 面板：Project 底座、材质+反应编辑器、世界画刷/检视器、调试叠层、性能 HUD、存读档、子系统调参、sim 控制与 Edit/Play 模式面板。
- [x] Build Settings 面板已落在 Shell 程序集，消费 plan/15 `tools/build-player`，支持设置、预检、NDJSON 进度、日志、取消、Build And Run、dev-audit / release audit 布局分流。
- [x] plan/15 audit 已拒绝 player 包 `app/` 内 `PixelEngine.Editor.dll`、ImGuizmo/ImPlot 编辑器闭包，并允许 `Hexa.NET.ImGui` 核心作为玩家 HUD 依赖。

## 4. M14 Unity-like UX Contract checklist

- [ ] **Project Window 资产模型**：将现有 `AssetBrowserPanel` 从文件浏览底座升级为 Unity-like Project Window，补 stable asset id/manifest、logical path、asset type、预览、搜索过滤、创建资产、删除确认、重命名与移动引用保持。
- [ ] **资产拖拽语义**：定义并验证拖拽 prefab/scene/material/script/texture/audio 到 Hierarchy、Scene View、Inspector 字段时的行为；无效 drop 必须给出可见诊断，不能静默失败。
- [ ] **脚本外部编辑器**：脚本资产双击应调用外部编辑器或系统默认 opener，并记录失败诊断；不得只在 Project Window 中选中脚本而无打开行为。
- [ ] **Project Settings**：通过 plan/18 `ProjectSettingsDto` 绑定工程名、content root、script source dir、默认 scene、资源规则、编辑器偏好、默认 UI backend；支持读写、校验、迁移和错误提示。
- [ ] **Player Settings**：通过 plan/18 `PlayerSettingsDto` 绑定窗口标题、分辨率、VSync、图标、版本、启动场景、输入默认、运行时 UI backend、发行通道；保证运行时与 build-player 参数一致。
- [x] **Build Settings**：已通过 plan/18 `BuildProfileDto` 同源投影绑定 RID/channel/configuration、R2R/AOT、入包场景、启动场景、输出目录、符号、Build/Build And Run；`BuildSettingsStore` 读写 Hosting `BuildSettings.json`，消除 shell 内独立构建设置 schema 与 Hosting schema 的漂移风险。
- [ ] **Console 产品面**：将构建日志、脚本编译错误、UI/native/backend 降级、资源加载错误统一进入 Console 可筛选视图；保留 build 面板局部日志，但错误入口不应分散。
- [ ] **Game View 产品面**：明确 Scene View 与 Game View 的相机、输入、Play 状态、UI overlay 与编辑器 overlay 关系；Game View 在 Play 模式应展示玩家视角与 Web-first UI，而 Scene View 保持 authoring 工具视角。
- [ ] **默认工作台体验**：首次启动、新建工程、创建 GameObject、添加脚本、保存场景、进入 Play、退出 Play、构建玩家包应形成连续可演示路线。

## 5. M15 人工 UX 证据 checklist

- [!] 真实窗口完整路线：需要同一 `reviewSessionId` / `gitCommit` 的视频或等价人工复核材料，覆盖启动 Shell、新建/打开工程、默认布局、Project/Hierarchy/Inspector/Scene View/Game View/Console、Play/Exit、保存、Build And Run。
- [!] Project Window 引用稳定性：需要人工或端到端证据证明资产移动/重命名后 Prefab、Scene、Inspector 字段与 Build 入包不丢引用；单元测试或脚本探针只能作为辅助。
- [!] 脚本外部编辑器：需要真实 OS opener / configured editor 的打开证据、失败提示截图或日志；不能用“按钮存在”作为完成。
- [!] Settings UX：Project/Player/Build Settings 需要真实窗口填写、保存、重启恢复、错误输入校验和 build-player 参数投影证据；不能只靠 DTO 单测。
- [!] Editor 产品可用性：需要人工确认布局、快捷键、拖拽、gizmo、Undo/Redo、Console 错误、Build 面板反馈在目标窗口环境中可理解、可恢复、无阻塞 UX 缺陷。
- [!] 证据状态边界：`scripted_probe_only`、截图、短跑 smoke、`manual_evidence_attached_pending_review` 只能说明证据入口可用，不能把 M15 UX 验收转为 [x]。

## 6. 验证命令与证据路径 checklist

- [x] `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --filter FullyQualifiedName~HostingProjectDisciplineTests|FullyQualifiedName~EngineWindowOwnershipTests` 覆盖 Hosting 去 Editor 引用与窗口所有权解耦。
- [x] `dotnet test tests/PixelEngine.Editor.Shell.Tests/PixelEngine.Editor.Shell.Tests.csproj -c Release --filter FullyQualifiedName~EditorShellProjectTests|FullyQualifiedName~EditorScene|FullyQualifiedName~Prefab|FullyQualifiedName~PlayerBuildService` 覆盖工程模型、场景往返、Prefab 与 build-player 编排。
- [x] `dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --filter FullyQualifiedName~PlayerOnly|FullyQualifiedName~DemoStartupOptionsTests` 覆盖玩家包解耦与 startup 分派边界。
- [x] `tools/build-player.ps1` / `tools/build-player.sh` 与 `tools/audit-release-artifacts.*` 是 Build Settings 面板消费的出包与 player-only 审计真相源。
- [x] `docs/runtime-reports/2026-07-06-editor-shell-attach-probe.md` 是当前 Shell attach / scripted probe 证据路径。
- [!] `tools/demo-manual-acceptance-preflight.ps1` 只提供人工验收证据入口；缺合格 manifest、真实窗口材料与人工复核前，M15 UX 证据保持阻塞。

## 7. 依赖与下一闭合节点 checklist

- [x] 上游依赖：plan/00 依赖方向与技术栈、plan/12 Editor ImGui 面板层、plan/18 Hosting attach/Edit/Play/scene writer、plan/11 脚本与 Scene 模型、plan/08 RenderWindow/GL/UI 层、plan/15 build-player 与 player-only audit 已登记为 Shell 的公开 API 边界。
- [x] 下游消费：plan/13 Demo 使用 player-only 解耦后的公开 runtime；plan/20 复用 `PixelEngine.Gui` 字体与 ManagedFallback；plan/14 负责 shell scripted probe、scene/prefab/build tests；plan/17 只登记 M13/M14/M15 DAG 与退出标准。
- [ ] 下一闭合节点：将 plan/18 `ProjectSettingsDto` / `PlayerSettingsDto` 绑定到 Project Settings / Player Settings 面板与运行时消费路径，补 Settings UX 保存、重启恢复、错误输入校验和 build-player 参数投影证据。
- [ ] 下一闭合节点：补 Project Window stable asset id/manifest 与拖拽/移动/重命名引用保持路线，并把证据写入本文件 M14 UX Contract。
- [!] M15 后续节点：补真实窗口人工 UX 材料，完成后再同步 README/plan17 dashboard；不得用 scripted probe 替代人工 UX 完成态。
