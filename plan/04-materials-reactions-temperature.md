# Plan 04 — 材质 / 反应 / 温度（Materials / Reactions / Temperature）

> 范围锚定：本文档定义**材质定义、反应表、温度场**的运行时数据模型与执行逻辑。权威依据：架构文档 §7.3 / §7.4 / §7.5（材质 / 反应 / 温度），并受 §5.3（parity）、§5.5（KeepAlive）、§5.8（32px halo）、§11.2（name↔id）、§4.3（过载降级）约束。技术栈与全局约定见 `00-conventions-and-techstack.md`，开发宪法见 `../AGENTS.md`。
> 状态约定：`- [ ]` 未开始 / `- [x]` 完成自测 / `- [~]` 进行中 / `- [!]` 阻塞（后跟原因）。

---

## 1. 目标与范围

本文档交付 PixelEngine 世界丰富度的「材质语义层」：把一个 `Material` id 翻译成可被 CA 内核消费的物理参数（密度、扩散、可燃性、相变阈值、导热、生命周期等），并提供两套以材质为输入的运行时执行：**邻居对反应表**（接触反应，含火传播）与**温度场**（概率热传导 + 阈值相变）。所有表均为启动期构建的**扁平、cache-aware、运行时零字符串 / 零字典**结构，热路径上对惰性材质一次比较即早退。

明确的范围边界（不得越界）：

CA 的**移动机制本身**（powder / liquid / gas swap、bottom-up 扫描、checkerboard 调度、dirty-rect、parity 时钟位的实现、KeepAlive 的实现）属于 `plan/03-simulation-kernel.md`。本文档只定义 movement 所**消费的材质字段**（如 `Density`、`Dispersion`、`Type`、`LiquidStatic`、`LiquidSand`），并定义反应 / 温度 pass 在 CA 帧相位中的**挂入点与执行契约**，但调度框架由 plan/03 提供。

从 **JSON 文件加载**（反序列化、文件 IO、资产载入、`[tag]` 实际展开执行、name→id 实际分配、JSON 源生成 context）由 **`PixelEngine.Content` 模块**（其内容管线计划文档）负责落地实现。本文档负责：定义 `materials.json` / `reactions.json` 的**数据格式与字段语义**、定义 `[tag]` 语法糖的**展开规则**、定义运行时扁平表的**目标结构**、以及加载产物（表）被消费的**执行逻辑**。即「格式与目标结构在此定义、加载实现在 Content」。

存档时 name↔id 表的**磁盘格式与 remap 字节落地**由 `plan/07-world-streaming-serialization.md` 负责；本文档定义 name↔id 映射的**内存契约与 remap 原语**供其调用（不变式 #8）。材质纹理 / `BaseColorBGRA` 的**渲染采样与上色**由 `plan/08-rendering.md` 负责；本文档只定义字段。`Lifetime` 倒计时驱动的**通用 cell 生命周期循环**与 cell↔particle handshake 由 `plan/05-particles-lifecycle.md` 负责；本文档定义 fire 的点燃 / 燃烧位语义与 `DefaultLifetime` / `FireHp` 字段，并把 burnout 的寿命消费交给 plan/05。材质音效播放由 `plan/10-audio.md` 负责；本文档只定义 `AudioCueSet` 字段挂点。

工程哲学遵循 `AGENTS.md §2`：一步到位、无 MVP、无 stub；性能默认拉满（SoA 扁平表、SIMD stencil、零热路径分配、可降频可降级）。

---

## 2. 技术栈与依赖

与 `00-conventions-and-techstack.md` 完全一致，不另立选型：

- 运行时 / 语言：.NET 10 LTS、C# 14；`Nullable` enable、file-scoped namespace；本模块涉及热路径与 SIMD，所在项目开 `AllowUnsafeBlocks`、`Optimize`（Release）。
- 数学 / SIMD：`System.Numerics` + `System.Runtime.Intrinsics`（`Avx2` / `Avx512F` / `Avx10v2` + `Vector<T>`），温度 stencil 必带 scalar fallback；`gate on Vector512.IsHardwareAccelerated`（架构 §12.5）。
- 内容序列化（格式定义侧）：`System.Text.Json` + 源生成器（DTO schema 在此定义，`JsonSerializerContext` 实现在 Content）。
- RNG：复用 `PixelEngine.Core` 的每 chunk 可种子化 / counter-based RNG（确定性 seam，架构 §6.2），反应概率与液体抖动共用，**热路径不 new Random**。
- 常量：`EngineConstants.ChunkSize=64`、`EngineConstants.MoveCap=32`、`EngineConstants.TempFieldDownscale=4`（集中于 `PixelEngine.Core`，`00 §7`）。
- 内存：温度场 ping-pong 缓冲与反应 packed 数组走 `GC.AllocateArray(pinned:true)` / `ArrayPool<T>`，稳态帧零托管分配（`AGENTS.md §3`）。

模块归属（见 §3.1 详述）：数据模型类型与执行体（`MaterialDef` / `Reaction` / 注册表 / `ReactionEngine` / `TemperatureField`）置于 **`PixelEngine.Simulation`**；JSON DTO 与加载 / 展开实现置于 **`PixelEngine.Content`**（`Content → Simulation` 依赖，不构成反向依赖）。

---

## 3. 详细设计

### 3.1 模块归属与依赖（关键决策）

CA 反应 / 温度执行在每帧热路径中被调用，必须直接持有 `MaterialDef[]` 与 packed `Reaction[]`。依据不变式 #9（CPU sim 权威、热路径自洽）与「依赖绝不反向」原则（`00 §5`），本文档作如下归属决策并显式记录：

**数据模型类型与运行时扁平表、所有执行体定义在 `PixelEngine.Simulation`**：`MaterialDef`、`MaterialHotTable`、`Reaction` / `ReactionFlags`、`MaterialProperty`、`MaterialTag`、`MaterialTable`（兼 name↔id 注册表）、`ReactionEngine`、`TemperatureField`、`MaterialCustomUpdate` 委托与注册表。

**`PixelEngine.Content` 作为加载器**：反序列化 `materials.json` / `reactions.json`，执行 `[tag]` 展开、name→id 分配，**填充上述 Simulation 拥有的表类型**；因此 `Content → Simulation` 依赖。架构 §3.1 所述「Content 维护 name→id 映射」由此实现为：Content 在加载期写入 Simulation 定义的注册表实例。本决策与架构 §7.3/§7.4 的字段定义、与 `00 §5` 的「Content 负责加载」一致，不与任何不变式冲突。

> 说明：`CellType` 枚举（架构 §7.2 的 6 值：`Empty/Solid/Powder/Liquid/Gas/Fire`）作为 cell 模型基础类型，由 `plan/03` 在 Simulation 内定义；本文档 `MaterialDef.Type` 引用之，不重复定义。

### 3.2 `MaterialDef` 数据模型（架构 §7.3）

`MaterialDef` 是数据驱动材质定义，启动期由 Content 从 JSON 构建进 `MaterialDef[]`，以**运行时 id** 索引。采用 `readonly record struct`（值类型、便于 `ref readonly` 只读传递、避免堆分配）。完整字段（涵盖架构 §7.3 全部）：

```csharp
namespace PixelEngine.Simulation;

/// <summary>材质定义。Name 为稳定字符串键（入盘/热重载基准），Id 为运行时分配的索引（绝不入盘，见 §11.2 / 不变式 #8）。</summary>
public readonly record struct MaterialDef
{
    public ushort   Id;                  // 运行时分配索引（数组下标），不入盘
    public string   Name;                // 稳定键，如 "water"（入盘/remap 基准）
    public CellType Type;                // 选择 movement 规则集（架构 §7.2，类型由 plan/03 定义）

    // —— movement 消费字段（实现机制在 plan/03）——
    public byte     Density;             // 邻居 Density < 我 → swap（液/气位移；油浮水沉，架构 §7.3）
    public byte     Dispersion;          // 液体/气体每 CA 步最大水平铺开 cell 数（viscosity 反量）；语义化对外别名 = FlowRate（编辑器/图例/GetInfo 读此值），加载期强制 clamp 到 [0, EngineConstants.MoveCap]=32 并加构建期断言（守 #4，杜绝越过 move cap，§3.11）
    public bool     LiquidStatic;        // 不流动的类固体液体（Noita liquid_static）
    public bool     LiquidSand;          // 粉末式液体（Noita liquid_sand）

    // —— 燃烧 / 火 ——
    public byte     Flammability;        // 接触点燃概率权重（0-255）
    public ushort   AutoIgnitionTemp;    // 自燃温度阈值（°C，热场启用时检查）
    public int      FireHp;              // 燃烧耐久；-1 = 永燃
    public byte     TemperatureOfFire;   // 燃烧时向温度场注入的热量基准
    public byte     GeneratesSmoke;      // 燃烧/反应产烟倾向（0 = 不产烟）

    // —— 相变阈值 + 目标（不进反应表，由热场驱动，架构 §7.4）——
    public float    MeltPoint;   public ushort MeltTarget;    // ≥ 阈值 → 转 MeltTarget
    public float    FreezePoint; public ushort FreezeTarget;  // ≤ 阈值 → 转 FreezeTarget
    public float    BoilPoint;   public ushort BoilTarget;    // ≥ 阈值 → 转 BoilTarget

    // —— 热学 ——
    public byte     HeatConduct;         // 每帧传导概率（TPT 约定，0-255）
    public float    HeatCapacity;        // 温变加权（必须非零，校验项）

    // —— 生命周期 / 耐久 ——
    public ushort   DefaultLifetime;     // fire/gas 默认倒计时（lifecycle 在 plan/05 消费）
    public byte     Durability;          // 抗性系数（被 sim ApplyDamage 真实消费，§3.11）：单次伤害 < Durability*DamageAbsorb 被完全吸收（不掉血）；取代原「抗腐蚀/抗挖」模糊语义，加载期沿用 durability 字段直迁移

    // —— 结构完整度 / 破坏（数据驱动；per-cell 累计 Damage byte lane 与 ApplyDamage 执行在 plan/03，材质侧契约见 §3.11）——
    public ushort   Integrity;           // 固体 cell 满结构完整度（生命值，单位=伤害点上限）；0 = 即时破坏（沙/水一碰即抛），>0 = per-cell 累计 Damage 达此值才破坏
    public ushort   DestroyedTarget;     // 破坏产物材质 id（入盘写稳定 name，加载期 name→id 解析，守 #8）；stone→gravel、wood→ash；==0(Empty) 破坏后直接清空
    public byte     DebrisCount;         // 破坏时抛出碎屑粒子数（0 = 不抛）；粒子材质 = DestroyedTarget 或本材质（handshake plan/05）
    public byte     MineYield;           // 采集掉落值：>0 且置 Diggable 位时，该 cell 被玩家武器破坏发一次采集事件（crystal=1）；0 = 非矿物（§3.11）

    // —— 渲染 / 上色（采样实现在 plan/08）——
    public int      TextureId;           // 材质纹理索引（-1 = 仅用纯色）
    public uint     BaseColorBGRA;       // 基色（BGRA8，匹配上传格式，架构 §9.2）
    public byte     ColorNoise;          // 便宜噪声幅度（替代纹理）

    // —— 视觉可辨识（渲染相位 CPU 按 id 查表算 BGRA，只存 MaterialDef，绝不写回 cell，守 #7；着色实现在 plan/08）——
    public MaterialRenderStyle    RenderStyle;      // 着色风格；驱动 plan/08 差异化着色与图例分组（进 MaterialVisualTable 热列，§3.2）
    public MaterialLegendCategory LegendCategory;   // 玩家图例分组（Terrain/Liquid/Gas/…）
    public uint     EdgeColorBGRA;       // 固体/可破坏物边缘描边色（BGRA8，0 = 不描边）
    public byte     Opacity;             // 渲染不透明度 0-255（气体半透明；默认 255）
    public uint     HighlightColorBGRA;  // 液体流动高光/表面叠加色（BGRA8，0 = 不叠加）
    public string   DisplayName;         // 玩家可读名（冷字段，绝不入 cell/热路径）
    public bool     LegendVisible;       // 是否出现在玩家图例（中间产物如 acid_gas 置 false）

    // —— 标记 / 反应 / 音效 ——
    public uint     PropertyFlags;       // MaterialProperty 位域（含 tag 成员位，见 §3.4）
    public int      ReactionStart;       // 指向 packed Reaction[] 的本材质切片起点
    public byte     ReactionCount;       // 本材质反应数；==0 → 热路径一次比较早退（§3.5）
    public AudioCueSet AudioCues;        // 材质化音效钩子（架构 §10.2，播放在 plan/10）
}
```

`MaterialProperty`（`PropertyFlags` 的语义位域，含 tag 成员位）：

```csharp
[Flags]
public enum MaterialProperty : uint
{
    None          = 0,
    // tag 成员位（加载期由 JSON tags 写入，供 [tag] 展开按位筛选材质，§3.4）
    Meltable      = 1u << 0,
    Acid          = 1u << 1,
    Fire          = 1u << 2,
    Corrodible    = 1u << 3,
    Cold          = 1u << 4,
    MoltenMetal   = 1u << 5,
    Static        = 1u << 6,
    BurnableFast  = 1u << 7,
    // 运行时行为位
    Emissive      = 1u << 8,   // 发光（renderer/emissive buffer 消费，plan/08）
    HasCustomUpdate = 1u << 9, // 有 custom-update 委托（§3.8 快速门控）
    Conductive    = 1u << 10,  // 预留：导电（脚本/反应可用）
    // 破坏行为位（§3.11 ApplyDamage 消费）
    Indestructible = 1u << 11, // 免疫破坏：ApplyDamage 对其 no-op（如 boundary_stone / 关卡边界 bedrock），不累计 Damage
    Diggable      = 1u << 12,  // 可被 Excavator/采矿工具挖掘；配合 MineYield 触发采集事件
    // bit13+ 预留
}

/// <summary>[tag] 标签 → MaterialProperty 成员位 的固定映射，加载期 tag 展开使用（§3.4）。</summary>
public enum MaterialTag : byte
{
    Meltable, Acid, Fire, Corrodible, Cold, MoltenMetal, Static, BurnableFast
}

/// <summary>材质着色风格（渲染相位按 id 查表算 BGRA，绝不写回 cell，守 #7；差异化着色在 plan/08）。</summary>
public enum MaterialRenderStyle : byte { Ground, Powder, Liquid, Gas, Solid, Destructible, Hazard, Emissive }

/// <summary>玩家材质图例分组（plan/13 MaterialLegendHud 消费，编辑器 MaterialLegendPreview 对照，plan/12）。</summary>
public enum MaterialLegendCategory : byte { Terrain, Liquid, Gas, Destructible, Hazard, Resource, Special }
```

`AudioCueSet`（音效挂点，句柄由 plan/10 解析）：

```csharp
/// <summary>材质化音效钩子集合（per-event，绝不 per-cell，架构 §10.2）。句柄解析与播放在 plan/10。</summary>
public readonly record struct AudioCueSet
{
    public int ImpactCue;     // 粒子高速沉积
    public int FireCue;       // 燃烧 crackle（区域聚合）
    public int SplashCue;     // 液体飞溅/落水
    public int ExplosionCue;  // 爆炸/冲击
    public int ShatterCue;    // 刚体破碎/拆分
    public int AmbientCue;    // 区域材质 ambient loop
}
```

**`MaterialHotTable`（SoA 热表，性能优化）**：`MaterialDef[]` 含 `string` / `AudioCueSet` 等冷字段，热路径只读 `Density/Dispersion/Type/HeatConduct/HeatCapacity/相变阈值/ReactionStart/ReactionCount/PropertyFlags`。为避免把冷字段拉进 cache line（`AGENTS.md §3` SoA 纪律、架构 §7.1），加载期从 `MaterialDef[]` 派生一份 SoA 热表（按字段分列的并行数组），movement / reaction / temperature 内层循环只触碰热表。`MaterialDef[]` 保留为权威 / 工具 / 编辑路径。

**破坏热路径列（§3.11）**：热表并列 `_durability` / `_integrity` / `_destroyedTarget` 列，且 `_propertyFlags` 列携带 `Indestructible` / `Diggable` 位，供 plan/03 `ApplyDamage` 按 id 单次只读查表判定抗性 / 生命值 / 产物 / 免疫，零热路径分配（守 SoA 纪律）。**渲染视觉表（`MaterialVisualTable`）**：视觉字段（`RenderStyle` / `EdgeColorBGRA` / `Opacity` / `HighlightColorBGRA` / `LegendCategory`）另加载期派生一份并列 SoA `MaterialVisualTable`，供 plan/08 渲染相位 CPU 按 id 查表算 BGRA、**绝不写回 cell（守 #7）**；`DisplayName` / `LegendVisible` 等纯冷字段仍留权威 `MaterialDef[]`，由图例 / 编辑器按需读（非每 cell 每帧热路径）。

### 3.3 name 稳定键 ↔ 运行时 id 映射（不变式 #8，架构 §11.2 / §17.4）

运行时数值 id 仅作数组索引、**绝不入盘**；入盘一律稳定字符串键 `Name`。`MaterialTable` 同时充当注册表，持有扁平表与映射：

```csharp
public sealed class MaterialTable
{
    private MaterialDef[] _defs;                       // 以运行时 id 索引（权威）
    private MaterialHotTable _hot;                     // SoA 热表（§3.2）
    private readonly Dictionary<string, ushort> _nameToId;  // 仅 加载/存档/热重载 用，非热路径

    public ref readonly MaterialDef Get(ushort id);   // 热路径只读访问
    public bool TryGetId(string name, out ushort id);
    public ushort GetIdOrFallback(string name, ushort fallback);
    public string GetName(ushort id);
    public int Count { get; }

    // —— 供 serialization(plan/07) 的内存契约（remap 原语）——
    /// <summary>导出当前 id→name 表，写入存档头（落盘格式在 plan/07）。</summary>
    public (ushort id, string name)[] BuildIdNameTable();
    /// <summary>据存档头的 savedId→name 表，构建 savedId→currentId 的 remap LUT；
    /// 缺失材质映射到 fallback（架构 §11.2/§11.4）。remap 应用在 plan/07。</summary>
    public ushort[] BuildRemapLut(ReadOnlySpan<(ushort savedId, string name)> savedTable, ushort fallbackId);

    // —— 热重载稳定分配（架构 §17.4，触发 UI 在 plan/12）——
    /// <summary>增量重载：保留既有 name→id，新材质追加新 id，删除材质保留 id 作 tombstone；
    /// 绝不重排既有 id（否则损坏 live 网格引用），返回被 tombstone 的 id 列表供诊断。</summary>
    public ushort[] ReloadStable(ReadOnlySpan<MaterialDef> newDefs);
}
```

约束：`_nameToId` 仅在加载 / 存档 / 热重载使用，**绝不进 sim 每帧热循环**（热循环只用 id 索引）。`ReloadStable` 保证 id 稳定是 live 网格与存档双重正确性的基石；删除材质的 live cell 在重载后重映射到 fallback 并产出「替换了 N 个被删材质活 cell」诊断（架构 §17.4）。本注册表为 plan/07（存档 remap）、plan/11（脚本按 name 查 id）、plan/12（编辑器材质编辑 / 热重载触发）共同消费。

### 3.4 反应表数据模型与 `[tag]` 展开（架构 §7.4）

反应是「`A + B 接触 → C + D`，概率 p」的全数据驱动表。运行时 `Reaction` 为 `readonly record struct`（精确镜像架构 §7.4 字段）：

```csharp
public readonly record struct Reaction
{
    public ushort InputA;   // 本切片所属材质（owner，见 §3.5 定向语义）
    public ushort InputB;   // 反应对方材质
    public ushort OutputA;  // InputA 位置的产物
    public ushort OutputB;  // InputB 位置的产物
    public byte   Probability; // 0-255（Noita rate 0-100 在加载期映射：round(rate*255/100)）
    public byte   Flags;    // ReactionFlags
}

[Flags]
public enum ReactionFlags : byte
{
    None          = 0,
    Fast          = 1 << 0, // 双消耗/高优先：同 tick 两侧均转化，先于普通反应裁决（架构 §7.4 bit0）
    Directional   = 1 << 1, // 定向：仅在 InputA→InputB 取向触发（不双向镜像，见 §3.6）
    SpawnParticle = 1 << 2, // 产物之一抛为自由粒子（handshake 到 plan/05）
    EmitHeat      = 1 << 3, // 向温度场注入热量（§3.9）
    // bit4-7 预留：定向反应可选编码 von Neumann 方向码（默认按取向/扫描序裁决，§3.6）
}
```

**`[tag]` 语法糖在加载期展开（架构 §7.4，运行时零字符串 / 零字典）**：作者在 `reactions.json` 用 `[meltable] / [acid] / [fire] / [corrodible] / [cold] / [molten_metal] / [static] / [burnable_fast]` 作 `InputA/InputB/OutputA/OutputB` 的占位。展开规则（实现在 Content，规则在此定义）：

1. 每个 `[tag]` 解析为 `MaterialTag`，对应 `MaterialProperty` 成员位；其成员集合 = 所有 `PropertyFlags` 含该位的材质 id（材质 JSON 用 `tags` 声明，加载期置位）。
2. 一条含 tag 的规则展开为输入对的**笛卡尔积**：对 `InputA` 集合 × `InputB` 集合的每个具体材质对生成一条具体 `Reaction`。
3. 输出端 tag 的解析：若 OutputA/OutputB 也是 tag（如 `[fire]+[burnable_fast]→[fire]+fire`），约定 `[fire]` 这类输出 tag 取该 tag 的**代表材质**（JSON 中 tag 声明一个 `representative`，默认取集合首个稳定排序材质）；具体材质输出（如 `steam`）直接按 name 解析 id。
4. **有序对去重（`min(matA,matB)` 归一）**：展开后对每个无序对 `{a,b}` 去重，作者重复 / 对称定义（同时写 `(water,lava)` 与 `(lava,water)`）坍缩为一条逻辑反应（架构 §7.4，消除对称重复）。
5. 概率 / Flags：从规则继承；Noita rate(0-100) 在此映射到 byte(0-255)。

datamined 参考（架构 §7.4，由 plan/13 Demo 填具体内容）：`[lava]+water→rock_static+steam @80`；`[acid]+[corrodible]→acid_gas @50`；`[fire]+[burnable_fast]→[fire]+fire @40`；`[fire]+water→[fire]+steam @20`；`[steam]+[static]→water+[static] @3`（冷凝）；`[molten_metal]+[cold]→metal_sand+[cold] @50`。

### 3.5 cache-aware 查表结构（架构 §7.4 修正，反对 `int[N*N]`）

**明确反对 `int[N*N]` 大表**（N→1000 时 ~4MB、对 2M cell/帧随机读 cache-hostile、对称对存两份；架构 §7.4 修正 / R12）。采用**每材质紧凑切片 + packed 数组**：

- 全部具体 `Reaction` 打包进单个 `Reaction[] _packed`，**按 owner 材质（`InputA`）分组连续存放**；`MaterialDef.ReactionStart` / `ReactionCount` 指向本材质的连续切片 `[ReactionStart, ReactionStart+ReactionCount)`。
- **早退**：绝大多数惰性材质 `ReactionCount==0`，热路径对当前 cell 材质一次比较即早退、根本不触碰反应数据（最高频情形）。
- **切片内查找**按 `ReactionCount` 选择策略（加载期定，封装为 `ReactionLookupMode`）：`Linear`（少量，线性扫描命中 cache）/ `Binary`（较多，切片按 `InputB` 升序，二分）/ `DirectTable`（极多，如 `[fire]` 展开后；为该材质私有一张小 `ushort[256]`/稀疏 `ushort[]` 直查表 neighbor id→packed index，cache-resident）。阈值**按实测 cache-miss 率定，不按字节大小**（架构 §7.4 / R12）。
- **取向与去重的协调**：早退必须基于**当前 cell 材质**的 `ReactionCount`，否则 `min` 归一会令「惰性 cell（id 较小）紧邻反应性邻居」被错误早退漏掉反应。因此一条无序对去重后的逻辑反应，**物化进两个 owner 切片**（普通反应），并按取向修正 Input/Output（owner=a：`InputA=a,InputB=b,OutputA=c,OutputB=d`；owner=b：`InputA=b,InputB=a,OutputA=d,OutputB=c`）。定向反应只物化进 `InputA` 切片（单取向，§3.6）。去重发生在展开期（防作者重复与同取向重复物化），物化双份是 owner 切片早退正确性的代价（表很小，约数千条，符合 R12「按实测而非字节」）。

```csharp
public sealed class ReactionTable
{
    private Reaction[] _packed;                  // 按 owner 分组连续
    private ReactionLookupMode[] _modeByMat;     // 每材质查找策略
    private ushort[]?[] _directTables;           // DirectTable 模式材质的私有直查表（其余 null）

    /// <summary>在 owner=mat 的切片中查 neighbor 反应；命中返回 packed 索引，否则 -1。</summary>
    public int Find(ushort mat, ushort neighbor, in MaterialDef def);
    public ref readonly Reaction At(int packedIndex);
}
public enum ReactionLookupMode : byte { None, Linear, Binary, DirectTable }
```

### 3.6 反应执行逻辑（跨 chunk / 边界 / 双输出 / 定向正确性）

反应 pass 在 CA 帧相位 [4] 内、随 chunk bottom-up 扫描的每个 cell 移动后执行（调度框架由 plan/03 提供）。`ReactionEngine.TryReact` 被 plan/03 的 per-cell 更新调用，契约如下：

```csharp
public sealed class ReactionEngine
{
    /// <summary>对处于 parity-guarded 扫描中的 cell c 与其 von Neumann 邻居尝试反应。
    /// 写入恒在距离 1（远在 32px halo 内，无锁安全，§5.8）。</summary>
    public void TryReact(ref CellCursor c, in NeighborSet neighbors, ref ChunkWorkContext ctx);
}
```

每对 `(c, n)` 的裁决步骤：

1. **早退**：`def[c.Material].ReactionCount==0` → 返回（§3.5）。
2. **parity 防重**：若 c 或 n 的 parity 位已等于本帧值 → 跳过该对（架构 §5.3）。每 cell 每帧至多一次反应（限成本）。
3. **查表**：`ReactionTable.Find(c.Material, n.Material, def)`；未命中下一邻居。`Fast` 标记反应先于普通反应裁决。
4. **概率**：取 per-chunk RNG 一个 byte，`< Probability` 则触发（确定性 seam 用 counter-based RNG，§6.2）。
5. **应用**：写 `OutputA → c`、`OutputB → n`；给 c、n **两个输入与产物都打本帧 parity**（架构 §5.3，跨 chunk 防二次触发的关键）；按产物 `DefaultLifetime` 置 `Lifetime`；标记 c、n 所在 chunk dirty（grow working rect）；若 n 跨 chunk 边界则对其 chunk 触发 **KeepAlive**（架构 §5.5，目标 chunk 因 border ring 必驻留，§3.4）。
6. **副作用**：`EmitHeat` → `TemperatureField.AddHeat`（§3.9）；`SpawnParticle` → 入粒子抛射请求（plan/05 相位 7 消费）；`GeneratesSmoke` → 产烟材质替代输出。

**跨 chunk / 边界 / 双输出 / 定向正确性**（架构 §5.8 / §7.4，不变式 #4）：

- 反应只作用 von Neumann 邻居（距离 1），写入恒在 32px halo 内，checkerboard 下同遍线程写区永不重叠 → **无锁安全**（§5.8）。
- 邻居 cell 跨在相邻 parity 的 chunk（不同 pass 处理，遍间 barrier 保证 parity 写可见）。当 c 在 pass X 与 n 反应并双双打 parity 后，pass Y 扫到 n 回看 c 时，因 c 的 parity 已等于本帧值而**跳过**——保证「谁先扫到谁执行、另一侧 parity 跳过」，**双输出不在两侧各执行一次、物质不凭空翻倍**（架构 §7.4，不变式 #4）。
- **定向反应**（`Directional`）只物化进 `InputA` 切片，仅当 `InputA` 材质的 cell 扫描遇到 `InputB` 邻居时触发（取向敏感），产物 `OutputA→该 cell`、`OutputB→邻居`；不双向镜像。若需具体空间方向（上/下/左/右），用 `Flags` bit4-7 编码 von Neumann 方向码（默认不编码、按扫描取向裁决）。
- **边界质量守恒**为承重测试项：双输出 / 定向反应在 chunk 边界既不翻倍也不丢失，要求专项边界守恒测试（详见 `plan/14-testing-benchmarking.md`，架构 §16.2 / R2）。

### 3.7 接触式火传播（便宜路径，架构 §7.5）

火传播做成**普通概率反应**，使其在**不开温度场时也工作**（架构 §7.5「默认走 Noita 式廉价接触点燃，全扩散热场作可选增强」）。机制：

- **接触点燃**：`[fire] + [burnable*] → [fire] + fire` 类反应（§3.4 展开），概率由对方 `Flammability` 与反应 `Probability` 综合；命中时把可燃 cell 转为 fire 材质并置 `Flags` burning 位（架构 §7.1 bit2）。
- **自燃**：温度场启用时，若 cell 温度 ≥ `AutoIgnitionTemp` 则点燃（在温度相变 / 火 pass 中检查，§3.9）；不开热场时自燃不触发，接触点燃仍生效。
- **燃烧位语义**：burning 是持久 Flags 位（入盘，架构 §11.3），表示该 cell 正在燃烧、每 tick 向温度场注入 `TemperatureOfFire`、按 `GeneratesSmoke` 产烟、按 `SpawnParticle` 喷火花粒子。
- **燃尽**：`FireHp` 为燃烧耐久（-1 永燃）；其倒计时复用 cell `Lifetime` 通用生命周期循环（实现在 `plan/05-particles-lifecycle.md`），归零时按 burnout 反应转为 ash / empty。本文档定义点燃 / 燃烧位 / 注热语义与字段，倒计时循环交 plan/05。

### 3.8 custom-update 委托钩子（架构 §7.4）

对反应表 / 阈值相变**表达不了**的少数行为（clone、生长、重力井）保留 per-material 可选委托（架构 §7.4「Noita 表 + TPT 每元素 Update 的混合最优解」）：

```csharp
/// <summary>材质自定义更新钩子。在 parity-guarded 扫描中、反应 pass 后调用；
/// 必须遵守 parity / 32px-halo / dirty+KeepAlive 约束（同 §3.6）。</summary>
public delegate void MaterialCustomUpdate(ref CellCursor cell, ref ChunkWorkContext ctx);
```

注册与门控：`MaterialTable.RegisterCustomUpdate(string name, MaterialCustomUpdate fn)` 写入 `MaterialCustomUpdate?[] _customUpdates`（以 id 索引，null = 无），并置该材质 `PropertyFlags |= HasCustomUpdate`。CA per-cell 更新仅当 `HasCustomUpdate` 位置位时调用委托（快速门控，避免每 cell null 检查 / 虚调用）。委托由引擎代码或脚本注册（脚本绑定在 `plan/11-scripting-system.md`，本文档定义委托类型 + 注册表 + 门控位）。委托内写入同样恒在 halo 内、打 parity、标 dirty / KeepAlive。

### 3.9 温度场（架构 §7.5，TempFieldDownscale=4）

温度场是**最贵的可选子系统**，按 TPT 模型粗化 + 降频实现（架构 §7.5 / §4.3）：

- **1/4 分辨率**：`EngineConstants.TempFieldDownscale=4`，一个温度 cell 覆盖 4×4 sim cell；每 64×64 chunk 持有 16×16 的温度子块（架构 §11.3「该 chunk 区域的粗 Temperature 子块」）。类型 `Half`（默认省内存，~0.5MB@1080p）或 `float`（可配，精度优先）。
- **概率热传导**：`HeatConduct` 作每帧传导概率（TPT 约定），`HeatCapacity` 作温变加权（必须非零，校验项）。5-point von Neumann stencil。
- **SIMD stencil**：传导 pass 是规则、分支统一的网格运算，用 `System.Runtime.Intrinsics`（`Avx2`/`Avx512F`/`Avx10v2`）+ `Vector<T>` + **scalar fallback**（架构 §12.5，`gate on Vector512.IsHardwareAccelerated`）。读源、写目标用 **ping-pong 双缓冲**（`_temp` / `_tempScratch`，帧末 swap）——温度是**独立于 cell 网格的粗场**，对其双缓冲**不违反不变式 #3**（#3 仅禁双缓冲 cell 网格）。
- **跨 chunk 传导**：stencil 读邻居 chunk 温度 halo（只读）；温度 pass 处于独立帧相位 [5]（与 CA pass [4] 分离），不与 checkerboard 竞争，可按自身方案并行（架构 §3.3 相位 [5]）。
- **阈值相变（不进反应表，架构 §7.4）**：传导后 `ApplyPhaseTransitions` 扫活跃 / 温度-dirty 区域 sim cell，读其降采样坐标温度，对该 cell 材质比较 `MeltPoint→MeltTarget` / `FreezePoint→FreezeTarget` / `BoilPoint→BoilTarget`，越阈值即转目标材质（写 Material SoA、打 parity、标 dirty / KeepAlive）。每 cell 每 pass 至多一次相变。ICE→WATR→STEAM 链由此自动得到（架构 §7.5）。
- **热源**：burning cell（§3.7）注入 `TemperatureOfFire`；`EmitHeat` 反应（§3.6）注入热量；接口 `TemperatureField.AddHeat(int worldX, int worldY, float deltaC)` 映射到降采样温度 cell，按 `HeatCapacity` 加权。
- **降频与降级**：温度场每帧或**每 N 帧**跑一遍（全分辨率每帧会主导预算，架构 §7.5 务必粗化 / 降频）；作为 **§4.3 第一级过载降级目标**——重载时先降 N（降频）、再退化为「仅接触式火传播（§3.7），关全扩散热场」。降级策略向 `PixelEngine.Core` 诊断 / 降级控制器注册（`00 §7`）。

```csharp
public sealed class TemperatureField
{
    // 每 chunk 16×16 子块，Half/float 可配；ping-pong 双缓冲（不违反不变式 #3，仅粗场）
    public void ConductStep(/* active chunks */);          // SIMD 5-point stencil
    public void ApplyPhaseTransitions(/* active cells */, MaterialTable mats); // 阈值相变写 Material
    public void AddHeat(int worldX, int worldY, float deltaC);
    public int  Downscale => EngineConstants.TempFieldDownscale; // = 4
}
```

### 3.10 数据驱动文件格式（materials.json / reactions.json，schema 在此 / 加载在 Content）

格式与字段语义在此定义；反序列化、`[tag]` 展开执行、name→id 分配、源生成 context 在 `PixelEngine.Content`；实际内容由 `plan/13-demo-game.md` 填入 `content/`。用 `System.Text.Json` + 源生成器（`00 §4`）。

**`materials.json`**（材质数组，targets 与 tags 用稳定 name / tag 名，加载期解析为 id / 位）：

```jsonc
[
  {
    "name": "water",                 // 稳定键（必填，唯一）
    "type": "Liquid",                // CellType
    "density": 100, "dispersion": 5,
    "liquidStatic": false, "liquidSand": false,
    "flammability": 0, "autoIgnitionTemp": 0, "fireHp": 0,
    "temperatureOfFire": 0, "generatesSmoke": 0,
    "meltPoint": null, "meltTarget": null,
    "freezePoint": 0.0, "freezeTarget": "ice",      // 目标按 name
    "boilPoint": 100.0, "boilTarget": "steam",
    "heatConduct": 30, "heatCapacity": 4.18,        // 非零
    "defaultLifetime": 0, "durability": 0,           // durability→Durability 抗性系数
    "integrity": 0,                                  // 0 = 即时破坏（液体一碰即抛）
    "destroyedTarget": null, "debrisCount": 0, "mineYield": 0,
    "textureId": -1, "baseColor": "#FF3366CC",      // BGRA8（或 #AARRGGBB，加载期归一）
    "colorNoise": 8,
    "renderStyle": "Liquid", "legendCategory": "Liquid",
    "opacity": 210, "edgeColor": "#00000000", "highlightColor": "#5533AAFF",
    "displayName": "水", "legendVisible": true,
    "tags": ["corrodible"],                          // → PropertyFlags 位
    "emissive": false,
    "audioCues": { "impact": "drip", "splash": "splash_water", "ambient": "deep_water" }
  },
  {
    "name": "stone", "type": "Solid", "density": 200,
    "durability": 180, "integrity": 600,             // 高抗性 + 大生命值：需炸弹级累计当量才碎
    "destroyedTarget": "gravel", "debrisCount": 4,   // 破坏产 gravel + 4 碎屑（by name → id，守 #8）
    "heatConduct": 90, "heatCapacity": 2.5,
    "renderStyle": "Ground", "legendCategory": "Terrain",
    "edgeColor": "#FF202024", "opacity": 255,
    "baseColor": "#FF6E6E7A", "colorNoise": 8,
    "displayName": "岩石", "legendVisible": true,
    "tags": ["static", "corrodible"]
  },
  {
    "name": "gravel", "type": "Powder", "density": 190,
    "durability": 40, "integrity": 0,                // stone 碎块：低抗、即时破坏、可再抛落
    "destroyedTarget": null, "debrisCount": 0,
    "heatConduct": 80, "heatCapacity": 2.2,
    "renderStyle": "Powder", "legendCategory": "Terrain",
    "baseColor": "#FF585862", "colorNoise": 12,
    "displayName": "碎石", "legendVisible": true,
    "tags": ["static"]
  },
  {
    "name": "boundary_stone", "type": "Solid", "density": 255,
    "durability": 255, "integrity": 0,               // integrity 无意义：Indestructible 位使 ApplyDamage no-op
    "destroyedTarget": null, "debrisCount": 0,
    "heatConduct": 0, "heatCapacity": 10.0,
    "renderStyle": "Solid", "legendCategory": "Special",
    "edgeColor": "#FF101014", "opacity": 255,
    "baseColor": "#FF303038", "colorNoise": 0,
    "displayName": "边界岩", "legendVisible": false,
    "tags": ["static", "indestructible"]             // indestructible → PropertyFlags bit11
  }
]
```

**新增字段语义（§3.2）**：`durability`（抗性系数）、`integrity`（满结构完整度，0=即时破坏）、`destroyedTarget`（破坏产物，**稳定 name**，加载期经 `MaterialTable` 解析为 id，缺失落 fallback，守 #8）、`debrisCount`、`mineYield`、`renderStyle`、`legendCategory`、`edgeColor`（BGRA8/`#AARRGGBB`）、`opacity`、`highlightColor`、`displayName`、`legendVisible`。tag 名 `indestructible` / `diggable` 加载期置对应 `PropertyFlags` 位（bit11/bit12）。任何指向材质的 name 引用（`destroyedTarget`、以及 Demo 侧 `buildMaterial` 等建材引用）一律入盘写 name、加载期 name→id 解析（守 #8），运行时热路径只用 id。所有视觉字段（`renderStyle`/`edgeColor`/`opacity`/`highlightColor`/`legendCategory`）只落进 `MaterialDef` 并派生 `MaterialVisualTable`，渲染相位 CPU 算 BGRA，**绝不写回 cell（守 #7）**。加载期校验增补：`dispersion` clamp 到 `[0, EngineConstants.MoveCap]` 并断言（守 #4）、`destroyedTarget`/tag 可解析、`Indestructible` 材质允许 `integrity==0`（免疫不依赖生命值）。DTO（`MaterialJson` 增上列字段）与反序列化 / 校验 / 热重载实现归属 Content；boundary_stone / gravel / crystal 等具体内容由 plan/13 填 `content/materials.json`。

**`reactions.json`**（反应规则数组，input/output 可为具体 name 或 `[tag]`）：

```jsonc
[
  {
    "inputA": "[lava]", "inputB": "water",
    "outputA": "rock_static", "outputB": "steam",
    "probability": 80,                 // 0-100，加载期映射 0-255
    "flags": ["emitHeat"]              // fast/directional/spawnParticle/emitHeat
  },
  { "inputA": "[fire]", "inputB": "[burnable_fast]",
    "outputA": "[fire]", "outputB": "fire", "probability": 40, "flags": [] }
]
```

**tag 声明**（可在 materials.json 顶层或独立 `tags` 段声明代表材质，供输出 tag 解析，§3.4 规则 3）。Schema 校验（必填字段、name 唯一、target / tag 可解析、`HeatCapacity!=0`）在加载期执行（Content）。DTO（`MaterialJson` / `ReactionJson` / `JsonSerializerContext`）定义在 Content，本文档定义其字段契约。

### 3.11 破坏 / 结构完整度消费契约（per-cell 累计伤害 + ApplyDamage，plan/03 执行）

可玩化要求「持久破坏」：地形被武器 / 爆炸 / 激光 / 酸蚀按材质抗性差异化损毁并可入盘。破坏的**执行**（cell SoA lane、`ApplyDamage`、破坏动作）由 `plan/03-simulation-kernel.md` 落地（属 CA 内核安全相位的离散编辑）；本文档定义其消费的**材质侧契约**与破坏语义，并登记 cell lane 的存在与守则。

**per-cell 累计伤害 lane（归属 plan/03）**：cell SoA 新增一条 **`Damage`（byte）平面**，记录该 cell 已累计承受的伤害（非满血值）。写入 / 相变 / 反应生成新 cell 时该 lane 归 0（自然默认，无额外 init）。**单缓冲原地读改写（守 #1，不双缓冲）**；`Damage` 非颜色，不违反「颜色不入 cell（守 #7）」。内存：64×64 chunk × 1B = 4KB/chunk，对应 cell 预算 4B→5B/cell（+25%）、每常驻 chunk 16KB→20KB（此预算数字须与 plan/03/07/16 三处一致，本处仅登记契约）。

**`ApplyDamage(x, y, dmg, kind)` 规则（plan/03 实现，消费本文档材质字段）**，逐步裁决：

1. **刚体像素守则（守 #5）**：写 cell 前必查 `CellFlags.RigidOwned`；命中刚体所属像素则**绝不在其上累加 `Damage`**，而经 `IRigidDamageSink.OnOwnedCellDamaged(x, y, dmg)` 路由给刚体系统触发受损像素剥离 / 形状重建（plan/06），CA 网格侧不改该 cell 材质。
2. **免疫**：材质 `PropertyFlags` 含 `Indestructible`（bit11）→ `ApplyDamage` no-op（boundary_stone / bedrock）。
3. **抗性吸收**：`effective = max(0, dmg - Durability * EngineConstants.DamageAbsorb)`（`DamageAbsorb` 为集中常量，不写死魔法数）。
4. **即时破坏 vs 累计**：材质 `Integrity == 0` → 任意 `effective > 0` 立即破坏（沙 / 水 / gravel）；否则 `Damage += (byte)min(255, effective)`，当 `Damage * EngineConstants.DamageScale ≥ Integrity` 破坏（`DamageScale` 集中常量，把 byte lane 映射到 `ushort Integrity` 域）。
5. **破坏动作**：cell 材质转 `DestroyedTarget`（或 `Empty`）、清 `Damage` lane、打本帧 parity、标所在 chunk dirty、跨界则对邻 chunk `KeepAlive`（守 #2/#3/#6）；按 `DebrisCount` 入粒子抛射请求（handshake plan/05 相位 7，粒子材质 = `DestroyedTarget` 或本材质）；若材质置 `Diggable` 且 `MineYield > 0` 则发一次采集事件（plan/13 GameDirector 订阅计数）。

**破坏路径差异化抗性（`DamageKind` + `IWorldEffects`，API 缺口归 plan/05+plan/11 补）**：抗性差异全由 `materials.json` 数据表达，改数值即改表现。

- **爆炸**：`IWorldEffects.DamageCircle(x, y, radius, damage, falloff, DamageKind.Explosive)`——半径内每 cell 按距离衰减调 `ApplyDamage`。sand/dirt（低 `Durability`、`Integrity`=0/小）一炸即碎抛为粒子；stone（高 `Durability`、大 `Integrity`）需炸弹级当量累计；metal 近乎免疫小爆破。取代原 `Explode`「无条件抛射半径内全部 cell」——旧 `EjectionRequest` 抛射改为破坏动作触发（`DestroyedTarget` 化 + 碎屑），使抗性生效；`Explode` 内部改为 `DamageCircle` 组合。
- **激光**：`IWorldEffects.DamageBeam(x, y, dx, dy, length, damagePerCell, heatPerCell, DamageKind.Beam)`——沿束逐 cell `ApplyDamage` + `AddHeat`（§3.9）；穿透直到累计能量耗尽或遇超高 `Durability`。木 / 冰快速烧穿，metal 慢烧（配合熔点相变成 molten_metal）。
- **酸蚀（既有反应）**：酸反应命中改为对 corrodible cell 调 `ApplyDamage`（按反应 `Probability` 与 `Durability` 折算 `DamageKind.Corrosion`），使 stone 比 metal 蚀得快，取代原「概率直接转 acid_gas」的一刀切。

**不变式合规**：破坏是安全相位的**离散编辑**（写操作只在单线程输入 / 安全相位 + 标 dirty + KeepAlive，守 #2/#3/#6），**不受 32px halo 约束**（halo 与 #4 约束 CA movement，不约束离散破坏半径，切勿误引）；单缓冲原地（守 #1）；`Damage` 非颜色、视觉字段只存 MaterialDef（守 #7）。

**存档（契约登记，落地在 plan/07）**：`Damage` lane 随 `ChunkSnapshot` / `ChunkCodec`（RLE 段）入盘；bump `SaveFormatVersion` 并提供旧档→新档迁移（旧档 `Damage=0`）；随 material remap，缺失材质落 fallback 后其 cell `Damage` 清 0（避免跨材质语义漂移）。

---

## 4. 实现清单

> 勾选规则：完成并自测通过才勾。命名 / 字段 / 方法 / 数据结构如下，标注架构 §。本清单只含本范围（Simulation 侧数据模型 + 执行 + 格式契约）；JSON 反序列化 / IO 实现属 Content 清单，不在此勾。

### 4.1 材质数据模型（架构 §7.3 / §7.1 / §7.2）

- [x] `MaterialDef` `readonly record struct`，含 §3.2 全部字段（Id/Name/Type/Density/Dispersion/LiquidStatic/LiquidSand/Flammability/AutoIgnitionTemp/FireHp/TemperatureOfFire/GeneratesSmoke/Melt+Freeze+Boil(Point+Target)/HeatConduct/HeatCapacity/DefaultLifetime/Durability/TextureId/BaseColorBGRA/ColorNoise/PropertyFlags/ReactionStart/ReactionCount/AudioCues）（架构 §7.3）。
- [x] `MaterialProperty : uint` `[Flags]`，含 8 个 tag 成员位 + `Emissive`/`HasCustomUpdate`/`Conductive` + 预留位（§3.2）。
- [x] `MaterialTag : byte` 枚举与 `MaterialProperty` 位的固定映射（§3.4）。
- [x] `AudioCueSet` `readonly record struct`（Impact/Fire/Splash/Explosion/Shatter/Ambient cue，架构 §10.2）。
- [x] `MaterialDef.Type` 引用 plan/03 的 `CellType`（不重复定义，架构 §7.2）。
- [x] `MaterialHotTable` SoA 热表：从 `MaterialDef[]` 派生热路径字段并列数组（Density/Dispersion/Type/HeatConduct/HeatCapacity/相变阈值/ReactionStart/ReactionCount/PropertyFlags），冷字段不入热表（架构 §7.1 / §12.1）。
- [x] `MaterialDef` 增可玩性 / 破坏字段：`Integrity`(ushort)/`DestroyedTarget`(ushort，写稳定 name 加载 name→id 守 #8)/`DebrisCount`(byte)/`MineYield`(byte)；`Durability` 语义收紧为「被 sim 真实消费的抗性系数」（durability 字段直迁移，§3.2 / §3.11）。
- [x] `MaterialDef` 增视觉可辨识字段：`RenderStyle`/`LegendCategory`/`EdgeColorBGRA`(uint)/`Opacity`(byte)/`HighlightColorBGRA`(uint)/`DisplayName`(string 冷)/`LegendVisible`(bool)；新增 `MaterialRenderStyle`/`MaterialLegendCategory` 枚举（视觉只存 MaterialDef，渲染 CPU 算 BGRA 不写回 cell，守 #7，§3.2）。
- [x] `MaterialProperty` 增 `Indestructible`(bit11) / `Diggable`(bit12) 破坏行为位（§3.2 / §3.11）。
- [x] `Dispersion` 加载期 clamp 到 `[0, EngineConstants.MoveCap]` 并加**构建期断言**（守 #4）；`FlowRate` 作 `Dispersion` 语义别名对外暴露（不新增字段，§3.2）。
- [x] `MaterialHotTable` 增破坏热列 `_durability`/`_integrity`/`_destroyedTarget`（+ `_propertyFlags` 携 Indestructible/Diggable），供 plan/03 `ApplyDamage` 按 id 只读查表；另派生 `MaterialVisualTable`（RenderStyle/EdgeColorBGRA/Opacity/HighlightColorBGRA/LegendCategory）供 plan/08 渲染相位（§3.2）。

### 4.2 name↔id 映射与注册表（不变式 #8，架构 §11.2 / §17.4）

- [x] `MaterialTable`：`MaterialDef[] _defs` + `MaterialHotTable _hot` + `Dictionary<string,ushort> _nameToId`（后者非热路径）。
- [x] `ref readonly MaterialDef Get(ushort)` / `TryGetId` / `GetIdOrFallback` / `GetName` / `Count`。
- [x] `BuildIdNameTable()` 导出 id→name（供 plan/07 落盘，架构 §11.2）。
- [x] `BuildRemapLut(savedTable, fallbackId)` 构建 savedId→currentId LUT，缺失材质映射 fallback（架构 §11.2 / §11.4，供 plan/07）。
- [x] `ReloadStable(newDefs)` 增量稳定重载：保留既有 id、追加新 id、删除作 tombstone、绝不重排，返回 tombstone id 列表 + fallback 替换计数诊断（架构 §17.4，供 plan/12）。
- [x] `HeatCapacity != 0` 校验（构建期，必须非零，架构 §7.3）。

### 4.3 反应表数据模型与 cache-aware 查表（架构 §7.4 / R12）

- [x] `Reaction` `readonly record struct`（InputA/InputB/OutputA/OutputB/Probability/Flags，架构 §7.4）。
- [x] `ReactionFlags : byte` `[Flags]`（Fast/Directional/SpawnParticle/EmitHeat + bit4-7 预留方向码，架构 §7.4）。
- [x] `ReactionTable`：packed `Reaction[] _packed`（按 owner 分组连续）、`ReactionLookupMode[] _modeByMat`、DirectTable 材质私有 `ushort[]?[] _directTables`（§3.5）。
- [x] `ReactionLookupMode : byte`（None/Linear/Binary/DirectTable）；加载期按 `ReactionCount` 选模式（阈值按实测 cache-miss 定，架构 R12）。
- [x] `Find(mat, neighbor, in def)`：`ReactionCount==0` 一次比较早退；Linear 线性 / Binary 二分（切片按 InputB 升序）/ DirectTable 直查（§3.5）。
- [x] `At(packedIndex)` 返回 `ref readonly Reaction`。
- [x] **明确反对** `int[N*N]` 大表（架构 §7.4 修正，代码注释引用 §7.4 / R12）。

### 4.4 `[tag]` 展开规则契约（架构 §7.4，展开执行在 Content）

- [x] 定义 `MaterialTag`→`MaterialProperty` 位映射，供按位筛选 tag 成员材质（§3.4 规则 1）。
- [x] 定义笛卡尔积展开规则：输入 tag 集合 × 集合 → 具体材质对（§3.4 规则 2）。
- [x] 定义输出 tag 解析（tag `representative` 代表材质）（§3.4 规则 3）。
- [x] 定义有序对去重 `min(matA,matB)` 归一 + 双 owner 切片物化（普通反应）/ 单 owner（定向）（§3.4 规则 4 / §3.5）。
- [x] 定义概率映射 rate(0-100)→byte(0-255)（§3.4 规则 5）。

### 4.5 反应执行（不变式 #3 / #4，架构 §5.3 / §5.5 / §5.8 / §7.4）

- [x] `ReactionEngine.TryReact(ref CellCursor, in NeighborSet, ref ChunkWorkContext)`，在 plan/03 CA 相位 [4] per-cell 更新中调用（§3.6）。实现接入现有 `IReactionExecutor` / `NeighborWindow` seam。
- [x] 早退（`ReactionCount==0`）→ parity 防重（c/n 任一已本帧 parity 则跳过，架构 §5.3）→ 查表 → 概率（per-chunk RNG byte）→ 应用。
- [x] 应用：写 OutputA→c / OutputB→n；c/n 输入与产物**全打本帧 parity**（架构 §5.3）；置产物 `DefaultLifetime`；标 dirty；n 跨界则 KeepAlive 其 chunk（架构 §5.5）。
- [x] 写入恒在 von Neumann 距离 1（32px halo 内，无锁安全，架构 §5.8，注释引用）。
- [x] 双输出 / 定向「谁先扫到谁执行、另一侧 parity 跳过」防翻倍 / 防丢失（不变式 #4，架构 §7.4）。
- [x] 定向反应单 owner 切片 + 取向语义（产物 OutputA→cell / OutputB→邻居）（§3.6）。
- [x] `Fast` 反应先于普通反应裁决；每 cell 每帧至多一次反应（架构 §7.4）。
- [x] 副作用：`EmitHeat`→`AddHeat`；`SpawnParticle`→粒子抛射请求（plan/05）；`GeneratesSmoke`→产烟输出（§3.6）。

### 4.6 接触式火传播（架构 §7.5）

- [x] 火传播实现为普通概率反应（`[fire]+[burnable*]→[fire]+fire`），不开热场亦工作（架构 §7.5）。
- [x] 点燃置 burning Flags 位（持久位，架构 §7.1 bit2 / §11.3）；自燃 `temp≥AutoIgnitionTemp`（热场启用时，§3.9）。
- [x] burning cell 每 tick 注 `TemperatureOfFire`、按 `GeneratesSmoke` 产烟、按需喷火花粒子；burnout 走 `FireHp`/`Lifetime`（倒计时循环在 plan/05）转 ash/empty（§3.7）。

### 4.7 custom-update 委托钩子（架构 §7.4）

- [x] `MaterialCustomUpdate` 委托类型（`ref CellCursor, ref ChunkWorkContext`）（§3.8）。实现接入现有 `NeighborWindow` seam，委托显式接收 window 与 context。
- [x] `MaterialTable.RegisterCustomUpdate(name, fn)` 写 `MaterialCustomUpdate?[] _customUpdates` 并置 `HasCustomUpdate` 位。
- [x] CA per-cell 仅当 `HasCustomUpdate` 位置位才调用委托（门控，免 null/虚调用开销）；委托内遵守 parity/halo/dirty/KeepAlive。

### 4.8 温度场（架构 §7.5 / §12.5 / §4.3）

- [x] `TemperatureField`：每 chunk 16×16（=64/`TempFieldDownscale`）`Half`/`float` 子块 + ping-pong `_temp`/`_tempScratch`（不违反不变式 #3，§3.9）。
- [x] `ConductStep`：5-point stencil，`HeatConduct` 概率传导 + `HeatCapacity` 加权；SIMD（Avx2/Avx512F/Avx10v2 + `Vector<T>`）+ scalar fallback，`gate on Vector512.IsHardwareAccelerated`（架构 §12.5）。
- [x] 跨 chunk 温度 halo 只读传导；温度 pass 处帧相位 [5]（与 CA [4] 分离，架构 §3.3）。
- [x] `ApplyPhaseTransitions`：阈值相变 melt/freeze/boil（`MaterialDef` 阈值+目标，**不进反应表**，架构 §7.4），写 Material、打 parity、标 dirty/KeepAlive，每 cell 每 pass 至多一次。
- [x] `AddHeat(worldX, worldY, deltaC)` 映射降采样 cell，并提供按 `HeatCapacity` 加权的热源入口（热源：burning / EmitHeat）。
- [x] 每 N 帧降频；作为 §4.3 第一级降级 seam（降 N → 退化为仅接触式火传播），暴露给后续 Hosting 降级编排（架构 §4.3 / §7.5 / `00 §7`）。

### 4.9 数据格式契约（schema 在此，加载在 Content）

- [x] `materials.json` schema：§3.10 全字段（target/tag 用 name，`baseColor` BGRA8 归一，`HeatCapacity!=0`）。
- [x] `reactions.json` schema：input/output 支持具体 name 与 `[tag]`，`probability` 0-100，`flags` 字符串数组（§3.10）。
- [x] tag 声明 + 代表材质 representative 字段契约（§3.4 规则 3）。
- [x] 标注 DTO（`MaterialJson`/`ReactionJson`/`JsonSerializerContext`）与反序列化 / 展开 / name→id 分配实现归属 Content；内容由 plan/13 填 `content/`。
- [x] `materials.json` schema 增字段：`integrity`/`destroyedTarget`(name)/`debrisCount`/`mineYield`/`renderStyle`/`legendCategory`/`edgeColor`/`opacity`/`highlightColor`/`displayName`/`legendVisible`；`durability` 语义化；tag 名 `indestructible`/`diggable` → bit11/bit12（§3.10）。
- [x] `MaterialJson` DTO 增上列字段（实现归属 Content）；加载期 `destroyedTarget`/建材 name 引用经 `MaterialTable` name→id 解析（守 #8），`dispersion` clamp+断言（守 #4），`Indestructible` 材质允许 `integrity==0`（§3.10 / §3.11）。
- [x] 新增材质 `boundary_stone`（Indestructible 边界）/ `gravel`（stone 碎块 Powder，即时破坏）schema 示例齐备；具体内容与 crystal 由 plan/13 填 `content/`（§3.10）。

### 4.10 破坏 / 结构完整度契约（材质侧定义，执行在 plan/03）

- [x] 登记 cell SoA 新增 `Damage`(byte) lane 契约（归属 plan/03）：默认 0、单缓冲原地（守 #1）、非颜色（守 #7）；预算 4B→5B/cell、常驻 chunk 16KB→20KB（与 plan/03/07/16 一致，§3.11）。
- [x] 定义 `ApplyDamage(x,y,dmg,kind)` 消费规则：`RigidOwned` 命中经 `IRigidDamageSink.OnOwnedCellDamaged` 路由（守 #5，不累加 Damage）→ `Indestructible` no-op → `effective=max(0,dmg-Durability*DamageAbsorb)` → `Integrity==0` 即时破坏 / 否则累计至 `Damage*DamageScale≥Integrity`（`DamageAbsorb`/`DamageScale` 集中常量，§3.11）。
- [x] 定义破坏动作：转 `DestroyedTarget`/`Empty` + 清 Damage + parity + dirty + 跨界 KeepAlive（守 #2/#3/#6）+ `DebrisCount` 碎屑请求（plan/05）+ `Diggable`&`MineYield` 采集事件（plan/13，§3.11）。证据：`SimulationKernel.DestroyCell/NotifyCellDestroyed/MarkDamageDirty` 清 Damage、写 rubble/Empty、写 parity/lifetime、标 dirty/KeepAlive 并发布 `CellDestructionEvent(DebrisCount,MineYield)`；`CellDamageRubbleHandshakeTests` 与 `WorldEffectBoundaryConservationTests` 覆盖 rubble、碎屑、MineYield、跨 chunk KeepAlive 与零分配入口。
- [~] 定义 `DamageKind` 与三路径差异化抗性契约：爆炸 `DamageCircle`（`Explode` 内部改组合）、激光 `DamageBeam`(+AddHeat)、酸蚀反应改走 `ApplyDamage`；抗性差异全由 materials.json 表达（§3.11；API 缺口归 plan/05+plan/11）。进展：`IWorldEffects.DamageCircle/DamageBeam`、`World.Explode`→`DamageCircle`、Demo 武器 `DamageCircle`/`DamageBeam`+`AddHeat`、`DamageBeamBurnThroughTests` 与脚本 flush/零分配测试已覆盖爆炸/激光路径；缺口：`DamageKind.Corrosion` 目前仅为 API 枚举，`acid + [corrodible]` 仍走 reaction 输出式腐蚀，尚未统一到 `ApplyStructuralDamage`/Corrosion 抗性路径，不能勾选完成。
- [x] 登记 `Damage` lane 存档契约（落地 plan/07）：入 `ChunkSnapshot`/`ChunkCodec`(RLE)、bump `SaveFormatVersion`、旧档迁移 `Damage=0`、material remap 缺失 fallback 后 `Damage` 清 0（§3.11）。

---

## 5. 验收标准

- [x] `MaterialDef` 含架构 §7.3 全部字段，无遗漏；`MaterialHotTable` 热路径不触碰冷字段（字段审计 + `MaterialTableTests` 覆盖；反汇编随后续热路径基准确认）。
- [x] name↔id：改 `materials.json` 顺序 / 增删材质后，`BuildRemapLut` 使旧档逐 cell 正确重映射，缺失材质落 fallback（与 plan/07 存档往返测试联动通过，架构 §11.2 / §16.2，不变式 #8）。
- [x] 热重载 `ReloadStable` 后既有 id 不变、live 网格不损坏，删除材质活 cell 重映射 fallback 并输出诊断计数（架构 §17.4）。
- [x] `[tag]` 展开正确：tag 成员集合按位筛选无误、笛卡尔积齐全、有序对去重无对称重复、rate→byte 映射正确（反应表测试通过，架构 §16.2）。
- [x] cache-aware 查表：惰性材质（`ReactionCount==0`）一次比较早退、热路径不触反应数据；Linear/Binary/DirectTable 三模式结果一致；**无 `int[N*N]` 大表**（代码审查 + `ReactionLookupBenchmarks` 覆盖 inert / linear / binary / direct table，架构 §7.4 / R12）。
- [x] 反应质量守恒：双输出 / 定向反应在 chunk 边界**不翻倍、不丢失**（边界守恒性质测试通过，引用 `plan/14`，架构 §16.2 / 不变式 #4 / R2）。
- [x] parity 防重：同对反应一帧至多一次，跨 pass barrier 后另一侧因 parity 跳过（单线程 oracle 比对统计性质，架构 §5.3 / §16.2）。
- [x] 反应写入恒在 32px halo 内、跨界触发 KeepAlive 唤醒驻留邻居 chunk（雪崩跨界正确传播，架构 §5.5 / §5.8）。
- [x] 接触式火传播在**关闭温度场**时正常工作（接触点燃 / 燃烧 / burnout 链），不依赖热场（架构 §7.5）。
- [x] 温度场：ICE→WATR→STEAM 相变链正确（阈值 + 目标，不经反应表）；SIMD stencil 有 scalar fallback 且结果一致；每 N 帧降频与 §4.3 一级降级生效、退化为接触火传播后仍跑（架构 §7.5 / §4.3）。
- [x] custom-update：`HasCustomUpdate` 门控仅对声明材质调用委托，委托内写入遵守 parity/halo/dirty/KeepAlive（架构 §7.4）。
- [x] 稳态帧零托管堆分配（反应 / 温度 pass 无 LINQ / 闭包 / 装箱 / 字符串；`ReactionTemperatureAllocationBenchmarks` 经 BenchmarkDotNet MemoryDiagnoser 确认 `Allocated=-`，`AGENTS.md §3`）。
- [x] 与不变式 #3/#4/#8/#9 及技术栈 `00` 无冲突（自审通过：反应/温度仍为单缓冲 parity、32px halo/KeepAlive、name 稳定键与 CPU sim 权威）。
- [x] 差异化破坏：同一 `DamageCircle(damage=X)` 下 sand/dirt 立即碎抛、stone 需累计多次 / 大当量才破坏、metal 小爆破近免疫、`boundary_stone`(Indestructible) 完全不破坏——差异全来自 `materials.json` 抗性数值（改数值即改表现，无写死；`CellDamageResistanceTests.DamageCircleDifferentiatesMaterialResistanceFromData`、`SimulationDataStructureTests.ApplyStructuralDamageDestroysSolidAndRoutesRigidOwned` 与 `DemoStartupOptionsTests.DemoContentMaterialsDriveStructuralDamageResistance` 覆盖）。
- [~] cell 破坏归零转 `DestroyedTarget`（stone→gravel）并按 `DebrisCount` 抛碎屑；`Integrity==0` 材质即时破坏；`Diggable`&`MineYield` 材质（crystal）被武器破坏发一次采集事件；`Damage` lane 稳态帧零托管分配（守 #1，MemoryDiagnoser 确认）。进展：`CellDamageRubbleHandshakeTests` 覆盖 rubble/Empty、DebrisCount→真实粒子、Diggable MineYield→`CellDestructionEvent`；`CellDamageResistanceTests.MaxIntegrityZeroDestroysImmediatelyAfterHardness` 覆盖 `Integrity==0`；`WorldEffectBoundaryConservationTests.StructuralDamageEntriesDoNotAllocateAfterWarmup` 与 `ScriptSimulationContextTests.WorldDamageAndHeatCommandsDoNotAllocateAfterWarmup` 覆盖结构破坏入口与脚本命令稳态 0 分配；Demo `WeaponControllerExcavatorPublishesMineYieldEvent` 覆盖挖掘武器采集事件。缺口：普通 `DamageCircle`/爆炸破坏 crystal 后到 `MineYieldEvent`/MissionDirector 的玩法桥仍缺直接自动化证据，不能把整条验收项勾成完成。
- [x] 刚体像素受击不累加 `Damage`、经 `IRigidDamageSink.OnOwnedCellDamaged` 路由触发形状重建（守 #5，与 plan/06 联动测试）。证据：`SimulationKernel.ApplyStructuralDamage` 命中 `RigidOwned` 时只调 `_rigidDamageSink.OnOwnedCellDamaged(wx,wy)` 并清 Damage；`RigidOwnedDamageRoutingTests.DamageCircleRoutesRigidOwnedCellToPhysicsQueueWithoutAccumulatingDamage` 与 `DamageBeamRoutesRigidOwnedCellToPhysicsQueueWithoutMutatingMaterial` 通过。
- [x] `Dispersion` 加载期 clamp 生效、构建期断言拦截越 `MoveCap` 值；液体单步水平位移 ≤ 32px（守 #4，`MaterialDispersionClampTests`）。
- [x] 视觉字段只落 `MaterialDef`/`MaterialVisualTable`、渲染相位 CPU 算 BGRA，sim cell 无颜色写入（守 #7，字段审计 + 与 plan/08 着色联动）。
- [x] `Damage` lane 存档往返正确：新档写读一致、旧档迁移 `Damage=0`、material remap 缺失落 fallback 后 `Damage` 清 0（与 plan/07 往返测试联动，守 #8）。

---

## 6. 依赖关系

前置（必须先完成）：

- `plan/02-core-infrastructure.md`：`EngineConstants`（ChunkSize/MoveCap/TempFieldDownscale）、RNG（counter-based）、内存（POH/ArrayPool）、诊断 / 降级控制器、SIMD 能力探测（`00 §7`）。
- `plan/03-simulation-kernel.md`：`CellType`、`CellCursor` / `NeighborSet` / `ChunkWorkContext`、Material/Flags/Lifetime/Temperature SoA、parity 时钟位、dirty-rect、KeepAlive、checkerboard 相位 [4]/[5] 框架（本文档反应 / 温度 pass 挂入其中，架构 §5）。**并新增 `Damage`(byte) cell SoA lane 与 `ApplyDamage` 执行**（消费本文档 `Durability`/`Integrity`/`DestroyedTarget`/`Indestructible`/`MineYield` 契约，§3.11；预算 4B→5B/cell 与 plan/03/07/16 一致）。

并行 / 协作：

- `PixelEngine.Content`（内容管线计划）：依赖本文档的 Simulation 表类型，实现 JSON 反序列化、`[tag]` 展开、name→id 分配（`Content → Simulation`，§3.1）。
- `plan/12-editor-tooling-ui.md`：材质编辑器、反应表可视化、JSON 热重载触发（消费 `MaterialTable.ReloadStable`，架构 §17.4）。

下游消费：

- `plan/05-particles-lifecycle.md`：消费 `DefaultLifetime`/`FireHp`/burning 位（lifetime 倒计时循环）、`SpawnParticle` 抛射请求 / handshake；**破坏碎屑 handshake（`DebrisCount`）与武器破坏 API `DamageCircle`/`DamageBeam`（§3.11 `IWorldEffects` 缺口，plan/05+plan/11 补）**。
- `plan/06-rigidbody-integration.md`：消费 `IRigidDamageSink.OnOwnedCellDamaged`——`ApplyDamage` 命中 `RigidOwned` 像素时路由触发形状重建（守 #5，§3.11）。
- `plan/07-world-streaming-serialization.md`：消费 `BuildIdNameTable` / `BuildRemapLut`（存档 name↔id remap 落盘，不变式 #8）；**落地 `Damage` lane 入 `ChunkSnapshot`/`ChunkCodec`、bump `SaveFormatVersion`、旧档迁移与 remap 清 0（§3.11）**。
- `plan/08-rendering.md`：消费 `BaseColorBGRA` / `TextureId` / `ColorNoise` / `Emissive`（上色与 emissive，架构 §9）；**消费 `MaterialVisualTable`（`RenderStyle`/`EdgeColorBGRA`/`Opacity`/`HighlightColorBGRA`）做差异化着色 + `MaterialSwatchProvider` 图例采样，渲染相位 CPU 算 BGRA 不写回 cell（守 #7，§3.2）**。
- `plan/10-audio.md`：消费 `AudioCueSet`（材质化音效，架构 §10.2）。
- `plan/11-scripting-system.md`：消费 `MaterialTable.TryGetId`（按 name 查 id）、`RegisterCustomUpdate`（脚本注册 custom-update）；**门面公开 `IWorldEffects.DamageCircle/DamageBeam/AddHeat`、`MaterialInfo` 增 `DisplayName/Hardness(=Durability)/Integrity/MineYield/FlowRate/IsDestructible` 供武器与图例读（§3.11 / §C.4）**。
- `plan/13-demo-game.md`：填 `content/materials.json` / `reactions.json` 实际内容（含 boundary_stone/gravel/crystal 及全材质抗性 / 视觉字段）；消费采集事件（`MineYield`）驱动可玩循环，`MaterialLegendHud` 经 `MaterialSwatchProvider` 展示 `LegendVisible` 材质。
- `plan/14-testing-benchmarking.md`：边界反应守恒、tag 展开、name↔id remap、cache-miss、温度相变链测试与基准。

> 执行顺序（`plan/README.md`）：`03 → 05 → 04 → 07`。本文档在 CA 内核（03）与粒子（05）之后落地。

---

## 7. 提交节点

按 `AGENTS.md §6`，每个节点完成即用中文 git 提交（type 前缀英文，scope=`sim`/`content`）。建议节点：

1. `feat(sim): 材质数据模型 MaterialDef + MaterialProperty/MaterialTag + 热表与 name↔id 注册表`（§4.1 / §4.2，对应 plan/04 实现清单 4.1–4.2）。
2. `feat(sim): cache-aware 反应表 Reaction/ReactionTable + tag 展开规则契约`（§4.3 / §4.4，反对 int[N*N]，对应 4.3–4.4）。
3. `feat(sim): 反应执行 ReactionEngine（parity/halo/KeepAlive + 双输出/定向边界守恒）`（§4.5，对应 4.5）。
4. `feat(sim): 接触式火传播 + custom-update 委托钩子`（§4.6 / §4.7，对应 4.6–4.7）。
5. `feat(sim): 温度场 TemperatureField（SIMD 概率传导 + 阈值相变 + 降频降级）`（§4.8，对应 4.8）。
6. `feat(content): materials.json / reactions.json schema 与 tag 语法契约`（§4.9，对应 4.9；DTO/加载实现随 Content 计划提交）。
7. `feat(sim): 材质可玩性/视觉字段 + 破坏契约（Durability 语义化/Integrity/DestroyedTarget/RenderStyle/MaterialVisualTable + Indestructible/Diggable + Dispersion clamp + ApplyDamage/Damage lane 契约）`（§3.2 / §3.11 / §4.1 / §4.10，对应新增可玩性字段与破坏契约；`Damage` lane 与 `ApplyDamage` 执行随 plan/03 提交，schema 新字段随 Content 提交）。

> 每节点完成需对应 §5 验收条目自测通过并勾选；边界守恒 / name↔id remap / 破坏差异 / 存档往返等跨文档验收随 `plan/14` 与 plan/03/06/07 测试落地最终确认。
