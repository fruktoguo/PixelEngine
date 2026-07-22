using System.Text.Json.Nodes;
using PixelEngine.Hosting;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Noita Herringbone Wang 派生数据、来源身份与流式采样契约测试。
/// </summary>
public sealed class NoitaWangTerrainCatalogTests
{
    private const ulong WorldSeed = 0x5049_5845_4C53_4248UL;

    /// <summary>
    /// 验证正式目录锁定 Build 17130612 的 15 套模板、20 个宏图 biome 绑定与来源 hash。
    /// </summary>
    [Fact]
    public void CatalogLocksNoitaBuildSourcesTemplatesAndReferenceBindings()
    {
        NoitaWangTerrainCatalog catalog = LoadCatalog();

        Assert.Equal(NoitaWangTerrainCatalog.CurrentSchemaVersion, catalog.SchemaVersion);
        Assert.Equal("17130612", catalog.ReferenceBuildId);
        Assert.Equal("9dbd52ced019a643169a2db02f46c77f8766c6e5", catalog.ReferenceVersionHash);
        Assert.Equal("stb-herringbone-wang-corner-v1", catalog.Algorithm);
        Assert.Equal("d371be3ed0cdc728461e9f053867142bf2d406507a2aad844dec107f6e1dffa0", catalog.AlgorithmLicenseSha256);
        Assert.Equal("122df34514edaf312e1a15a619b3d6a44d49ce605c929d5950c9051a57429d04", catalog.SourceMaterialsSha256);
        Assert.Equal(15, catalog.Sets.Length);
        Assert.Equal(
            [
                "coalmine",
                "coalmine-alt",
                "excavationsite",
                "fungicave",
                "fungiforest",
                "snowcave",
                "snowcastle",
                "rainforest",
                "rainforest-open",
                "rainforest-dark",
                "vault",
                "vault-frozen",
                "crypt",
                "wandcave",
                "wizardcave",
            ],
            catalog.Sets.Select(static set => set.Id));
        Assert.Equal(
            [13, 13, 20, 13, 13, 26, 13, 20, 20, 20, 20, 20, 22, 15, 22],
            catalog.Sets.Select(static set => set.ShortSide));
        Assert.Equal(
            [72, 32, 32, 27, 27, 32, 48, 32, 32, 32, 32, 32, 36, 32, 36],
            catalog.Sets.Select(static set => set.HorizontalTileCount));
        Assert.Equal(
            [72, 32, 32, 27, 27, 32, 48, 16, 16, 16, 32, 32, 24, 32, 24],
            catalog.Sets.Select(static set => set.VerticalTileCount));

        string[] bindings = [.. catalog.Sets.SelectMany(static set => set.ReferenceBiomeIds)];
        Assert.Equal(20, bindings.Length);
        Assert.Equal(bindings.Length, bindings.Distinct(StringComparer.Ordinal).Count());
        Assert.All(catalog.Sets, set =>
        {
            Assert.StartsWith("data/biome/", set.SourceBiomePath, StringComparison.Ordinal);
            Assert.StartsWith("data/wang_tiles/", set.SourceWangPath, StringComparison.Ordinal);
            Assert.StartsWith("data/scripts/biomes/", set.SpawnSourcePath, StringComparison.Ordinal);
            Assert.Equal(64, set.SourceBiomeSha256.Length);
            Assert.Equal(64, set.SourceWangSha256.Length);
            Assert.Equal(64, set.SpawnSourceSha256.Length);
            Assert.Equal(64, set.DecodedSha256.Length);
            Assert.NotEmpty(set.Markers);
            Assert.Same(set.Decoded, catalog.FindForReferenceBiome(set.ReferenceBiomeIds[0]));
        });
        Assert.Equal(
            "7e45205c7eb1e7a804e73f1ae7d7c3bbb37436d41f530e769c3921544041c8dc",
            catalog.Sets[0].SourceWangSha256);
        Assert.Equal(
            "11b43f3a3d5653ce8529166e9b3d50e62e8a70b78bb48890a15e0d4eb632e268",
            catalog.Sets[^1].SourceWangSha256);
    }

    /// <summary>
    /// 验证未知字段、伪造 decoded hash、错误 tile 头与重复 reference binding 均 fail-closed。
    /// </summary>
    [Fact]
    public void CatalogRejectsUnknownFieldsCorruptPayloadMetadataAndDuplicateBindings()
    {
        string source = File.ReadAllText(Path.Combine(ContentRoot(), "noita-wang-terrain.json"));

        JsonObject unknown = ParseObject(source);
        unknown["unmappedField"] = true;
        InvalidDataException unknownError = Assert.Throws<InvalidDataException>(
            () => NoitaWangTerrainCatalog.Parse(unknown.ToJsonString()));
        _ = Assert.IsType<System.Text.Json.JsonException>(unknownError.InnerException);

        JsonObject corruptHash = ParseObject(source);
        JsonObject firstSet = FirstSet(corruptHash);
        firstSet["decodedSha256"] = new string('0', 64);
        InvalidDataException hashError = Assert.Throws<InvalidDataException>(
            () => NoitaWangTerrainCatalog.Parse(corruptHash.ToJsonString()));
        Assert.Contains("decodedSha256 与解码内容不一致", hashError.Message, StringComparison.Ordinal);

        JsonObject badHeader = ParseObject(source);
        JsonArray cornerColors = Assert.IsType<JsonArray>(FirstSet(badHeader)["cornerColors"]);
        cornerColors[0] = 2;
        InvalidDataException headerError = Assert.Throws<InvalidDataException>(
            () => NoitaWangTerrainCatalog.Parse(badHeader.ToJsonString()));
        Assert.Contains("horizontalTileCount 与 STB 模板头不一致", headerError.Message, StringComparison.Ordinal);

        JsonObject duplicateBinding = ParseObject(source);
        JsonArray sets = Assert.IsType<JsonArray>(duplicateBinding["sets"]);
        JsonObject secondSet = Assert.IsType<JsonObject>(sets[1]);
        JsonArray secondBindings = Assert.IsType<JsonArray>(secondSet["referenceBiomeIds"]);
        secondBindings[0] = "coalmine";
        InvalidDataException bindingError = Assert.Throws<InvalidDataException>(
            () => NoitaWangTerrainCatalog.Parse(duplicateBinding.ToJsonString()));
        Assert.Contains("绑定重复", bindingError.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 15 套模板在正负坐标、64-cell chunk 边界和反向访问下结果一致，
    /// 不同 seed/biome 产生不同签名，稳态采样不分配。
    /// </summary>
    [Fact]
    public void SamplingIsLoadOrderIndependentSeedSensitiveDistinctAndAllocationFree()
    {
        NoitaWangTerrainCatalog catalog = LoadCatalog();
        HashSet<ulong> signatures = [];
        int totalMarkers = 0;
        foreach (NoitaWangTerrainSetDefinition definition in catalog.Sets)
        {
            DecodedNoitaWangTerrainSet set = definition.Decoded;
            ulong salt = StableIdSalt(definition.ReferenceBiomeIds[0]);
            ulong firstSignature = SampleSignature(
                set,
                WorldSeed,
                salt,
                reverse: false,
                out int emptyCount,
                out int solidCount,
                out int markerCount);
            ulong reverseSignature = SampleSignature(
                set,
                WorldSeed,
                salt,
                reverse: true,
                out int reverseEmptyCount,
                out int reverseSolidCount,
                out int reverseMarkerCount);
            ulong otherSeedSignature = SampleSignature(
                set,
                WorldSeed ^ 0x9E37_79B9_7F4A_7C15UL,
                salt,
                reverse: false,
                out _,
                out _,
                out _);

            Assert.Equal(firstSignature, reverseSignature);
            Assert.Equal(emptyCount, reverseEmptyCount);
            Assert.Equal(solidCount, reverseSolidCount);
            Assert.Equal(markerCount, reverseMarkerCount);
            Assert.NotEqual(firstSignature, otherSeedSignature);
            Assert.InRange(emptyCount, 1_024, (256 * 256) - 1_024);
            Assert.InRange(solidCount, 1_024, (256 * 256) - 1_024);
            Assert.True(signatures.Add(firstSignature), $"Wang set {definition.Id} 与其他模板产生相同采样签名。");
            totalMarkers += markerCount;

            foreach ((long x, long y) in BoundaryCoordinates())
            {
                Assert.Equal(
                    set.Sample(x, y, WorldSeed, salt),
                    set.Sample(x, y, WorldSeed, salt));
            }
        }

        Assert.True(totalMarkers > 0, "采样窗口必须命中并保留至少一个 spawn marker semantic。");

        DecodedNoitaWangTerrainSet hotSet = catalog.Sets[0].Decoded;
        ulong hotSalt = StableIdSalt("coalmine");
        for (int i = 0; i < 4_096; i++)
        {
            _ = hotSet.Sample(i - 2_048, (i * 17L) - 8_192, WorldSeed, hotSalt);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int checksum = 0;
        for (int i = 0; i < 65_536; i++)
        {
            checksum += hotSet.Sample(i - 32_768, (i * 31L) - 65_536, WorldSeed, hotSalt);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        Assert.InRange(allocated, 0, 1_024);
    }

    private static ulong SampleSignature(
        DecodedNoitaWangTerrainSet set,
        ulong worldSeed,
        ulong salt,
        bool reverse,
        out int emptyCount,
        out int solidCount,
        out int markerCount)
    {
        const int Side = 256;
        byte[] samples = new byte[Side * Side];
        emptyCount = 0;
        solidCount = 0;
        markerCount = 0;
        int start = reverse ? samples.Length - 1 : 0;
        int end = reverse ? -1 : samples.Length;
        int step = reverse ? -1 : 1;
        for (int index = start; index != end; index += step)
        {
            int x = (index % Side) - 128;
            int y = (index / Side) - 128;
            byte semantic = set.Sample(x, y, worldSeed, salt);
            samples[index] = semantic;
            if (DecodedNoitaWangTerrainSet.IsMarker(semantic))
            {
                markerCount++;
            }
            else if (semantic == (byte)NoitaWangTerrainSemantic.Empty)
            {
                emptyCount++;
            }
            else
            {
                solidCount++;
            }
        }

        ulong signature = 14_695_981_039_346_656_037UL;
        for (int i = 0; i < samples.Length; i++)
        {
            signature ^= samples[i];
            signature *= 1_099_511_628_211UL;
        }

        return signature;
    }

    private static IEnumerable<(long X, long Y)> BoundaryCoordinates()
    {
        long[] coordinates = [-129, -128, -65, -64, -63, -1, 0, 1, 63, 64, 65, 127, 128, 129];
        for (int i = 0; i < coordinates.Length; i++)
        {
            yield return (coordinates[i], coordinates[i * 5 % coordinates.Length]);
        }
    }

    private static ulong StableIdSalt(string value)
    {
        ulong hash = 14_695_981_039_346_656_037UL;
        for (int i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= 1_099_511_628_211UL;
        }

        return hash;
    }

    private static NoitaWangTerrainCatalog LoadCatalog()
    {
        return NoitaWangTerrainCatalog.Load(new EngineScriptConfigApi(ContentRoot()));
    }

    private static JsonObject FirstSet(JsonObject document)
    {
        JsonArray sets = Assert.IsType<JsonArray>(document["sets"]);
        return Assert.IsType<JsonObject>(sets[0]);
    }

    private static JsonObject ParseObject(string json)
    {
        return Assert.IsType<JsonObject>(JsonNode.Parse(json));
    }

    private static string ContentRoot()
    {
        return Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }
}
