# PixelEngine 架构与需求设计文档

> 面向 .NET 10 LTS（2026）的 Noita 级别 2D 像素世界引擎 + Unity-like Editor + Web-first 透明 HTML UI Runtime + Showcase Demo Game
> 版本：v2.2（新增核心目标与产品定位锚文档）
> 作者：Lead Architect
> 产品北极星：`PixelEngine-核心目标与产品定位.md`。本文负责技术架构，最终产品形态、目标用户、Demo 完整度与优先级判断以该文档为产品层依据。
> 置信度标注约定：本文在关键结论处标注 [高] / [中] / [低]。来自 GDC 2019 "Exploring the Tech and Design of Noita" talk 与多方实现交叉验证者为 [高]；由社区实现反推、未经官方确认者为 [中]；纯属推断 / 需实测者为 [低]。涉及 .NET 10 / Box2D v3 / OpenGL 的 API 事实均按 2026 年官方文档校核。

---

## 1. 需求分析与目标

### 1.1 项目定位

本项目是一个自研 2D 像素游戏引擎，其 **WORLD（世界模拟）层对标 Noita（Nolla Games 的 "Falling Everything" 引擎）**。引擎必须复刻 Noita 的世界技术核心：每个屏幕像素都是一个被独立模拟、且可参与碰撞的物质单元（material cell），具体包括 falling-sand 细胞自动机（cellular automata, CA）、自由粒子（free particles）、像素级精确碰撞（pixel-perfect collision）、细胞生命周期（cell lifecycle）、以及材质反应（material reactions）。产品交付物由四个面组成：可复用引擎内核、面向资深 Unity 用户的 Unity-like Editor、面向玩家的 Web-first 透明 HTML UI Runtime，以及 Showcase Demo Game。Showcase Demo Game 不以堆叠内容量取胜，而是用完整且聚焦的可玩闭环证明射击/爆炸地形破坏、切割后刚体物理、透明 HTML UI、性能/手感和公开 API dogfood。

**明确不在范围内**的是 Noita 的法杖 / 法术系统（wand/spell system）。我们只做「世界」这一半，不做 roguelite 的构筑玩法。这一点对架构很关键：它意味着我们不需要为成千上万种 modifier 的运行时组合做开放式脚本架构，可以把材质 / 反应做成相对收敛的数据驱动表。

### 1.2 「对标 Noita 世界」的具体含义

「对标」不是像素级抄袭，而是在以下可验证维度上达到同等档次的技术与观感 [高]：

第一，**全像素模拟且可碰撞**。屏幕上看到的每一个像素都不是单纯绘制结果，而是一个有材质属性、参与物理的 cell；地形可被任意挖掘 / 爆破，挖出的碎块能脱落、坍塌、被点燃、被腐蚀。

第二，**falling-sand 行为正确**。粉末（沙）形成正确的休止角（angle of repose）堆积；液体会水平铺平并按密度分层（油浮于水）；气体上升扩散；火沿可燃物概率传播并把水烧成蒸汽；熔岩遇水生成石头。这些是 emergent 行为，由局部规则与数据驱动的反应表产生，而非脚本硬编码个例。

第三，**像素簇 → 刚体**。被挖断、与锚定地形脱离的连通固体像素块会转化为受 Box2D 驱动的刚体（marching squares 提取轮廓 → 简化 → 凸分解 → 复合刚体），刚体每帧再被栅格化回像素世界，形成「两套世界互相耦合」的标志性效果。**关键不变式：刚体像素每帧确实往返于网格（erase → step → re-stamp），CA 因此能把刚体当地形对待**——沙能堆在木箱上、火能烧到木箱、酸能腐蚀它、CA 也能反过来挖它（这是双向耦合的本义，见 §8.3 对亚像素漂移的正确处理方式）。

第四，**性能与规模**。在全屏激活的极端情况下逼近 60fps，而典型场景（多数像素静止）有充裕余量；世界可向任意方向扩展（chunk hash-map + 流式装载 / 卸载）。

第五，**自由粒子与网格的双向转换**。冲击 / 爆炸把 cell 抛为带速度的飞行粒子，粒子落定后重新沉积为 cell（「跳进血泊溅起血花再落下」的连续感）。

我们**不追求**的对标维度：bit 级可复现的实时模拟（Noita 实时 sim 本身就是非确定性的，见 §6）、真实 Navier-Stokes 流体（Noita 是 fake pressure，我们也是）、以及世界生成的 Herringbone Wang tile 程序化布局（属于内容生成，不是世界 sim 技术核心，Demo 用简化方案即可）。

### 1.3 硬性需求

性能：全屏像素 sim 60fps（16.6ms 帧预算）。广泛兼容性：Windows 为主，理想情况覆盖 Linux / Mac。可维护性。语言策略：C# 为主语言，仅在 C# 确实无法胜任的性能关键处下沉到 C++。技术基线：假定 .NET 10 为当前 LTS。

### 1.4 成功度量（Success Metrics）

可量化、可在目标机上用 BenchmarkDotNet / 帧计时验证的指标如下 [中，数值需实测校准]：

模拟吞吐：在 8 物理核机器上，单帧（8ms sim 预算内）完成 **2–4 百万次「完全激活」cell 更新**。1920×1080 ≈ 2.07M cells，因此「全屏全激活的混沌液体」处于预算边缘，而启用 dirty-rect 后的典型场景留有大量余量。静止像素的边际成本必须 ≈ 0（这是能不能达标的根本，见 §5）。**注意：该吞吐目标的并行缩放因子（见 §12.7）必须以实测替代估算，因为内核瓶颈是内存延迟与分支预测，而非内存带宽——两者的缩放曲线截然不同。**

2026-07-02 本机 Short 校准（Ryzen 7 5800X / .NET 10.0.8，详见 `docs/benchmark-reports/2026-07-02-plan14-short.md`）显示：当前 `CellThroughputBenchmark.StepJobSystem(FullActiveLiquid)` 为 262,144 active cells / 38.327ms，约 54.7K active cells / 8ms，尚未达本节 2–4M active cells / 8ms 目标；典型 dirty-rect 为 413.1us。本记录用于把估算落到当前实现数据，不代表最终目标已达成。

2026-07-10 本机正式规模校准（详见 `docs/evidence-2026-07-10-perf-003-ca-throughput.md`）新增 2,166,784-cell `FullActive2M`：Ryzen 7 5800X / .NET 10.0.8 / 8 worker（8 physical cores）的 `StepJobSystem` 平均 12.965ms，折算约 1.337M active cells / 8ms；该结果仍低于 2M 下限，不改变本节目标，也不构成目标硬件达标证据。

帧时间分配（目标，1080p 内部分辨率）：CA sim ≤ 8ms，渲染上传 + 光照 + 后处理 ≤ 4ms，刚体 Box2D Step + 形状重建 ≤ 3–4ms，游戏逻辑 + 音频派发 + 余量 ≤ 1ms。

内存（单屏热数据）：每像素 sim 状态 ~4 字节、渲染缓冲 ~4 字节，1080p 总热数据 ~17MB，可舒适驻留 L3 / RAM。**大世界常驻内存上限单列预算（见 §12.2），不可用单屏数字代替。**

GC：稳态帧循环内**零托管分配**，Gen0 极少触发，无可感知 GC 停顿。

兼容性：默认构建在 OpenGL 3.3 Core 基线上运行（覆盖 ~2010 年后几乎所有桌面 GPU / iGPU）；对问题 Intel / 老驱动提供 OpenGL ES 3.0 + ANGLE 回退路径。

---

## 2. 核心技术挑战

这一节先把「难在哪、为什么难」讲清楚，后续章节给解法。

**挑战一：让静止像素免费。** 朴素实现每帧扫描全屏 200 万 cell，即便每 cell 仅 20ns 也要 40ms，直接爆预算。唯一出路是让每帧成本随「激活 cell 数」而非「屏幕面积」缩放。这要求 chunk + per-chunk dirty rectangle + sleeping chunks 三件套，且其 grow/shrink 与跨 chunk 唤醒（KeepAlive）逻辑必须精确，否则要么 sim「死掉」（雪崩在 chunk 边界停住），要么永远醒着（dirty rect 收不回去）。[高]

**挑战二：原地单缓冲 + 多线程的无锁安全。** 为了让 dirty-rect 优化生效，必须**单缓冲原地更新**（double buffer 会强制每帧改写整个世界，毁掉 dirty-rect）。但原地更新是顺序相关的：一个下落的 cell 可能在同一遍扫描里被再次处理而移动两次。更难的是多线程时相邻 chunk 边界上的数据竞争。Noita 的解法（4-pass checkerboard + 32px move cap）极其精巧但也极易写错——边界 KeepAlive 是整个引擎最容易出 bug 的地方，错误表现为像素在 chunk 缝隙处消失 / 复制 / 抖动。**且跨 chunk 的反应写入（不只是移动）也必须纳入同一无锁论证，否则边界处的双输出 / 定向反应会重复触发（见 §5.4、§7.4）。**[高]

**挑战三：C# 在每像素热循环里的开销。** sand-movement 内层是数据相关的 gather/scatter（读邻居、条件 swap），**无法 SIMD 向量化**，且受内存延迟与读-改-写依赖链、以及条件分支的分支误预测约束（**注意：不是内存带宽约束——工作集 ~6MB 驻留 L3，真实 DRAM 流量仅数 GB/s，远低于 ~50GB/s 的带宽上限**）。C# 的 bounds-check、潜在 GC 停顿、数组基址重载都会在这里放大。这是全项目最可能被迫下沉 C++ 的地方，也是「C# 能不能做 Noita」的真正赌注。我们的判断是：通过 SoA + Span/ref 指针漫游 + 消除 bounds check + 持久线程池，C# 可达标，但这是**强制性工程纪律，不是可选优化**。[中]

**挑战四：两套世界的同步。** 像素 CA 世界与 Box2D 刚体世界互相耦合。每帧要在旧变换处擦除刚体像素、Step 物理、读回新变换、按新变换重新栅格化。旋转栅格化若用 forward sampling 会留洞，必须 inverse sampling。刚体被破坏时要重跑连通分量、可能一分为多。形状重建（marching squares + DP + 凸分解 + 建刚体）是热点，多个刚体同时被毁会造成帧时间尖刺。且刚体的网格读写不能与 CA 的 checkerboard pass 竞争。**此外，Box2D v3 自身不开线程，其多线程需要我们提供 task-callback 桥（见 §14.2），这是 native 互操作真正的复杂度所在。**[高]

**挑战五：广泛兼容性 vs 极致性能的张力。** 极致性能偏好固定 ISA（AVX2）+ 原生库；广泛兼容偏好运行时检测 CPU 自动 light-up + 单一可移植构建。NativeAOT **默认退化到 SSE2 baseline**，会悄无声息地砍掉 SIMD。这两个硬需求必须在技术选型层面调和，**我们用 CoreCLR + ReadyToRun（R2R）作为「快启动 + 运行时 light-up」的中间路线**，而非二选一（§12.3、§13）。[高]

**挑战六：非确定性是模型固有属性。** 多线程原地单缓冲 sim 不是 bit 可复现的——线程调度顺序改变结果。若 Demo 需要回放、种子分享实时态、或 lockstep 联机，这套模型会与你对抗。必须现在就决定要不要确定性模式（事后改造极难，见 §6）。[中]

**挑战七：帧节奏与过载策略。** 像素 CA 既不能被渲染插值，又是最贵的子系统。一个朴素的「fixed-step accumulator 追帧」会在 sim 超预算时强制额外步数、直接进入 death spiral。必须明确定义过载下的降级顺序与时间膨胀策略（见 §4）。[高]

---

## 3. 总体架构

### 3.1 分层：引擎 vs Demo

采用清晰的两层 + 三纵切分。**引擎层（PixelEngine.\*）**是可复用的无玩法内核；**Demo 层（PixelEngine.Demo）**是消费引擎的具体游戏。引擎绝不反向依赖 Demo。

引擎内部按职责切为若干 assembly（详细工程结构见 §16），核心模块及其单一职责如下。

`PixelEngine.Core` 提供基础设施：数学（向量、变换、定点可选）、内存（Pinned Object Heap / NativeMemory 缓冲封装、ArrayPool 封装）、持久线程池（persistent worker pool + 帧屏障 barrier，**同时服务 CA 与 Box2D task 桥**）、确定性 RNG（每 chunk 可种子化）、事件总线（供音频 / 玩法消费 sim 事件）、诊断 / 计时。它不知道「像素」是什么。

`PixelEngine.Simulation` 是 falling-sand 内核：CellGrid（SoA 缓冲）、Chunk + dirty rect、4-pass checkerboard 调度、movement rules、material 与 reaction 表、temperature 场、free-particle pool、cell↔particle handshake。这是引擎心脏，也是性能纪律最严的地方。

`PixelEngine.Physics` 是刚体桥：连通分量（CCL）、marching squares、Douglas-Peucker、凸分解、Box2D v3 绑定封装、**Box2D task-callback 桥（把 Box2D 的并行 for 派发到 Core 的线程池）**、两套世界同步（erase/step/rasterize）、破坏后形状重建。它依赖 Simulation（读写网格）与原生 Box2D。

`PixelEngine.Rendering` 是渲染后端：Silk.NET 封装、窗口、纹理流式上传（PBO/persistent-mapped）、**自由粒子合成**、光照（emissive + fog-of-war + bloom + dither + gamma）、可选 GPU compute。它消费 Simulation 产出的 RGBA buffer 与粒子缓冲。

`PixelEngine.Audio` 是音频子系统：设备 / 混音、positional source 池、从 sim 事件队列驱动的材质化音效（impact / fire / splash / explosion / ambient）。它消费 Core 的事件总线，不进 sim 热循环。

`PixelEngine.Content` 是内容管线：从 JSON 加载 MaterialDef、Reaction 表（含 [tag] 展开）、材质纹理，构建运行时扁平索引表，**维护 material 的稳定字符串键 → 运行时 id 的映射（存档与热重载的基石，见 §11）**。

`PixelEngine.World` 是世界管理：chunk 的 hash-map 驻留、流式装载 / 卸载（serialize/deserialize 到磁盘）、激活半径与边界 ring、相机 / 视口、**常驻内存上限与 LRU 驱逐**。

`PixelEngine.Serialization` 是存档：world manifest、chunk 二进制格式、版本迁移、material id 重映射、free particles / 刚体 / 温度场的持久化（见 §11）。

`PixelEngine.Gui` 是中性 ImGui host / fallback 基础设施（字体、`IGuiContext`、诊断/fallback 绘制），服务 ManagedFallback、诊断与编辑器复用；它不是玩家侧 Web UI 产品主路径。

`PixelEngine.UI` 是 Web-first 透明 HTML UI Runtime：`IGameUiBackend` 抽象、RmlUi HTML/CSS 子集默认后端、Ultralight 可选高保真后端、ManagedFallback 基线、C#↔UI 桥、输入命中/透明区 pass-through、与渲染层 same-GL 合成。RmlUi 不承诺完整 HTML5/CSS3/JS，AI 生成标准 Web 页面走 Ultralight gate。

`PixelEngine.Editor` 是 Dear ImGui 面板层；`apps/PixelEngine.Editor.Shell` 是 Unity-like Editor 顶层应用，拥有 Project Window、Hierarchy、Inspector、Scene View、Game View、Console、Project Settings、Player Settings、Build Settings、Prefab、脚本双击外部编辑器等 authoring UX。壳、Showcase Demo Game 与 Hosting 的依赖方向必须保持 `{Demo, EditorShell} → Hosting`，Hosting 只提供中性 DTO/API，不引用 editor authoring 类型。

Showcase Demo Game 层包含玩法：玩家角色控制器（kinematic AABB vs 像素，独立于 Box2D）、输入、相机、功能完整但聚焦的 showcase 关卡、射击/爆炸/切割后刚体展示、透明 HTML HUD/menu/settings、性能与手感展示。Showcase Demo Game 只依赖引擎公开 API，不为展示效果绕过 Hosting / Scripting facade。

### 3.2 每帧数据所有权

CellGrid 的 SoA 数组是**唯一权威世界状态**。Simulation 原地改它。Physics 在专属相位里读 / 写它（擦除与栅格化刚体像素）。Rendering 每帧从 material id + 温度只读地生成 RGBA buffer 并叠加自由粒子，再上传 GPU。三者通过明确的**相位顺序**而非锁来避免竞争。世界 chunk 的**驻留集合（hash-map 的增删）只在帧边界的单线程相位变更**，后台流式线程只做磁盘 I/O 与字节缓冲准备，不触碰活动帧的 live map（见 §3.4、§11.5）。

### 3.3 帧循环 / tick 顺序（文本图）

下面是一帧的精确相位序。相位之间是顺序的（barrier 分隔），相位内部按需多线程。每帧**至多一次** sim step 与一次 physics step；过载时整体放慢（时间膨胀），绝不追帧（见 §4）。

```
FRAME N
├─ [0] Input & Time          单线程。采集输入；逻辑步长固定 dt = 1/60（见 §4，
│                            非追帧 accumulator）。决定本帧是否执行 sim step（sim 可
│                            降频，render 始终出帧）
│
├─ [1] Game Logic / Demo     单线程或粗粒度并行。玩家意图、生成事件、爆炸请求入队
│
├─ [2] Residency apply       单线程。应用上一帧后台 I/O 准备好的 chunk 装载 / 卸载到
│                            live hash-map（结构性变更只在此发生，见 §3.4 / §11.5）
│
├─ [3] Particle → Cell 沉积  并行。上一帧飞行的 free particles 推进+尝试落定；
│                            落定者写回网格并标记所在 chunk dirty
│
├─ [4] CA Simulation         核心。4-pass checkerboard over 2x2 chunk parity grid：
│      ├ pass A: 处理 (cx%2,cy%2)==(0,0) 的活跃 chunk  ── 多线程，无锁
│      ├ barrier
│      ├ pass B: (1,0)                                   ── 多线程
│      ├ barrier
│      ├ pass C: (0,1)                                   ── 多线程
│      ├ barrier
│      └ pass D: (1,1)                                   ── 多线程
│      每 chunk：bottom-up 扫描其 current dirty rect；movement(powder/liquid/gas
│      swap) + reaction(邻居对查表，写入与跨界写入受 32px-halo 保护) + lifetime；
│      改动累积进 working dirty rect；边界移动 / 跨界反应写入触发 KeepAlive(邻居 chunk)
│
├─ [5] Temperature/Heat      并行（可选 / 降频）。粗分辨率(CELL=4)热扩散；
│                            触发 melt/freeze/boil 相变(阈值+目标材质)
│
├─ [6] Dirty-rect swap       每 chunk：working rect → current rect；空则 chunk 进入 sleep
│
├─ [7] Cell → Particle 抛射  处理本帧爆炸/冲击：读 cell→清网格→从池取 particle 设速度
│
├─ [8] Physics 同步          (a) CCL 检测新脱落连通块 → 形状重建为新刚体
│      ├ (b) 对每活跃刚体：在旧变换处把其像素从网格擦除
│      ├ (c) b2World_Step(dt, subStep=4)  ── 由我方 task 桥把 Box2D 并行 for 派发到
│      │                                     Core 线程池(见 §14.2)；非 Box2D 自开线程
│      ├ (d) 读回每刚体新 transform
│      └ (e) inverse-sampling 把刚体像素重新栅格化进网格，标记 chunk dirty，写
│            owned-by-body-K 标记
│
├─ [9] Build Render Buffer   并行。material id 采样材质纹理 + 温度 glow → uint BGRA8；
│                            再叠加自由粒子(stamp 到 render buffer，emissive 者另入
│                            emissive buffer，见 §9.3)
│
├─ [10] GPU Upload & Render  渲染线程。dirty-rect 子上传或全帧上传到世界纹理；
│                            光照 pass + bloom + dither + gamma；世界/角色渲染 →
│                            游戏 Web UI(order=100,透明 alpha,same-GL) →
│                            可选编辑器 ImGui overlay(order=200) → present
│
└─ [11] World streaming(后台) 异步。超激活半径的 chunk 序列化为字节缓冲；进入半径的
                             chunk 从磁盘反序列化为字节缓冲。仅准备数据，不改 live map；
                             实际增删在下一帧相位[2]应用
```

关于相位顺序的关键约束。其一，刚体的网格擦除 / 栅格化（相位 8b、8e）必须与 CA pass（相位 4）分处不同相位，否则会与 checkerboard 竞争；放在 CA 之后、渲染之前。其二，particle→cell（相位 3）放在 CA 之前，使新沉积像素本帧即被 CA 看见；cell→particle（相位 7）放在 CA 之后，避免刚抛出的粒子本帧又被 CA 误处理。其三，**chunk 驻留的结构性增删（相位 2）与后台 I/O（相位 11）严格分离**，前者单线程、在帧边界、对 live map 独占；后者只碰离线字节缓冲（见 §3.4）。[中]

### 3.4 流式装卸的线程安全

「concurrent hash-map」本身不足以保证安全：一个 worker 在相位 4 做 KeepAlive / 移动写入某邻居 chunk 时，若后台线程正在卸载或刚装载该 chunk，就是数据竞争。我们用**装卸屏障**而非并发容器解决 [高]：

后台流式线程（相位 11）只做两件事——把待卸载 chunk 的权威数组序列化进一个独立字节缓冲、把待装载 chunk 的磁盘字节反序列化进一个游离的 `Chunk` 对象。它**绝不**把这些插入或移出活动帧正在使用的 live hash-map。真正的 map 增删只在相位 2 单线程发生，此时所有 CA / physics 相位都已结束、下一帧尚未开始，不存在并发访问。

为避免「跨界写入落到一个非驻留邻居」的洞，激活区外再保留**一圈宽度 1 chunk 的 border ring**（驻留但默认 sleep）。这样任何 32px-halo 内的跨界写入永远落在一个已驻留 chunk 上；border ring 自身被写入时会被唤醒并在下一帧的相位 2 把它的外圈邻居提升为新 border。驱逐发生在 border 之外，因此被驱逐 chunk 不可能在本帧被任何 worker 触碰。

---

## 4. 时间步进与帧节奏

这是从草稿中分离出来、并修正「fixed-step accumulator over once-per-frame pipeline」自相矛盾的专章。

### 4.1 模型选择：固定逻辑步长 + 时间膨胀，不追帧

明确决定：**sim 与 physics 每个渲染帧至多执行一次 step，逻辑步长恒为 dt = 1/60**；当一帧的真实墙钟时间超过 16.6ms 时，游戏**整体放慢**（时间膨胀，Noita 的实际行为），**而不是用 accumulator 追帧** [高]。理由有三：(1) 像素 CA 是最贵子系统，追帧会在 sim 超预算时强制额外步数，正反馈直接 death spiral；(2) 像素 CA 不能被渲染插值（cell 是离散整数态，没有可插值的连续表示）；(3) 一帧跑 2 次 CA 但只跑 1 次 physics（或反之）会产生可见的耦合错位（刚体像素与 CA 像素步调不一致）。

因此我们**不**使用 accumulator 在一个渲染帧内驱动 N 个 sim step。CA step、physics step、particle step 三者**严格 1:1 绑定到同一个被执行的 sim tick**，使用同一个 dt，physics 的 `subStepCount=4` 是 Box2D 内部的子步（求解稳定性），与「额外 CA step」无关、不互相换算。

### 4.2 sim 降频（render 与 sim 解耦的唯一合法形式）

允许的解耦是**降低 sim 频率而保持 render 频率**，这是优雅降级而非追帧：在重载或低端机上可把 sim 设为 30Hz（每两个渲染帧执行一次相位 3–8），渲染相位 9–10 仍每帧出帧，复用上一次 sim 产出的世界纹理（必要时对相机平移做整图偏移，而非对像素内容插值）。dt 相应取 1/30。这给出「画面流畅、世界慢一点」的体验，远好于卡顿或 death spiral。是否启用、阈值多少，由 §17 的帧计时器自适应或玩家设置决定。

### 4.3 过载降级顺序（明确策略）

当连续若干帧 sim 超预算，按以下固定顺序降级，先省最贵且最不影响正确性的子系统 [中]：

第一级，降低 / 关闭全分辨率热场（相位 5 改为每 N 帧或仅接触式火传播，见 §7.5）。第二级，降低光照质量（关 Radiance Cascades / bloom，回退 fog-of-war + emissive，见 §9.3）。第三级，对远离相机的活跃 chunk 降频模拟（每隔一帧才更新外圈活跃 chunk）。第四级，整体 sim 降到 30Hz（§4.2）。第五级（兜底），接受 <60fps 的真实减速——因为我们不追帧，最坏情况只是低帧率，**不会进入 death spiral**。

为防止任何单帧的物理 / 形状重建尖刺把降级逻辑带偏，所有「每帧成本上限」类节流（形状重建每刚体每帧至多一次、合并像素移除、CCL off-thread）在 §8.4、§15 强制实施。

### 4.4 玩法逻辑的步进

Demo 的玩法逻辑（相位 1）与 sim 同 tick 推进，使用同一 dt，天然与 sim 对齐。若 Demo 某些子系统（如固定频率的 AI 决策）需要独立的固定步，可在 Demo 层维护一个**带上限的小型 accumulator**（最多追 1 步，超出即丢弃并接受时间膨胀），但**它绝不驱动 sim / physics 的额外步**——sim 永远每个被执行的 tick 一步。这把「确定性输入时序」的便利限制在廉价的玩法逻辑里，与昂贵且不可插值的 sim 解耦。

---

## 5. 像素模拟内核

这是引擎的心脏。以下设计直接采用 Noita 拓扑，因为这四个选择（64×64 chunk + hash-map、单缓冲原地、per-chunk dirty rect、32px move cap）是**相互强化**的，正是它们共同让全屏 sim 跑到 60fps [高]。

### 5.1 Cell 网格与 chunk 系统

世界是 1 像素 = 1 cell 的均匀网格。cell 按 **64×64 像素**分组为 chunk（talk 中被引用最多的数字）[高]。chunk 存在一个以 chunk 坐标为键的 hash-map 里，世界因此可向任意方向无限扩展，无需固定全局数组。只有玩家附近 / 有活动的 chunk 驻留并被模拟；超出激活半径的 chunk 序列化卸载，重入时反序列化（玩家修改得以持久）。驻留集合的并发安全见 §3.4，常驻上限见 §12.2，磁盘格式见 §11 [中，磁盘格式与半径未公开]。

为什么是 64×64：64 与 32px move cap 构成精确配对（见 §5.6、§5.8）。chunk 太小则 dirty-rect 与调度 overhead 占比高、跨 chunk KeepAlive 频繁；太大则 dirty-rect 粒度粗、sleeping 收益下降。64 是经实战验证的甜点 [高]。我们采用 64×64 并做成编译期常量便于 JIT 优化，保留可调能力以便实测。

### 5.2 更新模型：单缓冲、原地

关键决定：**单缓冲原地更新，不做 double buffer** [高]。理由：double buffer 必须每帧改写整个世界，会毁掉 dirty-rect 优化；单缓冲只需触碰活跃像素。代价是顺序相关性——一个已下落的 cell 可能在后续扫描里被再次访问并移动两次，由 §5.3 的 parity 标记防止。

### 5.3 per-cell「本帧已更新」标记

因为原地更新，必须防止 cell 一帧移动两次。每个 cell 在 Flags 字节里带一个 **frame-parity bit（奇偶时钟位）**，每帧翻转其含义而非清零（省去每帧清扫 200 万 cell 的成本）。扫描时跳过 parity 已等于本帧值的 cell，从而强制「每 cell 每帧至多一次移动 / 反应」[中]。**反应的两个输入与两个输出在反应发生时都打上当前 parity**，这既防止本帧重复处理，也是跨 chunk 反应不被相邻 pass 二次触发的关键（见 §7.4）。

### 5.4 per-chunk dirty rectangle（跳过静止区域）

每个 chunk 持有一个 dirty rectangle = 本帧可能仍需模拟的 cell 的最小 / 最大包围盒。下一帧只迭代该子矩形，于是已沉降的 chunk 几乎零成本，完全沉降的 chunk rect 收缩为空、chunk 进入 sleep。**这是全屏 60fps 的首要原因：绝大多数像素在绝大多数帧里是静止的** [高]。

grow/shrink 机制 [中]：grow——每当一个 cell 被 set/move/react，把 chunk 的 working rect 扩展到包含该 cell 加一个小 padding（社区实现用 ~1–2 cell，做成可调参数），使改动的邻居下帧被重新检查。shrink——每帧从「本帧实际改动的 cell」重建 rect：working rect 累积本帧活动，帧末交换成下帧 current rect。**dirty rect 本身是双缓冲（working/current），即便 cell 是单缓冲。** 若本帧无活动，新 rect 为空，chunk 转入 idle。

### 5.5 跨 chunk 边界唤醒（KeepAlive）

dirty rect 被钳制在 chunk 边界内，所以在 chunk 边缘移动的 cell、或把反应输出写入邻居 chunk 的 cell，必须唤醒邻居，否则活动在缝隙处死掉。当一个被改动的 cell 位于 chunk 边界时，worker 通过 **KeepAlive** 扩展相邻 chunk 的 working dirty rect 以覆盖被触碰的边缘 cell，让雪崩正确跨越边界传播 [中]。**这是整个内核最易出 bug 的区域**，必须有针对性测试（见 §16）。因 border ring 的存在（§3.4），KeepAlive 的目标 chunk 必然驻留。

### 5.6 运动规则与扫描顺序

运动是**数据驱动的贪心局部规则**：每个 cell 持有一个有序候选偏移列表，与第一个有效目标 swap [高]。

粉末（sand）：先试正下方（与下方更轻 / 空 cell swap），不行则试左下、右下，左右选择逐帧交替偏置避免左 / 右倾。粉末在平地**不**水平铺开，这正是休止角堆积的来源。

液体：在粉末规则后追加**水平铺开**（左 / 右，按 dispersion 量），使水找平。压力 / 找平是从水平流动 emergent 出来的，**没有全局压力场**，不是真 Navier-Stokes。不同液体带 viscosity/dispersion 参数控制每帧铺开距离 [高]。

气体：液体的上下翻转——先试上、再上对角、再侧向铺开，带随机抖动使其扩散而非干净柱状 [高]。

solid/static 不参与 CA 移动；fire/energy 由 lifetime 驱动，通过反应传播。

扫描顺序：chunk 内**自底向上**扫描下落，使一柱沙在一遍内自然坍塌。为消除水平漂移偏置，对角与液体铺开的左 / 右选择逐帧交替 / 随机化 [高]。

### 5.7 多线程：4-pass checkerboard over 2×2 chunk grid

**这是最该逐字照抄的杀手级细节** [高]。朴素多线程会在共享 chunk 边界上竞争。解法：把 chunk 当 2×2 super-grid，更新做 **4 遍**；每遍只处理 `(cx%2, cy%2)` 匹配 4 个 parity 之一 `{(0,0),(1,0),(0,1),(1,1)}` 的 chunk。

一遍之内，每个被更新 chunk 与同遍其它被更新 chunk 相隔整整一个 chunk = 64px。一个线程更新它的 64×64 区域，**外加每个基本方向 32px 的 halo**。因为同遍邻居相距 64px、halo 只有 32px，两个线程的写区域永不重叠——**包括边界 cell 与跨界反应写入**——所以无需任何 per-cell 锁。同遍内线程完全独立；4 遍之间用 barrier 分隔。

工作以 per-chunk task 派发到**持久线程池**（不是每帧 Parallel.For——其分区 / 委托开销在 60fps 细粒度工作下浪费）。每遍有效并行度约为 (活跃 chunk 数)/4。风险：活跃 chunk 很少时线程利用率差、barrier 开销可能主导。Mitigation：活跃 chunk 数低于阈值时回退单线程 [中]。

### 5.8 32 像素移动上限（让线程安全成立的不变式）

硬性全局规则：**单次更新中任何像素移动不超过 32 像素** [高]。32px = 半个 chunk，正是保证每个被更新 chunk 周围 32px halo 足够、从而 checkerboard 的 64px 间隔无碰撞的关键。它还限制快速液体 / 爆炸跨世界「瞬移」、稳定 sim。边界写入因此被隐式处理：像素可以跨进 halo，但永远到不了另一个线程的 chunk。**反应只作用于 von Neumann 邻居（距离 1），远在 32px halo 内，故跨界反应写入同样安全（见 §7.4）。**

---

## 6. 确定性

从草稿 §4.9 升格为专章，并补齐草稿缺失的回放 / 回退（undo）决策。

### 6.1 哪些确定、哪些不确定

世界**生成**是种子化 / 确定性的（Herringbone Wang tile, per Sean Barrett），支持种子分享。实时 **sim 默认不是 bit 确定性的**：原地单缓冲 + 多线程 chunk 顺序使竞争同一目标 cell 的解析顺序随线程调度变化，相同输入会发散 [中]。

### 6.2 架构决定：默认非确定性高性能，预留确定性模式 seam

默认采用非确定性高性能模式，但**从 M0 起就把三处变化点抽象为可替换策略**：(1) RNG（每 chunk 可种子化、可切换为纯函数式 counter-based RNG）；(2) cell 争用解析（默认随机 / 调度序，可切换为固定优先级：密度优先 + 固定方位优先级 + 固定 chunk 迭代序）；(3) 扫描顺序（可切换为固定单线程序）。确定性模式额外要求内核内避免依赖平台浮点的非定值运算（热场、粒子积分用定点或固定 round 模式）。代价是部分性能 / 并行度。理由：事后把确定性改造进多线程原地 sim 极难；即便 v1 不启用，预留 seam 成本很低，收益是不把未来（回放 / 种子分享实时态 / lockstep 联机）锁死。

### 6.3 回放与回退（undo/rewind）的明确取舍

草稿未表态，这里定调 [中]：**v1 不做帧级实时回退 / rewind**。理由：单缓冲原地模型没有历史帧，要支持帧级 undo 必须保存每帧 delta 或周期快照，前者在 200 万 cell 规模下内存与拷贝成本高、后者在非确定模型下无法精确重演。我们只提供**粗粒度快照存档**（周期性整世界序列化为「存档点」，可回到该点重载，见 §11），不提供连续 undo。若未来确需 deterministic replay，必须先启用 §6.2 的确定性模式，再以「初态快照 + 输入流」重演（而非保存每帧像素），这条路径因 seam 预留而保持开放。

### 6.4 跨平台确定性的额外约束

即便启用确定性模式，跨机 bit 一致还需：Box2D v3 的多线程解算**不保证跨机确定**（浮点累加顺序随 worker 划分变化），故确定性 / lockstep 场景下 physics 必须单线程（workerCount=1）运行，或将刚体物理排除出确定性边界（仅 CA 确定）。这条约束须与 §14、§15 的线程 / 库选择一并考虑。

---

## 7. 材质 / 反应 / 粒子 / 生命周期

### 7.1 Cell 字节布局（SoA）

采用 **struct-of-arrays**，因为它对单字段遍历（清 flag、扩散热、转色）顺序、预取友好，避免把未用字段拉进 cache line，且是 SIMD 唯一能消费的布局 [高]。

| 数组 | 类型 / cell | 1080p 体积 | 内容 |
|---|---|---|---|
| `Material` | `ushort`（2B） | 4.1 MB | 材质 id（支持 >256；若 ≤256 可用 `byte` 省一半） |
| `Flags` | `byte`（1B） | 2.07 MB | bit0=parity 时钟, bit1=settled/sleep, bit2=burning, bit3=freefalling/有速度, bit4-7=备用（其中 burning 等**持久位**会被存档，parity 等**瞬时位**不存档，见 §11.3） |
| `Lifetime` | `byte`（1B，可选） | 2.07 MB | fire/gas 倒计时 |
| `Temperature` | `Half`/`float`，**1/4 分辨率** | ~0.5 MB（480×270） | 粗热场（TPT CELL=4 技巧） |
| `Render`（独立） | `uint` BGRA8（4B） | 8.3 MB | 每帧从 Material 纹理 + glow 重建，上传 GPU |

热网格状态按 4B/cell 预算（`Material` + `Flags` + `Lifetime`）在 1080p 下约 7.9MiB（8.3MB），加粗温度场约 0.5MB；独立 render buffer 约 7.9MiB（8.3MB），合计约 16.3MiB（17MB）。**关键纪律：渲染颜色不存进 sim cell**；颜色由「材质纹理按世界坐标采样 + 温度 glow」在渲染相位生成，per-cell 零颜色存储 [高]。若需便宜噪声替代纹理，可加 1 字节 `colorVariant`，但必须重新评审 per-cell 预算。

我们采用 SoA 作为 sim 热路径布局；AoS 的 16 字节 `Cell` struct 仅作工具 / 编辑路径备选。

### 7.2 Cell-type 分类

用统一 6 值枚举选择运动规则集 [高]：`Empty, Solid/Static, Powder, Liquid, Gas, Fire/Energy`。这统一了 TPT（5 状态 flag）与 Noita（4 个 cell_type）。Noita 的「沙」不是独立类型而是 `liquid + liquid_sand=1`；`liquid_static=1` 是不流动的类固体液体。我们把 powder 显式化。

### 7.3 材质定义 record

数据驱动，启动期从 JSON 加载进 `MaterialDef[]`，以 id 索引。**每个材质带一个稳定字符串键 `Name`（如 `"water"`），运行时 id 仅在加载期分配；存档 / 热重载靠 Name 而非 id 保持稳定（见 §11.2）。**

```csharp
struct MaterialDef {
    ushort Id;  string Name;   // Name 是稳定键，Id 是运行时分配
    CellType Type;
    byte   Density;          // 邻居 Density < 我 → swap（液 / 气位移）
    byte   Dispersion;       // 液体每步水平铺开距离（viscosity 反量）
    byte   Flammability;
    ushort AutoIgnitionTemp;
    int    FireHp;           // -1 = 永燃
    byte   TemperatureOfFire;
    byte   GeneratesSmoke;
    float  MeltPoint;  ushort MeltTarget;     // 相变阈值 + 目标材质
    float  FreezePoint; ushort FreezeTarget;
    float  BoilPoint;  ushort BoilTarget;
    byte   HeatConduct;      // 每帧传导概率（TPT 约定）
    float  HeatCapacity;     // 必须非零
    ushort DefaultLifetime;
    byte   Durability;
    bool   LiquidStatic; bool LiquidSand;
    int    TextureId;  uint BaseColorBGRA;  byte ColorNoise;
    uint   PropertyFlags;    // 镜像 TPT 位域
    int    ReactionStart; byte ReactionCount;  // 指向 packed Reaction[] 的紧凑区间（见 §7.4）
    AudioCueSet AudioCues;   // 材质化音效钩子（见 §10.2）
}
```

密度约定：单字节 density，液 / 气位移统一用「邻居密度 < 我则 swap」，与 TPT/Noita 行为一致（油浮水沉由此而来）[高]。

### 7.4 反应规则 record 与运行时（cache-aware）

采用 **Noita 全数据驱动反应表**：`A + B 接触 → C + D，概率 p`。作者期用 `[tag]` 语法糖（`[meltable]/[acid]/[fire]/[corrodible]/[cold]/[molten_metal]/[static]/[burnable_fast]`）匹配多材质，**在加载期展开为具体材质对**，运行时零字符串 / 字典开销 [高]。

```csharp
struct Reaction {
    ushort InputA, InputB, OutputA, OutputB;
    byte   Probability; // 0-255；Noita rate 0-100 映射到 0-255
    byte   Flags;       // bit0 fast/双消耗, bit1 directional, bit2 spawn-particle, bit3 emit-heat
}
```

**查表数据结构改用 cache-aware 方案，而非草稿的 `int[N*N]` 大表**（修正：草稿那张表在 N→1000 时达 ~4MB，每邻居随机读对 2M cell/帧是 cache-hostile，且对称对存了两份）[中]：

主路径用**每材质紧凑反应列表**——`MaterialDef` 持有 `ReactionStart/ReactionCount`，指向一个按材质分组、packed 的 `Reaction[]`。绝大多数惰性材质 `ReactionCount==0`，热路径一次比较即早退、根本不触碰反应数据（这是最高频情形）。有反应的材质其列表通常只有几项，线性扫描命中 cache。对反应极多的少数材质（如 `[fire]` 展开后），可对其列表内部按邻居 id 排序并二分，或退化为该材质私有的小 `byte[256]` 直查（仍是 cache-resident 的小表）。**评判依据是实测 cache-miss 率，不是表的字节大小。** 反应只存有序对（按 `min(matA,matB)` 归一），消除对称重复。

**跨 chunk 与边界反应的正确性**（修正草稿缺口）：反应只作用于 von Neumann 邻居（距离 1），写入恒在 32px halo 内，故 checkerboard 下无锁安全（§5.8）。重复触发由 §5.3 的 parity 标记防止：反应发生时给两个输入与两个输出都打当前 parity；相邻 chunk 在后续 pass（有 barrier 保证可见性）扫到同一边界对时，因 parity 已等于本帧值而跳过。**定向反应（directional）与双输出反应在边界尤其要测**：必须保证「谁先扫到谁执行、另一侧因 parity 跳过」，绝不允许两侧各执行一次导致物质凭空翻倍（见 §16 的边界质量守恒测试）。每 cell 每帧至多一次反应以限成本。

datamined 例子作参考：`[lava]+water → rock_static+steam @80`；`[acid]+[corrodible] → acid_gas @50`；`[fire]+[burnable_fast] → [fire]+fire @40`；`[fire]+water → [fire]+steam @20`；`[steam]+[static] → water+[static] @3`（冷凝）；`[molten_metal]+[cold] → metal_sand+[cold] @50`。

**纯温度相变（melt/freeze/boil）不进反应表**，而是放在 MaterialDef 的阈值 + 目标 id，由热场驱动。对表达不了的少数行为（clone、生长、重力井）保留可选的 per-material custom-update 委托钩子。这是 Noita（表）与 TPT（每元素 C++ Update）的混合最优解 [高]。

### 7.5 温度场

按 TPT 模型但更省 [高]：**粗分辨率（1/4，CELL=4）** 的 `Half`/`float` 热场，`HeatConduct` 作每帧传导概率、`HeatCapacity` 加权温变。相变自动：传导后温度越阈值即转 High/Low transition（ICE→WATR→STEAM 免费得到）。热场每帧或每 N 帧跑一遍（最贵的可选子系统，全分辨率每帧会主导预算——务必粗化或降频，并作为 §4.3 第一级降级目标）。

**关键：火传播本身做成普通概率反应**（接触点燃），使「便宜路径」在不开热场时也能工作。默认走 Noita 式廉价接触点燃，把全扩散热场作为可选增强。

### 7.6 grid cell vs free particle

两套系统 + 显式 handshake [高]。**grid cell** 由整数 (x,y) 寻址、属于 CA、相互可碰撞、按扫描更新。**free particle** 是离网格 agent，带浮点位置 + 速度，弹道运动（`x+=vx; y+=vy; vy+=g`），飞行时**不**参与 CA，存于池 / 数组。用于火花、血雾、爆炸碎屑、飞溅。便宜，因为只有（通常几千个）活跃粒子更新。

粒子 struct 裁到 ~20 字节：`float x,y,vx,vy`(16) + `ushort material`(2) + `byte colorVariant`(1) + `byte life`(1)。连续数组 + active-count，死亡 swap-remove，无 per-particle GC 分配。在 `Span<Particle>` 上无虚调用迭代，5万–20万活跃粒子在 C# 60fps 舒适 [中]。

cell↔particle handshake [中]：**cell→particle**（抛射，相位 7）——读材质、网格写 Empty、从池取粒子、拷材质 + 色、按冲量设速。**particle→cell**（沉积，相位 3）——推进、采样下一整数位置；若目标 solid/blocked 或速度≈0 则尝试沉积；目标 Empty 写回材质并释放粒子，被占则按密度位移或挪到相邻空 cell，否则保持短命粒子或杀死。沉积处标 chunk dirty 唤醒 CA。**每粒子硬性 max-lifetime**，否则迷途粒子会泄漏（强制项，见 §19）。

**自由粒子的渲染路径见 §9.3——它们不在 material 网格里，必须显式合成，否则飞行中不可见（修正草稿缺口）。**

---

## 8. 全像素碰撞与刚体

引擎维护两套耦合 sim：per-pixel CA 世界与 Box2D 刚体世界。

### 8.1 静态地形碰撞 vs 动态刚体

两种截然不同的表示 [中]。**静态地形就是像素网格本身**，绝不整体喂给 Box2D（那意味着上百万条边）。角色与 CA 直接与固体像素碰撞；动态 Box2D 刚体与地形碰撞通过：(a) 仅为活跃刚体附近的 dirty chunk 生成**用后即弃的静态 Box2D collider**（局部固体 mask → marching squares → 静态 chain/polygon，随 chunk dirty 重建），或 (b) 更粗的 tilemap collider。只有**动态簇**才走完整的 marching-squares→polygon 刚体。

Box2D 尺度陷阱：1 像素 = 1 Box2D 米会把形状推出 Box2D 调优的 0.1–10m 范围、降低 solver 稳定性。**必须设合理尺度**（1 物理单位 = 8–32 px，或 `b2SetLengthUnitsPerMeter`）[中]。

### 8.2 像素簇 → 刚体的转换管线

确认的管线：**连通分量(CCL) → Marching Squares → Douglas-Peucker → 凸分解 → Box2D 复合刚体** [高]。

CCL：当某区域地形改变（如挖掘切断一块），对受影响固体像素跑连通分量标记（迭代 flood fill，**用显式栈不用递归**防栈溢出，4/8 连通）；任何不接触锚点（世界边界 / 指定静态质量）且小于尺寸上限的连通块成为刚体候选。Noita talk 确认：刚体某像素被毁时重算该刚体形状（若切成多块则多个刚体）——即对其 body-local 像素集重跑 CCL 得 N≥1 个新刚体 [高]。**碎片下限**：连通块小于某像素阈值（如 < 圈定值）的不建刚体，而是直接转为自由粒子 / debris（既符合观感，又遏制 §8.4 的尖刺，见下）。

Marching Squares：沿二值 mask 边界走（16 种 case），产出像素分辨率闭合折线。Douglas-Peucker：以 epsilon（~1–2px 可调）简化，顶点数砍 1–2 个数量级。

凸分解（**修正草稿的「先 ear-clipping 三角化」**）：简化后的（通常凹）多边形必须拆成凸片，因为 Box2D 多边形必须凸且 **≤8 顶点（`B2_MAX_POLYGON_VERTICES = 8`）**。草稿建议起步用三角化（每片 3 顶点），但三角化产生 `verts-2` 个 shape，一个碎裂刚体会变成成百上千个 b2 shape，直接放大 solver 成本与 §8.4 的重建尖刺。**因此直接采用 Hertel-Mehlhorn / Ivan Fratric 的 PolyPartition，生成更少、更大、≤8 顶点的凸片**，并配合上面的碎片像素下限封顶。三角化仅作为「PolyPartition 对退化输入失败时」的健壮回退。

Box2D v3 复合刚体 API [高]：一个 `b2BodyId`(`b2_dynamicBody`) 挂多个 polygon shape（v3 砍掉 v2 的 fixture，shape 直接挂 body）。流程：`b2CreateBody` → 每凸片 `b2ComputeHull(points,count)` 然后 `b2MakePolygon(&hull, radius)`（**绝不手填 `b2Polygon`**；**`radius` 传 0 以得到锐利的像素地形边缘**，非零 radius 会把多边形外扩成圆角，破坏像素贴合——草稿漏了这点）→ `b2CreatePolygonShape(bodyId, &shapeDef, &poly)`。density 驱动自动质量。读回用 `b2Body_GetPosition` + `b2Body_GetRotation`（`b2Rot` 是 cos/sin）。

### 8.3 两套世界栅格化同步

每帧（相位 8）[高]：(1) 在刚体**当前**变换处把其像素从网格擦除（清空，使 CA 不当它是地形）；(2) `b2World_Step(world, dt, subStepCount=4)`（v3 用 sub-step 取代 v2 的分离 vel/pos 迭代数；多线程由我方 task 桥驱动，见 §14.2）；(3) 读回新变换；(4) 按新变换重新栅格化刚体像素，标记每 cell「归刚体 K 所有」。

**旋转栅格化必须用 inverse sampling**：对刚体 AABB 内每个目标 cell，把 cell 中心反变换到 body-local 空间、最近邻采样刚体 mask——保证任意旋转下水密填充。forward 栅格化（正向变换每个源像素）在旋转下留洞 [高]。

**亚像素漂移的正确处理（修正草稿 R6 的自相矛盾）**：草稿 R6 曾建议「不让像素往返穿过网格」，但那会**破坏 §1.2 的双向耦合**——若刚体像素从不写入 CA 网格，CA 就看不到它作地形，沙堆不上去、火烧不到、酸蚀不动、CA 也挖不了它。正确做法是**两者兼得**：把 body-local mask 作为**不可变的权威「形状源」**（始终从它做 inverse-sampling 与碰撞查询、绝不让形状本身被往返侵蚀），但**仍然每帧把瞬时的 owned-by-body-K cell 擦除并重新 stamp 进 CA 网格**以供耦合。漂移由此根除——因为权威形状不参与往返、每帧的 stamp 都是从不可变 mask 经精确 inverse transform 重新生成，而非「拷上一帧的网格像素」。被 CA 挖掉的部分则反映为对 body-local mask 的一次显式破坏事件（相位 8a 的 CCL 重建），而非 mask 的累积侵蚀。

每个 owned 像素逻辑上只存 `{bodyId, localX, localY, material}`，**世界坐标每帧由变换导出**，使破坏时单次 CCL 即可拆分刚体 [高]。

### 8.4 挖掘 / 破坏

刚体丢像素（挖 / 炸 / 烧）时标该刚体 dirty 并重建：取剩余 body-local mask → CCL → 每连通块重跑 marching-squares→DP→PolyPartition → 建**新** Box2D 刚体，**把父体线 / 角速度转移给子体** 使拆分看着物理，销毁旧体 [中]。

形状重建是热点，必须节流（也是 §4.3 过载策略依赖的保证）：**每帧每受影响刚体至多重建一次**（合并多次像素移除）；sleeping 刚体跳过重建直到被扰动；CCL / marching-squares / PolyPartition 尽量 off-thread（在 Core 线程池上、相位 8a 内并行各刚体）；小于像素下限的碎片转粒子而非建体（§8.2）。多簇同时被毁仍可能尖刺（风险见 §19 R4）。

### 8.5 玩家 / 生物碰撞（与刚体分离）

角色**不是** marching-squares 刚体，而是 kinematic/character-controller AABB（或小 bitmap），直接对固体像素场解算 [中]。方法：AABB vs 邻近几何、speculative contacts 防穿透、多次 sub-iteration 把角色推出墙 / 地 / 坡。沿 AABB 边采样固体像素直接得地面 / 墙 / 坡检测。便宜、确定、与 Box2D step 解耦，使移动手感独立于刚体负载。

---

## 9. 渲染

渲染器真实职责很轻 [高]：每帧 blit 一张大的、频繁更新的 RGBA8 纹理（cell 网格）+ 自由粒子合成 + 一个光照 / 后处理 pass。Sim 留在 CPU（匹配 Noita 选择、C#-first 策略、确定性 checkerboard 多线程、直接数组访问的轻量像素碰撞），所以 GPU 层主要是「上传 + 合成 + shader」。

### 9.1 后端选型：Silk.NET（主）+ FNA（备）

**主后端：Silk.NET 2.23.x，目标 OpenGL 3.3 Core**，窗口用 Silk.NET 的 GLFW/SDL [高]。理由：MIT + .NET Foundation 背书；最干净的 reflection-free NativeAOT/trim 故事；对唯一性能关键路径（纹理流式）的完全控制（PBO、persistent-mapped、dynamic texture、compute）；内建升级路径——不换库即可加 GL 4.4 persistent-mapped、GL/Vulkan compute、Vulkan 后端。代价是自写窗口 / 渲染器 / 后处理，但对「单张全屏纹理 + post」是一小块众所周知的代码。

**备选 / batteries-included：FNA**（FNA3D 自动选 D3D11/Vulkan/Metal/GL），被多款商业像素游戏验证——**Celeste（FNA）、Rogue Legacy（XNA→FNA）、FEZ、TowerFall** 等；Stardew Valley 以 MonoGame 为主、部分平台用 FNA。（**修正草稿：Dead Cells 用 Haxe + Heaps，并非 FNA/XNA，已移除。**）代价是 XNA API 隐藏上传路径，对 PBO/persistent-mapping 控制更少。仅当工具生态权重压过 compute 与底层控制时才选 MonoGame 3.8.5（注意主线 MonoGame **无 compute shader**，仅第三方 cpt-max fork 有）。

不推荐 Veldrid（原作者弃坑、社区 fork 维护风险）与 OpenTK 5（v5 仍 prerelease）作长寿引擎主后端。

### 9.2 纹理流式（性能关键路径）

格式匹配是第一杠杆 [高]：用 **BGRA8 + `GL_UNSIGNED_INT_8_8_8_8_REV`**（internal `GL_RGBA8`）/ `DXGI_FORMAT_B8G8R8A8_UNORM`，避免逐像素 swizzle，实测比 `GL_RGB/GL_UNSIGNED_BYTE` 快 **>25x**。**因此 cell 颜色在 CPU 内存里就按 BGRA 存**，上传是直 memcpy。

世界层用**单张视口大小纹理**，dirty-rect `glTexSubImage2D` 子上传（或全帧，本就便宜）——per-chunk 纹理会倍增 bind/draw call 并产生缝。chunking 只用于 CPU sim 网格，不用于 GPU 上传纹理 [高]。

带宽现实 [高]：1080p RGBA8 世界纹理 ~8MB，全帧 60fps 重传 ~480MB/s；720p ~3.7MB(~220MB/s)——对 PCIe（数 GB/s）微不足道。**结论：dirty-chunk 上传是 CPU 侧优化（省 memcpy / cache 压力），不是 GPU 带宽必需**。先发全帧，profiling 显示 CPU 拷贝成本高时再加 dirty-rect。

流式路径分级：起步用全帧 `glTexSubImage2D` 进 **2-PBO ping-pong**（map 前 orphan，异步 DMA）；加 dirty-rect 子上传；把 GL 4.4 persistent-mapped + fence 作为**可选快车道 A/B 测试**（现场报告显示它在 HD 流式上未必更快，PBO 保持可移植默认）[中]。D3D11 路径：`USAGE_DYNAMIC` + `B8G8R8A8_UNORM` + `Map(WRITE_DISCARD)`，DISCARD 每次重写整资源，故 D3D11 偏全帧上传。

### 9.3 自由粒子的合成（修正草稿缺口）

自由粒子不在 material 网格里，相位 9 若只从网格建 render buffer，飞行粒子将不可见。明确方案 [中]：**默认在相位 9 把活跃粒子 stamp 进 render buffer（CPU）**——按 `round(x),round(y)` 写其颜色，使其与像素世界视觉一致、并统一受后续光照 / bloom 影响；**发光粒子（火花、熔渣）同时写入 emissive buffer**，从而正确产生 bloom 辉光。规模上 5万–20万粒子的 CPU stamp 是顺序写、便宜。备选是相位 10 用一遍 GPU point-sprite 批绘（更适合需要亚像素 / 加色混合的高密度火花），届时这批 sprite 也要参与 emissive pass。合成顺序：世界纹理 → 粒子 stamp → 角色 / 刚体高亮 → 光照合成 → bloom → dither → gamma → UI。

### 9.4 光照与后处理

先发 **Noita 式管线** [高]：(1) 从发光材质 + 发光粒子建 emissive additive buffer；(2) 从 cell 网格建 occluder/solidity map；(3) 可见性——最简是 mattdesl 式每光源 1D shadow map 做硬阴影，或 fog-of-war reveal 拿 Noita 观感（光源 punch 洞，fog 是粗字节数组，1 字节 /32×32 区）；(4) additive composite + bloom（bright-pass → mip 降采样 → dual-Kawase/separable Gaussian → additive upsample）。再加 dithering（抗 banding）、gamma、可选 CRT/scanline。

可选高质量模式：**Radiance Cascades**（Sannikov, PoE2 用）做无噪、软、bounced 的 2D GI，作为「fancy lighting」模式，实现为一串 compute/fragment pass [中]。它与 bloom 同属 §4.3 第二级降级目标。

### 9.5 兼容性与 GPU compute

目标 **OpenGL 3.3 Core 为广基线**（覆盖 ~2010+ 几乎所有桌面 GPU/iGPU，Intel Gen9+ 稳），它有 `glTexSubImage2D` + PBO + FBO，够整条管线。**capability-gate** 4.3/4.4 特性（compute、persistent mapping）。对羸弱 / 老驱动或 Intel/Windows 上有问题的 desktop GL，回退 **OpenGL ES 3.0 via ANGLE**（ES3 仍支持 PBO）。**不硬依赖 Vulkan 或 GL 4.x** [高]。

GPU compute sim 评估 [中]：技术可行（block/Margolus CA 规避两 cell 入一格竞争），C# 可用 ComputeSharp（DX12，AOT-safe，Windows-only）或 Silk.NET compute（GL 4.3+/Vulkan，跨平台）。但对 Noita clone 是毒药：(1) 像素级碰撞需网格回 CPU，GPU→CPU readback 卡流水线；(2) 确定性、刚体 / 液压交互在 block CA 里极难表达；(3) 同步成本。**架构决定：CPU sim 权威，仅把光照、粒子、post 可选下放 GPU compute。**

---

## 10. 音频

草稿完全遗漏，此处补齐为一等子系统（交付物是「引擎 + Demo」，且材质化音效是 Noita 观感的一部分）。

### 10.1 子系统选型

**主选：Silk.NET.OpenAL**（OpenAL Soft），与 Silk.NET 技术栈一致、MIT、跨 Win/Linux/Mac、内建 3D positional source 与距离衰减，AOT/trim 友好 [中]。备选：若主渲染走 FNA，则配套 **FAudio**（FNA 生态原生）；或 **miniaudio** 经薄绑定（单文件、零依赖，适合极简打包）。混音与解码由库的音频线程 / 回调线程处理，**不进 sim 热循环**。

### 10.2 材质驱动的事件钩子

sim 不直接播声，而是把**粗粒度音频事件**写进 Core 的事件总线（一个无锁 SPSC / 多生产者 ring buffer），由音频子系统每帧消费。钩点（per-event，绝不 per-cell）[中]：

粒子高速沉积 → impact（按材质 `AudioCues` 与冲击速度选样本 / 调音量）；反应点燃 / 燃烧持续 → fire crackle（聚合为区域 ambient，避免每火 cell 一声）；液体大量铺开 / 飞溅落水 → splash；相位 7 的爆炸 / 冲击 → explosion；刚体破碎（相位 8a）→ shatter；区域内某材质占比高 → 材质化 ambient loop（如熔岩咕嘟、深水低频）。每事件带世界坐标，音频层按相机 / 听者做 positional pan + 衰减。

`MaterialDef.AudioCues` 把材质映射到样本集与参数（音量 / 音高范围 / 冷却时间）。**强制去重 / 限频**：同类事件每帧上限 N 个（如最多 8 个 impact），相近坐标合并，防止满屏沙落时音频过载——这与 sim 性能纪律同理。

### 10.3 与帧节奏的关系

音频事件在 sim 被执行的 tick 产生；sim 降频（§4.2）时事件密度随之降低，听感与画面一致。音频混音在自身线程异步进行，不占 16.6ms 帧预算（仅事件入队是主线程的廉价操作，计入 §1.4 的「游戏逻辑 + 音频派发 ≤1ms」）。

---

## 11. 序列化与存档

草稿把存档与 chunk 流式混为一谈，并埋了「material id 由 JSON 加载序决定 → 改 materials.json 静默损坏所有存档」的隐患。此章独立设计。

### 11.1 两类持久化的区分

**流式（streaming）**：运行时把超出激活半径的 chunk 卸到磁盘、重入时读回，对玩家透明（§3.4、§5.1）。**存档（save）**：玩家显式或周期性把整个世界状态落盘为可重载的存档点（§6.3 的粗粒度快照）。两者共用同一 chunk 二进制格式与同一 material 重映射机制，但存档额外含 world manifest（全局态）。

### 11.2 material id 稳定性（核心修正）

运行时数值 id 由加载序分配，**绝不可入盘**；入盘的一律是稳定字符串键 `Name`。存档头部写一张 **id↔name 表**（保存时所用的 id → name 映射）。读档时：按当前 `MaterialDef[]` 的 name→id 重建一张 `savedId → currentId` 的 remap LUT，对每个 chunk 的 `Material` 数组整体重映射。改 materials.json 顺序、增删材质都不再损坏存档。

缺失材质（存档引用的 name 在当前定义里已删除）走**迁移策略**：映射到一个声明的 fallback（如 `"unknown_solid"` 或 `Empty`），或触发显式 migration 步骤（§11.4）。这是把材质做成稳定字符串键、运行时 id 仅作内部索引的根本理由（§7.3）。

### 11.3 序列化内容清单

每个 chunk 落盘：`Material`（**RLE 压缩**，大片均匀区收益巨大）；`Flags` 的**持久位**（burning 等，**parity / sleep 等瞬时位读档时重置**，避免把调度态当世界态）；`Lifetime`；该 chunk 区域的粗 `Temperature` 子块。世界级（manifest）落盘：世界种子、版本号、当前游戏时间、玩家状态、material id↔name 表、**在飞自由粒子**（位置 / 速度 / 材质 / 寿命）、**刚体**（id、不可变 body-local mask + 每像素材质、当前 transform、线 / 角速度——足以读档后重建 Box2D 刚体并续跑）。

压缩：chunk payload 先 RLE 再过 LZ4 / Deflate（绝大多数 chunk 高度均匀，压缩比很高，直接服务 §12.2 的磁盘增长控制）。

### 11.4 版本化与迁移

存档头含显式 `FormatVersion`（int）。读档时若版本低于当前，按注册的迁移链逐步升级（v1→v2→…）。每个迁移是纯函数 `bytes → bytes` 或结构化转换。material 表的 name 基机制使「新增 / 重排材质」无需迁移；只有改变了某材质语义或字段布局才需迁移步骤。

### 11.5 与流式 / 线程的交互

后台流式线程（相位 11）只做序列化 / 反序列化字节，产出 / 消费游离字节缓冲；live map 的增删与 chunk 对象的接入只在相位 2 单线程发生（§3.4）。整世界存档（玩家触发）应在帧边界（相位 2 或专门的暂停点）对一致快照执行，避免在 sim 中途读取半更新的网格。

---

## 12. 性能策略

### 12.1 内存布局与 per-cell 预算

SoA（§7.1），sim cell 紧到 ~4 字节，独立 RGBA render buffer，总 ~8B/像素。**避免 per-cell float/velocity**，快速物质建模为池化粒子 [中]。

### 12.2 大世界常驻内存上限（修正草稿缺口）

草稿只算了单屏 ~17MB。给出大世界预算 [中]：每常驻 chunk 的 sim 态 = 64×64 × (2B material + 1B flags + 1B lifetime) ≈ **16KB**，加粗温度子块 ~1KB 与元数据，约 **~18–20KB/chunk**。render buffer 是屏幕大小、非 per-chunk，只覆盖可见区。

激活区按 chunk 计：例如 2560×1440 视口 ≈ 40×23 chunk 可见；加模拟边距与 border ring，设激活区 ~128×128 chunk = 16384 chunk × 20KB ≈ **320MB**。据此设**硬上限**（建议常驻世界 ≤512MB，可配置）与**驱逐目标**（LRU，按到相机距离 + 闲置时长）。超上限时即便仍在激活半径内也提前驱逐最远的 sleeping chunk（落盘）。磁盘上已探索区域随游玩增长，靠 §11.3 的 RLE+LZ4 压制（均匀 chunk 压缩率极高）。激活半径、上限、驱逐策略均为可调参数，按目标机内存实测定档。

### 12.3 编译模式：CoreCLR + ReadyToRun 作发行默认（修正草稿的二元选择）

草稿把选择框成「JIT vs NativeAOT」。引入 **ReadyToRun（R2R / crossgen2）作为中间路线** [高]：R2R 把程序集预编译为 native image 以**快启动**，同时**保留运行时 CPU 检测、Tiered Compilation 的 Tier-1 重 JIT 与 Dynamic PGO**——即热的 sim 方法仍会在运行时基于真实 CPU 重编译并 light-up AVX2/AVX-512。这同时拿到「快启动」与「运行时 SIMD light-up + PGO」，优于纯 JIT（启动慢）或 NativeAOT（永远拿不到 Dynamic PGO，且默认 SSE2 baseline 静默砍 SIMD）。

**发行默认：CoreCLR 自包含 + R2R（composite）。** sim 热循环走 Tier-1 重 JIT，跨多样用户 CPU 取最佳 codegen。.NET 10 的 JIT escape analysis（小短命对象栈分配）等改进也只在 JIT/R2R 路径可得。

**NativeAOT 仅作可选次级产物**（为极致启动 / footprint 或受限分发场景），且**必须显式 `<IlcInstructionSet>avx2,bmi2,fma,popcnt,...</IlcInstructionSet>`** 并反汇编验证 ymm/zmm 存在，只对已知硬件发，R2R/JIT 构建作回退（NativeAOT 默认最小 ISA ≈ SSE2，`Vector<T>` 停在 16 字节、AVX2/AVX-512 路径不生成——这是 SIMD-heavy sim 不以 AOT 为默认的最大单一原因 [高]）。

### 12.4 GC 策略（以实测定档，不预设）

**目标：帧循环内零托管分配**，GC 实质从不跑 [高]。手段：object/particle pooling、`stackalloc` 小 scratch（<~1KB，绝不在循环里）、`Span<T>`、`ArrayPool<T>.Shared`、struct enumerator、禁 boxing（热路径无 object 参数、无 LINQ、无捕获闭包）。

GC 模式**不预设**（修正草稿直接钦定 Workstation 的武断）[中]：在 8–16 物理核 + 多 MB pinned 工作集下，万一真触发 GC，**Workstation GC 的并行度远低于 Server GC，单次停顿反而更长**。因此 M0 起就用 BenchmarkDotNet 对比 **Workstation+Concurrent vs Server GC**，两者均配 `GCSettings.LatencyMode = SustainedLowLatency`，按实测的最坏停顿定档（多核大堆下 Server GC 的并行回收常给更短最坏停顿）。关键段可 `GC.TryStartNoGCRegion`。零帧分配下 Gen0 极慢填满，两种模式都几乎不触发——正因如此这是低风险项，但仍以数据而非断言决定，与全文「实测优先」一致。

### 12.5 SIMD：只用在规则网格 pass

**核心 sand/liquid 运动步是数据相关 gather/scatter，几乎无 SIMD 收益，别碰** [高]。SIMD 用在规则、分支统一的 pass：palette→RGBA 转色、热扩散 5-point stencil、流体压力松弛 Jacobi/Gauss-Seidel、bulk fill/clear、dirty flag 扫描 / popcount。起步用可移植 `Vector<T>`，最热 stencil 降到 `System.Runtime.Intrinsics`（Avx2/Avx512F/AVX10.2）+ scalar fallback。典型 SIMD 加速 ~2x。注意 AVX-512/Vector512 在部分 Skylake-X/Cascade/Cooper Lake 可能触发降频净变慢——**gate on `Vector512.IsHardwareAccelerated` 并逐目标实测** [中]。

### 12.6 消除 bounds check 与指针漫游

最内层邻居访问：一次取 `ref T baseRef = ref MemoryMarshal.GetArrayDataReference(arr)`，再 `Unsafe.Add(ref baseRef, offset)` 索引，消除 per-access bounds check 与基址重载；或 `fixed (T* p = arr)`。沿 scanline「漫游」ref/指针避免重算 `y*width+x`。**这是 C# 缩小到 C++ 差距的关键，隔离在干净 API 后、在 chunk 边界而非 per-cell 验证索引** [高]。一个循环里多 span 时 JIT 通常只对一个 elide，其余需 Unsafe.Add。用 `DOTNET_JitDisasm`/Disasmo/Rider 反汇编确认 `RNGCHKFAIL` 消失、ymm/zmm 出现；perf 敏感改动用 BenchmarkDotNet `[DisassemblyDiagnoser]` 守门。

### 12.7 多线程与缩放因子（以实测替代估算）

**持久 worker pool（每物理核一线程）+ 每帧 barrier，不用 per-frame Parallel.For** [中/高]。固定 64×64 chunk 分区，4-phase checkerboard 使相邻 chunk 不同时更新——无锁边界。per-chunk/per-thread 元数据**填充到 64 字节 cache line** 防 false sharing。同一线程池亦服务 Box2D task 桥（§14.2），因相位顺序错开不冲突。

**缩放因子必须实测（修正草稿）**：草稿假设内核「内存带宽约束、~5–6x 有效（sub-linear）」。实际工作集 ~6MB 驻 L3、真实 DRAM 流量仅数 GB/s，**瓶颈是内存延迟（散乱 L2/L3 访问）+ 数据相关 swap 的分支误预测，不是带宽**。延迟约束的代码常随核数近线性缩放（更多 outstanding miss 在飞），仅在共享 L3 争用时才 sub-linear——因此「5–6x」无依据。M2 起用 BenchmarkDotNet 在目标机实测每核加速曲线，据此回填 §1.4 / §12.8 的吞吐目标，**不把估算当设计保证**。

当前 Short 校准中，`CoreScalingBenchmark.ParallelRangeSum(1,048,576 items)` 的 1 / 2 / 4 worker 分别为 303.02us / 230.69us / 162.16us，4 worker 对 1 worker 约 1.87x；这是 JobSystem 调度与同步基准，不等价于 CA full-active 缩放上限。CA full-active 当前仍需 plan/16 针对热循环与调度开销继续优化。

### 12.8 现实 cells/frame 目标

预算 ~16.6ms/帧、~8ms 给 CA。一个分支型原地 sand 更新（邻居读 + 条件 swap + flag 写，**延迟 + 分支约束**）单线程约 ~10–40ns/活跃 cell。按 ~20ns 算，8ms 单线程 ~400K 活跃 cell；多核加速以 §12.7 实测曲线为准（不预设 sub-linear）→ 量级 **~2–4 百万全激活 cell/帧** [中]。1080p ~2.07M cell，全混沌满屏处预算边缘，典型场景大量余量。**数量级估算，必须在目标硬件用 BenchmarkDotNet 确认，不可当设计保证。**

2026-07-02 当前实现校准：`StepJobSystem(FullActiveLiquid)` 约 146ns/active cell（262,144 cells / 38.327ms），明显高于本节估算的 10–40ns/active cell；`TypicalDirtyRect` 为 413.1us，说明小活跃区域路径已可用但调度 overhead 仍显著。后续性能收口以 `CellThroughputBenchmark` 的正式多迭代报告作为 §12.8 达标判据。

2026-07-10 2M 规模校准：`FullActive2M` 平均约 5.98ns/active cell（2,166,784 cells / 12.965ms），使用 8 physical cores 后优于旧小夹具的线性换算，但仍不足以在 8ms 内完成 2M 更新；该差距保留为 PERF-003 与目标硬件证据的未闭合项。

---

## 13. 技术选型（决策表）

| 决策点 | 选定方案 | 理由 | 备选 / 何时改选 |
|---|---|---|---|
| .NET 版本 | **.NET 10 LTS** | 当前 LTS；JIT escape analysis、Vector512/AVX10.2 light-up、`Vector<T>` 自动加宽、Dynamic PGO | 无（题设基线） |
| 编译模式（发行） | **CoreCLR 自包含 + ReadyToRun（R2R composite）** | 快启动 + 运行时 CPU 检测 + Tier-1 重 JIT + Dynamic PGO 同时拿到；sim 热方法仍 light-up AVX2/AVX-512 | NativeAOT 仅作次级产物（极致启动 / 受限分发），**必须显式 IlcInstructionSet**，限已知硬件；纯 JIT 作开发态 |
| 渲染库 | **Silk.NET 2.23.x（OpenGL 3.3 Core 基线）** | MIT + .NET Foundation；最佳 reflection-free AOT/trim；完全控制纹理流式；GL4.4/compute/Vulkan 升级路 | **FNA**（Celeste/Rogue Legacy/FEZ/TowerFall 验证）；MonoGame 仅当工具生态压过 compute |
| 兼容回退 | **OpenGL ES 3.0 via ANGLE** | 覆盖 Intel/老驱动 desktop GL 问题机 | 高端可选 Vulkan 后端 |
| 物理库 | **原生 Box2D v3.1 via HughPH.Box2D** | C 重写、SSE2/AVX/NEON SIMD、graph-coloring solver、>2x v2.4；惯用 .NET + Span API | **ikpil/Box2D.NET（纯托管 v3.1）**——若想让 task 桥用普通托管委托而非 `[UnmanagedCallersOnly]`、或要 AOT/WASM 零 native，可选它（SIMD 吞吐可能逊）；**Hexa.NET.Box2D**（更薄）。**避开** v2 时代 Aether/Velcro |
| 物理多线程 | **自建 task-callback 桥，把 Box2D 并行 for 派发到 Core 线程池** | Box2D v3 不自开线程、不捆 enkiTS（见 §14.2） | 单线程（workerCount=1）——确定性 / lockstep 模式必需（§6.4） |
| 数学 / SIMD | **System.Numerics + System.Runtime.Intrinsics** | 内建、可移植 `Vector<T>` + 必要处 Avx2/Avx512/AVX10.2 + scalar fallback | 无第三方需要 |
| ECS 与否 | **不用通用 ECS** | sim 是 SoA 网格热循环，ECS 的 archetype/查询抽象在 per-cell 尺度是负担、妨碍 bounds-check 消除与 cache 布局控制 | Demo 少量实体可用轻量手写组件数组或 friflo/Arch，**绝不进 sim 内核** |
| 音频 | **Silk.NET.OpenAL（OpenAL Soft）** | 与 Silk.NET 栈一致、跨平台、内建 positional source、AOT 友好 | FAudio（若用 FNA）；miniaudio（极简打包） |
| 互操作属性 | **`[LibraryImport]`（source-gen），禁新 DllImport** | AOT/trim-safe、可内联、编译期诊断；blittable 签名性能等同 DllImport | 仅生成器表达不了的签名才回退 |
| 构建 / 发行 | **CoreCLR 自包含 + R2R（默认）；NativeAOT 仅 CI per-RID 次级** | 快迭代 + 广兼容；AOT 强制 native 库 dual-build 与固定 ISA | 6 RID |
| 内存（跨界缓冲） | **POH（`GC.AllocateArray<T>(n, pinned:true)`）或 `NativeMemory.Alloc`** | 零拷贝双缓冲于 sim/physics/renderer，无 per-call pin、不碎片化 | 短期数组 per-call pin（不跨帧时） |
| 序列化 | **自研 chunk 二进制（RLE+LZ4）+ name 基 material 重映射 + 版本迁移** | 控制格式、稳定 id、压缩大世界（§11） | — |

### 13.1 关于 ECS 的明确立场

不引入通用 ECS 作 sim 架构。Noita 式 sim 的本质是「200 万个同构 cell 的 SoA 数组上的紧致热循环」，通用 ECS 的实体身份、archetype 迁移、查询调度在 per-cell 尺度是纯开销，且其内存布局抽象妨碍 bounds-check 消除与精确 cache 控制。这是数据导向设计（DOD），不是 ECS。Demo 的稀疏实体（玩家、敌人、物品）数量小、行为异构，可用轻量手写组件数组，确需可用 friflo.Engine.ECS 或 Arch，但严格隔离在 Demo 层、绝不进 sim 内核。

---

## 14. C# vs C++ 边界

边界判定的硬数学 [高]：blittable `[LibraryImport]` per-call 开销 <10ns（GC 模式转换几 ns + 一个栈帧），帧预算 16,666,000ns——任何「每帧一次」的 native 子系统互操作开销是噪声。陷阱是 **per-pixel/per-cell 跨界**，所以核心 CA 热循环必须整个待在**一侧**，且那一侧是 **C#**。

### 14.1 CA 内核留 C#（不下沉 C++）

判断 [中]：C# 手写 SIMD 经 Intrinsics 达 C++ parity 到 ~30%；而 sand 更新是顺序的、**延迟 + 分支约束、非 ALU 约束**，SIMD/语言收益有限；真正 8–16x 杠杆是多线程独立 chunk（checkerboard），C# 原生胜任。把内核重写 C++ 只换个位数百分比收益、却付巨大维护 / 移植代价。**结论：CA 内核、粒子积分、反应 pass 留 C#。** C++ 仅作最后手段——若 profiling 证明 per-pixel 温度 / 空气扩散 pass 是瓶颈才考虑那一小块。

### 14.2 唯一强 native 案例：Box2D v3（含必须自建的 task 桥）

刚体物理是 native 真正挣到价值处 [高]：Box2D v3 是手调 C + SIMD（x64 SSE2/AVX、arm64 NEON）+ graph-coloring solver-island 并行，设计上被绑定而非移植。

**关键修正：Box2D v3 自己不开线程、也不捆 enkiTS。** enkiTS 只出现在 Box2D 的 sample app。库通过 `b2WorldDef` 上的用户回调 `enqueueTask` / `finishTask` + `workerCount` 来并行；若回调返回 NULL，则**完全串行**运行。因此草稿「`b2World_Step` 内部多线程(enkiTS)」是错的，要拿到引用的多线程数字（5050 体 / 14950 接触 / 4 worker：AVX2 0.90ms、SSE2 1.02ms、scalar 1.91ms），**必须自建一个托管 task-system 桥**：

桥的形态——用 `[UnmanagedCallersOnly]` 实现 `enqueueTask`/`finishTask` 两个 static 回调，注入 `b2WorldDef`，`workerCount` 设为 Core 线程池的 worker 数。`enqueueTask` 收到 Box2D 的 `b2TaskCallback`（一个 `delegate* unmanaged` 函数指针）、`itemCount`、`minRange`，把 `[0,itemCount)` 切成 ≥`minRange` 的区间，派发到 **Core 的持久线程池**（与 CA 共用，因相位顺序错开不冲突），每个 worker 以稳定的 `workerIndex∈[0,workerCount)` 经 IL `calli` 回调 `task(start,end,workerIndex,ctx)`；`finishTask` 等待这批完成。起步可用**同步实现**（`enqueueTask` 内即在池上 fork-join 并阻塞到完成、返回哑句柄，`finishTask` 为 no-op），正确且简单；后续若要重叠可改真正异步 join。

**这些回调每个 `Step` 触发多次（按 island / color 分区），且每次都重入托管代码——因此绝不能用 `[SuppressGCTransition]`**（这恰恰是 §14.3 「callback 保持粗粒度、per-event 不 per-pixel」原则的一个明确例外：这里是 per-partition、频繁、且会进托管，必须走正常 GC 转换）。这套桥是 native 互操作真正的复杂度所在，远超「`b2World_Step` 本身」。

（备选：若选 ikpil 纯托管 Box2D.NET，task 系统就是普通托管委托、无需 `[UnmanagedCallersOnly]`，桥更简单，代价是可能损失 native SIMD 吞吐——这是选型权衡，见 §13。）

确定性 / lockstep 模式下，task 桥设 `workerCount=1` 串行运行以求跨机一致（§6.4）。

### 14.3 互操作纪律

`[LibraryImport]` 全局唯一，禁新 `DllImport`，启用 `<AllowUnsafeBlocks>`，blittable-only 签名作硬规则。`[SuppressGCTransition]` 仅留给已证实的 **<1μs、非阻塞、无回调、不抛异常的叶子调用**（~4x 加速到 ~1–2ns）；误用在阻塞 / 长调用上会 ~2x 退化并 stall 全部 GC——**绝不用于 `b2World_Step`、也绝不用于 §14.2 的 enqueue/finish 回调**。反向回调（Box2D contact/ray/task callback）用 `[UnmanagedCallersOnly]` static + blittable + 不抛，经 `delegate* unmanaged<>`（IL `calli`）调用。零拷贝：跨界缓冲走 POH 或 NativeMemory，double-buffer 于 sim/physics/renderer。

### 14.4 native 构建二元性

NativeAOT 静态链 native 库经 MSBuild `<NativeLibrary Include>`（`.lib`/`.a`），RID-gated；CoreCLR 开发与 R2R 发行需**动态**库（`.dll`/`.so`/`.dylib`）。**因此每个 native 依赖要 dual-build（动态 + 静态）× 6 RID**——这是「works in debug, crashes in publish」bug 的温床（风险见 §19）。Linux 上别静态链 libc，动态链 glibc。把 native surface 收敛到**仅 Box2D 一个依赖**（OpenAL/ANGLE 走系统 / 动态分发）本身就是风控措施。

---

## 15. 兼容性与可移植性

Windows 为主，理想覆盖 Linux/Mac。目标 6 RID：`win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64`。

渲染兼容靠 OpenGL 3.3 Core 广基线 + capability-gate 4.3/4.4 + ANGLE(ES 3.0) 回退（§9.5），不硬依赖 Vulkan/GL 4.x。CPU SIMD 兼容靠 **CoreCLR+R2R 的运行时检测 + Tier-1 重 JIT**（§12.3）而非固定 ISA，这是「广兼容」与「极致性能」调和的核心。

native 资产打包：框架依赖 / 自包含发行把 native 放 `runtimes/<rid>/native/`，host 自动拷正确二进制；AOT 发 per-RID 自包含。macOS arm64 额外需 codesign + notarization。Silk.NET、Box2D、OpenAL 绑定均捆 Win/Linux/Mac native，单一 native 依赖（Box2D）的 fan-out 是主要可维护性论据。

确定性可移植：Box2D v3 多线程解算**不保证跨机 bit 确定**；若要 lockstep/replay，physics 须 `workerCount=1` 或排除出确定性边界，与 §6 的确定性模式一并处理。

---

## 16. 项目结构与工程化

### 16.1 Solution / project 布局

```
PixelEngine.sln
├─ src/
│  ├─ PixelEngine.Core/          数学/内存(POH,NativeMemory)/线程池/RNG/事件总线/诊断
│  ├─ PixelEngine.Simulation/    CA 内核:CellGrid(SoA)/Chunk/dirtyrect/checkerboard/
│  │                             movement/material/reaction/temperature/particles
│  ├─ PixelEngine.Physics/       CCL/marchingsquares/DP/PolyPartition/Box2D 桥+task 桥/
│  │                             两世界同步
│  ├─ PixelEngine.Rendering/     Silk.NET 封装/窗口/纹理流式/粒子合成/光照/bloom/post
│  ├─ PixelEngine.Audio/         OpenAL 封装/positional source 池/事件驱动材质音效
│  ├─ PixelEngine.Content/       MaterialDef/Reaction 加载/tag 展开/name↔id 表/材质纹理
│  ├─ PixelEngine.World/         chunk hashmap 驻留/border ring/流式装卸/激活半径+内存上限
│  ├─ PixelEngine.Serialization/ chunk 二进制(RLE+LZ4)/world manifest/版本迁移/id 重映射
│  └─ PixelEngine.Interop/       [LibraryImport] Box2D v3 绑定+[UnmanagedCallersOnly]task 桥
├─ demo/
│  └─ PixelEngine.Demo/          玩法:玩家控制器/输入/UI/关卡内容
├─ tests/
│  ├─ PixelEngine.Simulation.Tests/
│  ├─ PixelEngine.Physics.Tests/
│  └─ PixelEngine.Serialization.Tests/
├─ bench/
│  └─ PixelEngine.Benchmarks/    BenchmarkDotNet
├─ content/                       materials.json, reactions.json, 材质纹理, 音效
└─ native/                        Box2D dual-build 脚本与产物(per-RID)
```

引擎绝不依赖 Demo。Interop 单独成 assembly 隔离 unsafe/native surface。

### 16.2 测试策略

CA 内核最易出 bug 处是 **chunk 边界 KeepAlive、单缓冲 parity、跨界反应** [高]，测试针对性：

质量守恒性质测试（无反应时 cell 总数不变，跨 chunk 边界亦然——捕获边界吞 / 复制像素 bug）；**反应质量守恒（带 §7.4 的双输出 / 定向反应在 chunk 边界不翻倍 / 不丢失）**；确定性回归（固定单线程 + 固定 RNG，已知初态 → 已知终态快照）；movement 规则单测（单柱沙一帧坍塌、水找平、油浮水、气上升）；反应表测试（tag 展开正确、概率边界、name↔id 重映射正确）；凸分解测试（PolyPartition 每片 ≤8 顶点且凸、覆盖原 mask、`radius=0`）；inverse-sampling 栅格化水密性（旋转任意角无洞）；**存档往返测试（save→load 后世界逐 cell 等价；改 materials.json 顺序 / 增删材质后旧档仍正确重映射；版本迁移链）**；**流式线程安全（KeepAlive 进正在装 / 卸的边界 chunk 无竞争，靠 §3.4 屏障）**。multithreaded sim 用「单线程结果作 oracle 比对统计性质」而非 bit 比对（因非确定）。

### 16.3 内容管线

materials/reactions 用 JSON（开发友好、可加 schema 校验）在启动期加载进扁平索引表，tag 在加载期展开为具体材质对，**name→id 在此分配并落入运行时表**。材质纹理与音效作普通资产启动期载入。无需重型 content pipeline（不选 MonoGame 的次要理由）。热重载见 §17.2。

---

## 17. 调试与可视化工具

从草稿 §12.3 扩展为专章——sim 的不可见态（dirty rect、parity、活跃 chunk、owned-by-body）必须可视化，否则边界 bug 几乎不可调试。

### 17.1 帧内分项计时与 overlay

常驻 debug overlay 显示每相位耗时（particle/CA pass A–D/heat/physics step/形状重建/render/upload/audio 派发），活跃 chunk 数、活跃 cell 数、自由粒子数、刚体数、常驻 chunk 数与内存占用、当前 sim 频率（60/30Hz，§4.2）。这是 §4.3 过载降级与 §12 调优的眼睛。

### 17.2 sim 可视化叠层（可切换）

按键切换叠层：dirty rect 边框（看 sleeping/active）、chunk 网格与 parity 着色（看 4-pass 分区）、KeepAlive 唤醒热点、cell parity 位、温度热力图、owned-by-body-K 着色（看刚体往返 stamp 是否正确）、自由粒子轨迹、CCL 连通块着色。这些叠层直接命中 §2 列出的最易错区。

### 17.3 反汇编与微基准守门

`DOTNET_JitDisasm`/Disasmo/Rider 反汇编确认 bounds check 消失、SIMD 寄存器出现。BenchmarkDotNet `[DisassemblyDiagnoser]` 守门 perf 敏感改动。在所有 6 RID 代表硬件上跑 cells/frame 与每核加速曲线基准，把 §12.7/§12.8 的估算落实为实测。

### 17.4 数据热重载

materials/reactions JSON 支持运行时热重载以利调参 [中]。热重载纪律：**id 稳定**——重载只做增量 / 稳定分配（保留既有 name→id，新增材质追加新 id，删除材质保留 id 作 tombstone 或重映射活 cell 到 fallback），绝不重排 id，否则会损坏 live 网格里引用旧 id 的 cell（与 §11.2 同理）。改变语义 / 概率 / 反应表整体重建，下一帧生效；改变材质纹理 / 音效重新加载资产。引擎给出「重载后用 fallback 替换了 N 个被删材质的活 cell」的诊断输出。

### 17.5 外部编辑器自动化公共 API

PixelEngine Editor 必须提供与 UI 同权威、可版本协商的本地自动化控制面 [高]。Codex、Claude Code、普通脚本和 CI 只依赖公开协议、.NET Client 或 CLI，不依赖 MCP、Computer Use、OCR、屏幕坐标、ImGui 内部结构或测试专用入口。自动化只能调用与菜单、快捷键、工具栏、面板及上下文操作共用的语义 command；禁止直接改 UI 私有字段、绕过 dirty guard、另建影子场景/选择/设置状态或把 scripted probe 伪装成公共 API。

**传输与发现。** 协议采用有长度上限的二进制 frame header + UTF-8 JSON envelope，major 不兼容时拒绝连接，minor 通过 hello/capability negotiation 向下协商；控制消息不携带截图、纹理、profile trace、场景导出等大块字节。Windows 实现异步 Named Pipe，并用 current-user ACL 与 `PipeOptions.CurrentUserOnly` 限制边界；wire schema 为 Unix Domain Socket 预留稳定 transport kind 和 endpoint 字段，但实例只能宣告真实可用的 transport，不得发布空壳 endpoint。每个 Editor 进程生成不可复用的 instance id，在原子 discovery descriptor 中登记 PID、process start identity、Editor/协议版本、项目/场景摘要、endpoint、capability digest、credential 路径和心跳/过期信息；客户端必须验证进程身份与握手，忽略或安全清理 stale descriptor。默认 discovery/credential 目录只允许当前用户访问，测试和 CI 可显式传入隔离根目录。

**认证、授权与安全。** Named Pipe ACL 不是唯一认证。实例生成高熵 bearer secret，secret 仅写入 current-user credential 文件或由调用方通过文件注入，握手使用随机 challenge 的 HMAC-SHA256，禁止把 secret 放进进程命令行、日志、descriptor、错误或 capability 输出。会话得到显式 scope，例如 `editor.read`、`editor.control`、`project.write`、`settings.write`、`process.build`、`process.launch` 与 `automation.admin`；每个 capability 声明最小 scope，服务端在排队前和执行前都校验。所有路径输入先 canonicalize，再限制在项目、声明的导入源、build root 或会话 artifact root；符号链接/reparse-point 逃逸、UNC/设备路径、参数注入和任意进程启动必须拒绝或要求专门授权。审计日志记录 session、principal、request/correlation id、capability、结果、耗时和 revision，但不记录 secret 与敏感字段。

**消息、错误与流控。** request/response/event envelope 包含 protocol version、request id、correlation id、method/capability id、deadline、session 与 payload schema version。每个请求支持 deadline/timeout 和显式 cancel；取消必须能移除尚未执行的主线程 work item，正在执行的可取消工作在安全边界停止，已经跨过不可逆点则返回带结果状态的结构化错误，绝不谎报取消成功。错误统一包含稳定 code/category、可本地化 message、machine-readable details、transient、retry-after、current revision 与 correlation id。连接和会话有独立的有界请求数、frame 大小、事件 backlog、artifact quota 与并发 build/run 限额；慢消费者触发明确 backpressure/overflow，不允许无界缓存。

**稳定身份、分页与 revision。** instance、project、scene asset、GameObject、component、asset/folder、panel/window、console entry、build、player process、play session、runtime entity、transaction、subscription 与 artifact 均有稳定或明确作用域的 id；显示名称和数组下标不能充当身份。集合读取使用结构化 filter/sort/page size 与 opaque cursor；cursor 绑定 filter digest、snapshot revision 和有限租期，跨 revision 使用返回 `stale_cursor`。所有权威资源暴露 resource revision，写请求支持 `ifRevision`/`ifSceneRevision` 乐观并发；冲突返回当前 revision 与可重新读取的资源 id，不能 last-write-wins 静默覆盖。

**订阅、重连与制品。** 客户端可订阅状态、选择、层级、Inspector、资产、Console、Play、runtime、Profiler、build、layout 和 artifact 事件；事件具有 subscription id、单调 sequence、state revision 与因果 request id。服务端保存有界 replay window，断线后凭 resume token 和最后确认 sequence 续订；窗口已淘汰时返回 `resync_required`，客户端必须重新抓取 snapshot。截图、预览、profile、日志包、场景/能力导出和其他大型数据由 worker 写入会话隔离的原子制品文件，响应只返回 canonical path、media type、byte length、SHA256、created time、source revision 和可选尺寸/编码元数据；读取或清理制品仍受 scope、配额和路径边界约束。

**主线程、Engine phase 与事务。** Pipe accept/read/write、JSON parse/serialize、hash 和文件编码在后台异步执行；任何 Editor/ImGui/GL/authoring/Engine 权威对象只允许在 Editor 主线程或声明的 Engine phase 访问。I/O 线程把有界 work item 投递到显式 scheduler 并唤醒窗口；Editor 在固定 automation ingress safe-point 执行 authoring/UI 命令，Engine 数据在 Input/Simulation/Physics/Render 等 capability 声明的 phase 读取或变更。只读结果从安全点复制为不可变 snapshot 后再离线程序列化。自动化空闲时不得使用 timer/socket/frame 扫描轮询，不创建 work item、集合、闭包或日志字符串，稳态每帧托管分配必须为 0。

会话支持 begin/commit/rollback、租期、幂等 key 和 disconnect rollback。可逆 authoring/settings/asset 操作可加入事务，commit 在主线程一次校验全部 precondition 后原子应用，并作为一个 Editor Undo group；失败或掉线恢复磁盘与内存 before image、selection、dirty state 和 revision。Play/Stop、外部进程启动、build 与其他不可逆或长任务明确标为 transaction-forbidden，不能假装原子。`undo`/`redo` 与 UI 共用唯一 `EditorUndoStack` 和命令历史，事件与 revision 在提交后发布，事务中间态不对其他会话或 UI 可见。

独立 CLI 的一次启动对应一条认证连接，进程退出即触发该 session 的 disconnect rollback；禁止文档或 Skill 引导用户用多个 CLI 进程拼接 begin/stage/commit。CLI 必须提供单进程、单连接的有界 transaction plan 命令，在同一 session 内完成 capability 校验、begin、按序 staging、commit 与失败终态查询/rollback；只有保持同一连接的 .NET Client 才能手动控制 transaction 生命周期。commit 返回失败后客户端先查询权威终态，仅 `Active` 可再次 rollback，已经 `RolledBack`/`Expired`/`Committed` 时不得重复执行或谎报恢复失败。

**能力闭包。** 仓库保存版本化、机器可读 capability matrix 与 JSON Schema。矩阵至少覆盖：实例/项目发现；窗口、panel、focus、dock/undock、layout；Scene 新建/打开/保存/另存/关闭、选择与 Scene/Game capture；Hierarchy/GameObject；Inspector/Transform/组件与字段 schema；Project/folder/asset/import/move/rename/delete/reference/preview；Console；Play/Pause/Step/Stop；runtime entity/component/world/debug data；Canvas/CanvasScaler 与 Game View presentation；Scene tool/gizmo/grid/snap/brush/stroke；Preferences、Project/Player/Build Settings；Profiler/debug overlay；build/preflight/cancel/status/result；产物启动、等待和终止。每个可见 menu item、shortcut、panel action 和 context action 必须反向映射到 capability/command id、请求/响应 schema、权限、revision、事务模式与执行 phase；验证器发现未映射 UI 操作、仅测试实现、空 handler 或 capability 宣称大于真实实现时必须失败。

交付程序集边界为低依赖 protocol/schema、可复用 Server core、公开 .NET Client、Editor Shell semantic adapter 与独立 CLI；player 包不得包含 Server 或 Editor automation 闭包。CLI 默认输出紧凑、确定、可管道消费的文本，同时提供 JSON/NDJSON、事件 follow、deadline/cancel、artifact path/hash 校验。clean final-output 必须同时包含发布的 Editor、CLI、Protocol/Client NuGet、wire/capability Schema、canonical matrix、开发者文档和经验证 Skill，并保存逐 CLI 进程 PID/退出码/日志 SHA256 的 E2E 报告；独立 verifier 不信任 manifest 自报，必须重算 package entry、Skill 精确文件集、矩阵闭包/digest、Editor/CLI identity、operation 日志与根级 SHA256。最终产品证据必须由 clean worktree 构建的全新 Editor 和全新外部 CLI 进程，在隔离 user-data/discovery/artifact root 下只通过公共 API 完成“编辑场景→运行→调试→停止→再次运行→修改→构建→启动产物”，并闭合 schema compatibility、权限、路径逃逸、并发 revision、事务/Undo、事件重连、超时取消、idle 零轮询/零分配与 clean final-output 审计。

---

## 18. 路线图 / 里程碑

采用 **vertical-slice-first** 排序：尽早让「沙能下落并渲染到屏幕」端到端跑通，再逐层加厚。

| 里程碑 | 主题 | 具体交付物 | 验收 |
|---|---|---|---|
| **M0** | 骨架 + 垂直切片 + 帧节奏 | Silk.NET 开窗；单 chunk SoA 网格；沙 / 空两材质；单线程原地 bottom-up + parity；BGRA 全帧 PBO 上传；鼠标画沙；**固定逻辑步长 + 时间膨胀的帧循环骨架（§4，无追帧 accumulator）**；帧计时 overlay | 屏上画沙下落堆休止角，稳定 60fps；过载只降帧不 death spiral |
| **M1** | chunk + dirty-rect | 64×64 chunk hash-map；per-chunk dirty rect(working/current)；sleeping chunk；KeepAlive；border ring 雏形；质量守恒性质测试；dirty-rect 叠层 | 静止区零成本；多 chunk 雪崩跨边界正确传播 |
| **M2** | 多线程内核 | 持久线程池 + barrier；4-pass checkerboard；32px move cap；false-sharing padding；活跃 chunk 少时单线程回退；**每核加速曲线实测（§12.7）** | 实测缩放达标（以数据为准）；无边界竞争损坏 |
| **M3** | 材质 / 液体 / 气体 / 反应 | 数据驱动 MaterialDef + 紧凑反应列表(JSON + tag 展开 + name↔id 表)；liquid/gas/powder 规则；密度位移；接触式火传播；跨界 / 双输出反应正确性测试；材质纹理上色；热重载 | 水找平 / 油浮水 / 气上升 / 火烧 / 熔岩遇水成石；改 JSON 顺序不损坏；边界反应不翻倍 |
| **M4** | 粒子 + 生命周期 + 合成 | free-particle 池(~20B,swap-remove)；cell↔particle handshake；max-lifetime；爆炸抛射；**粒子 render 合成 + emissive(§9.3)** | 爆炸碎屑划弧、落定回沉积、发光粒子 bloom，无泄漏、飞行可见 |
| **M5** | 温度场（可选增强） | 1/4 分辨率 Half 热场；HeatConduct 概率传导；melt/freeze/boil 阈值相变；SIMD stencil；作为 §4.3 一级降级 | ICE→WATR→STEAM 链；不回退 60fps；重载降级生效 |
| **M6** | 像素碰撞 + 刚体 | 玩家 kinematic AABB vs 像素；CCL→marchingsquares→DP→**PolyPartition(radius=0)**→Box2D v3 复合体；**Box2D task 桥(§14.2)**；两世界 erase/step/inverse-rasterize + 不可变 mask 权威(§8.3)；破坏重建 + 速度转移 + 碎片下限 + 节流 | 挖断块掉落成刚体、可旋转、再被毁拆分；沙能堆刚体上、火能烧刚体（双向耦合）；无亚像素侵蚀；多核物理生效 |
| **M7** | 光照 + 后处理 | emissive + fog-of-war + bloom + dither + gamma；dirty-rect 子上传；作为 §4.3 二级降级 | Noita 式观感；渲染 ≤4ms |
| **M8** | 音频 | OpenAL 子系统 + positional source 池；sim 事件总线；材质化 impact/fire/splash/explosion/ambient；限频去重 | 材质化音效随事件播放、定位正确、不过载 |
| **M9** | 存档 + 流式 + 打包 | chunk 二进制(RLE+LZ4) + world manifest + 版本迁移 + id 重映射；流式装卸 + border ring + 内存上限 LRU；6-RID CI；Box2D dual-build；R2R 发行 + NativeAOT 次级；macOS codesign | 无限世界持久 + 整世界存档往返正确；常驻内存守上限；Win 主 + Linux/Mac 跑通 |
| **M10** | Demo 整合 + 调优 | 一个可玩关卡；全套 debug 叠层(§17)；目标硬件基准落实；GC 模式实测定档(§12.4)；确定性模式开关(若需,§6) | 满足 §1.4 全部成功度量 |

排序原则：M0–M2 先夯「性能 + 帧节奏地基」（chunk/dirty-rect/多线程/时间膨胀），它们决定一切；M3–M5 加「世界丰富度」；M6 是「每像素可碰撞」的另一半，刻意放在 sim 稳定之后；M7–M8 是观感 / 听感；M9 是持久化与发行。可选项（M5 温度全场、M7 Radiance Cascades）明确标记可裁剪。

---

## 19. 风险与未决问题

### 19.1 主要风险与缓解

**R1 — C# 热循环爆预算（最高风险）** [中]。SoA/unsafe/Span/池化是强制纪律；全屏混沌液体逼近 16.6ms。缓解：M0 即建 BenchmarkDotNet 基准，反汇编确认 bounds check 消除，确保 dirty-rect 真生效；**瓶颈按延迟 + 分支而非带宽分析（§12.7）**；预留「per-pixel 扩散 pass 下沉 C++」作最后手段，内核主体不动。

**R2 — chunk 边界 KeepAlive / 单缓冲 parity / 跨界反应 bug** [高]。表现为缝隙处像素消失 / 复制 / 抖动、边界反应物质翻倍。缓解：质量守恒 + 反应守恒性质测试 + 单线程 oracle 比对 + 边界专项测试 + parity 标记防重复（§5.3、§7.4）；KeepAlive 逻辑最小化、隔离、重测；§17.2 叠层辅助。

**R3 — AOT SSE2 退化静默砍 SIMD** [高]。缓解：发行走 CoreCLR+R2R（运行时 light-up）；若发 AOT 必须显式 IlcInstructionSet 并反汇编验证 ymm/zmm，限已知硬件。

**R4 — 形状重建帧尖刺** [中]。多刚体同时被毁时 marchingsquares+DP+PolyPartition+建体爆帧。缓解：每帧每刚体至多一次、合并像素移除、sleeping 跳过、off-thread、碎片下限转粒子、池化 body/shape；与 §4.3 过载降级联动。

**R5 — native dual-build 不匹配（debug 正常 publish 崩）** [高]。缓解：CI 同时验证 CoreCLR/R2R（动态）与 AOT（静态）路径；Box2D 作唯一 native 依赖限制 fan-out；Linux 动态链 glibc。

**R6 — 旋转栅格化亚像素漂移** [中]。缓解：**body-local mask 作不可变权威形状源、每帧从它 inverse-sample 重新 stamp，而非拷上一帧网格像素**；仍保留 erase/re-stamp 往返以维持双向耦合（§8.3，已修正草稿的错误缓解）。

**R7 — 多线程少活跃 chunk 时利用率差 + barrier 主导** [中]。缓解：活跃 chunk 低于阈值回退单线程。

**R8 — 非确定性堵死回放 / 联机** [中]。缓解：M0 起把 RNG/争用解析 / 扫描顺序抽象为可替换策略，预留确定性 seam（§6.2）；physics 确定性需 workerCount=1（§6.4）。

**R9 — Box2D 尺度失配静默降稳定性** [中]。缓解：设 1 物理单位 = 8–32px 或 `b2SetLengthUnitsPerMeter`，subStep=4，`b2MakePolygon(radius=0)`，建库时即定尺度约定。

**R10 — 库命名冲突** [高，易避]。两个 "Box2D.NET"（ikpil 纯托管 vs BeanCheeseBurrito 生成绑定）。缓解：csproj 锁定确切 package id + 仓库。

**R11 — fake pressure 流体局限** [中]。深水柱找平慢、U 型管失败。模型固有可接受；如需更好流体，局部加压力松弛 pass（SIMD 化）而非全局求解器。

**R12 — 反应表规模与 cache** [中]。缓解：紧凑 per-material 列表 + 惰性材质早退 + 有序对去重（§7.4），按实测 cache-miss 而非字节数定档；material id 用 ushort 留头。

**R13 — 自由粒子泄漏** [中]。缓解：硬性 max-lifetime + 「无处沉积则杀死」回退，强制项。

**R14 — Box2D task 桥写错（漏多线程 / GC stall / workerIndex 复用）** [高，新增]。缓解：起步用同步 fork-join 桥（§14.2）求正确；回调禁 `[SuppressGCTransition]`；分配稳定 workerIndex；先验证串行 (workerCount=1) 正确再开多线程；用 §17.1 计时确认 physics 真并行。

**R15 — 存档 material id 漂移损坏旧档** [高，新增]。缓解：入盘只用 name、存 id↔name 表、读档 remap、缺失材质 fallback（§11.2）；存档往返测试纳入 CI（§16.2）。

**R16 — 流式与 sim 的 map 竞争** [中，新增]。缓解：结构性增删只在帧边界单线程相位、后台仅做 I/O 字节、border ring 兜底跨界写入（§3.4）。

### 19.2 未决问题（需 owner 拍板或实测）

内部模拟分辨率：真 1080p sim 像素，还是 Noita 那样低分辨率世界放大？直接定 cells/frame 预算与内存，最高优先级。

确定性是否需要（回放 / 联机）？决定是否现在启用 §6 确定性模式 + physics workerCount=1。

回退 / undo 是否需要？v1 暂定仅粗粒度快照存档、不做帧级 rewind（§6.3）；若需求变更须重审。

材质 / 反应规模（数百 vs 数千）？决定 byte vs ushort id、反应表结构细节。

温度是否全分辨率一等场 vs 接触 / 事件驱动？推荐默认 Noita 式 + 可选粗热场。

大世界内存上限与激活半径具体档位？按目标机 RAM 实测定（§12.2）。

GC 模式（Workstation vs Server）？按 §12.4 实测最坏停顿定。

音频后端（OpenAL vs FAudio vs miniaudio）与 Demo 音效资产范围？随渲染后端最终选定联动。

macOS/Linux 是否发布即一等目标还是后续？若 macOS 即时重要，早接 MoltenVK / FNA3D Metal 进选型。

光照忠实度（fog-of-war）vs 超越（Radiance Cascades）？推荐 v1 走 Noita 式，RC 作后续可选模式。

部分待定 Noita 内部数字（dirty-rect padding、流式激活半径与驱逐、线程池大小、磁盘格式、两 cell 争同一目标的解析规则）未官方公开，均按相应章节社区反推值起步，**以实测校准而非奉为圭臬** [低]。

---

*文档结束。本设计的四个相互强化的基石——64×64 chunk hash-map、单缓冲原地更新、per-chunk dirty rect、32px move cap——加 4-pass checkerboard 无锁调度，是达成全屏 60fps 的不可分割组合，应作为编码起点逐字落实。三组关键工程取舍是：CA 内核留 C#、仅 Box2D v3 下沉 native（且必须自建 task-callback 桥）、发行用 CoreCLR+R2R 兼得快启动与运行时 SIMD light-up。两条曾被低估的硬约束——「固定逻辑步长 + 时间膨胀（绝不追帧）」与「material 以稳定字符串键入盘、运行时 id 仅作索引」——分别守住帧节奏与存档兼容，必须从 M0 起就内建，事后补极难。*
