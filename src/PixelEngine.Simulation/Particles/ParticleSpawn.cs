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
    /// 转换为活跃粒子，并把寿命钳制到粒子系统最大寿命。
    /// </summary>
    public Particle ToParticle()
    {
        return new Particle
        {
            X = X,
            Y = Y,
            Vx = Vx,
            Vy = Vy,
            Material = Material,
            ColorVariant = ColorVariant,
            Life = Life > EngineConstants.ParticleMaxLifetimeTicks ? EngineConstants.ParticleMaxLifetimeTicks : Life,
        };
    }
}
