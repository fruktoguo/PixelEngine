# Plan 18 — Hosting Runtime（PixelEngine.Hosting）

> **状态迁移（2026-07-10）**：本文件保留详细设计与历史 checkbox；当前状态、顺序和完成条件以 [`plan/tasks/README.md`](tasks/README.md) 为唯一真相源。不要在本文件新增 live task；设计变化仍须同步到这里。

> **DOC-002 历史证据口径（2026-07-10）**：后文 checkbox 与“已通过/已完成”叙述冻结自旧计划快照 `179efc3a`，迁移基线为 `5af1541f`，均不构成 live 状态；证据等级以 [稳定 Evidence Index](../docs/evidence-index.md) 为准。未入索引的 `artifacts/`、`BenchmarkDotNet.Artifacts/`、`scratch/` 仅是可再生历史线索；替代报告与重跑命令见 [DOC-002 校正报告](../docs/evidence-2026-07-10-doc-002-legacy-plan-audit.md)。

> 产品依据：`../docs/PixelEngine-核心目标与产品定位.md`。本文件是 Hosting / runtime 状态账本，负责 Engine Core 运行时编排、Unity-like Editor 所需中性运行时 API、Web-first 透明 HTML UI Runtime 装配，以及 Showcase Demo Game 的公开运行入口。
> 状态约定：`- [x]` 已有源码、测试、工具、报告或 plan 证据；`- [ ]` 未完成目标；`- [!]` 外部证据债、人工验收、硬件/native/发行/真实窗口阻塞。本文不再使用进行中状态，所有部分完成事项拆成已完成子项与未完成/阻塞子项。

## 2026-07-20 无限程序化世界装配合同（DEMO-006）

`IProceduralWorldGenerator` 的有限 resident world 合同保持兼容；无限场景由独立 `IStreamingProceduralWorldGenerator` 标识，并通过 `ProceduralWorldDescriptor.CreateInfinite` 声明 seed、初始相机焦点与稳定 persistence key。Hosting 为它创建 `WorldManager`、公开缺失 chunk initializer adapter、Simulation 与 World phase driver，首次同步泵入 initial active + border 区后再进入脚本循环。默认持久目录位于可写的 LocalApplicationData，而不是 ContentRoot / 安装目录；宿主和测试可显式传入 root。

同 seed 的 chunk 生成只依赖 `ChunkCoord` 与材质查询快照，必须支持负坐标且跨边界连续。`AttachCurrentSceneWorld` 对 procedural 已成功装配但没有 `WorldLoadResult` 的情形不得误判成“没有 world”并重复挂载 resident fallback；调用方以已注册 `SimulationPhaseDriver` / 明确装配状态区分。

`.scene` 可通过互斥于 `initialSaveDirectory` 的 `proceduralWorldGenerator` 字段声明流式生成器。Hosting 先查询显式注册表，再从该场景已物化的 Behaviour 中发现实现 `IStreamingProceduralWorldGenerator` 的同键实例；因此独立 Editor 无需反向引用 Demo 程序集，也能在动态脚本加载后装配和运行该世界。默认 Demo 场景 `scenes/infinite-sandbox.scene` 使用此路径。

---

## 1. 当前产品职责

- [x] **Engine Core runtime**：`PixelEngine.Hosting` 拥有 `EngineBuilder` / `Engine` / `EngineContext`、12 相位主循环、子系统装配、生命周期、过载降级与 headless 驱动。
- [x] **Unity-like Editor 运行时边界**：Hosting 只暴露中性 API，不引用 `PixelEngine.Editor`；`apps/PixelEngine.Editor.Shell` 通过注入式 host extension 使用窗口/GL 所有权解耦、Edit/Play/Step、`.scene` writer 与运行时物化。
- [x] **Web-first UI Runtime 装配点**：Hosting 装配 `GameUiHost`、`GameUiPhaseDriver`、`IGameUiService` 与 order=100 的游戏 UI 层，并保证相位 [10] 为 world → Web-first game UI → Editor ImGui overlay。
- [x] **Showcase Demo Game 公开入口**：Demo 通过 Hosting 公开 API 启动、读取 `content/startup.json`、选择 `.scene` / save directory / procedural generator，并消费脚本、输入、音频、UI、物理和世界服务。
- [x] **非职责边界**：Hosting 不实现 CA、Physics、Rendering、Audio、Editor ImGui 面板层、Unity-like Editor 产品 UX、Web-first UI 后端本体或 Demo 玩法内容；这些由对应 leaf plan 承担。

## 1.1 UI-004：`.scene` v3、Canvas 物化与 presentation 装配边界

Hosting 是场景级 Web Canvas 与三层分辨率的中性装配所有者；不能让 EditorShell 直接构造 `GameUiHost` 内部状态，也不能把 Canvas 伪装成用户脚本 Behaviour。

- `.scene` schema v3 在 GameObject 上增加可判别的 `WebCanvas` 与 `CanvasScaler` 内建组件 DTO。`WebCanvas` 保存 manifest/screen 资产引用、enabled、sorting order 与 scene-level primary；effective `UiCanvasId` 由 owning GameObject StableId 派生，不重复入盘。`CanvasScaler` 保存 plan/20 §1.1.2 的完整 settings。Prefab asset 禁止 `primary=true`；duplicate/paste/prefab instantiate/nested prefab 先 remap instance GameObject StableId，复制 scene primary 时清除副本 primary。重复 StableId 或多个 enabled explicit primary 是阻止 Save/Play 的 schema error；writer 稳定排序并保证 v3 save→load 等价。
- v1/v2 或 v3 中完全没有 Canvas 的场景保持旧语义：loader 生成不写回文档的 implicit primary Canvas，指向工程既有 UI manifest。存在显式 Canvas 后不再额外生成 implicit Canvas；primary 顺序固定为 enabled explicit → legacy implicit → enabled Canvas 中 sorting order/StableId 第一项。disabled primary 被跳过并诊断；全部 explicit Canvas disabled 时 primary 为空，旧 API no-op/返回失败，不复活 implicit Canvas。
- authoring→runtime 物化把 enabled Canvas 转成固定容量 Canvas registry/handle；缺少 Scaler 的 Canvas 使用 `UiCanvasScalerSettings.Default`，孤立 Scaler 只保留诊断且不物化 Canvas。Canvas/Scaler 不进入 `Scripting.Scene` 的按组件类型热桶，不执行 Behaviour 生命周期。切场景、Stop 和 Engine dispose 逆序释放各 Canvas 的 document、model、backend surface 与输入状态。
- Hosting/Rendering 公开带中文 XML 的 `GamePresentationDescriptor`（presentation size/source、effective world content rect、`UiDisplayMetrics`、display-metrics revision 与单调 presentation revision）、`GamePresentationInputMapping`（presentation/UI 与 world/gameplay 两阶段区域/坐标）、`IDisplayMetricsSource`、`IGamePresentationOverride`（Editor pending preset）和 `IGameUiCompositionPolicy`。pending descriptor 只在 render 前帧边界 commit；texture/snapshot/input/IME 仅在 revision 一致时共同切换，允许一帧延迟但禁止新旧几何混用。职责不得重新折叠回 `EngineOptions.InternalWidth/Height` 或 `IUiPresentTargetProvider`。
- Player 启动默认从 `PlayerSettingsDto` 得到 presentation/window 尺寸与 `PlayerWindowMode { Windowed, MaximizedWindow, BorderlessFullscreen }`；旧 settings 缺字段时迁移为 Windowed。Width/Height 继续作为 presentation 尺寸与普通窗口初始尺寸；Maximized/Borderless 只改变 OS window/framebuffer，presentation 仍保持配置值并 Fit 呈现。WindowMode 必须贯穿 Player Settings UI/store、build request/result、packaged startup、audit 与 `RenderWindowOptions`，在首帧前应用；不得暴露未实现的 Exclusive Fullscreen。EditorShell preset 仍是 session-local override，不修改 Player Settings、scene 或 build profile。
- Rendering 保持 `EngineOptions.InternalWidth/Height` 对应的固定 world/camera surface（默认 640×360），不因 4:3/portrait/1080p preset 改 camera aspect、可见世界范围或内部像素数；该 surface 居中 Fit 到 presentation，4:3/portrait 的 letterbox 属于明确产品边界。gameplay overlay 只写 world content rect，runtime Canvas 写完整 presentation；`CurrentViewportTexture` 发布最终 presentation surface，独立 Player 再把它 Fit/呈现到 OS framebuffer。
- Edit 时 composition policy 必须显式拒绝 runtime Canvas surface；不能把 provider 缺失解释成“回退整 runtime viewport”。Play/Paused 允许合成；Stop 后 policy、registry、screen stack、focus/capture/composition 全部回到干净 Edit 基线，Play→Stop→Play 的 Canvas handle 与 backend 状态不得泄漏。

自动化必须覆盖 v1/v2 implicit compatibility、v3 多 Canvas roundtrip、同 Canvas prefab 双实例 StableId remap、duplicate primary 清除、重复 primary 拒绝、无/孤立 Scaler、disabled/all-disabled primary、旧无 Canvas 项目；Player WindowMode 全链迁移/打包/首帧状态与 Editor override 隔离；16:9/4:3/portrait 均保持固定 internal world/camera 尺寸并正确居中 letterbox；pending→committed revision、跨 monitor physical DPI、presentation/UI 与 world/gameplay 两阶段输入；Edit/Play/Paused/Stop policy，以及重复 Play 后 document/handle/input 状态清零。

## 2. 状态总览 checklist

- [x] M10/M13 Hosting 底座已闭合：`EngineBuilder`、`Engine`、`EngineContext`、12 相位、headless、Play/Edit/Step、GUI 宿主中性化、窗口/GL 所有权解耦与 EditorShell attach 证据已记录。
- [x] M14 Web-first UI runtime 装配底座已闭合：`EnableGameUi`、`UseUiBackend`、`GameUiHost`、`GameUiPhaseDriver`、`IGameUiService`、相位 [10] order 合成与禁用零装配已落地。
- [x] M14 player 启动分派已闭合：`content/startup.json` 可分派 `SceneFile`、`SaveDirectory`、`Procedural`，缺省回落 `playable-world`。
- [x] M14 中性配置 DTO 底座已落地并被 Shell 消费：`ProjectSettingsDto`、`PlayerSettingsDto`、`BuildProfileDto` 与 `EngineProjectSettingsStore` 读写/校验入口已实现；Project / Player / Build Settings 均通过 Hosting DTO/store 持久化，Project Settings 已驱动 EditorProject/EngineProject 入口，Player Settings 已投影到 headless runtime options、build-player 参数、`build-result.json` 与 packaged `startup.json`。
- [!] M15 Editor 真实窗口人工验收仍阻塞：scripted probe、截图、`manual_evidence_attached_pending_review` 只能作为证据入口，不能替代人工 UX / 真实窗口验收完成。
- [!] M15 native leak 证据仍阻塞：managed detector 与 process smoke 不能替代跨平台 runner、GL driver 级 detector、OpenAL/Box2D/ALC/GL 外部工具级报告。

## 3. 已实现证据 checklist

- [x] 依赖方向已收敛：`{Demo, EditorShell} → Hosting → {Scripting, Rendering, Audio, Physics, World, Serialization, Content, Simulation, UI, Gui} → Interop → Core`；Hosting 编译期不再引用 `PixelEngine.Editor`。
- [x] 子系统装配与**初始化顺序**已按 Core → Content → Simulation → World → Physics → Audio → Rendering → GPU → Scripting → Gui → optional injected Editor 装配，关闭逆序释放；Hosting 创建的 `AudioSystem` 已纳入 Engine owned runtime resources，owning clip cache 随 `AudioSystem.Shutdown` 删除已上传 buffer，`AudioPhaseDriverTests.EngineLoadsContentAudioAndInjectsScriptAudioApi` 验证 `Engine.Dispose()` 后测试后端 live object 清零。
- [x] 12 相位主循环已绑定：相位 [0] 输入/时间、[1] 脚本与 UI 事件、[2] residency、[3] 粒子沉积、[4] CA、[5] 温度、[6] dirty swap、[7] cell→particle、[8] physics、[9] render buffer、[10] GPU/render/UI、[11] streaming。
- [x] 不追帧帧节奏已锁定：每帧至多一次 sim/physics step，sim 可降到 30Hz 且 render 逐帧出帧；`EnginePhasePipelineTests` 与 `EngineOverloadControllerTests` 覆盖降频/过载行为。
- [x] 过载降级按五级顺序触发：热场、光照、远 chunk、sim 30Hz、接受低 fps 顺序下发质量档位。
- [x] 脚本服务后端已聚合 `IWorldAccess`、`IParticleService`、`IPhysicsService`、`IMaterialRegistry`、`ICamera`、`IInput`、`IEventBus`、`IAudioService`、`ISceneService`、`IDiagnostics`、`IRuntimeControlApi`，写操作经延迟命令队列落正确相位。
- [x] Play/Edit/Step 已接入 `EngineEditorPlaySessionService` 与 `EngineWorldSnapshotStore`，进入 Play 前快照、退出 Play 回滚、脚本生命周期重新派发。
- [x] GUI 宿主中性化已完成：`PixelEngine.Gui` 承载 `HexaImGuiBackend`、中性 `IGuiContext` 适配与 `GuiFontManager`；玩家包闭包不含 `PixelEngine.Editor.dll`，但允许玩家 HUD 所需 `Hexa.NET.ImGui`。
- [x] 窗口/GL 所有权解耦已完成：EditorShell 可先创建窗口和 GL context，Engine attach 外部窗口且 `Engine.Dispose()` 不销毁该窗口；`docs/runtime-reports/2026-07-06-editor-shell-attach-probe.md` 记录真实窗口 scripted probe。
- [x] `.scene` v2 writer 与运行时物化已完成：`SaveSceneDocument` 支持 `ParentId`、Transform TRS、`Vector2`、v1 兼容读取、稳定排序、运行时扁平 DOD 物化。
- [x] Game UI 装配已完成：`RenderPipeline.RegisterUiLayer` order=100/200、`GameUiPhaseDriver`、`UiInputRouter`、`IGameUiService`、`ui.*` 计时与禁用零开销均有 `PixelEngine.UI.Tests` / `PixelEngine.Hosting.Tests` 覆盖。
- [x] player startup 分派已完成：`DemoStartupOptionsTests` 与 `SceneAndHeadlessTests` 覆盖 `SceneFile`、`SaveDirectory`、`Procedural` 三路径。

## 4. M14 配置 DTO checklist

- [x] `ProjectSettingsDto`：已定义工程名、content root、script source dir、默认 scene、资源规则、编辑器偏好、默认 UI backend，并提供默认值、schema version、JSON 读写、校验与路径逃逸拒绝测试。
- [x] `ProjectSettingsDto` EditorShell 消费：Project Settings 面板已通过 `ProjectSettingsStore` 直接代理 Hosting `EngineProjectSettingsStore` 的 `ProjectSettings.json`，保存后同步更新 `EditorProject` / `project.pixelproj`，重开工程时回读并驱动 `ToEngineProject()` 的 content root、script dir 与 start scene，不在 Shell 内定义第二套 schema。
- [x] `PlayerSettingsDto`：已定义窗口标题、分辨率、VSync、图标、版本号、启动场景、输入默认、运行时 UI backend、发行通道，并提供默认值、JSON 读写、校验与路径逃逸拒绝测试。
- [x] `PlayerSettingsDto` EditorShell / runtime 消费：Player Settings 面板已通过 `PlayerSettingsStore` 读写 Hosting `PlayerSettings.json`，并经 `PlayerSettingsEditorAdapter` 投影到 `EngineBuilder` headless/runtime options、BuildRequest、build-player 参数、`build-result.json` 与 packaged `startup.json`；Demo startup 读取同源 title/size/vsync/runtime UI backend/release channel。
- [x] `BuildProfileDto`：已定义目标 RID/channel/configuration、R2R/AOT、Debug/Release、入包场景、启动场景、输出目录、符号、Build/Build And Run 参数；EditorShell Build Settings 已通过 `BuildProfileEditorAdapter` / `BuildSettingsStore` 回读并投影到 build-player 请求。
- [x] `EngineProjectSettingsStore` 读写/校验入口：已提供 `ProjectSettings.json`、`PlayerSettings.json`、`BuildSettings.json` 与玩家包 `startup.json` 的 Hosting 中性 JSON 读写入口、默认值与 schema version。
- [x] `EngineProject` 统一入口：已将上述 DTO 与 content/scenes 扫描、`startup.json`、`.scene` loader 统一成 Hosting 中性工程 schema；`EngineProject.Load` 读取工程根 settings/build profile/startup，`EngineProject.FromContentRoot` 覆盖玩家包 `.scene` / save directory / procedural fallback，EditorShell 继续仅消费 Hosting DTO/store，未引入 EditorShell authoring UI 类型。

## 5. 未完成目标 checklist

- [x] 将 Project Settings / Player Settings / Build Settings 的默认值、读写、校验与路径逃逸拒绝纳入 Hosting 测试，避免 EditorShell 在壳内形成第二套 settings schema 底座。
- [x] 在 plan/19 中把 Project / Player Settings 面板逐项绑定到本文件 DTO，保证 Unity-like Editor 的工程与玩家设置不形成 shell-local schema；自动化证据覆盖 store roundtrip、scripted apply/capture、错误输入不保存与 PlayerSettings → build/runtime 投影。
- [x] Build Settings 已消费 `BuildProfileDto` 同源投影：EditorShell 以 Hosting DTO 持久化 `BuildSettings.json`，并在启动 build-player 前叠加同源 `PlayerSettingsDto` 的标题、版本、图标、启动场景、窗口、VSync、runtime UI backend 与发行通道。

## 6. 证据债 / 阻塞 checklist

- [!] Editor 真实窗口验收：仍需同一 `reviewSessionId` / `gitCommit` 的真实窗口视频、截图、交互 checklist 与人工复核；`tools/demo-manual-acceptance-preflight.ps1` 的 `hudMenuEditorVideo` / `editor-window` scope 必须携带 `editor_enabled`、`editor_bridge_frames`、`criteria`、`hudReadable`、`menuButtonsClicked`、`editorDockspaceOpened`；`scripted_probe_only`、`capture.bmp`、`manual_evidence_attached_pending_review` 均不能转为 [x]。
- [!] Editor 真实窗口观测/覆盖仍缺人工复核证据：上述 `manual_evidence_attached_pending_review` 只代表证据入口，不代表 Unity-like Editor UX 验收完成。
- [!] Native leak 证据：仍需 GL driver 级对象创建/销毁、OpenAL、Box2D、ALC 共同覆盖的 detector 报告；`tools/native-leak-preflight.ps1` 与 `blocked_invalid_native_leak_evidence` 只提供证据门禁，`process_smoke_only` 与 managed live-count 只能辅助定位；native GL/OpenAL/Box2D 工具级泄漏审计仍由 §5 的 native leak detector 阻塞项闭合。
- [!] Managed native leak detector 辅助证据：`tools/PixelEngine.Tools.ManagedNativeLeakDetector` 已覆盖 `gl_context_rendering_wrappers` 与 `managed_no_gl_context` 等托管 live-count 边界，但 GL driver 级 detector 证据仍保持阻塞。
- [!] 外部 runner：跨平台 native/resource leak 与真实窗口证据需要目标平台 runner 或人工环境；本机 win-x64 short run 不替代 M15。

## 7. 验证命令与证据路径 checklist

- [x] `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --filter FullyQualifiedName~HostingProjectDisciplineTests|FullyQualifiedName~EngineWindowOwnershipTests|FullyQualifiedName~EnginePhasePipelineTests` 覆盖依赖方向、窗口所有权、相位与降频。
- [x] `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --filter FullyQualifiedName~AudioPhaseDriverTests` 覆盖 content/audio 预加载、脚本音频 API 注入、材质 cue 映射、sim 降频一致性、dispatch 诊断与 Engine-owned 音频资源释放。
- [x] `dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release --filter FullyQualifiedName~GameUiHostTests|FullyQualifiedName~UiInputRouterTests|FullyQualifiedName~GameUiServiceBridgeTests` 覆盖 Web-first UI 装配、输入仲裁、服务桥和禁用门控。
- [x] `dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --filter FullyQualifiedName~DemoStartupOptionsTests` 覆盖 player startup 分派与 packaged `startup.json` 中 title/window/vsync/runtime UI backend/release channel 消费，当前通过 22/22。
- [x] `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --filter "FullyQualifiedName~EditorShellBuildTests|FullyQualifiedName~SceneAndHeadlessTests|FullyQualifiedName~HostingProjectDisciplineTests|FullyQualifiedName~EngineBuilderTests"` 覆盖 Project/Player Settings store 与面板 scripted probe、ProjectSettings → EditorProject/EngineProject、EngineProject 统一入口合并 settings/startup/Build Profile/.scene 扫描、错误输入不保存、PlayerSettings → BuildRequest/runtime options/build-player 参数/`build-result.json` 投影、EngineBuilder 窗口标题/启动场景与 player build 编排，当前通过 82/82。
- [x] `docs/runtime-reports/2026-07-02-demo-window-smoke.md`、`docs/runtime-reports/2026-07-02-demo-window-longrun.md`、`docs/runtime-reports/2026-07-06-editor-shell-attach-probe.md` 是现有 runtime / window / attach 证据路径。
- [!] `tools/demo-manual-acceptance-preflight.ps1` 与 `tools/native-leak-preflight.ps1` 只提供证据入口；未有合格 manifest 和人工/外部复核前保持阻塞。

## 8. 依赖与下一闭合节点 checklist

- [x] 上游依赖：plan/01、plan/02、plan/03–10、plan/11、plan/12、plan/15、plan/19、plan/20 的公开契约已经在 Hosting 中作为装配边界登记。
- [x] 下游消费：plan/19 使用窗口所有权、Edit/Play、`.scene` writer 与 Settings DTO；plan/20 使用 UI 装配和 `IGameUiService`；plan/13 使用 startup 分派和公开 runtime services；plan/15 使用 build profile / player-only 审计边界。
- [x] 本轮闭合节点：`EngineProject` 统一入口已在 Hosting 层收敛 Project/Player/Build Settings、玩家包 `startup.json`、content/scenes 扫描与 `.scene` descriptor 解析；Demo/player startup 与 EditorShell `ToEngineProject()` 均改走 Hosting 中性入口，真实 Settings UX 保存、重启恢复、人工填写与截图证据仍归 M15 `[!]`。
- [!] M15 后续节点：补 Editor UX 人工证据与 native leak 外部 detector 证据，完成后再更新 README/plan17 dashboard。
