using PixelEngine.Serialization;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 侧世界全局态桥，负责在 WorldSaveService 与当前 ParticleSystem/Physics 后端之间转换快照。
/// </summary>
internal sealed class RuntimeWorldStateBridge(ParticleSystem particles) : IWorldStateSnapshotSource, IWorldStateSnapshotSink
{
    private readonly ParticleSystem _particles = particles ?? throw new ArgumentNullException(nameof(particles));

    /// <summary>
    /// 当前在飞自由粒子数量。
    /// </summary>
    public int FreeParticleCount => _particles.ActiveCount;

    /// <summary>
    /// 当前刚体数量；Physics 快照后端接入前固定为 0。
    /// </summary>
    public int RigidBodyCount => 0;

    /// <summary>
    /// 将当前自由粒子活跃前缀转换为存档 DTO。
    /// </summary>
    /// <param name="destination">目标快照缓冲区。</param>
    public void CopyFreeParticles(Span<FreeParticleSnapshot> destination)
    {
        ReadOnlySpan<Particle> active = _particles.ActiveReadOnly;
        if (destination.Length < active.Length)
        {
            throw new ArgumentException("自由粒子快照目标缓冲区不足。", nameof(destination));
        }

        for (int i = 0; i < active.Length; i++)
        {
            Particle particle = active[i];
            destination[i] = new FreeParticleSnapshot(
                particle.X,
                particle.Y,
                particle.Vx,
                particle.Vy,
                particle.Material,
                particle.ColorVariant,
                particle.Life);
        }
    }

    /// <summary>
    /// 导出刚体快照；Physics 后端接入前不应被请求。
    /// </summary>
    /// <param name="destination">目标刚体快照缓冲区。</param>
    public void CopyRigidBodies(Span<RigidBodySnapshot> destination)
    {
        if (!destination.IsEmpty)
        {
            throw new InvalidOperationException("Physics 后端未接入，不能导出刚体快照。");
        }
    }

    /// <summary>
    /// 将已完成材质重映射的自由粒子存档 DTO 恢复到 ParticleSystem。
    /// </summary>
    /// <param name="snapshots">自由粒子快照。</param>
    public void RestoreFreeParticles(ReadOnlySpan<FreeParticleSnapshot> snapshots)
    {
        Particle[] particles = new Particle[snapshots.Length];
        for (int i = 0; i < snapshots.Length; i++)
        {
            FreeParticleSnapshot snapshot = snapshots[i];
            particles[i] = new Particle
            {
                X = snapshot.X,
                Y = snapshot.Y,
                Vx = snapshot.Vx,
                Vy = snapshot.Vy,
                Material = snapshot.Material,
                ColorVariant = snapshot.ColorVariant,
                Life = snapshot.Life,
            };
        }

        _particles.RestoreFrom(particles);
    }

    /// <summary>
    /// 恢复刚体快照；Physics 后端接入前遇到非空快照明确失败。
    /// </summary>
    /// <param name="bodies">刚体快照。</param>
    public void RestoreRigidBodies(ReadOnlySpan<RigidBodySnapshot> bodies)
    {
        if (!bodies.IsEmpty)
        {
            throw new NotSupportedException("当前 Hosting 尚未接入 Physics 刚体快照恢复后端。");
        }
    }
}
