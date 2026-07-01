# Plan 13 — 落沙游戏 Demo（demo/PixelEngine.Demo）

> 本文件定义在 PixelEngine 引擎之上开发的落沙游戏 Demo。Demo 与引擎的关系等同「Unity 游戏之于 Unity 引擎」：**只依赖引擎的公开 API，绝不触碰引擎内部类**。Demo 是引擎公开 API 的 dogfood 验证——若某玩法能力只能靠引擎内部实现，即判定引擎 API 设计有缺陷，记为「需引擎补 API」并上报，**不得在 Demo 里开后门**（`AGENTS.md §0`）。
> 权威设计依据：`../docs/PixelEngine-架构与需求设计.md`（下称架构文档）。技术栈定稿：`00-conventions-and-techstack.md`。开发宪法：`../AGENTS.md`。
> 状态：`- [ ]` 未开始 / `- [x]` 完成并自测通过 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。

---

## 1. 目标与范围

本文件交付一个**侧视（side-view）落沙沙盒游戏**：一名可操作角色（kinematic AABB）在可被任意挖掘 / 爆破的像素地形上跑、跳、蹬墙，地形与物质遵循引擎的全像素 falling-sand 模拟。Demo 是「在引擎上做游戏」的端到端证明，必须把架构文档列出的全部世界能力——完整材质集与反应、像素簇→Box2D 刚体、自由粒子、emissive + fog-of-war 光照、材质化音频、脚本热重载、内嵌编辑器——在一个**可玩关卡**里串成最低完整玩法（架构 §18 M10、§1.2）。

落在范围内的内容是：宿主启动与主循环装配（经 `PixelEngine.Hosting`）；继承脚本基类 `Behaviour`（plan/11）的玩家角色控制器、相机跟随、材质笔刷、爆破工具、关卡生成器、物质发射器、HUD 与暂停菜单；Demo 自带的内容资产（`content/materials.json`、`content/reactions.json`、材质纹理、音效）；一个名为「熔岩矿洞逃生」的可玩关卡；以及一份**Demo 反推出的引擎公开 API 需求清单**，逐项标注所属 plan 文档与「已规划 / 需引擎补 API」状态（§3.13）。

明确不在范围内的是：引擎子系统本身的实现（散落于 plan/02–12，本文件只**消费**它们的公开 API，不实现）；法杖 / 法术等 Noita roguelite 构筑玩法（架构 §1.1 明确排除）；联机 / 回放 / 帧级 undo（架构 §6.3，v1 不做）；新的第三方依赖（Demo 不得引入引擎技术栈表以外的 NuGet 包，§2）。

工程哲学遵循 `AGENTS.md §2`：一步到位、无 MVP、无占位实现。Demo 玩法逻辑全部跑在帧循环相位 [1] Game Logic（架构 §3.3），与 sim 同 tick、同 `dt` 推进；**绝不驱动 sim / physics 的额外 step**（架构 §4.4：Demo 若需独立固定步，只能用「最多追 1 步、超出即丢」的小 accumulator，且不外溢到内核）。角色控制器与 Box2D step 解耦（架构 §8.5），保证手感不随刚体负载漂移。

---

## 2. 技术栈与依赖

运行时与语言：**.NET 10 LTS / C# 14**，与全局一致（plan/00 §1）。`demo/PixelEngine.Demo` 是一个可执行项目（`OutputType=Exe`），`Program.cs` 为入口。Demo **不开 `AllowUnsafeBlocks`**（玩法层无需 unsafe；热路径在引擎内），`Nullable` / `ImplicitUsings` / file-scoped namespace 继承自 `Directory.Build.props`（plan/00 §6）。

依赖方向严格单向（plan/00 §5）：`Demo → Hosting → {Editor, Scripting, Rendering, Audio, Physics, World, Serialization, Content, Simulation} → Interop → Core`。Demo 的 `.csproj` **只 `ProjectReference` 到 `PixelEngine.Hosting`**（门面）与 `PixelEngine.Scripting`（`Behaviour`/`Component` 基类与脚本服务接口）；其余子系统能力一律经由 Hosting 暴露的 `EngineContext` 服务接口取得，不直接引用 `PixelEngine.Simulation` 等内部装配（这正是「只用公开 API」的工程强制手段）。CI 用一条依赖方向检查（plan/14）确保 Demo 不出现对引擎内部 assembly 的反向 / 越层引用。

第三方依赖：**零新增**。Demo 复用引擎已选定的 Silk.NET（窗口 / 输入 / GL / AL）、Hexa.NET.ImGui（HUD / 菜单经引擎 GUI 服务）、Roslyn + 可回收 ALC（脚本热重载）、System.Text.Json（内容加载），均通过引擎公开 API 间接使用，Demo 项目自身不直接 `PackageReference` 它们（plan/00 §4）。native 面收敛在引擎侧的 Box2D 唯一依赖（架构 §14.4），Demo 不引入任何 native。

脚本模型：**项目引用 + Roslyn ALC 热重载**（plan/11）。Demo 的所有玩法脚本继承 `Behaviour`，既可作为编译进 Demo 程序集的常规类型由引擎实例化，也可在开发态被引擎的 Roslyn 编译 + 可回收 `AssemblyLoadContext` 热重载（Unity 式迭代，架构 §17.4、plan/00 §4）。编辑器为**内嵌 ImGui**（plan/12），用于关卡可视化编辑与调试叠层；Demo 同时提供脚本化关卡生成（§3.11），两条路径产出同一份场景。

内容资产（Demo 自带，由 `PixelEngine.Content` 在装载期加载，plan/04）：`content/materials.json`、`content/reactions.json`、`content/textures/`（材质纹理）、`content/audio/`（音效）、`content/scenes/lava-mine.scene`（关卡序列化产物，plan/07 场景格式）。所有材质以**稳定字符串键 `Name` 入盘**，运行时数值 id 仅作索引（架构不变式 §8、§11.2）。

---

## 3. 详细设计

### 3.1 启动与宿主（Program.cs）

`Program.cs` 是端到端流程的起点，只用 `PixelEngine.Hosting` 的公开门面：构造 `Engine`（经 `EngineBuilder`/`EngineOptions` 配置内部分辨率、激活半径、目标帧率、内容根目录、GC 模式实测档位等，对应架构 §12），装配全部子系统（相位顺序由 Hosting 固定，架构 §3.3），加载内容包（materials/reactions/纹理/音效，plan/04），加载场景 `content/scenes/lava-mine.scene`（plan/07）或在缺失时回退到 `LevelDirector` 脚本生成（§3.11），随后调用 `Engine.Run()` 进入固定逻辑步长 + 时间膨胀的主循环（架构 §4，绝不追帧）。`Program.cs` 还负责把 Demo 的脚本程序集注册到引擎脚本宿主，使引擎能发现并实例化所有 `Behaviour`（plan/11）。

宿主关闭、异常落盘、命令行参数（如 `--editor` 启动内嵌编辑器、`--scene <path>` 指定关卡、`--no-hot-reload`）也在此处理。整个文件**不出现任何引擎内部类型**，是「只用公开 API」的第一道证据。

### 3.2 脚本与场景模型

Demo 玩法以 `Behaviour`（plan/11）为单元组织。每个 `Behaviour` 经引擎生命周期回调驱动：`OnAttach`/`OnStart`（实体就绪）、`OnUpdate(float dt)`（在相位 [1] Game Logic 每个被执行的 sim tick 调用一次，与 sim 同 `dt`）、`OnGui(IGuiContext gui)`（在 UI 合成相位调用，用于 HUD / 菜单）、`OnDestroy`。`Behaviour` 通过基类暴露的服务句柄访问引擎能力（`Input`、`World`、`Camera`、`Particles`、`Lighting`、`Audio`、`Physics`、`Diagnostics`、`Content`），这些句柄是引擎公开服务接口（§3.13），不是内部类。

场景是一组实体 + 其挂载的 `Behaviour` 及参数。Demo 既支持**编辑器可视化编排**并序列化为 `.scene`（plan/12 + plan/07），也支持 `LevelDirector` 在运行时**脚本化生成**等价场景（§3.11）。热重载：开发态下修改任一 `Behaviour` 源码触发 Roslyn 重编译 + ALC 卸载 / 重载，保留场景实体与世界状态（plan/11、架构 §17.4 的 id 稳定纪律由引擎保证）。

### 3.3 玩家角色（PlayerController）

`PlayerController : Behaviour` 实现**侧视 kinematic AABB 角色**，直接对像素固体场解算，与 Box2D 解耦（架构 §8.5）。它经引擎角色控制器 API（plan/06）创建一个 `ICharacterBody2D`（AABB 尺寸如 6×12 cell），每 tick 流程：读输入意图（左 / 右 / 跳，plan/08 输入服务）→ 施加重力（+y 向下，坐标系 plan/00 §7）→ 计算期望位移 → 调 `body.Move(delta)`，由引擎做 AABB vs 固体像素的 speculative-contact 多次 sub-iteration 推出，返回 `CollisionResult{ IsGrounded, OnWallLeft, OnWallRight, OnCeiling, GroundNormal }`。

跑：地面加速 / 空气控制 / 摩擦减速，速度上限受架构 32px move cap 间接约束（单 tick 位移远小于半 chunk）。跳：`IsGrounded` 时给 -y 冲量，带 coyote-time 与 jump-buffer。蹬墙（wall-jump）：`OnWallLeft/Right` 且非 grounded 时贴墙下滑（限速），按跳给出离墙斜向冲量。**站在沙 / 刚体上**天然成立——因为引擎角色控制器采样的是 CA 权威像素场，而刚体像素每帧被 stamp 回网格（架构不变式 §5、§8.3 双向耦合），powder 的 settled 堆积与 owned-by-body 像素都被当作可站立固体；这一点是「角色 API 必须读到刚体往返像素」的验收依据（§5）。

`PlayerHealth : Behaviour` 采样玩家 AABB 覆盖 cell 的材质（经 `World.GetCell`，plan/11 世界脚本接口）：接触 lava / fire / acid 扣血并喷血粒子（§3.8），死亡触发在出生点重生（清除附近危险物质或瞬移）。受击 / 落地 / 跳跃经 `Audio.PlayOneShot` 播放音效（§3.10）。

### 3.4 相机（CameraFollow）

`CameraFollow : Behaviour` 用引擎相机 API（plan/08）让 `Camera` 平滑跟随玩家：`Camera.Position` 向玩家位置做带阻尼插值（lookahead 朝移动方向偏移），夹在关卡边界内，支持缩放（滚轮，受 HUD 状态影响）。相机平移在 sim 降频（架构 §4.2 30Hz 模式）下走整图偏移而非像素插值（由引擎渲染相位处理，Demo 只设目标位置）。`Camera.WorldToScreen`/`ScreenToWorld` 供材质笔刷把鼠标屏幕坐标映射到世界 cell（§3.5）。

### 3.5 材质笔刷与世界交互（MaterialBrush / ExplosiveTool）

`MaterialBrush : Behaviour` 实现「挖 / 放材质」沙盒玩法，全部经**世界脚本接口**（plan/11）写 cell：左键放置当前选中材质、右键擦除（写 `empty`）、滚轮调笔刷半径、数字键 `1`–`0` 切换当前材质（sand/water/oil/lava/fire/stone/wood/acid/ice/metal 等，经 `Content.GetMaterialId(name)` 取运行时 id，plan/04）。写入用 `World.SetCell(x,y,id)` / `World.FillCircle(center,radius,id)` 批量接口，引擎负责标脏 chunk 并唤醒 CA（架构 §5.4 dirty-rect、§5.5 KeepAlive，Demo 不感知）。鼠标坐标经 `Camera.ScreenToWorld`（§3.4）。

`ExplosiveTool : Behaviour`（中键 / 专用键）调引擎爆炸 API `World.Explode(center, radius, force)`（plan/05 cell→particle 抛射 + 对邻近刚体施加冲量），把范围内 cell 清空并抛为带速度的自由粒子（火花 / 碎屑，§3.8），同时震动相机、播放 explosion 音效（§3.10）。爆炸是 Demo 触发、引擎在相位 [7] Cell→Particle 执行（架构 §3.3、§7.6），Demo 只入队请求。

### 3.6 材质集与反应内容（materials.json / reactions.json）

`content/materials.json` 定义**完整材质集**（格式见 plan/04 的 `MaterialDef`，架构 §7.3），每项带稳定 `Name`、`CellType`、`Density`、`Dispersion`、`Flammability`/`FireHp`、相变阈值与目标（`MeltPoint/MeltTarget`、`FreezePoint/FreezeTarget`、`BoilPoint/BoilTarget`）、`HeatConduct`/`HeatCapacity`、`PropertyFlags`（含 emissive、tag 归属）、纹理 id 与基色、`AudioCues`（§3.10）。材质清单（≥ 任务要求的 12 种，另补足反应链所需中间产物）：

`empty`（Empty）；`sand`、`dirt`、`ash`（Powder，ash 为木 / 油燃尽残渣）；`water`、`oil`（Liquid，oil 密度 < water 故浮于水，且 `[burnable_fast]`）、`acid`（Liquid，`[acid]`）、`lava`（Liquid，emissive + 高温，`FreezeTarget=stone`）、`molten_metal`（Liquid，emissive + 高温，`FreezeTarget=metal`）；`steam`、`smoke`、`acid_gas`（Gas，带 lifetime 上升扩散）；`fire`（Fire/Energy，emissive + lifetime + 概率传播）；`stone`、`wood`（`[corrodible]`+`[burnable]`+`FireHp`）、`ice`（`MeltTarget=water`）、`metal`（高 `HeatConduct`，`MeltTarget=molten_metal`，慢 `[corrodible]`）、`glass`（sand 受热相变产物）。tag（`[acid]`/`[corrodible]`/`[fire]`/`[burnable]`/`[burnable_fast]`/`[cold_static]`/`[molten]` 等）在加载期展开为具体材质对（架构 §7.4），Demo 内容侧只写 tag，运行时零字符串开销。

`content/reactions.json` 定义**反应内容**（格式见 plan/04 的 `Reaction`，`A+B→C+D @概率`，架构 §7.4），覆盖任务要求的全部反应类型：

熔岩遇水成石 `[molten]/lava + water → stone + steam @80`；熔岩点燃木 `lava + wood → lava + fire @60`；火烧木传播 `[fire] + [burnable]/wood → fire + fire @40`；油速燃 `[fire] + [burnable_fast]/oil → fire + fire @75`；水灭火 `water + [fire] → steam + smoke @90`（水→蒸汽、火→烟）；酸腐蚀 `[acid] + [corrodible] → acid_gas + acid @50`（对 metal 降为 @30 表现更耐蚀）；蒸汽冷凝 `[steam] + [cold_static]/stone → water + stone @3`；熔融金属遇水凝固 `molten_metal + water → metal + steam @60`。

**纯温度相变不进反应表**（架构 §7.4），由 `MaterialDef` 阈值 + 温度场（plan/04 §温度场）驱动：冰融化 `ice --热--> water`（受 lava/fire 邻近升温）；水沸腾 `water → steam`、水结冰 `water → ice`；熔岩冷却 `lava → stone`；金属熔化 `metal → molten_metal` 与回凝 `molten_metal → metal`；沙烤成玻璃 `sand --高温--> glass`。这些与 §3.7 的火 / 熔岩共同产生 emergent 行为（架构 §1.2），不脚本硬编码个例。

### 3.7 刚体演示（可破坏木结构 → Box2D 刚体）

关卡在熔岩湖上架设**木质栈桥 / 脚手架**（wood 固体像素）。玩家用笔刷 / 爆炸 / 火（§3.5–3.6、§3.6 反应）挖断结构后，与锚定地形脱离的连通固体块由引擎自动转为 Box2D 复合刚体（架构 §8.2：CCL→marching squares→Douglas-Peucker→PolyPartition `radius=0`→`b2_dynamicBody`，plan/06）。掉落的刚体可被玩家推动、被其它刚体砸、被继续挖 / 烧 / 酸蚀而再次破碎（架构 §8.4 破坏重建 + 速度转移）。

Demo 侧无需实现刚体逻辑——这正是反推点：刚体的产生 / 同步 / 破坏全在引擎相位 [8]（架构 §3.3），Demo 仅提供可破坏的 wood 几何、并依赖角色控制器把刚体像素当地形踩（§3.3，验证双向耦合 §8.3）。玩家「推」刚体的交互需要引擎暴露「kinematic 角色接触 → 对 dynamic 刚体施加冲量」的 API（`Physics.ApplyImpulseAtContact` 或角色控制器内建推力），该能力的 API 归属见 §3.13（标注状态）。金属梁（metal）靠近熔岩会熔化（§3.6 温度相变）→ 失去支撑 → 结构坍塌成刚体，串联演示「温度→相变→连通性变化→刚体」。

### 3.8 粒子（火花 / 血 / 碎屑 / 爆炸抛射）

经引擎自由粒子 API（plan/05，架构 §7.6）产出三类效果：火花（熔岩 / 火 / 金属撞击迸发的发光短命粒子，参与 emissive → bloom，§3.9）；血（`PlayerHealth` 受击喷射的红色粒子，落定后可沉积为短暂血迹 cell）；碎屑（挖掘 / 刚体破碎 / 爆炸抛出的材质碎块，弹道飞行后按密度沉积回网格，架构 §7.6 cell↔particle handshake）。Demo 调 `Particles.Emit(origin, count, velocityDistribution, materialId, colorVariant, life)` 或 `Particles.Burst(...)` 入队发射；爆炸抛射走 §3.5 的 `World.Explode`（引擎在相位 [7] 把 cell 读出→清网格→取池粒子设速）。所有粒子受引擎强制 max-lifetime 约束（架构 §7.6、§19 R13），Demo 不管理生命周期。

### 3.9 光照（emissive + fog-of-war）

光照走 Noita 式管线（架构 §9.4，plan/08）。emissive 自动：lava / molten_metal / fire / 发光火花的 `PropertyFlags` 标记 emissive，引擎渲染相位据材质 + 温度 glow 生成 additive emissive buffer 并做 bloom（架构 §9.3、§9.4），Demo 只需在内容里标对材质，无需脚本。fog-of-war：矿洞默认黑暗，引擎以 fog-of-war reveal 在光源与玩家周围 punch 洞（架构 §9.4）；Demo 经 `Lighting.RevealAround(playerPos, radius)` 与按需 `Lighting.AddPointLight(pos, radius, color, intensity)`（如出口门口的指示光、玩家手持光源）驱动。光照质量是架构 §4.3 二级降级目标，由引擎自适应，Demo 不干预。

### 3.10 音频（材质化 impact/fire/splash/explosion + 资产清单）

音频走事件驱动材质化管线（架构 §10，plan/10）：sim 把粗粒度音频事件写入 Core 事件总线，音频子系统消费并按材质 `AudioCues` + 世界坐标做 positional 播放，**不进 sim 热循环**（架构 §10.2、§10.3）。Demo 的职责是（a）在 `materials.json` 为各材质配 `AudioCues`（样本集 + 音量 / 音高 / 冷却）；（b）提供音效资产；（c）对玩法专属一次性音效（跳 / 落地 / 受击 / UI / 通关）调 `Audio.PlayOneShot(clip, worldPos?)`。引擎强制同类事件限频去重（架构 §10.2），Demo 不感知。

音效资产清单（`content/audio/`）：`impact_sand.wav`、`impact_stone.wav`、`impact_wood.wav`、`impact_metal.wav`（材质化 impact）；`splash_water.wav`、`splash_acid.wav`（splash）；`fire_crackle_loop.wav`、`lava_bubble_loop.wav`（区域 ambient）；`sizzle_lava_water.wav`、`acid_corrode.wav`（反应音）；`explosion.wav`、`shatter_wood.wav`、`shatter_glass.wav`（爆炸 / 刚体破碎）；`player_jump.wav`、`player_land.wav`、`player_hurt.wav`、`footstep_stone.wav`（玩家）；`ui_click.wav`、`goal_reached.wav`（UI / 通关）。

### 3.11 关卡布局（LevelDirector：熔岩矿洞逃生）

`LevelDirector : Behaviour` 用脚本化生成一个完整可玩关卡（也可由编辑器编排并序列化为 `.scene`，plan/12 + plan/07，两路等价）。主题「熔岩矿洞逃生」：玩家从左侧 stone 洞窟出生，须穿越熔岩湖上的可破坏 wood 栈桥抵达右侧 `GoalTrigger` 出口门。布局要素（用 `World.FillRect/FillCircle/Stamp` 与材质源放置，plan/11）：

底部中央 lava 湖（emissive 主光源，§3.9）；左上 water 龙头与顶部 sand 漏斗，由 `MaterialEmitter` 每 tick 注入（玩家可把水引到熔岩上现造 stone 桥——熔岩遇水成石 §3.6——作为过关解法之一）；横跨熔岩的 wood 栈桥与 metal 梁（可破坏→刚体 §3.7，metal 近熔岩会熔化坍塌）；侧道入口的 acid 池（腐蚀 stone，§3.6）；近冷区的 ice 结晶（受热融化）；oil 池贴近 wood（火灾隐患，演示火传播链）；可推动的 metal 箱（刚体）。关卡边界用不可破坏 stone（锚定质量，架构 §8.2 CCL 锚点）。生成使用引擎确定性 RNG（plan/02，可种子化）以保证可复现布局。

`MaterialEmitter : Behaviour`（参数：材质名 + 速率 + 喷口矩形）实现水龙头 / 沙漏斗 / 熔岩涌泉，每 tick 经 `World.SetCell` 在喷口注入物质。`GoalTrigger : Behaviour`（参数：触发矩形）检测玩家 AABB 进入即判定通关，播 `goal_reached.wav` 并弹出胜利菜单（§3.12）。

### 3.12 HUD / 菜单

`DemoHud : Behaviour.OnGui` 用引擎 GUI 服务（即时模式，基于内嵌 ImGui，plan/11 `IGuiContext` + plan/12）绘制游戏内 HUD：当前选中材质（名 + 色块 + 数字键提示）、笔刷半径、玩家状态（生命、是否着火 / 接触危险）、操作提示（移动 / 跳 / 蹬墙 / 挖放 / 爆炸 / 切材质 / 暂停）、性能行（fps / sim 频率 / 活跃 chunk / 粒子 / 刚体数，取自引擎诊断服务 `Diagnostics`，架构 §17.1、plan/02）。`PauseMenu : Behaviour.OnGui`（Esc 切换）提供继续 / 重开关卡 / 切换调试叠层（dirty-rect / parity / owned-by-body 等，架构 §17.2，引擎提供开关 API）/ 打开内嵌编辑器（plan/12）/ 退出。HUD / 菜单**不直接调 Hexa.NET.ImGui**，而是经引擎 `IGuiContext` 公开 API，保持「只用公开 API」。

### 3.13 引擎公开 API 需求清单（反推 API 完整性）

下表逐项列出 Demo 必需的引擎公开 API、所属 plan 文档与状态。**「需引擎补 API」表示该能力对应的公开 API 在现有 plan 范围内尚无明确归属，须补引擎而非在 Demo 内变通**（`AGENTS.md §0`）；落地时若发现缺口未被相应 plan 接纳，按 `AGENTS.md §2` 标 `- [!] 阻塞` 上报。

| 能力（Demo 需求） | 期望公开 API（签名意向） | 所属 plan | 状态 |
|---|---|---|---|
| 创建引擎 / 配置 / 运行主循环 / 内容包加载 | `EngineBuilder`、`EngineOptions`、`Engine.Run()`、`Engine.Shutdown()`、`Engine.LoadContentPackage()` | Hosting（plan/00 §5） | 部分实现：Demo 可经 Hosting 构造 Engine、加载已存在的 materials/reactions 内容包并 headless 冒烟运行；非 headless 路径已调用 `Engine.AttachWindowRuntime()` 接入窗口、输入与 Rendering 相位；仍缺 save directory/world 物化与真实窗口端到端验收 |
| 子系统访问门面 | `EngineContext`（暴露 `World/Camera/Input/Particles/Lighting/Audio/Physics/Content/Diagnostics/Gui`） | Hosting（plan/00 §5） | 部分实现：`EngineContext` 可注册/查询 typed service 与 role availability，脚本后端已能自动装配 World/Camera/Input/Particles/Lighting/Audio/Content/Time/Event；窗口态已有 `AttachWindowRuntime()` 组合 RenderWindow/Input/Rendering；仍缺 GUI 与 save directory/world 组合门面 |
| 注册 Demo 脚本程序集 / 实例化 Behaviour | `Engine.RegisterScriptAssembly(...)`、`Behaviour` 生命周期（`OnStart/OnUpdate/OnGui/OnDestroy`） | plan/11 / Hosting | 部分实现：`Engine.RegisterScriptAssembly(...)`、Hosting registry、procedural/scene file Behaviour 物化、`Engine.AttachScripting(...)`、`AttachScriptingFromServices()` 与 `Behaviour.OnGui`/`IGuiContext` 调度已落地；仍缺真实窗口端到端 GUI 验收 |
| 脚本服务句柄注入 | `Behaviour.World/Input/Camera/...` 属性 | plan/11 | 已规划（plan/11 世界脚本接口） |
| 场景加载 / 保存 | `Engine.LoadScene(name)`、`SceneSourceKind.SceneFile`、`.scene` 格式 | plan/07（序列化）+ plan/12（编辑器编排） | 部分实现：Hosting 可切换已注册场景、区分 `.scene` 文件来源，并从 `.scene` 物化脚本实体/Behaviour 参数；`AttachCurrentSceneWorld` 已可显式从 save directory 或 `.scene InitialSaveDirectory` 装配 live World/Simulation/粒子后端；仍缺刚体快照恢复、world seed/game time 完整恢复与编辑器导出格式 |
| 输入查询 | `IInput`（`IsDown/Pressed/Released(Key)`、`MousePosition`、`MouseButton`、`Wheel`） | plan/08（Silk.NET 输入）/ Hosting | 部分实现：Scripting 已有 `ScriptInputApi`、完整 Demo 所需键位/鼠标枚举、键鼠边沿、滚轮与轴快照；Hosting 已有 `SilkInputPhaseDriver` 采集窗口键鼠并支持通道门控，且已被 `AttachWindowRuntime()` 装配；仍缺真实窗口输入验收 |
| 读写 cell（笔刷 / 关卡生成 / 危险采样） | `IWorld.GetCell/SetCell`、`FillRect/FillCircle/Stamp` | plan/11（世界脚本接口） | 已规划 |
| 材质按名取 id | `EngineContentPackage.ResolveMaterial/TryResolveMaterial(...)`、`IMaterialQuery.Resolve/TryResolve(...)` | plan/04（Content）+ Hosting/plan/11 | 部分实现：Hosting 内容包门面与脚本材质查询接口已可用，public API 不泄漏 Content/Simulation 实现类型；Demo materials/reactions/textures/audio 已就位 |
| 角色控制器（kinematic AABB vs 像素） | `IPhysics.CreateCharacterBody(aabb)`、`body.Move(delta) → CollisionResult{Grounded,OnWall,OnCeiling,Normal}` | plan/06（§8.5 角色控制器） | 部分实现：Scripting `ICharacterController.Create/SetPosition/Move/GetState` 已接入真实 Physics 像素 AABB 控制器，返回位置、左右墙、天花板、实际位移与地面法线；尚未封装为 `ICharacterBody2D` 对象式 API |
| 角色站在刚体 / 沙上 | 角色控制器采样 CA 权威场（含 owned-by-body stamp 像素） | plan/06 + 架构 §8.3 | 已规划（依赖双向耦合不变式） |
| 角色推动 dynamic 刚体 | `IPhysics.ApplyImpulseAtContact(...)` 或角色控制器内建推力 | plan/06 | 需引擎补 API（kinematic→dynamic 推力交互） |
| 爆炸（清 cell + 抛粒子 + 推刚体） | `IWorld.Explode(center,radius,force)` | plan/05 + plan/06 | 部分实现：Scripting 已有 `IWorldEffects.Explode`，在相位安全窗口触发 cell→particle 抛射、爆炸音频事件、`RigidOwned` damage 通知与邻近刚体径向冲量；仍缺真实窗口爆破可玩验收 |
| 自由粒子发射 | `IParticles.Emit/Burst(origin,count,velDist,materialId,life)` | plan/05 | 部分实现：脚本 `IParticleSpawner.Spawn/Burst` 已经延迟到粒子安全相位并接入真实 `ParticleSystem`，爆炸抛射复合入口已通过 `IWorldEffects.Explode` 接入；仍缺更丰富的速度分布 Emit API |
| 相机控制 | `ICamera.Position/Zoom`、`WorldToScreen/ScreenToWorld`、`Follow(target,damping)` | plan/08（相机） | 部分实现：Scripting 已有 `ScriptCameraApi`，支持中心、缩放、视口、屏幕/世界坐标转换与 `CameraSnapshot`；Hosting 已有 `ScriptCameraSynchronizer` 同步 Rendering `CameraState` 与 World residency，`RenderPhaseDriver` 已消费该快照构建 render buffer；仍缺统一 Transform 后的 `Follow(Entity)` 与真实窗口画面验收 |
| 点光源 / fog-of-war reveal | `ILighting.AddPointLight(...)`、`RevealAround(pos,radius)` | plan/08（光照） | 部分实现：Scripting 已有 `ScriptLightingApi`，Hosting 已有 `ScriptLightingSynchronizer` 同步 Rendering `LightSource` 与 `FogOfWarBuffer`，`RenderPhaseDriver` 已把 fog-of-war 与点光源传入 `RenderPipeline`，点光源已合成进 visibility mask；仍缺真实窗口光照验收 |
| 一次性音效播放 | `IAudio.PlayOneShot(clip, worldPos?)` | plan/10 | 部分实现：Scripting `IAudioApi` 已支持 `PlayOneShot` 与 `PlayAt`，`ScriptAudioApi` 可桥接真实 `AudioSystem`；Hosting 已能从 `content/audio` 预加载 Demo wav clip 并注入脚本上下文，`audio/cues.json` 已把材质事件 cue 句柄映射到已加载 clip buffer，`World.Explode` 可触发 explosion 音频事件；仍缺窗口态音频验收 |
| 材质化音效配置 | `MaterialDef.AudioCues`（materials.json 字段） | plan/04 + plan/10 | 已规划 |
| 即时模式 HUD / 菜单 | `IGuiContext`（窗口 / 文本 / 按钮 / 色块），`Behaviour.OnGui` | plan/11 / plan/12 | 部分实现：脚本公开 `IGuiContext`、`Behaviour.OnGui` 调度链、Hosting/Editor ImGui host、DemoHud 与 PauseMenu 已落地；PauseMenu 已可继续/暂停、请求退出、切换调试叠层、请求打开已接入的完整 Editor dockspace，并对未接入的重开关卡后端返回明确失败 |
| 性能 / 状态诊断读取 | `IDiagnostics`（fps、sim 频率、活跃 chunk / 粒子 / 刚体计数） | plan/02（诊断） | 部分实现：脚本 `IDiagnosticsApi.Capture()` 已从 Hosting `EngineCounters` 暴露 FPS、SimHz、活跃/常驻 chunk、自由粒子与刚体数；仍缺更细分相位耗时、active/resident chunk 生产侧完整更新与窗口态 HUD 验收 |
| 调试叠层开关 | `IDiagnostics.ToggleOverlay(OverlayKind)` | plan/12（调试叠层）+ 架构 §17.2 | 部分实现：脚本 `IDiagnosticsApi` 已可切换 dirty rect / chunk parity / KeepAlive / cell parity / temperature / owned body / particle / CCL 叠层，RenderPhaseDriver 已把 DebugOverlayController 接入逐 cell 着色与 overlay command；仍缺真实窗口验收 |
| 确定性 RNG（关卡生成） | `IRandom`（可种子化） | plan/02（RNG） | 已规划 |

API 缺口登记结果：仍需由引擎公开 API 接纳的阻塞项为：Hosting 的 Physics 刚体快照恢复与 world seed/game time 完整恢复；plan/06 的 kinematic 角色推动 dynamic 刚体公开交互；脚本可见重开关卡安全后端；plan/08 的脚本点光源彩色照明窗口态验收。打开完整 Editor dockspace 的脚本控制 API 已落地，但要求进程以 `--editor` 启动并已接入窗口/Rendering。上述缺口均在本表中保持「需引擎补 API」或「部分实现但阻塞」状态，Demo 不允许绕过公开 API 直接引用内部实现。

---

## 4. 实现清单

工程与启动
- [x] 建 `demo/PixelEngine.Demo/PixelEngine.Demo.csproj`（`Exe`，仅 `ProjectReference` 到 `PixelEngine.Hosting` 与 `PixelEngine.Scripting`，继承 `Directory.Build.props`，无新 NuGet）。〔plan/00 §5〕
- [!] `Program.cs`：已用 `EngineBuilder`/`EngineProject` 构造 Engine，支持 `--editor/--headless/--scene/--content/--ticks/--no-hot-reload/--log-dir`；已通过 `Engine.LoadContentPackage()` 加载 Demo materials/reactions 内容包，已区分 save directory 与 `.scene` 来源，默认 `content/scenes/lava-mine.scene` 可加载并物化 `LevelDirector`；已支持 `.scene` 与 procedural 脚本实体/Behaviour 物化，已用 `Engine.AttachResidentSimulationWorld(...)` + `Engine.AttachScriptingFromServices()` 接入 headless resident Simulation/Scripting 后端并跑通 2 tick 冒烟；save directory 与 `.scene InitialSaveDirectory` 场景可经 `Engine.AttachCurrentSceneWorld(...)` 显式装配 live World/Simulation/粒子后端；非 headless 路径已调用 `Engine.AttachWindowRuntime()` 接入窗口、输入与 Rendering 相位；阻塞：未完成真实窗口可玩关卡验收。〔Hosting；§3.1〕
- [x] CI 依赖方向断言：Demo 无对引擎内部 assembly 的越层 / 反向引用。〔plan/14；§2〕

玩家与相机
- [!] `PlayerController : Behaviour`：源码已落地，经 `ICharacterController.Create/SetPosition/Move/GetState` 创建、传送并移动 AABB，实现跑 / 跳 / 贴墙滑落 / 蹬墙、重力、coyote-time 与 jump-buffer；headless 路径已能由 Hosting 自动注入脚本后端并驱动场景 Behaviour，`AttachWindowRuntime()` 已装配 `SilkInputPhaseDriver` 将窗口键盘快照写入 `ScriptInputApi`；阻塞：缺少真实窗口输入与可玩控制验收。〔plan/06、plan/08 输入；§3.3〕
- [!] `PlayerHealth : Behaviour`：源码已落地，按玩家 AABB 采样 `lava/fire/acid`，扣血、喷粒子、触发受击音效并死亡重生；headless 路径已能由 Hosting 自动驱动，并已通过 `AttachAudioFromContentAsync()` 预加载 Demo wav clip、注入脚本音频 API；阻塞：缺少窗口态输入/渲染运行验收与受击场景端到端验证。〔plan/11、plan/05、plan/10；§3.3〕
- [!] `CameraFollow : Behaviour`：源码已落地，可跟随同实体 `PlayerController`，支持阻尼、lookahead、边界夹取与缩放；headless 路径已有默认脚本相机 API，Hosting 已有 `ScriptCameraSynchronizer` 将脚本相机快照同步为 Rendering `CameraState` 并可更新 World residency 相机，`RenderPhaseDriver` 已用该快照构建并提交 render buffer；阻塞：缺少统一 Transform 后的 `Follow(Entity)` 与真实窗口画面跟随验收。〔plan/08；§3.4〕

世界交互
- [!] `MaterialBrush : Behaviour`：源码已落地，支持左键放置、右键擦除、滚轮调半径、数字键 `1`–`0` 切材质，经 `Cells.Paint` 写入，`Materials.Resolve` 取 id，`Camera.ScreenToWorld` 映射鼠标；headless 路径已能由 Hosting 自动注入 cell/material/camera/input 后端，`AttachWindowRuntime()` 已装配窗口鼠标/滚轮快照、脚本相机同步与 Rendering 提交；阻塞：缺少窗口态笔刷写入与画面反馈验收。〔plan/11、plan/04、plan/08；§3.5〕
- [!] `ExplosiveTool : Behaviour`：源码已落地，中键按鼠标世界坐标调用公开 `Context.World.Explode`，并请求 fog reveal 与点光反馈；Scripting/Simulation/Physics 测试已覆盖爆炸复合入口在安全相位触发 cell 抛射、explosion 音频事件、`RigidOwned` damage 通知与邻近刚体径向冲量；阻塞：缺少真实窗口中键爆破、画面反馈、音频定位与刚体推动端到端验收。〔plan/05、plan/06、plan/10;§3.5〕

内容
- [x] `content/materials.json`：完整材质集（empty/sand/dirt/ash/water/oil/acid/lava/molten_metal/steam/smoke/acid_gas/fire/stone/wood/ice/metal/glass），含稳定 `Name`、CellType、密度 / 流散 / 可燃 / 相变阈值 / 温度参数 / PropertyFlags(emissive 等) / 纹理 / `AudioCues`，tag 归属。〔plan/04 格式；§3.6〕
- [x] `content/reactions.json`：熔岩遇水成石、熔岩点燃木、火烧木传播、油速燃、水灭火、酸腐蚀、蒸汽冷凝、熔融金属遇水凝固，用 tag 书写；Hosting 已在装配 Simulation world 时接入 `ReactionEngine`、反应副作用与 `BurningCellSystem`，并用 headless 集成测试验证已加载 `ReactionTable` 会进入 CA 主循环。〔plan/04 格式；§3.6〕
- [x] 温度相变内容校核：冰融化 / 水沸 / 水冻 / 熔岩冷却 / 金属熔化回凝 / 沙烤玻璃 经 `MaterialDef` 阈值（不进反应表）。〔plan/04 温度场、架构 §7.4;§3.6〕
- [x] `content/textures/`：各材质纹理（按世界坐标采样，颜色不入 cell，架构不变式 §7）。〔plan/04;§3.6〕
- [x] `content/audio/`：§3.10 音效资产清单全部就位并被 `AudioCues`/`PlayOneShot` 引用。〔plan/10;§3.10〕

刚体 / 粒子 / 光照 / 音频（Demo 侧消费）
- [!] 木 / 金属可破坏结构布置：`LevelDirector` 已铺设木桥与金属梁，并经 `IRigidBodyApi.CreateFromRegion` 在 headless 真实 Hosting/Scripting/Physics 路径注册 6 个动态刚体；集成测试已切断其中一段木桥并验证 Physics 销毁父刚体、创建 2 个子刚体；阻塞：缺少窗口态可推 / 砸 / CA 挖断后再破坏端到端验收。〔plan/06;§3.7〕
- [!] 火花 / 血 / 碎屑发射：`PlayerHealth` 已用 `Particles.Burst` 喷血，`MaterialEmitter` 已用 `Particles.Spawn` 做喷口粒子，`SparkEmitter` 已接入 lava 区域 fire 火花，刚体小碎片可由 Physics damage 重建写入自由粒子；`World.Explode` 已可把 cell 抛射为粒子并推动邻近刚体；阻塞：缺少真实窗口爆炸粒子、刚体推动与无粒子泄漏端到端验收。〔plan/05;§3.8〕
- [!] emissive 材质标注正确（lava/molten_metal/fire/火花），Scripting 已有 `Lighting.RevealAround` + `AddPointLight` 请求 API，Hosting 已有 `ScriptLightingSynchronizer` 将脚本请求同步为 Rendering `LightSource` 与 `FogOfWarBuffer`，`RenderPhaseDriver` 已把 fog-of-war 与点光源传入 `RenderPipeline` 并 stamp 自由粒子到 emissive buffer；`RenderPipeline` 已把点光源合成进 visibility mask；阻塞：缺少真实窗口光照验收。〔plan/08;§3.9〕
- [!] `materials.json` 的 `AudioCues` 已覆盖 impact/fire/splash/ambient/explosion/shatter，玩法脚本可经 `Audio.PlayOneShot`/`PlayAt` 请求音效；Hosting 已能从 `content/audio` 预加载 19 个 wav clip 并注入脚本上下文，`audio/cues.json` 已把材质事件 cue 句柄接到 `MaterialAudioPlayer`/已加载 clip buffer，粒子事件也接入 `Context.Events`，`World.Explode` 已可触发 explosion cue；阻塞：缺少窗口态定位音频/ambient/sizzle/corrosion 满屏限频验收。〔plan/04、plan/10;§3.10〕

关卡与 UI
- [!] `LevelDirector : Behaviour`：源码已落地，脚本生成「熔岩矿洞逃生」基础布局并装配玩家、相机、笔刷、喷口和目标触发器；Hosting procedural scene source 已可按入口 Behaviour 名自动物化 `LevelDirector` 到脚本场景，且 headless resident world 可经 `AttachScriptingFromServices()` 自动驱动；save directory 与 `.scene InitialSaveDirectory` 可显式装配 live World/Simulation/粒子后端，窗口态已可装配输入与 Rendering 相位；阻塞：真实窗口可玩关卡验收仍未完成。〔plan/11、plan/02;§3.11〕
- [!] `MaterialEmitter : Behaviour`（材质 + 速率 + 喷口）：源码已落地，支持周期性 cell 注入、粒子、音频和点光源请求；headless 路径已能由 Hosting 自动驱动脚本场景并注入已加载脚本音频 API，fog-of-war 与点光源请求已可进入 Rendering 管线；阻塞：缺少真实窗口喷口画面与音频触发验收。〔plan/11;§3.11〕
- [!] `GoalTrigger : Behaviour`：源码已落地，玩家进入触发区后触发通关状态、音效、粒子与光照反馈；headless 路径已能由 Hosting 自动驱动脚本场景并注入已加载脚本音频 API，fog-of-war 与点光源请求已可进入 Rendering 管线；阻塞：缺少胜利菜单/GUI 服务、窗口态通关画面与音效触发验收。〔plan/11、plan/10;§3.11〕
- [x] `content/scenes/lava-mine.scene`：已按 `.scene` 文档格式序列化 `LevelDirector` 入口与关卡参数，默认启动路径可加载并物化为等价脚本场景。〔plan/12、plan/07;§3.2、§3.11〕
- [!] `DemoHud : Behaviour.OnGui`：源码已落地，经 `IGuiContext` 显示玩家生命、当前材质色块、笔刷半径、爆破次数、目标状态与 FPS/SimHz/Frame/活跃 chunk/自由粒子/刚体数；阻塞：缺真实窗口 HUD 验收。〔plan/11、plan/12、plan/02;§3.12〕
- [!] `PauseMenu : Behaviour.OnGui`：源码已落地，经 `IGuiContext` 绘制暂停窗口，Esc 切换暂停/继续，`Context.Runtime` 请求退出、请求打开已接入的完整 Editor dockspace、对重开关卡返回明确未接入原因，`Context.Diagnostics` 切换 dirty rect / parity / KeepAlive / temperature / owned body / particle / CCL 调试叠层；阻塞：脚本层尚缺可完整重建 world/scene/script runtime 的重开关卡后端。〔plan/12、架构 §17.2;§3.12〕

API 缺口登记
- [x] 把 §3.13 表中所有「需引擎补 API」项作为引擎 API 缺口逐条登记并上报，确认被对应 plan（Hosting/06/05/08/10/11/12/07）接纳；未接纳者标 `- [!] 阻塞`。〔AGENTS.md §0、§2;§3.13〕

---

## 5. 验收标准

- [ ] `dotnet run --project demo/PixelEngine.Demo -c Release` 开窗进入关卡，端到端跑通「引擎装配→内容加载→场景加载→主循环」，无内部类引用（编译期由 csproj 引用范围 + CI 断言保证）。〔§3.1、§2〕
- [ ] 玩家可跑 / 跳 / 蹬墙，**站在 settled 沙堆与掉落的木 / 金属刚体上不穿不陷**（验证角色控制器读到刚体往返像素，架构 §8.3 双向耦合）。〔§3.3〕
- [ ] 相机平滑跟随玩家、夹在关卡边界、滚轮缩放生效；sim 降频时画面仍流畅（引擎整图偏移）。〔§3.4〕
- [ ] 数字键切材质、左键放 / 右键擦 / 滚轮调半径在正确世界坐标写入 cell 并被 CA 接管（沙堆休止角、水找平、油浮于水、气体上升）。〔§3.5、§3.6〕
- [!] 反应可观测：headless 路径已验证 Hosting 会把已加载 `ReactionTable` 接入 CA 主循环；阻塞：仍缺真实窗口中水灭火（火→烟、水→蒸汽）、火沿木 / 油传播、熔岩遇水成石冒蒸汽、酸腐蚀 stone/wood/metal、蒸汽冷凝回水、熔融金属遇水凝固的可视化验收。〔§3.6〕
- [ ] 温度相变可观测：lava / fire 附近 ice 融化、water 沸腾成 steam、lava 冷却成 stone、metal 近熔岩熔化、sand 烤成 glass。〔§3.6〕
- [ ] 玩家挖断木栈桥 → 连通块掉落为 Box2D 刚体，可被推动、被砸、被继续挖 / 烧 / 酸蚀而再破碎；金属梁近熔岩熔化致结构坍塌。〔§3.7〕
- [ ] 火花 / 血 / 碎屑粒子按弹道飞行、碎屑落定回沉积为 cell、发光火花产生 bloom；爆炸抛射 cell 成粒子并推动邻近刚体；无粒子泄漏。〔§3.8、§3.9〕
- [ ] 矿洞默认黑暗，熔岩 / 火 / 熔融金属 emissive 发光，fog-of-war 在玩家与光源周围揭示；光照过载时引擎自动降级不卡顿。〔§3.9〕
- [ ] 材质化音效随事件定位播放（沙 / 石 / 木 / 金属 impact、水 / 酸 splash、火 / 熔岩 ambient、熔岩遇水 sizzle、酸腐蚀、爆炸、刚体破碎），玩法音效（跳 / 落 / 受击 / 通关 / UI）正确触发，满屏事件下限频不过载。〔§3.10〕
- [ ] 关卡可从出生点用至少一种解法（引水成石桥 / 坍塌木桥成路）抵达出口触发通关，全程演示全部材质 / 反应 / 刚体 / 粒子 / 光照 / 音频。〔§3.11〕
- [ ] HUD 正确显示当前材质 / 笔刷 / 玩家状态 / 操作提示 / 性能行；暂停菜单可继续 / 重开 / 切叠层 / 开编辑器 / 退出；HUD 经 `IGuiContext` 而非直接 ImGui。〔§3.12〕
- [ ] 开发态修改任一 `Behaviour` 源码触发 Roslyn + ALC 热重载，场景与世界状态保留。〔plan/11、§3.2〕
- [ ] §3.13 全部「需引擎补 API」项已登记、被对应 plan 接纳或标 `- [!] 阻塞`，Demo 内无任何引擎内部类后门。〔§3.13、AGENTS.md §0〕

---

## 6. 依赖关系

前置（必须先完成其公开 API）：plan/02（Core：诊断 / RNG / 事件总线）、plan/03（CA 内核，世界写入语义）、plan/04（材质 / 反应 / 温度 + Content）、plan/05（粒子）、plan/06（角色控制器 + 刚体 + Box2D 桥）、plan/07（场景 / 存档序列化）、plan/08（渲染 / 相机 / 光照 / 输入）、plan/10（音频）、plan/11（脚本系统 Behaviour + 世界脚本接口 + 热重载）、plan/12（编辑器 + GUI 服务 + 调试叠层）。Demo 是这些子系统的集成验证，位列执行顺序末段（plan/README「集成与交付」段，架构 §18 M10）。

被依赖：plan/14（测试 / 基准把 Demo 作为端到端集成与性能场景）、plan/16（性能加固以 Demo 关卡为 profiling 负载）、plan/17（路线图把本文件映射到 M10）。

横向约束：Demo 不得违反架构不变式（`AGENTS.md §1`），尤其帧节奏（不追帧、不驱动额外 sim step，架构 §4.4）与「只用公开 API」。

备注（须上报）：plan/README 与 plan/00 §5 定义了 `PixelEngine.Hosting` 工程，但**未在 plan 清单里给出专属的 Hosting 计划文档**；Demo 的启动与主循环强依赖 Hosting 公开门面（`Engine`/`EngineBuilder`/`EngineContext`/场景加载）。此为 §3.13 多项「需引擎补 API」的根因，建议补一份 Hosting plan 或将其并入 plan/11，按 `AGENTS.md §5`「先改计划再改代码」处理。

---

## 7. 提交节点

- 节点 1：`feat(demo): Demo 工程骨架与宿主启动（Program.cs + Engine 装配 + 内容/场景加载）`〔§3.1、§4 工程与启动〕
- 节点 2：`feat(demo): 玩家角色控制器与相机跟随（kinematic AABB 跑/跳/蹬墙）`〔§3.3、§3.4〕
- 节点 3：`feat(demo): 材质笔刷与世界交互（挖/放/爆破 + 完整 materials.json/reactions.json 内容）`〔§3.5、§3.6〕
- 节点 4：`feat(demo): 刚体/粒子/光照/音频集成（可破坏木结构、火花血屑、emissive+fog、材质化音效）`〔§3.7–§3.10〕
- 节点 5：`feat(demo): 熔岩矿洞关卡、HUD 与暂停菜单（LevelDirector/场景/UI）`〔§3.11、§3.12〕
- 节点 6：`docs(demo): 引擎公开 API 需求清单落定与缺口上报（反推 API 完整性）`〔§3.13、§4 API 缺口登记〕

每个节点完成即按 `AGENTS.md §6` 用中文 git 提交；提交正文注明对应本文件「实现清单」条目与所引 plan 章节。
