namespace PixelEngine.Serialization;

/// <summary>
/// 世界全局态存档导出入口，由 particles 与 physics 所在的宿主层实现。
/// </summary>
public interface IWorldStateSnapshotSource
{
    /// <summary>
    /// 当前在飞自由粒子数量。
    /// </summary>
    int FreeParticleCount { get; }

    /// <summary>
    /// 当前刚体数量。
    /// </summary>
    int RigidBodyCount { get; }

    /// <summary>
    /// 拷贝自由粒子快照到调用方提供的缓冲区。
    /// </summary>
    void CopyFreeParticles(Span<FreeParticleSnapshot> destination);

    /// <summary>
    /// 拷贝刚体快照到调用方提供的缓冲区。
    /// </summary>
    void CopyRigidBodies(Span<RigidBodySnapshot> destination);
}
