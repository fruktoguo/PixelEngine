using System.Text.Json.Serialization;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 内容配置 JSON source-generation 上下文；实际文件读取仍由 Hosting Content/Config API 执行。
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(WeaponCatalog))]
public sealed partial class DemoConfigJsonContext : JsonSerializerContext;

/// <summary>
/// Demo 数据驱动武器目录；由 Hosting Content/Config API 从 content/weapons.json 加载。
/// </summary>
public sealed class WeaponCatalog
{
    /// <summary>
    /// 可装备武器定义，顺序即 HUD 与数字键默认顺序。
    /// </summary>
    public WeaponDefinition[] Weapons { get; init; } = [];
}

/// <summary>
/// 武器类型，决定 WeaponController 分派到的公开引擎 API。
/// </summary>
public enum WeaponKind
{
    /// <summary>即时命中小当量射击。</summary>
    SingleShot,

    /// <summary>放置式炸弹。</summary>
    Bomb,

    /// <summary>抛物线延时手榴弹。</summary>
    Grenade,

    /// <summary>持续光束武器。</summary>
    Laser,

    /// <summary>挖掘工具。</summary>
    Excavator,

    /// <summary>建造工具。</summary>
    Builder,
}

/// <summary>
/// 区域破坏随距离的衰减曲线。
/// </summary>
public enum WeaponFalloff
{
    /// <summary>不衰减。</summary>
    None,

    /// <summary>线性衰减。</summary>
    Linear,

    /// <summary>二次衰减。</summary>
    Quadratic,
}

/// <summary>
/// 单个武器的数据定义。材质和音效都以稳定字符串键引用。
/// </summary>
public sealed class WeaponDefinition
{
    /// <summary>稳定武器 id。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>HUD 展示名。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>武器类型。</summary>
    public WeaponKind Kind { get; init; }

    /// <summary>结构破坏当量。</summary>
    public float Damage { get; init; }

    /// <summary>作用半径。</summary>
    public int Radius { get; init; }

    /// <summary>伤害衰减曲线。</summary>
    public WeaponFalloff Falloff { get; init; }

    /// <summary>径向冲量强度。</summary>
    public float Impulse { get; init; }

    /// <summary>引信时间，单位秒。</summary>
    public float FuseSeconds { get; init; }

    /// <summary>投掷初速。</summary>
    public float ThrowSpeed { get; init; }

    /// <summary>投射物重力。</summary>
    public float Gravity { get; init; }

    /// <summary>碰撞反弹系数。</summary>
    public float Bounce { get; init; }

    /// <summary>开火冷却，单位秒。</summary>
    public float CooldownSeconds { get; init; }

    /// <summary>最大弹药。</summary>
    public int AmmoMax { get; init; }

    /// <summary>换弹时间，单位秒。</summary>
    public float ReloadSeconds { get; init; }

    /// <summary>每 cell 注热量。</summary>
    public float HeatPerCell { get; init; }

    /// <summary>光束每秒破坏当量。</summary>
    public float BeamDps { get; init; }

    /// <summary>建造或生成材质名。</summary>
    public string SpawnMaterial { get; init; } = string.Empty;

    /// <summary>散布角度。</summary>
    public float Spread { get; init; }

    /// <summary>后坐力强度。</summary>
    public float Recoil { get; init; }

    /// <summary>屏幕震动强度。</summary>
    public float ScreenShake { get; init; }

    /// <summary>弹道显示时长。</summary>
    public float TracerDuration { get; init; }

    /// <summary>枪口音效 cue。</summary>
    public string MuzzleCue { get; init; } = string.Empty;

    /// <summary>命中音效 cue。</summary>
    public string ImpactCue { get; init; } = string.Empty;

    /// <summary>HUD 色值字符串。</summary>
    public string HudColor { get; init; } = string.Empty;
}
