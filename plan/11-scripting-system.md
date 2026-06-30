# Plan 11 — C# 脚本系统（PixelEngine.Scripting）

> 本文件定义开发者用 C# 编写 PixelEngine 游戏逻辑的完整机制。技术栈以 `00-conventions-and-techstack.md` 为准，架构依据 `../docs/PixelEngine-架构与需求设计.md`（下称「架构文档」），开发宪法 `../AGENTS.md`。
> 状态约定：`- [ ]` 未开始 / `- [x]` 完成并自测通过 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。
> 范围边界：本文件**只**覆盖 `PixelEngine.Scripting` 程序集与其对外的脚本 API 契约。编辑器 UI（Inspector 面板、热重载按钮的具体绘制）属 `plan/12`，本文件只定义供其调用的协作接口；各「世界能力」接口（事件总线、材质查询、粒子、物理、音频等）的**实现**分别属 plan/02、04、05、06、10，本文件只**聚合并暴露**它们的公开契约。

---

## 1. 目标与范围

### 1.1 一句话定位

PixelEngine 的脚本系统让开发者像「在 Unity 引擎之上写 Unity 游戏」一样在 PixelEngine 引擎之上写游戏：**游戏是一个独立的 .NET 项目**，通过 `ProjectReference`（仓库内 Demo）或 NuGet（外部游戏）引用引擎公开程序集，在**任意 IDE**（Rider / Visual Studio / VS Code）里靠标准 .NET 项目 + XML 文档注释获得 IntelliSense 补全与跳转，并通过 **Roslyn + 可回收 AssemblyLoadContext** 获得 Unity 式的运行时热重载迭代体验。引擎 MIT 全开源。

### 1.2 本文件覆盖（逐项可勾选见 §4/§5）

本系统交付五块能力：(1) **项目引用模型**——界定引擎公开 API 边界（哪些 `public`、哪些 `internal`），并保证所有公开 API 带中文 XML 文档注释（plan/00 §7、AGENTS §4）；(2) **Behaviour / Component / System API**——Unity 式生命周期回调，绑定到帧相位 1（架构 §3.3），组件挂载到 Demo 稀疏实体的轻量手写组件数组（架构 §13.1，绝不进 sim 内核）；(3) **世界脚本接口**——脚本可调用的引擎能力的统一 facade（cell 读写、材质查询、粒子、raycast/固体采样、刚体、角色控制器、相机、输入、事件订阅、音效），并以**延迟命令模型**保证调用落在正确相位；(4) **Roslyn 热重载**——内存编译用户脚本程序集、可回收 ALC 装卸、状态保持；(5) **异常隔离**——脚本回调抛异常被捕获、记录、禁用该脚本，绝不崩引擎（AGENTS §4）；外加**轻量 IDE 集成**（探测并 `Process.Start` 打开 IDE、生成/打开 `.sln`/`.csproj`）。

### 1.3 明确不在本范围内

不在范围：通用 ECS（架构 §13.1 已否决，sim 是 SoA 网格 DOD，不引入 archetype/查询调度）；编辑器面板的具体绘制（plan/12）；各世界能力接口的内部实现（plan/02/04/05/06/10）；游戏内容与玩法本身（plan/13 Demo）；为脚本提供额外语言服务（LSP / 自建补全）——用户已明确「倒也不需要额外支持」，靠标准 .NET 项目 + XML 注释即得 IDE 体验。

### 1.4 与不变式的关系

脚本回调运行在**帧相位 1（Game Logic / Demo）**，单线程或粗粒度并行，不进 CA 热循环（相位 4）、不进 physics step（相位 8）。脚本对权威世界状态（cell 网格、刚体世界）的**写入一律走延迟命令队列**，由引擎在对应相位（cell→particle 相位 7、刚体创建相位 8a、cell 直接写在相位 1/相位 3 前安全窗口）落地，从而不破坏架构 §3.2 的「相位顺序而非锁」与四大基石。脚本派发路径本身遵守 AGENTS §3 的稳态零分配纪律（无 LINQ、无闭包捕获、无装箱、无 `params`）；用户脚本体内的分配是用户责任，但引擎提供零分配的 API 形态（`struct` 返回、`Span<T>`、`TryX` 模式）使「写出零分配玩法逻辑」成为可能。

---

## 2. 技术栈与依赖

### 2.1 选型（不与 plan/00 §4 冲突）

脚本编译用 **Roslyn**（`Microsoft.CodeAnalysis.CSharp`，plan/00 §4「脚本编译」行）；脚本隔离用 **可回收 AssemblyLoadContext**（BCL `System.Runtime.Loader`，`isCollectible:true`，plan/00 §4「脚本隔离」行）。文件监听用 BCL `System.IO.FileSystemWatcher`。IDE 启动用 BCL `System.Diagnostics.Process`。状态保持的字段反射/序列化复用 `System.Text.Json + 源生成器`（plan/00 §4「内容序列化」行）或反射快照（编辑/迭代路径，非帧热路径，允许反射）。

### 2.2 程序集与依赖方向

`PixelEngine.Scripting` 位于 plan/00 §5 依赖链 `Demo → Hosting → {…Scripting…} → Interop → Core` 中。它**引用**：`PixelEngine.Core`（事件总线/时间/诊断/内存池/常量，plan/02）、`PixelEngine.Content`（材质查询，plan/04）、`PixelEngine.Simulation`（cell 公开只读/写 API、粒子 spawn，plan/03/05）、`PixelEngine.Physics`（刚体/角色控制器/固体采样/raycast 公开 API，plan/06）、`PixelEngine.Audio`（音效播放，plan/10）。它**被** `PixelEngine.Hosting` 装配与驱动、被 `PixelEngine.Editor` 用于 Inspector 反射与热重载触发（plan/12）。**绝不反向依赖 Demo / Editor**。

### 2.3 .csproj 约定

`PixelEngine.Scripting.csproj` 继承 `Directory.Build.props`（plan/00 §6）：`net10.0`、`Nullable=enable`、`LangVersion=14`、file-scoped namespace。开 `<GenerateDocumentationFile>true</GenerateDocumentationFile>` 并把缺失 XML 注释（CS1591）在公开类型上提升为 error（保障 IntelliSense 依赖，AGENTS §4）。本程序集**不**需要 `AllowUnsafeBlocks`（非 sim/physics 热路径）。游戏项目模板 `.csproj` 通过 `ProjectReference`（Demo）或 `PackageReference`（外部游戏的 NuGet）引用引擎公开程序集集合。

---

## 3. 详细设计

### 3.1 项目引用模型（公开 API 边界）

「等同 Unity 游戏之于 Unity 引擎」：游戏项目只看见引擎的**公开 API 层**，看不见内核内部。边界规则：

**`public`（API 层，稳定、带中文 XML 注释）**：`Behaviour`、`IComponent`、`ISystem` 及其生命周期；`Entity`/`Scene`；`IScriptContext` 及其聚合的世界能力接口（§3.3）；面向脚本的轻量值类型（`MaterialId`、`CellView`、`RaycastHit`、`BodyHandle`、`ParticleSpawnDesc` 等只读 `struct`）；特性 `[SerializeField]`/`[Persist]`/`[HideInInspector]`/`[ScriptComponent]`。

**`internal`（内核，禁止游戏直接触碰）**：CellGrid 的 SoA 数组与指针漫游、Chunk/dirty-rect/checkerboard 调度（架构 §5）、Box2D `[LibraryImport]` 绑定与 task 桥（架构 §14.2）、ALC 装卸与 Roslyn 编译管线、命令队列内部缓冲。内核仅经 `InternalsVisibleTo` 暴露给对应测试与 `PixelEngine.Editor`。**铁律（架构 §3.1、AGENTS §0）**：若 Demo 需要某能力却只能靠 `internal` 实现，说明公开 API 有缺陷，必须修引擎公开 API，而非在 Demo 开后门或把内核类设 `public`。

所有 `public` 成员**必须**带中文 XML 文档注释（含 `<summary>`、参数、返回值、以及「在哪个相位调用安全」的说明）；这是脚本 IDE 体验的唯一信息源（plan/00 §7、AGENTS §4），无需额外语言服务。

### 3.2 Behaviour / Component / System API

Unity 式三件套，挂载到 Demo 稀疏实体（架构 §13.1 的轻量手写组件数组，绝不进 sim 内核）：

```csharp
namespace PixelEngine.Scripting;

/// <summary>所有用户脚本组件的基类；生命周期回调由引擎主循环在帧相位 1 驱动（架构 §3.3）。</summary>
public abstract class Behaviour : IComponent
{
    /// <summary>所属实体。由引擎在实例化时注入，脚本中只读。</summary>
    public Entity Entity { get; internal set; } = null!;
    /// <summary>世界能力 facade；脚本经此调用引擎（见 IScriptContext）。</summary>
    protected IScriptContext Context { get; private set; } = null!;
    /// <summary>是否启用；为 false 时跳过 OnUpdate/OnFixedSimTick。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>实体激活后、首个 OnUpdate 之前调用一次（相位 1）。用于初始化。</summary>
    protected virtual void OnStart() { }
    /// <summary>每个渲染帧调用一次（相位 1）；dt 为本帧真实步长（时间膨胀下可变，架构 §4）。</summary>
    protected virtual void OnUpdate(float dt) { }
    /// <summary>仅在执行了 sim step 的帧调用（相位 1，sim 降频时跳过，架构 §4.2）；固定步长 1/60 或 1/30。用于与 sim 对齐的玩法逻辑。</summary>
    protected virtual void OnFixedSimTick() { }
    /// <summary>组件或实体被销毁、或热重载卸载前调用一次（相位 1）。用于释放订阅与句柄。</summary>
    protected virtual void OnDestroy() { }
}

/// <summary>组件标记接口；非 Behaviour 的纯数据组件可只实现本接口。</summary>
public interface IComponent { }

/// <summary>系统接口；对某类组件做批处理（替代「每实体虚调用」的可选高效路径）。引擎在相位 1 按注册顺序调用。</summary>
public interface ISystem
{
    void OnSimTick(IScriptContext context);   // 固定步逻辑（相位 1，随 sim tick）
    void OnFrame(IScriptContext context, float dt); // 每帧逻辑（相位 1）
}
```

回调相位绑定（架构 §3.3）：`OnStart`/`OnUpdate`/`OnDestroy` 在**相位 1** 每渲染帧；`OnFixedSimTick` 在**相位 1** 但仅当本帧执行 sim step（架构 §4.2 sim 可降到 30Hz，降频帧跳过），其 `dt` 取固定逻辑步长（绝不追帧，架构 §4.1）。`ISystem` 提供数据导向批处理路径，避免大量同构组件逐个虚调用的开销（与架构 §13.1 「轻量手写组件数组」一致）。

实体/组件存储（`Scene`）：稀疏实体用**按组件类型分桶的紧凑数组**（每类型一段连续 `T[]` + 活跃计数 + 自由列表），实体持 `(typeId, slot)` 句柄而非对象引用，销毁用 swap-remove。这给 `ISystem` 顺序遍历的 cache 友好布局，且不引入通用 ECS 的 archetype 迁移成本。**严格隔离在 Scripting/Demo 层，绝不进 `PixelEngine.Simulation`**。

```csharp
/// <summary>游戏实体；轻量句柄 + 组件集合（架构 §13.1，不是 sim cell）。</summary>
public sealed class Entity
{
    public int Id { get; }
    public Scene Scene { get; }
    public T AddComponent<T>() where T : class, IComponent, new();
    public bool TryGetComponent<T>(out T component) where T : class, IComponent;
    public void RemoveComponent<T>() where T : class, IComponent;
    public void Destroy(); // 延迟到相位 1 帧末结构性销毁，避免遍历中改集合
}

/// <summary>实体容器；持有所有实体与按类型分桶的组件数组，驱动 ISystem。</summary>
public sealed class Scene
{
    public Entity CreateEntity();
    public void RegisterSystem(ISystem system);
    // 由 ScriptRuntime 在相位 1 调用：DispatchStart/DispatchUpdate(dt)/DispatchFixedSimTick/FlushDestroyed
}
```

### 3.3 世界脚本接口（脚本可调的引擎能力 facade）

脚本经单一 facade `IScriptContext` 访问所有引擎能力。各子接口的**契约在此聚合暴露，实现归属对应 plan**。统一原则：**读取即时**（相位 1 看到一致的上帧末状态），**写入延迟**（入命令队列，引擎在对应相位落地，保证不破坏 §3.2 相位安全）。

```csharp
namespace PixelEngine.Scripting;

/// <summary>脚本访问引擎的统一入口；由 Hosting 在装配期注入每个 Behaviour。</summary>
public interface IScriptContext
{
    IWorldCellAccess     Cells      { get; } // cell 读写/材质（plan/03、04）
    IMaterialQuery       Materials  { get; } // 按名查材质、查属性（plan/04）
    IParticleSpawner     Particles  { get; } // spawn 自由粒子（plan/05）
    ISolidSampler        Solids     { get; } // raycast / 采样固体像素（plan/06）
    IRigidBodyApi        Bodies     { get; } // 建/查/控刚体（plan/06）
    ICharacterController Character  { get; } // 角色控制器 AABB vs 像素（plan/06、§8.5）
    ICameraApi           Camera     { get; } // 相机视口/跟随（plan/08）
    IInputApi            Input      { get; } // 键鼠/手柄（plan/02、08）
    IEventBus            Events     { get; } // 事件订阅（plan/02 事件总线）
    IAudioApi            Audio      { get; } // 播放音效（plan/10）
    IGameTime            Time       { get; } // dt、固定步、帧计数、时间膨胀系数（plan/02、§4）
    Scene                Scene      { get; } // 实体/组件
}

// ── cell 读写与材质（写入延迟到安全窗口；读取即时只读上帧末状态）──
public interface IWorldCellAccess
{
    MaterialId GetMaterial(int x, int y);          // 即时读
    CellView   Sample(int x, int y);               // 即时读：材质 + flags 快照（只读 struct）
    bool       IsSolid(int x, int y);              // 即时读
    void       SetCell(int x, int y, MaterialId material);          // 延迟写：入命令队列，相位 1 安全窗口落地并标 chunk dirty（架构 §5.4 KeepAlive）
    void       Paint(int x, int y, int radius, MaterialId material);// 延迟写：圆形批量
}

// ── 材质查询（plan/04；name 稳定键 → 运行时 id，架构 §7.3/§11.2）──
public interface IMaterialQuery
{
    MaterialId   Resolve(string name);             // "water" → MaterialId；失败返回 MaterialId.Invalid
    MaterialInfo GetInfo(MaterialId id);           // 只读属性视图（密度/类型/可燃等）
    bool         TryResolve(string name, out MaterialId id);
}

// ── 粒子（plan/05；spawn 延迟到相位 7 cell→particle 安全窗口，架构 §7.6）──
public interface IParticleSpawner
{
    void Spawn(in ParticleSpawnDesc desc);         // 延迟入队：位置/速度/材质/寿命
    void Burst(float x, float y, MaterialId material, int count, float speed); // 爆炸式抛射
}

// ── raycast / 固体采样（plan/06；即时只读网格，架构 §8.5）──
public interface ISolidSampler
{
    bool Raycast(float x, float y, float dx, float dy, float maxDist, out RaycastHit hit);
    bool SampleSolidAabb(float x, float y, float w, float h); // AABB 内是否含固体像素
}

// ── 刚体（plan/06；建/毁延迟到相位 8a，控制力延迟到相位 8 step 前，架构 §8.2/§8.3）──
public interface IRigidBodyApi
{
    BodyHandle CreateFromRegion(int x, int y, int w, int h);   // 延迟：CCL→轮廓→凸分解→Box2D（架构 §8.2）
    bool       TryGetTransform(BodyHandle h, out BodyTransform t); // 即时读上帧末 transform
    void       ApplyImpulse(BodyHandle h, float ix, float iy);  // 延迟到 step 前
    void       Destroy(BodyHandle h);                           // 延迟到相位 8a
}

// ── 角色控制器（plan/06；kinematic AABB vs 像素，独立于 Box2D，架构 §8.5）──
public interface ICharacterController
{
    CharacterHandle Create(float x, float y, float w, float h);
    void            Move(CharacterHandle h, float dx, float dy); // 延迟：相位 8 角色解算
    CharacterState  GetState(CharacterHandle h);                 // 即时读：onGround/onWall/velocity
}

public interface ICameraApi   { void SetCenter(float x, float y); void Follow(Entity target); RectF Viewport { get; } }
public interface IInputApi    { bool IsDown(Key k); bool WasPressed(Key k); float Axis(Axis a); (float X, float Y) MousePixel { get; } }
public interface IAudioApi    { void PlayAt(string cue, float x, float y, float volume = 1f); } // plan/10；入事件总线
public interface IGameTime    { float DeltaTime { get; } float FixedStep { get; } long FrameCount { get; } float TimeScale { get; } bool SimSteppedThisFrame { get; } }
```

事件订阅（plan/02 事件总线，架构 §3.1）：sim 在相位 3–8 产生的事件写入 Core 的无锁 ring buffer；脚本经 `Events.Subscribe<TEvent>(handler)` 注册，引擎在**相位 1** 排空队列并分发（与音频 plan/10 同源消费），保证脚本回调统一落在相位 1。订阅句柄实现 `IDisposable`，热重载卸载时自动退订（§3.5）。

延迟命令队列（`internal ScriptCommandQueue`）：所有「写入类」API 把请求编码为 blittable `struct` 命令压入 per-thread 缓冲（零分配，`ArrayPool`/POH），Hosting 在每个相位的安全窗口（cell 写在相位 1 末/相位 3 前；particle 在相位 7；body 在相位 8a；impulse 在相位 8c 前）批量 flush。这把脚本的自由调用顺序收敛到架构 §3.3 的相位顺序，使脚本无法制造跨相位竞争。

### 3.4 Roslyn 热重载（Unity 式迭代）

管线：`改脚本 → 重编译 → 卸载旧 ALC → 装新 ALC → 重建组件实例 → 恢复状态`。

`internal sealed class ScriptCompiler`：用 `CSharpCompilation.Create`，引用集 = 当前进程已加载的引擎公开程序集的 `MetadataReference`（`Core/Content/Simulation/Physics/Audio/Scripting` + BCL `Microsoft.NETCore.App` 参考程序集），`OutputKind.DynamicallyLinkedLibrary`，`OptimizationLevel.Release`。`Emit` 到 `MemoryStream`（PE 字节 + 可选 PDB）。`EmitResult.Diagnostics` 全部捕获：编译失败时**保留旧程序集继续运行**、把诊断（行列 + 中文摘要）上报给编辑器（plan/12）与诊断系统（plan/02），绝不让编译错误中断游戏。

`internal sealed class ScriptLoadContext : AssemblyLoadContext`：`base(name, isCollectible: true)`；`Load` 对引擎/BCL 程序集返回 `null`（回退默认上下文共享类型标识，避免「同名类型不可互转」），仅对用户脚本程序集私有装载。

`internal sealed class HotReloadService`：`FileSystemWatcher` 监听脚本源目录（去抖合并连续写事件）；触发 `RequestReload()`（也供编辑器按钮直接调用，§3.6）。**重载在帧边界相位 1 开始处执行**（结构性变更，类比架构 §3.3 相位 2 的 residency apply，绝不在 sim 中途换程序集）：

1. 对每个活跃 `Behaviour` 调 `OnDestroy()`（相位 1），并退订其事件/释放句柄；
2. 用 `[Persist]`/公开字段反射把状态序列化进瞬态 `StateSnapshot`（按 `类型全名 + 字段名 + 类型` 为键）；
3. 旧 `ScriptLoadContext.Unload()`，`GC.Collect()` + `WaitForPendingFinalizers()` 确认可回收（弱引用探测，超时则告警「ALC 未能卸载，存在泄漏引用」）；
4. 装新程序集，重建组件实例，按键回填 `StateSnapshot`（类型/名字匹配的字段恢复，**不匹配或新增字段重置为默认**）；
5. 调新实例 `OnStart()`，恢复事件订阅。

状态保持策略（可配置）：默认**保留可序列化的公开/`[Persist]` 字段并重置其余**；提供「完全重置」开关供需要干净态的迭代。不可序列化的运行时句柄（`BodyHandle`/`CharacterHandle`/订阅）**不**保留，由 `OnStart` 重建——这是避免悬挂引用旧 ALC 的关键。**绝不**跨 ALC 缓存用户类型的 `Type`/委托/实例（否则旧 ALC 无法回收，遵循 .NET 可回收 ALC 最佳实践）。

### 3.5 异常隔离（绝不崩引擎）

`internal sealed class ScriptInvoker`：每个生命周期回调与事件处理器**单独**包 `try/catch`（在脚本/宿主边界，非 AGENTS §4 所指的「库内吞异常」——这是 host 边界对用户代码的隔离，合规）。捕获到异常时：经 plan/02 诊断系统记录（异常类型 + 消息 + 脚本类型全名 + 回调名 + 帧号），把该 `Behaviour` 标 `Faulted` 并 `Enabled=false`（跳过其后续所有回调，避免每帧刷异常），编辑器（plan/12）高亮该脚本。一个脚本出错**绝不**影响同帧其它脚本、也绝不冒泡到主循环。提供 `ResetFault()`（修脚本热重载后清除 Faulted 态）。回调零分配派发：用缓存的开放委托/虚调用，catch 块只在异常路径分配（稳态零分配，AGENTS §3）。

### 3.6 IDE 集成（轻量）

`internal sealed class IdeLauncher`：探测已安装 IDE——Rider（`%LOCALAPPDATA%\Programs\Rider*`/Toolbox、`rider` on PATH）、Visual Studio（`vswhere`）、VS Code（`code` on PATH / 注册表）；按优先级 `Process.Start` 打开游戏 `.sln`。`internal sealed class ProjectGenerator`：从模板生成/刷新游戏 `.csproj`（引用引擎公开程序集集合，开 `GenerateDocumentationFile` 以承接引擎 XML 注释）与 `.sln`，确保「新建脚本」后 IDE 立即可见。**不做**额外语言服务——标准 .NET 项目 + 引擎 XML 注释已提供补全/跳转/签名提示（用户明确「倒也不需要额外支持」）。

### 3.7 与编辑器（plan/12）协作

`public ScriptFieldDescriptor[] InspectField(Behaviour b)`：反射枚举 `[SerializeField]`/公开字段（排除 `[HideInInspector]`），产出名字/类型/当前值/可写性的描述，供 plan/12 Inspector 绘制与编辑（编辑器/迭代路径，非帧热路径，允许反射）。`HotReloadService.RequestReload()` 是编辑器「热重载按钮」入口（§3.4）。编辑器对脚本组件值的修改经回写 setter，下一相位 1 生效。

### 3.8 与 Hosting 协作

脚本生命周期由 `PixelEngine.Hosting` 主循环驱动（plan/00 §5）。`public interface IScriptRuntime`：`Initialize(IScriptContext)`、`BeginFrame()`（相位 1 开始：apply 待处理热重载 → 分发 OnStart）、`Update(float dt)`（相位 1：分发 OnUpdate + ISystem.OnFrame + 事件排空）、`FixedSimTick()`（相位 1 且 SimSteppedThisFrame：分发 OnFixedSimTick + ISystem.OnSimTick）、`EndFrame()`（flush 销毁队列）、`Shutdown()`。Hosting 在装配期构造 `IScriptContext` 并把各子系统公开 API 注入其字段。

---

## 4. 实现清单

### 4.1 项目引用模型与公开 API 边界
- [x] 建 `PixelEngine.Scripting.csproj`，继承 `Directory.Build.props`，开 `GenerateDocumentationFile`、CS1591 公开类型 error（§2.3、plan/00 §6）
- [~] 审定并标注公开 API 表面：`Behaviour`/`IComponent`/`ISystem`/`Entity`/`Scene`/`IScriptContext` 及全部子接口/值类型为 `public`；内核（CellGrid SoA、checkerboard、Box2D 桥、ALC、编译管线、命令队列）为 `internal`（§3.1，架构 §3.1/§13.1）
- [x] 配置 `InternalsVisibleTo`：仅 `PixelEngine.Scripting.Tests` 与 `PixelEngine.Editor`（§3.1）
- [x] 为**所有** `public` 成员写中文 XML 文档注释，含 `<summary>`/参数/返回值/「相位安全」说明（§3.1，plan/00 §7、AGENTS §4）
- [ ] 建游戏项目 `.csproj` 模板与引用集合（`ProjectReference` for Demo / `PackageReference` for 外部 NuGet），承接引擎 XML 注释（§3.6）

### 4.2 Behaviour / Component / System API（相位 1，架构 §3.3）
- [x] `abstract class Behaviour`：`Entity`/`Context`/`Enabled` + 生命周期 `OnStart()`/`OnUpdate(float dt)`/`OnFixedSimTick()`/`OnDestroy()`（§3.2）
- [x] `interface IComponent` 标记接口；`interface ISystem` 含 `OnSimTick`/`OnFrame`（数据导向批处理，§3.2、架构 §13.1）
- [x] `sealed class Entity`：`AddComponent<T>`/`TryGetComponent<T>`/`RemoveComponent<T>`/`Destroy()`（延迟销毁，§3.2）
- [x] `sealed class Scene`：按组件类型分桶的紧凑数组 + 自由列表 + swap-remove；`CreateEntity`/`RegisterSystem`；`DispatchStart`/`DispatchUpdate(dt)`/`DispatchFixedSimTick`/`FlushDestroyed`（§3.2，绝不进 sim 内核）
- [x] 特性 `[SerializeField]`/`[Persist]`/`[HideInInspector]`/`[ScriptComponent]`（§3.1/§3.7）
- [ ] 派发路径零分配验证：无 LINQ/闭包/装箱/`params`，缓存开放委托或虚调用（AGENTS §3）

### 4.3 世界脚本接口 facade（§3.3）
- [x] `interface IScriptContext` 聚合 `Cells/Materials/Particles/Solids/Bodies/Character/Camera/Input/Events/Audio/Time/Scene`（§3.3）
- [x] `interface IWorldCellAccess`：即时读 `GetMaterial/Sample/IsSolid`，延迟写 `SetCell/Paint`（入命令队列，相位 1 安全窗口落地 + 标 dirty，架构 §5.4）
- [x] `interface IMaterialQuery`：`Resolve/TryResolve/GetInfo`（name 稳定键，plan/04，架构 §7.3/§11.2）
- [x] `interface IParticleSpawner`：`Spawn/Burst`（延迟到相位 7，plan/05，架构 §7.6）
- [x] `interface ISolidSampler`：`Raycast/SampleSolidAabb`（即时只读网格，plan/06，架构 §8.5）
- [x] `interface IRigidBodyApi`：`CreateFromRegion/TryGetTransform/ApplyImpulse/Destroy`（建毁延迟相位 8a、力延迟 step 前，plan/06，架构 §8.2/§8.3）
- [x] `interface ICharacterController`：`Create/Move/GetState`（kinematic AABB，延迟相位 8，plan/06，架构 §8.5）
- [x] `interface ICameraApi`/`IInputApi`/`IAudioApi`/`IGameTime`（plan/08/02/10，架构 §4/§10）
- [~] 事件订阅：`IEventBus.Subscribe<TEvent>` + `IDisposable` 句柄；引擎相位 1 排空 ring buffer 分发（plan/02，架构 §3.1）
- [x] 只读值类型：`MaterialId`/`CellView`/`MaterialInfo`/`RaycastHit`/`BodyHandle`/`BodyTransform`/`CharacterHandle`/`CharacterState`/`ParticleSpawnDesc`/`RectF`（blittable `readonly struct`，零分配）
- [~] `internal ScriptCommandQueue`：blittable 命令 struct + per-thread 缓冲（`ArrayPool`/POH，零分配）；Hosting 在各相位安全窗口 flush（§3.3，架构 §3.3）

### 4.4 Roslyn 热重载（§3.4）
- [ ] `internal sealed class ScriptCompiler`：`CSharpCompilation` + 引擎公开程序集 `MetadataReference` + BCL 参考程序集；`Emit` 到 `MemoryStream`；捕获并上报 `Diagnostics`（编译失败保留旧程序集，§3.4）
- [ ] `internal sealed class ScriptLoadContext : AssemblyLoadContext`（`isCollectible:true`；引擎/BCL 类型回退默认上下文，仅用户脚本私有装载）
- [ ] `internal sealed class HotReloadService`：`FileSystemWatcher` 去抖；`RequestReload()`；重载在**帧边界相位 1 开始**执行（§3.4，架构 §3.3 相位 2 同理）
- [ ] 热重载五步：OnDestroy+退订 → `StateSnapshot` 反射快照 → `Unload()`+GC 确认可回收（弱引用探测+超时告警）→ 重建实例+回填状态 → OnStart+恢复订阅（§3.4）
- [ ] 状态保持策略：默认保留 `[Persist]`/公开字段、其余重置；提供「完全重置」开关；句柄/订阅不保留由 OnStart 重建（§3.4）
- [ ] 防泄漏：禁止跨 ALC 缓存用户 `Type`/委托/实例；句柄经稳定 id 而非对象引用（§3.4，.NET 可回收 ALC 最佳实践）

### 4.5 异常隔离（§3.5，AGENTS §4）
- [x] `internal sealed class ScriptInvoker`：每回调/事件处理器单独 `try/catch`（host 边界）
- [~] 捕获后：经 plan/02 诊断记录（异常+脚本类型+回调名+帧号）、标 `Faulted` + `Enabled=false`、通知编辑器；绝不冒泡主循环
- [x] `ResetFault()`：热重载修复后清除 Faulted（§3.5）
- [x] 验证：单脚本异常不影响同帧其它脚本、不崩引擎（对应 §5 验收）

### 4.6 IDE 集成（轻量，§3.6）
- [ ] `internal sealed class IdeLauncher`：探测 Rider/VS（vswhere）/VS Code（PATH/注册表），按优先级 `Process.Start` 打开 `.sln`
- [ ] `internal sealed class ProjectGenerator`：从模板生成/刷新游戏 `.csproj`/`.sln`，开 `GenerateDocumentationFile`（§3.6）
- [ ] 「新建脚本」流程：写入源文件 → 刷新项目 → 触发热重载（§3.4/§3.6）

### 4.7 编辑器与 Hosting 协作
- [ ] `public ScriptFieldDescriptor[] InspectField(Behaviour b)`：反射枚举 `[SerializeField]`/公开字段（排除 `[HideInInspector]`），供 plan/12 Inspector（§3.7）
- [ ] `HotReloadService.RequestReload()` 暴露为编辑器热重载入口（§3.7）
- [ ] `public interface IScriptRuntime`：`Initialize/BeginFrame/Update(dt)/FixedSimTick/EndFrame/Shutdown`，由 Hosting 主循环相位 1 驱动（§3.8，架构 §3.3）
- [ ] Hosting 装配期构造 `IScriptContext` 并注入各子系统公开 API（§3.8）

---

## 5. 验收标准
- [ ] 游戏项目仅经 `ProjectReference`/`PackageReference` 引用引擎公开程序集即可编译并运行；不引用任何 `internal` 内核类型（§3.1，架构 §3.1）
- [ ] 在 Rider / VS / VS Code 任一中打开游戏项目，`Behaviour`/`IScriptContext`/世界 API 均有中文 IntelliSense 补全、签名提示与跳转（靠 XML 注释，无额外语言服务，§3.1/§3.6）
- [x] CS1591 在公开类型上为 error：缺任一公开成员 XML 注释则构建失败（§2.3，AGENTS §4）
- [ ] 一个继承 `Behaviour` 的脚本，其 `OnStart`/`OnUpdate(dt)`/`OnFixedSimTick`/`OnDestroy` 按相位 1 节奏被正确调用；sim 降到 30Hz 时 `OnFixedSimTick` 跳过、`OnUpdate` 仍每帧（§3.2，架构 §4.2）
- [x] 组件挂载到 Demo 稀疏实体且**不**出现在 `PixelEngine.Simulation` 内核数据结构中（§3.2，架构 §13.1）
- [ ] 脚本经 facade 完成：读写 cell、查材质（按 name）、spawn 粒子、raycast/采样固体、建/查/控刚体、驱动角色控制器、控相机、读输入、订阅事件、播放音效——全部走公开 API（§3.3）
- [ ] 脚本写入类调用经命令队列在正确相位落地：cell 写标 chunk dirty 并被下帧 CA 看见；particle 落相位 7；body 建于相位 8a——无跨相位竞争、不破坏 §3.2 相位安全（架构 §3.3）
- [ ] 修改脚本源文件后自动重编译并热重载：旧 ALC 成功 `Unload` 并被 GC 回收（弱引用确认）、组件实例重建、`[Persist]`/公开字段状态按策略恢复（§3.4）
- [ ] 编译错误不中断运行：保留旧程序集，诊断（行列+中文摘要）上报编辑器（§3.4）
- [x] 脚本任一回调抛异常被捕获、记录、该脚本被禁用，**引擎主循环继续、同帧其它脚本不受影响、进程不崩溃**（§3.5，AGENTS §4）
- [ ] 反复热重载 N 次（如 50 次）无 ALC/内存泄漏（无残留可回收上下文，无单调增长的句柄/委托缓存，§3.4）
- [ ] 稳态帧脚本派发路径零托管堆分配（命令队列/事件分发/回调派发，BenchmarkDotNet 内存诊断为 0，§3.3/§3.5，AGENTS §3）
- [ ] `IdeLauncher` 在装有 Rider/VS/VS Code 的机器上能正确探测并打开 `.sln`；`ProjectGenerator` 生成的项目可被 IDE 直接加载（§3.6）
- [ ] 编辑器经 `InspectField` 能展示并编辑脚本组件的 `[SerializeField]`/公开字段，修改下一相位 1 生效（§3.7）

---

## 6. 依赖关系

前置（必须先完成或并行就绪）：`plan/01`（解决方案/CPM/Directory.Build）、`plan/02`（Core：事件总线/时间/诊断/内存池/常量——本系统的 facade 直接消费）、`PixelEngine.Hosting`（主循环相位驱动与装配，plan/00 §5）。

facade 聚合的能力实现来自：`plan/03`（cell 读写）、`plan/04`（材质查询）、`plan/05`（粒子 spawn）、`plan/06`（刚体/角色控制器/raycast/固体采样）、`plan/08`（相机/输入）、`plan/10`（音效）。本系统可先用各子系统的**公开接口契约**对接，接口未就绪的能力以 `- [!] 阻塞：依赖 plan/0x 接口` 标记，不写假实现（AGENTS §2）。

下游消费者：`plan/12`（编辑器 Inspector 反射、热重载按钮入口）、`plan/13`（Demo 落沙游戏 + 可操作角色，仅依赖本系统暴露的公开 API——是公开 API 的 dogfood 验证，架构 §3.1/§1.2）。

执行顺序（plan/README、plan/17）：位于「可编程与可编辑：11 → 12」，刻意置于 sim/物理稳定（03–07）之后、编辑器（12）与 Demo（13）之前。

---

## 7. 提交节点

按 AGENTS §6 每完成一个节点用中文 git 提交（`scope=script`）：

- [ ] `feat(script): 建立 Scripting 项目与公开 API 边界(项目引用模型+XML 注释门禁)`（对应 §4.1）
- [ ] `feat(script): 实现 Behaviour/Component/System 与实体组件存储(相位1生命周期)`（对应 §4.2）
- [ ] `feat(script): 实现 IScriptContext 世界脚本接口 facade 与延迟命令队列`（对应 §4.3）
- [ ] `feat(script): 实现 Roslyn 编译与可回收 ALC 热重载(状态保持+防泄漏)`（对应 §4.4）
- [ ] `feat(script): 实现脚本回调异常隔离(记录/禁用/不崩引擎)`（对应 §4.5）
- [ ] `feat(script): 实现轻量 IDE 集成与项目生成(Rider/VS/VSCode 启动)`（对应 §4.6）
- [ ] `feat(script): 接通编辑器 Inspector 反射与 Hosting 主循环驱动`（对应 §4.7）
- [ ] `test(script): 脚本系统验收测试(热重载无泄漏/异常隔离/相位安全/零分配)`（对应 §5）
