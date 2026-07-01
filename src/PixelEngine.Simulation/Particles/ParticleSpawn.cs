using PixelEngine.Core;

namespace PixelEngine.Simulation.Particles;

/// <summary>
/// 单个自由粒子的生成参数。
/// </summary>
/// <remarks>
/// 创建自由粒子生成请求。
/// </remarks>
public readonly record struct ParticleSpawn(
    float X,
    float Y,
    float Vx,
    float Vy,
    ushort Material,
    byte ColorVariant,
    byte Life)
{
    /// <summary>
    /// 转换为活跃粒子，并把寿命钳制到默认粒子系统最大寿命。
    /// </summary>
    public Particle ToParticle()
    {
        return ToParticle(EngineConstants.ParticleMaxLifetimeTicks);
    }

    /// <summary>
    /// 转换为活跃粒子，并把寿命钳制到指定粒子系统最大寿命。
    /// </summary>
    /// <param name="maxLifetimeTicks">当前粒子系统寿命上限。</param>
    /// <returns>活跃粒子。</returns>
    public Particle ToParticle(int maxLifetimeTicks)
    {
        maxLifetimeTicks = Math.Clamp(maxLifetimeTicks, 1, byte.MaxValue);
        return new Particle
        {
            X = X,
            Y = Y,
            Vx = Vx,
            Vy = Vy,
            Material = Material,
            ColorVariant = ColorVariant,
            Life = Life > maxLifetimeTicks ? (byte)maxLifetimeTicks : Life,
        };
    }
}
