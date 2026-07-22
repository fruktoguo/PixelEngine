using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 玩家生命与危险材质采样脚本。
/// </summary>
public sealed class PlayerHealth : Behaviour
{
    private PlayerController? _player;
    private MaterialId _lava;
    private MaterialId _fire;
    private MaterialId _acid;
    private PlayerVisual? _visual;
    private CampaignRunDirector? _runDirector;
    private float _hurtCooldown;
    private bool _materialsResolved;

    /// <summary>
    /// 最大生命值。
    /// </summary>
    public float MaxHealth { get; set; } = 100f;

    /// <summary>
    /// 当前生命值。
    /// </summary>
    public float Health { get; private set; }

    /// <summary>
    /// 本次运行累计受到伤害的次数，供窗口探针与 HUD 验证。
    /// </summary>
    public int DamageEventCount { get; private set; }

    /// <summary>
    /// 本次运行累计重生次数。
    /// </summary>
    public int RespawnCount { get; private set; }

    /// <summary>
    /// 熔岩每秒伤害。
    /// </summary>
    public float LavaDamagePerSecond { get; set; } = 75f;

    /// <summary>
    /// 火焰每秒伤害。
    /// </summary>
    public float FireDamagePerSecond { get; set; } = 35f;

    /// <summary>
    /// 酸液每秒伤害。
    /// </summary>
    public float AcidDamagePerSecond { get; set; } = 45f;

    /// <summary>
    /// 受击音效与视觉闪烁的最小间隔，单位秒。
    /// </summary>
    public float HurtSoundCooldown { get; set; } = 0.35f;

    /// <summary>
    /// 最近一次采样到的危险 cell 数量。
    /// </summary>
    public int HazardContactCellCount { get; private set; }

    /// <summary>
    /// 危险接触覆盖度；一整行玩家宽度计为 1，范围 0..1。
    /// </summary>
    public float HazardContactFraction { get; private set; }

    /// <summary>
    /// 玩家受击闪烁持续时间，单位秒。
    /// </summary>
    public float HurtFlashSeconds { get; set; } = 0.12f;

    /// <summary>Portal 或其他明确保护效果剩余的全伤害无敌时间。</summary>
    public float InvulnerabilityRemainingSeconds { get; private set; }

    /// <summary>
    /// 强制按熔岩伤害计算；仅供窗口运行态健康链路探针使用。
    /// </summary>
    public bool ForceHazardForProbe { get; set; }

    /// <inheritdoc />
    protected override void OnStart()
    {
        Health = MaxHealth;
        InvulnerabilityRemainingSeconds = 0f;
        ResolveMaterials();
        _ = Entity.TryGetComponent<PlayerController>(out _player);
        _ = Entity.TryGetComponent<PlayerVisual>(out _visual);
        _ = Entity.TryGetComponent<CampaignRunDirector>(out _runDirector);
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        if (_player is null)
        {
            _ = Entity.TryGetComponent<PlayerController>(out _player);
            if (_player is null)
            {
                return;
            }
        }

        ResolveMaterials();
        _hurtCooldown = MathF.Max(0f, _hurtCooldown - dt);
        InvulnerabilityRemainingSeconds = MathF.Max(0f, InvulnerabilityRemainingSeconds - dt);
        if (InvulnerabilityRemainingSeconds > 0f)
        {
            HazardContactCellCount = 0;
            HazardContactFraction = 0f;
            return;
        }

        float damage = SampleHazardDamage() * dt;
        if (damage <= 0f)
        {
            return;
        }

        ApplyDamage(damage);
    }

    /// <summary>
    /// 恢复到满血并让玩家回到出生点。
    /// </summary>
    public void Respawn()
    {
        Health = MaxHealth;
        InvulnerabilityRemainingSeconds = 0f;
        HazardContactCellCount = 0;
        HazardContactFraction = 0f;
        RespawnCount++;
        _player?.Respawn();
    }

    /// <summary>
    /// 由脚本层外部碰撞事件施加伤害，例如动态刚体碎块主动压入玩家 AABB。
    /// </summary>
    /// <param name="amount">伤害量。</param>
    public void ApplyExternalDamage(float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0f || InvulnerabilityRemainingSeconds > 0f)
        {
            return;
        }

        ApplyDamage(amount);
    }

    /// <summary>
    /// 授予一段有界的全伤害无敌时间；较长的现有效果不会被较短请求覆盖。
    /// </summary>
    /// <param name="seconds">持续时间，单位秒。</param>
    public void GrantInvulnerability(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f)
        {
            return;
        }

        InvulnerabilityRemainingSeconds = MathF.Max(
            InvulnerabilityRemainingSeconds,
            Math.Clamp(seconds, 0f, 1f));
    }

    private void ResolveMaterials()
    {
        if (_materialsResolved)
        {
            return;
        }

        _lava = Context.Materials.Resolve("lava");
        _fire = Context.Materials.Resolve("fire");
        _acid = Context.Materials.Resolve("acid");
        _materialsResolved = _lava.IsValid && _fire.IsValid && _acid.IsValid;
    }

    private float SampleHazardDamage()
    {
        if (ForceHazardForProbe)
        {
            HazardContactCellCount = 1;
            HazardContactFraction = 1f;
            return LavaDamagePerSecond;
        }

        CharacterState state = _player!.State;
        int minX = (int)MathF.Floor(state.X);
        int minY = (int)MathF.Floor(state.Y);
        int maxX = (int)MathF.Ceiling(state.X + state.Width);
        int maxY = (int)MathF.Ceiling(state.Y + state.Height);
        int horizontalSamples = Math.Max(1, maxX - minX);
        int hazardCells = 0;
        float accumulatedDamage = 0f;
        float peakDamage = 0f;
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                if (!TryGetMaterial(x, y, out MaterialId material))
                {
                    continue;
                }

                float cellDamage = DamageFor(material);
                if (cellDamage <= 0f)
                {
                    continue;
                }

                hazardCells++;
                accumulatedDamage += cellDamage;
                peakDamage = MathF.Max(peakDamage, cellDamage);
            }
        }

        HazardContactCellCount = hazardCells;
        HazardContactFraction = Math.Clamp(hazardCells / (float)horizontalSamples, 0f, 1f);
        return peakDamage <= 0f
            ? 0f
            : MathF.Min(peakDamage, accumulatedDamage / horizontalSamples);
    }

    private bool TryGetMaterial(int x, int y, out MaterialId material)
    {
        try
        {
            material = Context.Cells.GetMaterial(x, y);
            return true;
        }
        catch (InvalidOperationException exception) when (IsUnresidentChunk(exception))
        {
            material = default;
            return false;
        }
    }

    private static bool IsUnresidentChunk(InvalidOperationException exception)
    {
        return exception.Message.Contains("目标 chunk 未驻留", StringComparison.Ordinal);
    }

    private float DamageFor(MaterialId material)
    {
        if (material == _lava)
        {
            return LavaDamagePerSecond;
        }

        if (material == _fire)
        {
            return FireDamagePerSecond;
        }

        if (material == _acid)
        {
            return AcidDamagePerSecond;
        }

        if (!material.IsValid)
        {
            return 0f;
        }

        MaterialInfo info = Context.Materials.GetInfo(material);
        if (IsNamedOrTagged(info, "acid"))
        {
            return AcidDamagePerSecond;
        }

        if (IsNamedOrTagged(info, "fire"))
        {
            return FireDamagePerSecond;
        }

        bool hotHazard =
            info.TemperatureOfFire > 0 &&
            (IsHazard(info) ||
             info.Emissive ||
             IsNamedOrTagged(info, "molten"));
        if (hotHazard)
        {
            float heatScale = Math.Clamp(info.TemperatureOfFire / 255f, 0.25f, 1f);
            return MathF.Max(FireDamagePerSecond, LavaDamagePerSecond * heatScale);
        }

        return IsHazard(info)
            ? FireDamagePerSecond
            : 0f;
    }

    private static bool IsHazard(MaterialInfo info)
    {
        return string.Equals(info.LegendCategory, "Hazard", StringComparison.Ordinal);
    }

    private static bool IsNamedOrTagged(MaterialInfo info, string token)
    {
        return info.Name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            info.DisplayName.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyDamage(float amount)
    {
        Health = MathF.Max(0f, Health - amount);
        DamageEventCount++;
        EmitHurtFeedback();
        if (Health <= 0f)
        {
            if (_runDirector is null)
            {
                _ = Entity.TryGetComponent<CampaignRunDirector>(out _runDirector);
            }

            if (_runDirector?.HandlePlayerDeath() != true)
            {
                Respawn();
            }
        }
    }

    private void EmitHurtFeedback()
    {
        if (_player is null)
        {
            return;
        }

        if (_hurtCooldown > 0f)
        {
            return;
        }

        if (_visual is null)
        {
            _ = Entity.TryGetComponent<PlayerVisual>(out _visual);
        }

        _visual?.ShowDamageFeedback(HurtFlashSeconds);
        Context.Audio.PlayAt("player_hurt.wav", _player.CenterX, _player.CenterY);
        _hurtCooldown = MathF.Max(0f, HurtSoundCooldown);
    }
}
