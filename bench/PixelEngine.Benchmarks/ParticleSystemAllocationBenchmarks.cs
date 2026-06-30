using BenchmarkDotNet.Attributes;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 自由粒子连续缓冲池的零分配基准。
/// </summary>
[MemoryDiagnoser]
public class ParticleSystemAllocationBenchmarks
{
    private readonly ParticleSystem _particles = new(capacity: 1024);
    private readonly ParticleSpawn _spawn = new(1, 2, 3, 4, 5, 6, 7);

    /// <summary>
    /// TrySpawn + swap-remove 的稳态分配基准。
    /// </summary>
    [Benchmark]
    public void SpawnAndRemoveSwapBack()
    {
        _ = _particles.TrySpawn(in _spawn);
        _particles.RemoveAtSwapBack(_particles.ActiveCount - 1);
    }
}
