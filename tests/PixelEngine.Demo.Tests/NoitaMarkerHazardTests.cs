using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>危险 marker 的 profile 与激光碰撞快速测试。</summary>
public sealed class NoitaMarkerHazardTests
{
    /// <summary>验证七种已实现危险来源进入专属 C# 实体。</summary>
    [Theory]
    [InlineData("spawn_lasergun")]
    [InlineData("spawn_lasergate_ver")]
    [InlineData("spawn_laser_trap")]
    [InlineData("spawn_electricity_trap")]
    [InlineData("spawn_acid")]
    [InlineData("spawn_burning_barrel")]
    [InlineData("spawn_cloud_trap")]
    public void SupportedHazardMarkersCreateHazardGameplayProfile(string function)
    {
        NoitaWangMarkerAnchor anchor = Anchor(function);

        Assert.True(NoitaWangMarkerVisualProfile.TryCreate(anchor, out NoitaWangMarkerVisualProfile profile));
        Assert.Equal(NoitaWangMarkerGameplayKind.Hazard, profile.GameplayKind);
        Assert.Equal(string.Empty, profile.GameplayMaterialName);
    }

    /// <summary>验证水平与垂直激光只伤害光束厚度内的角色 AABB。</summary>
    [Fact]
    public void BeamIntersectionUsesCharacterAabbAndThickness()
    {
        CharacterState horizontalHit = State(20f, 8f, 6f, 10f);
        CharacterState verticalHit = State(8f, 20f, 10f, 6f);
        CharacterState miss = State(20f, 20f, 6f, 6f);

        Assert.True(NoitaMarkerHazard.IntersectsBeam(horizontalHit, 0f, 10f, 40f, 10f, 2.5f));
        Assert.True(NoitaMarkerHazard.IntersectsBeam(verticalHit, 10f, 0f, 10f, 40f, 2.5f));
        Assert.False(NoitaMarkerHazard.IntersectsBeam(miss, 0f, 10f, 40f, 10f, 2.5f));
    }

    private static NoitaWangMarkerAnchor Anchor(string function)
    {
        return new NoitaWangMarkerAnchor("vault", "vault", "#ffffffff", function, "test", 1, 32, 64);
    }

    private static CharacterState State(float x, float y, float width, float height)
    {
        return new CharacterState(
            x,
            y,
            width,
            height,
            false,
            false,
            false,
            false,
            0f,
            0f,
            0f,
            0f,
            0f,
            0f,
            0f,
            0f,
            0f);
    }
}
