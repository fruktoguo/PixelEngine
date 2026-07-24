using System.Security.Cryptography;
using System.Text.Json;
using PixelEngine.Hosting;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Snowcastle Wang marker 随机 Pixel Scene 的纯 C# 生成与运行时组合测试。
/// </summary>
public sealed class NoitaSnowcastlePixelSceneTests
{
    /// <summary>
    /// 锁定两个来源 Lua、12 个场景、25 种材质和全部 visual/background 资产 provenance。
    /// </summary>
    [Fact]
    public void CatalogPreservesSnowcastleSceneTablesAndAssets()
    {
        string contentRoot = ContentRoot();
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(contentRoot, "noita-snowcastle-pixel-scenes.json")));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("17130612", root.GetProperty("referenceBuildId").GetString());
        Assert.Equal(25, root.GetProperty("materialNames").GetArrayLength());
        JsonElement[] scenes = [.. root.GetProperty("scenes").EnumerateArray()];
        Assert.Equal(12, scenes.Length);
        Assert.Equal(4, scenes.Count(static scene => scene.GetProperty("markerFunction").GetString() == "load_pixel_scene"));
        Assert.Equal(8, scenes.Count(static scene => scene.GetProperty("markerFunction").GetString() == "load_pixel_scene2"));
        Assert.All(scenes, static scene =>
        {
            Assert.True(scene.GetProperty("width").GetInt32() is 130 or 260);
            Assert.True(scene.GetProperty("height").GetInt32() is 130 or 260);
            Assert.Equal(64, scene.GetProperty("materialSha256").GetString()!.Length);
            Assert.Equal(64, scene.GetProperty("decodedSha256").GetString()!.Length);
        });

        JsonElement[] assets = [.. root.GetProperty("assets").EnumerateArray()];
        Assert.Equal(10, assets.Length);
        foreach (JsonElement asset in assets)
        {
            string relativePath = asset.GetProperty("contentPath").GetString()!;
            string path = Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), relativePath);
            Assert.Equal(
                asset.GetProperty("contentSha256").GetString(),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            Assert.Equal(asset.GetProperty("sourceSha256").GetString(), asset.GetProperty("contentSha256").GetString());
        }
    }

    /// <summary>
    /// 同一 marker/seed 必须稳定选择相同场景，两个来源表只能选择各自成员。
    /// </summary>
    [Fact]
    public void SelectionIsDeterministicAndKeepsSourceGroups()
    {
        int first = NoitaSnowcastlePixelSceneCatalog.SelectSceneIndex("load_pixel_scene", 1500, 5900, 1234);
        Assert.Equal(first, NoitaSnowcastlePixelSceneCatalog.SelectSceneIndex("load_pixel_scene", 1500, 5900, 1234));
        Assert.Equal("load_pixel_scene", NoitaSnowcastlePixelSceneCatalog.DecodedScenes[first].Definition.MarkerFunction);

        int second = NoitaSnowcastlePixelSceneCatalog.SelectSceneIndex("load_pixel_scene2", 1500, 5900, 1234);
        Assert.Equal(second, NoitaSnowcastlePixelSceneCatalog.SelectSceneIndex("load_pixel_scene2", 1500, 5900, 1234));
        Assert.Equal("load_pixel_scene2", NoitaSnowcastlePixelSceneCatalog.DecodedScenes[second].Definition.MarkerFunction);
        Assert.NotEqual(-1, first);
        Assert.NotEqual(-1, second);
    }

    /// <summary>
    /// 视口查询必须把 Snowcastle marker 实例化为真实 background/visual，且不再生成紫色 SceneLoad 占位。
    /// </summary>
    [Fact]
    public void ViewportCollectsRealSceneLayersAndSuppressesPlaceholder()
    {
        EngineScriptConfigApi configApi = new(ContentRoot());
        CampaignConfig config = CampaignConfig.Load(configApi);
        BiomeCatalog biomes = BiomeCatalog.Load(configApi, config);
        NoitaWangTerrainCatalog wang = NoitaWangTerrainCatalog.Load(configApi);
        IMaterialQuery materials = LoadMaterials();
        PlayableCavernWorldGenerator generator = new();
        ProceduralWorldBuildRequest request = new(
            PlayableCavernWorldGenerator.Key,
            materials,
            Config: configApi,
            WorldSeedOverride: config.InitialRunSeed);
        _ = generator.Describe(in request);

        NoitaWangMarkerAnchor anchor = FindSnowcastleSceneAnchor(biomes, wang, config);
        Assert.False(NoitaWangMarkerVisualProfile.TryCreate(anchor, out _));

        WorldVisualLayerDescriptor[] layers = new WorldVisualLayerDescriptor[96];
        int layerCount = generator.CollectWorldVisualLayers(
            anchor.WorldX - 320,
            anchor.WorldY - 240,
            anchor.WorldX + 640,
            anchor.WorldY + 480,
            layers);
        Assert.InRange(layerCount, 1, layers.Length);
        Assert.Contains(
            layers.AsSpan(0, layerCount).ToArray(),
            static layer => layer.Asset.LogicalPath.Contains("snowcastle-pixel-scenes", StringComparison.Ordinal));
    }

    /// <summary>
    /// 编译后的 25 种场景材质必须全部解析为运行时稳定 id，掩码尺寸与索引范围有效。
    /// </summary>
    [Fact]
    public void MaterialMasksCompileToRuntimeMaterialIds()
    {
        IMaterialQuery materials = LoadMaterials();
        CompiledNoitaSnowcastlePixelSceneCatalog compiled = NoitaSnowcastlePixelSceneCatalog.Compile(materials);
        Assert.Equal(26, compiled.MaterialIds.Length);
        Assert.Equal(12, compiled.Scenes.Length);
        ReadOnlySpan<string> names = NoitaSnowcastlePixelSceneCatalog.MaterialNames;
        for (int i = 0; i < names.Length; i++)
        {
            Assert.Equal(materials.Resolve(names[i]).Value, compiled.MaterialIds[i + 1]);
        }
        foreach (DecodedNoitaSnowcastlePixelScene scene in compiled.Scenes)
        {
            Assert.Equal(scene.Definition.Width * scene.Definition.Height, scene.MaterialIndices.Length);
            Assert.All(scene.MaterialIndices, index => Assert.InRange(index, (byte)0, (byte)25));
            Assert.Contains(scene.MaterialIndices, static index => index > 0);
        }
    }

    /// <summary>
    /// marker 选中的场景材质必须确定性写入权威 64x64 chunk，而不只是作为 visual overlay 存在。
    /// </summary>
    [Fact]
    public void SelectedSceneWritesDeterministicMaterialsIntoAuthoritativeChunk()
    {
        EngineScriptConfigApi configApi = new(ContentRoot());
        CampaignConfig config = CampaignConfig.Load(configApi);
        BiomeCatalog biomes = BiomeCatalog.Load(configApi, config);
        NoitaWangTerrainCatalog wang = NoitaWangTerrainCatalog.Load(configApi);
        IMaterialQuery materials = LoadMaterials();
        NoitaWangMarkerAnchor anchor = FindSnowcastleSceneAnchor(biomes, wang, config);
        int sceneIndex = NoitaSnowcastlePixelSceneCatalog.SelectSceneIndex(
            NoitaSnowcastlePixelSceneCatalog.ResolveMarkerFunction(in anchor)!,
            anchor.WorldX,
            anchor.WorldY,
            config.InitialRunSeed);
        DecodedNoitaSnowcastlePixelScene scene = NoitaSnowcastlePixelSceneCatalog.DecodedScenes[sceneIndex];
        CompiledNoitaSnowcastlePixelSceneCatalog compiled = NoitaSnowcastlePixelSceneCatalog.Compile(materials);
        Dictionary<(int X, int Y), ushort[]> generatedChunks = [];
        bool matched = false;

        for (int index = 0; index < scene.MaterialIndices.Length && !matched; index++)
        {
            byte materialIndex = scene.MaterialIndices[index];
            if (materialIndex == 0)
            {
                continue;
            }

            long worldX = anchor.WorldX + (index % scene.Definition.Width);
            long worldY = anchor.WorldY + (index / scene.Definition.Width);
            if (NoitaPixelSceneCatalog.TrySample(worldX, worldY, out _))
            {
                continue;
            }

            int chunkX = FloorDivide(worldX, 64);
            int chunkY = FloorDivide(worldY, 64);
            if (!generatedChunks.TryGetValue((chunkX, chunkY), out ushort[]? cells))
            {
                cells = new ushort[64 * 64];
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
                generatedChunks.Add((chunkX, chunkY), cells);
            }

            int localX = (int)(worldX - ((long)chunkX * 64));
            int localY = (int)(worldY - ((long)chunkY * 64));
            matched = cells[(localY * 64) + localX] == compiled.MaterialIds[materialIndex];
        }

        Assert.True(matched, $"场景 {scene.Definition.Id} 没有任何材质像素写入权威 chunk。");

        foreach (((int chunkX, int chunkY), ushort[] first) in generatedChunks)
        {
            ushort[] second = new ushort[64 * 64];
            Half[] temperatures = new Half[16 * 16];
            PlayableCavernWorldGenerator.PopulateChunkForVerification(
                materials,
                chunkX,
                chunkY,
                second,
                temperatures,
                config.InitialRunSeed,
                config,
                biomes,
                wang);
            Assert.Equal(first, second);
        }
    }

    private static IMaterialQuery LoadMaterials()
    {
        return EngineContentLoader.LoadMaterialPackage(ContentRoot()).Materials;
    }

    private static NoitaWangMarkerAnchor FindSnowcastleSceneAnchor(
        BiomeCatalog biomes,
        NoitaWangTerrainCatalog wang,
        CampaignConfig config)
    {
        NoitaWangMarkerAnchor[] anchors = new NoitaWangMarkerAnchor[512];
        for (long minimumY = 5200; minimumY <= 6200; minimumY += 256)
        {
            for (long minimumX = -1024; minimumX <= 4096; minimumX += 256)
            {
                int count = PlayableCavernWorldGenerator.CollectWangMarkerAnchors(
                    biomes,
                    wang,
                    config,
                    config.InitialRunSeed,
                    minimumX,
                    minimumY,
                    minimumX + 255,
                    minimumY + 255,
                    anchors);
                for (int i = 0; i < count; i++)
                {
                    if (NoitaSnowcastlePixelSceneCatalog.Supports(in anchors[i]))
                    {
                        return anchors[i];
                    }
                }
            }
        }

        throw new Xunit.Sdk.XunitException("参考 Snowcastle 范围内未找到 Pixel Scene marker。");
    }

    private static string ContentRoot()
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "demo", "PixelEngine.Demo", "content"));
    }

    private static int FloorDivide(long value, int divisor)
    {
        return checked((int)Math.Floor(value / (double)divisor));
    }
}
