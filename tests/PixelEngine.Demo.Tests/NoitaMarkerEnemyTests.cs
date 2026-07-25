using System.Security.Cryptography;
using System.Text.Json;
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

    /// <summary>菌类 marker 必须进入专属敌人运行时，而不是被 Vegetation 总开关抑制。</summary>
    [Fact]
    public void FungusMarkerCreatesDedicatedEnemyProfile()
    {
        NoitaWangMarkerAnchor anchor = Anchor("spawn_fungi");

        Assert.True(NoitaWangMarkerVisualProfile.TryCreate(anchor, out NoitaWangMarkerVisualProfile profile));
        Assert.Equal(NoitaWangMarkerGameplayKind.Enemy, profile.GameplayKind);
    }

    /// <summary>普通煤矿保留空/小/大三项权重，alt 表不产生大菌。</summary>
    [Fact]
    public void FungusMarkerPreservesSourceWeightedOutcomes()
    {
        int empty = 0;
        int small = 0;
        int large = 0;
        int alternateLarge = 0;
        for (ulong seed = 0; seed < 4096; seed++)
        {
            NoitaMarkerEnemy enemy = new();
            enemy.Bind(Anchor("spawn_fungi"), seed);
            if (!enemy.IsPopulated)
            {
                empty++;
            }
            else if (enemy.IsLargeFungus)
            {
                large++;
                Assert.Equal(190f, enemy.MaxHealth);
            }
            else
            {
                small++;
                Assert.Equal(65f, enemy.MaxHealth);
            }

            NoitaMarkerEnemy alternate = new();
            alternate.Bind(Anchor("spawn_fungi", "coalmine_alt"), seed);
            alternateLarge += alternate.IsLargeFungus ? 1 : 0;
        }

        Assert.InRange(empty, 1800, 2100);
        Assert.InRange(small, 1800, 2100);
        Assert.InRange(large, 120, 260);
        Assert.Equal(0, alternateLarge);
    }

    /// <summary>生成的 20 张 stand 帧必须与 provenance 逐文件一致。</summary>
    [Fact]
    public void FungusSpriteFramesMatchProvenance()
    {
        string contentRoot = Path.Combine(AppContext.BaseDirectory, "content");
        string provenancePath = Path.Combine(
            contentRoot,
            "sprites",
            "noita",
            "marker-enemies",
            "provenance.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(provenancePath));
        JsonElement[] assets = [.. document.RootElement.GetProperty("assets").EnumerateArray()];
        Assert.Equal(2, assets.Length);
        int frameCount = 0;
        foreach (JsonElement asset in assets)
        {
            foreach (JsonElement frame in asset.GetProperty("frames").EnumerateArray())
            {
                string relativePath = frame.GetProperty("contentPath").GetString()!;
                string path = Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.Equal(
                    frame.GetProperty("contentSha256").GetString(),
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
                frameCount++;
            }
        }

        Assert.Equal(20, frameCount);
    }

    private static NoitaWangMarkerAnchor Anchor(string function, string referenceBiomeId = "coalmine")
    {
        return new NoitaWangMarkerAnchor(
            referenceBiomeId,
            "coalmine",
            "ff70a8ff",
            function,
            "lua",
            NoitaWangTerrainCatalog.MarkerSemanticBase,
            10,
            0);
    }
}
