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
- 依赖方向(plan/00 §5):`Hosting` 位于子系统之上、Demo/Editor 之下——`Demo/Editor → Hosting → {Rendering, Audio, Physics, World, Serialization, Content, Scripting, Simulation} → Interop → Core`。Hosting 引用全部子系统以装配它们;子系统不反向依赖 Hosting。

## 3. 详细设计

### 3.1 装配与生命周期

`EngineBuilder` 用 fluent 配置:窗口/分辨率、内部 sim 分辨率(架构 §19.2 待决项,做成配置)、worker 线程数、GC 模式、是否启用 Editor、是否 headless、确定性模式开关(架构 §6.2)、GPU 后端门控(plan/09)、内容根目录、起始场景。`Build()` 产出 `Engine`。

`Engine` 持有所有子系统实例与一个 `EngineContext`(服务定位 + 诊断 + 事件总线 + 时间)。初始化顺序:Core(JobSystem/时钟/事件总线/诊断)→ Content(加载 materials/reactions 填 Simulation 表)→ Simulation(CellGrid/chunk)→ World(驻留/相机)→ Physics(Box2D world + task 桥绑到 JobSystem)→ Audio(OpenAL 设备)→ Rendering(GL 上下文/纹理/管线)→ GPU(能力门控)→ Scripting(ALC/Behaviour 宿主)→ Editor(可选,共享 GL 上下文)。关闭顺序逆序,确保 native(Box2D/OpenAL/GL)正确释放、ALC 卸载。

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
[10] GPU Upload & Render    Rendering.Present() + GPU(plan/08/09)+ Editor.Render()(若启用,plan/12)
[11] World streaming        World.KickStreaming()(后台 I/O,只备字节,plan/07)
```

Hosting 不实现相位内逻辑,只保证**顺序、barrier、dt 一致、sim 降频时 render 仍出帧**(复用上帧世界纹理,架构 §4.2)。

### 3.3 过载降级编排(架构 §4.3 决策者)

Hosting 读 plan/02 诊断计时器,按架构 §4.3 五级顺序决策降级:①降/关全分辨率热场 → ②降光照质量(关 RC/bloom) → ③远 chunk 降频 → ④sim 降到 30Hz → ⑤接受 <60fps(因不追帧,最坏只是低帧率不 death spiral)。决策结果通过 `EngineContext` 的质量档位下发给各子系统;编辑器(plan/12)可显示/覆盖当前档位。

### 3.4 公开门面与脚本服务后端

`EngineContext` 暴露脚本(plan/11)与 Demo(plan/13)所需的服务:`IWorldAccess`(读写 cell/材质/采样固体像素/raycast)、`IParticleService`(plan/05)、`IPhysicsService`(建/查/控刚体 + 角色控制器,plan/06)、`IMaterialRegistry`(plan/04)、`ICamera`(plan/08/07)、`IInput`、`IEventBus`(plan/02)、`IAudioService`(plan/10)、`ISceneService`、`IDiagnostics`。这些是 plan/11 世界脚本接口的**实现后端**:plan/11 定义脚本可见契约,Hosting 在此聚合各子系统实现并注入。写操作经延迟命令队列落到正确相位(plan/11 §相位安全模型)。

### 3.5 场景/项目模型与加载

`Scene` = 起始世界(地形/材质布局,可来自存档 plan/07 或程序化生成)+ 实体与脚本组件(plan/11 轻量组件数组)+ 相机/光照配置。`ISceneService` 负责加载/卸载/切换场景。项目模型:内容根(content/)、materials/reactions、资产、起始场景引用。Demo 的 `Program.cs` 经 `EngineBuilder` 指定项目与起始场景。

### 3.6 Play / Edit 模式与 headless

与编辑器(plan/12)协调三态:Edit(暂停 sim、可编辑世界/材质/实体)、Play(运行游戏与脚本)、Step(单步恰一 tick)。模式切换时脚本生命周期(OnStart/OnDestroy)与世界快照(进入 Play 前快照、退出 Play 回滚,类 Unity)由 Hosting 协调(用 plan/07 快照)。**headless 模式**:无窗口/无渲染/无音频,仅跑 Core+Sim+Physics+World,供 plan/14 测试与 BenchmarkDotNet 基准驱动确定步数。

## 4. 实现清单

- [x] `EngineBuilder`:fluent 配置(窗口/内部 sim 分辨率/worker 数/GC 模式/Editor 开关/headless/确定性开关/GPU 门控/内容根/起始场景),`Build()→Engine`。[架构 §19.2 配置化]
- [x] `Engine`:持有全部子系统 + `EngineContext`;`Run()`/`RunOneTick()`/`Shutdown()`。
- [x] `EngineContext`:服务定位 + 诊断 + 事件总线 + 时间 + 当前质量档位。
- [!] 子系统装配与**初始化顺序**(§3.1);native(Box2D/OpenAL/GL)与 ALC 的正确释放顺序。`EngineBuilderTests.SubsystemsInitializeInOrderAndShutdownInReverseOrder` 已覆盖 Hosting 子系统逆序关闭；`EnginePhaseDriverTests.EngineShutdownDisposesAttachedScriptRuntime` 已覆盖 Engine 关闭会调用已接入 `IScriptRuntime.Shutdown()`；`HotReloadServiceTests`/`AlcCollectibilityTests` 已覆盖热重载旧 ALC 可回收；`docs/runtime-reports/2026-07-02-demo-window-smoke.md` 已记录真实窗口 Content/Simulation/Physics/Audio/Scripting/Rendering/Input 装配后 120 tick 正常退出，并记录 EditorRenderBridge/Hexa ImGui OpenGL3 后端 60 tick 正常退出；该报告已在 `1e02ce3` 后复验普通窗口 120 tick 与 Editor 窗口 60 tick 均自然退出；`docs/runtime-reports/2026-07-02-demo-window-longrun.md` 已记录本机非 Editor 3600 tick 与 Editor 1200 tick 长跑自然退出和峰值工作集；`tools/native-leak-preflight.ps1` 已能把缺少 detector 报告显式标为 `blocked_missing_detector`/`process_smoke_only`，并通过 `EvidenceManifestPath` 对 `gl/openal/box2d/alc` 四类工具级报告做 scope/hash 清单预检，避免用进程 smoke 冒充 native 泄漏验收。阻塞:仍缺 6-RID runner、专用 native leak detector 与 GPU/OpenAL/Box2D 工具级资源审计证据。
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

## 5. 验收标准

- [x] `EngineBuilder().…​.Build().Run()` 能装配全部子系统并跑稳定 60fps 空场景。Rendering/Audio/Physics/Scripting/Editor 已有真实运行入口，`--window-ticks 3600` 本机窗口长跑已验证可有限运行并退出；有限窗口短跑会输出 `elapsed_ms`/`avg_tick_ms`/`last_profile_ms` 与最慢相位，`docs/runtime-reports/2026-07-02-demo-window-smoke.md` 已记录 `empty-window-probe.scene` 真实空 scene 120 tick 旧样本曾因 `BuildRenderBuffer=25.52` / `RenderBufferBuild=25.51` 超预算，后续修复纯矢量 debug overlay 误传与透明 Empty 全世界清屏路径后，120 tick 复验为 `avg_tick_ms=12.82`、`last_profile_ms=7.79`，600 tick 复验为 `avg_tick_ms=7.83`、`last_profile_ms=7.20`，均低于 16.67ms 帧预算。
- [x] 12 相位顺序与架构 §3.3 完全一致;用诊断计时器可见各相位耗时。
- [x] sim 降到 30Hz 时画面仍 60fps 出帧、世界慢放、无 death spiral(注入人工过载验证)。
- [!] 过载降级按五级顺序触发且可在编辑器观测/覆盖。阻塞:五级顺序与 Sim30Hz 已测试,Editor 运行入口、Hexa ImGui OpenGL3 后端与诊断面板已接入，并通过 60 tick Editor 窗口短跑；脚本化窗口短跑已证明 Demo HUD 绑定诊断数据且 Escape 可进入暂停菜单状态；`tools/demo-manual-acceptance-preflight.ps1` 的 `hudMenuEditorVideo` scope 负责索引真实窗口 UI 布局、鼠标点击、Editor dockspace/覆盖操作和菜单链路证据，但 `manual_evidence_attached_pending_review` 仍不等于验收通过。仍缺对应人工复核证据。
- [x] 脚本经 `EngineContext` 能读写世界/建刚体/播音效,写操作落在正确相位(配合 plan/11 测试)。AudioService 与 PhysicsSystem 后端已注册，脚本可见 Physics 建/查/控/毁刚体命令与角色移动已在 phase 8 step 前 flush。
- [x] Play/Edit/Step 切换正确:进入 Play 快照、退出回滚到编辑态,脚本 OnStart/OnDestroy 正确触发。world 快照/回滚已由 `EngineWorldSnapshotStore` 接入 Editor 临时 Play，`EngineEditorPlaySessionService.ExitPlay()` 已结束脚本 Play Session 并允许再次进入 Play 时重新 OnStart，存活 Behaviour 字段与脚本 Scene 拓扑均可恢复。
- [x] headless 模式可被 plan/14 测试/基准以确定步数驱动,无窗口依赖。
- [!] 关闭时 native 资源与 ALC 正确释放,无泄漏(配合 plan/14 scripting 测试)。Hosting 已验证关闭时释放 `IScriptRuntime`，Scripting 已验证热重载旧 ALC 可回收；本机真实窗口非 Editor 3600 tick 与 Editor 1200 tick 进程均 exit=0，外部采样峰值工作集约 163 MB；`tools/native-leak-preflight.ps1` 可生成 process smoke、detector 报告索引与四类 evidence manifest hash 清单，但无 detector 时默认失败，证据齐全也仅为 `pending_review`。阻塞:仍缺跨平台 runner 与专用 GL/OpenAL/Box2D native leak detector 证据。
- [x] Demo(plan/13)仅经 Hosting 公开 API 启动,无引擎内部后门。

## 6. 依赖关系

- 前置:plan/01(项目)、plan/02(时间/JobSystem/事件总线/诊断)、plan/03–10(被编排的子系统)。
- 紧耦合:plan/11(脚本服务后端在此聚合;Behaviour 生命周期由此驱动)、plan/12(Play/Edit 模式、相位10 Editor.Render 钩子)。
- 后置:plan/13(Demo 经 Hosting 启动)、plan/14(headless 驱动测试/基准)。
- 协调点:plan/10 的 `AudioEvent` 类型经 plan/02 事件总线流转(Hosting 不重定义);plan/04 的 `IMaterialRegistry` 后端。

## 7. 提交节点

- [x] `feat(host): EngineBuilder/Engine/EngineContext 装配与生命周期`
- [x] `feat(host): 12 相位主循环编排 + 固定步长不追帧 + sim 降频`
- [x] `feat(host): 过载降级编排 + 脚本服务后端聚合`。
- [x] `feat(host): 场景/项目模型 + Play/Edit/Step 模式 + headless`。
