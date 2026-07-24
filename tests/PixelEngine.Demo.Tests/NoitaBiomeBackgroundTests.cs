using System.Security.Cryptography;
using System.Text.Json;
using PixelEngine.Hosting;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>Noita biome weather background 与 edge 组合测试。</summary>
public sealed class NoitaBiomeBackgroundTests
{
    /// <summary>锁定 129 个 reference biome、111 个去重资产及逐文件 provenance。</summary>
    [Fact]
    public void CatalogPreservesEveryReferenceBiomeAndBackgroundAsset()
    {
        string contentRoot = ContentRoot();
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(contentRoot, "noita-biome-backgrounds.json")));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("17130612", root.GetProperty("referenceBuildId").GetString());
        Assert.Equal("9dbd52ced019a643169a2db02f46c77f8766c6e5", root.GetProperty("referenceVersionHash").GetString());
        Assert.Equal(129, root.GetProperty("biomeCount").GetInt32());
        Assert.Equal(111, root.GetProperty("assetCount").GetInt32());
        Assert.Equal(129, root.GetProperty("biomes").GetArrayLength());
        Assert.Equal(129, NoitaBiomeBackgroundCatalog.Biomes.Length);
        Assert.Equal(111, NoitaBiomeBackgroundCatalog.Assets.Length);

        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string relativePath = asset.GetProperty("contentPath").GetString()!;
            string path = Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), relativePath);
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            Assert.Equal(asset.GetProperty("contentSha256").GetString(), hash);
            Assert.Equal(asset.GetProperty("sourceSha256").GetString(), hash);
        }
    }

    /// <summary>煤矿视口必须组合真实 coalmine weather background，且查询结果确定。</summary>
    [Fact]
    public void CoalMineViewportCollectsDeterministicWeatherBackgroundTiles()
    {
        EngineScriptConfigApi configApi = new(ContentRoot());
        CampaignConfig config = CampaignConfig.Load(configApi);
        PlayableWorldDirector generator = new();
        IViewportWorldVisualLayerProvider viewportProvider =
            Assert.IsAssignableFrom<IViewportWorldVisualLayerProvider>(generator);
        ProceduralWorldBuildRequest request = new(
            PlayableCavernWorldGenerator.Key,
            LoadMaterials(),
            Config: configApi,
            WorldSeedOverride: config.InitialRunSeed);
        ProceduralWorldDescriptor descriptor = generator.Describe(in request);
        Assert.Equal(ProceduralWorldExtent.Infinite, descriptor.Extent);

        WorldVisualLayerDescriptor[] first = new WorldVisualLayerDescriptor[256];
        WorldVisualLayerDescriptor[] second = new WorldVisualLayerDescriptor[256];
        int firstCount = viewportProvider.CollectWorldVisualLayers(0, 768, 512, 1024, first);
        int secondCount = viewportProvider.CollectWorldVisualLayers(0, 768, 512, 1024, second);
        Assert.Equal(firstCount, secondCount);
        Assert.True(first.AsSpan(0, firstCount).SequenceEqual(second.AsSpan(0, secondCount)));
        Assert.Contains(
            first.AsSpan(0, firstCount).ToArray(),
            static layer => layer.Layer == WorldVisualLayerKind.Background &&
                layer.Asset.LogicalPath.EndsWith("background_coalmine.png", StringComparison.Ordinal));
    }

    /// <summary>动态背景是逐帧视口查询，完成延迟初始化后必须保持零托管分配。</summary>
    [Fact]
    public void ViewportBackgroundCollectionIsAllocationFreeAfterWarmup()
    {
        EngineScriptConfigApi configApi = new(ContentRoot());
        CampaignConfig config = CampaignConfig.Load(configApi);
        PlayableWorldDirector generator = new();
        ProceduralWorldBuildRequest request = new(
            PlayableCavernWorldGenerator.Key,
            LoadMaterials(),
            Config: configApi,
            WorldSeedOverride: config.InitialRunSeed);
        ProceduralWorldDescriptor descriptor = generator.Describe(in request);
        Assert.Equal(ProceduralWorldExtent.Infinite, descriptor.Extent);

        WorldVisualLayerDescriptor[] layers = new WorldVisualLayerDescriptor[256];
        int totalCount = generator.CollectWorldVisualLayers(0, 768, 512, 1024, layers);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            totalCount += generator.CollectWorldVisualLayers(0, 768, 512, 1024, layers);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(totalCount > 0);
    }

    private static IMaterialQuery LoadMaterials()
    {
        return EngineContentLoader.LoadMaterialPackage(ContentRoot()).Materials;
    }

    private static string ContentRoot()
    {
        return Path.Combine(AppContext.BaseDirectory, "content");
    }
}
