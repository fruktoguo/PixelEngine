namespace PixelEngine.Simulation;

/// <summary>
/// 材质定义。<see cref="Name" /> 是稳定字符串键，运行时 <see cref="Id" /> 仅作数组索引，绝不入盘。
/// </summary>
public readonly record struct MaterialDef
{
    /// <summary>
    /// 创建默认材质定义。默认相变阈值为 NaN，HeatCapacity 为 1，TextureId 为 -1。
    /// </summary>
    public MaterialDef()
    {
    }

    /// <summary>
    /// 运行时材质 id，必须等于材质表数组下标。
    /// </summary>
    public ushort Id { get; init; }

    /// <summary>
    /// 稳定字符串键，用于内容加载、存档 remap 与热重载。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// CA movement 消费的基础 cell 类型。
    /// </summary>
    public CellType Type { get; init; }

    /// <summary>
    /// 材质密度；目标密度小于源材质时允许位移。
    /// </summary>
    public byte Density { get; init; }

    /// <summary>
    /// 液体或气体每步横向扩散距离。
    /// </summary>
    public byte Dispersion { get; init; }

    /// <summary>
    /// 标记该液体是否不参与普通流动。
    /// </summary>
    public bool LiquidStatic { get; init; }

    /// <summary>
    /// 标记该液体是否按粉末式规则下落。
    /// </summary>
    public bool LiquidSand { get; init; }

    /// <summary>
    /// 接触点燃概率权重，范围 0-255。
    /// </summary>
    public byte Flammability { get; init; }

    /// <summary>
    /// 自燃温度阈值，单位摄氏度；0 表示不开启自燃检查。
    /// </summary>
    public ushort AutoIgnitionTemp { get; init; }

    /// <summary>
    /// 燃烧耐久；-1 表示永燃。
    /// </summary>
    public int FireHp { get; init; }

    /// <summary>
    /// burning cell 每 tick 注入温度场的热量基准。
    /// </summary>
    public byte TemperatureOfFire { get; init; }

    /// <summary>
    /// 燃烧或反应产烟倾向，0 表示不产烟。
    /// </summary>
    public byte GeneratesSmoke { get; init; }

    /// <summary>
    /// 熔化阈值；使用 <see cref="float.NaN" /> 表示无熔化相变。
    /// </summary>
    public float MeltPoint { get; init; } = float.NaN;

    /// <summary>
    /// 熔化目标材质 id。
    /// </summary>
    public ushort MeltTarget { get; init; }

    /// <summary>
    /// 凝固阈值；使用 <see cref="float.NaN" /> 表示无凝固相变。
    /// </summary>
    public float FreezePoint { get; init; } = float.NaN;

    /// <summary>
    /// 凝固目标材质 id。
    /// </summary>
    public ushort FreezeTarget { get; init; }

    /// <summary>
    /// 沸腾阈值；使用 <see cref="float.NaN" /> 表示无沸腾相变。
    /// </summary>
    public float BoilPoint { get; init; } = float.NaN;

    /// <summary>
    /// 沸腾目标材质 id。
    /// </summary>
    public ushort BoilTarget { get; init; }

    /// <summary>
    /// 每帧热传导概率权重，范围 0-255。
    /// </summary>
    public byte HeatConduct { get; init; }

    /// <summary>
    /// 热容量；构建材质表时必须非零。
    /// </summary>
    public float HeatCapacity { get; init; } = 1f;

    /// <summary>
    /// fire、gas 或自由粒子使用的默认 lifetime。
    /// </summary>
    public ushort DefaultLifetime { get; init; }

    /// <summary>
    /// 抗腐蚀、抗挖或脚本化破坏耐久。
    /// </summary>
    public byte Durability { get; init; }

    /// <summary>
    /// 结构破坏吸收强度；数值越高，同等 Damage 越难累积。
    /// </summary>
    public byte Hardness { get; init; }

    /// <summary>
    /// 累计结构完整度阈值；0 表示有效伤害命中后即时破坏。
    /// </summary>
    public ushort MaxIntegrity { get; init; }

    /// <summary>
    /// 结构破坏后的目标材质 id；0 表示破坏后清空为 Empty。
    /// </summary>
    public ushort RubbleTarget { get; init; }

    /// <summary>
    /// 材质纹理索引；-1 表示仅使用纯色。
    /// </summary>
    public int TextureId { get; init; } = -1;

    /// <summary>
    /// BGRA8 基色，匹配渲染上传格式。
    /// </summary>
    public uint BaseColorBGRA { get; init; }

    /// <summary>
    /// 便宜颜色噪声幅度。
    /// </summary>
    public byte ColorNoise { get; init; }

    /// <summary>
    /// 材质标签与运行时行为位。
    /// </summary>
    public MaterialProperty PropertyFlags { get; init; }

    /// <summary>
    /// 该材质在 packed 反应表中的起始索引。
    /// </summary>
    public int ReactionStart { get; init; }

    /// <summary>
    /// 该材质可触发的反应数量。
    /// </summary>
    public byte ReactionCount { get; init; }

    /// <summary>
    /// 材质化音效 cue 句柄集合。
    /// </summary>
    public AudioCueSet AudioCues { get; init; }

    internal static MaterialDef Tombstone(ushort id)
    {
        return new MaterialDef
        {
            Id = id,
            Name = string.Empty,
            Type = CellType.Empty,
            HeatCapacity = 1f,
            TextureId = -1,
        };
    }
}

/// <summary>
/// 材质标签与运行时行为位。
/// </summary>
[Flags]
public enum MaterialProperty : uint
{
    /// <summary>
    /// 无标签。
    /// </summary>
    None = 0,

    /// <summary>
    /// 可熔化 tag。
    /// </summary>
    Meltable = 1u << 0,

    /// <summary>
    /// 酸性 tag。
    /// </summary>
    Acid = 1u << 1,

    /// <summary>
    /// 火焰 tag。
    /// </summary>
    Fire = 1u << 2,

    /// <summary>
    /// 可腐蚀 tag。
    /// </summary>
    Corrodible = 1u << 3,

    /// <summary>
    /// 低温 tag。
    /// </summary>
    Cold = 1u << 4,

    /// <summary>
    /// 熔融金属 tag。
    /// </summary>
    MoltenMetal = 1u << 5,

    /// <summary>
    /// 静态材质 tag。
    /// </summary>
    Static = 1u << 6,

    /// <summary>
    /// 快速燃烧 tag。
    /// </summary>
    BurnableFast = 1u << 7,

    /// <summary>
    /// 发光材质，供渲染 emissive 路径消费。
    /// </summary>
    Emissive = 1u << 8,

    /// <summary>
    /// 材质存在 custom-update 委托。
    /// </summary>
    HasCustomUpdate = 1u << 9,

    /// <summary>
    /// 导电材质预留位。
    /// </summary>
    Conductive = 1u << 10,
}

/// <summary>
/// 可在 reaction JSON 中使用的固定 tag 名。
/// </summary>
public enum MaterialTag : byte
{
    /// <summary>
    /// 可熔化材质集合。
    /// </summary>
    Meltable,

    /// <summary>
    /// 酸性材质集合。
    /// </summary>
    Acid,

    /// <summary>
    /// 火焰材质集合。
    /// </summary>
    Fire,

    /// <summary>
    /// 可腐蚀材质集合。
    /// </summary>
    Corrodible,

    /// <summary>
    /// 低温材质集合。
    /// </summary>
    Cold,

    /// <summary>
    /// 熔融金属材质集合。
    /// </summary>
    MoltenMetal,

    /// <summary>
    /// 静态材质集合。
    /// </summary>
    Static,

    /// <summary>
    /// 快速燃烧材质集合。
    /// </summary>
    BurnableFast,
}

/// <summary>
/// 材质 tag 与 <see cref="MaterialProperty" /> 位的固定映射。
/// </summary>
public static class MaterialTagMap
{
    /// <summary>
    /// 将 tag 映射为对应的材质属性位。
    /// </summary>
    public static MaterialProperty ToProperty(MaterialTag tag)
    {
        return tag switch
        {
            MaterialTag.Meltable => MaterialProperty.Meltable,
            MaterialTag.Acid => MaterialProperty.Acid,
            MaterialTag.Fire => MaterialProperty.Fire,
            MaterialTag.Corrodible => MaterialProperty.Corrodible,
            MaterialTag.Cold => MaterialProperty.Cold,
            MaterialTag.MoltenMetal => MaterialProperty.MoltenMetal,
            MaterialTag.Static => MaterialProperty.Static,
            MaterialTag.BurnableFast => MaterialProperty.BurnableFast,
            _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, "未知材质 tag。"),
        };
    }
}

/// <summary>
/// 材质化音效钩子集合。句柄解析与播放由 Audio 子系统负责。
/// </summary>
public readonly record struct AudioCueSet
{
    /// <summary>
    /// 撞击 cue。
    /// </summary>
    public int ImpactCue { get; init; }

    /// <summary>
    /// 燃烧 cue。
    /// </summary>
    public int FireCue { get; init; }

    /// <summary>
    /// 飞溅 cue。
    /// </summary>
    public int SplashCue { get; init; }

    /// <summary>
    /// 爆炸 cue。
    /// </summary>
    public int ExplosionCue { get; init; }

    /// <summary>
    /// 刚体破碎 cue。
    /// </summary>
    public int ShatterCue { get; init; }

    /// <summary>
    /// 区域 ambient cue。
    /// </summary>
    public int AmbientCue { get; init; }
}
