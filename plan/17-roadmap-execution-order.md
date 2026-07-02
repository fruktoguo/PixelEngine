# Plan 17 — 路线图、执行顺序与提交节点总表

> 本文件是实际编码的节奏表:把 `plan/01`–`plan/16` 编织进里程碑 M0–M12,给出依赖图、并行关系、退出标准与中文 git 提交节点序列。与架构文档 §18 的 M0–M10 对齐,并把用户新增的脚本系统(plan/11)、编辑器(plan/12)纳入。
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
18 宿主/主循环编排 ── 11 脚本系统 ── 12 编辑器 UI ── 13 Demo 游戏  │
                                                           │
14 测试/基准  ─┐                                           │
16 性能加固    ├── 贯穿全程(cross-cutting,随每个子系统推进)│
15 打包/分发  ─┘ ←───────────────────────────────────────┘
```

要点:`02` 是一切的地基(JobSystem 同时服务 CA checkerboard 与 Box2D task 桥)。`03` 之上,`04/05/06/07` 可并行起步。`08` 渲染可与 `04/05` 并行。`11→12→13` 是「可编程→可编辑→做游戏」链。`14` 测试与 `16` 性能加固不是末尾里程碑,而是**贯穿每个子系统**的横切线(写子系统即写其测试与基准)。`15` 打包在具备 native 依赖(`06`)后随时可立管线,正式收口在最后。

## 2. 里程碑映射(M0–M12)

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
| **M12** | Demo 整合+测试+打包+性能收口 | 13、14、15、16 | 落沙游戏:可操作角色+全材质反应+刚体+粒子+光照+音频+关卡(仅依赖公开 API);全套测试通过;6-RID R2R+AOT 打包;目标硬件基准落实;GC 模式定档 | 满足架构 §1.4 全部成功度量;Demo 端到端可玩;反推出的引擎 API 缺口全部补齐 |

> 与架构 §18 差异:新增 M10(脚本)、M11(编辑器);架构原 M9(存档+流式+打包)拆为 M9(流式存档)与 M12 内的打包收口;架构原 M10(Demo+调优)对应 M12。可选项(M5 全温度场、M7 Radiance Cascades)明确可裁剪但本项目「一步到位」默认全做。

## 3. 横切线(贯穿全程,非末尾)

- **测试(plan/14)**:每个里程碑写其对应测试;M1/M2 的边界正确性由质量守恒/反应守恒/oracle 比对性质测试把关(在没有可视化编辑器前,测试是边界 bug 的主要防线)。
- **性能加固(plan/16)**:每个热路径子系统落地即按 plan/16 审计表核对(SoA/零分配/多线程/SIMD/bounds-check 反汇编);M2、M5、M6、M7 是重点核对点。
- **打包(plan/15)**:M6 引入 Box2D native 后即建 dual-build CI;M12 收口 6-RID 发行。

## 4. 并行关系

- M3(材质)、M4(粒子)、部分 M7(渲染光照)可在 M2 完成后并行推进。
- M8(音频)依赖较少(仅 Core 事件总线),可在 M3 后任意并行。
- M10(脚本)与 M11(编辑器)的「框架搭建」可与 M6–M9 并行准备,但其完整实现依赖被它们消费/可视化的子系统先就位。
- 单人开发按表顺序;多人可按 DAG 分支并行,合流处跑 plan/14 性质测试。

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
| 30 | `build: 6-RID R2R+NativeAOT+Box2D dual-build 发行管线(M12)` | plan/15 |
| 31 | `perf: 性能加固审计收口+目标硬件实测校准(M12)` | plan/16 |

> 提交粒度可按实际拆得更细;但**每完成一个节点必须立即用中文提交**,不攒堆。每次提交前确保该节点相关测试通过(`AGENTS.md §7`)。

## 6. 验收(本路线图)

- [!] M0–M12 全部退出标准达成。阻塞：`plan/00`、`plan/09`、`plan/13`、`plan/14`、`plan/15`、`plan/16`、`plan/18` 仍保留目标硬件、6-RID CI/发行产物、macOS 签名公证、真实窗口人工验收、AVX-512/硬件计数器、ComputeSharp/DX12 资源契约与 native 泄漏审计等外部或架构决策阻塞，不能假装已达成。
- [!] 架构 §1.4 成功度量在目标硬件实测达标。阻塞：当前只有本机 win-x64 / Ryzen 7 5800X 短基准与局部报告；仍缺 6 RID 代表硬件长跑、AVX-512 降频净损、ETW 硬件计数器、真实窗口 Demo 玩法与完整帧预算闭合。
- [!] `plan/01`–`plan/16` 各文档「验收标准」全部勾选。阻塞：`rg -n "^- \[!\]" plan` 仍能看到多个明确阻塞项，尤其集中在真实窗口验收、发行矩阵、目标硬件性能与少量架构决策；需先解除这些阻塞后才能改为完成。
- [x] Demo 仅依赖引擎公开 API 跑通(无引擎内部后门)：`HostingProjectDisciplineTests` 已验证 Demo csproj 只引用 `PixelEngine.Hosting` / `PixelEngine.Scripting`、源码不绕过 Hosting facade、默认 `.scene` 通过公开文档格式引用 `LevelDirector`、可见内容包 API 不泄漏 Content / Simulation 实现类型；本轮 `dotnet test tests\PixelEngine.Hosting.Tests\PixelEngine.Hosting.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~HostingProjectDisciplineTests"` 通过 5 项。
- [x] 每个里程碑均有对应中文 git 提交记录：`git log --format="%s" --all` 显示 297 条符合 `<type>(<scope>): <中文简述>` 的本地提交，覆盖 core/sim/physics/render/audio/world/serialize/host/script/editor/demo/build/perf 等里程碑 scope；本轮 M12 Demo 收口也已按节点提交。

## 7. 提交节点

- 提交:`docs(plan): 建立路线图与提交节点总表(plan/17)`(随 plan 骨架首提)。
