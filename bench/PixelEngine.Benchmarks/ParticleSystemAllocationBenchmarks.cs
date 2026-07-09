using BenchmarkDotNet.Attributes;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 热路径：ParticleSystem 发射/回收的托管分配。
/// </summary>
[MemoryDiagnoser]
public class ParticleSystemAllocationBenchmarks
{
    private readonly ParticleSystem _particles = new(capacity: 1024);
    private readonly ParticleSpawn _spawn = new(1, 2, 3, 4, 5, 6, 7);
    private readonly DebrisEjectionRequest _debris = new(32, 48, 5, Count: 32, BaseSpeed: 1.5f, SpeedJitter: 0.75f, LifeTicks: 24);
    private readonly ParticleEmissionRequest _emit = new(32.5f, 48.5f, 6, Count: 32, DirAngleRad: 0.25f, DirSpreadRad: 0.5f, BaseSpeed: 1.25f, SpeedJitter: 0.4f, LifeTicks: 18);

    /// <summary>
    /// 验证Spawn And Remove Swap Back。
    /// </summary>
    [Benchmark]
    public void SpawnAndRemoveSwapBack()
    {
        _ = _particles.TrySpawn(in _spawn);
        _particles.RemoveAtSwapBack(_particles.ActiveCount - 1);
    }

    /// <summary>
    /// 结构破坏碎屑批量发射的稳态分配基准。
    /// </summary>
    [Benchmark]
    public void RequestDebrisBatch()
    {
        _particles.ResetTickStats();
        _particles.RequestDebris(in _debris);
        _particles.Clear();
    }

    /// <summary>
    /// 富速度锥火花批量发射的稳态分配基准。
    /// </summary>
    [Benchmark]
    public void EmitVelocityConeBatch()
    {
        _particles.ResetTickStats();
        _ = _particles.Emit(in _emit);
        _particles.Clear();
    }
}
