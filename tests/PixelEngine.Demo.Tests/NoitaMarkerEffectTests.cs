using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>Effect marker 的真实实现白名单与 fail-closed 快速测试。</summary>
public sealed class NoitaMarkerEffectTests
{
    /// <summary>验证 waterspout 创建真实 water particle 装置。</summary>
    [Fact]
    public void WaterSpoutCreatesDrippingLiquidGameplayEntity()
    {
        NoitaWangMarkerAnchor anchor = Anchor("spawn_waterspout");

        Assert.True(NoitaWangMarkerVisualProfile.TryCreate(anchor, out NoitaWangMarkerVisualProfile profile));
        Assert.Equal(NoitaWangMarkerGameplayKind.DrippingLiquid, profile.GameplayKind);
        Assert.Equal("water", profile.GameplayMaterialName);
    }

    /// <summary>验证尚未实现的 Effect 不再退化成统一 fire SparkEmitter。</summary>
    [Theory]
    [InlineData("spawn_worm_deflector")]
    [InlineData("spawn_endcrystal")]
    [InlineData("spawn_essence")]
    [InlineData("spawn_spell_visualizer")]
    public void UnsupportedEffectsFailClosed(string function)
    {
        Assert.False(NoitaWangMarkerVisualProfile.TryCreate(Anchor(function), out _));
    }

    /// <summary>验证同一坐标产生稳定滴落序列初态。</summary>
    [Fact]
    public void DrippingLiquidBindingIsDeterministic()
    {
        NoitaWangMarkerAnchor anchor = Anchor("spawn_waterspout");
        NoitaMarkerDrippingLiquid first = new();
        NoitaMarkerDrippingLiquid second = new();

        first.Bind(anchor);
        second.Bind(anchor);

        Assert.Equal(first.WorldX, second.WorldX);
        Assert.Equal(first.WorldY, second.WorldY);
        Assert.Equal(0, first.RealParticleCount);
        Assert.Equal(0, second.RealParticleCount);
    }

    private static NoitaWangMarkerAnchor Anchor(string function)
    {
        return new NoitaWangMarkerAnchor(
            "mountain_hall",
            "mountain_hall",
            "#ffffffff",
            function,
            "data/scripts/biomes/mountain/mountain_hall.lua",
            1,
            32,
            64);
    }
}
