# Plan 12 — 引擎内嵌编辑器与管理 UI（PixelEngine.Editor）

> 本文档定义 `PixelEngine.Editor` 子系统的完整实现计划：基于 **Dear ImGui（Hexa.NET.ImGui）** 的引擎内嵌、停靠式（docking）编辑器与全套管理/调试 UI。
> 权威依据：`../docs/PixelEngine-架构与需求设计.md`（下称「架构」，重点 §17、§3.3、§4、§7、§11）；技术栈：`00-conventions-and-techstack.md`；开发宪法：`../AGENTS.md`。
> 状态：`- [ ]` 未开始 / `- [x]` 完成并自测 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。

---

## 1. 目标与范围

`PixelEngine.Editor` 是引擎内嵌的、类似 Unity Editor 的可视化工具层：它把 sim 的不可见态（dirty rect、parity、活跃 chunk、owned-by-body）变为可视、可交互；提供材质/反应实时编辑、世界画刷、检视器、性能 HUD、存读档与各子系统调参面板；并提供「编辑 / 运行游戏」模式切换。

锁定决策（本文档核心、不可偏离）：

- 编辑器/管理 UI = **引擎内嵌 Dear ImGui（Hexa.NET.ImGui）停靠式面板**，**与 `plan/08`（Rendering）共享同一个 OpenGL 上下文**，ImGui 在世界渲染（架构帧相位 [10]）之后、`present` 之前绘制于顶层。绝不另起进程 / 另开窗口 / 另建 GL 上下文。
- 编辑器**只消费各子系统的只读 / 调参（read-only / tuning）公开 API，绝不实现任何子系统逻辑**。画刷写 cell 走 Simulation 的公开编辑 API；调试叠层的逐 cell 着色走 Rendering 的 debug overlay 钩子；逐项数据读 `plan/02` 的诊断 / 计时器快照。
- 一步到位，无 MVP：面板、叠层、调参项一次性做全。
- 编辑器是工具层，**不在 sim 热路径内**，但仍遵守「能多线程 / 省内存就上」：叠层几何构建、资源缩略图生成等离线工作可并行；逐帧避免明显的托管堆 churn（预分配缓冲、缓存格式化字符串）。编辑器可在发行构建中通过编译开关 / 运行时开关整体禁用，禁用后零开销。

范围内：ImGui 集成（GL 后端 + 输入桥 + docking + 中文字体 + ImGuizmo/ImPlot 备用）、材质/笔刷调色板、世界检视器、调试可视化叠层、性能 HUD、材质+反应实时编辑器（含 id 稳定热重载）、实体/脚本 Inspector、资源浏览器、场景/世界层级、sim 控制条、存读档 UI、刚体/粒子/光照调参、编辑/运行模式切换。

范围外（仅消费其 API，不在此实现）：CA 内核（`plan/03`）、材质反应温度逻辑（`plan/04`）、粒子（`plan/05`）、物理（`plan/06`）、存档序列化（`plan/07`）、渲染管线（`plan/08`）、脚本编译/热重载机制（`plan/11`）、宿主主循环与帧节奏（Hosting / 架构 §4）。这些子系统必须提供编辑器所需的公开 read-only/tuning API；若缺失，按 `AGENTS.md §2` 标 `- [!] 阻塞` 上报，由对应 plan 补 API，不在 Editor 里开后门访问内部类型。

---

## 2. 技术栈与依赖

- UI 框架：**Dear ImGui via `Hexa.NET.ImGui`**（即时模式、AOT 友好、活跃维护），`Hexa.NET.ImGui.Backends`（OpenGL3 后端）；启用 **docking** 分支特性。备用：`Hexa.NET.ImGuizmo`（变换 gizmo）、`Hexa.NET.ImPlot`（性能曲线绘图）、`Hexa.NET.ImNodes`（反应图，可选）。版本集中于 `Directory.Packages.props`（CPM）。**与 `plan/00` 选型表一致，不另立选型。**
- GL / 窗口 / 输入：复用 `plan/08` 的 `Silk.NET`（`Silk.NET.OpenGL`、`Silk.NET.Input`、`Silk.NET.Windowing`）；ImGui GL 后端绑定 Rendering 暴露的 **同一 `GL` 实例与同一 GL context**。输入经 `Silk.NET.Input` 事件桥接到 ImGui IO（桥接面 `plan/02` 时间/事件与 `plan/08` 输入）。
- 诊断 / 计时：`PixelEngine.Core`（`plan/02`）的诊断与分项计时器快照（架构 §17.1、`plan/00 §7`）。
- JSON：`System.Text.Json + 源生成器`（材质/反应编辑器读写 `content/*.json`，与 `plan/04`/`plan/07` 同一序列化设施）。
- 目标框架/语言：`.NET 10 / C# 14`，`Nullable enable`，file-scoped namespace，命名空间根 `PixelEngine.Editor`。Editor 项目**不**开 `AllowUnsafeBlocks`（非热路径；除 ImGui 后端 buffer 上传必需的最小 unsafe 块外）。
- 依赖方向（`plan/00 §5`，绝不反向）：`Demo → Hosting → { Editor, ... }`。Editor 引用 `Core / Simulation / Physics / World / Serialization / Content / Rendering / Audio / Scripting / Hosting` 的**公开 API**；被引用方绝不反向依赖 Editor。

---

## 3. 详细设计

### 3.1 集成与帧相位

编辑器作为一个 **EditorFramePhase** 挂入 Hosting 主循环（架构 §3.3）：在帧开始处采集输入并 `ImGui.NewFrame()`；在架构相位 [10]（GPU Upload & Render）之后、`present` 之前，由 `ImGuiController.Render()` 把 ImGui draw data 用共享 GL context 绘制到默认 framebuffer 顶层。世界画面通过 Rendering 暴露的**离屏 FBO 纹理**显示在 ImGui 的「世界视口」窗口中（render-to-texture），从而让停靠面板包裹/叠加于世界之上而不互相裁剪。

编辑器对世界的写操作（画刷 set/erase cell、温度笔刷）只在**单线程的输入/玩法相位（[0]/[1]）**、CA（相位 [4]）之前应用，等同架构 M0 的「鼠标画沙」；写后标记所在 chunk dirty 并触发 KeepAlive（经 Simulation 公开编辑 API），绝不在 CA 多线程相位中并发改网格。这保证不破坏不变式 #1/#2/#3（单缓冲原地 + checkerboard 无锁）。

### 3.2 模块与类型

`EditorApp`（顶层门面，由 Hosting 在启用编辑器时装配）持有：`ImGuiController`（GL 后端 + draw data 渲染）、`ImGuiInputBridge`（Silk.NET 输入 → ImGui IO）、`EditorFontManager`（字体/中文）、`EditorDockSpace`（docking 布局）、`EditorMode` 状态机（Edit/Play）、以及 `IEditorPanel` 面板集合与 `DebugOverlayController`。所有面板实现统一接口 `IEditorPanel { string Title; bool Visible; void Draw(in EditorContext ctx); }`。`EditorContext` 是传入各面板的只读句柄集合（Engine 门面、各子系统 read-only/tuning 接口、诊断快照、相机、选中态 `EditorSelection`）。

`EditorSelection` 集中保存当前选中：cell 坐标、实体/脚本句柄、刚体 id、资产路径、材质 id，供检视器与 Inspector 共享。

### 3.3 ImGui 集成

- `ImGuiController`：创建/销毁 ImGui context；上传字体 atlas 为 GL 纹理；每帧把 `ImDrawData` 翻译为 GL3 draw call（VBO/EBO/scissor）。复用 Rendering 的 `GL` 实例，渲染后恢复 GL 状态（program/blend/scissor/vao）避免污染世界渲染状态。开启 `ImGuiConfigFlags.DockingEnable`；视情况开 `ViewportsEnable`（多视口，需 Silk.NET 多窗口支持，作可选）。
- `ImGuiInputBridge`：把 Silk.NET 的鼠标/键盘/滚轮/文本输入事件灌入 `ImGuiIO`；处理 `WantCaptureMouse/WantCaptureKeyboard`——当 ImGui 捕获时，游戏/画刷不消费该输入，反之亦然，避免 UI 与世界交互打架。
- `EditorFontManager`：加载支持 **中文（CJK）** 的字体（如 Noto Sans CJK / 思源黑体子集），构建含 `GetGlyphRangesChineseFull` / 自定义常用字范围 + 拉丁 + 标点的 glyph range；支持 DPI 缩放与字号切换；合并 icon font（可选）。
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

读 `plan/02` 诊断/分项计时器快照，展示：每相位耗时（particle 沉积 / CA pass A–D / heat / physics step / 形状重建 / render / upload / audio 派发）、活跃 chunk 数、活跃 cell 数、自由粒子数、刚体数、常驻 chunk 数 + 估算内存（架构 §12.2）、当前 sim 频率（60/30Hz，§4.2）、帧时间与是否处于时间膨胀/降级。用 **ImPlot** 画滚动耗时曲线与相位堆叠条；显示当前 §4.3 过载降级级别。HUD 只读快照，不写子系统。

### 3.8 材质 + 反应实时编辑器（`MaterialReactionEditorPanel`，架构 §17.4，不变式 #8）

ImGui 表格编辑 `MaterialDef`（Name/CellType/Density/Dispersion/Flammability/相变阈值 + 目标/HeatConduct/HeatCapacity/纹理/音效等，架构 §7.3）与 `Reaction`（InputA/B、OutputA/B、Probability、Flags，含 `[tag]` 展开预览，架构 §7.4），编辑后写回 `content/materials.json`/`reactions.json` 并**触发热重载**（经 `plan/04` Content 的热重载 API）。

**id 稳定规则（强制，#8、架构 §11.2/§17.4）**：

- 增量/稳定分配——保留既有 `Name→id` 映射；新增材质追加新 id；**绝不重排 id**。
- 删除材质作 **tombstone**，并把 live 网格中引用被删 id 的活 cell **重映射到声明的 fallback**（如 `unknown_solid` 或 `Empty`）。
- 改语义/概率/反应表 → 整表重建，下一帧生效；改材质纹理/音效 → 重新加载对应资产（不动 id）。
- 重载后输出诊断：**「重载后用 fallback 替换了 N 个被删材质的活 cell」**，显示于面板与控制台。
- 编辑器对运行时数值 id 只读展示、绝不允许手工指定/重排（id 是内部索引，入盘的永远是 Name）。

可选用 ImNodes 以节点图方式编辑反应（备用）。

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

- `PhysicsTuningPanel`（`plan/06`）：物理尺度（1 单位=px）、subStep、task 桥 workerCount、碎片像素下限、形状重建节流、重力；显示刚体/接触/形状统计。
- `ParticleTuningPanel`（`plan/05`）：最大粒子数、重力、max-lifetime、沉积阈值、抛射冲量；显示活跃/泄漏统计。
- `LightingTuningPanel`（`plan/08`）：emissive 强度、bloom 阈值/迭代、fog-of-war 参数、dither/gamma、Radiance Cascades 开关（§9.4）。
所有面板只写各子系统暴露的 tuning 参数（read-write 调参接口），不改其内部结构。

### 3.15 编辑器 / 运行游戏模式切换（`EditorMode`，类 Unity Play 模式）

`EditorMode { Edit, Play }`：

- Edit 模式：画刷/检视器/叠层工具激活；sim 默认可暂停；输入优先给编辑器工具（受 ImGui capture 仲裁）。
- Play 模式：输入交给 Demo/脚本（玩家控制器），编辑工具让位；调试叠层与 HUD 仍可显示；可随时切回 Edit。
- 进入 Play 可选「以当前世界态运行」或「从存档点临时副本运行、退出还原」（后者经 `plan/07` 快照），避免编辑态被运行修改污染。
模式切换经 Hosting 的运行状态 API；切换不破坏帧节奏（#6）。

---

## 4. 实现清单

ImGui 集成与框架：
- [ ] `PixelEngine.Editor` 项目建立，引用各子系统公开 API，依赖方向符合 `plan/00 §5`（无反向依赖）（§2）
- [ ] `ImGuiController`：创建/销毁 ImGui context、字体 atlas → GL 纹理、`ImDrawData` → GL3 draw call、渲染后恢复 GL 状态（§3.3）
- [ ] ImGui GL 后端**复用 Rendering 的同一 `GL` 实例与 GL context**，在架构相位 [10] 之后、present 之前绘制（§3.1、§3.3）
- [ ] `ImGuiInputBridge`：Silk.NET 鼠标/键盘/滚轮/文本 → ImGuiIO；按 `WantCaptureMouse/Keyboard` 仲裁 UI 与世界输入（§3.3）
- [ ] 启用 **docking**（`DockingEnable`）；`EditorDockSpace` 主机窗口 + 默认布局 + 布局保存/恢复（§3.3）
- [ ] `EditorFontManager`：加载含 **中文 CJK glyph range** 的字体 + 拉丁/标点，支持 DPI 缩放与字号切换（§3.3）
- [ ] 接入 **ImGuizmo / ImPlot**（ImNodes 可选）作为备用绘图/编辑控件（§2）
- [ ] `EditorApp` 门面 + `IEditorPanel` 接口 + `EditorContext` + `EditorSelection`（§3.2）
- [ ] `EditorFramePhase` 挂入 Hosting 主循环；世界经 Rendering 离屏 FBO 纹理显示于「世界视口」面板（`ViewportPanel`）（§3.1）
- [ ] 编辑器整体可经编译/运行时开关禁用，禁用后零开销（§1）

世界编辑与检视：
- [ ] `MaterialBrushPalettePanel`：画/挖/橡皮/温度笔刷 + 笔刷大小/形状/概率 + 材质选择（缩略图）（§3.4）
- [ ] 画刷写入经 `ISimulationEditApi`，落相位 [1]，写后标 dirty + KeepAlive；不存 RGBA 入 cell（#4、#7、§3.4）
- [ ] 温度笔刷读写 `plan/04` 粗温度场（§3.4、架构 §7.5）
- [ ] `WorldInspectorPanel`：点选/跟随 cell → material(id+Name)/temperature/Flags 逐位/owned-by-body-K/坐标/chunk 信息/dirty rect/sleep（§3.5）

调试可视化叠层（架构 §17.2）：
- [ ] `DebugOverlayController` + `DebugOverlayPanel`：每种叠层独立可叠加开关（§3.6）
- [ ] dirty rect 边框叠层（矢量，world→screen）（§3.6）
- [ ] chunk 网格 + parity 着色叠层（4-pass 分区可视）（§3.6）
- [ ] KeepAlive 唤醒热点叠层（读 Simulation KeepAlive 事件/计数）（§3.6）
- [ ] cell parity 位叠层（经 Rendering debug 着色钩子）（§3.6）
- [ ] 温度热力图叠层（经 Rendering debug 着色钩子）（§3.6）
- [ ] owned-by-body-K 着色叠层（经 Rendering debug 着色钩子）（§3.6）
- [ ] 自由粒子轨迹叠层（读 `plan/05` 粒子缓冲）（§3.6）
- [ ] CCL 连通块着色叠层（读 `plan/06` 连通块/轮廓）（§3.6）

性能 HUD（架构 §17.1）：
- [ ] `PerformanceHudPanel`：每相位耗时（particle/CA A–D/heat/physics/形状重建/render/upload/audio）（§3.7）
- [ ] 活跃 chunk 数 / 活跃 cell 数 / 自由粒子数 / 刚体数 / 常驻 chunk 数 + 估算内存（§3.7、架构 §12.2）
- [ ] 当前 sim 频率（60/30Hz）+ 时间膨胀/§4.3 降级级别显示（§3.7）
- [ ] 用 ImPlot 绘制滚动耗时曲线与相位堆叠条（§3.7）
- [ ] HUD 仅读 `plan/02` 诊断/计时器快照，零写入（§3.7）

材质 + 反应实时编辑器（架构 §17.4，#8）：
- [ ] `MaterialReactionEditorPanel`：ImGui 表格编辑 `MaterialDef` 全字段（§3.8、架构 §7.3）
- [ ] ImGui 表格编辑 `Reaction`（输入/输出/概率/Flags + `[tag]` 展开预览）（§3.8、架构 §7.4）
- [ ] 编辑写回 `content/*.json` 并经 `plan/04` 触发热重载（§3.8）
- [ ] id 稳定：保留既有 Name→id、新增追加、**绝不重排**（#8、§3.8）
- [ ] 删除材质作 tombstone，并把活 cell 重映射到声明 fallback（§3.8）
- [ ] 改纹理/音效仅重载资产、不动 id（§3.8）
- [ ] 输出诊断「重载后用 fallback 替换了 N 个被删材质的活 cell」（§3.8、架构 §17.4）
- [ ] 运行时数值 id 只读展示，禁止手工指定/重排（#8、§3.8）

Inspector / 浏览 / 层级：
- [ ] `ScriptInspectorPanel`：反射展示/编辑脚本组件公开字段（配合 `plan/11` 元数据），支持基础/向量/枚举/材质引用/范围滑条（§3.9）
- [ ] Inspector 触发脚本热重载（经 `plan/11` Roslyn+ALC），重载后保留/重映射字段值（§3.9）
- [ ] `AssetBrowserPanel`：浏览 `content/` 材质纹理(缩略图)/音效(试听)/场景/JSON，筛选搜索，选中联动（§3.10）
- [ ] `SceneHierarchyPanel`：Demo 实体 + 活跃刚体列表，选择联动 Inspector/视口聚焦（§3.11）

sim 控制 / 存读档 / 调参 / 模式：
- [ ] `SimulationControlToolbar`：Play/Pause/单步/60-30Hz 降频，语义严格符合 #6（单步=恰一 tick、不追帧）（§3.12、架构 §4）
- [ ] sim 控制经 Hosting/`plan/02` 时钟设「本帧是否执行 sim step」，Editor 不自驱 step（§3.12）
- [ ] `SaveLoadPanel`：保存/加载/存档点列表（时间戳/种子/版本），经 `plan/07`，帧边界一致快照（§3.13、架构 §11.5）
- [ ] 存读档显示 name↔id 重映射 + 版本迁移 + 缺失材质 fallback 计数（§3.13、#8）
- [ ] `PhysicsTuningPanel`（`plan/06`）：尺度/subStep/workerCount/碎片下限/重建节流/重力 + 统计（§3.14）
- [ ] `ParticleTuningPanel`（`plan/05`）：最大数/重力/max-lifetime/沉积阈值/抛射冲量 + 泄漏统计（§3.14）
- [ ] `LightingTuningPanel`（`plan/08`）：emissive/bloom/fog-of-war/dither/gamma/Radiance Cascades 开关（§3.14、架构 §9.4）
- [ ] `EditorMode` 状态机（Edit/Play）+ 输入仲裁 + 切换经 Hosting 运行状态 API（§3.15）
- [ ] Play 模式可选「当前态运行」或「存档点临时副本运行、退出还原」（经 `plan/07`）（§3.15）

---

## 5. 验收标准

- [ ] ImGui 在 Rendering 的同一 GL context 上、世界渲染之后顶层绘制；渲染后 GL 状态被正确恢复，世界画面无被 UI 污染（§3.3）
- [ ] docking 生效：面板可自由停靠/浮动/拖拽；默认布局可保存并在重启后恢复（§3.3）
- [ ] 中文 UI 文本正常显示无缺字/方块；DPI 缩放下字体清晰（§3.3）
- [ ] 画刷在 Edit 模式可画/挖/擦/加温；写入仅落相位 [1]、标 dirty + KeepAlive；与 CA 多线程相位无竞争、无边界像素消失/复制（#1/#2/#4、§3.4）
- [ ] 世界检视器点选任一 cell 正确显示 material/temperature/Flags 逐位/owned-by-body/坐标/chunk/dirty/sleep（§3.5）
- [ ] 八种调试叠层（dirty rect/chunk+parity/KeepAlive/cell parity/温度热图/owned-by-body/粒子轨迹/CCL）均可独立切换并与世界对齐，能复现并定位边界/parity/刚体往返问题（架构 §17.2、§3.6）
- [ ] 性能 HUD 各相位耗时、计数、内存、sim 频率、降级级别与诊断快照一致；ImPlot 曲线实时更新（架构 §17.1、§3.7）
- [ ] 材质/反应编辑后热重载即时生效；新增材质追加 id、删除材质 id 不被复用、既有 id 不重排（#8、§3.8）
- [ ] 删除被使用中的材质后，live 网格引用 cell 被替换为 fallback，并输出「替换了 N 个活 cell」诊断；不出现 id 错位损坏（#8、架构 §17.4）
- [ ] 改材质纹理/音效仅重载资产，运行时 id 不变（§3.8）
- [ ] 脚本 Inspector 正确反射展示/编辑公开字段；触发热重载后字段值保留/合理重映射（§3.9、配合 `plan/11`）
- [ ] 资源浏览器列出 `content/` 资产并可选中联动；音效可试听；缩略图正确（§3.10）
- [ ] 层级面板列出实体/刚体并可选择联动 Inspector 与视口聚焦（§3.11）
- [ ] sim 控制条：Pause 停止 sim 但持续出帧；单步**恰执行一个 tick** 后回到暂停，绝不追帧/不多步；30Hz 降频时 render 仍每帧出帧（#6、§3.12、架构 §4.2）
- [ ] 存读档：保存→加载后世界逐 cell 等价；改 materials.json 顺序/增删后旧档正确重映射；缺失材质走 fallback 并提示（#8、架构 §11.2、§3.13）
- [ ] 三个调参面板的参数改动实时作用于对应子系统且不破坏其不变式（§3.14）
- [ ] Edit/Play 模式切换正确仲裁输入；「临时副本运行」退出后世界还原；切换不破坏帧节奏（#6、§3.15）
- [ ] Editor 仅消费各子系统公开 read-only/tuning API，无任何对子系统内部类型/字段的直接访问；无反向依赖（§1、§2）
- [ ] 编辑器禁用开关关闭后，主循环无 ImGui/叠层开销（§1）

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

被依赖：`plan/13`（Demo 借编辑器调参/调试，但 Demo 仅依赖引擎公开 API）；`plan/14`（测试可验证编辑器读 API 的正确性）。

并行关系：本文档须在所消费 API 就绪后实施；各面板之间可并行开发。精确顺序见 `plan/17`。

风险/阻塞：若某子系统未暴露所需 read-only/tuning API（如 Rendering 的 debug 着色钩子、Simulation 的 KeepAlive 事件、`plan/11` 的字段反射），按 `AGENTS.md §2` 标 `- [!] 阻塞：原因` 并由对应 plan 补 API，不在 Editor 内访问内部类型绕过。

---

## 7. 提交节点

按 `AGENTS.md §6`，每个节点完成即用中文 git 提交（scope=`editor`）：

- [ ] 节点 1：`feat(editor): 集成 Hexa.NET.ImGui(GL 后端+输入桥+docking+中文字体)`（§3.1–§3.3，实现清单「ImGui 集成与框架」全部）
- [ ] 节点 2：`feat(editor): 世界画刷调色板与世界检视器`（§3.4–§3.5）
- [ ] 节点 3：`feat(editor): 调试可视化叠层(架构 §17.2)`（§3.6）
- [ ] 节点 4：`feat(editor): 性能 HUD(架构 §17.1)`（§3.7）
- [ ] 节点 5：`feat(editor): 材质+反应实时编辑器与 id 稳定热重载(架构 §17.4)`（§3.8）
- [ ] 节点 6：`feat(editor): 脚本 Inspector/资源浏览器/层级面板`（§3.9–§3.11）
- [ ] 节点 7：`feat(editor): sim 控制条/存读档 UI/子系统调参面板`（§3.12–§3.14）
- [ ] 节点 8：`feat(editor): 编辑/运行(Play)模式切换`（§3.15）
- [ ] 节点 9：`docs(plan): 勾选 plan/12 验收并更新 README 进度`（§5 全部通过后）
