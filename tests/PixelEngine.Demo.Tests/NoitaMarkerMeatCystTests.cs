using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>Meat biome cyst 的来源权重、hitbox 与死亡状态快速测试。</summary>
public sealed class NoitaMarkerMeatCystTests
{
    /// <summary>验证 spawn_cyst 创建专用 gameplay 类型并保留 100px 光照。</summary>
    [Fact]
    public void CystMarkerCreatesDedicatedGameplayEntity()
    {
        Assert.True(NoitaWangMarkerVisualProfile.TryCreate(Anchor(), out NoitaWangMarkerVisualProfile profile));
        Assert.Equal(NoitaWangMarkerGameplayKind.MeatCyst, profile.GameplayKind);
        Assert.Equal(100f, profile.LightRadiusCells);
    }

    /// <summary>验证同 seed 稳定复现 30% 空权重、随机朝向和 `(x+5,y+5)` 偏移。</summary>
    [Fact]
    public void BindingIsDeterministicAndPreservesOffset()
    {
        NoitaMarkerMeatCyst first = new();
        NoitaMarkerMeatCyst second = new();
        first.Bind(Anchor(), 42UL);
        second.Bind(Anchor(), 42UL);

        Assert.Equal(first.IsPopulated, second.IsPopulated);
        Assert.Equal(first.Rotation, second.Rotation);
        Assert.Equal(15f, first.X);
        Assert.Equal(25f, first.Y);
        Assert.Equal(1f, first.MaxHealth);
    }

    /// <summary>验证 12x14 hitbox 外不受伤、命中后按 1 HP 来源死亡。</summary>
    [Fact]
    public void ProjectileHitKillsOneHpCyst()
    {
        NoitaMarkerMeatCyst cyst = FindPopulatedCyst();

        Assert.False(cyst.TryHitSegment(-20f, 20f, 40f, 20f, 1f, out _, out _));
        Assert.True(cyst.TryHitSegment(-20f, -3f, 40f, -3f, 1f, out _, out _));
        Assert.True(cyst.IsDead);
        Assert.Equal(0f, cyst.Health);
    }

    private static NoitaMarkerMeatCyst FindPopulatedCyst()
    {
        NoitaWangMarkerAnchor anchor = new(
            "meat",
            "meat",
            "ff123456",
            "spawn_cyst",
            "data/scripts/biomes/meat.lua",
            NoitaWangTerrainCatalog.MarkerSemanticBase,
            -5,
            -5);
        for (ulong seed = 0; seed < 32; seed++)
        {
            NoitaMarkerMeatCyst cyst = new();
            cyst.Bind(anchor, seed);
            if (cyst.IsPopulated)
            {
                return cyst;
            }
        }

        throw new InvalidOperationException("测试种子范围内没有生成 meat cyst。");
    }

    private static NoitaWangMarkerAnchor Anchor()
    {
        return new NoitaWangMarkerAnchor(
            "meat",
            "meat",
            "ff123456",
            "spawn_cyst",
            "data/scripts/biomes/meat.lua",
            NoitaWangTerrainCatalog.MarkerSemanticBase,
            10,
            20);
    }
}
