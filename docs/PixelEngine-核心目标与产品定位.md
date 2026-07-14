# PixelEngine 核心目标与产品定位

> 本文档描述 PixelEngine 最终应该开发到什么地步，以及后续所有 plan、实现、测试、发行和 Demo 取舍应服务的产品北极星。它不替代 `PixelEngine-架构与需求设计.md` 的技术设计，也不替代 `plan/17-roadmap-execution-order.md` 的执行顺序；它负责回答「我们最终要做成什么」。
>
> 当前定位：**面向 Unity 开发者的现代 2D 像素游戏引擎：Noita-like CPU 权威像素世界 + Unity-like Editor + Web-first 透明 HTML UI Runtime + Showcase Demo Game。**

---

## 1. 一句话目标

PixelEngine 要成为一个让资深 Unity 开发者可以自然上手的现代 2D 像素游戏引擎：开发者用 Unity-like Editor 工作流组织 GameObject、Component、Prefab、Scene、资源、脚本和构建；用 Web-first 透明 HTML UI Runtime 构建游戏 HUD、菜单和设置；用 Noita-like CPU 权威像素模拟提供可破坏地形、材质反应、粒子、刚体切割和高性能大世界；最终通过 Showcase Demo Game 证明整条开发链路成立。

这个目标包含四个同等重要的产品面：

1. **Engine Core**：Noita-like 像素世界技术内核。
2. **Unity-like Editor**：面向开发者的 authoring / 项目 / 资源 / 构建工作流。
3. **Web-first 透明 HTML UI Runtime**：面向玩家的透明 HTML/CSS 游戏 UI。
4. **Showcase Demo Game**：功能完整的小型游戏样本，证明引擎能力与开发体验。

---

## 2. 非目标与边界

PixelEngine 不应退化成以下任何一种东西：

- **不是单纯像素沙盒 Demo**：落沙、液体、爆炸只是引擎能力的一部分，项目最终必须具备可复用引擎、编辑器、UI、打包和 Demo 全链路。
- **不是只服务内部调试的工具集合**：编辑器不能只是 ImGui 调试面板堆叠，必须形成 Unity-like 的产品工作流。
- **不是 Unity 的完整克隆**：目标是 Unity-like 心智模型，不是复制 Unity 的所有系统、资产管线、UGUI 技术栈或运行时行为。
- **不是传统 UGUI 复刻**：游戏 UI 的内容、布局与交互仍以 Web-first 透明 HTML/CSS 为主；但场景中的 Canvas authoring、分辨率适配和 Game View 验证必须保留 Unity 用户熟悉的心智模型，不能因为底层使用 Web 就退化成场景外的全局弹层。
- **不是大型内容型游戏项目**：Demo 可以完整、可玩、有反馈和胜负，但不以堆关卡、剧情、成长、武器数量或 roguelite 内容量为目标。
- **不是 GPU 权威模拟引擎**：CPU simulation authoritative 是根基；GPU 只做渲染、光照、粒子合成和可选非权威 pass。

---

## 3. 目标用户画像

### 3.1 主要用户：资深 Unity 2D / 工具开发者

他们习惯：

- 用 Hierarchy 组织对象；
- 用 Inspector 编辑属性；
- 用 Project Window 管理资源；
- 用 Prefab 复用对象结构；
- 用 Scene View / Game View 区分编辑与运行；
- 用 Project Settings / Player Settings / Build Settings 配置项目；
- 用脚本组件表达玩法逻辑；
- 用一键构建得到玩家包。

PixelEngine 对这类用户的核心承诺是：**不用重新学习一套完全陌生的 authoring 心智模型，就能开发具备像素级破坏世界能力的 2D 游戏。**

### 3.2 次要用户：AI 辅助开发者

AI 时代的 UI 和内容开发应尽量可文本化、可生成、可编辑、可 diff。PixelEngine 应让 AI 更容易参与：

- HTML/CSS UI 文件可由 AI 生成和修改；
- 游戏对象、Prefab、场景、材质、反应、脚本都应有清晰文本资产格式；
- 编辑器工作流应降低人工定位成本，便于 AI 改完后由开发者快速验证；
- Demo 应提供清晰公开 API dogfood 样例，让 AI 后续能照着扩展。

---

## 4. 产品支柱一：Engine Core

Engine Core 是 PixelEngine 的底层卖点，目标不是「能模拟沙子」，而是提供可用来制作真实游戏的 Noita-like 像素世界能力。

### 4.1 必须达到的能力

- **CPU 权威像素世界**：CellGrid 是唯一权威状态；每个 cell 有材质、生命周期、反应、可碰撞语义。
- **高性能 falling-sand CA**：64×64 chunk、hash-map 驻留、dirty rect、sleeping chunk、4-pass checkerboard、多线程、32px move cap、parity 防重复处理。
- **材质与反应系统**：粉末、液体、气体、固体、火、温度、相变、密度位移、燃烧、熔岩遇水、腐蚀等行为由数据驱动表达。
- **可破坏地形**：射击、爆炸、切割、烧蚀、腐蚀等都能修改 cell 世界，并正确唤醒 dirty chunk 与渲染。
- **自由粒子系统**：爆炸碎屑、火花、飞溅、烟尘等能从 cell 抛射为 particle，并在落定后沉积回 cell。
- **刚体切割与双向耦合**：地形被切断后能转为 Box2D 刚体；刚体每帧 erase → step → inverse-sample re-stamp，CA 能把刚体当作地形处理。
- **渲染表现**：材质纹理、温度 glow、粒子、emissive、fog-of-war、bloom、dither、gamma、debug overlay 与 UI 合成顺序稳定。
- **音频反馈**：爆炸、撞击、破碎、火焰、液体、材质环境音等事件化、限频、定位播放。
- **世界流式与存档**：chunk 装卸、border ring、RLE+LZ4、material name remap、版本迁移、世界快照。
- **性能证据链**：BenchmarkDotNet、反汇编、硬件计数器、目标硬件长跑、零分配验证、native leak 审计。

### 4.2 成功标准

Engine Core 达标时，应能支持一个真实窗口中的复杂像素场景：玩家移动、射击、爆炸、材质反应、刚体坍塌、粒子飞溅、光照、音频、UI 同时运行，并且在目标硬件上保持可接受帧率和可解释的降级行为。

---

## 5. 产品支柱二：Unity-like Editor

编辑器目标不是「能调参数」，而是让开发者用接近 Unity 的方式 author 一个 PixelEngine 游戏。

### 5.1 默认工作台

打开编辑器后，用户应看到一个接近 Unity 的默认布局：

- **Hierarchy**：场景 GameObject 树。
- **Inspector**：当前选中 GameObject / Component / Asset 的属性编辑入口。
- **Project Window**：工程资源目录与资产入口。
- **Scene View**：编辑态视图，支持相机、点选、gizmo、画刷、调试叠层。
- **Game View**：运行态游戏视图，显示最终玩家看到的世界与透明 HTML UI；顶部提供 Free Aspect、常用宽高比、固定分辨率、自定义分辨率、显示缩放、最大化/还原与 Maximize On Play。
- **Console**：脚本错误、构建日志、资源导入、诊断输出。
- **Toolbar**：Scene 操作上下文、严格居中的 Play / Pause / Step、真实活动/禁用反馈与布局入口；保存和构建保留在明确菜单/快捷键中，不以宽按钮挤占全局工具栏。
- **Status Bar**：工程、场景 dirty、Edit/Play/Pause、对象数、任务与错误状态的低干扰持续反馈。

对 PixelEngine 已支持的编辑器表面，视觉层级、控件密度、停靠方式、hover/active/disabled/selection 状态、键盘鼠标焦点、Play 模式切换、DPI/resize 与系统录屏可观察性均以 Windows Unity 6.5 默认工作台为参照。这里的“不做完整 Unity 克隆”只排除 PixelEngine 产品范围外的子系统，不降低已公开工作流的交互和显示质量；不得通过未接线菜单、占位按钮或静态假面板制造相似外观。

### 5.2 GameObject / Component 工作流

GameObject 是编辑器 authoring 的中心单元。最终体验应包括：

- 创建、删除、重命名、复制、启用/禁用 GameObject；
- parent/child 层级组织；
- Transform 始终存在，支持位置、旋转、缩放；
- Component 列表可 Add / Remove / Reorder；
- 脚本 Behaviour 以组件形式挂载；
- 场景可挂载一个或多个 `Canvas (Web)` 与 `Canvas Scaler` 内建组件；Canvas 在 Scene View 中可见、可选择、可预览真实 HTML/CSS 内容，DOM 层级仍由 Web 文档管理而不伪装成 RectTransform 子树；
- 组件字段在 Inspector 中可编辑；
- 改动可 Undo / Redo；
- Scene View 中点选对象并用 gizmo 操作 Transform；
- Play 模式进入时从 authoring 模型物化到运行时，退出 Play 回滚到编辑态。

### 5.3 Prefab 工作流

Prefab 是长期必须保留的核心 authoring 能力，不是可选小功能。最终应支持：

- 从 GameObject 子树创建 prefab 资产；
- 从 Project Window 拖入 Hierarchy / Scene View 实例化；
- prefab instance 记录 override；
- Inspector 中清晰显示 override 状态，并支持 Revert；
- prefab 资产修改能传播到未 override 的实例；
- 支持嵌套 prefab；
- 运行时只看到展开后的扁平实体，不把 prefab 概念污染脚本热路径。

### 5.4 Project Window 与资源目录

Project Window 应接近 Unity 的项目资源入口，而不是普通文件浏览器。

最终应支持：

- 浏览工程根与 `content/`；
- 识别 scenes、prefabs、scripts、materials、textures、audio、ui、fonts、save、build settings 等资产类型；
- 显示图标、类型、路径、基础元数据；
- 支持搜索、过滤、排序、刷新；
- 支持资产预览：纹理缩略图、音频试听、材质摘要、场景摘要、Prefab 摘要；
- 支持拖拽：资源拖到 Hierarchy、Scene View、Inspector 字段、UI manifest 或设置面板；
- 支持资源引用：stable asset id / manifest + logical path 双轨，重命名或移动后引用不应静默损坏；
- 支持脚本双击打开外部编辑器；
- 支持创建常见资产：Scene、Prefab、Script、Material、UI Screen、Settings 等。

### 5.5 Settings 工作流

Settings 是正式产品面，不是后期补丁。至少应有：

- **Project Settings**：工程名、content root、script source dir、默认 scene、资源规则、编辑器偏好、UI backend 默认值。
- **Player Settings**：窗口标题、玩家窗口/呈现分辨率、`Windowed` / `Maximized Window` / `Borderless Fullscreen` 窗口模式、VSync、图标、版本号、启动场景、输入默认、运行时 UI 配置、发行通道；这些设置不得反向改变像素世界的内部渲染分辨率。Windowed 的 Width/Height 也是普通窗口初始客户区，后两种模式只改变 OS framebuffer，不得伪装尚未实现的 Exclusive Fullscreen。
- **Build Settings**：目标 RID、R2R / AOT、Debug / Release、入包场景、启动场景、输出目录、Build、Build And Run、日志、取消、失败诊断、产物校验。

### 5.6 编辑器达标定义

编辑器达标不是「面板都能打开」，而是开发者能完成一条真实链路：

1. 新建或打开项目；
2. 在 Project Window 创建 / 导入资源；
3. 在 Hierarchy 创建 GameObject；
4. 在 Inspector 添加脚本组件并编辑字段；
5. 在 Scene View 摆放 / 编辑对象和像素世界；
6. 创建 Prefab 并实例化；
7. 配置 Project / Player / Build Settings；
8. 点击 Play 验证；
9. 点击 Build 得到不含编辑器的玩家包；
10. 独立运行玩家包并进入同一游戏内容。

### 5.7 外部编辑器自动化公共 API

Editor 的全部语义数据和全部人工可达操作必须同时通过一套版本化、本地优先的公共自动化 API 暴露，使 Codex、Claude Code、普通脚本和 CI 在不依赖 MCP、屏幕坐标、OCR 或 Computer Use 的条件下完成与开发者相同的 authoring、运行、调试和构建工作流。这里的“全部”以机器可读能力矩阵为闭包：每个菜单项、快捷键、面板、工具栏动作和上下文操作都必须映射到稳定的 capability / command id、请求与响应 schema、权限、线程或引擎阶段、revision 与事务语义；新增可见操作若没有真实语义 API，验证必须失败。

自动化不是第二套编辑器状态，也不是测试专用后门。UI 与外部调用必须复用同一命令、校验、dirty guard、事务和 Undo/Redo 路径；读取必须来自安全点捕获的权威快照，写入必须调度到 Editor 主线程或明确的 Engine phase。公共交付面包括协议、Server、.NET Client、CLI、JSON Schema、能力矩阵、文档、测试、clean final-output 和 `$CODEX_HOME/skills/pixelengine-editor` Skill。最终验收必须由全新外部进程仅通过 CLI 完成“编辑场景→运行→调试→停止→再次运行→修改→构建→启动产物”，并证明权限、取消、超时、revision 冲突、事务回滚、Undo/Redo、事件断线续订、性能和制品完整性均成立。

---

## 6. 产品支柱三：Web-first 透明 HTML UI Runtime

UI 方向应服务两个目标：玩家体验现代化，以及 AI 辅助开发友好。

### 6.1 为什么 Web-first

传统即时模式 UI 或 UGUI 式硬编码控件不适合 AI 时代的大量快速迭代。HTML/CSS 的优势是：

- 文本格式天然适合生成、diff、review 和重构；
- 设计表达能力比硬编码控件更高；
- 菜单、HUD、设置、对话、背包、结算等游戏 UI 更容易模块化；
- 后续可让 AI 直接生成 UI screen、样式和数据绑定；
- 与现代前端心智模型兼容。

### 6.2 必须坚持的 UI 合成模型

游戏 UI 必须是同一窗口、同一渲染上下文内的透明叠加层：

- 世界先渲染；
- 游戏 HTML UI 以 alpha 合成在世界之上；
- 编辑器 overlay 在开发态盖在游戏 UI 之上；
- 透明区域透出世界；
- 透明或非交互区域 pass-through 给游戏；
- 按钮、输入框、滑条等交互区域 capture 输入；
- 不允许用外部浏览器窗口、第二进程、第二 GL context 冒充游戏内 UI。

### 6.3 Unity-like Web Canvas 与分辨率适配

Web-first 不等于“脱离场景的全局覆盖层”。游戏 UI 必须以场景内建组件参与 authoring，同时继续用 HTML/CSS 作为内容真相源：

- `Canvas (Web)` 挂在 GameObject 上，引用 UI manifest / screen 入口、启用状态、primary 与排序顺序；effective Canvas id 由 owning GameObject stable id 派生。Duplicate/paste/prefab instantiate 必须 remap 实例身份，Prefab asset 本体不得持久化 scene primary；同一场景允许多个 Canvas，运行时按稳定顺序合成和仲裁输入。
- `Canvas Scaler` 是 Canvas 的配套内建组件，完整支持 Unity CanvasScaler 的三种 UI Scale Mode：Constant Pixel Size、Scale With Screen Size、Constant Physical Size；Scale With Screen Size 同时支持 Match Width Or Height、Expand、Shrink，物理模式使用真实显示 DPI，取不到时才使用明确的 fallback DPI。
- Canvas 未挂 Scaler 时使用可见且可添加组件覆盖的 Unity-compatible 默认设置；孤立 Scaler 不物化 runtime Canvas。disabled primary 被跳过并诊断；存在显式 Canvas 但全部 disabled 时 primary 为空，旧无 Canvas 参数调用安全 no-op，不能暗中复活旧 implicit Canvas。
- Canvas 的 authoring 只管理屏幕空间、分辨率适配和 Web 文档入口。DOM 元素、CSS 布局和数据绑定仍在 Web 资产中维护，不把它们伪造成 RectTransform GameObject，也不暴露尚未实现的 World Space / Screen Space Camera 占位选项。
- Scene View 必须显示所选 Canvas 的参考分辨率边框，并用同一 UI 后端、同一 HTML/CSS/字体/图片资产生成真实 authoring preview；Edit 模式预览不得执行玩法 action 或写入运行时世界。
- Play 与 Paused 模式才把运行时 UI 合成到最终游戏画面；Edit 模式不得因后端仍已初始化而把 runtime UI 误叠到 Game View。Editor chrome 永远位于游戏 UI 之上。

分辨率必须拆成三层独立概念，任何设置不得再把它们混为一个 viewport：

1. **Internal World Resolution**：像素世界、相机与低分辨率世界渲染的固定内部尺寸，例如 640×360；它服务性能和像素语义，Game View preset 不改变 camera aspect、可见世界范围或内部像素数。
2. **Presentation / Game Screen Resolution**：玩家窗口或 Game View 预设对应的最终呈现表面；世界按保持宽高比的规则居中放入该表面，4:3/portrait 等非内部宽高比产生明确 letterbox，Web Canvas 在完整呈现表面上布局与合成。
3. **Editor Game View Display Rect**：呈现纹理在编辑器面板中的 Fit / 百分比缩放 / 裁剪 / 平移结果；它只影响编辑器观察与输入映射，不改变玩家构建、Canvas 逻辑尺寸或内部世界分辨率。

输入必须分两段解析：presentation/UI 坐标覆盖完整游戏画布并先参加 Web UI hit-test；只有未被 UI capture 且位于 world content rect 的输入才映射成 world/gameplay 坐标。这样 letterbox 上的 UI 可以交互，透明 Canvas 边缘却不会把点击漏给玩法。显示尺寸、DPI 或跨屏变化允许在下一帧边界生效，但纹理、输入与 IME 必须按同一 revision 切换，不能混用新旧几何。

没有显式 Canvas 的旧场景必须继续运行：加载器为旧的全局 `GameUiHost` 语义建立一个不落盘的 implicit primary Canvas；Editor 仍以只读兼容预览让它在 Scene View 可见，并提供显式 Convert To Web Canvas，不因打开/保存场景静默迁移。现有不带 Canvas 参数的 `IGameUiService` 调用继续路由到 primary Canvas；兼容路径不得复制文档、重复合成或改变旧存档/场景行为。

### 6.4 后端分层

最终 UI runtime 应保持三层后端策略：

- **ManagedFallbackBackend**：纯托管、永远可用、CI/headless/unsupported RID 安全回退。
- **RmlUiBackend**：默认产品路径，HTML/CSS 子集，适合 HUD、菜单、设置等可控 UI。
- **UltralightBackend**：可选高保真路径，服务标准 HTML5/CSS3/JS 与更强 AI 生成页面诉求。

RmlUi 不能被宣传成完整浏览器；标准 HTML5/JS 高保真能力应明确归 Ultralight profile。

### 6.5 UI 内容范围

Demo 和引擎示例至少应证明：

- 主菜单；
- HUD；
- 暂停菜单；
- 设置面板；
- 背包 / 对话 / 任务 / 结算中的至少若干代表屏；
- 中文字体无缺字；
- C# ↔ UI 数据绑定；
- UI 事件安全进入脚本相位；
- 输入三级仲裁：Editor > Game UI > Gameplay；
- UI 性能计时与降级可见。

---

## 7. 产品支柱四：Showcase Demo Game

Demo 不是「越简单越好」，而是**完整但聚焦**。它应该是一个功能完整的小型游戏样本，用有限内容证明引擎能力、编辑器工作流、UI runtime、打包发行和公开 API dogfood。

### 7.1 Demo 的定位

Demo 应承担四个职责：

1. **玩家视角**：它应该真的能玩，有开始、目标、反馈、失败/胜利、暂停、设置、重开、退出。
2. **引擎展示**：它必须集中展示 PixelEngine 区别于普通 2D 引擎的能力。
3. **开发者样例**：它是后续开发者学习公开 API 的主要样本。
4. **验收证据**：它是测试真实窗口、性能、UI、音频、发行包的主要载体。

### 7.2 Demo 必须具备的完整功能

一个达标 Demo 至少应有：

- 主菜单；
- 设置；
- 开始游戏；
- 可操作角色；
- 相机跟随与视野控制；
- 武器或工具系统；
- 射击与爆炸；
- 地形破坏；
- 地形切割后的刚体掉落、旋转、再破坏；
- 粒子与光照反馈；
- 材质反应展示；
- 透明 HTML HUD；
- 暂停菜单；
- 任务 / 目标 / 进度反馈；
- 失败与胜利条件；
- 重新开始；
- 基础音效与音乐 / ambient；
- 性能与手感诊断入口；
- 可从编辑器 Play，也可从玩家包独立运行。

### 7.3 Demo 内容量边界

Demo 可以有完整 loop，但不应被内容量绑架。

可以做：

- 一个高质量小场景或少量场景；
- 几种足以覆盖引擎能力的武器 / 工具；
- 一条明确任务线；
- 少量敌人 / 危险 / 目标物；
- 一套完整 UI；
- 充分的反馈与打磨。

不优先做：

- 大量关卡；
- 大型剧情；
- roguelite 构筑；
- 复杂经济 / 成长系统；
- 大规模敌人生态；
- 追求内容数量而牺牲引擎能力展示。

### 7.4 Demo 验收标准

Demo 达标时，应能回答：

- 这个游戏能不能完整开始、游玩、完成或失败？
- 玩家是否能直观看到地形被射击、爆炸、切割和物理坍塌？
- UI 是否真的用透明 HTML 叠加在世界上？
- Demo 是否只通过公开 API / 脚本 / 内容资产实现？
- 是否能从 Editor Play 进入，也能 Build 后独立运行？
- 性能、手感、音效、反馈是否达到可展示水平？
- 这个 Demo 是否能作为后续开发者学习 PixelEngine 的样板？

---

## 8. 公开 API 与 dogfood 原则

PixelEngine 的 Demo 与 EditorShell 都必须反向检验引擎 API 设计。

### 8.1 公开 API 优先

如果 Demo、编辑器或 UI 需要某个能力，却只能通过内部类型、反射、friend assembly 或特殊后门完成，应优先认为是引擎公开 API 缺口，而不是在上层绕过。

应补齐的公开面包括：

- 世界查询与修改；
- 材质与反应访问；
- 地形破坏请求；
- 爆炸 / 粒子 / 音频事件；
- GameObject / Component authoring DTO；
- Scene / Prefab 读写；
- Web Canvas / CanvasScaler descriptor、presentation/Canvas metrics 与多 Canvas UI service；
- Build / Project / Player settings DTO；
- 诊断计数器；
- 存档 / 读取；
- 输入与相机控制。

### 8.2 API 达标标准

公开 API 应满足：

- 脚本可用；
- Editor 可用；
- Demo 可用；
- XML 文档清晰；
- 不泄漏 sim 内部热路径结构；
- 不引入反向依赖；
- 能被测试锁定；
- 能支撑 AI 后续生成脚本和 UI。

---

## 9. 最终产品成熟度分级

### 9.1 Alpha：能力闭合

Alpha 阶段目标：所有核心系统有真实实现，不再依赖 stub / mock / TODO-later。

必须满足：

- 引擎核心模拟、渲染、物理、脚本、UI、编辑器、Demo 均能端到端运行；
- Demo 可玩但可允许体验粗糙；
- Editor 能打开项目、编辑场景、Play、Build；
- UI 能透明合成并交互；
- 基础测试通过；
- 明确列出所有真实平台 / 证据阻塞。

### 9.2 Beta：产品可用

Beta 阶段目标：开发者能用它认真制作 Demo 级游戏。

必须满足：

- Unity-like 编辑器主工作流顺畅；
- Project Window / Inspector / Prefab / Settings / Build 均可用；
- Demo 功能完整，反馈清晰；
- Web-first UI 在真实窗口中稳定；
- 发行玩家包可独立运行；
- 性能与内存有目标硬件证据；
- 常见失败有清楚诊断；
- 文档能指导新开发者上手。

### 9.3 Release Candidate：证据闭合

RC 阶段目标：不靠口头判断，所有关键结论都有证据。

必须满足：

- 全量 build / test / benchmark / smoke 有记录；
- 目标硬件长跑通过；
- native leak 审计通过；
- 发行包 audit 通过；
- UI 真实窗口视频 / 截图 / probe 证据齐全；
- IME、RmlUi ANGLE/GLES、Ultralight gate 等明确完成或明确降级；
- `pending_review`、`local_probe_only`、`scripted_probe_only`、`process_smoke_only` 等不再被误当作完成。

### 9.4 1.0：产品交付

1.0 阶段目标：PixelEngine 可以被作为一个完整产品展示和试用。

必须满足：

- 引擎能力、编辑器体验、UI runtime、Demo、发行包全部可演示；
- Demo 是功能完整的小型游戏样本；
- 编辑器可完成从项目创建到构建运行的闭环；
- 文档、教程、样例、API 注释足够支撑外部开发者理解；
- 关键性能目标有可复现报告；
- 所有架构不变式仍成立；
- 不存在必须靠内部后门完成的 Demo 功能。

---

## 10. 优先级判断规则

后续遇到功能取舍时，按以下顺序判断价值：

1. **是否保护或增强 Noita-like 核心能力？**
2. **是否让 Unity-like authoring 工作流更完整？**
3. **是否增强 Web-first 透明 UI 的真实产品价值？**
4. **是否让 Demo 更能证明完整开发链路？**
5. **是否减少公开 API 缺口和上层绕路？**
6. **是否增加可验证证据而非只增加文档声明？**
7. **是否降低未来 AI 辅助开发的复杂度？**

如果一个改动只增加内容量，但不增强以上任一目标，应谨慎排后。若一个改动能让完整链路更清晰，即使短期不是最酷的功能，也应优先。

---

## 11. 文档与计划同步要求

本文档是产品目标层。同步关系如下：

- 技术架构细节写入 `docs/PixelEngine-架构与需求设计.md`。
- 执行顺序和里程碑写入 `plan/17-roadmap-execution-order.md`。
- Editor 细节写入 `plan/19-standalone-editor-app.md`。
- Runtime HTML UI 细节写入 `plan/20-interactive-html-ui.md`。
- Showcase Demo Game 设计与验收写入 `plan/13-demo-game.md`。
- 测试、benchmark、证据门禁写入 `plan/14` / `plan/16`。
- 发行、打包、玩家包审计写入 `plan/15`。

当本文档的产品目标发生变化，应同步检查上述文档，避免出现「产品北极星更新了，但执行 plan 仍在追旧目标」的问题。

---

## 12. 当前核心目标版本

当前版本定义为：

**PixelEngine 是一个面向 Unity 开发者的现代 2D 像素游戏引擎。它用 Noita-like CPU 权威像素模拟作为底层差异化能力，用 Unity-like Editor 承载 GameObject / Component / Prefab / Project / Settings / Build 工作流，用 Web-first 透明 HTML UI Runtime 承载玩家 UI，用 Showcase Demo Game 证明从 authoring 到 build 到 runtime 的完整产品链路。**

只要后续开发仍服务这句话，项目方向就是收敛的；如果某项工作偏离这句话，应先重新讨论目标，再继续实现。
