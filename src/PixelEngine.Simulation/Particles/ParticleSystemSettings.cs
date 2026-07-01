using PixelEngine.Core;

namespace PixelEngine.Simulation.Particles;

/// <summary>
/// 自由粒子系统可在帧边界热更新的调参参数。
/// </summary>
/// <param name="MaxActiveCount">允许保持活跃的最大粒子数，不能超过粒子池固定容量。</param>
/// <param name="GravityPerTick">每 tick 追加到 Y 速度的重力增量，单位 cell/tick^2。</param>
/// <param name="MaxLifetimeTicks">粒子寿命上限，写入 byte 生命值前会钳制到 1..255。</param>
/// <param name="DepositSpeedEpsilon">速度平方低于该阈值平方时进入沉积判定。</param>
/// <param name="EjectionImpulseScale">cell 抛射为粒子时对请求冲量的全局倍率。</param>
/// <param name="MaxEjectionPerTick">单 tick 从 cell 抛射为自由粒子的上限。</param>
public readonly record struct ParticleSystemSettings(
    int MaxActiveCount,
    float GravityPerTick,
    int MaxLifetimeTicks,
    float DepositSpeedEpsilon,
    float EjectionImpulseScale,
    int MaxEjectionPerTick)
{
    /// <summary>
    /// 默认粒子调参，等价于历史编译期常量。
    /// </summary>
    public static ParticleSystemSettings Default => new(
        EngineConstants.ParticleCapacityDefault,
        EngineConstants.ParticleGravityPerTick,
        EngineConstants.ParticleMaxLifetimeTicks,
        EngineConstants.ParticleDepositSpeedEpsilon,
        EjectionImpulseScale: 1f,
        EngineConstants.ParticleEjectMaxPerTick);

    /// <summary>
    /// 针对固定池容量归一化参数，保证后续热路径无需重复做复杂校验。
    /// </summary>
    /// <param name="capacity">粒子池固定容量。</param>
    /// <returns>已钳制到安全范围的参数。</returns>
    public ParticleSystemSettings Normalize(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ValidateFinite(GravityPerTick, nameof(GravityPerTick));
        ValidateFinite(DepositSpeedEpsilon, nameof(DepositSpeedEpsilon));
        ValidateFinite(EjectionImpulseScale, nameof(EjectionImpulseScale));
        return this with
        {
            MaxActiveCount = Math.Clamp(MaxActiveCount, 1, capacity),
            MaxLifetimeTicks = Math.Clamp(MaxLifetimeTicks, 1, byte.MaxValue),
            DepositSpeedEpsilon = Math.Max(0f, DepositSpeedEpsilon),
            EjectionImpulseScale = Math.Max(0f, EjectionImpulseScale),
            MaxEjectionPerTick = Math.Clamp(MaxEjectionPerTick, 0, EngineConstants.ParticleEjectMaxPerTick),
        };
    }

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "粒子调参必须是有限数值。");
        }
    }
}
