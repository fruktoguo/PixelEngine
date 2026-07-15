using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 子系统调参面板测试。
/// 不变式：子系统调参写入可回滚、非法值被拒绝。
/// </summary>
public sealed class TuningPanelTests
{
    /// <summary>
    /// 验证物理和粒子调参面板会把状态应用到服务层。
    /// </summary>
    [Fact]
    public void PhysicsAndParticlePanelsApplyThroughServices()
    {
        RecordingPhysicsTuning physics = new();
        RecordingParticleTuning particles = new();
        PhysicsTuningPanel physicsPanel = new(physics);
        ParticleTuningPanel particlePanel = new(particles);

        physicsPanel.ApplyNow(physics.State with { SubStepCount = 8 });
        particlePanel.ApplyNow(particles.State with { MaxCount = 128 });

        Assert.Equal(8, physics.Applied.SubStepCount);
        Assert.Equal(128, particles.Applied.MaxCount);
    }

    /// <summary>
    /// 验证光照调参服务会直接修改 RenderPipelineSettings。
    /// </summary>
    [Fact]
    public void LightingTuningServiceAppliesRenderPipelineSettings()
    {
        RenderPipelineSettings settings = new();
        RenderPipelineLightingTuningService service = new(settings);
        LightingTuningPanel panel = new(service);

        panel.ApplyNow(new LightingTuningState(
            LightingQualityLevel.Full,
            BloomEnabled: false,
            BloomThreshold: 0.5f,
            BloomIntensity: 1.2f,
            FogOfWarEnabled: false,
            DitherEnabled: false,
            Gamma: 2.0f,
            RadianceCascadesEnabled: true));

        Assert.False(settings.EnableDither);
        Assert.Equal(2.0f, settings.Gamma);
        Assert.Equal(0f, settings.Bloom.Intensity);
        Assert.Equal(0.5f, settings.Bloom.Threshold);
        Assert.False(settings.EnableFogOfWar);
        Assert.True(settings.RadianceCascades.Enabled);
    }

    /// <summary>
    /// 验证粒子调参服务会直接修改 ParticleSystem 的运行时设置。
    /// </summary>
    [Fact]
    public void ParticleTuningServiceAppliesParticleSystemSettings()
    {
        ParticleSystem particles = new(capacity: 16);
        ParticleSystemTuningService service = new(particles);
        ParticleTuningPanel panel = new(service);

        panel.ApplyNow(new ParticleTuningState(
            MaxCount: 8,
            GravityPerTick: 0.4f,
            MaxLifetimeTicks: 64,
            DepositSpeedEpsilon: 0.2f,
            EjectionImpulseScale: 1.5f,
            MaxEjectionPerTick: 3,
            particles.Stats));

        Assert.Equal(8, particles.Settings.MaxActiveCount);
        Assert.Equal(0.4f, particles.Settings.GravityPerTick);
        Assert.Equal(64, particles.Settings.MaxLifetimeTicks);
        Assert.Equal(0.2f, particles.Settings.DepositSpeedEpsilon);
        Assert.Equal(1.5f, particles.Settings.EjectionImpulseScale);
        Assert.Equal(3, particles.Settings.MaxEjectionPerTick);
    }

    private sealed class RecordingPhysicsTuning : IPhysicsTuningService
    {
        public PhysicsTuningState State { get; } = new(
            32,
            4,
            1,
            4,
            0,
            0,
            9.8f,
            new PhysicsSystemStats(0, 0, 0, 0, default, 1, 0));

        public PhysicsTuningState Applied { get; private set; } = null!;

        public PhysicsTuningState Capture()
        {
            return Applied ?? State;
        }

        public void Apply(PhysicsTuningState state)
        {
            Applied = state;
        }
    }

    private sealed class RecordingParticleTuning : IParticleTuningService
    {
        public ParticleTuningState State { get; } = new(
            64,
            0.1f,
            120,
            0.05f,
            1f,
            8,
            new ParticleSystemStats(0, 64, 0, 0, 0, 0, 0, 0));

        public ParticleTuningState Applied { get; private set; } = null!;

        public ParticleTuningState Capture()
        {
            return Applied ?? State;
        }

        public void Apply(ParticleTuningState state)
        {
            Applied = state;
        }
    }
}
