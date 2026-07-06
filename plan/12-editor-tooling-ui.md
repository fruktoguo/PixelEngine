# Plan 12 — 引擎内嵌编辑器与管理 UI（PixelEngine.Editor）

> 本文档定义 `PixelEngine.Editor` 子系统的完整实现计划：基于 **Dear ImGui（Hexa.NET.ImGui）** 的引擎内嵌、停靠式（docking）编辑器与全套管理/调试 UI。
> 权威依据：`../docs/PixelEngine-架构与需求设计.md`（下称「架构」，重点 §17、§3.3、§4、§7、§11）；技术栈：`00-conventions-and-techstack.md`；开发宪法：`../AGENTS.md`。
> 状态：`- [ ]` 未开始 / `- [x]` 完成并自测 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。

---

## 1. 目标与范围

`PixelEngine.Editor` 是引擎内嵌的、类似 Unity Editor 的可视化工具层：它把 sim 的不可见态（dirty rect、parity、活跃 chunk、owned-by-body）变为可视、可交互；提供材质/反应实时编辑、世界画刷、检视器、性能 HUD、存读档与各子系统调参面板；并提供「编辑 / 运行游戏」模式切换。

锁定决策（本文档核心、不可偏离）：

- 编辑器/管理 UI = **引擎内嵌 Dear ImGui（Hexa.NET.ImGui）停靠式面板**，**与 `plan/08`（Rendering）共享同一个 OpenGL 上下文**，ImGui 在世界渲染（架构帧相位 [10]）之后、`present` 之前绘制于顶层。**面板层 `PixelEngine.Editor` 恒保持引擎内嵌、面板不另起进程 / 不另开窗口 / 不另建 GL 上下文——该约束是相对「被编辑 / 运行的游戏世界」成立的**：面板层不为运行游戏 spawn 第二进程、第二窗口或第二 GL 上下文。承载编辑器面板层的**宿主进程本身由独立顶层应用 `apps/PixelEngine.Editor.Shell`（见 `plan/19`）承载**（该壳拥有唯一 `RenderWindow`+GL 上下文，`plan/18` 提供 attach/不 own 的窗口所有权解耦 API）；编辑器进程内始终是**单窗口 / 单 GL 上下文**跑 Edit/Play，本条约束在该进程内仍完全成立。
- **ImGui 恒为编辑器 / 开发（dev）向 UI**：调试叠层、检视器、调参、性能 HUD、材质编辑器一律 ImGui。**面向玩家的发行级大 UI（主菜单 / 背包 / 对话 / 设置等）归 `PixelEngine.UI`（`plan/20`）的 HTML 后端**，与 ImGui 是两套并存 UI，共享同一 GL 上下文但职责不重叠（合成次序 / 输入仲裁见 §3.3 与本节新增小节）。玩家 HUD 所需的 ImGui host（`PixelEngine.Gui.HexaImGuiBackend`、`IGuiContext` 运行时适配、字体栈）已下沉至中性程序集 `PixelEngine.Gui`（位于 Rendering 之上、Editor 之下）；编辑器面板层仍保留 `PixelEngine.Editor` 专用 backend / dockspace / bridge，但由独立壳注入，不再被 Hosting 或 Demo 静态引用；`plan/20` 的字体 / 回退复用 `PixelEngine.Gui` 中性栈。
- 编辑器**只消费各子系统的只读 / 调参（read-only / tuning）公开 API，绝不实现任何子系统逻辑**。画刷写 cell 走 Simulation 的公开编辑 API；调试叠层的逐 cell 着色走 Rendering 的 debug overlay 钩子；逐项数据读 `plan/02` 的诊断 / 计时器快照。
- 一步到位，无 MVP：面板、叠层、调参项一次性做全。
- 编辑器是工具层，**不在 sim 热路径内**，但仍遵守「能多线程 / 省内存就上」：叠层几何构建、资源缩略图生成等离线工作可并行；逐帧避免明显的托管堆 churn（预分配缓冲、缓存格式化字符串）。编辑器可在发行构建中通过编译开关 / 运行时开关整体禁用，禁用后零开销。

范围内：ImGui 集成（GL 后端 + 输入桥 + docking + 中文字体 + ImGuizmo/ImPlot 备用）、材质/笔刷调色板、世界检视器、调试可视化叠层、性能 HUD、材质+反应实时编辑器（含 id 稳定热重载）、实体/脚本 Inspector、资源浏览器、场景/世界层级、sim 控制条、存读档 UI、刚体/粒子/光照调参、编辑/运行模式切换。

范围外（仅消费其 API，不在此实现）：CA 内核（`plan/03`）、材质反应温度逻辑（`plan/04`）、粒子（`plan/05`）、物理（`plan/06`）、存档序列化（`plan/07`）、渲染管线（`plan/08`）、脚本编译/热重载机制（`plan/11`）、宿主主循环与帧节奏（Hosting / 架构 §4）。这些子系统必须提供编辑器所需的公开 read-only/tuning API；若缺失，按 `AGENTS.md §2` 标 `- [!] 阻塞` 上报，由对应 plan 补 API，不在 Editor 里开后门访问内部类型。

职责边界（与 `plan/19`、`plan/20` 的划分，防止重复实现）：

- **本文件（`plan/12`）继续拥有全部编辑器面板实现**（`PixelEngine.Editor` 面板层：调色板/检视器/叠层/HUD/材质编辑器/Inspector/调参/存读档/模式切换等），但由独立壳 `apps/PixelEngine.Editor.Shell` 经 Hosting 的 `IEditorHostExtension` 注入；`Hosting` 编译期不再静态引用 `PixelEngine.Editor`。
- **`plan/19`（`apps/PixelEngine.Editor.Shell`）拥有壳应用 / 进程 / 窗口生命周期、Project 模型、类 Unity 的 GameObject 层级 + Inspector + 变换 gizmo + 拾取 + prefab + `.scene` 保存往返等 authoring UX**，并**注入**本文件的面板层（开发构建经 `RegisterDefaultEditorPanels` 公开 bootstrap）。壳复用本文件既有 ImGui 面板，不重造。
- **编辑器内构建（Build Settings 一键出包）面板由 `plan/19` 承载**（因需读 Hosting 的 `EngineProject`，必须位于 Hosting 之上的壳程序集，放 `PixelEngine.Editor` 会造成 `Editor→Hosting` 循环），其只**编排**、消费 `plan/15` §3.11 的 `build-player` 契约，不在此重实现打包。
- **本文件已 `- [x]` 的节点 1–9 与 §5 验收不重开**：以上均为交叉引用与新增 `- [ ]` 条目，不改动任何已完成条目的语义与勾选。

---

## 2. 技术栈与依赖

- UI 框架：**Dear ImGui via `Hexa.NET.ImGui`**（即时模式、AOT 友好、活跃维护），`Hexa.NET.ImGui.Backends`（OpenGL3 后端）；启用 **docking** 分支特性。备用：`Hexa.NET.ImGuizmo`（变换 gizmo）、`Hexa.NET.ImPlot`（性能曲线绘图）、`Hexa.NET.ImNodes`（反应图，可选）。版本集中于 `Directory.Packages.props`（CPM）。**与 `plan/00` 选型表一致，不另立选型。**
- GL / 窗口 / 输入：复用 `plan/08` 的 `Silk.NET`（`Silk.NET.OpenGL`、`Silk.NET.Input`、`Silk.NET.Windowing`）；ImGui GL 后端绑定 Rendering 暴露的 **同一 `GL` 实例与同一 GL context**。输入经 `Silk.NET.Input` 事件桥接到 ImGui IO（桥接面 `plan/02` 时间/事件与 `plan/08` 输入）。
- 诊断 / 计时：`PixelEngine.Core`（`plan/02`）的诊断与分项计时器快照（架构 §17.1、`plan/00 §7`）。
- JSON：`System.Text.Json + 源生成器`（材质/反应编辑器读写 `content/*.json`，与 `plan/04`/`plan/07` 同一序列化设施）。
- 目标框架/语言：`.NET 10 / C# 14`，`Nullable enable`，file-scoped namespace，命名空间根 `PixelEngine.Editor`。Editor 项目**不**开 `AllowUnsafeBlocks`（非热路径；除 ImGui 后端 buffer 上传必需的最小 unsafe 块外）。
- 依赖方向（`plan/00 §5`，绝不反向）：`{ Demo, apps/PixelEngine.Editor.Shell } → Hosting → { ..., Gui }`，`EditorShell → { Hosting, Editor, Gui }`，`Editor → { Gui, Core, Simulation, Physics, World, Serialization, Content, Rendering, Audio, Scripting }`。Editor 只引用这些公开 API；被引用方绝不反向依赖 Editor。壳应用 `apps/PixelEngine.Editor.Shell`（`plan/19`）位于 Hosting 之上、与 Demo 同层，注入本文件面板层。

---

## 3. 详细设计

### 3.1 集成与帧相位

编辑器作为一个 **EditorFramePhase** 挂入 Hosting 主循环（架构 §3.3）：在帧开始处采集输入并 `ImGui.NewFrame()`；在架构相位 [10]（GPU Upload & Render）之后、`present` 之前，由 `ImGuiController.Render()` 把 ImGui draw data 用共享 GL context 绘制到默认 framebuffer 顶层。世界画面通过 Rendering 暴露的**离屏 FBO 纹理**显示在 ImGui 的「世界视口」窗口中（render-to-texture），从而让停靠面板包裹/叠加于世界之上而不互相裁剪。

编辑器对世界的写操作（画刷 set/erase cell、温度笔刷）只在**单线程的输入/玩法相位（[0]/[1]）**、CA（相位 [4]）之前应用，等同架构 M0 的「鼠标画沙」；写后标记所在 chunk dirty 并触发 KeepAlive（经 Simulation 公开编辑 API），绝不在 CA 多线程相位中并发改网格。这保证不破坏不变式 #1/#2/#3（单缓冲原地 + checkerboard 无锁）。

### 3.2 模块与类型

`EditorApp`（编辑器面板层顶层门面，由独立编辑器壳经 `IEditorHostExtension` 注入 Hosting 相位 [10]）持有：`ImGuiController`（GL 后端 + draw data 渲染）、`ImGuiInputBridge`（Silk.NET 输入 → ImGui IO）、`EditorFontManager`/`GuiFontManager` 字体栈、`EditorDockSpace`（docking 布局）、`EditorMode` 状态机（Edit/Play）、以及 `IEditorPanel` 面板集合与 `DebugOverlayController`。所有面板实现统一接口 `IEditorPanel { string Title; bool Visible; void Draw(in EditorContext ctx); }`。`EditorContext` 是传入各面板的只读句柄集合（Engine 门面、各子系统 read-only/tuning 接口、诊断快照、相机、选中态 `EditorSelection`）。

`EditorSelection` 集中保存当前选中：cell 坐标、实体/脚本句柄、刚体 id、资产路径、材质 id，供检视器与 Inspector 共享。

### 3.3 ImGui 集成

- `ImGuiController`：创建/销毁 ImGui context；上传字体 atlas 为 GL 纹理；每帧把 `ImDrawData` 翻译为 GL3 draw call（VBO/EBO/scissor）。复用 Rendering 的 `GL` 实例，渲染后恢复 GL 状态（program/blend/scissor/vao）避免污染世界渲染状态。开启 `ImGuiConfigFlags.DockingEnable`；视情况开 `ViewportsEnable`（多视口，需 Silk.NET 多窗口支持，作可选）。
- `ImGuiInputBridge`：把 Silk.NET 的鼠标/键盘/滚轮/文本输入事件灌入 `ImGuiIO`；处理 `WantCaptureMouse/WantCaptureKeyboard`——当 ImGui 捕获时，游戏/画刷不消费该输入，反之亦然，避免 UI 与世界交互打架。**多级输入仲裁（扩展 `plan/20` HTML UI 共存后的确定性路由）**：单一 `WantCapture` 布尔不足以协调三套输入消费者，须建立显式优先级链 **编辑器 ImGui > 模态游戏 UI（`plan/20` 模态 HTML 面板，如对话/背包全屏）> 非模态 HUD（准星/图例/血条，不吞输入）> 世界（画刷/gizmo/玩家控制器）**。仲裁在每帧输入分发前求值：编辑器 ImGui 若 `WantCaptureMouse/Keyboard` 命中则独占；否则若存在 open 的模态 HTML UI 则由其消费；非模态 HUD 只绘制不拦截；剩余落世界。Play 模式下编辑器工具让位（编辑器仅保留调试叠层/HUD 显示，见 §3.15），输入优先级降为 HTML UI 之下，游戏玩家控制器接管世界层。该仲裁器为单一真相，`plan/20` 与 `plan/13` 均从此读优先级，不各自实现 capture 逻辑。
- `EditorFontManager` / `GuiFontManager`：编辑器面板层保留 `EditorFontManager` 管理 dockspace / 面板所用字体；玩家 HUD 经 `PixelEngine.Gui.GuiFontManager` 使用同一 CJK 字体候选与 DPI 缩放策略，避免玩家包依赖 `PixelEngine.Editor`。`plan/20` 的 HTML 回退基线复用 `PixelEngine.Gui` 中性字体栈。
- `EditorDockSpace`：建立全窗口 dockspace 主机窗口；提供默认停靠布局（左：层级 + 资源浏览；中：世界视口；右：Inspector + 检视器 + 调色板；下：性能 HUD + 控制台/诊断）；布局可保存/恢复（`imgui.ini` 或自管布局）。

### 3.4 材质 / 笔刷调色板面板（`MaterialBrushPalettePanel`）

工具：画（Paint）、挖（Dig，置 Empty）、橡皮（Erase）、温度笔刷（Temperature，向温度场写入增量/目标温度）。参数：笔刷大小（半径）、形状（圆/方/点）、不透明/概率（散点密度）、目标材质选择（从 `plan/04` Content 的运行时材质表 + 名称/缩略图列出）。温度笔刷读写 `plan/04` 温度场（粗 1/4 分辨率，架构 §7.5）。所有写入经 Simulation 公开编辑 API（`ISimulationEditApi.Paint/Dig/SetTemperature(...)`），落在相位 [1]，写后标 dirty + KeepAlive（不变式 #4）。材质选择遵循「颜色不入 cell」（#7）——调色板缩略图由材质纹理/BaseColor 生成，cell 只写 material id。

### 3.5 世界检视器（`WorldInspectorPanel`）

在世界视口中点选一个 cell，显示其：material（id + 稳定 Name）、temperature（采样粗热场）、Flags 位（parity/settled/burning/freefalling 等，逐位解码，架构 §7.1）、owned-by-body-K（若属某刚体）、所在世界坐标 (x,y)、chunk 坐标与 chunk 局部坐标、该 chunk 的 dirty rect 与 sleep 状态。纯只读（读 Simulation/Physics 的 read-only API）。支持「跟随鼠标实时探查」与「点选锁定」两模式。

### 3.6 调试可视化叠层（`DebugOverlayController` + `DebugOverlayPanel`，架构 §17.2）

面板提供每种叠层的开关（可叠加）。叠层分两类实现，均**不实现子系统逻辑**，只消费数据：

- 矢量叠层（由 Editor 用 ImGui 前景 draw list、经相机 world→screen 变换绘制）：dirty rect 边框、chunk 网格 + parity 着色（4-pass 分区可视）、KeepAlive 唤醒热点（读 Simulation 暴露的本帧 KeepAlive 事件/计数）、自由粒子轨迹（读 `plan/05` 粒子缓冲位置/速度）、CCL 连通块着色（读 `plan/06` 连通块/刚体轮廓）。
- 逐 cell 着色叠层（经 Rendering 的 **debug overlay 着色钩子**实现，Editor 只切换模式 + 传参，逐 cell 上色在 GPU/渲染相位完成，避免 Editor 触碰网格热数据）：cell parity 位、温度热力图、owned-by-body-K 着色。

`DebugOverlayController` 暴露 `OverlayFlags` 给 Rendering（`plan/08` 须提供 `SetDebugOverlay(mode, params)` 钩子）。叠层直接对应架构 §2 列出的最易错区（边界 KeepAlive、parity、跨界反应、刚体往返）。

### 3.7 性能 HUD（`PerformanceHudPanel`，架构 §17.1）

读 `plan/02` 诊断/分项计时器快照，展示：每相位耗时（particle 沉积 / CA pass A–D / heat / physics step / 形状重建 / render / upload / audio 派发）、活跃 chunk 数、活跃 cell 数、自由粒子数、刚体数、常驻 chunk 数 + 估算内存（架构 §12.2）、当前 sim 频率（60/30Hz，§4.2）、帧时间与是否处于时间膨胀/降级。HUD 必须把每帧总墙钟拆成 CPU busy、GPU elapsed、present/vsync wait 三类：CPU busy 来自 `Stopwatch.GetTimestamp` 的主相位/子相位；GPU elapsed 来自 OpenGL timer query 异步回读（不可用时显式标注 unavailable，绝不用 CPU 秒表冒充 GPU）；`SwapBuffers` 阻塞单列为 present-wait 非工作时间，不并入 render work。HUD 显示 VSync 状态并提供运行时开关，显示扣除等待后的有效 FPS，并给出 CPU-bound/GPU-bound/vsync-bound/present-bound/balanced 的瓶颈摘要。用 **ImPlot** 画滚动耗时曲线与相位堆叠条；显示当前 §4.3 过载降级级别。HUD 只读性能快照；除 VSync 开关外不写子系统。

### 3.8 材质 + 反应实时编辑器（`MaterialReactionEditorPanel`，架构 §17.4，不变式 #8）

ImGui 表格编辑 `MaterialDef`（Name/CellType/Density/Dispersion/Flammability/相变阈值 + 目标/HeatConduct/HeatCapacity/纹理/音效等，架构 §7.3）与 `Reaction`（InputA/B、OutputA/B、Probability、Flags，含 `[tag]` 展开预览，架构 §7.4），编辑后写回 `content/materials.json`/`reactions.json` 并**触发热重载**（经 `plan/04` Content 的热重载 API）。

**id 稳定规则（强制，#8、架构 §11.2/§17.4）**：

- 增量/稳定分配——保留既有 `Name→id` 映射；新增材质追加新 id；**绝不重排 id**。
- 删除材质作 **tombstone**，并把 live 网格中引用被删 id 的活 cell **重映射到声明的 fallback**（如 `unknown_solid` 或 `Empty`）。
- 改语义/概率/反应表 → 整表重建，下一帧生效；改材质纹理/音效 → 重新加载对应资产（不动 id）。
- 重载后输出诊断：**「重载后用 fallback 替换了 N 个被删材质的活 cell」**，显示于面板与控制台。
- 编辑器对运行时数值 id 只读展示、绝不允许手工指定/重排（id 是内部索引，入盘的永远是 Name）。

可选用 ImNodes 以节点图方式编辑反应（备用）。

**demo-playability 新增可玩性 / 视觉字段编辑（`plan/04` §3.2 `MaterialDef` 扩展）**：材质表新增可编辑列——可玩性字段 `Durability`（被 sim 真实消费的抗性系数）/`MaxIntegrity`/`RubbleTarget`（破坏后转化目标，写稳定 name、加载期经 `MaterialTable` 解析为 id，守 #8）/`DebrisCount`/`MineYield`/`FlowRate`（语义锚定既有 `Dispersion`，加载期 clamp `<= EngineConstants.MoveCap`，守 #4），与视觉辨识字段 `RenderStyle`/`LegendCategory`/`OutlineColorBGRA`/`Alpha`/`FlowTintBGRA`/`DisplayName`/`LegendVisible`。这些字段**只存 `MaterialDef`、绝不写回 cell**（渲染相位 CPU 算 BGRA，守 #7）；编辑后同 §3.8 现有路径写回 `content/materials.json` 并触发 `plan/04` 的 **id 稳定热重载（`ReloadStable`）**——保留既有 `Name→id` 映射、绝不重排 id，视觉/可玩性字段变更为整表值刷新、下一帧生效，`RubbleTarget` 的 name→id 解析在重载期完成并对缺失目标走 fallback（复用 §3.8 tombstone/fallback 计数诊断）。

**只读 `MaterialLegendPreview`（编辑器内材质图例预览，作者调参对照）**：材质编辑器面板内嵌一个只读预览区，按 `LegendCategory` 分组列出全材质的 swatch（`BaseColor`/`OutlineColorBGRA` 描边 + `Alpha` 半透 + `FlowTintBGRA` 流动着色预览）与关键可玩性数值（`MaxIntegrity`/`FlowRate`/`RenderStyle`），供作者边调参边核对视觉辨识度。**它与玩家可见的材质图例 HUD（`plan/13` §3.14 `MaterialLegendHud`，游戏内、经 `IGuiContext`）是两套独立 UI**：`MaterialLegendPreview` 是编辑器调参工具、只读、不进玩家包；`MaterialLegendHud` 是玩家运行时 UI、走玩家 HUD 输入路径。二者只共享同一份 `MaterialDef` 视觉字段作为数据源，互不复用输入路径或渲染代码。

### 3.9 实体 / 脚本 Inspector（`ScriptInspectorPanel`，配合 `plan/11`）

反射展示/编辑当前选中实体上脚本组件的**公开字段/属性**（`plan/11` 提供反射元数据 + get/set 安全访问；编辑器不直接反射用户私有成员）。支持基础类型、向量、枚举、材质引用、滑条范围特性。提供「触发脚本热重载」按钮（经 `plan/11` 的 Roslyn + ALC 热重载入口），重载后保留/重映射可序列化字段值。变更在帧边界应用，避免与脚本执行相位竞争。

### 3.10 资源浏览器（`AssetBrowserPanel`）

浏览 `content/` 资产树：材质纹理（缩略图）、音效（可试听，经 `plan/10` Audio）、场景/默认世界文件、materials.json/reactions.json。支持筛选/搜索、显示元信息、选中后联动 `EditorSelection`（材质纹理 → 材质编辑器，场景 → 加载）。缩略图生成可并行（离线工作池）。只读浏览 + 触发加载，不实现资产管线逻辑。

### 3.11 场景 / 世界层级面板（`SceneHierarchyPanel`）

列出当前世界中的 Demo 实体（玩家、敌人、物品等，经 `plan/11`/Hosting 的实体注册表）与活跃刚体（经 `plan/06`）；支持选择 → 联动 Inspector 与世界视口聚焦；显示实体启用状态。纯展示/选择，不持有玩法逻辑。

### 3.12 sim 控制条（`SimulationControlToolbar`，配合 `plan/02` 时间 + 架构 §4，不变式 #6）

控件：Play / Pause / **单步（Step）** / sim 频率切换（60/30Hz 降频，§4.2）。语义严格遵守不变式 #6：

- Pause = 跳过 sim/physics 相位（[3]–[8]）但渲染相位（[9]–[10]）继续出帧（复用上次世界纹理，§4.2）。
- 单步 = **恰好执行一个 sim tick（dt 固定 = 1/60，至多一步）后立即回到 Pause**，绝不用 accumulator 追帧、绝不一帧跑多步。
- 降频 = 设 sim 为 30Hz（dt=1/30），render 仍每帧出帧。
这些控制经 Hosting/`plan/02` 的时钟/帧节奏 API 设置「本帧是否执行 sim step」标志，Editor 不自行驱动 sim step。

### 3.13 存读档 UI（`SaveLoadPanel`，配合 `plan/07`）

保存当前世界为存档点 / 加载存档 / 列出已有存档点（含时间戳、世界种子、版本号）；触发整世界一致快照（在帧边界/暂停点执行，架构 §11.5）。经 `plan/07` 序列化 API；显示读档时的 material name↔id 重映射结果与版本迁移信息（含缺失材质 fallback 计数，#8/§11.2）。只调用序列化 API，不实现存档格式。

### 3.14 子系统调参面板

- `PhysicsTuningPanel`（`plan/06`）：实时调参 subStep、Box2D world 重力、碎片像素下限；物理尺度与 task 桥 workerCount 属创建期/全局约束，仅只读展示，形状重建节流在 `plan/06` 暂无真实运行时 API 时不伪装为热改；显示刚体/接触/形状统计。
- `ParticleTuningPanel`（`plan/05`）：最大活跃粒子数、重力、max-lifetime、沉积速度阈值、抛射冲量倍率、单 tick 抛射上限；显示活跃/泄漏统计。
- `LightingTuningPanel`（`plan/08`）：emissive 强度、bloom 阈值/迭代、fog-of-war 参数、dither/gamma、Radiance Cascades 开关（§9.4）。
所有面板只写各子系统暴露的 tuning 参数（read-write 调参接口），不改其内部结构。

### 3.15 编辑器 / 运行游戏模式切换（`EditorMode`，类 Unity Play 模式）

`EditorMode { Edit, Play }`：

- Edit 模式：画刷/检视器/叠层工具激活；sim 默认可暂停；输入优先给编辑器工具（受 ImGui capture 仲裁）。
- Play 模式：输入交给 Demo/脚本（玩家控制器），编辑工具让位；调试叠层与 HUD 仍可显示；可随时切回 Edit。
- 进入 Play 可选「以当前世界态运行」或「从存档点临时副本运行、退出还原」（后者经 `plan/07` 快照），避免编辑态被运行修改污染。
模式切换经 Hosting 的运行状态 API；切换不破坏帧节奏（#6）。

### 3.16 编辑器 ImGui 与游戏 HTML UI（`plan/20`）共存

编辑器 ImGui 与 `PixelEngine.UI`（`plan/20`，游戏内 HTML 大 UI）同处一个 GL 上下文、同在世界渲染（相位 [10]）之后叠加，须建立确定性协作，不靠订阅顺序隐式决定叠放：

- **合成次序（UI 先 / 编辑器后）**：经 `plan/08` 新增的**显式带序号 / 优先级的 UI 层注册接口**（替代脆弱的多播订阅顺序），叠放顺序固定为 **世界 → 游戏 HTML UI（`plan/20`）→ 编辑器 ImGui（本文件）**——编辑器面板恒绘制在游戏 UI 之上，便于开发期覆盖调试。两订阅者各自 `Composite`/`Render` 后恢复 GL 状态（program/blend/scissor/vao），互不污染（呼应 §3.3 GL 状态恢复）。发行构建禁用编辑器后仅 HTML UI 存在，零编辑器开销（守 §1）。
- **输入仲裁**：沿用 §3.3 的多级优先级链 **编辑器 ImGui > 模态游戏 UI > 非模态 HUD > 世界**。
- **Play 让位**：进入 Play 模式，编辑器工具让位（仅保留调试叠层/HUD 显示），游戏 HTML UI 接管玩家交互（呼应 §3.15 `EditorMode`）。
- **性能 HUD 增 UI 分项**：`PerformanceHudPanel`（§3.7）新增 `ui.update` / `ui.paint` / `ui.upload` / `ui.composite` 四个分项计时口径（读 `plan/02` 诊断快照中 `plan/20` 上报的 UI 相位），区分 UI 逻辑更新（相位 0/1）与光栅化 / 纹理上传 / 合成（相位 10），使 UI 尖刺可与世界相位区分定位；UI 相位仅脏 / 动画时执行，尖刺只掉渲染帧不违 #6（呼应 `plan/16`）。

---

## 4. 实现清单

ImGui 集成与框架：
- [x] `PixelEngine.Editor` 项目建立，引用各子系统公开 API，依赖方向符合 `plan/00 §5`（无反向依赖）（§2）
- [x] `ImGuiController`：创建/销毁 ImGui context、字体 atlas → GL 纹理、`ImDrawData` → GL3 draw call、渲染后恢复 GL 状态（§3.3）
- [x] ImGui GL 后端**复用 Rendering 的同一 `GL` 实例与 GL context**，在架构相位 [10] 之后、present 之前绘制（§3.1、§3.3）
- [x] `ImGuiInputBridge`：Silk.NET 鼠标/键盘/滚轮/文本 → ImGuiIO；按 `WantCaptureMouse/Keyboard` 仲裁 UI 与世界输入（§3.3）
- [x] 启用 **docking**（`DockingEnable`）；`EditorDockSpace` 主机窗口 + 默认布局 + 布局保存/恢复（§3.3）
- [x] `EditorFontManager`：加载含 **中文 CJK glyph range** 的字体 + 拉丁/标点，支持 DPI 缩放与字号切换（§3.3）
- [x] 接入 **ImGuizmo / ImPlot**（ImNodes 可选）作为备用绘图/编辑控件（§2）
- [x] `EditorApp` 门面 + `IEditorPanel` 接口 + `EditorContext` + `EditorSelection`（§3.2）
- [x] `EditorFramePhase` 挂入 Hosting 主循环；世界经 Rendering 离屏 FBO 纹理显示于「世界视口」面板（`ViewportPanel`）（§3.1）
- [x] 编辑器整体可经编译/运行时开关禁用，禁用后零开销（§1）

世界编辑与检视：
- [x] `MaterialBrushPalettePanel`：画/挖/橡皮/温度笔刷 + 笔刷大小/形状/概率 + 材质选择（缩略图）（§3.4）
- [x] 画刷写入经 `ISimulationEditApi`，落相位 [1]，写后标 dirty + KeepAlive；不存 RGBA 入 cell（#4、#7、§3.4）
- [x] 温度笔刷读写 `plan/04` 粗温度场（§3.4、架构 §7.5）
- [x] `WorldInspectorPanel`：点选/跟随 cell → material(id+Name)/temperature/Flags 逐位/owned-by-body-K/坐标/chunk 信息/dirty rect/sleep（§3.5）

调试可视化叠层（架构 §17.2）：
- [x] `DebugOverlayController` + `DebugOverlayPanel`：每种叠层独立可叠加开关（§3.6）
- [x] dirty rect 边框叠层（矢量，world→screen）（§3.6）
- [x] chunk 网格 + parity 着色叠层（4-pass 分区可视）（§3.6）
- [x] KeepAlive 唤醒热点叠层（读 Simulation KeepAlive 事件/计数）（§3.6）
- [x] cell parity 位叠层（经 Rendering debug 着色钩子）（§3.6）
- [x] 温度热力图叠层（经 Rendering debug 着色钩子）（§3.6）
- [x] owned-by-body-K 着色叠层（经 Rendering debug 着色钩子）（§3.6）
- [x] 自由粒子轨迹叠层（读 `plan/05` 粒子缓冲）（§3.6）
- [x] CCL 连通块着色叠层（读 `plan/06` 连通块/轮廓）（§3.6）

性能 HUD（架构 §17.1）：
- [x] `PerformanceHudPanel`：每相位耗时（particle/CA A–D/heat/physics/形状重建/render/upload/audio）（§3.7）
- [x] 性能 HUD 切片 1：CPU busy / OpenGL GPU elapsed / present-wait 三分口径跑通；`PresentWait` 不计入 render work；HUD 显示 VSync 状态、运行时开关、有效 FPS 与瓶颈摘要（§3.7）
- [x] 性能 HUD 切片 2：滚动窗口 avg/p50/p95/p99/max、帧时间历史波动图、尖刺/稳态标注、有效帧耗时统计、负载计数趋势与静态/动态成本结构面板（§3.7）
- [x] 活跃 chunk 数 / 活跃 cell 数 / 自由粒子数 / 刚体数 / 常驻 chunk 数 + 估算内存（§3.7、架构 §12.2）
- [x] 当前 sim 频率（60/30Hz）+ 时间膨胀/§4.3 降级级别显示（§3.7）
- [x] 用 ImPlot 绘制滚动耗时曲线与相位堆叠条（§3.7）
- [x] HUD 仅读 `plan/02` 诊断/计时器快照，零写入（§3.7）

材质 + 反应实时编辑器（架构 §17.4，#8）：
- [x] `MaterialReactionEditorPanel`：ImGui 表格编辑 `MaterialDef` 全字段（§3.8、架构 §7.3）
- [x] ImGui 表格编辑 `Reaction`（输入/输出/概率/Flags + `[tag]` 展开预览）（§3.8、架构 §7.4）
- [x] 编辑写回 `content/*.json` 并经 `plan/04` 触发热重载（§3.8）
- [x] id 稳定：保留既有 Name→id、新增追加、**绝不重排**（#8、§3.8）
- [x] 删除材质作 tombstone，并把活 cell 重映射到声明 fallback（§3.8）
- [x] 改纹理/音效仅重载资产、不动 id（§3.8）
- [x] 输出诊断「重载后用 fallback 替换了 N 个被删材质的活 cell」（§3.8、架构 §17.4）
- [x] 运行时数值 id 只读展示，禁止手工指定/重排（#8、§3.8）
- [x] 材质编辑器新增可玩性字段列（`Durability`/`MaxIntegrity`/`RubbleTarget`/`DebrisCount`/`MineYield`/`FlowRate`）编辑，`RubbleTarget` 写稳定 name、加载期解析为 id，`FlowRate` clamp `<= EngineConstants.MoveCap`（守 #4/#8、§3.8、`plan/04` §3.2）。证据：`MaterialReactionEditorPanel` 主表列与详情区使用计划语义名；`MaterialEditorRowAliasesGameplayAndVisualFieldNames`、`ApplyFallsBackMissingRubbleTargetWithoutReorderingRuntimeIds`、`LoadFallsBackMissingDestroyedTargetToEmpty`。
- [x] 材质编辑器新增视觉辨识字段列（`RenderStyle`/`LegendCategory`/`OutlineColorBGRA`/`Alpha`/`FlowTintBGRA`/`DisplayName`/`LegendVisible`）编辑，仅存 `MaterialDef` 不写回 cell（守 #7、§3.8）。证据：编辑器别名写回现有 Content schema 的 `edgeColor`/`opacity`/`highlightColor`，渲染字段仍只存在 `MaterialDef`/VisualTable，不写入 cell；`MaterialEditorDocumentRoundTripsPlayableAndVisualFields` 与 `MaterialEditorRowAliasesGameplayAndVisualFieldNames`。
- [x] 新增字段编辑后经 `plan/04` `ReloadStable` id 稳定热重载生效，`RubbleTarget` 缺失走 fallback 并复用 tombstone 计数诊断（#8、§3.8）。证据：`FileMaterialReactionContentService.Apply` 仍走 `BuildStableReload`/`ReloadStable`；缺失 `RubbleTarget` 在 Content loader 解析为 fallback 0 且 id 不重排；删除材质 live-grid tombstone 计数诊断沿用既有 `MaterialReactionApplyResult.DiagnosticMessage`。
- [x] 只读 `MaterialLegendPreview`：按 `LegendCategory` 分组的全材质 swatch（描边/alpha/flowTint 预览）+ 关键可玩性数值，与玩家 `MaterialLegendHud`（`plan/13` §3.14）为两套独立 UI、只共享 `MaterialDef` 数据源（§3.8）。证据：`MaterialLegendPreview`/`MaterialLegendPreviewEntry` 从编辑文档构建只读条目，`MaterialLegendPreviewGroupsAllMaterialsWithSwatchAndGameplayValues` 与 `MaterialLegendPreviewIsEditorOnlyAndDoesNotReusePlayableHud` 覆盖。

Inspector / 浏览 / 层级：
- [x] `ScriptInspectorPanel`：反射展示/编辑脚本组件公开字段（配合 `plan/11` 元数据），支持基础/向量/枚举/材质引用/范围滑条（§3.9）
- [x] Inspector 触发脚本热重载（经 `plan/11` Roslyn+ALC），重载后保留/重映射字段值（§3.9）
- [x] `AssetBrowserPanel`：浏览 `content/` 材质纹理(缩略图)/音效(试听)/场景/JSON，筛选搜索，选中联动（§3.10）
- [x] `SceneHierarchyPanel`：Demo 实体 + 活跃刚体列表，选择联动 Inspector/视口聚焦（§3.11）

sim 控制 / 存读档 / 调参 / 模式：
- [x] `SimulationControlToolbar`：Play/Pause/单步/60-30Hz 降频，语义严格符合 #6（单步=恰一 tick、不追帧）（§3.12、架构 §4）
- [x] sim 控制经 Hosting/`plan/02` 时钟设「本帧是否执行 sim step」，Editor 不自驱 step（§3.12）
- [x] `SaveLoadPanel`：保存/加载/存档点列表（时间戳/种子/版本），经 `plan/07`，帧边界一致快照（§3.13、架构 §11.5）
- [x] 存读档显示 name↔id 重映射 + 版本迁移 + 缺失材质 fallback 计数（§3.13、#8）
- [x] `PhysicsTuningPanel`（`plan/06`）：经 `PhysicsSystemTuningService` 实时应用 subStep、Box2D world 重力、碎片阈值；scale/workerCount 按创建期约束只读展示，不伪装为热改（§3.14）
- [x] `ParticleTuningPanel`（`plan/05`）：经 `ParticleSystemSettings` / `ParticleSystemTuningService` 实时应用最大活跃数、重力、max-lifetime、沉积速度阈值、抛射冲量倍率、单 tick 抛射上限（§3.14）
- [x] `LightingTuningPanel`（`plan/08`）：emissive/bloom/fog-of-war/dither/gamma/Radiance Cascades 开关（§3.14、架构 §9.4）
- [x] `EditorMode` 状态机（Edit/Play）+ 输入仲裁 + 切换经 Hosting 运行状态 API（§3.15）
- [x] Play 模式可选「当前态运行」或「存档点临时副本运行、退出还原」（经 `IEditorPlaySnapshotStore` 接入 `plan/07` 存读档服务）（§3.15）

GUI 宿主中性化与壳/UI 共存（跨 `plan/19`/`plan/20` 前置，见 §1、§3.16）：
- [x] 玩家 HUD ImGui host 下沉至中性程序集 `PixelEngine.Gui`：`HexaImGuiBackend`、`IGuiContext` 运行时适配（`ScriptGuiContext`）、`GuiRenderBridge`、`GuiFontManager`（含 CJK）已迁入；Hosting/Demo 使用 Gui 承载玩家 HUD，玩家包不再经 `PixelEngine.Editor`（§1）
- [x] 面板层由 `apps/PixelEngine.Editor.Shell`（开发构建）经公开 `IEditorHostExtension` / `EditorShellHostExtension` 注入；本文件面板实现保持位于 Hosting 之下、不反向依赖壳（§1 职责边界、`plan/19`）
- [x] 多级输入仲裁器：编辑器 ImGui > 模态游戏 UI（`plan/20`）> 非模态 HUD > 世界，单一真相供 `plan/13`/`plan/20` 复用，Play 模式下编辑器降级让位；`InputArbitrator` / `InputArbitrationState` 统一合并 `IEditorInputCaptureSource`、`GuiInputSnapshot` 与 `UiInputCapture`，`Engine.ResolveGuiInputRoute` 只消费该单一结果；`InputArbitratorTests`、`EngineWindowOwnershipTests.ResolveGuiInputRouteAppliesNeutralEditorCaptureBeforeGameUiBySourceContract` 与 `UiInputRouterTests` 覆盖 Editor 优先、Play 让位、HTML UI 捕获与上游 capture 截断（§3.3、§3.16）
- [x] 编辑器 ImGui 与游戏 HTML UI（`plan/20`）合成次序经 `plan/08` 显式 UI 层注册接口固定为 世界→HTML UI→编辑器，各自 `Render` 后恢复 GL 状态；`RenderPipeline.RegisterUiLayer` / `UiPresentLayerOrders` 固定 Game < Editor，`GuiRenderBridge`/`UiLayerCompositor` 注册 Game 层、`EditorRenderBridge` 注册 Editor 层，`RenderPipelineContractTests.UiPresentLayersUseStableOrdersForGameAndEditor` 覆盖稳定排序与 GL 状态快照恢复（§3.16）
- [x] `PerformanceHudPanel` 新增 `ui.update`/`ui.paint`/`ui.upload`/`ui.composite` 分项计时口径，区分 UI 逻辑（相位 0/1）与光栅化/上传/合成（相位 10）：`FrameSubPhase`/`EngineCounters`/HUD 采样已分列 `ui.upload`；当前 RmlUi/ManagedFallback 无上传型后端时真实值为 0，native/Ultralight 脏矩形上传实现继续归 `plan/20` 后续切片（§3.7、§3.16）

---

## 5. 验收标准

- [x] ImGui 在 Rendering 的同一 GL context 上、世界渲染之后顶层绘制；渲染后 GL 状态被正确恢复，世界画面无被 UI 污染（§3.3）
- [x] docking 生效：面板可自由停靠/浮动/拖拽；默认布局可保存并在重启后恢复（§3.3）
- [x] 中文 UI 文本正常显示无缺字/方块；DPI 缩放下字体清晰（§3.3）
- [x] 画刷在 Edit 模式可画/挖/擦/加温；写入仅落相位 [1]、标 dirty + KeepAlive；与 CA 多线程相位无竞争、无边界像素消失/复制（#1/#2/#4、§3.4）
- [x] 世界检视器点选任一 cell 正确显示 material/temperature/Flags 逐位/owned-by-body/坐标/chunk/dirty/sleep（§3.5）
- [x] 八种调试叠层（dirty rect/chunk+parity/KeepAlive/cell parity/温度热图/owned-by-body/粒子轨迹/CCL）均可独立切换并与世界对齐，能复现并定位边界/parity/刚体往返问题（架构 §17.2、§3.6）
- [x] 性能 HUD 各相位耗时、计数、内存、sim 频率、降级级别与诊断快照一致；CPU/GPU/present-wait 三分口径、预热剔除滚动百分位、尖刺/稳态标注与负载成本结构已接入；真实窗口静态/高活跃场景各 720 帧、预热 120 帧、稳态 600 帧样本已记录于 `docs/runtime-reports/2026-07-04-performance-hud-steady-window-samples.md`（架构 §17.1、§3.7）
- [x] 材质/反应编辑后热重载即时生效；新增材质追加 id、删除材质 id 不被复用、既有 id 不重排（#8、§3.8）
- [x] 删除被使用中的材质后，live 网格引用 cell 被替换为 fallback，并输出「替换了 N 个活 cell」诊断；不出现 id 错位损坏（#8、架构 §17.4）
- [x] 改材质纹理/音效仅重载资产，运行时 id 不变（§3.8）
- [x] 脚本 Inspector 正确反射展示/编辑公开字段；触发热重载后字段值保留/合理重映射（§3.9、配合 `plan/11`）
- [x] 资源浏览器列出 `content/` 资产并可选中联动；音效可试听；缩略图正确（§3.10）
- [x] 层级面板列出实体/刚体并可选择联动 Inspector 与视口聚焦（§3.11）
- [x] sim 控制条：Pause 停止 sim 但持续出帧；单步**恰执行一个 tick** 后回到暂停，绝不追帧/不多步；30Hz 降频时 render 仍每帧出帧（#6、§3.12、架构 §4.2）
- [x] 存读档：保存→加载后世界逐 cell 等价；改 materials.json 顺序/增删后旧档正确重映射；缺失材质走 fallback 并提示（#8、架构 §11.2、§3.13）
- [x] 三个调参面板的可编辑参数改动实时作用于对应子系统且不破坏其不变式；Box2D scale/workerCount 等创建期约束只读展示，不作为可编辑参数（§3.14）
- [x] Edit/Play 模式切换正确仲裁输入；「临时副本运行」退出后经 `IEditorPlaySnapshotStore` 恢复世界；切换不破坏帧节奏（#6、§3.15）
- [x] Editor 仅消费各子系统公开 read-only/tuning API，无任何对子系统内部类型/字段的直接访问；无反向依赖（§1、§2）
- [x] 编辑器禁用开关关闭后，主循环无 ImGui/叠层开销（§1）
- [x] 材质编辑器可编辑 demo-playability 全部可玩性/视觉字段并经 `ReloadStable` 热重载生效、id 不重排、`RubbleTarget` 缺失走 fallback；`FlowRate` 超 `MoveCap` 被 clamp（#4/#7/#8、§3.8）
- [x] 只读 `MaterialLegendPreview` 正确按类别分组预览全材质 swatch 与描边/alpha/flowTint/关键数值，且与玩家 `MaterialLegendHud` 输入/渲染路径互不复用（§3.8）
- [x] 玩家 HUD ImGui host（backend/`IGuiContext`/字体栈）已下沉 `PixelEngine.Gui`，Demo/Hosting 不再经 Editor 承载玩家 HUD；面板层经 `IEditorHostExtension` 由壳注入且无反向依赖（§1）
- [x] 编辑器 ImGui 与游戏 HTML UI（`plan/20`）确定性叠放（世界→HTML UI→编辑器）、各自恢复 GL 状态无互相污染；多级输入仲裁按 编辑器>模态 UI>非模态 HUD>世界 生效，Play 模式编辑器让位；验证：`dotnet test tests\PixelEngine.Hosting.Tests\PixelEngine.Hosting.Tests.csproj -c Release --filter "InputArbitratorTests|EngineWindowOwnershipTests"`、`dotnet test tests\PixelEngine.Rendering.Tests\PixelEngine.Rendering.Tests.csproj -c Release --filter "RenderPipelineContractTests"`、`dotnet test tests\PixelEngine.UI.Tests\PixelEngine.UI.Tests.csproj -c Release --filter "UiInputRouterTests"`（§3.3、§3.16）
- [x] 性能 HUD 显示 `ui.update`/`ui.paint`/`ui.upload`/`ui.composite` 四分项且与 `plan/20` 上报口径一致：`ui.update`/`ui.paint`/`ui.upload`/`ui.composite` 与 cadence/skipped 已对齐 `EngineCounters`/`FrameSubPhase`，无上传型后端 `ui.upload=0`，native/Ultralight 真实脏矩形上传实现仍归 `plan/20`（§3.7、§3.16）

---

## 6. 依赖关系

前置（必须先具备其公开 API）：

- `plan/00`（技术栈，ImGui/Silk.NET 选型）、`plan/01`（项目骨架、CPM）。
- `plan/02`（Core）：诊断/分项计时器快照、时间/帧节奏标志、事件总线。
- `plan/03`（CA 内核）：`ISimulationEditApi`（画刷写 cell + dirty/KeepAlive）、cell/chunk/dirty-rect/parity/KeepAlive 只读访问。
- `plan/04`（材质/反应/温度）：运行时材质表、Content 热重载 API、温度场读写、id 稳定/重映射设施。
- `plan/05`（粒子）：粒子缓冲只读 + 调参。
- `plan/06`（物理）：刚体/连通块/owned-by-body 只读 + 调参 + task 桥参数。
- `plan/07`（世界/流式/存档）：存档保存/加载/存档点列表、name↔id 重映射、版本迁移、一致快照、临时副本。
- `plan/08`（Rendering）：**共享 GL context/`GL` 实例**、离屏世界 FBO 纹理、debug overlay 逐 cell 着色钩子、光照调参、Silk.NET 输入事件源。
- `plan/10`（Audio）：音效试听。
- `plan/11`（脚本）：组件字段反射元数据 + 安全 get/set、Roslyn+ALC 热重载入口、实体注册表。
- Hosting：主循环相位挂载点、运行状态（Edit/Play、sim run/step）API。

被依赖：`plan/13`（Demo 借编辑器调参/调试，但 Demo 仅依赖引擎公开 API）；`plan/14`（测试可验证编辑器读 API 的正确性）；`plan/19`（`apps/PixelEngine.Editor.Shell` 复用并注入本文件面板层，经 `RegisterDefaultEditorPanels`）；`plan/20`（`PixelEngine.UI` 与编辑器 ImGui 共存，复用 §3.3 输入仲裁与 `PixelEngine.Gui` 字体栈）。

新增关联（本轮）：

- `PixelEngine.Gui`（新增中性程序集）：玩家 HUD ImGui host（`HexaImGuiBackend`）、`IGuiContext` 运行时适配（`ScriptGuiContext`）、`GuiRenderBridge`、`GuiFontManager`（含 CJK）下沉至此，位于 Rendering 之上、Editor 之下；Editor 可消费其中性调试/字体策略类型，但保留面板层专用 backend / dockspace / bridge。此中性化是 `plan/19` 壳注入、`plan/15` 玩家包 player-only 审计、`plan/20` UI 字体/回退复用三者的共同前置。
- `plan/04`（材质）：`MaterialDef` 新增可玩性/视觉字段与 `ReloadStable` id 稳定热重载 API（供 §3.8 材质编辑器编辑与 `MaterialLegendPreview` 预览）。
- `plan/08`（Rendering）：显式带序号/优先级的 UI 层注册接口（供编辑器与 `plan/20` HTML UI 确定性叠放，替代多播订阅顺序）。

并行关系：本文档须在所消费 API 就绪后实施；各面板之间可并行开发。精确顺序见 `plan/17`。

风险/阻塞：若某子系统未暴露所需 read-only/tuning API（如 Rendering 的 debug 着色钩子、Simulation 的 KeepAlive 事件、`plan/11` 的字段反射），按 `AGENTS.md §2` 标 `- [!] 阻塞：原因` 并由对应 plan 补 API，不在 Editor 内访问内部类型绕过。

---

## 7. 提交节点

按 `AGENTS.md §6`，每个节点完成即用中文 git 提交（scope=`editor`）：

- [x] 节点 1：`feat(editor): 集成 Hexa.NET.ImGui(GL 后端+输入桥+docking+中文字体)`（§3.1–§3.3，实现清单「ImGui 集成与框架」全部）
- [x] 节点 2：`feat(editor): 世界画刷调色板与世界检视器`（§3.4–§3.5）
- [x] 节点 3：`feat(editor): 调试可视化叠层(架构 §17.2)`（§3.6）
- [x] 节点 4：`feat(editor): 性能 HUD(架构 §17.1)`（§3.7）
- [x] 节点 5：`feat(editor): 材质+反应实时编辑器与 id 稳定热重载(架构 §17.4)`（§3.8）
- [x] 节点 6：`feat(editor): 脚本 Inspector/资源浏览器/层级面板`（§3.9–§3.11）
- [x] 节点 7：`feat(editor): sim 控制条/存读档 UI/子系统调参面板`（§3.12–§3.14）
- [x] 节点 8：`feat(editor): 编辑/运行(Play)模式切换`（§3.15）
- [x] 节点 9：`docs(plan): 勾选 plan/12 验收并更新 README 进度`（§5 全部通过后）
- [x] 节点 10：`refactor(gui): 玩家 HUD ImGui host/IGuiContext/字体栈下沉 PixelEngine.Gui 中性程序集`（§1、§6，`plan/19`/`plan/15`/`plan/20` 共同前置）
- [x] 节点 11：`feat(editor): 材质编辑器可玩性/视觉字段编辑+MaterialLegendPreview+ReloadStable 热重载`（§3.8）
- [x] 节点 12：`feat(editor): 多级输入仲裁与 HTML UI(plan/20)共存合成次序+性能 HUD ui.* 分项`：输入仲裁、合成次序与 HUD `ui.update`/`ui.paint`/`ui.upload`/`ui.composite` 独立口径已完成；native/Ultralight 脏矩形上传实现继续由 `plan/20` 节点 8/10 跟进（§3.3、§3.16）
