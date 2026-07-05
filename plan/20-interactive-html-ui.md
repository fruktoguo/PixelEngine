# Plan 20 — 交互界面 / HTML UI 模块（PixelEngine.UI）

> 本文档定义 `PixelEngine.UI` 子系统：把**游戏内大 UI**（主菜单、设置、背包、对话、HUD、加载/暂停/结算等"屏"）改用 **HTML/CSS + 数据绑定/脚本** 编写，让 UI 可直接手写、便于 AI 生成，与像素世界在同一 OpenGL 上下文内合成。**明确不动**：CA/sim、plan/08 渲染管线、plan/12 编辑器 ImGui —— 编辑器/调试/管理 UI 仍是 Dear ImGui，本模块只覆盖"面向玩家的大 UI"。
> 权威依据：`../AGENTS.md`（§1 不变式、§2 一步到位）；`00-conventions-and-techstack.md`（§4 选型表、§4.1 门控依赖）；`08-rendering.md`（§3.7 合成顺序、UI 层注册接口）；`11-scripting-system.md`（契约-后端范式、`IWorldEffects` 同款）；`12-editor-tooling-ui.md`（§3.3 WantCapture 仲裁）；`18-hosting-runtime.md`（12 相位、EngineContext 服务后端）；`19-standalone-editor-app.md`（GUI 宿主中性化重构，本模块字体/回退复用其成果）。
> 里程碑归属：**M14「玩法深化与交互 UI」**（与 demo-playability 横切同批）。
> 状态：`- [ ]` 未开始 / `- [x]` 完成并自测 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。

---

## 1. 目标与范围

### 1.1 可行性结论（先回答"可行吗、性能与兼容如何"）

- **可行**，且**默认发行零新增 native 风险**：本模块以三后端分层实现——`ManagedFallbackBackend`（纯托管基线，复用 plan/19 中性化后的 `PixelEngine.Gui` ImGui host，零新增 native）为**永远可用的底座**；`RmlUi`（HTML/CSS 子集，单一 dynamic native）为**主后端**；`Ultralight`（标准 HTML5/CSS3/JS 高保真，离屏 bitmap）为**可选门控 profile**。技术上唯一需要打通的是"HTML 核产出的像素/几何 → 复用 plan/08 的同一 GL 上下文合成到世界之上"，plan/08 为此提供**显式带序号的 UI 层注册接口**（§3.2），本模块注册一层即可，无需另起窗口/进程/GL 上下文。
- **性能**：大 UI 是"低频重绘 + 稀疏透明"场景，采用**事件驱动重绘**（仅 DOM/数据变化或动画帧才重绘）+ **脏矩形上传** + **一次全屏 alpha 混合合成**，静态菜单稳态每帧近零成本，HUD 只在数值变化时局部重绘。UI 光栅化在渲染线程相位 10 内、只在"脏"时执行；因不变式 #6（不追帧），即便 UI 光栅化偶发尖刺也只掉渲染帧，**绝不拖累固定步长 sim**（sim 在相位 3–8 已先跑完）。
- **兼容性**：合成走 OpenGL 3.3 Core 必备能力（纹理/FBO/alpha blend）或纯几何绘制，天然跟随 plan/08 的桌面 GL / ANGLE(ES3) 双路径。**Windows 优先**（plan/15 修订）：RmlUi native 首期仅构建 `win-x64`（可含 `win-arm64`），其余四 RID 保留矩阵位、dormant；未激活 RID 与 AOT 通道**回退 `ManagedFallbackBackend`**（不静态链 native、运行时不 P/Invoke UI 核），保证任何 RID 上大 UI 都能显示。真正的兼容风险集中在**native 核本身的跨平台构建与许可**（见 §5 候选评估、§7 不变式冲突），已用"基线纯托管 + 主后端 dynamic-only 门控"消解。

### 1.2 范围内

HTML/CSS UI 渲染核封装（`IGameUiBackend` 抽象 + 三个后端：`ManagedFallbackBackend`/`RmlUiBackend`/`UltralightBackend`）、UI 文档/屏管理、C#↔UI 双向数据/事件桥、输入路由与 WantCapture 仲裁、与 plan/08 的 UI 层注册合成、与 plan/18 的相位挂载、独立 `FontEngine`（含 CJK）、诊断/降级接入、内容资产（HTML/CSS/JS/字体/图集）管线、契约 `IGameUiService` 公开给脚本/Demo。

### 1.3 范围外

- 编辑器/调试/管理 UI（仍是 plan/12 的 ImGui，一律不改）。
- sim/材质/物理/渲染管线逻辑（本模块只**消费** plan/08 的 GL 与 UI 层注册、**消费** EngineContext 服务读游戏态）。
- 具体 Demo 的 UI 内容（在 plan/13，本模块只提供 API 与后端）。
- GUI 宿主中性化重构本身（`PixelEngine.Gui` 程序集的抽取、`HexaImGuiBackend`/`GuiFontManager`/中性 `IGuiContext` 运行时适配下沉）由 **plan/19 M13 入口门**交付；本模块**依赖其成果**并复用之，不重复实现。

### 1.4 锁定决策（本文档核心，不可偏离）

- 游戏大 UI = **HTML/CSS 渲染核，与 plan/08 共享同一 OpenGL 上下文**，经 plan/08 的**显式带序号 UI 层注册接口**（§3.2）在相位 10 合成于世界之上、编辑器 ImGui 之下；**绝不另起进程/另开窗口/另建 GL 上下文**（与 plan/12 对 ImGui 的同款约束一致）。
- **三后端分层锁定**（`IGameUiBackend` 可切换）：
  - **`ManagedFallbackBackend`（纯托管基线，永远可用）**：不引任何新 native，构建于 plan/19 中性化后的 `PixelEngine.Gui`（`HexaImGuiBackend` + `GuiFontManager` CJK 字体栈）之上。它**统一现有玩家 GUI facade** —— `Behaviour.OnGui(IGuiContext)`、`ScriptGuiContext`（现于 Editor，plan/19 下沉 Gui）、plan/13 §3.12 `DemoHud`/`PauseMenu`/`PlayableHud` —— 使之与本模块共享**同一个 Gui host 实例**，而**不另立平行绘制 API**（§3.1、§3.11）。
  - **`RmlUiBackend`（主后端，默认发行的 HTML 路径）**：vendored RmlUi（RML/RCSS = **HTML/CSS 子集**）+ 静态链 FreeType，打包为单一 dynamic `PixelEngine.UI.Native`，bring-your-own-renderer 直接复用同一 GL 上下文绘几何。首期仅 `win-x64`（可含 `win-arm64`）。
  - **`UltralightBackend`（可选门控 profile）**：标准 HTML5/CSS3/JS 高保真，离屏 BGRA8→纹理→全屏 alpha quad。受商业许可（<$100K）、WebKit-fork 原生 DLL 体积、AOT 绑定不确定性约束，默认不发。
- **顶层显式决策点（不得混淆卖点，见 consolidatedOpenQuestions #2）**：`RmlUi` 是 HTML/CSS **子集**（RML/RCSS，JS 受限→data-model + 事件），**不能忠实渲染"AI 生成的标准 HTML5/CSS3/JS"页面**。因此：（a）"AI 直接生成标准 HTML 页面"这一诉求，**只能走 `Ultralight` 可选 profile**，不得以"默认 RmlUi"同时声称满足；（b）若 Demo 大 UI 需在不含任何 native 的基线发行里可用，其内容需能落到 `ManagedFallbackBackend`（ImGui 皮肤化）等价实现（plan/13 承担）。这一取舍在本文档顶层显式记账，而非藏在风险段。
- native 核（RmlUi/Ultralight）**默认以 dynamic-only 分发**（与 OpenAL/ANGLE 同级），**不参与 Box2D 的 static+dynamic dual-build**，并登记为 plan/00 §4.1 **门控依赖**；`ManagedFallbackBackend` 作为**无 native 的基线**保证默认"广兼容基线"发行不含任何 HTML native 也能用。据此**不变式 #10 的修订（把 HTML UI native 核归为门控类、明确 dual-build 仅锁 Box2D）已获批准**（§7）。
- 依赖方向锁定：`UI → { Gui, Rendering, Core }`；**禁止 `UI → Editor`**、禁止 `UI → Scripting/Simulation/Physics/Demo`（契约用 plan/11 契约-后端范式在 Scripting 声明、由 Hosting 桥接，§2）。

---

## 2. 技术栈与依赖

- 语言/运行时：`.NET 10 / C# 14`，`Nullable enable`，file-scoped namespace，根命名空间 `PixelEngine.UI`。**仅** native 互操作与纹理上传所需最小 unsafe 块开 `AllowUnsafeBlocks`（对齐 AGENTS §4 与 plan/12 Editor 同类受限措辞：unsafe 面收敛到 P/Invoke marshalling 与 `glTexSubImage2D` 上传，不外溢到逻辑层）。
- GL/窗口/输入：复用 plan/08 的 `Silk.NET.OpenGL` 同一 `GL` 实例与 `IInputContext`（不新增窗口后端）。
- 中性 GUI 宿主：依赖 plan/19 交付的 `PixelEngine.Gui`（`HexaImGuiBackend`、中性 `IGuiContext` 运行时适配、`GuiFontManager`）。`ManagedFallbackBackend` 完全构建其上，不再依赖 `PixelEngine.Editor`。
- native 互操作：**仅 `[LibraryImport]` 源生成**（禁新 `DllImport`，遵 AGENTS §3）；回调（UI 事件/数据 getter/字体/渲染接口）用 `[UnmanagedCallersOnly]`，AOT 友好（复用 plan/06 Box2D task 桥同款范式）。
- 序列化/资源：UI 资源（HTML/CSS/JS/字体/图集）走 `content/ui/`，加载器纯 I/O + `System.Text.Json` 源生成读清单，不引入反射回退（trim 友好）。
- 诊断：向 `PixelEngine.Core` 诊断/计时器注册 `ui.update`/`ui.paint`/`ui.upload`/`ui.composite` 分项耗时（供 plan/12 HUD 与 plan/18 降级）。
- **依赖方向**（plan/00 §5，绝不反向）：`UI → { Gui, Rendering, Core }`（读中性 Gui host/字体、共享 GL/输入、诊断）。`Hosting → UI`（装配、注入游戏态模型、挂相位、桥接契约）。`Demo → Hosting (+可选 UI)`。UI 与 Editor 是**同级消费者且互不依赖**；游戏态经 Hosting 在装配期以模型绑定注入。
- **契约所在程序集（plan/11 契约-后端范式，确保 Scripting/脚本不反向依赖 UI）**：`IGameUiService` 抽象与 `UiValue`/`UiEvent`/`UiScreenHandle` 等脚本可见契约**声明在 `PixelEngine.Scripting`**（与 `IWorldEffects`（`src/PixelEngine.Scripting/ScriptFacades.cs`）同栈同款），**后端实现由 Hosting 提供**：Hosting 持有 `GameUiHost` 并实现 `IGameUiService` 转发之。UI 程序集**不实现** `IGameUiService`、不引用 Scripting —— UI 只暴露具体 `GameUiHost` 供 Hosting 桥接。由此 Scripting/Demo 通过 `EngineContext.GameUi`（`IGameUiService`）驱动 UI，而无一条 `Scripting/Demo → UI` 或 `UI → Scripting` 的边。
- native 核 profile（§5 推荐）：主 **RmlUi**（vendored，静态链 FreeType 进单一 `PixelEngine.UI.Native` 动态库）；可选 **Ultralight**（离屏 bitmap→纹理）。均登记 plan/00 §4.1、dynamic-only；`ManagedFallbackBackend` 无 native。

---

## 3. 详细设计

### 3.1 模块与类型总览

`GameUiHost`（顶层门面，由 Hosting 在启用大 UI 时装配）持有：`IGameUiBackend`（可切换后端）、`UiDocumentManager`（屏/文档栈与显隐）、`UiInputRouter`（Silk.NET 输入 → 后端 + 计算 WantCapture）、`UiModelBridge`（游戏态↔UI 数据/事件）、`UiLayerCompositor`（向 plan/08 注册 UI 层，§3.2）、`FontEngine`（字体/CJK，§3.7）、`UiDiagnostics`（分项计时）。

`IGameUiBackend`（后端抽象，统一"直接几何绘制""离屏 bitmap blit""纯托管 ImGui"三种合成形态）关键成员：`Initialize(width,height,dpiScale)`、`Resize`、`LoadDocument(UiDocumentSource)→UiDocumentHandle`、`UnloadDocument`、`Update(deltaSeconds)`、`bool IsDirty`（事件驱动重绘核心：clean 时可跳过 Paint/Composite）、`bool IsAnimating`、`FeedPointerMove/FeedPointerButton/FeedScroll/FeedKey/FeedText`（输入注入）、`UiHitResult HitTest(x,y)`（光标下是否为可交互/不透明 UI，决定是否吞输入）、`SetModelValue/TryGetModelValue`（数据桥）、`int DrainEvents(Span<UiEvent>)`（拉取 UI→游戏 事件，零分配环形缓冲）、`Composite(GL gl, in UiViewport viewport)`（在共享 GL 上下文、当前默认 framebuffer 上绘制自身并恢复 GL 状态）。

三后端在 `Composite` 内形态不同、契约一致：

- **`ManagedFallbackBackend`（纯托管基线）**：不含 native、不含离屏纹理。它把当前屏栈的语义 UI 树（`UiDocumentManager` 保有的按钮/文本/列表/进度条等抽象控件，源自 HTML→抽象控件的托管布局器 `ManagedUiLayout`）映射为 **plan/19 `PixelEngine.Gui` 中性 host 的即时模式绘制调用**。它与既有玩家 HUD 复用**同一个 Gui host 实例**：`Behaviour.OnGui(IGuiContext)`、`DemoHud`/`PauseMenu`/`PlayableHud` 走的是同一路径，`ManagedFallbackBackend` 只是把"屏管理/data-model 事件"接到该 host 上，**不新建平行绘制 API**。`Composite` 即让该 Gui host 在共享 GL 上下文提交其 draw data（Hexa.NET.ImGui，允许进玩家包，§7）。CJK 走 `GuiFontManager`（§3.7）。此后端**永远可用**，是 AOT/未激活 RID/门控关闭时的落点。
- **`RmlUiBackend`（主后端，几何路径）**：native shim 实现 RmlUi 的 `RenderInterface`（在 C++ 侧复用当前 GL 上下文直接 `glDrawElements`，避免逐 draw-call 跨托管回调），`Composite` 让 shim 用共享上下文重放已编译几何（`CompileGeometry`），**无离屏纹理、无 PBO 上传**，静态 UI = 少量缓存 draw call。字体走 shim 内静态链 FreeType；CJK 字体资产由 `FontEngine` 供给（§3.7）。GL 函数加载与状态 save/restore 协议见 §3.2。
- **`UltralightBackend`（可选门控，离屏路径）**：native 核 CPU 渲染到离屏 BGRA8 位图（或 GPU driver），`Composite` 把**脏矩形**经 `glTexSubImage2D` 上传到一张 `GlTexture`（复用 plan/08 §3.3 脏矩形子上传技术），再画一个**全屏 alpha 混合四边形**。

`UiValue` 是 blittable 联合（bool/double/int64/字符串句柄），`UiEvent { UiDocumentHandle Doc; int ElementIdHash; UiActionId Action; UiValue Payload; }`，全部零 GC。

### 3.2 与 plan/08 的合成集成（显式 UI 层注册，替代多播订阅顺序）

**plan/08 新增显式带序号/优先级的 UI 层注册接口**（本模块与 plan/08 的硬顺序前置，见 §8）：`RenderPipeline.RegisterUiLayer(int order, IUiCompositeLayer layer)`（`IUiCompositeLayer.Composite(GL gl, in UiViewport viewport)`），管线在世界 blit + 光照 + bloom + dither + gamma + overlay 之后、`SwapBuffers` 之前，Viewport 复位为窗口尺寸后，**按 `order` 升序**回放已注册 UI 层。这**取代**原设计"靠多播 `BeforePresentUi` 订阅先后隐式决定叠放"的脆弱做法——叠放由显式序号决定，与装配代码书写顺序无关：

- 游戏 HTML UI 层 `order = 100`（`UiLayerCompositor`）。
- 编辑器 ImGui 层 `order = 200`（`EditorRenderBridge`）——编辑器是开发者工具，恒盖在游戏 UI 之上。

叠放次序：**世界 → 游戏大 UI(100) → 编辑器 ImGui(200，若启用)**。发行构建禁用编辑器时，大 UI 即最顶层 UI。

**GL 状态 save/restore 协议（跨托管-原生边界，强制）**：任一 UI 层 `Composite` 进入时保存、退出时精确恢复 `GL_CURRENT_PROGRAM`、绑定的 VAO/VBO/EBO、`GL_ACTIVE_TEXTURE` 与各单元纹理绑定、`GL_BLEND`/blend func/equation、`GL_SCISSOR_TEST`/scissor box、`GL_VIEWPORT`、`GL_DEPTH_TEST`/`GL_CULL_FACE`、`glPixelStorei(GL_UNPACK_ALIGNMENT)`——与 `ImGuiController` 同款纪律，避免污染下一帧世界渲染与相邻 UI 层。RmlUi shim 在 C++ 侧同样遵守此协议（进 `RenderInterface` 前 push、出后 pop），并**只在本层 scope 内改状态**。合成启用 alpha blend（`GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA`）使 UI 透明区透出世界。

**RmlUi C++ shim 的 GL 函数加载（桌面 GL 与 ANGLE/GLES3 两路，不以一句 glDrawElements 带过）**：shim **不自带 GL loader、不 `dlopen` 系统 GL**，而是由托管侧在 `Initialize` 时把 plan/08 现有 `GL`（Silk.NET）已解析的**函数指针表**（`glGenBuffers`/`glBindBuffer`/`glBufferData`/`glDrawElements`/`glGenTextures`/`glTexImage2D`/`glTexSubImage2D`/`glUseProgram`/`glUniform*`/`glGenVertexArrays`/`glVertexAttribPointer`/`glEnable`/`glBlendFunc`/`glScissor` 等最小子集）以结构体经 `[LibraryImport]` 传入 shim，shim 只经这些指针调用——从而**桌面 GL 与 ANGLE(GLES3) 走同一份由 plan/08 选定的入口**（谁激活哪条路径由 plan/08 决定，shim 不感知），保证与世界渲染共用同一上下文/同一 loader，杜绝"shim 拿到另一份 GL 入口"的隐蔽 bug。着色器按 GL 3.3 Core / GLES3 双 profile 各备一份（`#version 330 core` / `#version 300 es`），由传入的 profile 标志选择。

**事件驱动重绘**：`UiLayerCompositor` 仅在 `backend.IsDirty || backend.IsAnimating` 时执行光栅化/上传；否则 RmlUi 后端重放缓存几何（廉价 draw）、Ultralight 后端复用上帧纹理重画四边形（廉价）、ManagedFallback 后端重放上帧 draw data。静态菜单稳态无光栅化成本。

### 3.3 相位挂载（Hosting，plan/18）

不新增相位，复用现有 12 相位（不变式无关）：

- **相位 [0/1] Input & Game Logic**：`UiInputRouter.PumpInto(backend)`（把本帧 Silk.NET 输入喂后端）→ `UiModelBridge.PushGameStateToUi()`（游戏态→UI 模型，读 EngineContext 只读快照）→ `backend.DrainEvents` 派发 UI→游戏 事件到已注册处理器（经 plan/11 相位安全写入队列，写落在正确相位）→ `backend.Update(dt)`（推进布局/动画/UI 脚本）。
- **相位 [10] GPU Upload & Render**：经 plan/08 UI 层回放由 `UiLayerCompositor.Composite` 执行（§3.2），仅在脏时光栅化/上传。

Hosting 新增 `GameUiPhaseDriver`（挂相位 0/1 的 UI 逻辑）与在渲染装配处 `RegisterUiLayer(100, uiLayerCompositor)`。`EngineBuilder` 增 `EnableGameUi(bool)` 与后端选择 `UseUiBackend(UiBackendKind)`（`ManagedFallback`/`RmlUi`/`Ultralight`）；禁用时零开销（不注册层、不装配 driver）。

**UI cadence 与 sim 频率解耦的显式闭合（回应 §3.8）**：UI `Update(dt)` 的 `dt` **恒为渲染帧 dt（render cadence）**，与固定步长 sim 频率无关。当 plan/18 §4.3 过载降级把 sim 步进降到 30Hz（丢的是 sim 追帧，不是渲染帧）时，渲染循环仍按显示刷新率跑，`GameUiPhaseDriver` 每个**渲染帧**推进一次 `UiModelBridge`（读最新一次已完成 sim 的快照）与 `backend.Update(renderDt)`——**UI 动画不因 sim 降频而卡顿或加倍**。反向地，若渲染帧率本身被降（plan/18 第二级降级候选可"降 UI 重绘频率/停 UI 动画"），则 UI cadence 随渲染帧率下降，但仍单调、无追帧（守 #6）。此规则写死在 `GameUiPhaseDriver`：UI 只认渲染 dt，绝不消费 sim 的 accumulator/substep 计数。

### 3.4 C#↔UI 双向通信（`UiModelBridge` + `IGameUiService`）

对脚本/Demo 暴露 `IGameUiService`（声明在 Scripting、由 Hosting 实现并挂 `EngineContext.GameUi`，§2）关键成员：`ShowScreen(screenId)→UiScreenHandle`（加载并显示一屏 `content/ui/<id>.html`）、`HideScreen`、`PushModal(screenId)`（叠加模态，如设置浮层）、`BindModel(screen,modelName,IUiModel)`（绑定视图模型）、`SetValue(screen,path,UiValue)`（游戏态→UI）、`TryGetValue(screen,path,out UiValue)`（UI→游戏 读回）、`event Action<UiEvent> UiEventRaised`（UI→游戏 事件，在相位 1 派发）、`Invoke(screen,action,UiValue)`（C#→UI 主动调用）。

映射到三后端：
- **RmlUi**：`BindModel` → RmlUi data-model（`Rml::DataModelConstructor`，两地同步）；HTML 里 `data-event-click="start_game"`/`data-model` 绑定；UI→游戏 事件经 shim 的 `[UnmanagedCallersOnly]` EventListener 回调，入 `UiEvent` 环形缓冲，相位 1 派发；`Invoke`/`SetValue` 改 data-model 值，下次 `Update` 反映。
- **Ultralight**：`BindModel` → 一个 JS 全局对象（`engine`），`SetValue` 调 JS setter，UI 里 `engine.emit('start_game', payload)` 经注册的 C# JS 函数回调入同一 `UiEvent` 缓冲；`Invoke` 经 `EvaluateScript`/调用 JS 函数。
- **ManagedFallback**：`BindModel` → 托管字典视图模型；`ManagedUiLayout` 的控件按 `data-event-*` 元数据在被点击时直接入同一 `UiEvent` 缓冲；`SetValue`/`Invoke` 改托管模型，下次 `Update` 布局反映。三后端对上层是**同一套 `UiEvent`/`UiValue` 语义**，切后端不改 Demo 逻辑。

**相位安全**：所有 UI→游戏 的世界写入（如"开始游戏""加载存档"触发世界快照）**不在回调里直接改世界**，而是入 plan/11 的延迟命令队列，在正确相位 flush（复用 plan/18 §3.4 已建机制）。游戏→UI 只读游戏态快照，无竞争。

### 3.5 输入路由与 WantCapture 仲裁（`UiInputRouter`）

复刻 plan/12 的 `WantCaptureMouse/Keyboard` 范式，形成三级仲裁链：**编辑器 ImGui（dev，最高，仅 Edit 模式）→ 游戏大 UI（命中测试 hover/focus）→ 游戏/脚本输入（最低）**。

- `UiInputRouter` 从 plan/08 的 `IInputContext` 取鼠标/键盘/滚轮/文本事件，喂 `backend.Feed*`，并用 `backend.HitTest(cursor)` + 焦点态算出 `UiWantCaptureMouse/Keyboard`。
- 引入 UI 捕获门（与 plan/12 `EditorInputGate` 同构）：当编辑器未捕获、但大 UI 命中不透明/可交互元素时，UI 吞该输入，游戏/脚本 `IInputApi` 本帧视其为已消费；反之输入透传给游戏。次序：`freeForGameplay = !editor.WantCaptureMouse && !ui.WantCaptureMouse`。
- 文本输入/IME：焦点在 UI 输入框时把字符事件路由给 UI（`FeedText`），否则给游戏。
- 三后端 `HitTest` 语义一致：RmlUi 查布局命中盒 + 元素不透明度；Ultralight 查 DOM 命中；ManagedFallback 查 `ManagedUiLayout` 控件矩形（并与 Gui host 的 `WantsMouse` 一致），保证切后端仲裁行为不变。

### 3.6 与编辑器 ImGui 的共存

二者都经 plan/08 UI 层注册、都在同一 GL 上下文。约束：(a) 合成次序由显式序号决定，UI(100) 先、编辑器(200) 后（§3.2）；(b) 输入仲裁编辑器优先（§3.5）；(c) 各自 `Composite` 后恢复 GL 状态，互不污染；(d) Play 模式下编辑器工具让位、游戏大 UI 接管输入（呼应 plan/12 §3.15 EditorMode）。发行构建禁用编辑器后，仅游戏大 UI 存在。**注意**：`ManagedFallbackBackend` 与编辑器共用 plan/19 的 `HexaImGuiBackend`，但**是两个独立 ImGui context**（一个游戏 UI、一个编辑器），各自 begin/end frame、各自 draw data，经不同 UI 层序号提交，不互相串扰。

### 3.7 字体引擎（`FontEngine`，含 CJK，独立节点）

**`FontEngine` 是独立子系统节点**，不与后端耦合，统一供给三后端的字形来源，避免"每后端各配一套字体/CJK 逻辑"：

- **资产**：`content/ui/fonts/` 存放拉丁 + CJK 字体（含**子集化的中文字体**，呼应 plan/12 §3.3 中文需求）。`FontEngine` 负责字体清单、fallback 链（拉丁→CJK→符号）、DPI 缩放下的 hinting 参数、字形覆盖检测（缺字上报诊断，验收要求"中文无缺字"）。
- **供给三后端**：RmlUi 后端 → 把 `FontEngine` 选定的字体字节喂 shim 内**静态链 FreeType**（RmlUi `FontEngineInterface`）；Ultralight 后端 → 注册系统/自带字体到 Ultralight font loader；ManagedFallback 后端 → 复用 plan/19 `GuiFontManager` 的 CJK glyph range 加载（`FontEngine` 与 `GuiFontManager` **共享同一份 CJK 字体资产与 glyph range 定义**，不各自维护一份码点表）。
- **一致性**：`FontEngine` 是三后端 CJK 覆盖的**单一事实源**——切后端时"哪些字能显示"不变。缺字回退与诊断（`ui.font.missingGlyph` 计数）统一在此。

### 3.8 内容资产与 UI 文档管线

`content/ui/`：`<screen>.html` + `.css` + 可选 `.js`（Ultralight）/data-model 绑定（RmlUi）/`data-*` 控件元数据（ManagedFallback 布局）+ `fonts/`（§3.7）+ `images/`。`UiDocumentManager` 负责屏栈（menu/settings/inventory/dialog/hud/pause/loading/result）、显隐、模态叠放、返回栈。加载器纯 I/O + `System.Text.Json` 源生成读 `ui-manifest.json`（屏 id→文件、预加载表），无反射回退（trim 友好）。同一份 HTML/CSS：RmlUi 走 RML/RCSS 子集直渲；Ultralight 走标准解析；ManagedFallback 走 `ManagedUiLayout`（解析 HTML 为抽象控件树，忽略高级 CSS、只取盒模型/文本/图像/按钮语义），三者共用 `data-event-*`/`data-model` 契约。

### 3.9 性能设计与预算

- **重绘策略**：事件驱动（§3.2）。静态屏稳态 0 光栅化；HUD 数值变化只标脏对应元素/区域。RmlUi 缓存已编译几何；Ultralight 用 surface 脏矩形；ManagedFallback 重放上帧 draw data。
- **上传/合成开销**：Ultralight 路径 1080p 满屏纹理 8MB，但 UI 稀疏透明，脏矩形子上传远小于此；合成是一次全屏 alpha 四边形，可忽略。RmlUi/ManagedFallback 路径无上传，仅几何/draw list draw。
- **对 60fps sim 预算的影响**：**零直接影响**。sim/物理在相位 3–8 于 JobSystem 上先跑完，UI 逻辑在相位 0/1 只做布局/事件（轻），UI 光栅化/合成在相位 10 的渲染线程；因不变式 #6 不追帧，UI 尖刺只掉渲染帧、sim 固定步长不受累（cadence 闭合见 §3.3）。`ui.update/paint/upload/composite` 计时注册 plan/02 诊断，plan/12 HUD 可见；纳入 plan/18 §4.3 过载降级第二级候选（过载时可停 UI 动画/降 UI 重绘频率）。
- **零分配纪律**：UI 逻辑相位稳态零托管堆分配（`UiValue`/`UiEvent` blittable、环形缓冲、字符串句柄池化），无 LINQ/闭包/装箱于每帧路径。

### 3.10 兼容性与 native 约束（呼应不变式 #10，详见 §7）

- 合成只用 GL 3.3 Core / ES3 必备能力（纹理/FBO/alpha blend/几何），跟随 plan/08 桌面 GL/ANGLE 双路径。
- native 核 **dynamic-only**（`PixelEngine.UI.Native.{dll|so|dylib}` 落 `runtimes/<rid>/native/`，AOT 亦运行时 P/Invoke 动态加载，**不静态链**），不进 Box2D dual-build 矩阵，收敛 fan-out。
- **Windows 优先（plan/15 修订）**：RmlUi native 首期仅构建 `win-x64`（可含 `win-arm64`）；linux/osx 四 RID 保留矩阵位、dormant（不激活构建，但矩阵/CI 保编译门）。**AOT 通道与未激活 RID 一律回退 `ManagedFallbackBackend`（不静态链、不 P/Invoke UI 核）**，保证任何目标上大 UI 可用。
- 登记 plan/00 §4.1 门控依赖：默认"广兼容基线"发行**用 `ManagedFallbackBackend`**（零新增 native）；`RmlUi`/`Ultralight` 为 opt-in gated 特性。

### 3.11 与既有玩家 GUI facade 的调和（强制显式，避免平行 API）

现状既有玩家 GUI 一条链：`Behaviour.OnGui(IGuiContext)`（`src/PixelEngine.Scripting/IGuiContext.cs`，接口留在 Scripting、"不绑定 ImGui/Rendering/Editor"）→ 运行时适配 `ScriptGuiContext`（现于 `PixelEngine.Editor`，plan/19 **下沉 `PixelEngine.Gui`**）→ `HexaImGuiBackend`；plan/13 §3.12 `DemoHud`/`PauseMenu`/`PlayableHud` 均以 `IGuiContext` 绘制。本模块**不另立平行绘制 API**，而是：

- `ManagedFallbackBackend` **就是**把 `content/ui` 屏栈接到上述**同一个 Gui host 实例**（plan/19 中性化后的 host）上渲染——它复用 `IGuiContext` 语义与 `GuiFontManager`，`WantsMouse/WantsKeyboard` 与 §3.5 仲裁一致。
- 既有 `DemoHud`/`PauseMenu`/`PlayableHud` 继续按 `IGuiContext` 工作、**不被重写**；启用 HTML UI 后，它们要么保持 ImGui 直绘（作为 HUD），要么内容迁移到 `content/ui`（plan/13 决定），二者共享同一 host、同一字体、同一输入门，**不产生两套竞争的 GUI 运行时**。
- `IGameUiService`（Scripting 契约）与 `IGuiContext`（Scripting 即时模式）**并存不冲突**：前者是"屏/文档/data-model"高层，后者是"即时模式绘制原语"；`ManagedFallbackBackend` 在内部用后者实现前者。脚本可任选其一，无平行世界。

### 3.12 推荐方案、理由、风险与降级（详见 §5 评估表）

- **推荐（已锁定）**：基线 **`ManagedFallbackBackend`**（零 native、永远可用）+ 主后端 **`RmlUi`**（自建 `[LibraryImport]` 绑定到 vendored RmlUi + 静态链 FreeType 的单一 dynamic `PixelEngine.UI.Native`）+ 可选 **`Ultralight`**（离屏 bitmap→GlTexture→全屏 alpha quad，用于 AI 生成的标准 HTML5/CSS3/JS 高保真页面）。**不采用** CEF/CefSharp（多进程、~150MB+、AOT/trim/footprint 最差）；**不用于世界内合成** WebView2（HWND 绑定、无真离屏、vsync 锁定、仅 Windows）。
- **理由**：RmlUi = MIT、单一小型 dynamic native（与 OpenAL/ANGLE 同级、不进 dual-build）、bring-your-own-renderer 直接复用同一 GL 上下文、AOT/trim 友好、纯几何无浏览器进程开销、事件驱动重绘。ManagedFallback 保证 #10 与广兼容基线；Ultralight 保留"标准 HTML/AI 生成"高保真出口。
- **风险**：RmlUi 是 HTML/CSS *子集*（RML/RCSS，JS 受限→data-model+事件），**不能替代标准 HTML5/AI 生成标准页面**（此诉求走 Ultralight，§1.4 顶层记账）；rmlui.net 社区绑定不成熟→需自建并长期维护绑定（同 Box2D 自建范式）；native 核跨 RID 构建/签名/打包工作量；FreeType 静态链进 shim 的许可与体积；Ultralight 商业许可(<$100K)/DLL 体积/AOT 绑定不确定。
- **降级路线**：(1) 关 native，全走 `ManagedFallbackBackend`（ImGui 皮肤化 game UI）；(2) HTML 只做低频静态屏（主菜单/设置/背包/对话）用 RmlUi，HUD 仍走 ManagedFallback/ImGui 逐帧。`IGameUiBackend` 抽象让 Demo 目标任一后端，降级不改 Demo 逻辑，仅换后端/内容。

---

## 4. 实现清单

模块骨架与后端抽象：
- [x] 建 `src/PixelEngine.UI/PixelEngine.UI.csproj`，`TargetFramework=net10.0`，依赖 `Gui`/`Rendering`/`Core`（依赖方向遵 plan/00 §5，无反向，无 Editor/Scripting 边），仅 native 互操作/纹理上传所需最小 unsafe 块开 `AllowUnsafeBlocks`（§2）
- [x] 定义 `IGameUiBackend`、`UiValue`/`UiEvent`/`UiDocumentSource`/`UiHitResult`/`UiViewport`/`UiBackendKind` 等 blittable 契约与句柄类型（§3.1）
- [x] 在 `PixelEngine.Scripting` 声明 `IGameUiService` 及脚本可见契约（`IWorldEffects` 同栈同款）；Hosting 已实现并挂 `EngineContext.GameUi`，Show/Hide/PushModal/BindModel/SetValue/TryGetValue/UiEventRaised/Invoke 全部经 `GameUiServiceBridge` 转发到运行时 UI 宿主（§2、§3.4）
- [x] `GameUiHost` 门面 + `UiDocumentManager`（屏栈/显隐/模态/返回栈）（§3.1、§3.8）
- [x] 可编译/运行时开关整体禁用大 UI，禁用后零开销（§1.4、§3.3）

基线后端（纯托管，永远可用）：
- [x] `ManagedFallbackBackend : IGameUiBackend`：`ManagedUiLayout`（HTML→抽象控件树）→ plan/19 `PixelEngine.Gui` 中性 host 即时模式绘制；`Composite` 提交 draw data；`HitTest`/`WantCapture` 对齐 Gui host（§3.1、§3.11）。已实现 XHTML 子集 text/button/checkbox/progress、模型值与事件环形缓冲、复用注入的 Gui host。
- [x] 与既有玩家 GUI facade 调和：共享同一 Gui host 实例，`DemoHud`/`PauseMenu`/`PlayableHud`/`Behaviour.OnGui` 不重写、不产生平行 API（§3.11）。已新增 `PixelEngine.Gui` 中性 `IGuiDrawContext`/`DrawCombinedFrame` 与 `GuiRenderBridge` managed 回调，ManagedFallback 与脚本 HUD 在同一 ImGui frame 内固定顺序绘制；Hosting 通过 `EnableGameUi` 接入同一 `GuiApp`。

主后端（RmlUi，Windows 优先）：
- [~] vendor RmlUi + FreeType 到 `native/rmlui/`/`native/freetype/`：已以 submodule 固定 RmlUi 6.2 与 FreeType 2.14.3，CMake 接入 `PixelEngine.UI.Native` dynamic-only 目标并静态链接 RmlUi/FreeType，`build-native` 同步到 `runtimes/<rid>/native/`；完整 RmlUi renderer shim/GL 函数表/字体接口仍待后续切片（§2、§3.10、§7）
- [~] C++ shim：已编入 RmlUi 官方 `RenderInterface_GL3`，以 native handle 管理 renderer create/destroy/viewport 并接入最小 `SystemInterface`；已在 `peui_native_render` 外加 scoped GL 状态守卫，覆盖 program/VAO/VBO/EBO、draw/read framebuffer、active texture 与最多 32 个 texture2D 绑定、blend/scissor/depth/cull/viewport/scissor/unpack alignment，并用真实窗口 GL smoke 验证 render 后恢复；ANGLE/GLES profile 分支仍待后续切片（§3.1、§3.2）
- [~] shim GL 函数指针注入：已由 `RenderWindow.TryGetProcAddress` 把当前 Silk.NET/OpenGL context 的 resolver 传给 native `gladLoadGLUserPtr`，并用真实窗口 smoke 验证 renderer 创建；Hosting 已在 RmlUi native 不可用或当前 context 为 GLES/ANGLE 时显式回退 ManagedFallback，并注册 `GameUiBackendSelection` 记录请求/实际后端与原因；完整 ANGLE/GLES native renderer profile 仍待后续切片（§3.2、§3.12）
- [!] 阻塞：RmlUi ANGLE/GLES native profile。原因：当前 vendored `RmlUi_Include_GL3.h` 是 `gl:core=3.3` glad loader，native shim 绑定官方 `RenderInterface_GL3`，不是 GLES3/ANGLE shader+loader 双 profile；继续完成需要引入或自建 GLES3 renderer/loader 路径，并重新验证同一 context 函数表、shader 版本与 GL 状态恢复。当前已交付安全降级：GLES/ANGLE 或 native 不可用时显式回退 `ManagedFallbackBackend`，不能把 native 双 profile 伪装为完成。
- [~] `[LibraryImport]` 绑定 shim 的 C-API：已接入 `PixelEngine.UI.Native` resolver、真实 RmlUi 版本探活、GL 注入、renderer/context/document/update/render 生命周期入口；model/input/事件回调用 `[UnmanagedCallersOnly]` 待后续切片（§2、§3.4）
- [~] `RmlUiBackend : IGameUiBackend`：已实现 Initialize/LoadDocument(资产→内存 RML)/SetScreenStack(Show/Hide)/Update/Resize/Composite(render)、Feed* 输入转发、DOM `HitTest` 基础命中并接入 Hosting `UiBackendKind.RmlUi`；已接入 PixelEngine DOM 绑定桥（`data-model`/`path` → `SetModelValue/TryGetModelValue`，`data-event-click`/`action`/`data-event-change` → `DrainEvents`，`InvokeAction` → native DOM action payload 应用），真实窗口 smoke 已验证文档渲染、基础输入注入、非 modal 空白透传、模型值 set/get、action invoke 与点击事件入队；完整 RmlUi `DataModelConstructor` / dotted path 映射仍待 `UiModelBridge` 后续切片（§3.1、§3.2、§3.4、§3.5）

合成与相位集成：
- [x] **plan/08 新增** `RenderPipeline.RegisterUiLayer(int order, IUiCompositeLayer)` 显式带序号 UI 层注册（替代多播订阅顺序）；本模块此项为硬前置（§3.2、§8）
- [x] `UiLayerCompositor : IUiCompositeLayer` 以 `order=100` 注册；已接入 RmlUi/非 ManagedFallback 后端的 Hosting 装配并调用 `GameUiHost.Composite`，ManagedFallback 仍复用同一 `GuiRenderBridge`；Editor 显式层改为 `order=200`，`BeforePresentUi` 仅保留为兼容 hook，不再作为游戏/编辑器 UI 叠放机制；托管 UI 层 `UiGlStateSnapshot` 已覆盖 framebuffer/program/VAO/VBO/EBO/active texture/texture2D/blend func/blend equation/UNPACK_ALIGNMENT/viewport/scissor/enable bits，RmlUi shim 侧 GL save/restore 已用真实窗口 smoke 验证（§3.2）
- [x] 编辑器 `EditorRenderBridge` 以 `order=200` 注册，保证 UI(100)<编辑器(200)（§3.2、§3.6）
- [x] `GameUiPhaseDriver` 挂相位 0/1：已接入 `Update(renderDt)`、预分配事件 drain、`IGameUiService.UiEventRaised` 派发与 `GameUiModelBridge` 模型推送，UI cadence 不受 sim 降频/TimeScale 影响；UI 事件接入 `ScriptEventBus` 后延迟到脚本事件 drain 派发并复用 Behaviour 异常隔离；`Invoke` 已经由节点 7 统一转发到后端 action 通道（§3.3、§3.4）
- [x] `EngineBuilder.EnableGameUi(bool)` + `UseUiBackend(UiBackendKind)`；禁用零开销（§3.3）

输入路由与仲裁：
- [~] `UiInputRouter`：已接入 Hosting 窗口输入源、Silk `KeyChar` 文本环形缓冲、backend `Feed*` 与 `HitTest` capture，并在脚本输入采样前屏蔽游戏通道；已实现 UI 键盘焦点保持（鼠标移出仍接收键盘/文本）、外部点击失焦透传、失焦/上游拦截时释放已按下 UI key edge 并排空陈旧文本；完整 IME composition 待引入平台 composition 事件后闭合（§3.5）
- [~] UI 捕获门（与 `EditorInputGate` 同构）：已新增 Hosting 中性 `IEditorInputCaptureSource` 并由 EditorShell 注册，脚本输入路由按 Editor capture → 大 UI capture → Game 顺序合并；大 UI pump 已接收上游 capture 门，Editor/Gui 捕获时不再向大 UI 注入同帧输入；完整 IME 与三后端 `HitTest` 完整一致性仍待后续切片（§3.5）

C#↔UI 通信：
- [x] `IGameUiService` 后端实现（Hosting）：已接入 ShowScreen/HideScreen/PushModal/BindModel/SetValue/TryGetValue/UiEventRaised/Invoke 与 Scripting 注入；禁用 HTML UI 时 `IScriptContext.GameUi` / `ScriptSimulationContext.GameUi` 返回 `NoopGameUiService`，脚本调用安全静默且不回传假数据；接入 `ScriptEventBus` 后 UI 事件不再同步调用脚本处理器，等脚本相位 drain 后派发；Invoke 已通过 `GameUiHost.InvokeAction` 转发到 ManagedFallback 与 RmlUi native DOM action 通道，并用真实窗口 RmlUi smoke 验证（§3.4）
- [~] `UiModelBridge`：已完成 RmlUiBackend 的真实 DOM 数据/事件桥、ManagedFallback 托管模型语义对齐、后端 `CopyModelPaths` 与 Hosting `GameUiModelBridge`（相位 1 前按当前 UI 文档声明 path 从 `IUiModel` 读取并推送）；UI→游戏 世界写入已通过 `UiEvent`→`ScriptEventBus`→`ScriptSimulationContext` 延迟命令队列测试覆盖；RmlUi 官方 `DataModelConstructor`、dotted path 到合法变量名映射与 Ultralight JS 对象桥仍待后续切片（Ultralight 归节点 10，可选 profile）（§3.4）
- [~] `UiDiagnostics`：已把 `ui.update`（模型推送/后端 Update/事件 drain）与 `ui.composite`（RmlUi/ManagedFallback/脚本 GUI present 层合成）接入 `FrameSubPhase`、`EngineCounters` 与 plan/12 性能 HUD；`ui.paint/upload`、事件驱动重绘验证与 plan/18 降级联动仍待后续切片（§3.9）

字体引擎（独立节点）：
- [x] `FontEngine`：已实现 content/ui/fonts 优先、`GuiFontManager` 共享系统候选回退、DPI 字号与共享 glyph range 覆盖扫描；已通过 `UiBackendInitializeInfo.FontSelection` 向后端供给字体选择，RmlUi 后端真实调用 native `Rml::LoadFontFace` 注册字体，ManagedFallback 复用同一 GuiFontManager glyph range，并把缺字数累计到 `EngineCounters.UiFontMissingGlyphs` / plan/12 性能 HUD；Ultralight 作为可选 profile 的实际注册归节点 10（§3.7）
- [x] CJK 子集资产接入：`FontEngine` 已复用 `PixelEngine.Gui.GuiFontManager` 的 CJK 候选字体与 glyph range 定义，Demo content/ui/fonts 已落 Noto Sans SC 简中变量子集字体 `NotoSansSC-VF.ttf`、OFL 许可与 SOURCE 记录，content/ui/fonts 候选优先生效且 RmlUi/ManagedFallback 可消费该选择；Ultralight 注册待可选 profile 激活后续切片（§3.7，呼应 plan/12 §3.3）

内容与资产：
- [~] `content/ui/` 结构 + `ui-manifest.json` 加载器：已实现纯 I/O + STJ 源生成 manifest 解析、screen id→资产路径映射、preload 标记、重复 id/路径逃逸/缺失文件校验，并接入 `GameUiServiceBridge` 优先按清单解析屏幕；已通过 `UiAssetDirectories` 暴露规范化 `fonts/`、`images/` 目录契约，并支持 `images[]` 图片资产清单的重复 id、路径逃逸、缺失文件校验与 preload 标记；图像解码/后端消费仍待后续切片（§3.8）
- [~] `ManagedUiLayout`：已实现 XHTML 子集 text/button/checkbox/progress、`data-event-*`/模型路径契约与根窗口盒模型（`x/y/width/height`、`data-*`、`style left/top/width/height` → `Gui.SetNextWindow`）；图像控件、完整 CSS 盒模型与后端图片消费仍待后续切片（§3.8、§3.1）

可选后端（Ultralight profile，gated）：
- [ ] `UltralightBackend : IGameUiBackend`：CPU 离屏 BGRA8 → 脏矩形 `glTexSubImage2D` 上传 → 全屏 alpha quad；JS 全局对象桥（§3.1、§3.4）
- [ ] Ultralight native + UltralightNet 绑定登记 plan/00 §4.1 门控、dynamic-only；AOT 不友好时门控排除并回退 `ManagedFallbackBackend`（§5、§7）

打包与发行（配合 plan/15）：
- [ ] `PixelEngine.UI.Native` 及可选 Ultralight native 落 `runtimes/<rid>/native/`，dynamic-only，不进 Box2D dual-build；首期仅 `win-x64`(+可选 `win-arm64`) 激活，其余 dormant（§3.10、§7）
- [ ] macOS 对 UI native 库 codesign + notarize（RID 激活后）；纳入 `SHA256SUMS`；README/许可声明（Ultralight 需转授权条款、RmlUi MIT + FreeType FTL）

---

## 5. 候选评估表（可行性研究结论）

| 维度 | WebView2 | CEF / CefSharp / CefGlue | Ultralight（可选高保真） | RmlUi（主后端） | ManagedFallback（纯托管基线） |
|---|---|---|---|---|---|
| 内核 | 系统 Edge/Chromium | 内嵌完整 Chromium | 轻量 WebKit fork | 自研 HTML/CSS 子集布局 | plan/19 Gui/ImGui 皮肤化 |
| HTML 保真 | 完整 HTML5/CSS3/JS | 完整 HTML5/CSS3/JS | 高（WebKit，含 JS/JSC） | **子集**（RML/RCSS，JS 受限） | 抽象控件（盒模型/文本/按钮，无高级 CSS） |
| 成熟度/维护 | 高（微软），offscreen 未支持 | 高（CEF 活跃） | 中（商业公司），UltralightNet 活跃度不定 | C++ 库成熟(MIT)；C# 绑定需自建加固 | 复用现有 Gui host，最成熟 |
| 许可 | 免费（分发 Runtime） | BSD（+Chromium 组件） | **商业**（<$100K 免费，超需付费） | **MIT**（FreeType FTL） | 无新许可 |
| 体积 | 系统 Runtime（~百 MB 外置） | **最重**（~150MB+ + 多进程） | 中（数十 MB DLL） | **最小**（单库 + FreeType，几 MB） | **零新增** |
| GL/纹理互操作 | **无真 offscreen**；HWND 绑定 | OSR→CPU pixel→GL 纹理 | CPU→离屏 bitmap→纹理 | **bring-your-own-renderer**：同一 GL 直绘几何 | 同一 GL，ImGui draw list |
| 独立进程/窗口 | 独立 HWND | **多进程** | 单进程库 | 单进程库，无窗口 | 无（进程内） |
| 性能 | 帧率锁 host vsync | 进程/IPC/合成开销大 | 事件驱动，游戏向 | **最轻**，几何缓存 | 轻（ImGui 逐帧或缓存） |
| 跨平台 | **仅 Windows** | Win/Linux/macOS | Win/Linux/macOS | Win/Linux/macOS（随我方 toolchain） | 全 RID（纯托管） |
| AOT/trim 友好 | 差 | 差 | 中（不确定） | **好**（`[LibraryImport]`+`[UnmanagedCallersOnly]`） | **最好**（纯托管） |
| 与 #10 冲突程度 | 高（且无法世界内合成） | **最高** | 高（WebKit-fork + 商业许可） | **低**（单 dynamic native，门控） | **无**（无 native） |

**结论**：plan/08 UI 层注册使"HTML 层渲染→世界之上合成"完全可行；**基线 ManagedFallback（保 #10 与广兼容）+ 主后端 RmlUi（架构纯度/许可/体积/AOT 最优）+ 可选 Ultralight（标准 HTML/AI 生成高保真出口）**，**排除 CEF、排除 WebView2 世界内合成**。RmlUi 子集**不**独自承担"标准 HTML5/AI 生成"卖点（§1.4 顶层记账）。

---

## 6. 验收标准

- [x] 大 UI 在 plan/08 同一 GL 上下文、世界渲染之后经 `RegisterUiLayer(100,…)` 合成、编辑器 ImGui(200) 在其上；合成后 GL 状态被正确恢复（含 shim 侧 save/restore），世界画面无被 UI 污染（§3.2）
- [x] 主菜单/设置/背包/对话/HUD 至少各一屏用 HTML/CSS 编写并正常显示（含中文无缺字）、可导航/返回/模态叠放；同内容在 `RmlUi` 与 `ManagedFallback` 两后端均可显示与交互。已落地 `content/ui/ui-manifest.json` 与五个 `screens/*.xhtml`，`GameUiDemoController` 经 `IGameUiService` 驱动主菜单/HUD/设置/背包/对话模态；`DemoUiContentTests` 覆盖 manifest、中文正文 glyph range、ManagedFallback 绘制/按钮/复选框事件、脚本屏栈导航，并用 `PIXELENGINE_RENDERING_GL_SMOKE=1` 真实窗口验证 RmlUi 载入同源页面、鼠标点击主菜单按钮、模型写入与合成（§3.8、§3.11）
- [!] 输入三级仲裁正确：编辑器(Edit) > 大 UI(命中) > 游戏；UI 命中不透明元素时游戏不消费该输入，反之透传；文本/IME 焦点路由正确；切后端仲裁行为不变（§3.5）。阻塞：Editor→大 UI→Game 的上游捕获门、KeyChar committed text、RmlUi/ManagedFallback HitTest 已接入并测试；完整 IME composition 需要平台 composition 事件/抽象，当前 Silk KeyChar 只覆盖提交文本；三后端一致性还受 UltralightBackend 未激活阻塞，不能伪装为完成。
- [x] C#↔UI 双向：游戏态变化经 `SetValue`/`BindModel`/data-model 反映到 UI，UI 交互（如"开始游戏"）产生 `UiEvent` 并经脚本事件总线在相位 1 派发，事件触发的世界写入经延迟队列落正确相位；禁用 HTML UI 时 `GameUi` 为 no-op 且调用安全；`Invoke` 已接入 ManagedFallback 与 RmlUi native action 通道并通过托管单测 + 真实窗口 RmlUi smoke 验证（§3.4、#6）
- [ ] 事件驱动重绘生效：静态屏稳态无光栅化开销（诊断 `ui.paint`≈0）；HUD 仅在数值变化时局部重绘/脏矩形上传（§3.2、§3.9）
- [x] UI cadence 与 sim 频率解耦：sim 降到 30Hz 时 UI 动画仍按渲染 cadence 平滑推进、不加倍不卡顿；UI 尖刺只掉渲染帧、sim 固定步长不受累（§3.3、#6）。已用 `GameUiPhaseDriverUpdatesEveryRenderFrameAndDrainsEvents` 覆盖 sim 跳帧与 TimeScale<1 时 UI 仍消费未缩放 render dt。
- [~] `FontEngine` 为三后端 CJK 单一事实源：已覆盖 ManagedFallback 与 RmlUi 的同源字体选择、共享 glyph range、真实 CJK 子集资产与 `ui.font.missingGlyph` 诊断；Ultralight 可选 profile 尚未激活，实际注册与后端一致性验收归节点 10（§3.7）
- [ ] native 核 dynamic-only、未静态链、未进 Box2D dual-build；首期 `win-x64`(+可选 `win-arm64`) 的 `runtimes/<rid>/native/` 含 UI native，其余 RID dormant；AOT 通道与未激活 RID 回退 `ManagedFallbackBackend` 且大 UI 仍可用（§3.10、§7，配合 plan/15）
- [ ] 禁用大 UI 开关后主循环无 UI 开销；降级路线可切到"全 ManagedFallback"或"静态屏 RmlUi + ManagedFallback HUD"，均不改 Demo 逻辑（§3.12）
- [ ] 既有玩家 GUI facade 未被平行 API 取代：`DemoHud`/`PauseMenu`/`PlayableHud`/`Behaviour.OnGui` 与 `ManagedFallbackBackend` 共享同一 Gui host、同一字体、同一输入门（§3.11）
- [ ] 公开 API 全带中文 XML 文档注释；UI 逻辑相位稳态零托管堆分配（BenchmarkDotNet `MemoryDiagnoser` Gen0=0）（§3.9）
- [ ] 本文档技术栈不与 plan/00 §4 冲突；native 核已登记 §4.1 门控依赖；不变式 #10 修订（门控类）已在 AGENTS §1/plan/00 §4/plan/15 同步（§7）

---

## 7. 与不变式 #10 的处理（修订已获批）

**冲突起点**：不变式 #10 要求"native 面收敛到 Box2D 一个依赖"。任何 HTML 核（CEF/Ultralight/RmlUi native）都是**新增 native 依赖**，与 #10 字面冲突。

**#10 的真实意图辨析**：#10 原文"其余（OpenAL/ANGLE 等）走系统/动态分发，降低 dual-build fan-out"表明其保护对象是 **static+dynamic × RID 的 dual-build 扇出**，而非"进程内绝对只有一个 .so"。OpenAL、ANGLE 已被 #10 接纳为**额外 native 依赖但 dynamic-only**。据此 UI native 核可作 **OpenAL/ANGLE 同级公民**处理。

**已批准的处置（宪法级修订，门控类；先改计划文档再改代码）**：
1. **修订 #10 措辞**为其真实意图——"只有 Box2D 需 static+dynamic dual-build；其余 native（OpenAL/ANGLE/UI 核 RmlUi/Ultralight）一律 dynamic-only 分发，且可为门控/可选依赖"——并在 `AGENTS.md §1 #10`、`plan/00 §4/§4.1`、`plan/15` 同步（本文档为该修订在 UI 侧的落地记账；跨文件同步由对应文档 writer 执行）。
2. UI native 核**严格 dynamic-only**：只产动态库、AOT 亦运行时 P/Invoke 动态加载，**绝不静态链**，**不进 Box2D dual-build 矩阵** → #10 保护的 dual-build fan-out 仍锁定 Box2D 一项。
3. **`ManagedFallbackBackend` 作为纯托管基线**保证默认"广兼容基线"发行**零新增 native**（Demo 大 UI 落 ManagedFallback），`RmlUi`/`Ultralight` 登记 plan/00 §4.1 **门控/可选依赖**（与 ComputeSharp/NVorbis 同栏），opt-in gated。
4. RmlUi 主后端把 **FreeType 静态链进单一 `PixelEngine.UI.Native` 动态库**，对外仍是"一个新增 dynamic 库"，不额外增 native 扇出面。
5. Windows 优先：native 首期仅 `win-x64`(+可选 `win-arm64`) 激活，其余 RID 保留矩阵位 dormant；AOT 与未激活 RID 回退 ManagedFallback（不静态链）。

由此 #10 修订后，HTML UI native 归**门控类**、dual-build 仍仅锁 Box2D，宪法一致性成立。

---

## 8. 依赖关系

前置：plan/00（§4/§4.1 选型与门控、#10 修订同步）、plan/01（骨架/CPM/native 构建）、plan/02（诊断/计时/事件）、plan/08（共享 GL 上下文、**新增 `RegisterUiLayer` UI 层注册接口**、输入源、脏矩形上传技术）、plan/18（相位挂载、EngineContext 服务聚合、EngineBuilder 开关、延迟命令队列）、**plan/19（GUI 宿主中性化重构 M13：`PixelEngine.Gui` 的 `HexaImGuiBackend`/中性 `IGuiContext` 适配/`GuiFontManager`，ManagedFallback 与 FontEngine 复用之）**。

**硬顺序约束**（写进 plan/17）：
1. plan/08 的 `RegisterUiLayer` 显式带序号 UI 层注册接口，必须**先于** `UiLayerCompositor` 装配（替代脆弱的多播订阅顺序）。
2. plan/19 M13 GUI 宿主中性化重构（`PixelEngine.Gui` 抽取 + `GuiFontManager` CJK），必须**先于** `ManagedFallbackBackend`/`FontEngine` 落地。

协同：plan/11（脚本经 `IGameUiService` 驱动 UI、契约声明在 Scripting、相位安全写入）、plan/12（编辑器 ImGui 共存与仲裁、HUD 显示 `ui.*`）、plan/13（Demo 提供 HTML 内容与消费 `IGameUiService`；HUD 迁移/ManagedFallback 等价实现取舍）、plan/14（headless UI 测试：布局/data-model/输入仲裁/脏矩形正确性、三后端一致性，不依赖 GL）、plan/15（UI native dynamic-only 打包、Windows 优先激活/其余 dormant、codesign、SHA256SUMS、许可声明）。

风险/阻塞：若 rmlui.net 绑定不足需自建 `[LibraryImport]` 绑定（同 Box2D 自建范式）；若选 Ultralight 需评估其 AOT 绑定与商业许可，不达标按 §3.12 降级回退 ManagedFallback 并标 `- [!] 阻塞：原因`。

## 9. 提交节点

按 AGENTS §6，每节点完成即中文 git 提交（scope=`ui`）：
- [x] 节点 1：`feat(ui): PixelEngine.UI 骨架 + IGameUiBackend 抽象 + 文档/屏管理`（§3.1、§3.8）
- [x] 节点 2：`feat(ui): ManagedFallbackBackend 纯托管基线(复用 PixelEngine.Gui host，统一现有 IGuiContext 玩家 HUD)`（§3.1、§3.11）
- [x] 节点 3：`feat(ui): FontEngine 独立节点 + CJK 子集(与 GuiFontManager 共享资产)`（§3.7）
- [!] 节点 4：`feat(ui): RmlUi native 核(vendored + FreeType 静态链 + [LibraryImport] 绑定 + GL 函数注入/双 profile)`（§3.1、§3.2、§7）。阻塞：桌面 GL3 RmlUi native 核、FreeType、C API、GL 注入、真实窗口 smoke、GL 状态恢复与 ManagedFallback 降级已完成；ANGLE/GLES native 双 profile 需要独立 GLES3 renderer/loader 方案，当前不能无依据勾选。
- [x] 节点 5：`feat(render): plan08 RegisterUiLayer 带序号 UI 层注册 + UiLayerCompositor 合成 + 相位0/1 UI 逻辑`（§3.2、§3.3）
- [!] 节点 6：`feat(ui): 输入三级仲裁(编辑器>大UI>游戏) + WantCapture 门 + 三后端 HitTest 一致`（§3.5、§3.6）。阻塞：已修复上游 capture 门与 UI key/text 状态清理；完整 IME composition 与 UltralightBackend HitTest 尚无真实实现入口。
- [x] 节点 7：`feat(ui): IGameUiService(契约在 Scripting)C#↔UI 双向数据/事件桥 + 相位安全写入`（§3.4）
- [ ] 节点 8：`feat(ui): 诊断计时 + 事件驱动重绘 + UI cadence 解耦 + 零分配加固 + 降级接入`（§3.3、§3.9、§3.12）
- [ ] 节点 9：`docs(plan): 登记 plan/00 §4.1 门控依赖 + #10 门控修订同步 + plan/15 Windows 优先打包 + README/17 索引`（§7、§8）
- [ ] 节点 10（可选）：`feat(ui): Ultralight 高保真后端(离屏 bitmap→纹理，门控，AOT 不达标回退 ManagedFallback)`（§3.1、§5）
