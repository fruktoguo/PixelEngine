# Plan 18 — Hosting Runtime（PixelEngine.Hosting）

> 产品依据：`../docs/PixelEngine-核心目标与产品定位.md`。本文件是 Hosting / runtime 状态账本，负责 Engine Core 运行时编排、Unity-like Editor 所需中性运行时 API、Web-first 透明 HTML UI Runtime 装配，以及 Showcase Demo Game 的公开运行入口。
> 状态约定：`- [x]` 已有源码、测试、工具、报告或 plan 证据；`- [ ]` 未完成目标；`- [!]` 外部证据债、人工验收、硬件/native/发行/真实窗口阻塞。本文不再使用进行中状态，所有部分完成事项拆成已完成子项与未完成/阻塞子项。

---

## 1. 当前产品职责

- [x] **Engine Core runtime**：`PixelEngine.Hosting` 拥有 `EngineBuilder` / `Engine` / `EngineContext`、12 相位主循环、子系统装配、生命周期、过载降级与 headless 驱动。
- [x] **Unity-like Editor 运行时边界**：Hosting 只暴露中性 API，不引用 `PixelEngine.Editor`；`apps/PixelEngine.Editor.Shell` 通过注入式 host extension 使用窗口/GL 所有权解耦、Edit/Play/Step、`.scene` writer 与运行时物化。
- [x] **Web-first UI Runtime 装配点**：Hosting 装配 `GameUiHost`、`GameUiPhaseDriver`、`IGameUiService` 与 order=100 的游戏 UI 层，并保证相位 [10] 为 world → Web-first game UI → Editor ImGui overlay。
- [x] **Showcase Demo Game 公开入口**：Demo 通过 Hosting 公开 API 启动、读取 `content/startup.json`、选择 `.scene` / save directory / procedural generator，并消费脚本、输入、音频、UI、物理和世界服务。
- [x] **非职责边界**：Hosting 不实现 CA、Physics、Rendering、Audio、Editor ImGui 面板层、Unity-like Editor 产品 UX、Web-first UI 后端本体或 Demo 玩法内容；这些由对应 leaf plan 承担。

## 2. 状态总览 checklist

- [x] M10/M13 Hosting 底座已闭合：`EngineBuilder`、`Engine`、`EngineContext`、12 相位、headless、Play/Edit/Step、GUI 宿主中性化、窗口/GL 所有权解耦与 EditorShell attach 证据已记录。
- [x] M14 Web-first UI runtime 装配底座已闭合：`EnableGameUi`、`UseUiBackend`、`GameUiHost`、`GameUiPhaseDriver`、`IGameUiService`、相位 [10] order 合成与禁用零装配已落地。
- [x] M14 player 启动分派已闭合：`content/startup.json` 可分派 `SceneFile`、`SaveDirectory`、`Procedural`，缺省回落 `playable-world`。
- [ ] M14 中性配置 DTO 仍未闭合：`ProjectSettingsDto`、`PlayerSettingsDto`、`BuildProfileDto` 与 `EngineProject` 读写/校验入口仍需实现并供 EditorShell Settings 与 build-player 共用。
- [!] M15 Editor 真实窗口人工验收仍阻塞：scripted probe、截图、`manual_evidence_attached_pending_review` 只能作为证据入口，不能替代人工 UX / 真实窗口验收完成。
- [!] M15 native leak 证据仍阻塞：managed detector 与 process smoke 不能替代跨平台 runner、GL driver 级 detector、OpenAL/Box2D/ALC/GL 外部工具级报告。

## 3. 已实现证据 checklist

- [x] 依赖方向已收敛：`{Demo, EditorShell} → Hosting → {Scripting, Rendering, Audio, Physics, World, Serialization, Content, Simulation, UI, Gui} → Interop → Core`；Hosting 编译期不再引用 `PixelEngine.Editor`。
- [x] 子系统生命周期已按 Core → Content → Simulation → World → Physics → Audio → Rendering → GPU → Scripting → Gui → optional injected Editor 装配，关闭逆序释放。
- [x] 12 相位主循环已绑定：相位 [0] 输入/时间、[1] 脚本与 UI 事件、[2] residency、[3] 粒子沉积、[4] CA、[5] 温度、[6] dirty swap、[7] cell→particle、[8] physics、[9] render buffer、[10] GPU/render/UI、[11] streaming。
- [x] 不追帧帧节奏已锁定：每帧至多一次 sim/physics step，sim 可降到 30Hz 且 render 逐帧出帧；`EnginePhasePipelineTests` 与 `EngineOverloadControllerTests` 覆盖降频/过载行为。
- [x] 过载降级已按热场、光照、远 chunk、sim 30Hz、接受低 fps 顺序下发质量档位。
- [x] 脚本服务后端已聚合 `IWorldAccess`、`IParticleService`、`IPhysicsService`、`IMaterialRegistry`、`ICamera`、`IInput`、`IEventBus`、`IAudioService`、`ISceneService`、`IDiagnostics`、`IRuntimeControlApi`，写操作经延迟命令队列落正确相位。
- [x] Play/Edit/Step 已接入 `EngineEditorPlaySessionService` 与 `EngineWorldSnapshotStore`，进入 Play 前快照、退出 Play 回滚、脚本生命周期重新派发。
- [x] GUI 宿主中性化已完成：`PixelEngine.Gui` 承载 `HexaImGuiBackend`、中性 `IGuiContext` 适配与 `GuiFontManager`；玩家包闭包不含 `PixelEngine.Editor.dll`，但允许玩家 HUD 所需 `Hexa.NET.ImGui`。
- [x] 窗口/GL 所有权解耦已完成：EditorShell 可先创建窗口和 GL context，Engine attach 外部窗口且 `Engine.Dispose()` 不销毁该窗口；`docs/runtime-reports/2026-07-06-editor-shell-attach-probe.md` 记录真实窗口 scripted probe。
- [x] `.scene` v2 writer 与运行时物化已完成：`SaveSceneDocument` 支持 `ParentId`、Transform TRS、`Vector2`、v1 兼容读取、稳定排序、运行时扁平 DOD 物化。
- [x] Game UI 装配已完成：`RenderPipeline.RegisterUiLayer` order=100/200、`GameUiPhaseDriver`、`UiInputRouter`、`IGameUiService`、`ui.*` 计时与禁用零开销均有 `PixelEngine.UI.Tests` / `PixelEngine.Hosting.Tests` 覆盖。
- [x] player startup 分派已完成：`DemoStartupOptionsTests` 与 `SceneAndHeadlessTests` 覆盖 `SceneFile`、`SaveDirectory`、`Procedural` 三路径。

## 4. M14 配置 DTO checklist

- [ ] `ProjectSettingsDto`：定义工程名、content root、script source dir、默认 scene、资源规则、编辑器偏好、默认 UI backend；提供默认值、schema version、读写、校验、路径逃逸拒绝与 EditorShell Project Settings 消费测试。
- [ ] `PlayerSettingsDto`：定义窗口标题、分辨率、VSync、图标、版本号、启动场景、输入默认、运行时 UI backend、发行通道；提供运行时消费、build-player 参数投影、EditorShell Player Settings 消费测试。
- [ ] `BuildProfileDto`：定义目标 RID/channel/configuration、R2R/AOT、Debug/Release、入包场景、启动场景、输出目录、符号、Build/Build And Run 参数；提供 build-player 同源映射与 EditorShell Build Settings 回读测试。
- [ ] `EngineProject` 读写/校验入口：将上述 DTO 与现有 content/scenes 扫描、`startup.json`、`.scene` loader、build-player 入参统一为一个中性 schema，禁止引入 EditorShell authoring UI 类型。

## 5. 未完成目标 checklist

- [ ] 将 Project Settings / Player Settings / Build Settings 的默认值、读写、校验与迁移纳入 Hosting 测试，避免 EditorShell 在壳内形成第二套 settings schema。
- [ ] 在 plan/19 中把 Settings 面板逐项绑定到本文件 DTO，保证 Unity-like Editor 的 Project / Player / Build Settings 与玩家包 build-player 同源。
- [ ] 在 plan/15 build-player 中消费 `BuildProfileDto` 或同源投影，保证 CLI 出包与 Editor Build Settings 参数一致。

## 6. 证据债 / 阻塞 checklist

- [!] Editor 真实窗口验收：仍需同一 `reviewSessionId` / `gitCommit` 的真实窗口视频、截图、交互 checklist 与人工复核；`scripted_probe_only`、`capture.bmp`、`manual_evidence_attached_pending_review` 均不能转为 [x]。
- [!] Native leak 证据：仍需 GL driver 级对象创建/销毁、OpenAL、Box2D、ALC 共同覆盖的 detector 报告；`process_smoke_only` 与 managed live-count 只能辅助定位。
- [!] 外部 runner：跨平台 native/resource leak 与真实窗口证据需要目标平台 runner 或人工环境；本机 win-x64 short run 不替代 M15。

## 7. 验证命令与证据路径 checklist

- [x] `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --filter FullyQualifiedName~HostingProjectDisciplineTests|FullyQualifiedName~EngineWindowOwnershipTests|FullyQualifiedName~EnginePhasePipelineTests` 覆盖依赖方向、窗口所有权、相位与降频。
- [x] `dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release --filter FullyQualifiedName~GameUiHostTests|FullyQualifiedName~UiInputRouterTests|FullyQualifiedName~GameUiServiceBridgeTests` 覆盖 Web-first UI 装配、输入仲裁、服务桥和禁用门控。
- [x] `dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --filter FullyQualifiedName~DemoStartupOptionsTests` 覆盖 player startup 分派。
- [x] `docs/runtime-reports/2026-07-02-demo-window-smoke.md`、`docs/runtime-reports/2026-07-02-demo-window-longrun.md`、`docs/runtime-reports/2026-07-06-editor-shell-attach-probe.md` 是现有 runtime / window / attach 证据路径。
- [!] `tools/demo-manual-acceptance-preflight.ps1` 与 `tools/native-leak-preflight.ps1` 只提供证据入口；未有合格 manifest 和人工/外部复核前保持阻塞。

## 8. 依赖与下一闭合节点 checklist

- [x] 上游依赖：plan/01、plan/02、plan/03–10、plan/11、plan/12、plan/15、plan/19、plan/20 的公开契约已经在 Hosting 中作为装配边界登记。
- [x] 下游消费：plan/19 使用窗口所有权、Edit/Play、`.scene` writer 与 Settings DTO；plan/20 使用 UI 装配和 `IGameUiService`；plan/13 使用 startup 分派和公开 runtime services；plan/15 使用 build profile / player-only 审计边界。
- [ ] 下一闭合节点：实现 `ProjectSettingsDto` / `PlayerSettingsDto` / `BuildProfileDto`，补测试并同步 plan/19 Settings UX checklist 与 plan/15 build-player 参数投影。
- [!] M15 后续节点：补 Editor UX 人工证据与 native leak 外部 detector 证据，完成后再更新 README/plan17 dashboard。
