using PixelEngine.Core;

namespace PixelEngine.Simulation.Particles;

/// <summary>
/// 自由粒子的连续缓冲池。活跃粒子始终位于数组前缀，释放使用 swap-remove，稳态不扩容。
/// </summary>
public sealed class ParticleSystem
{
    private readonly Particle[] _particles;
    private int _spawnedThisTick;
    private int _depositedThisTick;
    private int _killedByLifetimeThisTick;
    private int _droppedThisTick;

    /// <summary>
    /// 创建指定容量的自由粒子系统。
    /// </summary>
    public ParticleSystem(int capacity = EngineConstants.ParticleCapacityDefault)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _particles = GC.AllocateArray<Particle>(capacity, pinned: true);
    }

    /// <summary>
    /// 当前活跃粒子数量。
    /// </summary>
    public int ActiveCount { get; private set; }

    /// <summary>
    /// 固定粒子容量。
    /// </summary>
    public int Capacity => _particles.Length;

    /// <summary>
    /// 活跃粒子的可写连续前缀视图。
    /// </summary>
    public Span<Particle> Active => _particles.AsSpan(0, ActiveCount);

    /// <summary>
    /// 活跃粒子的只读连续前缀视图。
    /// </summary>
    public ReadOnlySpan<Particle> ActiveReadOnly => _particles.AsSpan(0, ActiveCount);

    /// <summary>
    /// 当前 tick 的诊断计数。
    /// </summary>
    public ParticleSystemStats Stats => new(
        ActiveCount,
        Capacity,
        _spawnedThisTick,
        _depositedThisTick,
        _killedByLifetimeThisTick,
        _droppedThisTick);

    /// <summary>
    /// 清空本 tick 的增量诊断计数，不改变活跃粒子。
    /// </summary>
    public void ResetTickStats()
    {
        _spawnedThisTick = 0;
        _depositedThisTick = 0;
        _killedByLifetimeThisTick = 0;
        _droppedThisTick = 0;
    }

    /// <summary>
    /// 尝试生成一个自由粒子。容量满时返回 false，并计入 dropped 诊断，不扩容。
    /// </summary>
    public bool TrySpawn(in ParticleSpawn spawn)
    {
        if (ActiveCount >= _particles.Length)
        {
            _droppedThisTick++;
            return false;
        }

        _particles[ActiveCount++] = spawn.ToParticle();
        _spawnedThisTick++;
        return true;
    }

    /// <summary>
    /// 以 swap-remove 释放指定活跃粒子槽位。
    /// </summary>
    public void RemoveAtSwapBack(int index)
    {
        if ((uint)index >= (uint)ActiveCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int last = --ActiveCount;
        if (index != last)
        {
            _particles[index] = _particles[last];
        }

        _particles[last] = default;
    }

    /// <summary>
    /// 清空全部活跃粒子并重置 tick 诊断。
    /// </summary>
    public void Clear()
    {
        _particles.AsSpan(0, ActiveCount).Clear();
        ActiveCount = 0;
        ResetTickStats();
    }
}
