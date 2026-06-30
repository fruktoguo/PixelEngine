namespace PixelEngine.Serialization;

/// <summary>
/// 世界全局态读档恢复入口，由 particles 与 physics 所在的宿主层实现。
/// </summary>
public interface IWorldStateSnapshotSink
{
    /// <summary>
    /// 恢复所有在飞自由粒子。
    /// </summary>
    void RestoreFreeParticles(ReadOnlySpan<FreeParticleSnapshot> particles);

    /// <summary>
    /// 恢复所有刚体快照。
    /// </summary>
    void RestoreRigidBodies(ReadOnlySpan<RigidBodySnapshot> bodies);
}
