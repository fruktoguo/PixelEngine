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
- [!] 子系统装配与**初始化顺序**(§3.1);native(Box2D/OpenAL/GL)与 ALC 的正确释放顺序。阻塞:Rendering/Audio/Physics/Scripting 已有 Hosting 装配与逆序释放基础,Editor 仍缺完整真实入口,ALC 释放顺序需对应脚本子系统落地后接入。
- [x] `GameLoop.Tick()`:严格 12 相位编排(§3.2),相位间 barrier(plan/02 JobSystem),每帧至多一次 sim/physics step。[不变式 #6,架构 §3.3]
- [x] sim 降频(30Hz)而 render 不降:render 复用上帧世界纹理(必要时整图相机偏移,不插值像素)。[架构 §4.2]
- [!] 各相位入口的调用绑定(Input/Time、Scripts、Residency、Particle 沉积/抛射、CA、Temperature、DirtySwap、Physics、BuildFrame、Present、Streaming)。阻塞:已绑定现有相位 0/1/2/3/4/5/6/7/8/9/10/11,相位 10 的 Editor.Render 叠加仍等待 Editor 后端。
- [x] 过载降级编排:读诊断 → 五级降级决策 → 经 `EngineContext` 下发质量档位。[架构 §4.3,不变式 #6]
- [!] 脚本服务后端聚合:`IWorldAccess`/`IParticleService`/`IPhysicsService`/`IMaterialRegistry`/`ICamera`/`IInput`/`IEventBus`/`IAudioService`/`ISceneService`/`IDiagnostics` 的实现注入(plan/11 契约的后端)。进展:cell/material/particle/solid/time/audio/input/camera/lighting/PhysicsSystem 后端已有真实注册,脚本可见刚体 façade 已接入 phase 8 flush；阻塞:Diagnostics/GUI 与角色移动延迟命令仍未形成完整聚合。
- [x] 写操作延迟命令队列:脚本/玩法的世界写入入队,在正确相位 flush(配合 plan/11 相位安全模型)。
- [!] `Scene` 模型 + `ISceneService`:加载/卸载/切换;从存档(plan/07)或程序化生成构建起始世界。阻塞:已完成来源校验与解析,`AttachCurrentSceneWorld` 可显式从 SaveDirectory 或 `.scene InitialSaveDirectory` 装配 live World/Simulation/粒子/Physics 后端并恢复 world seed/game time/刚体快照；程序化 world generator 仍未完成。
- [x] 项目模型:内容根、materials/reactions、资产、起始场景引用;`EngineBuilder` 装载。
- [!] Play/Edit/Step 三态机 + 进入 Play 前世界快照、退出回滚(plan/07 快照),脚本生命周期协调(plan/11、plan/12)。阻塞:已完成模式驱动与 StepOnce,快照回滚需要完整 world snapshot 聚合,脚本生命周期需 plan/11 Behaviour 宿主。
- [x] **headless 模式**:无窗口/渲染/音频,跑 Core+Sim+Physics+World,固定步数驱动(供 plan/14)。
- [x] 帧节奏与 `FrameClock`(plan/02)对接:固定 dt、时间膨胀、sim/render 解耦的频率管理。
- [x] 公开 API 全部中文 XML 文档注释(脚本 IntelliSense,plan/11/00 §7)。

## 5. 验收标准

- [!] `EngineBuilder().…​.Build().Run()` 能装配全部子系统并跑稳定 60fps 空场景。阻塞:Rendering/Audio/Physics/Scripting 已有真实运行入口,Editor 子系统尚未提供完整运行入口,稳定 60fps 空场景仍需 plan/14 运行态验证。
- [x] 12 相位顺序与架构 §3.3 完全一致;用诊断计时器可见各相位耗时。
- [x] sim 降到 30Hz 时画面仍 60fps 出帧、世界慢放、无 death spiral(注入人工过载验证)。
- [!] 过载降级按五级顺序触发且可在编辑器观测/覆盖。阻塞:五级顺序与 Sim30Hz 已测试,Editor 覆盖 UI 需 plan/12。
- [!] 脚本经 `EngineContext` 能读写世界/建刚体/播音效,写操作落在正确相位(配合 plan/11 测试)。进展:AudioService 与 PhysicsSystem 后端已注册,脚本可见 Physics 建/查/控/毁刚体命令已在 phase 8 step 前 flush；阻塞:角色移动延迟命令与完整 Diagnostics/GUI 聚合仍未完成。
- [!] Play/Edit/Step 切换正确:进入 Play 快照、退出回滚到编辑态,脚本 OnStart/OnDestroy 正确触发。阻塞:快照聚合与脚本生命周期需 plan/11/12。
- [x] headless 模式可被 plan/14 测试/基准以确定步数驱动,无窗口依赖。
- [!] 关闭时 native 资源与 ALC 正确释放,无泄漏(配合 plan/14 scripting 测试)。阻塞:ALC 与 native 子系统尚未落地。
- [x] Demo(plan/13)仅经 Hosting 公开 API 启动,无引擎内部后门。

## 6. 依赖关系

- 前置:plan/01(项目)、plan/02(时间/JobSystem/事件总线/诊断)、plan/03–10(被编排的子系统)。
- 紧耦合:plan/11(脚本服务后端在此聚合;Behaviour 生命周期由此驱动)、plan/12(Play/Edit 模式、相位10 Editor.Render 钩子)。
- 后置:plan/13(Demo 经 Hosting 启动)、plan/14(headless 驱动测试/基准)。
- 协调点:plan/10 的 `AudioEvent` 类型经 plan/02 事件总线流转(Hosting 不重定义);plan/04 的 `IMaterialRegistry` 后端。

## 7. 提交节点

- [x] `feat(host): EngineBuilder/Engine/EngineContext 装配与生命周期`
- [x] `feat(host): 12 相位主循环编排 + 固定步长不追帧 + sim 降频`
- [!] `feat(host): 过载降级编排 + 脚本服务后端聚合`。阻塞:过载已落地,脚本刚体后端与 phase 8 flush 已接通,但完整脚本服务后端仍需 Diagnostics/GUI 与角色移动延迟命令聚合。
- [!] `feat(host): 场景/项目模型 + Play/Edit/Step 模式 + headless`。阻塞:模型/模式/headless/Physics phase 8 基础已落地,完整快照/脚本生命周期仍需后续计划。
