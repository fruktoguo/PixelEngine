using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>静态目标 Portal marker 的来源映射与触发范围快速测试。</summary>
public sealed class NoitaMarkerPortalTests
{
    /// <summary>验证来源 XML 的五组静态绝对目标。</summary>
    [Theory]
    [InlineData("spawn_teleport_back", "data/scripts/biomes/lake.lua", -12557f, 190f)]
    [InlineData("spawn_buried_eye_teleporter", "data/scripts/biomes/snowcave.lua", 3895f, 4510f)]
    [InlineData("spawn_teleporter", "data/scripts/biomes/excavationsite_cube_chamber.lua", 190f, 1525f)]
    [InlineData("spawn_teleporter", "data/scripts/biomes/snowcastle_hourglass_chamber.lua", 190f, 5231f)]
    [InlineData("spawn_teleporter", "data/scripts/biomes/snowcave_secret_chamber.lua", 190f, 3080f)]
    public void StaticPortalDestinationsMatchReferenceXml(
        string function,
        string origin,
        float expectedX,
        float expectedY)
    {
        Assert.True(NoitaMarkerPortal.TryResolveDestination(function, origin, out float x, out float y));
        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);

        NoitaWangMarkerAnchor anchor = Anchor(function, origin);
        Assert.True(NoitaWangMarkerVisualProfile.TryCreate(anchor, out NoitaWangMarkerVisualProfile profile));
        Assert.Equal(NoitaWangMarkerGameplayKind.Portal, profile.GameplayKind);
    }

    /// <summary>验证动态目标 Portal 不会退化成通用触发火花。</summary>
    [Fact]
    public void DynamicRobotEggPortalFailsClosedUntilNetworkStateExists()
    {
        NoitaWangMarkerAnchor anchor = Anchor("spawn_teleport", "data/scripts/biomes/robot_egg.lua");

        Assert.False(NoitaWangMarkerVisualProfile.TryCreate(anchor, out _));
    }

    /// <summary>验证 30x30 来源 hitbox 与角色 AABB 的边界交叠。</summary>
    [Fact]
    public void PortalTriggerUsesReferenceHitboxExtent()
    {
        CharacterState hit = State(12f, 12f, 6f, 10f);
        CharacterState miss = State(16f, 16f, 6f, 10f);

        Assert.True(NoitaMarkerPortal.IntersectsTrigger(hit, 0f, 0f, 15f));
        Assert.False(NoitaMarkerPortal.IntersectsTrigger(miss, 0f, 0f, 15f));
    }

    private static NoitaWangMarkerAnchor Anchor(string function, string origin)
    {
        return new NoitaWangMarkerAnchor("lake", "lake", "#ffffffff", function, origin, 1, 32, 64);
    }

    private static CharacterState State(float x, float y, float width, float height)
    {
        return new CharacterState(
            x, y, width, height,
            false, false, false, false,
            0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
    }
}
