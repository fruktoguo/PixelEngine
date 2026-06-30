namespace PixelEngine.Simulation.Particles;

/// <summary>
/// 自由粒子只读读回接口，供渲染、编辑器和序列化阶段零拷贝读取活跃前缀。
/// </summary>
public interface IParticleReadback
{
    /// <summary>
    /// 当前活跃粒子数量。
    /// </summary>
    int ActiveCount { get; }

    /// <summary>
    /// 活跃粒子的只读连续前缀。
    /// </summary>
    ReadOnlySpan<Particle> Particles { get; }
}
