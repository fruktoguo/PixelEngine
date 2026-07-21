using System.Text.Json.Nodes;
using PixelEngine.Hosting;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Noita 复刻战役配置与无限纵深地形契约测试。
/// 不变式：配置严格校验、八区七节点拓扑连续、chunk 只由全局坐标与 run seed 决定。
/// </summary>
public sealed class CampaignWorldTests
{
    private const int ChunkSize = 64;
    private const int TemperatureSize = 16;

    /// <summary>
    /// 验证正式 campaign.json 只能经公开 Config API 加载，并按 canonical 顺序声明 Noita 八个 biome。
    /// </summary>
    [Fact]
    public void CampaignConfigLoadsEightValidatedRegionsThroughPublicConfigApi()
    {
        CampaignConfig config = LoadConfig();
        IMaterialQuery materials = LoadMaterials();
        EngineScriptConfigApi configApi = new(ContentRoot());

        Assert.Equal(CampaignConfig.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Equal("campaign", config.DefaultMode);
        Assert.Equal(CampaignConfig.RequiredRegionCount, config.Regions.Length);
        Assert.Equal(
            CampaignConfig.RequiredRegionCount,
            config.Regions.Select(static region => region.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ["mines", "coal-pits", "snowy-depths", "hiisi-base", "underground-jungle", "the-vault", "temple-of-the-art", "the-laboratory"],
            config.Regions.Select(static region => region.Id));
        Assert.Equal(
            ["Mines", "Coal Pits", "Snowy Depths", "Hiisi Base", "Underground Jungle", "The Vault", "Temple of the Art", "The Laboratory"],
            config.Regions.Select(static region => region.DisplayName));
        Assert.True(materials.Resolve(config.HolyMountainShellMaterial).IsValid);
        Assert.True(materials.Resolve(config.HolyMountainPlatformMaterial).IsValid);

        string[] legacyIds =
        [
            "shattered-lode",
            "ember-fungal-reach",
            "frostfall-chasm",
            "ironworks-bastion",
            "rootsea-wilds",
            "reactive-vault",
            "null-temple",
            "origin-crucible",
        ];
        for (int i = 0; i < config.Regions.Length; i++)
        {
            CampaignRegionDefinition region = config.Regions[i];
            Assert.False(string.IsNullOrWhiteSpace(region.DisplayName));
            Assert.Equal([legacyIds[i]], region.LegacyIds);
            Assert.True(config.TryResolveRegionIndex(region.Id, out int canonicalIndex));
            Assert.Equal(i, canonicalIndex);
            Assert.True(config.TryResolveRegionIndex(legacyIds[i], out int legacyIndex));
            Assert.Equal(i, legacyIndex);
            Assert.True(materials.Resolve(region.RockMaterial).IsValid, $"区域 {region.Id} 缺少岩层材质。");
            Assert.True(materials.Resolve(region.LooseMaterial).IsValid, $"区域 {region.Id} 缺少松散材质。");
            Assert.True(materials.Resolve(region.HazardMaterial).IsValid, $"区域 {region.Id} 缺少危险材质。");
        }

        Assert.False(config.TryResolveRegionIndex("unknown-biome", out int missingIndex));
        Assert.Equal(-1, missingIndex);

        AssertEquivalent(config, CampaignConfig.BuiltinDefault);
        PlayableCavernWorldGenerator generator = new();
        ProceduralWorldBuildRequest initialRequest = new(
            PlayableCavernWorldGenerator.Key,
            materials,
            Config: configApi);
        Assert.Equal(config.InitialRunSeed, generator.Describe(in initialRequest).WorldSeed);
        ProceduralWorldBuildRequest overrideRequest = new(
            PlayableCavernWorldGenerator.Key,
            materials,
            WorldSeedOverride: 42,
            Config: configApi);
        Assert.Equal(42UL, generator.Describe(in overrideRequest).WorldSeed);
    }

    /// <summary>
    /// 验证转向前 v1 配置会升级为 canonical biome/Holy Mountain 合同，旧 id 只作为读取 alias 保留。
    /// </summary>
    [Fact]
    public void CampaignConfigMigratesLegacyV1RegionAndForgeIdentifiers()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PixelEngine.CampaignConfig", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        try
        {
            JsonObject legacy = ParseObject(File.ReadAllText(Path.Combine(ContentRoot(), "campaign.json")));
            legacy["schemaVersion"] = 1;
            MoveProperty(legacy, "holyMountainHeightCells", "forgeHeightCells");
            MoveProperty(legacy, "holyMountainHalfWidthCells", "forgeHalfWidthCells");
            MoveProperty(legacy, "holyMountainShellMaterial", "forgeShellMaterial");
            MoveProperty(legacy, "holyMountainPlatformMaterial", "forgePlatformMaterial");

            string[] legacyIds =
            [
                "shattered-lode",
                "ember-fungal-reach",
                "frostfall-chasm",
                "ironworks-bastion",
                "rootsea-wilds",
                "reactive-vault",
                "null-temple",
                "origin-crucible",
            ];
            JsonArray regions = Assert.IsType<JsonArray>(legacy["regions"]);
            for (int i = 0; i < regions.Count; i++)
            {
                JsonObject region = Assert.IsType<JsonObject>(regions[i]);
                region["id"] = legacyIds[i];
                region["displayName"] = $"legacy-{i}";
                _ = region.Remove("legacyIds");
            }

            File.WriteAllText(Path.Combine(tempRoot, "campaign.json"), legacy.ToJsonString());
            CampaignConfig migrated = CampaignConfig.Load(new EngineScriptConfigApi(tempRoot));

            AssertEquivalent(migrated, CampaignConfig.BuiltinDefault);
            for (int i = 0; i < legacyIds.Length; i++)
            {
                Assert.True(migrated.TryResolveRegionIndex(legacyIds[i], out int resolved));
                Assert.Equal(i, resolved);
            }
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证错误 schema、区域数量和 null 区域表都会在装配世界前以明确数据错误拒绝。
    /// </summary>
    [Fact]
    public void CampaignConfigRejectsInvalidSchemaAndTopology()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PixelEngine.CampaignConfig", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        try
        {
            string source = File.ReadAllText(Path.Combine(ContentRoot(), "campaign.json"));
            string target = Path.Combine(tempRoot, "campaign.json");
            EngineScriptConfigApi configApi = new(tempRoot);

            JsonObject wrongSchema = ParseObject(source);
            wrongSchema["schemaVersion"] = CampaignConfig.CurrentSchemaVersion + 1;
            File.WriteAllText(target, wrongSchema.ToJsonString());
            InvalidDataException schemaError = Assert.Throws<InvalidDataException>(() => CampaignConfig.Load(configApi));
            Assert.Contains("schemaVersion", schemaError.Message, StringComparison.Ordinal);

            JsonObject missingRegion = ParseObject(source);
            JsonArray regions = Assert.IsType<JsonArray>(missingRegion["regions"]);
            regions.RemoveAt(regions.Count - 1);
            File.WriteAllText(target, missingRegion.ToJsonString());
            InvalidDataException countError = Assert.Throws<InvalidDataException>(() => CampaignConfig.Load(configApi));
            Assert.Contains("恰好包含 8", countError.Message, StringComparison.Ordinal);

            JsonObject nullRegions = ParseObject(source);
            nullRegions["regions"] = null;
            File.WriteAllText(target, nullRegions.ToJsonString());
            InvalidDataException nullError = Assert.Throws<InvalidDataException>(() => CampaignConfig.Load(configApi));
            Assert.Contains("regions 不能为空", nullError.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证八个纵深区域由一条逐 cell 连续的主通道连接，七个 Holy Mountain 具备房间、边界和平台实体地形。
    /// </summary>
    [Fact]
    public void CampaignTopologyConnectsEightRegionsThroughSevenHolyMountains()
    {
        CampaignConfig config = LoadConfig();
        IMaterialQuery materials = LoadMaterials();
        TerrainProbe probe = new(materials, config, config.InitialRunSeed);
        ushort empty = ResolveRequired(materials, "empty");
        ushort holyMountainShell = ResolveRequired(materials, config.HolyMountainShellMaterial);
        ushort holyMountainPlatform = ResolveRequired(materials, config.HolyMountainPlatformMaterial);

        long campaignBottom = RegionStart(config, CampaignConfig.RequiredRegionCount - 1) + config.RegionHeightCells;
        for (long worldY = config.SurfaceY; worldY <= campaignBottom; worldY += 31)
        {
            long pathX = PlayableCavernWorldGenerator.MainPathCenterX(worldY, config, config.InitialRunSeed);
            Assert.Equal(empty, probe.MaterialAt(pathX, worldY));
        }

        for (int regionIndex = 0; regionIndex < CampaignConfig.RequiredRegionCount; regionIndex++)
        {
            long regionY = RegionStart(config, regionIndex) + (config.RegionHeightCells / 2);
            CampaignDepthLocation location = config.ResolveLocation(regionY);
            Assert.Equal(CampaignDepthKind.Region, location.Kind);
            Assert.Equal(regionIndex, location.RegionIndex);

            long sideX = 4_096L + (regionIndex * 512L);
            ChunkSample sideChunk = probe.ChunkAt(sideX, regionY);
            CampaignRegionDefinition region = config.Regions[regionIndex];
            ushort rock = ResolveRequired(materials, region.RockMaterial);
            ushort loose = ResolveRequired(materials, region.LooseMaterial);
            ushort hazard = ResolveRequired(materials, region.HazardMaterial);
            int signatureCells = CountAny(sideChunk.Materials, rock, loose, hazard);
            Assert.True(signatureCells >= 256, $"区域 {region.Id} 缺少可辨识的材质地层，cells={signatureCells}。");

            int hazardCells = 0;
            for (int sampleIndex = 0; sampleIndex < 6; sampleIndex++)
            {
                long hazardX = 8_192L + (regionIndex * 4_096L) + (sampleIndex * 257L);
                long hazardY = regionY + ((sampleIndex - 2L) * ChunkSize);
                hazardCells += Count(sideChunk: probe.ChunkAt(hazardX, hazardY).Materials, hazard);
            }

            Assert.True(hazardCells > 0, $"区域 {region.Id} 必须实际生成危险材质 {region.HazardMaterial}。");

            long pathX = PlayableCavernWorldGenerator.MainPathCenterX(regionY, config, config.InitialRunSeed);
            Assert.Equal((float)(Half)region.BaseTemperature, probe.TemperatureAt(pathX, regionY));

            if (regionIndex == CampaignConfig.RequiredRegionCount - 1)
            {
                continue;
            }

            long holyMountainStart = HolyMountainStart(config, regionIndex);
            long roomY = holyMountainStart + 32;
            long roomCenterX = PlayableCavernWorldGenerator.MainPathCenterX(roomY, config, config.InitialRunSeed);
            CampaignDepthLocation holyMountainLocation = config.ResolveLocation(roomY);
            Assert.Equal(CampaignDepthKind.HolyMountain, holyMountainLocation.Kind);
            Assert.Equal(regionIndex, holyMountainLocation.HolyMountainIndex);
            Assert.Equal(empty, probe.MaterialAt(roomCenterX, roomY));
            Assert.Equal(holyMountainShell, probe.MaterialAt(roomCenterX + config.HolyMountainHalfWidthCells, roomY));

            long platformY = holyMountainStart + 62;
            long platformCenterX = PlayableCavernWorldGenerator.MainPathCenterX(platformY, config, config.InitialRunSeed);
            long platformX = platformCenterX + config.MainPathHalfWidthCells + 24;
            Assert.Equal(holyMountainPlatform, probe.MaterialAt(platformX, platformY));
            Assert.Equal(20f, probe.TemperatureAt(platformCenterX, platformY));
        }

        CampaignDepthLocation unboundedFinalRegion = config.ResolveLocation(campaignBottom + 100_000);
        Assert.Equal(CampaignDepthKind.Region, unboundedFinalRegion.Kind);
        Assert.Equal(CampaignConfig.RequiredRegionCount - 1, unboundedFinalRegion.RegionIndex);
    }

    /// <summary>
    /// 验证正负 chunk 以任意次序生成仍逐 cell 相同，而不同 run seed 会生成不同地貌。
    /// </summary>
    [Fact]
    public void CampaignChunksAreLoadOrderIndependentAndSeedSensitiveAcrossNegativeCoordinates()
    {
        CampaignConfig config = LoadConfig();
        IMaterialQuery materials = LoadMaterials();
        (int X, int Y)[] coordinates = [(-13, 4), (11, 10), (-7, 40), (5, 77)];
        Dictionary<(int X, int Y), ChunkSample> forward = [];
        Dictionary<(int X, int Y), ChunkSample> reverse = [];

        foreach ((int x, int y) in coordinates)
        {
            forward.Add((x, y), GenerateChunk(materials, config, config.InitialRunSeed, x, y));
        }

        for (int i = coordinates.Length - 1; i >= 0; i--)
        {
            (int x, int y) = coordinates[i];
            reverse.Add((x, y), GenerateChunk(materials, config, config.InitialRunSeed, x, y));
        }

        bool differentSeedChangedTerrain = false;
        foreach ((int x, int y) in coordinates)
        {
            ChunkSample expected = forward[(x, y)];
            ChunkSample reordered = reverse[(x, y)];
            Assert.True(expected.Materials.AsSpan().SequenceEqual(reordered.Materials));
            Assert.True(expected.Temperatures.AsSpan().SequenceEqual(reordered.Temperatures));

            ChunkSample otherSeed = GenerateChunk(
                materials,
                config,
                config.InitialRunSeed ^ 0x9E37_79B9_7F4A_7C15UL,
                x,
                y);
            differentSeedChangedTerrain |= !expected.Materials.AsSpan().SequenceEqual(otherSeed.Materials);
        }

        Assert.True(differentSeedChangedTerrain, "不同 run seed 必须改变至少一个已采样 chunk 的地形。");
    }

    private static ChunkSample GenerateChunk(
        IMaterialQuery materials,
        CampaignConfig config,
        ulong worldSeed,
        int chunkX,
        int chunkY)
    {
        ushort[] materialCells = new ushort[ChunkSize * ChunkSize];
        Half[] temperatureCells = new Half[TemperatureSize * TemperatureSize];
        PlayableCavernWorldGenerator.PopulateChunkForVerification(
            materials,
            chunkX,
            chunkY,
            materialCells,
            temperatureCells,
            worldSeed,
            config);
        return new ChunkSample(materialCells, temperatureCells);
    }

    private static int CountAny(ReadOnlySpan<ushort> materials, ushort first, ushort second, ushort third)
    {
        int count = 0;
        for (int i = 0; i < materials.Length; i++)
        {
            ushort material = materials[i];
            count += material == first || material == second || material == third ? 1 : 0;
        }

        return count;
    }

    private static int Count(ReadOnlySpan<ushort> sideChunk, ushort expected)
    {
        int count = 0;
        for (int i = 0; i < sideChunk.Length; i++)
        {
            count += sideChunk[i] == expected ? 1 : 0;
        }

        return count;
    }

    private static void AssertEquivalent(CampaignConfig actual, CampaignConfig expected)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.DefaultMode, actual.DefaultMode);
        Assert.Equal(expected.InitialRunSeed, actual.InitialRunSeed);
        Assert.Equal(expected.SurfaceY, actual.SurfaceY);
        Assert.Equal(expected.CampaignStartDepthCells, actual.CampaignStartDepthCells);
        Assert.Equal(expected.RegionHeightCells, actual.RegionHeightCells);
        Assert.Equal(expected.HolyMountainHeightCells, actual.HolyMountainHeightCells);
        Assert.Equal(expected.MainPathHalfWidthCells, actual.MainPathHalfWidthCells);
        Assert.Equal(expected.MainPathEntranceX, actual.MainPathEntranceX);
        Assert.Equal(expected.MainPathWanderCells, actual.MainPathWanderCells);
        Assert.Equal(expected.HolyMountainHalfWidthCells, actual.HolyMountainHalfWidthCells);
        Assert.Equal(expected.HolyMountainShellMaterial, actual.HolyMountainShellMaterial);
        Assert.Equal(expected.HolyMountainPlatformMaterial, actual.HolyMountainPlatformMaterial);
        Assert.Equal(expected.Regions.Length, actual.Regions.Length);
        for (int i = 0; i < expected.Regions.Length; i++)
        {
            CampaignRegionDefinition actualRegion = actual.Regions[i];
            CampaignRegionDefinition expectedRegion = expected.Regions[i];
            Assert.Equal(expectedRegion.Id, actualRegion.Id);
            Assert.Equal(expectedRegion.DisplayName, actualRegion.DisplayName);
            Assert.Equal(expectedRegion.LegacyIds, actualRegion.LegacyIds);
            Assert.Equal(expectedRegion.RockMaterial, actualRegion.RockMaterial);
            Assert.Equal(expectedRegion.LooseMaterial, actualRegion.LooseMaterial);
            Assert.Equal(expectedRegion.HazardMaterial, actualRegion.HazardMaterial);
            Assert.Equal(expectedRegion.HazardFrequency, actualRegion.HazardFrequency);
            Assert.Equal(expectedRegion.BaseTemperature, actualRegion.BaseTemperature);
        }
    }

    private static long RegionStart(CampaignConfig config, int regionIndex)
    {
        return config.SurfaceY +
            config.CampaignStartDepthCells +
            ((long)regionIndex * (config.RegionHeightCells + config.HolyMountainHeightCells));
    }

    private static long HolyMountainStart(CampaignConfig config, int holyMountainIndex)
    {
        return RegionStart(config, holyMountainIndex) + config.RegionHeightCells;
    }

    private static void MoveProperty(JsonObject source, string oldName, string newName)
    {
        JsonNode? value = source[oldName]?.DeepClone();
        Assert.NotNull(value);
        source[newName] = value;
        Assert.True(source.Remove(oldName));
    }

    private static ushort ResolveRequired(IMaterialQuery materials, string name)
    {
        MaterialId id = materials.Resolve(name);
        Assert.True(id.IsValid, $"缺少材质 {name}。");
        return id.Value;
    }

    private static CampaignConfig LoadConfig()
    {
        return CampaignConfig.Load(new EngineScriptConfigApi(ContentRoot()));
    }

    private static IMaterialQuery LoadMaterials()
    {
        return EngineContentLoader.LoadMaterialPackage(ContentRoot()).Materials;
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

    private sealed class TerrainProbe(IMaterialQuery materials, CampaignConfig config, ulong worldSeed)
    {
        private readonly Dictionary<(int X, int Y), ChunkSample> _chunks = [];

        public ushort MaterialAt(long worldX, long worldY)
        {
            (ChunkSample chunk, int localX, int localY) = Resolve(worldX, worldY);
            return chunk.Materials[(localY * ChunkSize) + localX];
        }

        public float TemperatureAt(long worldX, long worldY)
        {
            (ChunkSample chunk, int localX, int localY) = Resolve(worldX, worldY);
            int temperatureX = localX / (ChunkSize / TemperatureSize);
            int temperatureY = localY / (ChunkSize / TemperatureSize);
            return (float)chunk.Temperatures[(temperatureY * TemperatureSize) + temperatureX];
        }

        public ChunkSample ChunkAt(long worldX, long worldY)
        {
            int chunkX = FloorChunk(worldX);
            int chunkY = FloorChunk(worldY);
            return GetOrCreate(chunkX, chunkY);
        }

        private (ChunkSample Chunk, int LocalX, int LocalY) Resolve(long worldX, long worldY)
        {
            int chunkX = FloorChunk(worldX);
            int chunkY = FloorChunk(worldY);
            int localX = checked((int)(worldX - ((long)chunkX * ChunkSize)));
            int localY = checked((int)(worldY - ((long)chunkY * ChunkSize)));
            return (GetOrCreate(chunkX, chunkY), localX, localY);
        }

        private ChunkSample GetOrCreate(int chunkX, int chunkY)
        {
            if (!_chunks.TryGetValue((chunkX, chunkY), out ChunkSample? chunk))
            {
                chunk = GenerateChunk(materials, config, worldSeed, chunkX, chunkY);
                _chunks.Add((chunkX, chunkY), chunk);
            }

            return chunk;
        }

        private static int FloorChunk(long coordinate)
        {
            long quotient = Math.DivRem(coordinate, ChunkSize, out long remainder);
            return checked((int)(remainder < 0 ? quotient - 1 : quotient));
        }
    }

    private sealed record ChunkSample(ushort[] Materials, Half[] Temperatures);
}
