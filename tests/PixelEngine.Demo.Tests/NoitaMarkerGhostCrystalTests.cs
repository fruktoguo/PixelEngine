using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>ghost crystal 来源权重、战斗与死亡语义快速测试。</summary>
public sealed class NoitaMarkerGhostCrystalTests
{
    /// <summary>验证 marker 创建专用 gameplay 类型并保留来源光照半径。</summary>
    [Fact]
    public void GhostCrystalMarkerCreatesDedicatedGameplayEntity()
    {
        Assert.True(NoitaWangMarkerVisualProfile.TryCreate(Anchor(), out NoitaWangMarkerVisualProfile profile));
        Assert.Equal(NoitaWangMarkerGameplayKind.GhostCrystal, profile.GameplayKind);
        Assert.Equal(96f, profile.LightRadiusCells);
    }

    /// <summary>验证同一世界种子稳定复现空/实体组选择与 2~4 个幽灵范围。</summary>
    [Fact]
    public void BindingIsDeterministicAndPreservesSourcePopulationRange()
    {
        NoitaMarkerGhostCrystal first = new();
        NoitaMarkerGhostCrystal second = new();

        first.Bind(Anchor(), 0x1234_5678UL);
        second.Bind(Anchor(), 0x1234_5678UL);

        Assert.Equal(first.IsPopulated, second.IsPopulated);
        Assert.Equal(first.GhostCount, second.GhostCount);
        Assert.True(first.GhostCount is 0 or (>= 2 and <= 4));
        Assert.Equal(20f, first.MaxHealth);
    }

    /// <summary>验证法术线段命中水晶 hitbox、扣除 20 HP 并进入死亡状态。</summary>
    [Fact]
    public void ProjectileSegmentDamagesCrystalAndQueuesDeathRing()
    {
        NoitaMarkerGhostCrystal crystal = FindPopulatedCrystal();

        Assert.False(crystal.TryHitSegment(0f, 30f, 20f, 30f, 5f, out _, out _));
        Assert.True(crystal.TryHitSegment(0f, -10f, 20f, -10f, 8f, out float hitX, out float hitY));
        Assert.Equal(9f, hitX);
        Assert.Equal(-10f, hitY);
        Assert.Equal(12f, crystal.Health);
        Assert.True(crystal.TryHitSegment(0f, -10f, 20f, -10f, 20f, out _, out _));
        Assert.True(crystal.IsDead);
        Assert.Equal(0f, crystal.Health);
    }

    private static NoitaMarkerGhostCrystal FindPopulatedCrystal()
    {
        for (ulong seed = 0; seed < 32; seed++)
        {
            NoitaMarkerGhostCrystal crystal = new();
            crystal.Bind(Anchor(), seed);
            if (crystal.IsPopulated)
            {
                return crystal;
            }
        }

        throw new InvalidOperationException("测试种子范围内没有生成 populated ghost crystal。");
    }

    private static NoitaWangMarkerAnchor Anchor()
    {
        return new NoitaWangMarkerAnchor(
            "crypt",
            "crypt",
            "ffc8001a",
            "spawn_ghost_crystal",
            "data/scripts/biomes/crypt.lua",
            NoitaWangTerrainCatalog.MarkerSemanticBase,
            10,
            0);
    }
}
