namespace PixelEngine.Simulation.Particles;

/// <summary>
/// 粒子单步模拟结果的分类。
/// </summary>
internal enum ParticleOutcomeKind : byte
{
    /// <summary>继续存在于粒子系统中。</summary>
    Flying,
    /// <summary>请求写入目标格子并移除粒子。</summary>
    WantsDeposit,
    /// <summary>粒子被销毁，不产生沉积。</summary>
    Dead,
}
