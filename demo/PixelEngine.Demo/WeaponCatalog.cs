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

    /// <summary>
    /// 校验武器目录语义，避免坏内容包在运行时退化为空武器或默认值武器。
    /// </summary>
    public void Validate()
    {
        if (Weapons.Length == 0)
        {
            throw new InvalidDataException("武器目录不能为空。");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int i = 0; i < Weapons.Length; i++)
        {
            WeaponDefinition weapon = Weapons[i] ??
                throw new InvalidDataException($"武器目录第 {i} 项为空。");
            string label = string.IsNullOrWhiteSpace(weapon.Id) ? $"#{i}" : weapon.Id;
            Require(!string.IsNullOrWhiteSpace(weapon.Id), label, "id 不能为空。");
            Require(ids.Add(weapon.Id), label, "id 重复。");
            Require(!string.IsNullOrWhiteSpace(weapon.DisplayName), label, "displayName 不能为空。");
            Require(Enum.IsDefined(weapon.Kind), label, "kind 无效。");
            Require(Enum.IsDefined(weapon.Falloff), label, "falloff 无效。");
            Require(weapon.Damage >= 0f, label, "damage 不能为负。");
            Require(weapon.Radius >= 0, label, "radius 不能为负。");
            Require(weapon.Impulse >= 0f, label, "impulse 不能为负。");
            Require(weapon.FuseSeconds >= 0f, label, "fuseSeconds 不能为负。");
            Require(weapon.ThrowSpeed >= 0f, label, "throwSpeed 不能为负。");
            Require(weapon.Gravity >= 0f, label, "gravity 不能为负。");
            Require(weapon.Bounce is >= 0f and <= 1f, label, "bounce 必须位于 [0,1]。");
            Require(weapon.CooldownSeconds >= 0f, label, "cooldownSeconds 不能为负。");
            Require(weapon.AmmoMax > 0, label, "ammoMax 必须为正。");
            Require(weapon.ReloadSeconds >= 0f, label, "reloadSeconds 不能为负。");
            Require(weapon.HeatPerCell >= 0f, label, "heatPerCell 不能为负。");
            Require(weapon.BeamDps >= 0f, label, "beamDps 不能为负。");
            Require(weapon.Spread >= 0f, label, "spread 不能为负。");
            Require(weapon.Recoil >= 0f, label, "recoil 不能为负。");
            Require(weapon.ScreenShake >= 0f, label, "screenShake 不能为负。");
            Require(weapon.TracerDuration >= 0f, label, "tracerDuration 不能为负。");
            Require(!string.IsNullOrWhiteSpace(weapon.MuzzleCue), label, "muzzleCue 不能为空。");
            Require(!string.IsNullOrWhiteSpace(weapon.ImpactCue), label, "impactCue 不能为空。");
            Require(IsBgraHex(weapon.HudColor), label, "hudColor 必须是 #AARRGGBB。");
            ValidateKindSpecific(weapon, label);
        }
    }

    private static void ValidateKindSpecific(WeaponDefinition weapon, string label)
    {
        switch (weapon.Kind)
        {
            case WeaponKind.SingleShot:
                Require(weapon.Damage > 0f, label, "singleShot.damage 必须为正。");
                Require(weapon.Radius > 0, label, "singleShot.radius 必须为正。");
                Require(weapon.Impulse > 0f, label, "singleShot.impulse 必须为正。");
                Require(weapon.TracerDuration > 0f, label, "singleShot.tracerDuration 必须为正。");
                break;
            case WeaponKind.Bomb:
                Require(weapon.Damage > 0f, label, "bomb.damage 必须为正。");
                Require(weapon.Radius > 0, label, "bomb.radius 必须为正。");
                Require(weapon.Impulse > 0f, label, "bomb.impulse 必须为正。");
                break;
            case WeaponKind.Grenade:
                Require(weapon.Damage > 0f, label, "grenade.damage 必须为正。");
                Require(weapon.Radius > 0, label, "grenade.radius 必须为正。");
                Require(weapon.Impulse > 0f, label, "grenade.impulse 必须为正。");
                Require(weapon.FuseSeconds > 0f, label, "grenade.fuseSeconds 必须为正。");
                Require(weapon.ThrowSpeed > 0f, label, "grenade.throwSpeed 必须为正。");
                Require(weapon.Gravity > 0f, label, "grenade.gravity 必须为正。");
                break;
            case WeaponKind.Laser:
                Require(weapon.Radius > 0, label, "laser.radius 必须为正。");
                Require(weapon.HeatPerCell > 0f, label, "laser.heatPerCell 必须为正。");
                Require(weapon.BeamDps > 0f, label, "laser.beamDps 必须为正。");
                break;
            case WeaponKind.Excavator:
                Require(weapon.Radius > 0, label, "excavator.radius 必须为正。");
                break;
            case WeaponKind.Builder:
                Require(weapon.Radius > 0, label, "builder.radius 必须为正。");
                Require(!string.IsNullOrWhiteSpace(weapon.SpawnMaterial), label, "builder.spawnMaterial 不能为空。");
                break;
            default:
                throw new InvalidDataException($"武器 {label} kind 无效：{weapon.Kind}。");
        }
    }

    private static bool IsBgraHex(string value)
    {
        if (value.Length != 9 || value[0] != '#')
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            bool hex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }

    private static void Require(bool condition, string weaponId, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException($"武器 {weaponId} 配置无效：{message}");
        }
    }
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
