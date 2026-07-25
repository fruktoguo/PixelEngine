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
    [InlineData("spawn_endportal", "data/scripts/biomes/temple_wall_ending.lua", 1891f, 280f)]
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

    /// <summary>验证 ending Portal 接受稳定或不稳定 teleportatium，忽略空区与未驻留 cell。</summary>
    [Fact]
    public void EndingPortalActivationReadsReferenceLiquidArea()
    {
        MaterialId stable = new(7);
        MaterialId unstable = new(8);
        FakeCells cells = new();

        Assert.False(NoitaMarkerPortal.HasTeleportatium(cells, -2, 136, 2, 140, stable, unstable));

        cells.Set(0, 138, unstable);
        Assert.True(NoitaMarkerPortal.HasTeleportatium(cells, -2, 136, 2, 140, stable, unstable));
    }

    /// <summary>验证 ending Portal 应用来源 EntityLoad 的 y-4 偏移。</summary>
    [Fact]
    public void EndingPortalAppliesSourceEntityOffset()
    {
        NoitaMarkerPortal portal = new();

        portal.Bind(Anchor("spawn_endportal", "data/scripts/biomes/temple_wall_ending.lua"));

        Assert.Equal(60, portal.WorldY);
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

    private sealed class FakeCells : IWorldCellAccess
    {
        private readonly Dictionary<(int X, int Y), MaterialId> _cells = [];

        public bool IsResident(int x, int y)
        {
            return _cells.ContainsKey((x, y));
        }

        public MaterialId GetMaterial(int x, int y)
        {
            return _cells.GetValueOrDefault((x, y));
        }

        public CellView Sample(int x, int y)
        {
            return default;
        }

        public bool IsSolid(int x, int y)
        {
            return false;
        }

        public bool IsRigidOwned(int x, int y)
        {
            return false;
        }

        public void SetCell(int x, int y, MaterialId material)
        {
            Set(x, y, material);
        }

        public void Paint(int x, int y, int radius, MaterialId material)
        {
            Set(x, y, material);
        }

        public void Set(int x, int y, MaterialId material)
        {
            _cells[(x, y)] = material;
        }
    }
}
