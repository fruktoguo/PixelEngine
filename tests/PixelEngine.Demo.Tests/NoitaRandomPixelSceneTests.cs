using System.Security.Cryptography;
using System.Text.Json;
using PixelEngine.Hosting;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Noita biome 随机 Pixel Scene 的离线编译与权威地形组合测试。
/// </summary>
public sealed class NoitaRandomPixelSceneTests
{
    /// <summary>验证 319 个场景及 183 个自持 visual/background 资产的 provenance。</summary>
    [Fact]
    public void RuntimeCatalogPreservesAllCompiledScenesAndOwnedAssets()
    {
        string contentRoot = ContentRoot();
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(contentRoot, "noita-random-pixel-scenes-runtime.json")));
        JsonElement root = document.RootElement;
        Assert.Equal("pixelengine.noita-random-pixel-scenes-runtime/v1", root.GetProperty("schema").GetString());
        Assert.Equal("17130612", root.GetProperty("referenceBuildId").GetString());
        Assert.Equal(87, root.GetProperty("tables").GetInt32());
        Assert.Equal(319, root.GetProperty("scenes").GetInt32());
        Assert.Equal(73, root.GetProperty("materialNames").GetArrayLength());
        Assert.Equal(2, root.GetProperty("overrideCount").GetInt32());
        Assert.Equal(183, root.GetProperty("assetCount").GetInt32());

        using JsonDocument provenanceDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(contentRoot, "maps", "noita", "random-pixel-scenes", "provenance.json")));
        JsonElement[] assets = [.. provenanceDocument.RootElement.GetProperty("assets").EnumerateArray()];
        Assert.Equal(183, assets.Length);
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

    /// <summary>验证全部掩码、材质索引与 Vault 颜色覆盖可编译为运行时 id。</summary>
    [Fact]
    public void MaterialMasksAndColorOverridesCompileToStableRuntimeIds()
    {
        IMaterialQuery materials = LoadMaterials();
        CompiledNoitaRandomPixelSceneCatalog compiled = NoitaRandomPixelSceneCatalog.Compile(materials);
        Assert.Equal(74, compiled.MaterialIds.Length);
        Assert.Equal(2, compiled.OverrideMaterialIds.Length);
        Assert.Equal(319, compiled.Scenes.Length);
        Assert.Equal(520, NoitaRandomPixelSceneCatalog.MaximumWidth);
        Assert.Equal(520, NoitaRandomPixelSceneCatalog.MaximumHeight);
        Assert.All(compiled.OverrideMaterialIds, static choices => Assert.Equal(6, choices.Length));

        foreach (DecodedNoitaRandomPixelScene scene in compiled.Scenes)
        {
            Assert.Equal(scene.Definition.Width * scene.Definition.Height, scene.PixelCodes.Length);
            Assert.All(scene.PixelCodes, static code => Assert.InRange(code, (byte)0, (byte)75));
            Assert.Contains(scene.PixelCodes, static code => code > 0);
        }
    }

    /// <summary>验证 biome id 规范化和 is_unique 的全局至多一次语义。</summary>
    [Fact]
    public void NormalizedBiomeBindingAndUniqueSelectionAreDeterministic()
    {
        EngineScriptConfigApi configApi = new(ContentRoot());
        CampaignConfig config = CampaignConfig.Load(configApi);
        BiomeCatalog biomes = BiomeCatalog.Load(configApi, config);
        NoitaWangTerrainCatalog wang = NoitaWangTerrainCatalog.Load(configApi);
        NoitaRandomPixelSceneUniqueAnchor[] winners =
            PlayableCavernWorldGenerator.ResolveUniqueRandomPixelSceneAnchors(
                biomes,
                wang,
                config,
                config.InitialRunSeed);

        Assert.InRange(winners.Length, 0, 1);
        Assert.True(NoitaRandomPixelSceneCatalog.BiomeIdsMatch("coalmine_alt", "coalmine-alt"));

        NoitaWangMarkerAnchor[] anchors = CollectCoalmineAltAnchors(biomes, wang, config);
        int uniqueSelections = 0;
        foreach (NoitaWangMarkerAnchor anchor in anchors)
        {
            int sceneIndex = NoitaRandomPixelSceneCatalog.SelectSceneIndex(
                anchor.ReferenceBiomeId,
                NoitaRandomPixelSceneCatalog.ResolveMarkerFunction(in anchor),
                anchor.WorldX,
                anchor.WorldY,
                config.InitialRunSeed,
                winners);
            uniqueSelections += sceneIndex >= 0 && NoitaRandomPixelSceneCatalog.Scenes[sceneIndex].IsUnique ? 1 : 0;
        }

        Assert.Equal(winners.Length, uniqueSelections);
    }

    /// <summary>验证随机场景 visual/background 进入视口，并取代通用 marker 占位。</summary>
    [Fact]
    public void ViewportCollectsOwnedVisualLayersAndSuppressesMarkerPlaceholder()
    {
        EngineScriptConfigApi configApi = new(ContentRoot());
        CampaignConfig config = CampaignConfig.Load(configApi);
        BiomeCatalog biomes = BiomeCatalog.Load(configApi, config);
        NoitaWangTerrainCatalog wang = NoitaWangTerrainCatalog.Load(configApi);
        IMaterialQuery materials = LoadMaterials();
        NoitaRandomPixelSceneUniqueAnchor[] winners =
            PlayableCavernWorldGenerator.ResolveUniqueRandomPixelSceneAnchors(
                biomes,
                wang,
                config,
                config.InitialRunSeed);
        NoitaWangMarkerAnchor anchor = CollectCoalmineAltAnchors(biomes, wang, config)
            .First(candidate =>
            {
                int sceneIndex = NoitaRandomPixelSceneCatalog.SelectSceneIndex(
                    candidate.ReferenceBiomeId,
                    NoitaRandomPixelSceneCatalog.ResolveMarkerFunction(in candidate),
                    candidate.WorldX,
                    candidate.WorldY,
                    config.InitialRunSeed,
                    winners);
                return sceneIndex >= 0 &&
                    (NoitaRandomPixelSceneCatalog.Scenes[sceneIndex].VisualAsset.IsValid ||
                     NoitaRandomPixelSceneCatalog.Scenes[sceneIndex].BackgroundAsset.IsValid);
            });
        Assert.False(NoitaWangMarkerVisualProfile.TryCreate(anchor, out _));

        PlayableCavernWorldGenerator generator = new();
        ProceduralWorldBuildRequest request = new(
            PlayableCavernWorldGenerator.Key,
            materials,
            Config: configApi,
            WorldSeedOverride: config.InitialRunSeed);
        _ = generator.Describe(in request);
        WorldVisualLayerDescriptor[] layers = new WorldVisualLayerDescriptor[256];
        int count = generator.CollectWorldVisualLayers(
            anchor.WorldX - 64,
            anchor.WorldY - 64,
            anchor.WorldX + 64,
            anchor.WorldY + 64,
            layers);

        Assert.Contains(
            layers.AsSpan(0, count).ToArray(),
            static layer => layer.Asset.LogicalPath.Contains("random-pixel-scenes", StringComparison.Ordinal));
    }

    /// <summary>验证选中场景的材质像素进入权威 64x64 chunk。</summary>
    [Fact]
    public void SelectedSceneWritesMaterialsIntoAuthoritativeChunk()
    {
        EngineScriptConfigApi configApi = new(ContentRoot());
        CampaignConfig config = CampaignConfig.Load(configApi);
        BiomeCatalog biomes = BiomeCatalog.Load(configApi, config);
        NoitaWangTerrainCatalog wang = NoitaWangTerrainCatalog.Load(configApi);
        IMaterialQuery materials = LoadMaterials();
        NoitaRandomPixelSceneUniqueAnchor[] winners =
            PlayableCavernWorldGenerator.ResolveUniqueRandomPixelSceneAnchors(
                biomes,
                wang,
                config,
                config.InitialRunSeed);
        NoitaWangMarkerAnchor anchor = CollectCoalmineAltAnchors(biomes, wang, config)
            .First(static candidate => NoitaRandomPixelSceneCatalog.Supports(in candidate));
        int sceneIndex = NoitaRandomPixelSceneCatalog.SelectSceneIndex(
            anchor.ReferenceBiomeId,
            NoitaRandomPixelSceneCatalog.ResolveMarkerFunction(in anchor),
            anchor.WorldX,
            anchor.WorldY,
            config.InitialRunSeed,
            winners);
        DecodedNoitaRandomPixelScene scene = NoitaRandomPixelSceneCatalog.DecodedScenes[sceneIndex];
        CompiledNoitaRandomPixelSceneCatalog compiled = NoitaRandomPixelSceneCatalog.Compile(materials);
        Dictionary<(int X, int Y), ushort[]> generated = [];
        bool matched = false;

        for (int index = 0; index < scene.PixelCodes.Length && !matched; index++)
        {
            byte code = scene.PixelCodes[index];
            if (code == 0)
            {
                continue;
            }

            int sceneX = index % scene.Definition.Width;
            int sceneY = index / scene.Definition.Width;
            long worldX = anchor.WorldX + sceneX;
            long worldY = anchor.WorldY + sceneY;
            if (NoitaPixelSceneCatalog.TrySample(worldX, worldY, out _))
            {
                continue;
            }

            int chunkX = FloorDivide(worldX, 64);
            int chunkY = FloorDivide(worldY, 64);
            if (!generated.TryGetValue((chunkX, chunkY), out ushort[]? cells))
            {
                cells = new ushort[64 * 64];
                PlayableCavernWorldGenerator.PopulateChunkForVerification(
                    materials,
                    chunkX,
                    chunkY,
                    cells,
                    new Half[16 * 16],
                    config.InitialRunSeed,
                    config,
                    biomes,
                    wang);
                generated.Add((chunkX, chunkY), cells);
            }

            ushort expected = NoitaRandomPixelSceneCatalog.ResolveMaterial(
                compiled,
                code,
                anchor.WorldX,
                anchor.WorldY,
                sceneX,
                sceneY,
                config.InitialRunSeed);
            int localX = (int)(worldX - ((long)chunkX * 64));
            int localY = (int)(worldY - ((long)chunkY * 64));
            matched = cells[(localY * 64) + localX] == expected;
        }

        Assert.True(matched, $"随机场景 {scene.Definition.Id} 没有材质像素写入权威 chunk。");
    }

    private static NoitaWangMarkerAnchor[] CollectCoalmineAltAnchors(
        BiomeCatalog biomes,
        NoitaWangTerrainCatalog wang,
        CampaignConfig config)
    {
        List<NoitaWangMarkerAnchor> result = [];
        NoitaWangMarkerAnchor[] buffer = new NoitaWangMarkerAnchor[64 * 64];
        for (long minimumX = -1536; minimumX < -512; minimumX += 64)
        {
            for (long minimumY = config.SurfaceY + 512; minimumY < config.SurfaceY + 1024; minimumY += 64)
            {
                int count = PlayableCavernWorldGenerator.CollectWangMarkerAnchors(
                    biomes,
                    wang,
                    config,
                    config.InitialRunSeed,
                    minimumX,
                    minimumY,
                    minimumX + 63,
                    minimumY + 63,
                    buffer);
                for (int i = 0; i < count; i++)
                {
                    if (NoitaRandomPixelSceneCatalog.Supports(in buffer[i]))
                    {
                        result.Add(buffer[i]);
                    }
                }
            }
        }

        Assert.NotEmpty(result);
        return [.. result];
    }

    private static IMaterialQuery LoadMaterials()
    {
        return EngineContentLoader.LoadMaterialPackage(ContentRoot()).Materials;
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
