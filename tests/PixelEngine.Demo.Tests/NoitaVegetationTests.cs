using System.Security.Cryptography;
using System.Text.Json;
using PixelEngine.Hosting;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Noita VegetationComponent 数据、权威材质与动态视觉层的针对性测试。
/// </summary>
public sealed class NoitaVegetationTests
{
    /// <summary>锁定完整 layer、成熟 sprite、材质与 provenance 清单。</summary>
    [Fact]
    public void CatalogPreservesAllVegetationLayersAssetsAndMaterials()
    {
        string contentRoot = ContentRoot();
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(contentRoot, "noita-vegetation.json")));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("17130612", root.GetProperty("referenceBuildId").GetString());
        Assert.Equal(317, root.GetProperty("layerCount").GetInt32());
        Assert.Equal(105, root.GetProperty("assetCount").GetInt32());
        Assert.Equal(12, root.GetProperty("materialNames").GetArrayLength());
        Assert.Equal(317, root.GetProperty("layers").GetArrayLength());

        JsonElement[] assets = [.. root.GetProperty("assets").EnumerateArray()];
        Assert.Equal(105, assets.Length);
        foreach (JsonElement asset in assets)
        {
            string relativePath = asset.GetProperty("contentPath").GetString()!;
            string path = Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), relativePath);
            Assert.Equal(
                asset.GetProperty("contentSha256").GetString(),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        }
    }

    /// <summary>所有压缩 alpha mask 必须解码到准确尺寸，且 12 种材质均可编译。</summary>
    [Fact]
    public void MasksAndRuntimeMaterialsCompileWithoutFallback()
    {
        IMaterialQuery materials = LoadMaterials();
        CompiledNoitaVegetationCatalog compiled = NoitaVegetationCatalog.Compile(materials);
        Assert.Equal(105, compiled.Assets.Length);
        foreach (DecodedNoitaVegetationAsset asset in compiled.Assets)
        {
            Assert.Equal(asset.Definition.Width * asset.Definition.Height, asset.Mask.Length);
            Assert.Contains(asset.Mask, static alpha => alpha != 0);
        }

        string[] expectedMaterials =
        [
            "cactus", "ceiling_plant_material", "fungus_loose", "grass", "grass_dark", "moss",
            "plant_material", "plant_material_red", "snow", "soil", "soil_dead", "wood_loose",
        ];
        Assert.All(expectedMaterials, name => Assert.True(materials.TryResolve(name, out _), name));
    }

    /// <summary>同 seed/chunk 结果必须逐 cell 相同，并在煤矿写入非视觉菌类材质。</summary>
    [Fact]
    public void AuthoritativeVegetationIsDeterministicAndWritesMaterialCells()
    {
        EngineScriptConfigApi configApi = new(ContentRoot());
        CampaignConfig config = CampaignConfig.Load(configApi);
        BiomeCatalog biomes = BiomeCatalog.Load(configApi, config);
        NoitaWangTerrainCatalog wang = NoitaWangTerrainCatalog.Load(configApi);
        IMaterialQuery materials = LoadMaterials();
        ushort fungus = materials.Resolve("fungus_loose").Value;
        int fungusCells = 0;

        for (int chunkY = 12; chunkY <= 15; chunkY++)
        {
            for (int chunkX = 0; chunkX <= 7; chunkX++)
            {
                ushort[] first = GenerateChunk(materials, config, biomes, wang, chunkX, chunkY);
                ushort[] second = GenerateChunk(materials, config, biomes, wang, chunkX, chunkY);
                Assert.True(first.AsSpan().SequenceEqual(second));
                fungusCells += first.Count(material => material == fungus);
            }
        }

        Assert.True(fungusCells > 0, "煤矿权威 chunk 必须包含 VegetationComponent 生成的 fungus_loose cells。");
    }

    /// <summary>煤矿视口必须返回真实 vegetation PNG，而不是程序色块占位。</summary>
    [Fact]
    public void ViewportCollectsDeterministicVegetationSprites()
    {
        EngineScriptConfigApi configApi = new(ContentRoot());
        CampaignConfig config = CampaignConfig.Load(configApi);
        IMaterialQuery materials = LoadMaterials();
        PlayableCavernWorldGenerator generator = new();
        ProceduralWorldBuildRequest request = new(
            PlayableCavernWorldGenerator.Key,
            materials,
            Config: configApi,
            WorldSeedOverride: config.InitialRunSeed);
        _ = generator.Describe(in request);

        WorldVisualLayerDescriptor[] first = new WorldVisualLayerDescriptor[256];
        WorldVisualLayerDescriptor[] second = new WorldVisualLayerDescriptor[256];
        int firstCount = generator.CollectWorldVisualLayers(0, 768, 512, 1024, first);
        int secondCount = generator.CollectWorldVisualLayers(0, 768, 512, 1024, second);
        Assert.Equal(firstCount, secondCount);
        Assert.True(first.AsSpan(0, firstCount).SequenceEqual(second.AsSpan(0, secondCount)));
        Assert.Contains(
            first.AsSpan(0, firstCount).ToArray(),
            static layer => layer.Asset.LogicalPath.StartsWith("maps/noita/vegetation/", StringComparison.Ordinal));
    }

    private static ushort[] GenerateChunk(
        IMaterialQuery materials,
        CampaignConfig config,
        BiomeCatalog biomes,
        NoitaWangTerrainCatalog wang,
        int chunkX,
        int chunkY)
    {
        ushort[] cells = new ushort[64 * 64];
        Half[] temperatures = new Half[16 * 16];
        PlayableCavernWorldGenerator.PopulateChunkForVerification(
            materials,
            chunkX,
            chunkY,
            cells,
            temperatures,
            config.InitialRunSeed,
            config,
            biomes,
            wang);
        return cells;
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
