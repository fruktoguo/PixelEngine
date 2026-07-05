# Plan 17 — 路线图、执行顺序与提交节点总表

> 本文件是实际编码的节奏表:把 `plan/01`–`plan/20` 编织进里程碑 M0–M14,给出依赖图、并行关系、退出标准与中文 git 提交节点序列。与架构文档 §18 的 M0–M10 对齐,并把用户新增的脚本系统(plan/11)、编辑器(plan/12)、独立编辑器应用与发行解耦(plan/19,含 GUI 宿主中性化程序集 `PixelEngine.Gui`)、游戏内交互 HTML UI(plan/20,`PixelEngine.UI`)纳入。M13/M14 两个里程碑在 M12 之后续接:M13「编辑器独立化与发行解耦」、M14「玩法深化与交互 UI」。
> 原则:vertical-slice-first(尽早让「沙能下落并渲染上屏」端到端跑通),一步到位、无 MVP、无临时实现(`AGENTS.md`)。

---

## 1. 依赖图(DAG)

```
00 约定/技术栈
        │
01 项目骨架 ──────────────────────────────────────────────┐
        │                                                  │
02 Core(数学/内存/JobSystem/RNG/事件总线/时间/诊断/常量)   │
        │                                                  │
        ├── 03 CA 内核 ──┬── 04 材质/反应/温度              │
        │                ├── 05 粒子/生命周期               │
        │                ├── 07 世界/流式/存档              │
        │                └── 06 物理/碰撞/刚体(+Interop)    │
        │                                                  │
        ├── 08 渲染(GL/纹理流式/合成/光照) ── 09 GPU 计算   │
        │                                                  │
        └── 10 音频                                         │
                                                           │
(Gui) PixelEngine.Gui:GUI 宿主中性化(位于 Rendering 之上、Editor 之下)
        │      ImGui host(HexaImGuiBackend)+IGuiContext 运行时适配+中性 EditorRenderBridge+GuiFontManager(含 CJK)
        │      Editor 依赖 Gui;Hosting→Gui(删对 PixelEngine.Editor 硬引用,改抽象 GUI/相位[10]钩子);玩家 HUD 经 Gui 而非 Editor
        │
18 宿主/主循环编排 ── 11 脚本系统 ── 12 编辑器 UI ──┬── 13 Demo 游戏  │
                                    │              │                 │
                                    ├── 19 独立编辑器壳(apps/PixelEngine.Editor.Shell,在 12 之后)  │
                                    │      EditorShell→{Hosting,Editor,Gui};壳内编辑器内打包面板经子进程调 build-player │
                                    └────┴── 20 交互 HTML UI(src/PixelEngine.UI,在 12/13 之后;UI→{Gui,Rendering,Core}) │
                                                           │
14 测试/基准  ─┐                                           │
16 性能加固    ├── 贯穿全程(cross-cutting,随每个子系统推进)│
15 打包/分发  ─┘ ←───────────────────────────────────────┘
```

要点:`02` 是一切的地基(JobSystem 同时服务 CA checkerboard 与 Box2D task 桥)。`03` 之上,`04/05/06/07` 可并行起步。`08` 渲染可与 `04/05` 并行。`11→12→13` 是「可编程→可编辑→做游戏」链。`12→19` 把编辑器从「Demo 内嵌开关」抬为顶层独立应用 `apps/PixelEngine.Editor.Shell`;`12/13→20` 引入游戏内 HTML 大 UI(`PixelEngine.UI`)。新增中性程序集 `PixelEngine.Gui` 是 plan/19 壳注入、plan/15 玩家包 player-only 审计、plan/20 UI 字体/回退复用三者的**共同前置**(入口门):Editor 依赖 Gui,Hosting 删除对 `PixelEngine.Editor` 的硬 ProjectReference、改暴露抽象 GUI/相位[10]钩子接口由编辑器壳(开发构建)注入。依赖方向定稿:`{Demo(玩家运行时), EditorShell(编辑器顶层)} → Hosting → {Scripting, Rendering, Audio, Physics, World, Serialization, Content, Simulation, UI, Gui} → Interop → Core`;`Hosting→Gui`(不再→Editor),`Demo→Hosting(+可选 UI,不含 Editor)`。`14` 测试与 `16` 性能加固不是末尾里程碑,而是**贯穿每个子系统**的横切线(写子系统即写其测试与基准)。`15` 打包在具备 native 依赖(`06`)后随时可立管线,发行主线在 M13 收敛为 **Windows 优先**(win-x64 主/win-arm64 条件,其余 4 RID CI-optional/dormant,保留矩阵位)。

## 2. 里程碑映射(M0–M14)

| 里程碑 | 主题 | 主要 plan | 交付物 | 退出标准 |
|---|---|---|---|---|
| **M0** | 骨架+垂直切片+帧节奏 | 01、02(时间/诊断/内存)、03(单chunk)、08(窗口/全帧PBO上传) | Silk.NET 开窗;单 chunk SoA 网格;沙/空两材质;单线程原地 bottom-up+parity;BGRA 全帧 PBO 上传;鼠标画沙;固定步长+时间膨胀帧循环;帧计时 | 屏上画沙下落堆休止角,稳定 60fps;过载只降帧不 death spiral |
| **M1** | chunk+dirty-rect | 03、07(residency apply)、14 | 64×64 chunk hash-map;working/current 双缓冲 dirty rect;sleeping chunk;KeepAlive;border ring 雏形;质量守恒性质测试 | 静止区零成本;多 chunk 雪崩跨边界正确传播(测试通过) |
| **M2** | 多线程内核 | 02(JobSystem)、03(checkerboard)、14、16 | 持久线程池+barrier;4-pass checkerboard;32px move cap;false-sharing padding;活跃 chunk 少单线程回退;每核加速曲线实测 | 实测缩放达标;单线程 oracle 比对无边界损坏 |
| **M3** | 材质/液体/气体/反应 | 04、Content、14 | 数据驱动 MaterialDef+紧凑反应列表(JSON+tag展开+name↔id表);liquid/gas/powder 规则;密度位移;接触式火传播;跨界/双输出反应正确性测试;材质纹理上色;热重载(数据) | 水找平/油浮水/气上升/火烧/熔岩遇水成石;改 JSON 顺序不损坏;边界反应不翻倍 |
| **M4** | 粒子+生命周期+合成 | 05、08(粒子stamp+emissive)、14 | free-particle 池;cell↔particle handshake;max-lifetime;爆炸抛射;粒子 render 合成+emissive | 爆炸碎屑划弧、落定回沉积、发光粒子可见,无泄漏 |
| **M5** | 温度场(全场增强) | 04(温度)、16 | 1/4 分辨率 Half 热场;HeatConduct 概率传导;melt/freeze/boil 阈值相变;SIMD stencil;作 §4.3 一级降级 | ICE→WATR→STEAM 链;不回退 60fps;降级生效 |
| **M6** | 像素碰撞+刚体 | 06、Interop、14 | 玩家 kinematic AABB vs 像素;CCL→marchingsquares→DP→PolyPartition(radius=0)→Box2D v3 复合体;Box2D task 桥;两世界 erase/step/inverse-rasterize+不可变 mask 权威;破坏重建+速度转移+碎片下限+节流 | 挖断块掉落成刚体、可旋转、再被毁拆分;沙能堆刚体上、火能烧刚体(双向耦合);无亚像素侵蚀;多核物理生效 |
| **M7** | 光照+后处理+GPU 计算 | 08(光照)、09 | emissive+fog-of-war+bloom+dither+gamma;dirty-rect 子上传;GPU compute bloom/光照;可选 Radiance Cascades;GPU 粒子批绘 | Noita 式观感;渲染 ≤4ms;GPU 路径与 CPU 回退均可用 |
| **M8** | 音频 | 10 | OpenAL 子系统+positional source 池;sim 事件总线;材质化 impact/fire/splash/explosion/ambient;限频去重 | 材质化音效随事件播放、定位正确、不过载 |
| **M9** | 世界流式+存档 | 07、14 | chunk 二进制(RLE+LZ4)+world manifest+版本迁移+id 重映射;流式装卸+border ring+内存上限 LRU | 无限世界持久+整世界存档往返正确;常驻内存守上限;流式线程安全测试通过 |
| **M10** | 宿主+脚本系统 | 18、11、14 | 项目引用模型+公开 API XML 注释;Behaviour/Component API;世界脚本接口;Roslyn+可回收 ALC 热重载;异常隔离;IDE 启动 | 外部项目引用引擎获完整补全;脚本热重载不崩、ALC 可回收;脚本驱动一个测试实体 |
| **M11** | 编辑器+管理 UI | 12 | Hexa.NET.ImGui docking;材质/笔刷调色板;世界检视;全套调试叠层;性能 HUD;材质+反应实时编辑器(id 稳定热重载);Inspector;资源浏览器;sim 控制;存读档 UI;Play 模式 | 编辑器可视化全部内部态;改材质即时生效不损坏;可在编辑器内捏世界与调参 |
| **M12** | Demo 整合+测试+打包+性能收口 | 13、14、15、16 | 落沙游戏:可操作角色+全材质反应+刚体+粒子+光照+音频+关卡(仅依赖公开 API);全套测试通过;发行管线**win-x64 优先激活**(win-arm64 条件激活,linux/osx 四 RID dormant 保留矩阵位与全部实现、翻 active:true 即恢复)+R2R+AOT;玩家友好启动布局(包根启动 exe/脚本、content、运行时依赖进 app/);目标硬件基准落实;GC 模式定档 | 满足架构 §1.4 全部成功度量;Demo 端到端可玩;反推出的引擎 API 缺口全部补齐;**发行退出标准从「6-RID 收口」改为「win-x64 优先激活集全绿 + 保留集 dormant 位存在且可一键激活」** |
| **M13** | 编辑器独立化与发行解耦 | 19、18、12、15、Gui | **入口门=GUI 宿主中性化重构**:抽出 `PixelEngine.Gui`(HexaImGuiBackend/IGuiContext 运行时适配/中性 EditorRenderBridge/GuiFontManager 含 CJK)、Hosting 删对 `PixelEngine.Editor` 硬引用改抽象 GUI/相位[10]钩子接口(编辑器壳注入)、DemoProgram 改用中性 host。独立编辑器应用 `apps/PixelEngine.Editor.Shell`(顶层 EXE/单窗口单 GL 上下文/单进程):ProjectPicker+in-process 宿主跑 Edit/Play;类 Unity GameObject 层级/Inspector(名称+启用位+Transform TRS+组件增删)/ImGuizmo 变换 gizmo/点选拾取/prefab(完整含嵌套+override 传播)/.scene schema v2(ParentId+Transform 块+Vector2)保存往返、复用 plan/12 面板;编辑器内 BuildSettings 面板一键出**不含编辑器**的玩家包(子进程调 plan/15 build-player)。plan/15 Windows-first 目录整理(app/ 子目录布局+RID 激活门控)+玩家包 player-only 审计 | GUI 宿主中性化重构落地、`Hosting` 编译期不再引用 `PixelEngine.Editor`;shell 独立进程内可开工程、编辑层级/Inspector/gizmo/prefab、.scene v2 往返无损、Play 在编辑器进程内跑;编辑器内一键出包产出通过 player-only 审计(app/ 内无 `PixelEngine.Editor.dll` 与编辑器专属面板闭包,允许玩家 HUD 所需 `Hexa.NET.ImGui`);plan/18 §5 editor-window 证据从 Demo `--editor` 等价迁移到 shell `--window-ticks`/scripted-probe |
| **M14** | 玩法深化与交互 UI | 03、04、05、06、07、08、11、13、14、16、20 | demo-playability 横切:per-cell **Damage(byte) SoA 平面**(单缓冲原地、写 cell 前查 `CellFlags.RigidOwned`、命中刚体像素经 `IRigidDamageSink` 路由触发形状重建、绝不在刚体像素累加 Damage、进 ChunkSnapshot/ChunkCodec RLE 段、bump `SaveFormatVersion`+旧档迁移 Damage=0);MaterialDef 增 Integrity/DestroyedTarget/RenderStyle/EdgeColorBGRA/Opacity/HighlightColorBGRA 等破坏与视觉字段(只存 MaterialDef,渲染相位 CPU 算 BGRA 不写回 cell)、materials.json 增 boundary_stone/gravel;武器库 weapons.json+WeaponCatalog+WeaponController 六类(单点射击/炸弹/手榴弹/激光枪/挖掘/建造)+IWorldEffects DamageCircle/DamageBeam;熔岩矿洞逃生可玩循环(MissionDirector/ObjectiveCrystal×3/RisingHazardDirector 上涨熔岩/ExtractionTrigger 限时抵达出口/计分胜负)。plan/20 `PixelEngine.UI` 游戏内 HTML 大 UI(RmlUi 子集主后端+Ultralight 可选+ManagedFallbackBackend 纯托管基线,不变式 #10 已获批修订) | Damage 平面破坏可持久往返(save→load 逐 cell 等价)、抗性差异化生效、视觉可辨识;六类武器数据驱动可切换、熔岩矿洞逃生循环胜负判定正确;`PixelEngine.UI` HTML 大 UI 相位0-1-10 挂载、输入三级仲裁(编辑器>HTML UI>游戏)、与编辑器 ImGui/玩家 HUD 确定性叠放 |

> 与架构 §18 差异:新增 M10(脚本)、M11(编辑器);架构原 M9(存档+流式+打包)拆为 M9(流式存档)与 M12 内的打包收口;架构原 M10(Demo+调优)对应 M12。M13(编辑器独立化与发行解耦)、M14(玩法深化与交互 UI)是本轮用户新增需求在架构里程碑之外的续接,置于 M12 之后。可选项(M5 全温度场、M7 Radiance Cascades)明确可裁剪但本项目「一步到位」默认全做。

## 3. 横切线(贯穿全程,非末尾)

- **测试(plan/14)**:每个里程碑写其对应测试;M1/M2 的边界正确性由质量守恒/反应守恒/oracle 比对性质测试把关(在没有可视化编辑器前,测试是边界 bug 的主要防线)。
- **性能加固(plan/16)**:每个热路径子系统落地即按 plan/16 审计表核对(SoA/零分配/多线程/SIMD/bounds-check 反汇编);M2、M5、M6、M7 是重点核对点。
- **打包(plan/15)**:M6 引入 Box2D native 后即建 dual-build CI;M12 打包收口从「6-RID 收口」改为 **win-x64 优先激活**(win-arm64 条件,其余 4 RID CI-optional/dormant 保留矩阵位);M13 落地 `tools/build-player.ps1/.sh` 编排器(串 native→publish→verify→package→audit、NDJSON `schema=pixelengine.build/v1`+`build-result.json`)、app/ 子目录布局(包根仅启动 exe/脚本+content/,运行时依赖与全部 dll 收进 app/)与玩家包 player-only 审计(拒 `PixelEngine.Editor.dll` 与编辑器专属面板闭包,但**允许玩家 HUD 所需的 Hexa.NET.ImGui**)。

## 4. 并行关系

- M3(材质)、M4(粒子)、部分 M7(渲染光照)可在 M2 完成后并行推进。
- M8(音频)依赖较少(仅 Core 事件总线),可在 M3 后任意并行。
- M10(脚本)与 M11(编辑器)的「框架搭建」可与 M6–M9 并行准备,但其完整实现依赖被它们消费/可视化的子系统先就位。
- 单人开发按表顺序;多人可按 DAG 分支并行,合流处跑 plan/14 性质测试。

### 4.1 硬顺序约束(M13/M14,不可乱序)

M13/M14 引入的重构与解耦有若干**强前置**,乱序会使断言无法转绿或踩循环引用,须按下表执行:

- [x] **GUI 宿主中性化重构先于玩家包审计新规则**:抽出 `PixelEngine.Gui`(HexaImGuiBackend/IGuiContext 运行时适配/中性 EditorRenderBridge/GuiFontManager)、`Hosting` 删对 `PixelEngine.Editor` 硬 ProjectReference 改抽象 GUI/相位[10]钩子接口、`DemoProgram.cs` 改用中性 host —— 三者是 plan/15 玩家包 player-only 断言(app/ 内无 `PixelEngine.Editor.dll` 与编辑器专属面板闭包)的入口门前置;未落地前该审计断言标 blocked、无法转绿。注意:Demo **没有**对 `PixelEngine.Editor` 的直接 ProjectReference,耦合是经 `Hosting.csproj` 引用 Editor + Hosting 源文件用 Editor 类型 + 玩家 HUD 经 `IGuiContext`→`ScriptGuiContext`(在 Editor)→`HexaImGuiBackend` 的传递闭包;故真正动作是中性化重构而非「移除 Demo 对 Editor 的项目引用」。
- [x] **plan/18 窗口/GL 上下文所有权解耦公开 API + 编辑态 bootstrap 先于 plan/19 §4.1/§4.4 壳落地**:shell(`EditorShellWindow`)拥有唯一窗口/GL 上下文,Engine 提供 attach 到外部既有窗口且不 own、`Engine.Dispose` 不销毁该窗口的路径;`EditorHostBootstrap` 可在 Engine 装配前立起中性窗口/Gui host,默认编辑器面板由 shell 侧 `IEditorHostExtension` 注入。此 API 未就位前 shell 无法 attach 既有窗口、「释放 Engine 保留窗口」矛盾不消。
- [x] **plan/15 §3.11 build-player 编排器先于 plan/19 编辑器内 BuildSettingsPanel**:面板只经 `System.Diagnostics.Process` 子进程编排、逐行 drain NDJSON 进度、读 `build-result.json`,不重实现打包管线。
- [x] **plan/03 Damage 平面先 bump `SaveFormatVersion` 并落存档契约,再落 plan/04 破坏模型与 plan/08 裂纹着色**:per-cell Damage(byte) SoA 平面进 ChunkSnapshot/ChunkCodec(RLE 段)、bump `SaveFormatVersion`+旧档→新档迁移(旧档 Damage=0)、随 material remap 缺失 fallback 后 Damage 清 0;触及架构 §7.1/§12.2 每-cell 字节预算承重墙(4B→5B/cell +25%、每常驻 chunk 16KB→20KB),须先过显式预算评审。plan/04 MaterialDef 破坏字段与 plan/08 RenderStyle/Damage 裂纹着色以此平面为执行底座。
- [x] **plan/14 `PerformanceHardeningToolingDisciplineTests` 参数化与 plan/15 §2.1/§3.10 RID 门控实现同提交**(硬耦合):`tools/release-rids.json` 作为单一真相源，`tools/release-matrix.ps1` 派生 active RID、`packageCount`、`assetCount`，`tools/audit-release-artifacts.*` 与 `tools/release-evidence-preflight.*` 接收 active RID / expected package count；`HostingProjectDisciplineTests` 锁定含 / 不含 win-arm64 计数分支、dormant RID 翻 active dry-run、CI 6-RID build/test 矩阵不随发行门控收敛，`PerformanceHardeningToolingDisciplineTests.ReleaseEvidencePreflightBashEntryDelegatesActiveRidArguments` 等锁定 Bash/PowerShell 预检参数传递。外部 release 产物和 GitHub Release 证据仍按 §6 保持阻塞，不因本项勾选而视为发行验收完成。
- [x] **plan/20 HTML UI 阻塞已解除(不变式 #10 修订已获批)且 plan/08 显式 UI 层注册接口已先于 UiLayerCompositor 装配(plan/18)**:已用 `IUiPresentLayer` + `RenderPipeline.RegisterUiLayer(order, layer)` 替代脆弱的多播订阅顺序,确定性决定「世界→HTML UI→编辑器」叠放;`UiLayerCompositor`/`GuiRenderBridge` 注册 `UiPresentLayerOrders.Game`, `EditorRenderBridge` 注册 `UiPresentLayerOrders.Editor`,旧 `BeforePresentUi` 仅保留为兼容 hook;RmlUi 子集主后端 + Ultralight 可选 + ManagedFallbackBackend 纯托管基线的三后端选型随 #10 修订落地。验证:`RenderPipelineContractTests`、`PixelEngine.UI.Tests`、Hosting Game UI/窗口所有权相关测试通过。

## 5. 提交节点总表(中文 git 提交序列)

按 `AGENTS.md §6` 格式,每个节点完成即提交。下表是建议的提交粒度(每里程碑内多次小步提交);scope 见 AGENTS §6。

| 序 | 提交信息(示例) | 对应 |
|---|---|---|
| 0 | `docs(plan): 建立 plan 锚文档与全部子系统计划` | 本批 plan 文档(首提) |
| 1 | `build(core): 建立解决方案骨架/CPM/Directory.Build/CI` | plan/01 |
| 2 | `feat(core): 实现数学/内存/JobSystem/事件总线/时间/诊断` | plan/02 |
| 3 | `feat(sim): 单 chunk SoA 网格+原地 bottom-up+parity(M0)` | plan/03 |
| 4 | `feat(render): Silk.NET 开窗+BGRA 全帧 PBO 上传(M0)` | plan/08 |
| 5 | `feat(sim): 64x64 chunk+dirty rectangle+border ring(M1)` | plan/03、07 |
| 6 | `test(sim): chunk 边界质量守恒性质测试(M1)` | plan/14 |
| 7 | `perf(sim): 4-pass checkerboard 多线程+32px move cap(M2)` | plan/03、02 |
| 8 | `feat(sim): 数据驱动材质/反应表/tag 展开/接触火传播(M3)` | plan/04 |
| 9 | `feat(content): materials/reactions JSON 加载+name↔id 表(M3)` | plan/04、Content |
| 10 | `feat(sim): 自由粒子池+cell↔particle handshake(M4)` | plan/05 |
| 11 | `feat(render): 粒子 stamp 合成+emissive(M4)` | plan/08 |
| 12 | `feat(sim): 1/4 分辨率温度场+相变+SIMD stencil(M5)` | plan/04 |
| 13 | `feat(interop): Box2D v3 绑定+UnmanagedCallersOnly task 桥(M6)` | plan/06 |
| 14 | `feat(physics): CCL/marchingsquares/DP/凸分解→Box2D 刚体(M6)` | plan/06 |
| 15 | `feat(physics): 两世界同步+不可变 mask 权威+破坏重建(M6)` | plan/06 |
| 16 | `feat(physics): kinematic 角色控制器 vs 像素地形(M6)` | plan/06 |
| 17 | `feat(render): emissive+fog-of-war+bloom+dither 光照(M7)` | plan/08 |
| 18 | `feat(render): GPU compute 光照/bloom+可选 Radiance Cascades(M7)` | plan/09 |
| 19 | `feat(audio): OpenAL+事件驱动材质音效+限频去重(M8)` | plan/10 |
| 20 | `feat(world): chunk 流式装卸+border ring+内存上限 LRU(M9)` | plan/07 |
| 21 | `feat(serialize): chunk 二进制 RLE+LZ4+存档+id 重映射+迁移(M9)` | plan/07 |
| 22 | `feat(host): EngineBuilder/Engine/EngineContext+12 相位主循环编排(M10)` | plan/18 |
| 22b | `feat(script): Behaviour API+世界脚本接口(经 Hosting 服务后端)(M10)` | plan/11、18 |
| 23 | `feat(script): Roslyn+可回收 ALC 热重载+异常隔离+IDE 启动(M10)` | plan/11 |
| 24 | `feat(editor): ImGui docking+材质笔刷+世界检视(M11)` | plan/12 |
| 25 | `feat(editor): 调试叠层+性能 HUD+材质反应实时编辑器(M11)` | plan/12 |
| 26 | `feat(editor): Inspector+资源浏览器+sim 控制+存读档 UI+Play 模式(M11)` | plan/12 |
| 27 | `feat(demo): 落沙游戏角色控制+材质笔刷玩法+关卡(M12)` | plan/13 |
| 28 | `feat(demo): 完整材质集+反应内容+刚体/粒子/光照/音频(M12)` | plan/13 |
| 29 | `test: 补齐 physics/serialization/scripting 测试与基准(M12)` | plan/14 |
| 30 | `build: win-x64 优先激活+跨平台矩阵保留 dormant+R2R+NativeAOT+Box2D dual-build 发行管线(M12)` | plan/15 |
| 31 | `perf: 性能加固审计收口+目标硬件实测校准(M12)` | plan/16 |
| 32 | `feat(gui): GUI 宿主中性化——抽出 PixelEngine.Gui(HexaImGuiBackend/IGuiContext 适配/中性 EditorRenderBridge/GuiFontManager 含 CJK)(M13 入口门)` | plan/18、12、20、Gui |
| 33 | `refactor(host): Hosting 删对 PixelEngine.Editor 硬引用+改抽象 GUI/相位[10]钩子接口+DemoProgram 改用中性 host(M13)` | plan/18、13 |
| 34 | `feat(host): 窗口/GL 上下文所有权解耦 API(shell 拥有窗口/Engine attach 不 own/Dispose 不销毁)+EngineSceneDocument 保存 writer+编辑态 bootstrap(M13)` | plan/18 |
| 35 | `feat(editor): 独立编辑器壳 apps/PixelEngine.Editor.Shell——顶层 EXE/单窗口单上下文/ProjectPicker/in-process Edit-Play 宿主(M13)` | plan/19 |
| 36 | `feat(editor): GameObject 层级/Inspector(名称+启用位+TRS+组件增删)/ImGuizmo 变换 gizmo/点选拾取(M13)` | plan/19 |
| 37 | `feat(editor): prefab(嵌套+override 传播)+.scene schema v2(ParentId/Transform/Vector2)往返+复用 plan/12 面板(M13)` | plan/19、12 |
| 38 | `build(release): tools/build-player.ps1/.sh 编排器(native→publish→verify→package→audit)+NDJSON pixelengine.build/v1+build-result.json(M13)` | plan/15 |
| 39 | `feat(editor): 编辑器内 BuildSettings 面板——子进程调 build-player 出不含编辑器的玩家包(M13)` | plan/19、15 |
| 40 | `build(release): Windows-first RID 激活门控(release-rids.json)+app/ 子目录布局+玩家包 player-only 审计(拒 Editor.dll,允许 HUD ImGui)(M13)` | plan/15、14 |
| 41 | `feat(sim): per-cell Damage(byte) SoA 平面+单缓冲原地破坏+RigidOwned 路由 IRigidDamageSink+bump SaveFormatVersion 旧档迁移(M14)` | plan/03、07、16 |
| 42 | `feat(material): MaterialDef 破坏/视觉字段(Integrity/DestroyedTarget/RenderStyle/EdgeColor/Opacity/Highlight)+materials.json boundary_stone/gravel(M14)` | plan/04 |
| 43 | `feat(render): 按 RenderStyle 差异化着色(描边/半透/流动高光)+Damage 裂纹叠色,CPU 算 BGRA 不写回 cell(M14)` | plan/08 |
| 44 | `feat(demo): 武器库 weapons.json+WeaponCatalog/WeaponController 六类+IWorldEffects DamageCircle/DamageBeam(M14)` | plan/13、11、05 |
| 45 | `feat(demo): 熔岩矿洞逃生可玩循环——MissionDirector/ObjectiveCrystal×3/RisingHazardDirector/ExtractionTrigger/计分胜负(M14)` | plan/13 |
| 46 | `feat(ui): PixelEngine.UI 游戏内 HTML 大 UI——RmlUi 主/Ultralight 可选/ManagedFallback 基线+相位0-1-10 挂载+输入三级仲裁(M14)` | plan/20、08、18 |
| 47 | `test: 补齐 M13/M14——player-only 审计/build-player NDJSON 解析/Damage save→load 逐 cell 等价/武器与可玩循环/UI 布局与仲裁(M13/M14)` | plan/14 |

> 提交粒度可按实际拆得更细;但**每完成一个节点必须立即用中文提交**,不攒堆。每次提交前确保该节点相关测试通过(`AGENTS.md §7`)。提交节点 32–34 是 M13 入口门(GUI 宿主中性化重构),必须先于 40 的玩家包 player-only 审计;38(build-player 编排器)先于 39(编辑器内打包面板);41(Damage 平面+bump SaveFormatVersion)先于 42/43。

## 6. 验收(本路线图)

- [!] M0–M12 全部退出标准达成。阻塞：`plan/00`、`plan/09`、`plan/13`、`plan/14`、`plan/15`、`plan/16`、`plan/18` 仍保留目标硬件、发行产物(win-x64 优先激活集全绿+dormant 可一键激活)、macOS 签名公证、真实窗口人工验收、AVX-512/硬件计数器、ComputeSharp/DX12 资源契约与 native 泄漏审计等外部或架构决策阻塞；各 preflight 只接受 evidence manifest 或明确探针报告，`*_pending_review`、`local_probe_only`、`scripted_probe_only` 与 `process_smoke_only` 仍不是验收通过，不能假装已达成。
- [ ] M13 全部退出标准达成:GUI 宿主中性化重构落地(`PixelEngine.Gui` 抽出、`Hosting` 编译期不再引用 `PixelEngine.Editor`、`DemoProgram` 改用中性 host);`apps/PixelEngine.Editor.Shell` 独立进程内可开工程/编辑层级/Inspector/gizmo/prefab(嵌套+override)/.scene v2 往返无损/Play 就绪;编辑器内一键出包经 build-player 产出并通过玩家包 player-only 审计(app/ 内无 `PixelEngine.Editor.dll` 与编辑器专属面板闭包、允许玩家 HUD 所需 `Hexa.NET.ImGui`);win-x64 优先 RID 激活门控+app/ 子目录布局落地;plan/18 §5 editor-window 证据从 Demo `--editor` 等价迁移到 shell `--window-ticks`/scripted-probe。
- [ ] M14 全部退出标准达成:per-cell Damage(byte) SoA 平面破坏可持久往返(save→load 逐 cell 等价、bump `SaveFormatVersion`+旧档迁移)、抗性差异化生效且视觉可辨识(渲染相位 CPU 算 BGRA 不写回 cell);六类武器数据驱动可切换(weapons.json)、熔岩矿洞逃生可玩循环(水晶×3+上涨熔岩+限时抵达出口)胜负判定正确;`PixelEngine.UI` HTML 大 UI(RmlUi 主/Ultralight 可选/ManagedFallback 基线,不变式 #10 修订已获批)相位0-1-10 挂载、输入三级仲裁与编辑器/HUD 确定性叠放。
- [!] 架构 §1.4 成功度量在目标硬件实测达标。阻塞：当前只有本机 win-x64 / Ryzen 7 5800X 短基准与局部报告；仍缺 6 RID 代表硬件长跑、AVX-512 降频净损、ETW 硬件计数器、真实窗口 Demo 玩法与完整帧预算闭合。
- [!] `plan/01`–`plan/16` 各文档「验收标准」全部勾选。阻塞：`rg -n "^- \[!\]" plan` 仍能看到多个明确阻塞项，尤其集中在真实窗口验收、发行矩阵、目标硬件性能与少量架构决策；需先解除这些阻塞后才能改为完成。
- [x] Demo 仅依赖引擎公开 API 跑通(无引擎内部后门)：`HostingProjectDisciplineTests` 已验证 Demo csproj 只引用 `PixelEngine.Hosting` / `PixelEngine.Scripting`、源码不绕过 Hosting facade、默认 `.scene` 通过公开文档格式引用 `LevelDirector`、可见内容包 API 不泄漏 Content / Simulation 实现类型；本轮 `dotnet test tests\PixelEngine.Hosting.Tests\PixelEngine.Hosting.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~HostingProjectDisciplineTests"` 通过 5 项。
- [x] 每个里程碑均有对应中文 git 提交记录：`git log --format="%s" --all` 显示 297 条符合 `<type>(<scope>): <中文简述>` 的本地提交，覆盖 core/sim/physics/render/audio/world/serialize/host/script/editor/demo/build/perf 等里程碑 scope；本轮 M12 Demo 收口也已按节点提交。

## 7. 提交节点

- 提交:`docs(plan): 建立路线图与提交节点总表(plan/17)`(随 plan 骨架首提)。
- [x] 提交:`docs(plan): 插入 M13/M14 里程碑与提交节点 32–47+硬顺序约束(plan/17)`(本轮独立编辑器/发行解耦/玩法深化/交互 UI 修订)。
