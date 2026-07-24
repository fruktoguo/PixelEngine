using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Noita Build 17130612 世界内容目录的来源、完整性与关键组合层测试。
/// </summary>
public sealed class NoitaWorldContentCatalogTests
{
    /// <summary>
    /// 锁定完整 biome、材料、植被、Lua marker、pixel scene 与背景来源，防止退回少量手写代表项。
    /// </summary>
    [Fact]
    public void CatalogPreservesCompleteWorldCompositionSource()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(ContentRoot(), "noita-world-content.json")));
        JsonElement root = document.RootElement;
        Assert.Equal("pixelengine.noita-world-content/v1", root.GetProperty("schema").GetString());

        JsonElement reference = root.GetProperty("reference");
        Assert.Equal("17130612", reference.GetProperty("steamBuildId").GetString());
        Assert.Equal("9dbd52ced019a643169a2db02f46c77f8766c6e5", reference.GetProperty("versionHash").GetString());
        Assert.Equal(14_745, reference.GetProperty("dataRootFileCount").GetInt32());
        Assert.Equal(
            "122df34514edaf312e1a15a619b3d6a44d49ce605c929d5950c9051a57429d04",
            reference.GetProperty("materialsSha256").GetString());
        Assert.Equal(
            "78cf9b4c0abfc4fc24239ab9595d8c54e6e76b212773d8d2d2bf9be4f3fcdf58",
            reference.GetProperty("globalPixelScenesSha256").GetString());

        JsonElement statistics = root.GetProperty("statistics");
        Assert.Equal(146, statistics.GetProperty("biomes").GetInt32());
        Assert.Equal(640, statistics.GetProperty("materialLayers").GetInt32());
        Assert.Equal(317, statistics.GetProperty("vegetationLayers").GetInt32());
        Assert.Equal(785, statistics.GetProperty("spawnFunctions").GetInt32());
        Assert.Equal(12, statistics.GetProperty("splicedPixelSceneFiles").GetInt32());
        Assert.Equal(17, statistics.GetProperty("globalBackgroundImages").GetInt32());
        Assert.Equal(91, statistics.GetProperty("bufferedPixelScenes").GetInt32());
        Assert.Equal(2_232, statistics.GetProperty("biomeImplFiles").GetInt32());
        Assert.Equal(229, statistics.GetProperty("vegetationFiles").GetInt32());
    }

    /// <summary>
    /// 煤矿必须同时保留材料、植被与 Lua marker；全局场景必须保留固定背景和 buffered scene。
    /// </summary>
    [Fact]
    public void CatalogKeepsCoalmineAndGlobalSceneCompositionLayers()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(ContentRoot(), "noita-world-content.json")));
        JsonElement root = document.RootElement;
        JsonElement coalmine = root.GetProperty("biomes").EnumerateArray()
            .Single(static biome => biome.GetProperty("id").GetString() == "coalmine");

        Assert.Equal("data/biome/coalmine.xml", coalmine.GetProperty("sourcePath").GetString());
        Assert.Equal(7, coalmine.GetProperty("materialLayers").GetArrayLength());
        Assert.Equal(8, coalmine.GetProperty("vegetationLayers").GetArrayLength());
        JsonElement spawnFunctions = coalmine.GetProperty("lua").GetProperty("spawnFunctions");
        Assert.Equal(19, spawnFunctions.GetArrayLength());
        Assert.Contains(
            spawnFunctions.EnumerateArray(),
            static spawn => spawn.GetProperty("function").GetString() == "spawn_vines");
        Assert.Contains(
            spawnFunctions.EnumerateArray(),
            static spawn => spawn.GetProperty("function").GetString() == "spawn_chest");

        JsonElement global = root.GetProperty("globalPixelScenes");
        Assert.Contains(
            global.GetProperty("splicedFiles").EnumerateArray(),
            static path => path.GetString() == "data/biome_impl/spliced/boss_arena.xml");
        Assert.Contains(
            global.GetProperty("backgroundImages").EnumerateArray(),
            static image => image.GetProperty("filename").GetString() ==
                "data/biome_impl/hidden/holy_mountain_1.png");
        Assert.Contains(
            global.GetProperty("bufferedScenes").EnumerateArray(),
            static scene => scene.TryGetProperty("material_filename", out JsonElement path) &&
                path.GetString() == "data/biome_impl/snowcastle/forge.png");

        JsonElement[] buffered = [.. global.GetProperty("bufferedScenes").EnumerateArray()];
        Assert.Equal(30, buffered.Count(static scene => scene.GetProperty("assets").GetProperty("material").ValueKind == JsonValueKind.Object));
        Assert.Equal(22, buffered.Count(static scene => scene.GetProperty("assets").GetProperty("colors").ValueKind == JsonValueKind.Object));
        Assert.Equal(6, buffered.Count(static scene => scene.GetProperty("assets").GetProperty("background").ValueKind == JsonValueKind.Object));

        JsonElement forge = buffered.Single(static scene =>
            scene.TryGetProperty("material_filename", out JsonElement path) &&
            path.GetString() == "data/biome_impl/snowcastle/forge.png");
        AssertAsset(forge.GetProperty("assets").GetProperty("material"), "0dc4f73e2353119606ff33db0cc2fb4f0453491278c4fe125275030eb885f1a6");
        AssertAsset(forge.GetProperty("assets").GetProperty("colors"), "eda605ee5094d45f02f46521ee7a77d704d23c34d1fbc98785e1ac6817326121");
        AssertAsset(forge.GetProperty("assets").GetProperty("background"), "ff94e8889ed4777a94bed4f8e6a68015080d8a16a04f0295c45cdca8f7b867ed");
    }

    /// <summary>
    /// 所有参考 Lua 注册必须在构建前转换为纯 C# rule；运行时不得依赖 Lua fallback。
    /// </summary>
    [Fact]
    public void EverySourceMarkerRegistrationHasPureCSharpRule()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(ContentRoot(), "noita-world-content.json")));
        Dictionary<string, int> registrations = new(StringComparer.Ordinal);
        foreach (JsonElement biome in document.RootElement.GetProperty("biomes").EnumerateArray())
        {
            if (biome.GetProperty("lua").ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            foreach (JsonElement spawn in biome.GetProperty("lua").GetProperty("spawnFunctions").EnumerateArray())
            {
                string function = spawn.GetProperty("function").GetString()!;
                registrations[function] = registrations.GetValueOrDefault(function) + 1;
            }
        }

        Assert.Equal(NoitaMarkerRuleCatalog.UniqueRuleCount, registrations.Count);
        Assert.Equal(NoitaMarkerRuleCatalog.SourceRegistrationCount, registrations.Values.Sum());
        foreach ((string function, int count) in registrations)
        {
            Assert.True(NoitaMarkerRuleCatalog.TryResolve(function, out NoitaMarkerRule rule), function);
            Assert.Equal(function, rule.Function);
            Assert.Equal(count, rule.SourceRegistrationCount);
            Assert.InRange(rule.SourceBiomeCount, 1, 146);
        }

        Assert.Equal(NoitaMarkerRuleKind.Vegetation, NoitaMarkerRuleCatalog.Resolve("spawn_vines").Kind);
        Assert.Equal(NoitaMarkerRuleKind.Loot, NoitaMarkerRuleCatalog.Resolve("spawn_chest").Kind);
        Assert.Equal(NoitaMarkerRuleKind.Prop, NoitaMarkerRuleCatalog.Resolve("load_oiltank").Kind);
        Assert.Equal(NoitaMarkerRuleKind.PixelScene, NoitaMarkerRuleCatalog.Resolve("load_pixel_scene4").Kind);
        Assert.Equal(NoitaMarkerRuleKind.Encounter, NoitaMarkerRuleCatalog.Resolve("spawn_large_enemies").Kind);
    }

    /// <summary>
    /// 全局 buffered pixel scene 必须全部预编译成 C# 坐标和资产边界，运行时不读取 XML。
    /// </summary>
    [Fact]
    public void GlobalPixelScenesArePrecompiledIntoPureCSharpCatalog()
    {
        Assert.Equal(91, NoitaPixelSceneCatalog.SceneCount);
        Assert.Equal(91, NoitaPixelSceneCatalog.Scenes.Length);
        Assert.True(NoitaPixelSceneCatalog.TryFindAt(1464, 5976, out NoitaPixelSceneDefinition forge));
        Assert.Equal("global-buffered-2", forge.StableId);
        Assert.Equal(128, forge.Width);
        Assert.Equal(128, forge.Height);
        Assert.Equal("data/biome_impl/snowcastle/forge.png", forge.MaterialPath);
        Assert.Equal("data/biome_impl/snowcastle/forge_visual.png", forge.ColorsPath);
        Assert.Equal("data/biome_impl/snowcastle/forge_background.png", forge.BackgroundPath);
        Assert.Equal("0dc4f73e2353119606ff33db0cc2fb4f0453491278c4fe125275030eb885f1a6", forge.MaterialSha256);
        Assert.False(NoitaPixelSceneCatalog.TryFindAt(0, 0, out _));
    }

    /// <summary>
    /// material PNG 必须在构建前转换成带 hash 的语义掩码，运行时按原始世界坐标逐像素采样。
    /// </summary>
    [Fact]
    public void GlobalPixelSceneMaterialMasksDecodeAndSampleAtWorldScale()
    {
        Assert.Equal(30, NoitaPixelSceneCatalog.Masks.Length);
        Assert.Equal(30, NoitaPixelSceneCatalog.DecodedScenes.Length);
        Assert.True(NoitaPixelSceneCatalog.Masks.ToArray().Sum(static mask => mask.MarkerPixelCount) > 0);

        DecodedNoitaPixelScene forge = NoitaPixelSceneCatalog.DecodedScenes.ToArray().Single(static scene => scene.Ordinal == 2);
        Assert.Equal(128, forge.Width);
        Assert.Equal(128, forge.Height);
        Assert.Equal(128 * 128, forge.Materials.Length);
        Assert.All(forge.Materials, static value => Assert.InRange(value, (byte)0, (byte)NoitaPixelSceneMaterial.Glass));

        int materialIndex = Array.FindIndex(forge.Materials, static value => value != (byte)NoitaPixelSceneMaterial.Empty);
        Assert.True(materialIndex >= 0);
        int localX = materialIndex % forge.Width;
        int localY = materialIndex / forge.Width;
        Assert.True(NoitaPixelSceneCatalog.TrySample(
            forge.WorldX + localX,
            forge.WorldY + localY,
            out NoitaPixelSceneMaterial sampled));
        Assert.Equal((NoitaPixelSceneMaterial)forge.Materials[materialIndex], sampled);
        Assert.False(NoitaPixelSceneCatalog.TrySample(forge.WorldX - 1, forge.WorldY - 1, out _));
    }

    /// <summary>
    /// colors/background 图片必须随 Demo 独立分发，并逐项绑定参考来源 hash 与世界矩形。
    /// </summary>
    [Fact]
    public void GlobalPixelSceneVisualAssetsKeepCompleteProvenance()
    {
        string visualRoot = Path.Combine(ContentRoot(), "maps", "noita", "global-scenes");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(visualRoot, "provenance.json")));
        JsonElement root = document.RootElement;
        Assert.Equal("17130612", root.GetProperty("referenceBuildId").GetString());
        Assert.Equal(28, root.GetProperty("assetCount").GetInt32());
        JsonElement[] assets = [.. root.GetProperty("assets").EnumerateArray()];
        Assert.Equal(6, assets.Count(static asset => asset.GetProperty("kind").GetString() == "background"));
        Assert.Equal(22, assets.Count(static asset => asset.GetProperty("kind").GetString() == "colors"));

        foreach (JsonElement asset in assets)
        {
            string relativePath = asset.GetProperty("contentPath").GetString()!;
            string file = Path.Combine(ContentRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), relativePath);
            Assert.Equal(
                asset.GetProperty("sourceSha256").GetString(),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());
            Assert.Equal(asset.GetProperty("sourceSha256").GetString(), asset.GetProperty("contentSha256").GetString());
            Assert.True(asset.GetProperty("width").GetInt32() > 0);
            Assert.True(asset.GetProperty("height").GetInt32() > 0);
        }
    }

    private static string ContentRoot()
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "demo", "PixelEngine.Demo", "content"));
    }

    private static void AssertAsset(JsonElement asset, string expectedSha256)
    {
        Assert.Equal(expectedSha256, asset.GetProperty("sha256").GetString());
        Assert.Equal(128, asset.GetProperty("width").GetInt32());
        Assert.Equal(128, asset.GetProperty("height").GetInt32());
        Assert.True(asset.GetProperty("length").GetInt64() > 0);
    }
}
