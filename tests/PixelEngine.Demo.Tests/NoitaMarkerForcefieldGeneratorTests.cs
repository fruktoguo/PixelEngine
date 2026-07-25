using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>forcefield generator 权重、能量盾与本体破坏快速测试。</summary>
public sealed class NoitaMarkerForcefieldGeneratorTests
{
    /// <summary>验证 marker 创建专用 generator gameplay 类型。</summary>
    [Fact]
    public void ForcefieldMarkerCreatesDedicatedGameplayEntity()
    {
        Assert.True(NoitaWangMarkerVisualProfile.TryCreate(Anchor(), out NoitaWangMarkerVisualProfile profile));
        Assert.Equal(NoitaWangMarkerGameplayKind.ForcefieldGenerator, profile.GameplayKind);
        Assert.Equal(60f, profile.LightRadiusCells);
    }

    /// <summary>验证 1.0 空、0.5 实体权重稳定映射为 0 或单个装置。</summary>
    [Fact]
    public void BindingIsDeterministicAndPreservesSourceState()
    {
        NoitaMarkerForcefieldGenerator first = new();
        NoitaMarkerForcefieldGenerator second = new();
        first.Bind(Anchor(), 17UL);
        second.Bind(Anchor(), 17UL);

        Assert.Equal(first.IsPopulated, second.IsPopulated);
        Assert.Equal(30f, first.MaxHealth);
        Assert.Equal(3f, first.MaxShieldEnergy);
        Assert.Equal(2f, first.ShieldEnergy);
    }

    /// <summary>验证 Snowcastle 来源 safe 区域会抑制入口和 portal 邻域装置。</summary>
    [Theory]
    [InlineData(190, 5200)]
    [InlineData(0, 6200)]
    public void UnsafeSnowcastleRegionNeverPopulates(long x, long y)
    {
        NoitaMarkerForcefieldGenerator generator = new();
        generator.Bind(Anchor(x, y), ulong.MaxValue);

        Assert.False(generator.IsPopulated);
    }

    /// <summary>验证远距离法术先消耗 20px 能量盾，盾关闭后才命中 30 HP 本体。</summary>
    [Fact]
    public void ShieldInterceptsProjectileBeforeBody()
    {
        NoitaMarkerForcefieldGenerator generator = FindPopulatedGenerator();
        float bodyHealth = generator.Health;

        Assert.True(generator.TryHitSegment(-30f, -10f, 30f, -10f, 25f, out _, out _));
        Assert.Equal(bodyHealth, generator.Health);
        Assert.Equal(1f, generator.ShieldEnergy);
        Assert.True(generator.TryHitSegment(-30f, -10f, 30f, -10f, 25f, out _, out _));
        Assert.Equal(0f, generator.ShieldEnergy);
        Assert.False(generator.IsShieldActive);
        Assert.True(generator.TryHitSegment(-30f, -10f, 30f, -10f, 40f, out _, out _));
        Assert.True(generator.IsDead);
    }

    /// <summary>验证朝下 40 度缺口不会消耗盾能量，而会直接命中 generator 本体。</summary>
    [Fact]
    public void DownwardShieldGapExposesBody()
    {
        NoitaMarkerForcefieldGenerator generator = FindPopulatedGenerator();

        Assert.True(generator.TryHitSegment(8f, 30f, 8f, -10f, 10f, out _, out _));
        Assert.Equal(20f, generator.Health);
        Assert.Equal(2f, generator.ShieldEnergy);
    }

    private static NoitaMarkerForcefieldGenerator FindPopulatedGenerator()
    {
        for (ulong seed = 0; seed < 64; seed++)
        {
            NoitaMarkerForcefieldGenerator generator = new();
            generator.Bind(Anchor(), seed);
            if (generator.IsPopulated)
            {
                return generator;
            }
        }

        throw new InvalidOperationException("测试种子范围内没有生成 forcefield generator。");
    }

    private static NoitaWangMarkerAnchor Anchor(long x = 0, long y = 0)
    {
        return new NoitaWangMarkerAnchor(
            "snowcastle",
            "snowcastle",
            "ff123456",
            "spawn_forcefield_generator",
            "data/scripts/biomes/snowcastle.lua",
            NoitaWangTerrainCatalog.MarkerSemanticBase,
            x,
            y);
    }
}
