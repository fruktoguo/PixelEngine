# Plan 20 — Web-first 透明 HTML UI Runtime（PixelEngine.UI）

> 产品依据：`../docs/PixelEngine-核心目标与产品定位.md`。本文件是 Web-first 透明 HTML UI / Web-first UI Runtime 状态账本，负责 `PixelEngine.UI`、ManagedFallback/RmlUi/Ultralight 三后端、same-window/same-GL 透明合成、C#↔UI 桥、输入三级仲裁、FontEngine、`content/ui`、native dynamic-only gate 与 M14/M15 UI 证据。
> 状态约定：`- [x]` 已有源码、测试、工具、报告或 plan 证据；`- [ ]` 未完成目标；`- [!]` 外部证据债、人工验收、硬件/native/发行/IME/真实窗口阻塞。本文不再使用进行中状态，所有部分完成事项拆成已完成子项与未完成/阻塞子项。

---

## 1. 当前产品职责

- [x] **Web-first UI Runtime**：`PixelEngine.UI` 以 HTML/CSS + data-model/action 契约描述玩家 UI 屏幕，并在同一窗口、同一 GL context 内透明叠加到像素世界之上。
- [x] **三后端统一抽象**：`IGameUiBackend` 统一 `ManagedFallbackBackend`、`RmlUiBackend` 与预留的 `UltralightBackend` optional profile 入口；当前 Ultralight 未激活且默认回退 `ManagedFallback`，上层 Demo / Hosting 通过同一 `IGameUiService` 消费。
- [x] **透明合成与输入仲裁**：UI 层注册 order=100，Editor ImGui overlay order=200；透明/非交互区域 pass-through，交互区域 capture，输入链路为 Editor → Web-first UI → Game。
- [x] **内容与字体管线**：`content/ui`、`ui-manifest.json`、screens/images/fonts、`FontEngine` 与 CJK fallback 是玩家 UI 内容的统一资产入口。
- [x] **非职责边界**：本文件不实现 CA/sim、plan/08 世界渲染管线本体、Editor ImGui 面板层、Demo 具体 UI 文案与玩法逻辑、发行脚本内部逻辑；这些由对应 leaf plan 承担。

## 2. 状态总览 checklist

- [x] `PixelEngine.UI` 骨架、`IGameUiBackend`、`UiValue`/`UiEvent`、`GameUiHost`、`UiDocumentManager`、`IGameUiService` Hosting bridge 与禁用零开销已落地。
- [x] `ManagedFallbackBackend` 纯托管基线已落地，并复用 `PixelEngine.Gui` 中性 ImGui host、`GuiFontManager`、玩家 HUD / `Behaviour.OnGui` 即时模式路径。
- [x] RmlUi desktop GL3 主路径已落地：vendored RmlUi/FreeType、dynamic-only `PixelEngine.UI.Native`、GL 函数注入、RmlUi GL3 renderer、DOM data-model/action、HitTest、图片 PNG→TGA 缓存、真实窗口 smoke 与 GL state restore 均有证据。
- [x] same-window/same-GL 合成底座已落地：`RenderPipeline.RegisterUiLayer`、`UiLayerCompositor order=100`、Editor overlay order=200、`GameUiPhaseDriver` 与 render cadence 解耦已实现。
- [x] FontEngine、CJK 子集资产、content/ui manifest、screens/images、ManagedFallback/RmlUi 图片消费与 UI 诊断计时已落地。
- [ ] `UltralightBackend` 真实后端未完成：仓库仍无 Ultralight native 依赖、surface API、JS bridge、字体注册、许可/发行 gate；当前只存在可复用 offscreen presenter 底座与显式回退。本轮已正式保留为未激活 optional profile，并补 release audit 不允许 Ultralight native 混入包的自动化门禁。
- [!] M14 透明 UI 产品验收未闭合：自动化合成/HitTest/GL restore 不能替代真实窗口 Demo 产品面视频与人工体验确认。
- [!] M15 RmlUi ANGLE/GLES native profile 未闭合：当前 native shim 是 desktop GL3 official renderer，不是 GLES3/ANGLE 双 profile。
- [!] M15 真实平台 IME composition 未闭合：committed text、`UiTextComposition` 抽象与 `UiTextCompositionCapabilities` 诊断已分离并注册，但真实平台 composition 事件、候选/预编辑可视化和后端一致性仍缺。
- [!] M15 UI native / Ultralight / release 证据未闭合：真实 release artifact、许可声明、codesign/notarize、Ultralight native gate 仍需外部证据。

## 3. 三后端状态矩阵

| 后端 | 当前状态 | 已有证据 | 未完成 / 阻塞 | 回退条件 |
|---|---|---|---|---|
| `ManagedFallbackBackend` | - [x] 纯托管基线，永远可用 | - [x] XHTML 子集、text/button/checkbox/progress/image、模型值、事件环形缓冲、Gui host 合帧、CJK 字体、HitTest、HUD 共栈与零 native | - [ ] 完整 CSS 盒模型不是基线目标，只需保持产品屏幕等价可用 | - [x] 默认 fallback；native 缺失、unsupported RID、AOT、Ultralight 未激活时使用 |
| `RmlUiBackend` desktop GL3 | - [x] 默认 HTML/CSS 子集主路径 | - [x] RmlUi 6.2 + FreeType 2.14.3、`PixelEngine.UI.Native` dynamic-only、GL3 renderer、字体注册、DOM model/action、真实窗口 smoke、GL state restore | - [!] ANGLE/GLES native profile 未完成；RmlUi 是 HTML/CSS 子集，不承诺标准 HTML5/JS 完整保真 | - [x] RmlUi native 不可用、GLES/ANGLE 未支持或加载失败时显式回退 ManagedFallback 并记录 `GameUiBackendSelection` |
| `RmlUiBackend` ANGLE/GLES | - [!] 阻塞 | - [x] `RmlUiNativeProfileGate` 已识别 `RenderBackend.GlEs30Angle`、GLES context 与 ANGLE renderer/vendor/version 并安全回退，避免误用 GL3 renderer | - [!] 需要 GLES3 renderer/loader、shader `#version 300 es`、同 context 函数表验证、状态恢复 smoke | - [x] 保持 ManagedFallback，并记录 `GameUiBackendSelection` / Console 可见诊断 |
| `UltralightBackend` | - [ ] 未实现真实后端；- [x] 未激活 optional profile 已门控 | - [x] `UiOffscreenSurfacePresenter` 已验证 BGRA8 dirty upload + textured quad 合成底座；Hosting 对 `UiBackendKind.Ultralight` 明确回退；Editor label / runtime diagnostic / release audit 均标注 inactive | - [ ] 需要 native 依赖、surface API、JS bridge、字体注册、`content/ui` resource loader、许可/发行 gate；M15 还需 artifact / notarize / license 证据 | - [x] 未激活或不合规时回退 ManagedFallback，不伪造后端完成；release audit 不允许 Ultralight native 混入 |

## 4. 已实现证据 checklist

- [x] 模块依赖方向已锁定：`UI → {Gui, Rendering, Core}`；UI 不引用 Editor/Scripting/Simulation/Physics/World/Content/Serialization/Audio/Demo，脚本可见契约由 Scripting 声明、Hosting bridge 实现。
- [x] `IGameUiService` 已通过 Hosting `GameUiServiceBridge` 挂到 `EngineContext.GameUi`，支持 Show/Hide/PushModal/BindModel/SetValue/TryGetValue/UiEventRaised/Invoke；禁用 UI 时返回 no-op 服务。
- [x] `GameUiHost` / `UiDocumentManager` 已管理屏栈、显隐、模态、返回栈、预载和后端 dispatch。
- [x] `ManagedFallbackBackend` 已将 `content/ui` 抽象控件树映射到 `PixelEngine.Gui` 中性 host；与 Demo HUD、PauseMenu、PlayableHud、`Behaviour.OnGui` 共用同一 GUI 路径，不另立平行绘制 API。
- [x] RmlUi native 已使用 `[LibraryImport]` resolver、native handle 生命周期、renderer/context/document/update/render、font registration、mouse/keyboard/text 输入、DOM HitTest、model set/get/copy、action invoke 与 event drain。
- [x] `RenderPipeline.RegisterUiLayer(int order, IUiCompositeLayer)` 已成为显式 UI 层排序机制；game UI order=100，Editor ImGui order=200，替代多播订阅顺序。
- [x] `UiLayerCompositor` 和 native shim 已覆盖 GL 状态保存/恢复，包含 framebuffer/program/VAO/VBO/EBO/active texture/texture2D/blend/scissor/depth/cull/viewport/unpack alignment 等关键状态。
- [x] `GameUiPhaseDriver` 已使用 render cadence dt 推进 UI update、model bridge、event drain；sim 降频或 TimeScale 不会让 UI 动画追帧、卡顿或重复消费。
- [x] `UiInputRouter` 已接入 Hosting 窗口输入、key/button/scroll/committed text、HitTest capture、上游 Editor capture 门、失焦清理、文本队列 drain、控制字符过滤和稳态零分配。
- [x] `UiTextComposition` / `IUiInputSource.CaptureTextComposition` / `IGameUiBackend.FeedTextComposition` 已建立 committed text 与 composition 预编辑状态的抽象边界；`UiTextCompositionCapabilities` 已把真实平台 IME composition 能力诊断注册进 Hosting service；当前 Silk KeyChar 路径明确只返回 inactive composition，并报告 M15 真实平台 IME 仍阻塞。
- [x] `GameUiModelBridge` 已按当前文档声明 path 推送 `IUiModel`，RmlUi 官方 `DataModelConstructor` 已支持 Empty/Boolean/Int64/Double 标量、dotted path、重复 path 去重和稳定变量名映射。
- [x] `UiDiagnostics` 已接入 `ui.update` / `ui.paint` / `ui.upload` / `ui.composite`，并进入 `FrameSubPhase`、`EngineCounters` 与 Editor 性能 HUD。
- [x] `GameUiAllocationBenchmarks` 已覆盖静态 UI phase、clean composite/draw skip 与空闲输入泵，ShortRun `MemoryDiagnoser` 报告稳态 `Allocated == 0 B`。
- [x] `UiDirtyRectCollector`、`UiOverlayTexture`、`UiPresentContext.UploadOverlayTexture` 与 `UiOffscreenSurfacePresenter` 已提供离屏 surface dirty upload + textured quad 合成底座。
- [x] `FontEngine` 已选择 `content/ui/fonts` 优先、共享 `GuiFontManager` 系统候选、CJK glyph range、DPI 字号、missing glyph 诊断；RmlUi/ManagedFallback 可消费同一字体选择。
- [x] `content/ui/ui-manifest.json` 已支持 screen id、preload、images 清单、路径逃逸拒绝、缺失文件校验、预载 screen/image；Demo 已落五类屏幕并由 `GameUiDemoController` 驱动。
- [x] Demo Web-first HUD 已接入真实任务与武器状态：`GameUiDemoController.OnUpdate` 通过脚本公开 `IGameUiService.SetValue` 发布生命、武器槽位、弹药、冷却/热量、水晶进度、剩余时间、水位危险度与分数；`hud.xhtml` 使用 numeric model paths 与稳定中文 label，保留 `PlayableHud`/`IGuiContext` fallback 诊断路径，且不把未完成的 RmlUi 字符串池能力或真实窗口产品验收标为完成。
- [x] UI native packaging 已采用 dynamic-only：`PixelEngine.UiNative.targets`、CMake `SHARED`、`runtimes/<rid>/native/`、R2R 包含 UI native、AOT 不携带动态 UI native 并回退 ManagedFallback 的审计测试已落地。
- [x] `RmlUiNativeProfileGate` 已集中收紧 RmlUi desktop GL3 profile gate：显式 ANGLE/GLES backend request、GLES context、ANGLE renderer/vendor/version 全部拒绝加载 GL3 renderer 并回退 ManagedFallback；`RmlUiNativeProfileGateTests`、`EngineBuilderTests`、`EditorConsoleStoreTests` 与 `HostingProjectDisciplineTests.RmlUiAngleGlesProfileGateFallsBackAndDoesNotMarkM15Complete` 锁定 fallback reason、Console 可见诊断与 M15 不误勾。

## 5. M14 产品验收 checklist

- [!] **透明 UI 产品面**：HUD/menu/settings 必须在真实窗口中证明透明区域透出世界、alpha blend 正确、非交互透明区 pass-through、交互元素 capture、Editor overlay 盖在 game UI 上；当前自动化合成/HitTest/GL restore 已有证据，但仍缺真实窗口 Demo 产品面视频和人工体验确认。
- [x] **HTML 屏幕覆盖**：主菜单、设置、背包、对话、HUD 已有 `content/ui` 屏幕与 manifest，ManagedFallback 与 RmlUi 能显示/交互，中文字体可显示。
- [x] **C#↔UI 事件链路**：UI 事件经 `UiEvent` → `ScriptEventBus` → 脚本相位 drain，世界写入通过延迟队列落正确相位；禁用 UI 时 no-op 安全。
- [!] **输入三级仲裁**：Editor → Web-first UI → Game 的上游捕获门、committed text、RmlUi/ManagedFallback HitTest 已实现；真实平台 IME composition、候选/预编辑可视化、Ultralight HitTest 尚未完成，不能把 KeyChar 或 committed text 冒充 IME。
- [x] **UI cadence**：UI update 使用 render dt，sim 降到 30Hz 时 UI 仍按渲染 cadence 推进；过载只降低 present cadence，不跳过 update/event drain。
- [x] **事件驱动重绘**：ManagedFallback/RmlUi dirty/animation 门控已实现，静态屏 `ui.paint=0`、HUD 数值变化重绘、dirty rect upload 底座均有测试证据。
- [x] **降级路线**：禁用 UI 时不注册 `GameUiHost`/driver/service；RmlUi 不可用、GLES/ANGLE 未支持、Ultralight 未激活时显式回退 ManagedFallback 并记录原因，不伪造后端。
- [ ] **Demo 产品面打磨**：plan/13 仍需用本 runtime 完成可发行 HUD/menu/settings/pause/result 体验、输入手感和真实窗口路线；本文件只提供 runtime 能力。

## 6. M15 native / IME / Ultralight / release 证据 checklist

- [!] **RmlUi ANGLE/GLES profile**：需要真实 GLES3 renderer/loader、shader profile、同一 GL/ANGLE context 函数表注入、状态恢复与真实窗口 smoke；当前 GL3 renderer 不得标为双 profile 完成。
- [!] **真实平台 IME composition**：需要 Windows/macOS/Linux 目标输入后端提供 composition start/update/commit/cancel、候选/预编辑可视化、focus/capture 清理与 RmlUi/ManagedFallback/Ultralight 一致性；KeyChar/committed text 只能算提交文本。
- [x] **Ultralight optional profile inactive gate**：`UltralightOptionalProfileGate` 集中声明默认未激活、回退 `ManagedFallback` 与缺失 native SDK/provenance、commercial redistribution license、runtime surface/JS bridge、RID native binaries、SHA256/NOTICE、codesign/notarize、release artifact evidence 的可见诊断；Editor Settings、Hosting fallback、package NOTICE 与 release audit 已同源表达。
- [ ] **UltralightBackend 本体**：实现 native loading、surface creation、dirty rect pull、BGRA8 upload、JS global bridge、DOM HitTest、font/resource loader、lifetime dispose 与 no-op/fallback gate。
- [!] **Ultralight 许可与发行**：需要许可条款、商业阈值、redistribution 说明、native DLL/so/dylib provenance、SHA256、release artifact、codesign/notarize（macOS 激活后）证据；未满足前发行审计必须拒绝 Ultralight native 混入，不能把 native 文件出现当作 M15 闭合。
- [!] **UI native release artifact**：需要 win-x64/win-arm64 active R2R release artifact 证据、AOT fallback artifact 证据、`SHA256SUMS`、license README、GitHub release / workflow 同源报告；`workflow_dispatch`、`load-only`、`pending_review` 不能作为完成。
- [!] **真实窗口产品体验**：需要 Demo UI 产品面视频、人工体验 checklist 与复核结论；`scripted_probe_only`、短跑截图或 GL smoke 只能作为辅助证据。

## 7. 验证命令与证据路径 checklist

- [x] `dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release --filter FullyQualifiedName~GameUiHostTests|FullyQualifiedName~UiInputRouterTests|FullyQualifiedName~GameUiServiceBridgeTests|FullyQualifiedName~BackendConformance` 覆盖宿主、输入、服务桥与后端一致性基线；`UiInputRouterTests` 额外锁定 committed text 不冒充 IME composition，以及 `UiTextCompositionCapabilities` 从输入源透传诊断。
- [x] `dotnet test tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj -c Release --filter FullyQualifiedName~RmlUiGlBootstrapSmokeTests|FullyQualifiedName~RmlUiNativeProfileGateTests|FullyQualifiedName~UiOffscreenSurfacePresenterSmokeTests|FullyQualifiedName~ManagedFallbackBackendTests` 覆盖 RmlUi GL3 smoke、ANGLE/GLES profile gate、offscreen upload 底座与 ManagedFallback。
- [x] `dotnet test tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj -c Release --filter FullyQualifiedName~GameUi|FullyQualifiedName~InputArbitrator|FullyQualifiedName~DisabledGameUi` 覆盖 Hosting 装配、输入仲裁与禁用零开销。
- [x] `dotnet test tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj -c Release --filter FullyQualifiedName~DemoUiContentTests|FullyQualifiedName~GameUiDemoController` 覆盖 Demo content/ui 与公开 API dogfood。
- [x] `dotnet run -c Release --project bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj -- --filter *GameUiAllocationBenchmarks*` 是 UI 稳态零分配基准入口。
- [x] `tools/audit-release-artifacts.ps1` / `.sh` 包含 UI native dynamic-only 与 R2R/AOT fallback 审计断言，并在 Ultralight optional profile inactive 时拒绝 `Ultralight` / `WebCore` / `AppCore` native 混入 package 或 publish 产物。
- [!] `tools/demo-manual-acceptance-preflight.ps1`、release evidence preflight 与 native leak / codesign 相关工具只提供证据入口；未有合格 manifest、真实平台材料和人工/外部复核前保持阻塞。

## 8. 依赖与下一闭合节点 checklist

- [x] 上游依赖：plan/00 技术栈与 native gate、plan/08 `RegisterUiLayer` / GL context / offscreen upload、plan/18 Hosting phase driver / `IGameUiService` bridge、plan/19 `PixelEngine.Gui` / `GuiFontManager`、plan/11 脚本契约均已作为 UI runtime 边界登记。
- [x] 下游消费：plan/13 使用 `content/ui` 与 `IGameUiService` 做 Showcase Demo UI；plan/19 EditorShell 通过 order=200 盖在 game UI 上；plan/15 打包 UI native 与 fallback；plan/14 维护后端一致性、smoke、benchmark；plan/17 只登记 M14/M15 exit gate。
- [x] 闭合节点：正式保留 `UltralightBackend` 为未激活 optional profile，并补 runtime fallback、可见诊断、license/release gate 说明与 release audit 不允许 Ultralight native 混入的自动化测试。
- [ ] 下一闭合节点：若要激活 Ultralight，必须先实现真 `UltralightBackend`、native SDK/provenance、commercial redistribution license、surface/JS bridge、RID binaries、SHA256/NOTICE、codesign/notarize 与 release artifact evidence。
- [!] 下一闭合节点：补 RmlUi ANGLE/GLES native profile 设计与真实窗口 smoke，未完成前必须继续由 ManagedFallback 回退承接。
- [!] 下一闭合节点：接入真实平台 IME composition 事件与预编辑 UI，完成三后端一致性验证；不得用 KeyChar/committed text 替代。
- [!] 下一闭合节点：补透明 UI Demo 产品面真实窗口视频、人工体验 checklist、release/native 证据后，再同步 README/plan17 dashboard。
