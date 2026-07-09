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
    private MaterialId _bloodMaterial;
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
    /// 本次运行累计受到危险材质伤害的次数，供窗口探针与 HUD 验证。
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
    /// 受击音效最小间隔，单位秒。
    /// </summary>
    public float HurtSoundCooldown { get; set; } = 0.35f;

    /// <summary>
    /// 受击时喷出的粒子数量。
    /// </summary>
    public int HurtParticleCount { get; set; } = 10;

    /// <summary>
    /// 受击粒子速度。
    /// </summary>
    public float HurtParticleSpeed { get; set; } = 70f;

    /// <summary>
    /// 强制按熔岩伤害计算；仅供窗口运行态健康链路探针使用。
    /// </summary>
    public bool ForceHazardForProbe { get; set; }

    /// <inheritdoc />
    protected override void OnStart()
    {
        Health = MaxHealth;
        ResolveMaterials();
        _ = Entity.TryGetComponent<PlayerController>(out _player);
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
        RespawnCount++;
        _player?.Respawn();
    }

    /// <summary>
    /// 由脚本层外部碰撞事件施加伤害，例如动态刚体碎块主动压入玩家 AABB。
    /// </summary>
    /// <param name="amount">伤害量。</param>
    public void ApplyExternalDamage(float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0f)
        {
            return;
        }

        ApplyDamage(amount);
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
        _bloodMaterial = Context.Materials.Resolve("ash");
        _materialsResolved = _lava.IsValid && _fire.IsValid && _acid.IsValid;
    }

    private float SampleHazardDamage()
    {
        if (ForceHazardForProbe)
        {
            return LavaDamagePerSecond;
        }

        CharacterState state = _player!.State;
        int minX = (int)MathF.Floor(state.X);
        int minY = (int)MathF.Floor(state.Y);
        int maxX = (int)MathF.Ceiling(state.X + state.Width);
        int maxY = (int)MathF.Ceiling(state.Y + state.Height);
        float damage = 0f;
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                if (!TryGetMaterial(x, y, out MaterialId material))
                {
                    continue;
                }

                damage = MathF.Max(damage, DamageFor(material));
            }
        }

        return damage;
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
            Respawn();
        }
    }

    private void EmitHurtFeedback()
    {
        if (_player is null)
        {
            return;
        }

        if (_bloodMaterial.IsValid && HurtParticleCount > 0)
        {
            Context.Particles.Burst(_player.CenterX, _player.CenterY, _bloodMaterial, HurtParticleCount, HurtParticleSpeed);
        }

        if (_hurtCooldown <= 0f)
        {
            Context.Audio.PlayAt("player_hurt.wav", _player.CenterX, _player.CenterY);
            _hurtCooldown = HurtSoundCooldown;
        }
    }
}
