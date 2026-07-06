using System.Runtime.CompilerServices;
using PixelEngine.Core;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Plan 05 节点 1 的 Particle 布局与连续缓冲池测试。
/// </summary>
public sealed class ParticleSystemTests
{
    /// <summary>
    /// 验证 Particle 字节预算保持在架构 §7.6 指定的 20B。
    /// </summary>
    [Fact]
    public void ParticleSizeIsTwentyBytes()
    {
        Assert.Equal(20, Unsafe.SizeOf<Particle>());
    }

    /// <summary>
    /// 验证默认容量满足 20 万活跃粒子目标并留有余量。
    /// </summary>
    [Fact]
    public void ParticleConstantsMatchPlan()
    {
        Assert.True(EngineConstants.ParticleCapacityDefault >= 262_144);
        Assert.True(EngineConstants.ParticleMaxLifetimeTicks > 0);
        Assert.True(EngineConstants.ParticleEjectMaxPerTick > 0);
        Assert.True(EngineConstants.ParticleGravityPerTick > 0);
        Assert.True(EngineConstants.ParticleDepositSpeedEpsilon > 0);
    }

    /// <summary>
    /// 验证粒子系统暴露确定性策略 seam，默认保持高性能模式。
    /// </summary>
    [Fact]
    public void DeterminismModeDefaultsToHighPerformanceAndCanBeSelected()
    {
        Assert.Equal(DeterminismMode.HighPerformance, new ParticleSystem(capacity: 1).DeterminismMode);
        Assert.Equal(
            DeterminismMode.Deterministic,
            new ParticleSystem(capacity: 1, determinismMode: DeterminismMode.Deterministic).DeterminismMode);
    }

    /// <summary>
    /// 验证 TrySpawn 写入活跃前缀并在容量满时只计 dropped、不扩容。
    /// </summary>
    [Fact]
    public void TrySpawnWritesActivePrefixAndDropsWhenFull()
    {
        ParticleSystem particles = new(capacity: 2);
        Assert.True(particles.TrySpawn(new ParticleSpawn(1, 2, 3, 4, 5, 6, 7)));
        Assert.True(particles.TrySpawn(new ParticleSpawn(8, 9, 10, 11, 12, 13, 255)));

        Assert.False(particles.TrySpawn(new ParticleSpawn(0, 0, 0, 0, 1, 0, 1)));

        Assert.Equal(2, particles.ActiveCount);
        Assert.Equal(2, particles.Capacity);
        Assert.Equal(2, particles.Stats.SpawnedThisTick);
        Assert.Equal(1, particles.Stats.DroppedThisTick);
        Assert.Equal((1f, 2f, 3f, 4f, (ushort)5, (byte)6, (byte)7), Project(particles.ActiveReadOnly[0]));
        Assert.Equal(EngineConstants.ParticleMaxLifetimeTicks, particles.ActiveReadOnly[1].Life);
    }

    /// <summary>
    /// 验证运行时调参会限制活跃数量并钳制新粒子寿命。
    /// </summary>
    [Fact]
    public void ApplySettingsLimitsActiveCountAndClampsLifetime()
    {
        ParticleSystem particles = new(capacity: 4);
        _ = particles.TrySpawn(new ParticleSpawn(1, 0, 0, 0, 101, 1, 10));
        _ = particles.TrySpawn(new ParticleSpawn(2, 0, 0, 0, 102, 2, 10));
        _ = particles.TrySpawn(new ParticleSpawn(3, 0, 0, 0, 103, 3, 10));

        particles.ApplySettings(new ParticleSystemSettings(2, 0.1f, 5, 0.05f, 1f, 16));
        bool spawned = particles.TrySpawn(new ParticleSpawn(4, 0, 0, 0, 104, 4, 10));

        Assert.False(spawned);
        Assert.Equal(2, particles.ActiveCount);
        Assert.Equal(2, particles.Settings.MaxActiveCount);
        Assert.True(particles.Stats.DroppedThisTick >= 2);
    }

    /// <summary>
    /// 验证 swap-remove 释放保持活跃前缀紧密且不保留尾部旧粒子。
    /// </summary>
    [Fact]
    public void RemoveAtSwapBackKeepsActivePrefixDense()
    {
        ParticleSystem particles = new(capacity: 4);
        _ = particles.TrySpawn(new ParticleSpawn(1, 0, 0, 0, 101, 1, 10));
        _ = particles.TrySpawn(new ParticleSpawn(2, 0, 0, 0, 102, 2, 10));
        _ = particles.TrySpawn(new ParticleSpawn(3, 0, 0, 0, 103, 3, 10));

        particles.RemoveAtSwapBack(1);

        Assert.Equal(2, particles.ActiveCount);
        Assert.Equal((ushort)101, particles.ActiveReadOnly[0].Material);
        Assert.Equal((ushort)103, particles.ActiveReadOnly[1].Material);
        Assert.DoesNotContain(particles.ActiveReadOnly.ToArray(), p => p.Material == 102);
    }

    /// <summary>
    /// 验证 Clear 清空活跃前缀并重置 tick 统计。
    /// </summary>
    [Fact]
    public void ClearResetsActiveParticlesAndTickStats()
    {
        ParticleSystem particles = new(capacity: 1);
        _ = particles.TrySpawn(new ParticleSpawn(1, 0, 0, 0, 101, 1, 10));
        _ = particles.TrySpawn(new ParticleSpawn(2, 0, 0, 0, 102, 2, 10));

        particles.Clear();

        Assert.Equal(0, particles.ActiveCount);
        Assert.Equal(0, particles.Stats.SpawnedThisTick);
        Assert.Equal(0, particles.Stats.DroppedThisTick);
    }

    /// <summary>
    /// 验证 IParticleReadback 暴露活跃前缀只读视图。
    /// </summary>
    [Fact]
    public void ParticleReadbackExposesActivePrefix()
    {
        ParticleSystem particles = new(capacity: 2);
        _ = particles.TrySpawn(new ParticleSpawn(1, 2, 3, 4, 5, 6, 7));
        IParticleReadback readback = particles;

        Assert.Equal(1, readback.ActiveCount);
        Assert.Equal((ushort)5, readback.Particles[0].Material);
    }

    /// <summary>
    /// 验证 RestoreFrom 从已重映射粒子快照重建活跃前缀并清空诊断。
    /// </summary>
    [Fact]
    public void RestoreFromRebuildsActivePrefixAndClearsTickState()
    {
        ParticleSystem particles = new(capacity: 3);
        _ = particles.TrySpawn(new ParticleSpawn(1, 0, 0, 0, 101, 1, 10));
        _ = particles.TrySpawn(new ParticleSpawn(2, 0, 0, 0, 102, 2, 10));
        Particle[] snapshot =
        [
            new Particle { X = 9, Y = 8, Material = 201, Life = 7 },
        ];

        particles.RestoreFrom(snapshot);

        Assert.Equal(1, particles.ActiveCount);
        Assert.Equal((ushort)201, particles.ActiveReadOnly[0].Material);
        Assert.Equal(0, particles.Stats.SpawnedThisTick);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => particles.RestoreFrom(new Particle[4]));
    }

    /// <summary>
    /// 验证粒子诊断发布到 Core EngineCounters。
    /// </summary>
    [Fact]
    public void PublishDiagnosticsWritesEngineCounters()
    {
        ParticleSystem particles = new(capacity: 1);
        _ = particles.TrySpawn(new ParticleSpawn(1, 0, 0, 0, 101, 1, 10));
        _ = particles.TrySpawn(new ParticleSpawn(2, 0, 0, 0, 102, 2, 10));
        EngineCounters counters = new();

        particles.PublishDiagnostics(counters);

        Assert.Equal(1, counters.FreeParticles);
        Assert.Equal(1, counters.FreeParticlesSpawnedThisTick);
        Assert.Equal(1, counters.FreeParticlesDroppedThisTick);
    }

    /// <summary>
    /// 验证结构破坏碎屑请求直接生成粒子，且不需要读取或清空源 cell。
    /// </summary>
    [Fact]
    public void RequestDebrisSpawnsRequestedParticlesWithFiniteLifetime()
    {
        ParticleSystem particles = new(capacity: 8);
        DebrisEjectionRequest request = new(12, 18, 7, Count: 3, BaseSpeed: 2f, SpeedJitter: 1f, LifeTicks: 25);

        particles.RequestDebris(in request);

        Assert.Equal(3, particles.ActiveCount);
        Assert.Equal(3, particles.Stats.SpawnedThisTick);
        for (int i = 0; i < particles.ActiveCount; i++)
        {
            Particle particle = particles.ActiveReadOnly[i];
            Assert.Equal((ushort)7, particle.Material);
            Assert.Equal(12.5f, particle.X);
            Assert.Equal(18.5f, particle.Y);
            Assert.Equal(25, particle.Life);
            Assert.True(float.IsFinite(particle.Vx));
            Assert.True(float.IsFinite(particle.Vy));
        }
    }

    /// <summary>
    /// 验证碎屑请求遵守单 tick 抛射上限与固定池容量，超出部分只计 dropped。
    /// </summary>
    [Fact]
    public void RequestDebrisHonorsEjectionLimitAndCapacity()
    {
        ParticleSystem limited = new(
            capacity: 4,
            settings: new ParticleSystemSettings(4, 0.2f, 40, 0.05f, 1f, MaxEjectionPerTick: 2));
        DebrisEjectionRequest request = new(0, 0, 9, Count: 4, BaseSpeed: 1f, SpeedJitter: 0f, LifeTicks: 0);

        limited.RequestDebris(in request);

        Assert.Equal(2, limited.ActiveCount);
        Assert.Equal(2, limited.Stats.SpawnedThisTick);
        Assert.Equal(2, limited.Stats.DroppedThisTick);
        Assert.All(limited.ActiveReadOnly.ToArray(), particle => Assert.Equal(40, particle.Life));

        ParticleSystem full = new(capacity: 1);
        _ = full.TrySpawn(new ParticleSpawn(0, 0, 0, 0, 1, 0, 10));
        full.ResetTickStats();

        full.RequestDebris(in request);

        Assert.Equal(1, full.ActiveCount);
        Assert.Equal(0, full.Stats.SpawnedThisTick);
        Assert.Equal(4, full.Stats.DroppedThisTick);
    }

    private static (float X, float Y, float Vx, float Vy, ushort Material, byte ColorVariant, byte Life) Project(Particle particle)
    {
        return (particle.X, particle.Y, particle.Vx, particle.Vy, particle.Material, particle.ColorVariant, particle.Life);
    }
}
