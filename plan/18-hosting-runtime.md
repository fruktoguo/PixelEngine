# Plan 18 — 引擎宿主与运行时(PixelEngine.Hosting)

> 本文件是 plan/13 暴露的缺口补档:`PixelEngine.Hosting` 是引擎的「指挥者」——它拥有 12 相位帧循环的编排、子系统的装配与生命周期、`Engine`/`EngineBuilder`/`EngineContext` 公开门面、场景/项目模型与加载、Play/Editor 模式协调、以及脚本所消费的服务后端。各子系统文档只描述自己「在哪个相位、读写什么」;**真正按顺序调用它们的主循环在这里**。
> 逻辑位置:在 plan/11(脚本)、plan/12(编辑器)、plan/13(Demo)之前就位(对应里程碑 M10 的 Hosting 部分,见 plan/17)。依据:架构 §3.1/§3.3/§4/§4.3。
> 状态:`- [ ]` 未开始 / `- [x]` 完成 / `- [~]` 进行中 / `- [!]` 阻塞。

---

## 1. 目标与范围

提供引擎的运行时骨架与公开入口,使「一个游戏 = 引用引擎 + 配置子系统 + 提供场景与脚本 + 跑主循环」成立(等同 Unity 游戏之于 Unity 引擎)。范围:

- 子系统装配(assembly)与生命周期(init/shutdown 顺序)。
- **12 相位帧循环编排**(架构 §3.3),把 Core 时间(plan/02)、Simulation(plan/03)、Particles(plan/05)、Physics(plan/06)、World/Serialization(plan/07)、Rendering(plan/08)、GPU(plan/09)、Audio(plan/10)、Scripting(plan/11)、Editor(plan/12)按相位顺序与 barrier 串起来。
- 公开门面:`Engine` / `EngineBuilder` / `EngineContext` / 服务定位。
- 场景/项目模型与加载、Play/Edit 模式切换、headless 模式(测试/基准)。
- **过载降级编排**(架构 §4.3 的决策者:谁决定降热场/降光照/降 sim 到 30Hz)。
- 脚本世界接口(plan/11)的服务后端聚合点。

不在范围:各子系统内部逻辑(在各自 plan);ImGui 面板(plan/12);具体游戏内容(plan/13)。

## 2. 技术栈与依赖

- 纯 C#/.NET 10,无新增第三方依赖(沿用 plan/00 §4)。
- 依赖方向(plan/00 §5,**定稿**——修订早期把 `Editor → Hosting` 写反、且与代码 `Hosting → Editor` 冲突的文档 bug):顶层 app 为 `demo/PixelEngine.Demo`(玩家运行时)与 `apps/PixelEngine.Editor.Shell`(编辑器壳),二者同层:`{Demo, apps/EditorShell} → Hosting → {Scripting, Rendering, Audio, Physics, World, Serialization, Content, Simulation, UI, Gui} → Interop → Core`。补边:`Editor → {Gui, 子系统}`;`EditorShell → {Hosting, Editor, Gui}`;`UI → {Gui, Rendering, Core}`;`Hosting → Gui`(经 §3.7 GUI 宿主中性化重构后**不再** → Editor);`Demo → Hosting`(+可选 `UI`,**不含** Editor)。Hosting 引用全部子系统以装配它们;子系统与 Editor 不反向依赖 Hosting。`PixelEngine.Editor` 面板在装配层级上仍位于 Hosting 之下,但**由编辑器壳在开发构建装配期注入**(见 §3.7),Hosting 不静态引用它。

## 3. 详细设计

### 3.1 装配与生命周期

`EngineBuilder` 用 fluent 配置:窗口/分辨率、内部 sim 分辨率(架构 §19.2 待决项,做成配置)、worker 线程数、GC 模式、是否启用 Editor、是否 headless、确定性模式开关(架构 §6.2)、GPU 后端门控(plan/09)、内容根目录、起始场景。`Build()` 产出 `Engine`。

`Engine` 持有所有子系统实例与一个 `EngineContext`(服务定位 + 诊断 + 事件总线 + 时间)。初始化顺序:Core(JobSystem/时钟/事件总线/诊断)→ Content(加载 materials/reactions 填 Simulation 表)→ Simulation(CellGrid/chunk)→ World(驻留/相机)→ Physics(Box2D world + task 桥绑到 JobSystem)→ Audio(OpenAL 设备)→ Rendering(GL 上下文/纹理/管线)→ GPU(能力门控)→ Scripting(ALC/Behaviour 宿主)→ Gui(中性 ImGui host/字体栈,§3.7)→ Editor 叠层(**可选,仅开发构建**,经注入式 GUI 钩子挂入,Hosting 不静态引用 Editor,§3.7)。关闭顺序逆序,确保 native(Box2D/OpenAL/GL)正确释放、ALC 卸载。

### 3.2 主循环:12 相位编排(核心)

`GameLoop.Tick()` 严格按架构 §3.3 顺序调用各子系统的相位入口,相位间 barrier(用 plan/02 JobSystem)。**每帧至多一次 sim/physics step,固定 dt,绝不追帧**(不变式 #6;时钟来自 plan/02 `FrameClock`)。相位映射:

```
[0]  Input & Time        FrameClock.BeginFrame → 决定本帧是否执行 sim tick(可 30Hz 降频)
[1]  Game Logic/Scripts   Scripting.RunUpdate(dt)(相位1,脚本 OnUpdate/OnFixedSimTick)+ Demo 逻辑
[2]  Residency apply       World.ApplyResidency()(单线程,结构性增删,plan/07)
[3]  Particle→Cell         Particles.Deposit()(并行,plan/05)
[4]  CA Simulation         Simulation.StepCheckerboard()(4-pass,plan/03)
[5]  Temperature           Simulation.StepTemperature()(可选/降频,plan/04)
[6]  Dirty-rect swap       Simulation.SwapDirtyRects()
[7]  Cell→Particle         Particles.Emit()(plan/05)
[8]  Physics sync          Physics.Step()(erase→b2Step(task桥)→read→inverse-stamp,plan/06)
[9]  Build Render Buffer    Rendering.BuildFrame()(并行,material→BGRA + 粒子 stamp,plan/08)
[10] GPU Upload & Render    Rendering 世界渲染/GPU(plan/08/09) → UiCompositor 合成(游戏 HTML UI,plan/20,若启用) → 注入式 GUI 钩子 Render(Editor 叠层,plan/12/19,仅开发构建) → SwapBuffers
[11] World streaming        World.KickStreaming()(后台 I/O,只备字节,plan/07)
```

Hosting 不实现相位内逻辑,只保证**顺序、barrier、dt 一致、sim 降频时 render 仍出帧**(复用上帧世界纹理,架构 §4.2)。

**相位 [10] 子序(定稿)**:世界渲染/present(plan/08) → `UiCompositor.Composite`(游戏 HTML UI,plan/20,经 plan/08 显式 UI 层挂点 `RenderPipeline.BeforePresentUi`,仅启用 HTML UI 且脏时光栅化) → **注入式 GUI 相位[10]钩子**(编辑器 ImGui 叠层,由编辑器壳在开发构建注入,plan/12/19) → `SwapBuffers`。叠放次序固定为 世界 → 游戏 UI → 编辑器叠层(编辑器为开发者工具,盖住游戏 UI);发行玩家包禁用编辑器后,游戏 HTML UI 即最顶层 UI,玩家 HUD 走 `PixelEngine.Gui` 中性 ImGui host。**Hosting 不静态引用 `PixelEngine.Editor`**:相位[10] 的 Editor.Render 经抽象 GUI 钩子接口驱动,Editor 具体实现由壳注入(§3.7);玩家运行时该钩子为空,只保留游戏 UI 合成。

### 3.3 过载降级编排(架构 §4.3 决策者)

Hosting 读 plan/02 诊断计时器,按架构 §4.3 五级顺序决策降级:①降/关全分辨率热场 → ②降光照质量(关 RC/bloom) → ③远 chunk 降频 → ④sim 降到 30Hz → ⑤接受 <60fps(因不追帧,最坏只是低帧率不 death spiral)。决策结果通过 `EngineContext` 的质量档位下发给各子系统;编辑器(plan/12)可显示/覆盖当前档位。

### 3.4 公开门面与脚本服务后端

`EngineContext` 暴露脚本(plan/11)与 Demo(plan/13)所需的服务:`IWorldAccess`(读写 cell/材质/采样固体像素/raycast)、`IParticleService`(plan/05)、`IPhysicsService`(建/查/控刚体 + 角色控制器,plan/06)、`IMaterialRegistry`(plan/04)、`ICamera`(plan/08/07)、`IInput`、`IEventBus`(plan/02)、`IAudioService`(plan/10)、`ISceneService`、`IDiagnostics`。这些是 plan/11 世界脚本接口的**实现后端**:plan/11 定义脚本可见契约,Hosting 在此聚合各子系统实现并注入。写操作经延迟命令队列落到正确相位(plan/11 §相位安全模型)。启用游戏内 HTML UI 时(§3.9),`EngineContext` 并聚合 `IGameUiService`(供脚本/Demo 驱动大 UI),其 UI→游戏世界写入复用同一延迟命令队列。

### 3.5 场景/项目模型与加载

`Scene` = 起始世界(地形/材质布局,可来自存档 plan/07 或程序化生成)+ 实体与脚本组件(plan/11 轻量组件数组)+ 相机/光照配置。`ISceneService` 负责加载/卸载/切换场景。项目模型:内容根(content/)、materials/reactions、资产、起始场景引用。Demo 的 `Program.cs` 经 `EngineBuilder` 指定项目与起始场景。

**player 启动分派**:玩家运行时读 `content/startup.json`,按 `SceneSourceKind` 分派——`FromSave` 从 `SaveDirectory`(或 `.scene InitialSaveDirectory`)装配 live World/Simulation/粒子/Physics 后端并恢复 world seed/game time/刚体快照;`Procedural` **记录生成器键**,经注册式 `IProceduralWorldGenerator` 构建 resident world 并填充初始内容。默认启动场景是 `playable-world.scene`(生成器键 `PlayableWorldDirector`),**不是**熔岩矿洞逃生(后者是 plan/13/plan/20 M14 新增内容)。`EngineProject.Scenes` 现仅含单场景;编辑器内打包(plan/19 §5)的场景清单数据源 = 扫描 `content/scenes/`,而非 `EngineProject.Scenes`。

**`.scene` schema v2 与保存往返 API(供 plan/19 编辑器壳)**:`EngineSceneDocument`/`EngineSceneDocumentLoader` 现**仅有读**(`Load`/`Build`)。补齐往返:
- schema v2:`EngineSceneEntityDocument` 增 `ParentId` 与 `Transform`(TRS)块,新增 `Vector2` 字段类型支持;`FormatVersion` 1→2 且**保 v1 兼容**(v1 档无 ParentId/Transform 时按默认根实体 + 单位 Transform 物化)。
- 新增 Hosting 公开 writer **`SaveSceneDocument(EngineSceneDocument, path)`**(源生成上下文 `EngineSceneJsonContext` 扩展),稳定排序(按 StableId 升序)、往返等价(读→写→读逐字段一致,供 plan/14 性质测试)。编辑器壳只喂 `EngineSceneDocument`,不访问 Hosting 内部装配。
- **authoring↔运行时物化公开 API**:由 Hosting 公开 authoring 场景模型 ⇄ `EngineSceneDocument` 双向映射,以及 authoring→运行时物化(遍历实体,在 `Scripting.Scene` `CreateEntity` + `AddComponent(Transform)` 写 TRS + 逐组件 `AddComponent` 绑定字段,复用/扩展 `EngineSceneDocumentLoader` 字段绑定支持 Transform TRS 与 Vector2)。父子层级在物化时按父链复合**烘焙为世界 TRS**;**运行时 `Scripting.Scene` 保持扁平 DOD、不保留 parent 指针污染热路径**,编辑器内则保留 parent 供层级/gizmo。material 引用入盘走稳定 Name(守 #8),场景文档只存字段字符串值,不引入数值 id 依赖。
- authoring 层级模型 ⇄ `EngineSceneDocument` 的映射归**编辑器壳**(plan/19,Hosting 不认识壳侧 authoring 类型);Hosting 侧只提供**中性**的 `SaveSceneDocument` writer 与「从 `EngineSceneDocument` 物化到运行时 `Scripting.Scene`」公开 API。这些 API **不依赖 `PixelEngine.Editor`**,且玩家运行时同样复用它们加载 `.scene`,因此经 §3.7 GUI 宿主中性化(Hosting 不再引用 Editor)后玩家包自然不含编辑器 authoring 层,**无需条件编译剥离**。

### 3.6 Play / Edit 模式与 headless

与编辑器(plan/12)协调三态:Edit(暂停 sim、可编辑世界/材质/实体)、Play(运行游戏与脚本)、Step(单步恰一 tick)。模式切换时脚本生命周期(OnStart/OnDestroy)与世界快照(进入 Play 前快照、退出 Play 回滚,类 Unity)由 Hosting 协调(用 plan/07 快照)。**headless 模式**:无窗口/无渲染/无音频,仅跑 Core+Sim+Physics+World,供 plan/14 测试与 BenchmarkDotNet 基准驱动确定步数。

### 3.7 GUI 宿主中性化重构(M13 入口门,plan/19/20 强前置)

**耦合真相(纠正)**:Demo **并无**对 `PixelEngine.Editor` 的直接 `ProjectReference`。当前耦合是**传递闭包**——`Hosting.csproj` 硬引用 `PixelEngine.Editor`,Hosting 内约 8 个源文件直接使用 Editor 类型,玩家 HUD 走 `IGuiContext → ScriptGuiContext`(位于 Editor)`→ HexaImGuiBackend`(位于 Editor)。因此发行玩家包无法剥离 Editor,plan/15 的 player-only 审计在此前不可满足。真正的解耦动作 = GUI 宿主中性化 + Hosting 删 Editor 引用 + `DemoProgram.cs` 改用中性 host,**不是**「移除 Demo 对 Editor 的项目引用」(该引用本就不存在)。

**重构(此为 M13「编辑器独立化与发行解耦」入口门,阻塞 plan/19 壳注入、plan/15 player-only 审计、plan/20 UI 字体/回退复用三者)**:
- 新增中性程序集 `PixelEngine.Gui`(层级位于 Rendering 之上、Editor 之下),下沉玩家 HUD 所需的 ImGui host:`HexaImGuiBackend`、`IGuiContext` 运行时适配(即现 `ScriptGuiContext`)、`EditorRenderBridge` 的中性部分、字体栈(`EditorFontManager`→`GuiFontManager`,含 CJK)。
- Hosting **删除**对 `PixelEngine.Editor` 的硬 `ProjectReference`,改为暴露**抽象 GUI/相位[10] 钩子接口**(相位[10] 叠层 Render 与 Play/Edit/相位钩子);Editor 具体实现由**编辑器壳(开发构建)在装配期注入**,Hosting 不静态引用 Editor。玩家运行时该钩子为空实现。
- Hosting 内引用 Editor 类型的源文件改指 `PixelEngine.Gui` 中性类型;`DemoProgram.cs` 玩家 HUD(`DemoHud`/`PauseMenu`/`WeaponHud`/`MaterialLegendHud`,plan/13)改用 `PixelEngine.Gui` 中性 `IGuiContext` host,与武器/材质 HUD 同一路径。
- player-only 断言(plan/15 audit)以本重构落地为**前置**:audit **拒绝** `PixelEngine.Editor.dll` 与编辑器专属面板闭包,但**允许**玩家 HUD 所需的 `Hexa.NET.ImGui`(经 `PixelEngine.Gui`,撤销早期「拒绝 ImGui」的不可满足表述)。

### 3.8 窗口/GL 上下文所有权解耦与编辑态宿主 bootstrap(供 apps/PixelEngine.Editor.Shell)

**现状阻塞**:`RenderWindow` + `RenderPipeline` 目前登记在 `Engine._ownedRuntimeResources`,`Engine.Dispose()` 会销毁窗口。编辑器壳(plan/19 §4.1/§4.4)需在**关闭/切换工程时逆序释放 `Engine` 但保留窗口、重建 session**,故必须解耦窗口所有权,否则壳无法 attach 已有窗口并管理 Engine 生命周期(矛盾不可满足)。

新增公开 API(均为**中性 Hosting API**,不依赖 `PixelEngine.Editor`;经 §3.7 GUI 宿主中性化后 Hosting 编译期不再出现 Editor 类型,编辑器面板注册经抽象 GUI 钩子由壳注入,故玩家包不含编辑器闭包靠的是「Hosting 不引用 Editor」,**不使用条件编译剥离**):
- **窗口/GL 上下文所有权解耦**:把 `RenderWindow` + `RenderPipeline` 从 `Engine._ownedRuntimeResources` 解耦;新增「attach rendering 到壳**已拥有**的外部窗口、`Engine` **不 own** 该窗口、`Engine.Dispose()` **不销毁**它」的装配路径(`AttachWindowRuntime` 的外部窗口重载)。解 plan/19 standalone-editor blocker。
- **公开编辑态宿主 bootstrap**:新增供壳使用的中性 `EditorHostBootstrap`,允许**先于完整 Engine 装配**即可立窗口/GL/`PixelEngine.Gui` ImGui host(项目选择器阶段尚无工程时先出窗口,或创建空窗口只跑 ImGui);打开工程后 Edit 模式装配 → `AttachWindowRuntime`(外部窗口)复用现有窗口/输入/渲染链路;默认编辑器面板注册留在 shell/Editor 侧 `IEditorHostExtension`,Hosting 不暴露或返回 `EditorApp`,避免重新引入 `Hosting→Editor`。
- 与 Demo 路径差别仅在:宿主是壳、默认进入 Edit 模式(sim 暂停)、窗口标题走编辑器命名、并额外注册 GameObject 编辑面板;Play/Edit/Step 三态、快照回滚(`EngineEditorPlaySessionService` + `EngineWorldSnapshotStore`)、sim 控制(`EngineSimulationControlService`)均复用 §3.6 既有能力,不重造。

### 3.9 游戏内 HTML UI 装配(PixelEngine.UI,plan/20)

`EngineBuilder` 增 `EnableHtmlUi(bool)` 与后端选择 `UseUiBackend(UiBackendKind)`(RmlUi 子集为主 / Ultralight 可选 / `ManagedFallbackBackend` 纯托管基线,不变式 #10);**禁用时零开销**(不订阅、不装配)。启用时 Hosting:
- 装配 `GameUiHost`(顶层门面:`IGameUiBackend`/`UiDocumentManager`/`UiInputRouter`/`UiModelBridge`/`UiCompositor`/`UiDiagnostics`)。
- `UiCompositor` 经 plan/08 **显式 UI 层注册接口**(`RenderPipeline.BeforePresentUi`)订阅合成;Hosting 装配次序**先订阅 `UiCompositor`、后订阅 `EditorRenderBridge`/注入式 GUI 钩子**,保证相位[10]叠放为 世界 → 游戏 UI → 编辑器叠层(§3.2)。
- 挂 `GameUiPhaseDriver` 到相位 [0]/[1](输入泵入 → 游戏态→UI 模型 → UI 事件在相位 1 派发 → `Update(dt)`);UI→游戏世界写入**复用 §3.4 延迟命令队列**落正确相位(守相位安全);游戏→UI 只读 `EngineContext` 只读快照。
- `EngineContext` 聚合 `IGameUiService`(`ShowScreen`/`HideScreen`/`PushModal`/`BindModel`/`SetValue`/`TryGetValue`/`UiEventRaised`/`Invoke`),暴露给脚本/Demo。
- `PixelEngine.UI` 复用 `PixelEngine.Gui` 字体栈(含 CJK)与回退,依赖方向 `UI → {Gui, Rendering, Core}`、`Hosting → UI`,与 Editor 同级消费者互不依赖。`ui.update/paint/upload/composite` 分项计时注册 plan/02 诊断,纳入 §3.3 过载降级第二级候选。

## 4. 实现清单

- [x] `EngineBuilder`:fluent 配置(窗口/内部 sim 分辨率/worker 数/GC 模式/Editor 开关/headless/确定性开关/GPU 门控/内容根/起始场景),`Build()→Engine`。[架构 §19.2 配置化]
- [x] `Engine`:持有全部子系统 + `EngineContext`;`Run()`/`RunOneTick()`/`Shutdown()`。
- [x] `EngineContext`:服务定位 + 诊断 + 事件总线 + 时间 + 当前质量档位。
- [x] 子系统装配与**初始化顺序**(§3.1)、脚本 runtime shutdown 与 ALC 可回收路径已覆盖。`EngineBuilderTests.SubsystemsInitializeInOrderAndShutdownInReverseOrder` 已覆盖 Hosting 子系统逆序关闭；`EnginePhaseDriverTests.EngineShutdownDisposesAttachedScriptRuntime` 已覆盖 Engine 关闭会调用已接入 `IScriptRuntime.Shutdown()`；`HotReloadServiceTests`/`AlcCollectibilityTests` 已覆盖热重载旧 ALC 可回收；`docs/runtime-reports/2026-07-02-demo-window-smoke.md` 已记录真实窗口 Content/Simulation/Physics/Audio/Scripting/Rendering/Input 装配后 120 tick 正常退出，并记录 EditorRenderBridge/Hexa ImGui OpenGL3 后端 60 tick 正常退出；该报告已在 `1e02ce3` 后复验普通窗口 120 tick 与 Editor 窗口 60 tick 均自然退出；`docs/runtime-reports/2026-07-02-demo-window-longrun.md` 已记录本机非 Editor 3600 tick 与 Editor 1200 tick 长跑自然退出和峰值工作集。native GL/OpenAL/Box2D 工具级泄漏审计仍由 §5 的 native leak detector 阻塞项闭合。
- [x] `GameLoop.Tick()`:严格 12 相位编排(§3.2),相位间 barrier(plan/02 JobSystem),每帧至多一次 sim/physics step。[不变式 #6,架构 §3.3]
- [x] sim 降频(30Hz)而 render 不降:render 复用上帧世界纹理(必要时整图相机偏移,不插值像素)。[架构 §4.2]
- [x] 各相位入口的调用绑定(Input/Time、Scripts、Residency、Particle 沉积/抛射、CA、Reaction/Lifetime/Material custom update、Temperature、DirtySwap、Physics、BuildFrame、Present、Streaming)。已绑定现有相位 0/1/2/3/4/5/6/7/8/9/10/11,Simulation world 装配时已接入 `ReactionEngine`、反应副作用 sink 与 `BurningCellSystem`，窗口运行时已可把 EditorRenderBridge 接入相位 10 并注册有真实后端的一组 Editor 面板，本机 120 tick/3600 tick 窗口运行与 60 tick/1200 tick Editor 窗口运行已通过，且 `1e02ce3` 后复验普通窗口 120 tick 与 Editor 窗口 60 tick 通过；`docs/runtime-reports/2026-07-02-demo-scripted-window.md` 已记录真实窗口 80 tick 中相位 0 脚本输入、相位 1 脚本/HUD/暂停、相位 3/4/6 cell 写入、相位 8 刚体拆分、相位 9 音频抽取与相位 10 渲染链路均被触发；`lava-mine-goal-probe.scene` 窗口探针进一步验证真实窗口相位可触发 `GoalTrigger`。稳定 60fps、真实窗口人工验收与完整资源释放审计由本文件 §5 对应验收项继续阻塞，不再阻塞相位入口绑定本身。
- [x] 过载降级编排:读诊断 → 五级降级决策 → 经 `EngineContext` 下发质量档位。[架构 §4.3,不变式 #6]
- [x] 脚本服务后端聚合:`IWorldAccess`/`IParticleService`/`IPhysicsService`/`IMaterialRegistry`/`ICamera`/`IInput`/`IEventBus`/`IAudioService`/`ISceneService`/`IDiagnostics`/`IRuntimeControlApi` 的实现注入(plan/11 契约的后端)。cell/material/particle/solid/time/audio/input/camera/lighting/PhysicsSystem 后端已有真实注册，脚本可见刚体 façade 与角色移动已接入 phase 8 flush；Reaction/Lifetime/custom material update 后端已随 Simulation world 装配接入，Diagnostics 角色由 `EngineCounters` 注册，Runtime 控制可暂停/恢复/退出/打开已接入 Editor，并可通过重开关卡快照恢复首个脚本 tick 后的 world/script 基线；脚本热重载可由 `ScriptHotReloadRuntimeOptions` 在 Hosting 装配期创建 watcher，并由 `ScriptRuntime.BeginFrame()` 在相位 1 应用；脚本化窗口短跑已在真实窗口链路中同时消费 input/camera/cell/particle/physics/lighting/audio/diagnostics/runtime 后端并输出摘要。
- [x] 写操作延迟命令队列:脚本/玩法的世界写入入队,在正确相位 flush(配合 plan/11 相位安全模型)。
- [x] `Scene` 模型 + `ISceneService`:加载/卸载/切换;从存档(plan/07)或程序化生成构建起始世界。已完成来源校验与解析,`AttachCurrentSceneWorld` 可显式从 SaveDirectory 或 `.scene InitialSaveDirectory` 装配 live World/Simulation/粒子/Physics 后端并恢复 world seed/game time/刚体快照；`SceneSourceKind.Procedural` 可通过注册式 `IProceduralWorldGenerator` 构建 resident world 并填充初始内容，见 `SceneAndHeadlessTests.AttachCurrentSceneWorldBuildsRegisteredProceduralWorld`。
- [x] 项目模型:内容根、materials/reactions、资产、起始场景引用;`EngineBuilder` 装载。
- [x] Play/Edit/Step 三态机 + 进入 Play 前世界快照、退出回滚(plan/07 快照),脚本生命周期协调(plan/11、plan/12)。已完成模式驱动、StepOnce、resident world 快照/恢复、Editor 临时 Play 快照后端、存活 Behaviour 字段状态恢复、脚本 Scene 拓扑（Play 中新增/删除实体与组件）回滚，以及退出 Play 时对存活 Behaviour 派发 OnDestroy、再次进入 Play 时重新 OnStart。
- [x] **headless 模式**:无窗口/渲染/音频,跑 Core+Sim+Physics+World,固定步数驱动(供 plan/14)。
- [x] 帧节奏与 `FrameClock`(plan/02)对接:固定 dt、时间膨胀、sim/render 解耦的频率管理。
- [x] 公开 API 全部中文 XML 文档注释(脚本 IntelliSense,plan/11/00 §7)。

### 4.1 GUI 宿主中性化与发行解耦(M13 入口门,§3.7)

- [x] 新增中性程序集 `PixelEngine.Gui`(层级在 Rendering 之上、Editor 之下):下沉 `HexaImGuiBackend`、`IGuiContext` 运行时适配(现 `ScriptGuiContext`)、`EditorRenderBridge` 中性部分、字体栈(`EditorFontManager`→`GuiFontManager`,含 CJK)。[架构 §3.1,不变式 #10]
- [x] Hosting `.csproj` **删除**对 `PixelEngine.Editor` 的硬 `ProjectReference`;Hosting 内引用 Editor 类型的源文件改指 `PixelEngine.Gui` 中性类型,编译期不再出现 Editor 类型。
- [x] Hosting 暴露**抽象 GUI/相位[10] 钩子接口**(相位[10]叠层 Render + Play/Edit/相位钩子);玩家运行时为空实现,Editor 具体实现由编辑器壳在装配期注入(不静态引用 Editor)。
- [x] `DemoProgram.cs` 玩家 HUD 改用 `PixelEngine.Gui` 中性 `IGuiContext` host(与 `DemoHud`/`PauseMenu`/`WeaponHud`/`MaterialLegendHud` 同路径);发行玩家包传递闭包不含 `PixelEngine.Editor.dll`,但允许 `Hexa.NET.ImGui`(供 plan/15 player-only audit 前置)。

### 4.2 窗口/GL 所有权解耦 + 编辑态 bootstrap(§3.8,供 plan/19)

- [x] 把 `RenderWindow` + `RenderPipeline` 所有权解耦;新增 attach 到外部(壳拥有)窗口的装配路径,`Engine` 不 own、`Engine.Dispose()` 不销毁该窗口(解 plan/19 blocker)。
- [x] 公开编辑态宿主 bootstrap:先于完整 Engine 装配即可立窗口/GL/ImGui host(项目选择器阶段);打开工程后 Edit 装配 + `AttachWindowRuntime(外部窗口)`;默认编辑器面板注册留在 shell/Editor 侧 `IEditorHostExtension`,不让 Hosting 返回 `EditorApp`。
- [x] 上述窗口/GL 所有权解耦与编辑态 bootstrap 均为**中性 Hosting API**(不依赖 `PixelEngine.Editor`);编辑器面板经抽象 GUI 钩子由壳在装配期注入(§4.1),玩家包因 Hosting 不引用 Editor 而自然不含编辑器闭包,**不使用条件编译(`#if`)剥离**。

### 4.3 场景保存往返 + authoring 物化公开 API(§3.5,供 plan/19)

- [x] `.scene` schema v2:`EngineSceneEntityDocument` 增 `ParentId` + `Transform`(TRS)块 + `Vector2` 字段类型;`FormatVersion` 1→2 且保 v1 兼容(v1 档按默认根实体 + 单位 Transform 物化)。
- [x] 新增 Hosting 公开 writer `SaveSceneDocument(EngineSceneDocument, path)`(源生成 `EngineSceneJsonContext` 扩展):稳定排序(按 StableId 升序)、往返等价(读→写→读逐字段一致,供 plan/14)。
- [x] `EngineSceneDocument`→运行时 `Scripting.Scene` 物化公开 API(Transform 世界 TRS 烘焙、字段绑定扩展 Transform TRS/Vector2);运行时 Scene 保持扁平 DOD 不引 parent 指针;material 引用走稳定 Name(守 #8)。新增 `Engine.AttachScriptScene(Scripting.Scene)` 供编辑器壳把外部 authoring 投影接到当前 Hosting 场景,且不让 Hosting 持有 StableId 映射。authoring 层级模型 ⇄ `EngineSceneDocument` 映射归编辑器壳(plan/19);Hosting 侧 writer 与物化 API 为**中性**、玩家运行时亦复用(加载 `.scene`),**不条件编译剥离**。
- [x] 编辑器壳侧 authoring 场景模型 ⇄ `EngineSceneDocument` 双向映射(EditorSceneModel/GameObject 层级命令栈),见 plan/19 §4.5/§4.9。

### 4.4 相位[10] 子序 + 游戏内 HTML UI 装配(§3.2/§3.9,配 plan/20)

- [ ] 相位[10] 子序细化为 世界渲染/present → `UiCompositor.Composite`(游戏 UI,脏时光栅化) → 注入式 GUI 相位[10]钩子(Editor 叠层,开发构建) → `SwapBuffers`;叠放次序 世界 → 游戏 UI → 编辑器叠层。
- [ ] `EngineBuilder` 增 `EnableHtmlUi(bool)` + `UseUiBackend(UiBackendKind)`(RmlUi 主/Ultralight 可选/ManagedFallback 基线);禁用零开销。
- [ ] 启用时装配 `GameUiHost`;`UiCompositor` 经 plan/08 显式 UI 层挂点 `BeforePresentUi` 订阅,装配次序**先 UiCompositor、后 EditorRenderBridge/注入钩子**。
- [ ] 挂 `GameUiPhaseDriver` 到相位 [0]/[1](输入泵入/游戏态→UI/UI 事件相位1派发/`Update(dt)`);UI→游戏世界写入复用 §3.4 延迟命令队列落正确相位。
- [ ] `EngineContext` 聚合 `IGameUiService`(ShowScreen/HideScreen/PushModal/BindModel/SetValue/TryGetValue/UiEventRaised/Invoke),暴露脚本/Demo;`ui.*` 分项计时注册 plan/02 诊断,纳入 §3.3 降级第二级候选。

### 4.5 player 启动分派(§3.5)

- [ ] player 读 `content/startup.json` 按 `SceneSourceKind` 分派:`FromSave` 从 SaveDirectory 装配、`Procedural` 记生成器键经 `IProceduralWorldGenerator` 构建(默认 `playable-world.scene` / `PlayableWorldDirector`);编辑器内打包场景清单数据源 = 扫描 `content/scenes/`。

## 5. 验收标准

- [x] `EngineBuilder().…​.Build().Run()` 能装配全部子系统并跑稳定 60fps 空场景。Rendering/Audio/Physics/Scripting/Editor 已有真实运行入口，`--window-ticks 3600` 本机窗口长跑已验证可有限运行并退出；有限窗口短跑会输出 `elapsed_ms`/`avg_tick_ms`/`last_profile_ms` 与最慢相位，`docs/runtime-reports/2026-07-02-demo-window-smoke.md` 已记录 `empty-window-probe.scene` 真实空 scene 120 tick 旧样本曾因 `BuildRenderBuffer=25.52` / `RenderBufferBuild=25.51` 超预算，后续修复纯矢量 debug overlay 误传与透明 Empty 全世界清屏路径后，120 tick 复验为 `avg_tick_ms=12.82`、`last_profile_ms=7.79`，600 tick 复验为 `avg_tick_ms=7.83`、`last_profile_ms=7.20`，均低于 16.67ms 帧预算。
- [x] 12 相位顺序与架构 §3.3 完全一致;用诊断计时器可见各相位耗时。
- [x] sim 降到 30Hz 时画面仍 60fps 出帧、世界慢放、无 death spiral：`EnginePhasePipelineTests` 覆盖 30Hz 跳帧仍执行 render/streaming 相位且不 accumulator 追帧，`EngineOverloadControllerTests.OverloadedSim30HzKeepsRenderFramesWithoutCatchUp` 覆盖人工过载进入 Sim30Hz 后 render 逐帧执行。
- [x] 过载降级按五级顺序触发：`EngineOverloadControllerTests` 已覆盖五级 tier 推进、Sim30Hz 下发给 `FrameClock`、降温度场、降光照、远区 chunk 隔帧、render 不追帧与质量档位服务注册。
- [!] Editor 真实窗口观测/覆盖仍缺人工复核证据：Editor 运行入口、Hexa ImGui OpenGL3 后端与诊断面板已接入，并通过 EditorShell 有限窗口短跑；脚本化窗口短跑已证明 Demo HUD 绑定诊断数据且 Escape 可进入暂停菜单状态；`tools/demo-manual-acceptance-preflight.ps1` 的 `hudMenuEditorVideo` scope 负责索引真实窗口 UI 布局、鼠标点击、Editor dockspace/覆盖操作和菜单链路证据，且该 scope 的 manifest checklist 必须声明 `hudReadable=true`、`menuButtonsClicked=true`、`editorDockspaceOpened=true`，criteria 必须用同一组 key 写明人工判定标准；`-RunScriptedProbes` 会为每个机器 probe 写出 `capture.bmp` 并记录截图 path/sha256/尺寸/位深/`capture_unique_visible_pixels`，还会解析摘要键值并校验默认可玩、route-attempt、editor-window、goal、health、camera、reaction、audio 与 particle-light probe 的关键数值/布尔语义阈值，其中 editor-window 必须经 `apps/PixelEngine.Editor.Shell` 的 `--window-ticks` / `--scripted-probe` 入口启动并输出 `editor_enabled=True`、`editor_running=True`、`editor_panels>=1`、`editor_bridge_frames>=1` 与 `render_camera_synced=True`，避免只凭 marker 存在接受机器 probe；schema、未知 scope、元数据、缺 checklist/criteria、视频时长不足、MP4/MOV 缺 `moov` 视频 track/正 duration/非空 `mdat`、WebM/MKV 缺可解析 video stream/duration、缺 `reviewSessionId`/`gitCommit` 同源字段、不同 session/commit 拼接、缺文件、sha256 不匹配、scripted probe 语义阈值不达标或黑屏/纯色截图会写出 `blocked_invalid_manual_evidence` 报告并以 5 退出；`PerformanceHardeningToolingDisciplineTests.DemoManualAcceptancePreflightRejectsInvalidMetadata`、`DemoManualAcceptancePreflightRejectsRenamedTextAsVideoEvidence`、`DemoManualAcceptancePreflightRejectsFtypOnlyVideoEvidence`、`DemoManualAcceptancePreflightRejectsMixedReviewSessionIds`、`DemoManualAcceptancePreflightRejectsScriptedProbeSummaryBelowSemanticThreshold` 与 `DemoManualAcceptancePreflightRejectsBlankScriptedProbeCapture` 已锁定过短视频、文本改名视频、只有 `ftyp` 头的伪 MP4、不同人工验收 session 拼接、缺 checklist/criteria、过短观察说明、scripted probe 摘要不达标和纯黑截图不能冒充人工验收，但 `scripted_probe_only`、截图与 `manual_evidence_attached_pending_review` 仍不等于验收通过。
- [x] 脚本经 `EngineContext` 能读写世界/建刚体/播音效,写操作落在正确相位(配合 plan/11 测试)。AudioService 与 PhysicsSystem 后端已注册，脚本可见 Physics 建/查/控/毁刚体命令与角色移动已在 phase 8 step 前 flush。
- [x] Play/Edit/Step 切换正确:进入 Play 快照、退出回滚到编辑态,脚本 OnStart/OnDestroy 正确触发。world 快照/回滚已由 `EngineWorldSnapshotStore` 接入 Editor 临时 Play，`EngineEditorPlaySessionService.ExitPlay()` 已结束脚本 Play Session 并允许再次进入 Play 时重新 OnStart，存活 Behaviour 字段与脚本 Scene 拓扑均可恢复。
- [x] headless 模式可被 plan/14 测试/基准以确定步数驱动,无窗口依赖。
- [!] 关闭时 native 资源与 ALC 正确释放,无泄漏(配合 plan/14 scripting 测试)。Hosting 已验证关闭时释放 `IScriptRuntime`，Scripting 已验证热重载旧 ALC 可回收；本机真实窗口非 Editor 3600 tick 与 Editor 1200 tick 进程均 exit=0，外部采样峰值工作集约 163 MB；`OpenAlBackend`/`NullAudioBackend` 已暴露 `LiveSourceCount`、`LiveBufferCount` 与 `LiveObjectCount`，`AudioVoicePoolTests.VoicePoolDisposesAllPreallocatedSourcesForLeakEvidence` 覆盖 source 预分配后释放归零，后续 detector 可直接读取 OpenAL live object 计数；`PhysicsSystem.LiveBodyCount` 已覆盖动态刚体与静态地形 collider 的 live Box2D body 计数，`PhysicsSystemFacadeTests.OwnedWorldLiveBodyCountReturnsZeroAfterDestroyAndShutdown` 覆盖显式销毁与 owned world shutdown 后归零；`ScriptHotReloadController.CollectAndCountUnloadedLoadContextsAlive()` 已暴露旧脚本 ALC 调用 `Unload()` 后经完整 GC 仍存活的数量，`HotReloadServiceTests.RepeatedReloadsUnloadPreviousContexts` 覆盖 50 次热重载后该计数归零；Rendering 已新增 `GlResourceTracker`，覆盖引擎封装持有的 texture/buffer/framebuffer/shader program/compute program/shader/VAO/timer query live-count，用于本进程内 GL wrapper 泄漏定位，但不替代 driver 级 detector；`tools/native-leak-preflight.ps1` 可生成 process smoke、单 detector 报告索引与四类 evidence manifest hash 清单，无 detector 时为 `blocked_missing_detector` 或 `process_smoke_only`，单 detector 报告若缺 `detector`、`conclusion=no_leaks`、`scopes=GL; OpenAL; Box2D; ALC` 或四类 live count 归零字段会被拒绝为 `blocked_invalid_native_leak_evidence`，机器可读覆盖齐全也仅为 `detector_report_attached_pending_review`；manifest 缺 scope/report/hash 为 `blocked_missing_scope_evidence`，JSON/schema/未知 scope/hash mismatch、缺 `detectorRunId`/`gitCommit` 或不同 detector run/commit 拼接为 `blocked_invalid_native_leak_evidence`，其中未知 detector scope 由 `PerformanceHardeningToolingDisciplineTests.NativeLeakPreflightRejectsUnknownScopeWithReport` 锁定；四类 evidence manifest 还要求每个报告声明匹配的 `scope`/`detector`、同源 `detectorRunId`/`gitCommit`、`conclusion=no_leaks`，以及释放后 live-object 计数归零字段 `glObjectsLiveAfterShutdown`、`openAlObjectsLiveAfterShutdown`、`box2DBodiesLiveAfterShutdown`、`alcLoadContextsAliveAfterUnload`，由 `PerformanceHardeningToolingDisciplineTests.NativeLeakPreflightRejectsMixedDetectorRunIds`、`NativeLeakPreflightRejectsDetectorReportWithoutNoLeaksConclusion` 与 `NativeLeakPreflightRejectsDetectorReportWithoutZeroLiveCounts` 锁定；证据齐全也仅为 `detector_evidence_attached_pending_review`。`tools/PixelEngine.Tools.ManagedNativeLeakDetector` 已加入 solution，可生成本机 `evidence.json` 与四类 scope report，并通过 `OpenAlDevice`/`NullAudioBackend`、owned `PhysicsSystem` 和 `ScriptHotReloadController` 采集 `openAlObjectsLiveAfterShutdown=0`、`box2DBodiesLiveAfterShutdown=0`、`alcLoadContextsAliveAfterUnload=0`；GL scope 会先尝试创建真实 `RenderWindow` GL context 并分配/释放 `GlTexture`、`GlBuffer`、`Framebuffer`、`ShaderProgram` 等 Rendering wrapper，成功时以 `coverage=gl_context_rendering_wrappers` 采集 `glObjectsLiveAfterShutdown=0`，宿主不支持 GL 时才明确降级为 `managed_no_gl_context` 并记录失败原因。阻塞:仍缺跨平台 runner 与 GL driver 级专用 native leak detector 证据，managed detector 的 `gl_context_rendering_wrappers` 或 `managed_no_gl_context` 都不能替代真实 GL 对象创建/销毁后的外部 detector 或人工工具级报告。
- [x] Demo(plan/13)仅经 Hosting 公开 API 启动,无引擎内部后门。
- [x] GUI 宿主中性化落地可验证:Hosting 编译期不含 `PixelEngine.Editor` 引用;发行玩家包传递闭包无 `PixelEngine.Editor.dll` 与编辑器专属面板,但保留 `PixelEngine.Gui` + `Hexa.NET.ImGui`;玩家 HUD 经 `PixelEngine.Gui` 中性 host 正常显示(plan/15 audit 前置)。[§3.7]
- [ ] 编辑器壳可 attach Hosting 外部窗口装配 Engine、关闭工程时 `Engine.Dispose()` 逆序释放而**窗口保留**、重建 session 再装配成功;Shell 侧 `IEditorHostExtension` 可创建 `EditorApp` 并注册默认面板与 GameObject 面板。[§3.8]
- [x] `SaveSceneDocument` 往返等价:`.scene` v2(ParentId/Transform/Vector2)读→写→读逐字段一致、稳定排序;v1 档可被 v2 loader 兼容读取;`EngineSceneDocument`→运行时物化后运行时 Scene 为扁平 DOD、层级已烘焙为世界 TRS(配 plan/14 性质测试)。[§3.5]
- [ ] 启用 HTML UI 时相位[10]合成次序为 世界 → 游戏 UI → 编辑器叠层,合成后 GL 状态正确恢复;`IGameUiService` 事件在相位 1 派发、世界写入经延迟队列落正确相位不破坏帧节奏;禁用 HTML UI 零开销。[§3.2/§3.9,#6]
- [ ] player `content/startup.json` 分派正确:`Procedural` 键 `PlayableWorldDirector` 构建 `playable-world.scene`、`FromSave` 从存档装配;两路径均无窗口/后门依赖。[§3.5]
- [x] **editor-window 证据入口迁移**:plan/18 §5 的 editor-window 人工验收/preflight/scripted-probe 证据入口从 Demo `--editor` **迁移到** `apps/PixelEngine.Editor.Shell`(壳提供等价 `--window-ticks`/scripted-probe/截图入口);`tools/demo-manual-acceptance-preflight.ps1` 的 editor-window probe 改由壳进程启动并输出 `editor_enabled=True`/`editor_running=True`/`editor_panels>=1`/`editor_bridge_frames>=1`/`render_camera_synced=True`,原 Demo `--editor` 入口下线。(上方 `- [!]` Editor 真实窗口观测阻塞项在迁移后继续由壳入口闭合,证据链契约不变。)[§3.8,plan/19 §5]

## 6. 依赖关系

- 前置:plan/01(项目)、plan/02(时间/JobSystem/事件总线/诊断)、plan/03–10(被编排的子系统)。
- 紧耦合:plan/11(脚本服务后端在此聚合;Behaviour 生命周期由此驱动)、plan/12(Play/Edit 模式、相位10 Editor.Render 钩子)。
- 后置:plan/13(Demo 经 Hosting 启动)、plan/14(headless 驱动测试/基准)。
- 后置(M13/M14 新增):plan/19(`apps/PixelEngine.Editor.Shell` 消费 §3.8 窗口所有权解耦 + 编辑态 bootstrap + §3.5 `SaveSceneDocument`/authoring 物化 API + §5 editor-window 证据入口迁移)、plan/20(`PixelEngine.UI` 消费 §3.9 `EnableHtmlUi`/`UseUiBackend`/`GameUiHost`/`GameUiPhaseDriver`/`IGameUiService` 相位挂载与聚合)、plan/15(player-only audit 以 §3.7 GUI 中性化落地为前置)。
- 强前置(M13 入口门):§3.7 GUI 宿主中性化重构(新增 `PixelEngine.Gui`、Hosting 删 Editor 引用、暴露注入式 GUI 钩子)是 plan/19 壳注入、plan/15 player-only 审计、plan/20 UI 字体/回退复用三者的共同前置。
- 协调点:plan/10 的 `AudioEvent` 类型经 plan/02 事件总线流转(Hosting 不重定义);plan/04 的 `IMaterialRegistry` 后端;plan/08 的 `RenderPipeline.BeforePresentUi` 显式 UI 层挂点(游戏 UI 合成与编辑器叠层共用,§3.2/§3.9)。

## 7. 提交节点

- [x] `feat(host): EngineBuilder/Engine/EngineContext 装配与生命周期`
- [x] `feat(host): 12 相位主循环编排 + 固定步长不追帧 + sim 降频`
- [x] `feat(host): 过载降级编排 + 脚本服务后端聚合`。
- [x] `feat(host): 场景/项目模型 + Play/Edit/Step 模式 + headless`。
- [x] `refactor(host): 新增 PixelEngine.Gui 中性 ImGui host + Hosting 删 Editor 引用 + 注入式相位[10] GUI 钩子`(M13 入口门,§3.7)。
- [x] `feat(host): 窗口/GL 所有权解耦 + 公开编辑态 bootstrap(供 EditorShell)`(§3.8)。
- [x] `feat(host): SaveSceneDocument 往返 writer + .scene v2 schema + EngineSceneDocument→运行时物化公开 API`(§3.5)。
- [ ] `feat(host): EngineBuilder EnableHtmlUi/UseUiBackend + GameUiHost 装配 + 相位[10]子序 + IGameUiService 聚合`(§3.2/§3.9)。
- [x] `refactor(host): editor-window 证据入口从 Demo --editor 迁移到 EditorShell`(§5)。
