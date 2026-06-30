using System.Runtime.CompilerServices;
using PixelEngine.Core;
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

    private static (float X, float Y, float Vx, float Vy, ushort Material, byte ColorVariant, byte Life) Project(Particle particle)
    {
        return (particle.X, particle.Y, particle.Vx, particle.Vy, particle.Material, particle.ColorVariant, particle.Life);
    }
}
