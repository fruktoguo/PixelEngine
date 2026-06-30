# Plan 06 — 像素碰撞、刚体与 Box2D 桥（PixelEngine.Physics + PixelEngine.Interop）

> 范围：全像素碰撞与刚体物理的**全部**实现。包含 Box2D v3.1 的 `[LibraryImport]` 薄绑定、自建 task-callback 桥、像素簇→刚体管线（CCL→Marching Squares→Douglas-Peucker→凸分解→复合刚体）、两世界栅格化同步、破坏/挖掘重建、静态地形局部 collider、以及独立于 Box2D 的玩家/生物角色控制器。
> 权威依据：架构文档 §8（全像素碰撞与刚体）、§14（C#/C++ 边界与 task 桥）、§6.4（确定性 physics workerCount=1）；不变式 #5（CA↔刚体双向耦合）、#9（CPU sim 权威）、#10（native 收敛到 Box2D）。技术栈：`plan/00` §4 物理行；工程宪法：`AGENTS.md`。
> 状态标记：`- [ ]` 未开始 / `- [x]` 完成自测 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。
> 帧相位引用沿用架构 §3.3：本文档主要占据**相位 8（Physics 同步，含 8a–8e）**，并与**相位 3（particle→cell）**、**相位 7（cell→particle）**、**相位 1（Game Logic，角色控制器）**协作。

---

## 1. 目标与范围

本文档交付 PixelEngine 的两套耦合 sim 之一——Box2D 刚体世界——以及它与权威像素 CA 世界（`plan/03`）之间的双向栅格化耦合，外加一个**完全独立于 Box2D** 的角色控制器供 Demo（`plan/13`）使用。引擎维护两套相互耦合的世界：per-pixel CA 网格（唯一权威世界状态，架构 §3.2）与 Box2D 动态刚体世界；本文档负责后者及其与前者的每帧往返。

落在本范围内、逐项可勾选的能力包括：`PixelEngine.Interop` 中对 vendored Box2D v3.1 C 源的 `[LibraryImport]` 薄绑定（blittable-only，禁 `DllImport`）；自建 `[UnmanagedCallersOnly]` task-callback 桥把 Box2D 的并行 for 派发到 `PixelEngine.Core` 的持久线程池（JobSystem，`plan/02`）；物理尺度常量（1 物理单位=16px）与 `b2MakePolygon(radius=0)` 的锐利像素边缘约定；像素簇→刚体的完整转换管线；body-local mask 作不可变权威形状源的两世界同步（相位 8）；破坏/挖掘后的连通分量重建与父子体速度转移（相位 8a）；仅为活跃刚体邻近 dirty chunk 生成的用后即弃静态地形 collider；以及 kinematic AABB 角色控制器。

明确**不在**本范围内（属其它文档，本文档只定义接口契约、不实现）：CA 内核本体（CellGrid SoA、chunk、dirty rect、checkerboard、KeepAlive，属 `plan/03`）；自由粒子池与 cell↔particle handshake（属 `plan/05`，本文档在碎片下限处调用其抛射 API）；持久线程池/barrier/事件总线/`EngineConstants`（属 `plan/02`，本文档消费）；渲染刚体高亮/owned-by-body 叠层着色（属 `plan/08`/`plan/12`）；刚体与自由粒子的存档持久化字节格式（属 `plan/07`，本文档提供其需要序列化的不可变 mask + transform + 速度的只读快照 API）；Demo 玩家具体玩法（属 `plan/13`，本文档只交付通用 `CharacterController` 公开 API）。

工程纪律（`AGENTS.md` §2/§3，强制）：一步到位、无 MVP、无 stub 充当实现；能多线程就多线程（CCL/形状重建/inverse-sampling 各刚体并行派发到 JobSystem）、能省内存就省内存（owned 像素只存 `{bodyId,localX,localY,material}`、稳态帧零托管堆分配、body/shape/mask 池化）；公开 API 一次设计到位并带完整中文 XML 文档注释。凡与第 1 章不变式或 `plan/00` 技术栈冲突者，停止并以 `- [!] 阻塞：原因` 上报，绝不自行变通。

---

## 2. 技术栈与依赖

物理库：**Box2D v3.1**，vendored C 源置于 `native/box2d/`（`plan/00` §4、`plan/01` 落实 dual-build × 6 RID），是引擎**唯一** native 依赖（不变式 #10）。绑定方式：`PixelEngine.Interop` 内自建 `[LibraryImport]`（source-gen）薄绑定，**不**采用任何第三方托管绑定（`plan/00` §4 库命名陷阱、架构 R10：避开两个 "Box2D.NET"）。互操作硬规则（`AGENTS.md` §3、架构 §14.3）：仅 `[LibraryImport]`、禁新 `DllImport`、blittable-only 签名、`<AllowUnsafeBlocks>true`；`[SuppressGCTransition]` **绝不**用于 `b2World_Step` 与 task 桥的 enqueue/finish 回调（架构 §14.2/§14.3，不变式相关）。

多线程：Box2D v3 自身不开线程、不捆 enkiTS（架构 §14.2 关键修正），其并行靠 `b2WorldDef` 上的 `enqueueTask`/`finishTask`/`workerCount` 回调；本文档自建 task 桥把其并行 for 派发到 `PixelEngine.Core` 的 JobSystem（与 CA 共用同一持久线程池，因相位顺序错开不冲突，架构 §12.7）。确定性/lockstep 模式下 `workerCount=1` 串行（架构 §6.4、§14.2、不变式相关于 §6）。

数学/内存：`System.Numerics` + `System.Runtime.Intrinsics`（`plan/00` §4）；跨界缓冲走 POH/`NativeMemory`（`plan/00` §4，零拷贝）。物理尺度常量集中在 `PixelEngine.Core` 的 `EngineConstants`（`plan/00` §7）：`PhysicsPixelsPerMeter=16`。

项目内依赖方向（`plan/00` §5，绝不反向）：`PixelEngine.Physics → {PixelEngine.Simulation, PixelEngine.Interop, PixelEngine.Core}`；`PixelEngine.Interop → PixelEngine.Core`。本文档新增/落实两个 assembly：

- **`PixelEngine.Interop`**（`AllowUnsafeBlocks`）：Box2D v3.1 `[LibraryImport]` 绑定 + blittable 结构体/句柄 + `[UnmanagedCallersOnly]` task 桥。隔离全部 unsafe/native surface。
- **`PixelEngine.Physics`**（`AllowUnsafeBlocks`）：CCL、Marching Squares、Douglas-Peucker、凸分解、刚体管线、两世界同步、破坏重建、静态地形 collider、角色控制器。依赖 Simulation（读写网格）与 Interop（Box2D）。

上游依赖文档（必须先于本文档完成对应 API，`plan/README` 执行顺序）：`plan/01`（项目骨架、Box2D native dual-build）、`plan/02`（Core：JobSystem 持久线程池 + barrier、`EngineConstants`、事件总线、诊断计时、POH/NativeMemory、RNG）、`plan/03`（Simulation：`CellGrid` SoA、`Chunk`、dirty rect/KeepAlive、`Flags` 位布局、`CellType`、坐标系）。下游消费者：`plan/05`（碎片转粒子的反向，本文档调用其抛射 API）、`plan/07`（刚体/粒子存档）、`plan/08`/`plan/12`（渲染/叠层）、`plan/13`（Demo 角色与可挖掘刚体）、`plan/14`（physics 测试）、`plan/15`（dual-build/AOT）。

跨文档协调点（非冲突，需在对应文档落实，否则记 `- [!]`）：`plan/03` 的 `Flags` 字节须为本文档预留一个 **`RigidOwned` 持久语义位**（架构 §7.1 标注 bit4-7 备用；本文档取 bit4，用于让 CA 把 owned-by-body cell 当不可移动地形并在被反应/挖掘消耗时入队通知 physics，见 §3.6）。`plan/02` 的 JobSystem 须暴露「fork-join 区间派发 + 稳定 workerIndex」的 API 供 task 桥使用（见 §3.2）。

---

## 3. 详细设计

### 3.1 `PixelEngine.Interop`：Box2D v3.1 薄绑定（架构 §8.2、§14.3）

命名空间 `PixelEngine.Interop.Box2D`。原生库名常量 `Box2DLibrary.Name = "box2d"`，并在 `ModuleInitializer` 里 `NativeLibrary.SetDllImportResolver` 按 RID 解析 `runtimes/<rid>/native/`（CoreCLR/R2R 动态库）与 AOT 静态链路径分歧由 `plan/15` 落实（架构 §14.4，R5 风险）。

**blittable 句柄与结构体**（`Box2DTypes.cs`，全部 `readonly struct`，字段顺序与 Box2D v3.1 C ABI 逐字对齐，禁托管引用字段）：

- 句柄：`B2WorldId`、`B2BodyId`、`B2ShapeId`、`B2ChainId`（v3 句柄是 `{index, world, generation}` 小值类型，按值传递）。
- 数学：`B2Vec2{float X,Y}`、`B2Rot{float C,S}`（cos/sin，架构 §8.2）、`B2Transform{B2Vec2 P; B2Rot Q}`、`B2AABB{B2Vec2 LowerBound,UpperBound}`。
- 几何：`B2Hull{B2Vec2* Points 或 inline 固定缓冲; int Count}`、`B2Polygon`（含 `vertices[8]`/`normals[8]` inline fixed、`centroid`、`count`、`radius`——**绝不手填，恒经 `b2MakePolygon` 产出**，架构 §8.2）、`B2Segment{B2Vec2 Point1,Point2}`。
- def 结构：`B2WorldDef`（含 `gravity`、`workerCount`、`enqueueTask`/`finishTask`/`userTaskContext` 函数指针字段——task 桥注入点，架构 §14.2）、`B2BodyDef`（`type`、`position`、`rotation`、`linearVelocity`、`angularVelocity`、`userData` 等）、`B2ShapeDef`（`density`、`material/friction/restitution`、`filter`、`userData`）、`B2ChainDef`（静态地形链）。
- 枚举：`B2BodyType{ b2_staticBody=0, b2_kinematicBody, b2_dynamicBody }`。
- 回调签名 typedef：`b2TaskCallback = delegate* unmanaged<int /*start*/, int /*end*/, uint /*workerIndex*/, void* /*ctx*/, void>`；`b2EnqueueTaskCallback`、`b2FinishTaskCallback` 对应 native 期望的函数指针类型。

**`[LibraryImport]` 函数清单**（`Box2DFunctions.cs`，`partial static class Box2D`，`[LibraryImport(Box2DLibrary.Name)]`、blittable-only、`in`/`ref`/`out` 用于按指针传 struct）：

| 类别 | native 函数 | 用途 / 调用相位 |
|---|---|---|
| 尺度 | `b2SetLengthUnitsPerMeter` | 启动期设 16px=1m（架构 §8.1、R9） |
| world | `b2DefaultWorldDef` / `b2CreateWorld` / `b2DestroyWorld` | 创建/销毁世界，注入 task 桥（一次性） |
| world | `b2World_Step` | 相位 8c，`(worldId, dt, subStepCount=4)`；**禁 `[SuppressGCTransition]`**（架构 §8.3、§14.3） |
| world 事件 | `b2World_GetBodyEvents` / `b2World_GetContactEvents` | 相位 8d 读 move 事件、相位 8/音频读接触（材质音效钩子，`plan/10`） |
| body | `b2DefaultBodyDef` / `b2CreateBody` / `b2DestroyBody` | 相位 8a 建/毁刚体 |
| 几何 | `b2ComputeHull` | 凸片顶点→凸包（架构 §8.2，恒先于 MakePolygon） |
| 几何 | `b2MakePolygon` | `(in B2Hull, radius=0)` 锐利边缘（架构 §8.2 修正，R9） |
| 几何 | `b2MakeSegment` | 静态地形短链段（§3.7） |
| shape | `b2DefaultShapeDef` / `b2CreatePolygonShape` | 每凸片挂 body（v3 无 fixture，架构 §8.2） |
| shape | `b2CreateSegmentShape` / `b2CreateChain` / `b2DestroyChain` | 静态地形 collider（§3.7） |
| shape | `b2DestroyShape` | 重建时清旧 shape |
| 读变换 | `b2Body_GetPosition` / `b2Body_GetRotation` / `b2Body_GetTransform` | 相位 8d 读回（架构 §8.3） |
| 写变换 | `b2Body_SetTransform` | 读档重建续跑（`plan/07`） |
| 速度 | `b2Body_GetLinearVelocity` / `b2Body_SetLinearVelocity` | 相位 8a 父→子速度转移（架构 §8.4） |
| 速度 | `b2Body_GetAngularVelocity` / `b2Body_SetAngularVelocity` | 同上 |
| 冲量 | `b2Body_ApplyLinearImpulse` / `b2Body_ApplyForce` | 爆炸/冲击对刚体施力（相位 7 联动） |
| 质量 | `b2Body_GetMass` / `b2Body_ApplyMassFromShapes` | density 驱动自动质量（架构 §8.2） |
| 休眠 | `b2Body_SetAwake` / `b2Body_IsAwake` / `b2Body_Enable` / `b2Body_Disable` | sleeping 跳过重建（架构 §8.4 节流） |
| userData | `b2Body_GetUserData` / `b2Body_SetUserData` | body↔托管 `PixelRigidBody` 关联（存 int bodyKey，非托管指针） |

所有签名 blittable；`b2Polygon`/`b2Hull`/`b2WorldDef` 等大结构按 `in`/`ref` 传指针避免大值拷贝。绑定层不做业务逻辑，纯 1:1 映射；Box2D v3.1 头文件 ABI 对齐由 `plan/14` 的 interop smoke test 验证（建世界→建一体→Step→读回坐标变化）。

### 3.2 自建 task-callback 桥（架构 §14.2，本文档最复杂处，R14）

命名空间 `PixelEngine.Interop.Box2D`，类型 `Box2DTaskBridge`。这是 native 互操作真正的复杂度所在（架构 §14.2），必须先验证串行正确再开多线程（R14）。

形态：两个 `[UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])]` static 方法 `EnqueueTask` 与 `FinishTask`，其地址（`&Box2DTaskBridge.EnqueueTask`）连同 `workerCount` 与 `userTaskContext`（指向一个 pinned 的 `BridgeContext`）一并写入 `B2WorldDef` 后传给 `b2CreateWorld`。`workerCount` 设为 `PixelEngine.Core` JobSystem 的 worker 数（`plan/02`）。

`EnqueueTask(b2TaskCallback task, int itemCount, int minRange, void* taskContext, void* userContext)` 语义：把区间 `[0, itemCount)` 切成长度 `≥ minRange` 的若干子区间（`subCount = clamp(workerCount, 1, ceil(itemCount/minRange))`，均匀分块），派发到 JobSystem；每个 worker 以**稳定的** `workerIndex ∈ [0, workerCount)` 经 IL `calli` 回调 `task(start, end, workerIndex, taskContext)`（`delegate* unmanaged`，架构 §14.3）。返回一个哑任务句柄（`void*`，编码批次 id）。`FinishTask(void* taskHandle, void* userContext)` 等待该批完成。

**起步实现**：同步 fork-join（架构 §14.2）——`EnqueueTask` 内即在 JobSystem 上 fork-join 并阻塞到全部子区间完成、返回哑句柄；`FinishTask` 为 no-op。正确且简单。后续若要与主线程重叠可改真正异步 join（保留 `taskHandle` 语义）。

稳定 `workerIndex` 的分配：Box2D 用 `workerIndex` 索引每 worker 私有数据，**绝不允许两个并发回调复用同一 index**（R14）。JobSystem 须提供「带稳定 worker 槽位的区间派发」API（`plan/02` 协调点）；桥把 JobSystem 的物理线程槽位直接当 `workerIndex`，保证 `[0, workerCount)` 内无并发复用。

纪律：这些回调每个 `Step` 触发多次（按 island/color 分区）且每次重入托管代码，**绝不能 `[SuppressGCTransition]`**——必须走正常 GC 转换（架构 §14.2/§14.3，明确例外）。回调内 blittable、不抛异常（异常跨越 native 边界是 UB；内部 try/catch 兜底记诊断、绝不外泄）。确定性模式：`workerCount=1`，`EnqueueTask` 直接在调用线程串行执行整个区间（架构 §6.4/§14.2）。

### 3.3 物理尺度与坐标转换（架构 §8.1、R9）

类型 `PhysicsScale`（`PixelEngine.Physics`，静态），消费 `EngineConstants.PhysicsPixelsPerMeter = 16`（`plan/00` §7）。1 物理单位（Box2D 米）= 16 px（架构 §8.1，避开 1px=1m 把形状推出 0.1–10m 调优区的 R9 陷阱）。启动期 `b2SetLengthUnitsPerMeter(16f)` 一次（或纯在转换层缩放，二选一并固定，记 ADR）。提供 `PixelToPhysics(int px)`/`PhysicsToPixel(float m)`/`B2Vec2 ToPhysics(in CellPos)`/`CellPos ToCell(in B2Vec2)` 内联转换。坐标系沿用 `plan/00` §7：世界 cell 整数坐标权威、y 向下。`b2MakePolygon` 的 `radius` 恒传 `0`（锐利像素边缘；非零 radius 圆角破坏像素贴合，架构 §8.2 修正、R9）。

### 3.4 像素簇→刚体转换管线（架构 §8.2，确认管线 [高]）

总编排器 `ShapeBuilder.BuildBody(BodyLocalMask mask, ...)`，把不可变 mask 转为 Box2D 复合刚体。管线五段，全部在 `PixelEngine.Physics`，可 off-thread（相位 8a 各刚体并行派发到 JobSystem，架构 §8.4）：

1. **CCL — `ConnectedComponentLabeler`**（架构 §8.2）：**显式栈、非递归** flood fill（防栈溢出），可选 4/8 连通（`Connectivity` 参数）。输入二值固体 mask，输出每连通块的像素集合 + 标签。栈用 `ArrayPool<int>`/`stackalloc` scratch，零稳态分配。不接触锚点（世界边界/指定静态质量）且小于尺寸上限的连通块成为刚体候选；**碎片下限**：连通块像素数 `< FragmentPixelThreshold`（可调常量）的不建刚体，转自由粒子/debris（调用 `plan/05` 抛射 API，遏制 §8.4 尖刺、符合观感）。
2. **Marching Squares — `MarchingSquares`**（架构 §8.2）：沿二值 mask 边界走，**16 种 case** 查表，产出像素分辨率闭合折线（CCW 外轮廓 + CW 内孔，孔在凸分解前处理）。
3. **Douglas-Peucker — `DouglasPeucker`**（架构 §8.2）：以 `epsilon`（~1–2px **可调**）递归简化折线，顶点数砍 1–2 个数量级。显式栈递归（与 CCL 同纪律）。
4. **凸分解 — `ConvexDecomposer`**（架构 §8.2 修正、不变式相关）：简化后的（通常凹）多边形拆成凸片，因 Box2D 多边形必须凸且 **≤8 顶点（`B2_MAX_POLYGON_VERTICES = 8`）**。采用 **Hertel-Mehlhorn / Ivan Fratric PolyPartition**（生成更少、更大、≤8 顶点凸片，避免三角化产生 `verts-2` 个 shape 放大 solver 成本与重建尖刺）。顶点超 8 的凸片再二次切分至 ≤8。**三角化（ear-clipping）仅作 PolyPartition 对退化输入失败时的健壮回退**（架构 §8.2，绝不作主路径）。
5. **Box2D 复合刚体 — `ShapeBuilder`**（架构 §8.2 [高]）：`b2CreateBody`（`b2_dynamicBody`，position=mask 质心经 `PhysicsScale`）→ 对每凸片 `b2ComputeHull(points,count)` 然后 `b2MakePolygon(in hull, radius=0)`（绝不手填 `b2Polygon`）→ `b2CreatePolygonShape(bodyId, in shapeDef, in poly)`（density 驱动自动质量）→ `b2Body_ApplyMassFromShapes`。读回用 `b2Body_GetPosition` + `b2Body_GetRotation`。

管线产物连同不可变 mask 封装为 `PixelRigidBody`（§3.5）。`plan/14` 验证：每片 ≤8 顶点且凸、凸片并集覆盖原 mask、`radius=0`（`AGENTS.md` §7）。

### 3.5 body-local mask 作不可变权威形状源（架构 §8.3/R6，不变式 #5）

`BodyLocalMask`（`readonly` 类/结构 + POH 后备缓冲）：刚体的**不可变权威形状**——`{宽,高,local 原点, 固体位图(bitset), 每像素 material(ushort[])}`。它始终用于 inverse-sampling 与碰撞查询，**绝不让形状本身被往返侵蚀**（根除亚像素漂移的关键，架构 §8.3、R6）。每帧的 stamp 都是从这个不可变 mask 经精确 inverse transform 重新生成，而非拷上一帧的网格像素。CA 对刚体的挖掘**不**侵蚀 mask，而是反映为一次显式破坏事件（相位 8a 的 CCL 重建，§3.6）。

`PixelRigidBody`（托管刚体包装）：持 `B2BodyId`、不可变 `BodyLocalMask`、上一帧 transform、上一帧 stamp 的 cell 列表（供相位 8b erase）、bodyKey（int，写入 `b2Body_SetUserData`，避免存托管指针）。owned 像素逻辑上只存 `{bodyId, localX, localY, material}`，**世界坐标每帧由变换导出**（架构 §8.3 [高]），使破坏时单次 CCL 即可拆分。bodyKey↔`PixelRigidBody` 映射存 `PhysicsWorld` 的稠密数组 + 自由列表（池化，零分配）。

### 3.6 两世界栅格化同步（相位 8，不变式 #5、架构 §8.3）

编排器 `PhysicsSystem.SyncStep(dt)`，严格按架构 §3.3 相位 8 子序执行（与 CA 相位 4 错开避免 checkerboard 竞争）：

- **(8a) `RigidBodyDestruction` — CCL 检测新脱落块 + 破坏重建**：先排空 `RigidDamageQueue`（§3.6 末），对 dirty 刚体执行 §3.7 重建；同时对地形改变区域跑 CCL 检测新脱落的连通固体块→建新刚体（§3.4）。
- **(8b) `RigidBodyRasterizer.EraseAtCurrentTransform`**：对每活跃刚体，按其上一帧 stamp 的 cell 列表，在**当前（旧）变换**处把 owned 像素从网格擦除（写 `Empty`、清 `RigidOwned` 位），使 CA 不再当它地形（架构 §8.3 步骤 1）。
- **(8c) `b2World_Step(worldId, dt, subStepCount=4)`**：经 §3.2 task 桥多线程（非 Box2D 自开线程；确定性时 workerCount=1）。`subStepCount=4` 是 Box2D 内部子步、与「额外 CA step」无关（架构 §4.1、§8.3）。
- **(8d) 读回 transform**：`b2World_GetBodyEvents` 取移动事件或逐体 `b2Body_GetPosition`/`b2Body_GetRotation`，更新 `PixelRigidBody`（架构 §8.3 步骤 3）。
- **(8e) `RigidBodyRasterizer.StampInverseSampling`**：**inverse sampling 重栅格化**（架构 §8.3 步骤 4，水密关键）——对刚体 AABB 内每个目标 cell，把 cell 中心反变换到 body-local 空间、最近邻采样不可变 mask；命中固体则写回 material、置 `RigidOwned` 位、登记进本帧 stamp 列表与 `RigidStampRegistry`，并标记所在 chunk dirty 唤醒 CA（经 `plan/03` KeepAlive 接口）。**绝不用 forward sampling**（正向变换源像素在旋转下留洞，架构 §8.3 [高]）。

双向耦合机制（不变式 #5、架构 §1.2/§8.3）：owned cell 被 stamp 后带 `RigidOwned` 位，CA 把它当不可移动 Solid 地形（沙能堆其上、火能烧、酸能蚀）。当 CA 的反应/挖掘**消耗**一个 `RigidOwned` cell（写 Empty）时，须把该 cell 入 `RigidDamageQueue`（per-worker MPSC 缓冲，无锁，相位 4 内填充、相位 8a 排空）。physics 在 8a 经 `RigidStampRegistry`（cell→bodyKey+localX+localY）把受损 cell 映射回刚体与其 mask 局部坐标，对 mask 做一次显式破坏（清对应 mask 位），标该刚体 dirty 触发重建。这样 CA「挖得动」刚体，同时 mask 权威形状不被往返累积侵蚀。

### 3.7 破坏 / 挖掘（相位 8a，架构 §8.4/R4）

`RigidBodyDestruction.RebuildDirty(body)`：刚体丢像素（挖/炸/烧/酸）→ §3.6 已标 dirty 并对 `BodyLocalMask` 清相应位 → 取**剩余** body-local mask → `ConnectedComponentLabeler` 跑 CCL → 每连通块重跑 §3.4 的 Marching Squares→DP→PolyPartition → 建**新** Box2D 刚体 → **把父体线/角速度转移给每个子体**（`b2Body_GetLinearVelocity`/`GetAngularVelocity` 读父，`SetLinearVelocity`/`SetAngularVelocity` 写子，按子体相对父质心位置可加角速度诱导的线速度分量，使拆分看着物理，架构 §8.4 [中]）→ `b2DestroyBody` 销毁旧体。子体继承父 bodyKey 池槽或分配新槽。

节流（强制，也是架构 §4.3 过载降级所依赖的保证，R4）：**每帧每受影响刚体至多重建一次**（合并本帧多次像素移除再一次性重建）；**sleeping 刚体跳过重建**直到被扰动（`b2Body_IsAwake`）；CCL/Marching Squares/DP/PolyPartition **off-thread**（相位 8a 内各刚体并行派发到 JobSystem）；小于 `FragmentPixelThreshold` 的碎片**转粒子**而非建体（调用 `plan/05`）；body/shape/mask **池化**避免分配。多簇同时被毁仍可能尖刺（R4 已知风险），与 §4.3 降级联动由 `plan/16` 跨切面处理。

### 3.8 静态地形碰撞（架构 §8.1，不整体喂 Box2D）

`StaticTerrainColliders`：**静态地形就是像素网格本身，绝不整体喂 Box2D**（上百万条边，架构 §8.1）。动态刚体与地形碰撞通过**用后即弃的局部静态 collider**：仅为活跃刚体邻近的 **dirty chunk** 生成——取该 chunk 局部固体 mask（排除 `RigidOwned` cell）→ Marching Squares → Douglas-Peucker → `b2CreateChain`/`b2CreateSegmentShape` 静态 chain（架构 §8.1 方案 a）；chunk 再次 dirty 或刚体离开邻域时销毁重建（`b2DestroyChain`）。提供更粗的 `TilemapCollider` 作可选回退（架构 §8.1 方案 b）。生成范围由「活跃刚体 AABB 膨胀 N chunk」圈定，避免为全世界建 collider。角色控制器（§3.9）**不**走此路径，直接对像素场解算。

### 3.9 玩家 / 生物角色控制器（架构 §8.5，独立于 Box2D，供 `plan/13`）

`CharacterController`（`PixelEngine.Physics` 公开 API，带完整中文 XML 注释）：角色**不是** Marching-Squares 刚体，而是 kinematic AABB（或小 bitmap）**直接对固体像素场解算，完全独立于 Box2D、与 `b2World_Step` 解耦**（架构 §8.5，使移动手感独立于刚体负载）。运行于**相位 1（Game Logic）**，读取相位 1 起点稳定的权威网格（`plan/03` 只读 cell 查询 API）。

算法（架构 §8.5）：输入期望位移 → 沿轴拆分 → 对每轴用 **speculative contacts** 防穿透 + **多次 sub-iteration** 把角色推出墙/地/坡 → 沿 AABB 边采样固体像素直接得**地面/墙/坡检测**（`IsGrounded`、`WallContact`、`SlopeAngle`）。支持 step-up（爬小台阶/坡）、可配置 skin width 与最大 sub-iteration 数。把 `RigidOwned` cell 也视为固体（角色能站在/被推开于动态刚体上，双向耦合在角色侧的体现；可选对刚体施反作用冲量经 `b2Body_ApplyLinearImpulse`）。公开 `Move(in Vector2 desired, out CharacterCollisionInfo info)`、`Bitmap`/`Aabb` 形状设定、地面/墙/坡查询属性。零稳态分配（邻近像素采样走 `Span`/ref 漫游）。

### 3.10 相位编排与诊断

`PhysicsSystem` 总门面：`Initialize(JobSystem, CellGrid, EngineContext)`（建 world、注入 task 桥）、`SyncStep(dt)`（相位 8a–8e）、`Shutdown`（`b2DestroyWorld`）。向 `PixelEngine.Core` 诊断/计时器注册分项耗时（`plan/00` §7、架构 §17.1）：CCL、形状重建、Step、erase、inverse-sample、静态 collider、角色控制器，供编辑器性能 HUD 与 §4.3 过载降级。向事件总线（`plan/02`）发刚体破碎事件供音频（`plan/10` shatter）。暴露只读快照 API（不可变 mask + transform + 线/角速度）供 `plan/07` 存档。

---

## 4. 实现清单

### 4.1 PixelEngine.Interop — Box2D 绑定
- [ ] `Box2DLibrary`：库名常量 `"box2d"` + `ModuleInitializer` 注册 `NativeLibrary.SetDllImportResolver`（RID→`runtimes/<rid>/native/`），AOT 静态分歧留 `plan/15`（架构 §14.4）。
- [ ] `Box2DTypes.cs`：blittable `readonly struct` 句柄 `B2WorldId`/`B2BodyId`/`B2ShapeId`/`B2ChainId`，与 v3.1 C ABI 逐字对齐。
- [ ] `Box2DTypes.cs`：数学 `B2Vec2`/`B2Rot(C,S)`/`B2Transform`/`B2AABB`；几何 `B2Hull`/`B2Polygon`(inline fixed verts[8]/normals[8])/`B2Segment`。
- [ ] `Box2DTypes.cs`：def 结构 `B2WorldDef`(含 enqueue/finish 函数指针 + workerCount + userTaskContext)/`B2BodyDef`/`B2ShapeDef`/`B2ChainDef`；枚举 `B2BodyType`。
- [ ] 回调函数指针 typedef `b2TaskCallback`/`b2EnqueueTaskCallback`/`b2FinishTaskCallback`（`delegate* unmanaged`）。
- [ ] `Box2DFunctions.cs`：`[LibraryImport]` 尺度 `b2SetLengthUnitsPerMeter`。
- [ ] world：`b2DefaultWorldDef`/`b2CreateWorld`/`b2DestroyWorld`/`b2World_Step`/`b2World_GetBodyEvents`/`b2World_GetContactEvents`。
- [ ] body：`b2DefaultBodyDef`/`b2CreateBody`/`b2DestroyBody`/`b2Body_GetUserData`/`b2Body_SetUserData`。
- [ ] 几何：`b2ComputeHull`/`b2MakePolygon`(radius 参数)/`b2MakeSegment`。
- [ ] shape：`b2DefaultShapeDef`/`b2CreatePolygonShape`/`b2CreateSegmentShape`/`b2CreateChain`/`b2DestroyChain`/`b2DestroyShape`。
- [ ] 变换/速度/冲量/质量/休眠：`b2Body_GetPosition`/`GetRotation`/`GetTransform`/`SetTransform`/`GetLinearVelocity`/`SetLinearVelocity`/`GetAngularVelocity`/`SetAngularVelocity`/`ApplyLinearImpulse`/`ApplyForce`/`GetMass`/`ApplyMassFromShapes`/`SetAwake`/`IsAwake`/`Enable`/`Disable`。
- [ ] 校验全部签名 blittable、无 `DllImport`、大 struct 走 `in`/`ref`/`out`（`AGENTS.md` §3）。

### 4.2 PixelEngine.Interop — task-callback 桥（架构 §14.2，R14）
- [ ] `Box2DTaskBridge.BridgeContext`：pinned 结构，持 JobSystem 引用与批次状态（POH/`NativeMemory`）。
- [ ] `[UnmanagedCallersOnly(CallConvCdecl)] EnqueueTask`：切 `[0,itemCount)` 为 `≥minRange` 子区间，派发 JobSystem，稳定 `workerIndex`，IL `calli` 回调 `b2TaskCallback`，返回哑句柄。
- [ ] `[UnmanagedCallersOnly(CallConvCdecl)] FinishTask`：等待该批（起步同步 fork-join 下为 no-op）。
- [ ] 起步**同步 fork-join** 实现（`EnqueueTask` 内阻塞到完成，正确优先，架构 §14.2）。
- [ ] 稳定 `workerIndex` 分配：用 JobSystem 物理线程槽位，保证 `[0,workerCount)` 无并发复用（R14；依赖 `plan/02` 协调 API）。
- [ ] 回调内禁 `[SuppressGCTransition]`、不抛异常（try/catch 兜底记诊断，架构 §14.2/§14.3）。
- [ ] 确定性模式 `workerCount=1` 串行路径（架构 §6.4），由配置开关切换。
- [ ] 注入 `B2WorldDef`：`&EnqueueTask`/`&FinishTask`/`workerCount=JobSystem.WorkerCount`/`userTaskContext=&BridgeContext`。

### 4.3 PixelEngine.Physics — 尺度与转换
- [ ] `PhysicsScale`：消费 `EngineConstants.PhysicsPixelsPerMeter=16`，`b2SetLengthUnitsPerMeter(16)` 一次（架构 §8.1，R9）。
- [ ] `PixelToPhysics`/`PhysicsToPixel`/`ToPhysics(CellPos)`/`ToCell(B2Vec2)` 内联零分配转换。
- [ ] 全 `b2MakePolygon` 调用点 `radius=0`（架构 §8.2 修正，R9）；加断言/分析守门。

### 4.4 PixelEngine.Physics — 像素簇→刚体管线（架构 §8.2）
- [ ] `ConnectedComponentLabeler`：**显式栈非递归** flood fill，4/8 连通可选；`ArrayPool`/`stackalloc` scratch 零稳态分配。
- [ ] CCL 锚点判定（接触世界边界/静态质量不成体）+ 尺寸上限 + `FragmentPixelThreshold` 碎片转粒子（调 `plan/05`）。
- [ ] `MarchingSquares`：16-case 查表，CCW 外轮廓 + CW 内孔，输出像素分辨率闭合折线。
- [ ] `DouglasPeucker`：`epsilon` 可调（默认 ~1–2px），显式栈递归简化。
- [ ] `ConvexDecomposer`：**Hertel-Mehlhorn / PolyPartition** 主路径，凸片 ≤8 顶点（`B2_MAX_POLYGON_VERTICES`），超 8 二次切分。
- [ ] `ConvexDecomposer`：ear-clipping 三角化**仅作退化回退**（架构 §8.2，绝不主路径）。
- [ ] `ShapeBuilder.BuildBody`：`b2CreateBody`→每片 `b2ComputeHull`→`b2MakePolygon(radius=0)`→`b2CreatePolygonShape`→`b2Body_ApplyMassFromShapes`（绝不手填 `b2Polygon`）。

### 4.5 PixelEngine.Physics — 不可变 mask 与刚体包装（架构 §8.3/R6，不变式 #5）
- [ ] `BodyLocalMask`：不可变 `{w,h,localOrigin,固体 bitset,material[]}`，POH 后备；作权威形状源，永不被往返侵蚀。
- [ ] `PixelRigidBody`：`B2BodyId` + 不可变 mask + 上帧 transform + 上帧 stamp cell 列表 + bodyKey；owned 像素只存 `{bodyId,localX,localY,material}`。
- [ ] `PhysicsWorld`：bodyKey↔`PixelRigidBody` 稠密数组 + 自由列表（池化）；`b2Body_SetUserData` 存 bodyKey（非托管指针）。
- [ ] `RigidStampRegistry`：cell index→`{bodyKey,localX,localY}`，每帧 stamp 时重建，供 erase 与 damage 映射。
- [ ] `RigidDamageQueue`：per-worker 无锁 MPSC 缓冲，相位 4 填充、相位 8a 排空。

### 4.6 PixelEngine.Physics — 两世界同步（相位 8，不变式 #5）
- [ ] `PhysicsSystem.SyncStep(dt)` 编排相位 8a→8e，严格与 CA 相位 4 错开（架构 §3.3）。
- [ ] (8b) `RigidBodyRasterizer.EraseAtCurrentTransform`：按上帧 stamp 列表在旧变换处清网格、清 `RigidOwned` 位（架构 §8.3 步1）。
- [ ] (8c) 调 `b2World_Step(dt, subStepCount=4)` 经 task 桥（确定性 workerCount=1）（架构 §8.3 步2、§4.1）。
- [ ] (8d) 读回 transform（`b2World_GetBodyEvents` 或逐体 `GetPosition`/`GetRotation`）（架构 §8.3 步3）。
- [ ] (8e) `RigidBodyRasterizer.StampInverseSampling`：AABB 内每 cell 反变换最近邻采样 mask，写回 material+`RigidOwned`，登记 stamp+registry，标 chunk dirty（KeepAlive）（架构 §8.3 步4）。
- [ ] **inverse sampling 旋转水密无洞**，绝不 forward sampling（架构 §8.3 [高]）。
- [ ] CA 消耗 `RigidOwned` cell 时入 `RigidDamageQueue` 的接口（供 `plan/03` 反应/移动调用）。

### 4.7 PixelEngine.Physics — 破坏/挖掘（相位 8a，架构 §8.4/R4）
- [ ] `RigidBodyDestruction.RebuildDirty`：剩余 mask→CCL→每块 MS→DP→PolyPartition→建新体→销旧体。
- [ ] **父→子线/角速度转移**（`Get/SetLinearVelocity`+`Get/SetAngularVelocity`，含角速度诱导线速度分量）（架构 §8.4）。
- [ ] (8a) CCL 检测地形改变区新脱落连通块→建新刚体。
- [ ] 节流：每帧每刚体至多一次重建（合并移除）。
- [ ] 节流：sleeping 刚体跳过（`b2Body_IsAwake`）。
- [ ] 节流：CCL/MS/DP/PolyPartition off-thread（各刚体并行派发 JobSystem）。
- [ ] 节流：碎片 `< FragmentPixelThreshold` 转粒子（`plan/05`）；body/shape/mask 池化。

### 4.8 PixelEngine.Physics — 静态地形 collider（架构 §8.1）
- [ ] `StaticTerrainColliders`：仅为活跃刚体邻近 dirty chunk 生成；局部 mask（排除 `RigidOwned`）→MS→DP→`b2CreateChain` 静态链。
- [ ] chunk 再 dirty / 刚体离域时 `b2DestroyChain` 用后即弃重建。
- [ ] 生成范围按「活跃刚体 AABB 膨胀 N chunk」圈定，绝不整体喂 Box2D（架构 §8.1）。
- [ ] `TilemapCollider` 粗回退路径（架构 §8.1 方案 b）。

### 4.9 PixelEngine.Physics — 角色控制器（架构 §8.5，供 `plan/13`）
- [ ] `CharacterController`：kinematic AABB/小 bitmap，**独立于 Box2D**，运行相位 1，读相位起点稳定网格。
- [ ] speculative contacts + 多 sub-iteration 推出墙/地/坡（架构 §8.5）。
- [ ] 沿 AABB 边采样固体像素做地面/墙/坡检测：`IsGrounded`/`WallContact`/`SlopeAngle`；step-up；skin width、最大 sub-iteration 可配。
- [ ] 把 `RigidOwned` cell 视为固体；可选对刚体施反作用冲量（`b2Body_ApplyLinearImpulse`）。
- [ ] 公开 `Move(in Vector2 desired, out CharacterCollisionInfo)` + 形状/查询 API，完整中文 XML 注释，零稳态分配。

### 4.10 PixelEngine.Physics — 编排/诊断/快照
- [ ] `PhysicsSystem.Initialize`/`SyncStep`/`Shutdown`（建/注入桥/毁 world）。
- [ ] 向 Core 诊断注册分项耗时（CCL/重建/Step/erase/inverse-sample/静态 collider/角色）（架构 §17.1）。
- [ ] 刚体破碎事件入事件总线供音频（`plan/10`）。
- [ ] 只读快照 API（不可变 mask + transform + 线/角速度）供 `plan/07` 存档。

---

## 5. 验收标准

### 5.1 Interop 与 task 桥
- [ ] interop smoke test：建 world→建一动态体→`b2World_Step`→读回坐标确有重力下落（`plan/14`）。
- [ ] 全部绑定 blittable、零新 `DllImport`、`b2World_Step` 与 task 回调均无 `[SuppressGCTransition]`（代码审查 + 分析器）。
- [ ] task 桥串行（workerCount=1）下 Step 结果正确；多线程下与串行**统计等价**（非 bit，架构 §6.1/§6.4）。
- [ ] task 桥多线程经 §17.1 计时确认 physics 真并行（4 worker 较 1 worker Step 显著加速，R14）。
- [ ] `workerIndex` 无并发复用（压力测试 + 断言，R14）。
- [ ] 确定性模式 `workerCount=1` 跨运行可复现（架构 §6.4）。

### 5.2 转换管线与刚体
- [ ] 凸分解每片 **≤8 顶点且凸**（`plan/14` 性质测试，`AGENTS.md` §7）。
- [ ] 凸片并集**覆盖原 mask**（无丢面积，`AGENTS.md` §7）。
- [ ] 所有 `b2MakePolygon` **`radius=0`**（验证锐利边缘，`AGENTS.md` §7、R9）。
- [ ] CCL 用显式栈非递归，大连通块不栈溢出（架构 §8.2）。
- [ ] 碎片 `< FragmentPixelThreshold` 转自由粒子而非建体（架构 §8.2/§8.4）。

### 5.3 两世界同步（不变式 #5）
- [ ] **inverse-sampling 旋转水密无洞**：任意旋转角栅格化后刚体内部无空洞（`plan/14`，`AGENTS.md` §7）。
- [ ] **无亚像素侵蚀**：刚体自由旋转/平移 N 帧后，从不可变 mask 重 stamp 的像素数与初始 mask 固体数一致（R6、不变式 #5）。
- [ ] **双向耦合**：沙能堆在刚体上、火能烧刚体、酸能蚀刚体、CA 能挖刚体（架构 §1.2/§8.3）。
- [ ] 挖断的连通固体块掉落成刚体、可旋转、再被毁拆分为多体（架构 §8.2/§8.4，里程碑 M6）。
- [ ] 刚体网格擦除/栅格化在相位 8、与 CA 相位 4 不竞争（架构 §3.3 约束）。

### 5.4 破坏/挖掘节流
- [ ] 每帧每刚体至多一次重建；sleeping 刚体跳过（架构 §8.4）。
- [ ] CCL/MS/DP/PolyPartition off-thread 并行（§17.1 计时确认）。
- [ ] 父→子速度/角速度转移使拆分物理合理（视觉 + 单测）。

### 5.5 静态地形与角色
- [ ] 地形不整体喂 Box2D；动态刚体与地形碰撞经局部用后即弃静态 collider（架构 §8.1）。
- [ ] 角色控制器独立于 Box2D、与 `b2World_Step` 解耦，移动手感不受刚体负载影响（架构 §8.5）。
- [ ] 角色对固体像素场正确解算：不穿墙、正确地面/墙/坡检测、可爬小坡/台阶（架构 §8.5）。
- [ ] 角色把 `RigidOwned` cell 当固体（能站上动态刚体）。

### 5.6 性能与纪律
- [ ] 刚体 Step + 形状重建符合帧预算 ≤3–4ms（架构 §1.4，目标机 BenchmarkDotNet）。
- [ ] 稳态帧循环内本文档代码零托管堆分配（`AGENTS.md` §3，分配剖析）。
- [ ] 所有公开 API 带完整中文 XML 注释（`plan/00` §7）。
- [ ] 与第 1 章不变式（#5/#9/#10）及 `plan/00` 技术栈无冲突（自审）。

---

## 6. 依赖关系

上游（须先就绪，否则相关条目记 `- [!]`）：
- `plan/01`：解决方案骨架、`PixelEngine.Interop`/`PixelEngine.Physics` 工程、Box2D v3.1 vendored 源与 dual-build × 6 RID（native 产物到 `runtimes/<rid>/native/`）。
- `plan/02`：`PixelEngine.Core` JobSystem 持久线程池 + barrier（task 桥与各刚体并行所需）、**稳定 workerIndex 区间派发 API**（task 桥所需协调点）、`EngineConstants.PhysicsPixelsPerMeter`、事件总线、诊断/计时、POH/`NativeMemory`、RNG。
- `plan/03`：`PixelEngine.Simulation` 的 `CellGrid` SoA、`Chunk`、dirty rect + **KeepAlive** 唤醒接口、`CellType`、坐标系、`Flags` 字节布局须预留 **`RigidOwned` 位（bit4）** 与「消耗 owned cell 入 `RigidDamageQueue`」的反应/移动钩子（协调点）。
- `plan/05`：自由粒子抛射 API（碎片转粒子调用）。

下游（消费本文档产物）：
- `plan/07`：刚体（不可变 mask + transform + 速度）与自由粒子的存档/续跑（用本文档只读快照 API）。
- `plan/08`/`plan/12`：渲染刚体高亮、owned-by-body-K 着层、CCL 连通块叠层（架构 §17.2）。
- `plan/10`：刚体破碎 shatter 音效（事件总线）。
- `plan/13`：Demo 可挖掘刚体与可操作角色（`CharacterController` 公开 API）。
- `plan/14`：本文档全部性质测试（凸分解/水密/无侵蚀/边界）与 interop smoke test。
- `plan/15`：Box2D dual-build、AOT `IlcInstructionSet`、native 打包到 6 RID。
- `plan/16`：形状重建尖刺与 §4.3 过载降级联动（R4）。

里程碑映射：本文档整体对应架构 §18 的 **M6（像素碰撞 + 刚体）**，刻意置于 sim 稳定（M0–M5）之后（`plan/README` 执行顺序、架构 §18 排序原则）。

---

## 7. 提交节点

按 `AGENTS.md` §6 每完成一个节点立即用中文 git 提交（`type(scope): 中文简述`，scope=`physics`/`core`，正文注明对应 plan 条目/架构 §）：

- [ ] 节点 1：`feat(physics): Box2D v3.1 [LibraryImport] 薄绑定与 blittable 类型`（对应 §4.1，架构 §8.2/§14.3）。
- [ ] 节点 2：`feat(physics): 自建 Box2D task-callback 桥(同步 fork-join)派发到 JobSystem`（对应 §4.2，架构 §14.2）。
- [ ] 节点 3：`feat(physics): 物理尺度与坐标转换(16px=1m, radius=0)`（对应 §4.3，架构 §8.1）。
- [ ] 节点 4：`feat(physics): 像素簇→刚体管线(CCL→MS→DP→PolyPartition→复合体)`（对应 §4.4，架构 §8.2）。
- [ ] 节点 5：`feat(physics): 不可变 body-local mask 与刚体包装/registry/damage queue`（对应 §4.5，架构 §8.3/R6）。
- [ ] 节点 6：`feat(physics): 两世界栅格化同步(erase→step→inverse-sample re-stamp)`（对应 §4.6，相位 8，不变式 #5）。
- [ ] 节点 7：`feat(physics): 破坏/挖掘重建(CCL 拆分+父子速度转移+节流)`（对应 §4.7，架构 §8.4）。
- [ ] 节点 8：`feat(physics): 静态地形局部用后即弃 collider`（对应 §4.8，架构 §8.1）。
- [ ] 节点 9：`feat(physics): 独立于 Box2D 的角色控制器(kinematic AABB vs 像素)`（对应 §4.9，架构 §8.5）。
- [ ] 节点 10：`feat(physics): PhysicsSystem 相位编排/诊断/存档快照 API`（对应 §4.10）。
- [ ] 节点 11：`test(physics): 凸分解/水密/无侵蚀/边界/task 桥并行 验收测试`（对应第 5 章，`plan/14` 协同）。
