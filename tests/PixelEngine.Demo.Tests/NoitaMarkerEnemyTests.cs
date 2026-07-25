using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>Encounter marker 的敌人原型、命中与生命状态快速测试。</summary>
public sealed class NoitaMarkerEnemyTests
{
    /// <summary>验证 encounter profile 不再退化成火花占位。</summary>
    [Fact]
    public void EncounterMarkerCreatesEnemyGameplayEntity()
    {
        NoitaWangMarkerAnchor anchor = Anchor("spawn_scavengers");

        Assert.True(NoitaWangMarkerVisualProfile.TryCreate(anchor, out NoitaWangMarkerVisualProfile profile));
        Assert.Equal(NoitaWangMarkerGameplayKind.Enemy, profile.GameplayKind);
        Assert.Equal(string.Empty, profile.GameplayMaterialName);
    }

    /// <summary>验证来源函数映射到不同敌人压力语义。</summary>
    [Theory]
    [InlineData("spawn_large_enemies", "large", 120f)]
    [InlineData("spawn_scavengers", "robot", 90f)]
    [InlineData("spawn_crawlers", "swarm", 28f)]
    [InlineData("spawn_fish", "aquatic", 36f)]
    [InlineData("spawn_killer", "standard", 55f)]
    public void EnemyArchetypePreservesEncounterRole(string function, string archetype, float health)
    {
        NoitaMarkerEnemy enemy = new();
        NoitaWangMarkerAnchor anchor = Anchor(function);

        enemy.Bind(anchor);

        Assert.Equal(archetype, enemy.Archetype);
        Assert.Equal(health, enemy.MaxHealth);
        Assert.Equal(health, enemy.Health);
        Assert.False(enemy.IsDead);
    }

    /// <summary>验证法术飞行线段只命中半径内敌人并可真实击杀。</summary>
    [Fact]
    public void ProjectileSegmentDamagesAndKillsEnemy()
    {
        NoitaMarkerEnemy enemy = new();
        enemy.Bind(Anchor("spawn_crawlers"));

        Assert.False(enemy.TryHitSegment(0f, 20f, 20f, 20f, 10f, out _, out _));
        Assert.True(enemy.TryHitSegment(0f, 0f, 20f, 0f, 10f, out float hitX, out float hitY));
        Assert.Equal(10f, hitX);
        Assert.Equal(0f, hitY);
        Assert.Equal(18f, enemy.Health);
        Assert.True(enemy.TryHitSegment(0f, 0f, 20f, 0f, 20f, out _, out _));
        Assert.True(enemy.IsDead);
        Assert.Equal(0f, enemy.Health);
        Assert.False(enemy.TryHitSegment(0f, 0f, 20f, 0f, 10f, out _, out _));
    }

    private static NoitaWangMarkerAnchor Anchor(string function)
    {
        return new NoitaWangMarkerAnchor(
            "coalmine",
            "coalmine",
            "ff70a8ff",
            function,
            "lua",
            NoitaWangTerrainCatalog.MarkerSemanticBase,
            10,
            0);
    }
}
