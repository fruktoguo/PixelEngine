using System.Text.Json.Nodes;
using PixelEngine.Hosting;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Noita BitmapCaves XML 快照、结构语义与流式采样契约测试。
/// </summary>
public sealed class NoitaBitmapCavesTests
{
    private const ulong WorldSeed = 0x5049_5845_4C53_4248UL;
    private const int ChunkSize = 64;
    private const int TemperatureSize = 16;

    /// <summary>
    /// 锁定 Build 17130612 中 4 个缺失、3 个全零声明和 8 个启用配置，
    /// 以及 Coal Mine danger room 的原图身份和语义布局。
    /// </summary>
    [Fact]
    public void CatalogLocksBitmapCavesGroupsRangesAndDangerRoomSemantics()
    {
        NoitaWangTerrainCatalog catalog = LoadCatalog();

        Assert.Equal(
            ["fungicave", "fungiforest", "snowcave", "snowcastle", "clouds", "lake-deep", "meat", "mountain-center", "sandcave"],
            catalog.Sets
                .Where(static set => set.BitmapCaves is null)
                .Select(static set => set.Id));
        Assert.Equal(
            ["rainforest", "rainforest-open", "rainforest-dark"],
            catalog.Sets
                .Where(static set => set.BitmapCaves is not null && !set.DecodedBitmapCaves!.IsEnabled)
                .Select(static set => set.Id));
        Assert.Equal(
            [
                "coalmine", "coalmine-alt", "excavationsite", "vault", "vault-frozen", "crypt", "wandcave", "wizardcave",
                "liquidcave", "magic-gate", "pyramid", "robobase", "the-end", "the-sky", "town-under", "winter-caves",
            ],
            catalog.Sets
                .Where(static set => set.DecodedBitmapCaves?.IsEnabled == true)
                .Select(static set => set.Id));

        NoitaWangTerrainSetDefinition coalmine = catalog.FindDefinitionForReferenceBiome("coalmine");
        NoitaBitmapCavesDefinition caves = Assert.IsType<NoitaBitmapCavesDefinition>(coalmine.BitmapCaves);
        Assert.Equal(512, caves.SizeX);
        Assert.Equal(256, caves.SizeY);
        Assert.Equal(2, caves.CaveCountMin);
        Assert.Equal(2, caves.CaveCountMax);
        Assert.Equal(0, caves.CaveChildsMin);
        Assert.Equal(1, caves.CaveChildsMax);
        Assert.Equal(0.2, caves.CaveStrengthMin, 8);
        Assert.Equal(1.0, caves.CaveStrengthMax, 8);
        NoitaBitmapCaveStructureDefinition structure = Assert.Single(caves.Structures);
        Assert.Equal((5, 507, 0, 230), (structure.AabbMinX, structure.AabbMaxX, structure.AabbMinY, structure.AabbMaxY));
        Assert.Equal((2, 4), (structure.CountMin, structure.CountMax));
        Assert.Equal((1.45, 1.55), (structure.StrengthMin, structure.StrengthMax));
        NoitaBitmapCaveStructureVariantDefinition variant = Assert.Single(structure.Variants);
        Assert.Equal("data/biome_impl/coalmine/dangerroom.png", variant.SourceImagePath);
        Assert.Equal("dd94d797e8cb3d7c2a121a0f53681a8a3ef8c0d222bdf01d53f5da5487a9401e", variant.SourceImageSha256);
        Assert.Equal(8, variant.SourceWidth);
        Assert.Equal(8, variant.SourceHeight);
        Assert.Equal(64, variant.DecodedLength);
        Assert.Equal("fb15d2dfad0c51e8798170a448f0d710f1f72dee8cbb91544804658fa9842ca4", variant.DecodedSha256);

        DecodedNoitaBitmapCaveStructure decoded = Assert.Single(coalmine.DecodedBitmapCaves!.Structures.ToArray());
        Assert.Equal(64, decoded.Pixels.Length);
        Assert.All(decoded.Pixels.AsSpan(0, 8).ToArray(), value => AssertStructureMaterial(coalmine, value, "wood_static"));
        Assert.Equal((byte)NoitaWangTerrainSemantic.Empty, decoded.Pixels[9]);
        AssertStructureMaterial(coalmine, decoded.Pixels[40], "coal");
        AssertStructureMarker(coalmine, decoded.Pixels[10], "ffffff00", "builtin-or-unresolved-ffffff00");
        AssertStructureMarker(coalmine, decoded.Pixels[18], "ff800000", "builtin-or-unresolved-ff800000");
        AssertStructureMarker(coalmine, decoded.Pixels[51], "ffc88d1a", "builtin-or-unresolved-ffc88d1a");

        NoitaBitmapCavesDefinition vault = Assert.IsType<NoitaBitmapCavesDefinition>(
            catalog.FindDefinitionForReferenceBiome("vault").BitmapCaves);
        Assert.Equal((1, 4), (vault.BlobCavesCountMin, vault.BlobCavesCountMax));
        Assert.Equal((3, 5), (vault.CaveCountMin, vault.CaveCountMax));
        Assert.Equal((1, 3), (vault.SurfaceCavesCountMin, vault.SurfaceCavesCountMax));
        Assert.Equal((1, 3), (vault.SurfaceCaveChildsMin, vault.SurfaceCaveChildsMax));

        NoitaBitmapCavesDefinition crypt = Assert.IsType<NoitaBitmapCavesDefinition>(
            catalog.FindDefinitionForReferenceBiome("crypt").BitmapCaves);
        Assert.Equal(516, crypt.SizeX);
        Assert.Equal((0, 1), (crypt.BlobCavesCountMin, crypt.BlobCavesCountMax));

        NoitaBitmapCavesDefinition winterCaves = Assert.IsType<NoitaBitmapCavesDefinition>(
            catalog.FindDefinitionForReferenceBiome("winter-caves").BitmapCaves);
        Assert.Equal(9, winterCaves.Structures.Length);
        Assert.Equal(1_000, winterCaves.Structures[0].Variants.Length);
        Assert.Equal(1_008, winterCaves.Structures.Sum(static structure => structure.Variants.Length));
        Assert.Equal((5, 12), (winterCaves.Structures[0].CountMin, winterCaves.Structures[0].CountMax));
        Assert.Equal((1, 2), (crypt.CaveCountMin, crypt.CaveCountMax));
        Assert.Equal((0.2, 1.8), (crypt.CaveStrengthMin, crypt.CaveStrengthMax));
    }

    /// <summary>
    /// BitmapCaves 范围、结构来源与语义载荷一旦被篡改必须 fail-closed。
    /// </summary>
    [Fact]
    public void CatalogRejectsInvalidBitmapCavesRangesSourcesAndPayloads()
    {
        string source = File.ReadAllText(Path.Combine(ContentRoot(), "noita-wang-terrain.json"));

        JsonObject badRange = ParseObject(source);
        FirstBitmapCaves(badRange)["caveCountMax"] = 1;
        InvalidDataException rangeError = Assert.Throws<InvalidDataException>(
            () => NoitaWangTerrainCatalog.Parse(badRange.ToJsonString()));
        Assert.Contains("caveCountMax", rangeError.Message, StringComparison.Ordinal);

        JsonObject badPath = ParseObject(source);
        FirstStructureVariant(badPath)["sourceImagePath"] = "data/weather_gfx/fake.png";
        InvalidDataException pathError = Assert.Throws<InvalidDataException>(
            () => NoitaWangTerrainCatalog.Parse(badPath.ToJsonString()));
        Assert.Contains("sourceImagePath", pathError.Message, StringComparison.Ordinal);

        JsonObject badPayload = ParseObject(source);
        FirstStructureVariant(badPayload)["decodedSha256"] = new string('0', 64);
        InvalidDataException payloadError = Assert.Throws<InvalidDataException>(
            () => NoitaWangTerrainCatalog.Parse(badPayload.ToJsonString()));
        Assert.Contains("decodedSha256 与解码内容不一致", payloadError.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 八个启用配置在正负 block 与不同遍历顺序下保持确定，seed 会改变结果；
    /// 全零配置永不覆盖 Wang，稳态采样不分配。
    /// </summary>
    [Fact]
    public void SamplingIsDeterministicSeedSensitiveDisabledAwareAndAllocationFree()
    {
        NoitaWangTerrainCatalog catalog = LoadCatalog();
        HashSet<ulong> signatures = [];
        foreach (NoitaWangTerrainSetDefinition set in catalog.Sets.Where(
                     static item => item.DecodedBitmapCaves?.IsEnabled == true))
        {
            DecodedNoitaBitmapCaves caves = set.DecodedBitmapCaves!;
            ulong salt = StableIdSalt(set.ReferenceBiomeIds[0]);
            ulong forward = SampleSignature(caves, WorldSeed, salt, reverse: false, out int overrides);
            ulong reverse = SampleSignature(caves, WorldSeed, salt, reverse: true, out int reverseOverrides);
            ulong otherSeed = SampleSignature(
                caves,
                WorldSeed ^ 0x9E37_79B9_7F4A_7C15UL,
                salt,
                reverse: false,
                out int otherOverrides);

            Assert.Equal(forward, reverse);
            Assert.Equal(overrides, reverseOverrides);
            Assert.True(overrides > 0, $"{set.Id} 必须在一个完整 BitmapCaves block 内产生覆盖。");
            Assert.True(otherOverrides > 0, $"{set.Id} 的替代 seed 必须产生覆盖。");
            Assert.NotEqual(forward, otherSeed);
            Assert.True(signatures.Add(forward), $"{set.Id} 与其他 BitmapCaves 配置产生相同签名。");

            foreach ((long x, long y) in BoundaryCoordinates(caves.SizeX, caves.SizeY))
            {
                bool first = caves.TrySample(x, y, WorldSeed, salt, out byte firstSemantic);
                bool repeated = caves.TrySample(x, y, WorldSeed, salt, out byte repeatedSemantic);
                Assert.Equal(first, repeated);
                Assert.Equal(firstSemantic, repeatedSemantic);
            }
        }

        foreach (NoitaWangTerrainSetDefinition set in catalog.Sets.Where(
                     static item => item.DecodedBitmapCaves is { IsEnabled: false }))
        {
            Assert.False(set.DecodedBitmapCaves!.TrySample(0, 0, WorldSeed, StableIdSalt(set.Id), out _));
        }

        DecodedNoitaBitmapCaves hot = catalog.FindDefinitionForReferenceBiome("vault").DecodedBitmapCaves!;
        ulong hotSalt = StableIdSalt("vault");
        for (int i = 0; i < 4_096; i++)
        {
            _ = hot.TrySample(i & 511, (i * 17L) & 255, WorldSeed, hotSalt, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int checksum = 0;
        for (int i = 0; i < 65_536; i++)
        {
            checksum += hot.TrySample(
                i & 511,
                (i * 31L) & 255,
                WorldSeed,
                hotSalt,
                out byte semantic)
                ? semantic + 1
                : 0;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        Assert.InRange(allocated, 0, 1_024);
    }

    /// <summary>
    /// 验证生成器确实叠加启用配置，而无 BitmapCaves 的 Fungal Caverns 不受影响。
    /// </summary>
    [Fact]
    public void GeneratorAppliesBitmapCavesOnlyToBoundReferenceBiomes()
    {
        CampaignConfig campaign = CampaignConfig.Load(new EngineScriptConfigApi(ContentRoot()));
        BiomeCatalog biomes = BiomeCatalog.Load(new EngineScriptConfigApi(ContentRoot()), campaign);
        IMaterialQuery materials = EngineContentLoader.LoadMaterialPackage(ContentRoot()).Materials;
        NoitaWangTerrainCatalog enabled = LoadCatalog();
        JsonObject disabledDocument = ParseObject(
            File.ReadAllText(Path.Combine(ContentRoot(), "noita-wang-terrain.json")));
        foreach (JsonNode? node in Assert.IsType<JsonArray>(disabledDocument["sets"]))
        {
            Assert.IsType<JsonObject>(node)["bitmapCaves"] = null;
        }

        NoitaWangTerrainCatalog disabled = NoitaWangTerrainCatalog.Parse(disabledDocument.ToJsonString());
        int changed = 0;
        foreach ((int chunkX, int chunkY) in new[] { (4, 13), (5, 13), (4, 14), (5, 14) })
        {
            ushort[] withBitmap = GenerateChunk(materials, campaign, biomes, enabled, chunkX, chunkY);
            ushort[] wangOnly = GenerateChunk(materials, campaign, biomes, disabled, chunkX, chunkY);
            for (int i = 0; i < withBitmap.Length; i++)
            {
                changed += withBitmap[i] == wangOnly[i] ? 0 : 1;
            }
        }

        Assert.True(changed > 0, "Coal Mine 参考区必须包含 BitmapCaves 对 Wang 基底的真实覆盖。");
        Assert.Equal("showcase-campaign-v15", PlayableCavernWorldGenerator.PersistenceKey);

        ushort[] fungalEnabled = GenerateChunk(materials, campaign, biomes, enabled, -54, 30);
        ushort[] fungalDisabled = GenerateChunk(materials, campaign, biomes, disabled, -54, 30);
        Assert.True(fungalEnabled.AsSpan().SequenceEqual(fungalDisabled));
    }

    /// <summary>
    /// 锁定煤矿完整 BitmapCaves block 的空气占比、原生逐像素碎边与贯通洞室，防止
    /// 把 Noita 的洞穴强度错译成缩略图块或抹平其 Wang 边缘细节。
    /// </summary>
    [Fact]
    public void CoalMineCavernScaleMatchesNativeReferenceEnvelope()
    {
        CampaignConfig campaign = CampaignConfig.Load(new EngineScriptConfigApi(ContentRoot()));
        BiomeCatalog biomes = BiomeCatalog.Load(new EngineScriptConfigApi(ContentRoot()), campaign);
        IMaterialQuery materials = EngineContentLoader.LoadMaterialPackage(ContentRoot()).Materials;
        NoitaWangTerrainCatalog wang = LoadCatalog();
        ushort empty = ResolveRequired(materials, "empty");
        const int blockWidth = 512;
        const int blockHeight = 256;
        const int originChunkY = 12;
        ushort[] block = new ushort[blockWidth * blockHeight];

        for (int chunkY = 0; chunkY < blockHeight / ChunkSize; chunkY++)
        {
            for (int chunkX = 0; chunkX < blockWidth / ChunkSize; chunkX++)
            {
                ushort[] chunk = GenerateChunk(
                    materials,
                    campaign,
                    biomes,
                    wang,
                    chunkX,
                    originChunkY + chunkY);
                for (int localY = 0; localY < ChunkSize; localY++)
                {
                    chunk.AsSpan(localY * ChunkSize, ChunkSize).CopyTo(
                        block.AsSpan(
                            (((chunkY * ChunkSize) + localY) * blockWidth) + (chunkX * ChunkSize),
                            ChunkSize));
                }
            }
        }

        List<int> horizontalRuns = [];
        int emptyCells = 0;
        for (int y = 0; y < blockHeight; y++)
        {
            int run = 0;
            for (int x = 0; x < blockWidth; x++)
            {
                if (block[(y * blockWidth) + x] == empty)
                {
                    emptyCells++;
                    run++;
                }
                else if (run > 0)
                {
                    horizontalRuns.Add(run);
                    run = 0;
                }
            }

            if (run > 0)
            {
                horizontalRuns.Add(run);
            }
        }

        horizontalRuns.Sort();
        int p75 = horizontalRuns[horizontalRuns.Count * 3 / 4];
        double emptyPercent = emptyCells * 100.0 / block.Length;
        Assert.InRange(emptyPercent, 55.0, 68.0);
        Assert.InRange(horizontalRuns.Count, 4_500, 6_000);
        Assert.InRange(p75, 10, 24);
        Assert.InRange(horizontalRuns[^1], 256, blockWidth);
    }

    private static ulong SampleSignature(
        DecodedNoitaBitmapCaves caves,
        ulong worldSeed,
        ulong salt,
        bool reverse,
        out int overrides)
    {
        int width = (caves.SizeX + 1) / 2;
        int height = (caves.SizeY + 1) / 2;
        byte[] samples = new byte[checked(width * height)];
        overrides = 0;
        int start = reverse ? samples.Length - 1 : 0;
        int end = reverse ? -1 : samples.Length;
        int step = reverse ? -1 : 1;
        for (int index = start; index != end; index += step)
        {
            int localX = index % width * 2;
            int localY = index / width * 2;
            bool overridden = caves.TrySample(localX, localY, worldSeed, salt, out byte semantic);
            samples[index] = overridden ? (byte)(semantic + 1) : (byte)0;
            overrides += overridden ? 1 : 0;
        }

        ulong signature = 14_695_981_039_346_656_037UL;
        for (int i = 0; i < samples.Length; i++)
        {
            signature ^= samples[i];
            signature *= 1_099_511_628_211UL;
        }

        return signature;
    }

    private static IEnumerable<(long X, long Y)> BoundaryCoordinates(int sizeX, int sizeY)
    {
        long[] x = [-sizeX - 1L, -sizeX, -1, 0, 1, sizeX - 1L, sizeX, sizeX + 1L];
        long[] y = [-sizeY - 1L, -sizeY, -1, 0, 1, sizeY - 1L, sizeY, sizeY + 1L];
        for (int i = 0; i < x.Length; i++)
        {
            yield return (x[i], y[(i * 5) & 7]);
        }
    }

    private static ushort[] GenerateChunk(
        IMaterialQuery materials,
        CampaignConfig campaign,
        BiomeCatalog biomes,
        NoitaWangTerrainCatalog wang,
        int chunkX,
        int chunkY)
    {
        ushort[] materialCells = new ushort[ChunkSize * ChunkSize];
        Half[] temperatures = new Half[TemperatureSize * TemperatureSize];
        PlayableCavernWorldGenerator.PopulateChunkForVerification(
            materials,
            chunkX,
            chunkY,
            materialCells,
            temperatures,
            campaign.InitialRunSeed,
            campaign,
            biomes,
            wang);
        return materialCells;
    }

    private static ushort ResolveRequired(IMaterialQuery materials, string name)
    {
        MaterialId id = materials.Resolve(name);
        Assert.True(id.IsValid, $"缺少材质 {name}。");
        return id.Value;
    }

    private static void AssertStructureMarker(
        NoitaWangTerrainSetDefinition set,
        byte semantic,
        string expectedColor,
        string expectedFunction)
    {
        Assert.True(DecodedNoitaWangTerrainSet.IsMarker(semantic));
        NoitaWangMarkerDefinition marker = set.Markers[semantic - NoitaWangTerrainCatalog.MarkerSemanticBase];
        Assert.Equal(expectedColor, marker.Color);
        Assert.Equal(expectedFunction, marker.Function);
    }

    private static void AssertStructureMaterial(
        NoitaWangTerrainSetDefinition set,
        byte semantic,
        string expectedMaterial)
    {
        Assert.True(DecodedNoitaWangTerrainSet.IsMaterial(semantic));
        NoitaWangMaterialMappingDefinition mapping =
            set.MaterialMappings[semantic - NoitaWangTerrainCatalog.MaterialSemanticBase];
        Assert.Equal(expectedMaterial, mapping.Material);
    }

    private static JsonObject FirstBitmapCaves(JsonObject document)
    {
        JsonArray sets = Assert.IsType<JsonArray>(document["sets"]);
        JsonObject first = Assert.IsType<JsonObject>(sets[0]);
        return Assert.IsType<JsonObject>(first["bitmapCaves"]);
    }

    private static JsonObject FirstStructure(JsonObject document)
    {
        JsonArray structures = Assert.IsType<JsonArray>(FirstBitmapCaves(document)["structures"]);
        return Assert.IsType<JsonObject>(structures[0]);
    }

    private static JsonObject FirstStructureVariant(JsonObject document)
    {
        JsonArray variants = Assert.IsType<JsonArray>(FirstStructure(document)["variants"]);
        return Assert.IsType<JsonObject>(variants[0]);
    }

    private static JsonObject ParseObject(string json)
    {
        return Assert.IsType<JsonObject>(JsonNode.Parse(json));
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
