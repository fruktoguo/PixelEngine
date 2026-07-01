using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 侧反应副作用桥，连接 ReactionEngine、温度场与自由粒子系统。
/// </summary>
internal sealed class SimulationReactionSideEffects(
    TemperatureField temperature,
    ParticleSystem particles,
    MaterialTable materials) : IReactionSideEffectSink
{
    private const int MaxSmokeParticlesPerEvent = 4;
    private readonly TemperatureField _temperature = temperature ?? throw new ArgumentNullException(nameof(temperature));
    private readonly ParticleSystem _particles = particles ?? throw new ArgumentNullException(nameof(particles));
    private readonly MaterialTable _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    private readonly ushort _smokeMaterial = ResolveSmokeMaterial(materials);

    /// <summary>
    /// 将反应产生的热量写入 Hosting 持有的温度场。
    /// </summary>
    public void AddHeat(int wx, int wy, ushort sourceMaterial, byte heat)
    {
        if (heat == 0)
        {
            return;
        }

        _temperature.AddHeat(wx, wy, sourceMaterial, _materials.Hot, heat);
    }

    /// <summary>
    /// 将反应产生的像素喷射请求转发给自由粒子系统。
    /// </summary>
    public bool RequestParticleEjection(in EjectionRequest request)
    {
        return _particles.RequestEjection(in request);
    }

    /// <summary>
    /// 按反应产烟强度生成少量 smoke 或 steam 自由粒子。
    /// </summary>
    public void EmitSmoke(int wx, int wy, ushort sourceMaterial, byte amount)
    {
        _ = sourceMaterial;
        if (amount == 0 || _smokeMaterial == 0)
        {
            return;
        }

        int count = Math.Clamp((amount + 63) / 64, 1, MaxSmokeParticlesPerEvent);
        for (int i = 0; i < count; i++)
        {
            float offset = (i - ((count - 1) * 0.5f)) * 0.35f;
            ParticleSpawn spawn = new(
                wx + 0.5f + offset,
                wy + 0.5f,
                offset * 0.08f,
                -0.16f - (0.03f * i),
                _smokeMaterial,
                ColorVariant: 0,
                Life: DefaultLifetime(_smokeMaterial));
            _ = _particles.TrySpawn(in spawn);
        }
    }

    private static ushort ResolveSmokeMaterial(MaterialTable materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        return materials.TryGetId("smoke", out ushort smoke)
            ? smoke
            : materials.TryGetId("steam", out ushort steam) ? steam : (ushort)0;
    }

    private byte DefaultLifetime(ushort material)
    {
        ushort lifetime = _materials.Hot.DefaultLifetime[material];
        return lifetime == 0
            ? (byte)1
            : lifetime > byte.MaxValue ? byte.MaxValue : (byte)lifetime;
    }
}
