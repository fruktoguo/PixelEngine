using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
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
        BiomeCatalog biomes = LoadBiomes(config);
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
        Assert.Equal(
            [0, 1_536, 3_072, 5_120, 6_656, 8_704, 10_752, 13_312],
            config.Regions.Select(static region => region.StartDepthCells));
        Assert.Equal(
            [1_024, 1_024, 1_536, 1_024, 1_536, 1_536, 2_048, 1_600],
            config.Regions.Select(static region => region.HeightCells));
        Assert.Equal(512, config.HolyMountainHeightCells);
        Assert.Equal(14_912, config.CampaignEndDepthCells);
        Assert.True(materials.Resolve(biomes.HolyMountain.ShellMaterial).IsValid);
        Assert.True(materials.Resolve(biomes.HolyMountain.PlatformMaterial).IsValid);
        Assert.True(materials.Resolve(biomes.PortalNetwork.TeleportatiumMaterial).IsValid);
        Assert.Equal(BiomeCatalog.CurrentSchemaVersion, biomes.SchemaVersion);
        Assert.Equal(CampaignConfig.RequiredRegionCount, biomes.MainPath.Length);
        Assert.Equal(3, biomes.SideBiomes.Length);
        Assert.Equal(11, biomes.PixelScenes.Length);
        Assert.Equal(12, biomes.Landmarks.Length);
        Assert.Equal(6, biomes.Connections.Length);
        Assert.Equal("active", biomes.ExpansionStages.SideBiomes);
        Assert.Equal("active", biomes.ExpansionStages.SecretConnections);
        Assert.Equal("planned", biomes.ExpansionStages.ParallelWorlds);
        Assert.Equal("planned", biomes.ExpansionStages.NewGamePlus);
        Assert.Equal(8, biomes.HolyMountain.Landmarks.Length);
        Assert.Equal(16, biomes.HolyMountain.LayoutOperations.Length);
        Assert.Equal(3, biomes.PortalNetwork.PortalsPerHolyMountain);
        Assert.Equal("teleportatium", biomes.PortalNetwork.TeleportatiumMaterial);
        JsonObject campaignDocument = ParseObject(File.ReadAllText(Path.Combine(ContentRoot(), "campaign.json")));
        Assert.False(campaignDocument.ContainsKey("holyMountainShellMaterial"));
        Assert.False(campaignDocument.ContainsKey("holyMountainPlatformMaterial"));

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
            BiomeDefinition biome = biomes.MainPath[i];
            Assert.Equal(region.Id, biome.Id);
            Assert.Equal(region.DisplayName, biome.DisplayName);
            Assert.True(materials.Resolve(biome.Palette.Primary).IsValid, $"区域 {region.Id} 缺少 primary 材质。");
            Assert.True(materials.Resolve(biome.Palette.Secondary).IsValid, $"区域 {region.Id} 缺少 secondary 材质。");
            Assert.True(materials.Resolve(biome.Palette.Loose).IsValid, $"区域 {region.Id} 缺少 loose 材质。");
            Assert.True(materials.Resolve(biome.Palette.Structure).IsValid, $"区域 {region.Id} 缺少 structure 材质。");
            Assert.True(materials.Resolve(biome.Palette.Hazard).IsValid, $"区域 {region.Id} 缺少 hazard 材质。");
            Assert.True(materials.Resolve(biome.Palette.Pool).IsValid, $"区域 {region.Id} 缺少 pool 材质。");
            Assert.InRange(
                biomes.Landmarks.Count(landmark => string.Equals(landmark.Biome, region.Id, StringComparison.Ordinal)),
                1,
                4);
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
            legacy["forgeShellMaterial"] = "boundary_stone";
            legacy["forgePlatformMaterial"] = "metal";

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
            BiomeCatalog biomes = LoadBiomes(LoadConfig());
            for (int i = 0; i < regions.Count; i++)
            {
                JsonObject region = Assert.IsType<JsonObject>(regions[i]);
                region["id"] = legacyIds[i];
                region["displayName"] = $"legacy-{i}";
                _ = region.Remove("legacyIds");
                BiomeDefinition biome = biomes.MainPath[i];
                region["rockMaterial"] = biome.Palette.Primary;
                region["looseMaterial"] = biome.Palette.Loose;
                region["hazardMaterial"] = biome.Palette.Hazard;
                region["hazardFrequency"] = biome.HazardFrequency;
                region["baseTemperature"] = biome.BaseTemperature;
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
    /// 验证 v2 中与区域身份混存的地形字段会被完整消费后迁移，v3 只保留 run/topology 权威数据。
    /// </summary>
    [Fact]
    public void CampaignConfigMigratesLegacyV2TerrainFields()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PixelEngine.CampaignConfig", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        try
        {
            JsonObject legacy = ParseObject(File.ReadAllText(Path.Combine(ContentRoot(), "campaign.json")));
            legacy["schemaVersion"] = 2;
            legacy["holyMountainShellMaterial"] = "boundary_stone";
            legacy["holyMountainPlatformMaterial"] = "metal";
            BiomeCatalog biomes = LoadBiomes(LoadConfig());
            JsonArray regions = Assert.IsType<JsonArray>(legacy["regions"]);
            for (int i = 0; i < regions.Count; i++)
            {
                JsonObject region = Assert.IsType<JsonObject>(regions[i]);
                BiomeDefinition biome = biomes.MainPath[i];
                region["rockMaterial"] = biome.Palette.Primary;
                region["looseMaterial"] = biome.Palette.Loose;
                region["hazardMaterial"] = biome.Palette.Hazard;
                region["hazardFrequency"] = biome.HazardFrequency;
                region["baseTemperature"] = biome.BaseTemperature;
            }

            File.WriteAllText(Path.Combine(tempRoot, "campaign.json"), legacy.ToJsonString());
            CampaignConfig migrated = CampaignConfig.Load(new EngineScriptConfigApi(tempRoot));

            AssertEquivalent(migrated, CampaignConfig.BuiltinDefault);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证首个 biomes 拆分节点的 v3 配置会继续迁移 Holy Mountain 材质，v4 不再保留地形权威。
    /// </summary>
    [Fact]
    public void CampaignConfigMigratesV3HolyMountainTerrainFields()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PixelEngine.CampaignConfig", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        try
        {
            JsonObject legacy = ParseObject(File.ReadAllText(Path.Combine(ContentRoot(), "campaign.json")));
            legacy["schemaVersion"] = 3;
            legacy["holyMountainShellMaterial"] = "boundary_stone";
            legacy["holyMountainPlatformMaterial"] = "metal";
            File.WriteAllText(Path.Combine(tempRoot, "campaign.json"), legacy.ToJsonString());

            CampaignConfig migrated = CampaignConfig.Load(new EngineScriptConfigApi(tempRoot));

            AssertEquivalent(migrated, CampaignConfig.BuiltinDefault);
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
    /// 验证 biomes.json 对未知字段、越界 pixel-scene、坏连接和不存在材质均 fail-closed。
    /// </summary>
    [Fact]
    public void BiomeCatalogRejectsUnknownFieldsInvalidScenesConnectionsAndMaterials()
    {
        CampaignConfig campaign = LoadConfig();
        string source = File.ReadAllText(Path.Combine(ContentRoot(), "biomes.json"));

        JsonObject unknownField = ParseObject(source);
        unknownField["unmappedField"] = true;
        InvalidDataException unknownError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(unknownField.ToJsonString(), campaign));
        _ = Assert.IsType<System.Text.Json.JsonException>(unknownError.InnerException);

        JsonObject invalidTopologySize = ParseObject(source);
        Assert.IsType<JsonObject>(invalidTopologySize["worldTopology"])["width"] = 69;
        InvalidDataException topologySizeError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidTopologySize.ToJsonString(), campaign));
        Assert.Contains("70x48", topologySizeError.Message, StringComparison.Ordinal);

        JsonObject invalidTopologyRow = ParseObject(source);
        JsonObject invalidTopology = Assert.IsType<JsonObject>(invalidTopologyRow["worldTopology"]);
        JsonArray invalidRows = Assert.IsType<JsonArray>(invalidTopology["macroRows"]);
        invalidRows[0] = "00";
        InvalidDataException topologyRowError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidTopologyRow.ToJsonString(), campaign));
        Assert.Contains("140 个 hex 字符", topologyRowError.Message, StringComparison.Ordinal);

        JsonObject invalidTopologyIndex = ParseObject(source);
        JsonObject indexTopology = Assert.IsType<JsonObject>(invalidTopologyIndex["worldTopology"]);
        JsonArray indexRows = Assert.IsType<JsonArray>(indexTopology["macroRows"]);
        JsonNode firstRowNode = indexRows[0] ?? throw new Xunit.Sdk.XunitException("macroRows[0] 不能为空。");
        string firstRow = firstRowNode.GetValue<string>();
        indexRows[0] = "ff" + firstRow[2..];
        InvalidDataException topologyIndexError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidTopologyIndex.ToJsonString(), campaign));
        Assert.Contains("越界 biome index", topologyIndexError.Message, StringComparison.Ordinal);

        JsonObject invalidReferenceMask = ParseObject(source);
        JsonObject maskTopology = Assert.IsType<JsonObject>(invalidReferenceMask["worldTopology"]);
        JsonArray referenceBiomes = Assert.IsType<JsonArray>(maskTopology["referenceBiomes"]);
        JsonObject leftEntrance = referenceBiomes
            .Select(static node => Assert.IsType<JsonObject>(node))
            .Single(static biome => string.Equals(
                biome["id"]?.GetValue<string>(),
                "mountain-left-entrance",
                StringComparison.Ordinal));
        Assert.IsType<JsonObject>(leftEntrance["referenceTerrainMask"])["data"] = "not-base64";
        InvalidDataException referenceMaskError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidReferenceMask.ToJsonString(), campaign));
        Assert.Contains("不是合法 Base64", referenceMaskError.Message, StringComparison.Ordinal);

        JsonObject invalidLaboratoryMask = ParseObject(source);
        JsonObject laboratoryMaskTopology = Assert.IsType<JsonObject>(invalidLaboratoryMask["worldTopology"]);
        JsonObject fixedLaboratory = Assert.IsType<JsonObject>(laboratoryMaskTopology["fixedLaboratory"]);
        Assert.IsType<JsonObject>(fixedLaboratory["referenceTerrainMask"])["decodedSha256"] = new string('0', 64);
        InvalidDataException laboratoryMaskError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidLaboratoryMask.ToJsonString(), campaign));
        Assert.Contains("decodedSha256 与解码内容不一致", laboratoryMaskError.Message, StringComparison.Ordinal);

        JsonObject invalidScene = ParseObject(source);
        JsonArray scenes = Assert.IsType<JsonArray>(invalidScene["pixelScenes"]);
        JsonObject scene = Assert.IsType<JsonObject>(scenes[0]);
        JsonArray operations = Assert.IsType<JsonArray>(scene["operations"]);
        JsonObject firstOperation = Assert.IsType<JsonObject>(operations[0]);
        firstOperation["width"] = 4_096;
        InvalidDataException sceneError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidScene.ToJsonString(), campaign));
        Assert.Contains("超出 scene 宽度", sceneError.Message, StringComparison.Ordinal);

        JsonObject invalidLandmark = ParseObject(source);
        JsonArray landmarks = Assert.IsType<JsonArray>(invalidLandmark["landmarks"]);
        JsonObject landmark = Assert.IsType<JsonObject>(landmarks[0]);
        JsonArray landmarkOperations = Assert.IsType<JsonArray>(landmark["operations"]);
        Assert.IsType<JsonObject>(landmarkOperations[0])["width"] = 4_096;
        InvalidDataException landmarkBoundsError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidLandmark.ToJsonString(), campaign));
        Assert.Contains("超出 landmark 宽度", landmarkBoundsError.Message, StringComparison.Ordinal);

        JsonObject invalidLandmarkOperation = ParseObject(source);
        JsonArray invalidOperationLandmarks = Assert.IsType<JsonArray>(invalidLandmarkOperation["landmarks"]);
        JsonObject invalidOperationLandmark = Assert.IsType<JsonObject>(invalidOperationLandmarks[0]);
        JsonArray invalidLandmarkOperations = Assert.IsType<JsonArray>(invalidOperationLandmark["operations"]);
        Assert.IsType<JsonObject>(invalidLandmarkOperations[0])["kind"] = "unsupported";
        InvalidDataException landmarkOperationError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidLandmarkOperation.ToJsonString(), campaign));
        Assert.Contains("kind 不受支持", landmarkOperationError.Message, StringComparison.Ordinal);

        JsonObject invalidLandmarkCarve = ParseObject(source);
        JsonArray invalidCarveLandmarks = Assert.IsType<JsonArray>(invalidLandmarkCarve["landmarks"]);
        JsonObject invalidCarveLandmark = Assert.IsType<JsonObject>(invalidCarveLandmarks[0]);
        JsonArray invalidCarveOperations = Assert.IsType<JsonArray>(invalidCarveLandmark["operations"]);
        JsonObject invalidCarveOperation = Assert.IsType<JsonObject>(invalidCarveOperations[0]);
        invalidCarveOperation["kind"] = "carveRect";
        invalidCarveOperation["material"] = "stone";
        InvalidDataException landmarkCarveError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidLandmarkCarve.ToJsonString(), campaign));
        Assert.Contains("carve 操作必须使用 empty", landmarkCarveError.Message, StringComparison.Ordinal);

        JsonObject invalidConnection = ParseObject(source);
        JsonArray connections = Assert.IsType<JsonArray>(invalidConnection["connections"]);
        Assert.IsType<JsonObject>(connections[0])["to"] = "missing-side-biome";
        InvalidDataException connectionError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidConnection.ToJsonString(), campaign));
        Assert.Contains("to 必须引用侧区 biome", connectionError.Message, StringComparison.Ordinal);

        JsonObject excessiveConnections = ParseObject(source);
        JsonArray excessiveConnectionArray = Assert.IsType<JsonArray>(excessiveConnections["connections"]);
        for (int i = 0; i < 3; i++)
        {
            JsonObject clone = Assert.IsType<JsonObject>(excessiveConnectionArray[i]!.DeepClone());
            clone["id"] = $"capacity-overflow-{i}";
            excessiveConnectionArray.Add(clone);
        }

        InvalidDataException capacityError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(excessiveConnections.ToJsonString(), campaign));
        Assert.Contains("最多允许八项", capacityError.Message, StringComparison.Ordinal);

        JsonObject invalidHolyMountain = ParseObject(source);
        JsonObject holyMountain = Assert.IsType<JsonObject>(invalidHolyMountain["holyMountain"]);
        JsonArray layoutOperations = Assert.IsType<JsonArray>(holyMountain["layoutOperations"]);
        Assert.IsType<JsonObject>(layoutOperations[0])["x"] = 4_096;
        InvalidDataException holyMountainError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidHolyMountain.ToJsonString(), campaign));
        Assert.Contains("超出 Holy Mountain 横向布局", holyMountainError.Message, StringComparison.Ordinal);

        JsonObject invalidPortal = ParseObject(source);
        JsonObject portal = Assert.IsType<JsonObject>(invalidPortal["portalNetwork"]);
        portal["minimumPowerCells"] = 4_096;
        InvalidDataException portalError = Assert.Throws<InvalidDataException>(
            () => BiomeCatalog.Parse(invalidPortal.ToJsonString(), campaign));
        Assert.Contains("供能池可采样容量", portalError.Message, StringComparison.Ordinal);

        JsonObject invalidMaterial = ParseObject(source);
        JsonArray mainPath = Assert.IsType<JsonArray>(invalidMaterial["mainPath"]);
        JsonObject firstBiome = Assert.IsType<JsonObject>(mainPath[0]);
        Assert.IsType<JsonObject>(firstBiome["palette"])["primary"] = "missing-material";
        BiomeCatalog catalog = BiomeCatalog.Parse(invalidMaterial.ToJsonString(), campaign);
        ushort[] materialCells = new ushort[ChunkSize * ChunkSize];
        Half[] temperatureCells = new Half[TemperatureSize * TemperatureSize];
        InvalidOperationException materialError = Assert.Throws<InvalidOperationException>(
            () => PlayableCavernWorldGenerator.PopulateChunkForVerification(
                LoadMaterials(),
                0,
                4,
                materialCells,
                temperatureCells,
                campaign.InitialRunSeed,
                campaign,
                catalog));
        Assert.Contains("missing-material", materialError.Message, StringComparison.Ordinal);

        JsonObject invalidLandmarkMaterial = ParseObject(source);
        JsonArray missingMaterialLandmarks = Assert.IsType<JsonArray>(invalidLandmarkMaterial["landmarks"]);
        JsonObject missingMaterialLandmark = Assert.IsType<JsonObject>(missingMaterialLandmarks[0]);
        JsonArray missingMaterialOperations = Assert.IsType<JsonArray>(missingMaterialLandmark["operations"]);
        Assert.IsType<JsonObject>(missingMaterialOperations[0])["material"] = "missing-landmark-material";
        BiomeCatalog invalidLandmarkMaterialCatalog = BiomeCatalog.Parse(
            invalidLandmarkMaterial.ToJsonString(),
            campaign);
        InvalidOperationException landmarkMaterialError = Assert.Throws<InvalidOperationException>(
            () => PlayableCavernWorldGenerator.PopulateChunkForVerification(
                LoadMaterials(),
                0,
                4,
                materialCells,
                temperatureCells,
                campaign.InitialRunSeed,
                campaign,
                invalidLandmarkMaterialCatalog));
        Assert.Contains("missing-landmark-material", landmarkMaterialError.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证七个程序化主路径 biome 都能在固定容量 Span 中产生同 seed 相同的 authored encounter room，
    /// 且锚点对应的最高层 pixel-scene operation 确实写入世界；固定 Laboratory 由独立场景契约覆盖。
    /// </summary>
    [Fact]
    public void BiomePixelScenesProduceDeterministicBoundedEncounterAnchors()
    {
        CampaignConfig campaign = LoadConfig();
        BiomeCatalog catalog = LoadBiomes(campaign);
        IMaterialQuery materials = LoadMaterials();
        TerrainProbe probe = new(materials, campaign, catalog, campaign.InitialRunSeed);
        BiomeEncounterAnchor[] first = new BiomeEncounterAnchor[64];
        BiomeEncounterAnchor[] repeated = new BiomeEncounterAnchor[64];

        for (int regionIndex = 0; regionIndex < CampaignConfig.RequiredRegionCount - 1; regionIndex++)
        {
            long minimumY = campaign.RegionStartCellY(regionIndex);
            long maximumY = minimumY + campaign.RegionHeightCellsAt(regionIndex) - 1L;
            int count = PlayableCavernWorldGenerator.CollectEncounterAnchors(
                catalog,
                campaign,
                regionIndex,
                campaign.InitialRunSeed,
                -4_096,
                minimumY,
                4_096,
                maximumY,
                first);
            int repeatedCount = PlayableCavernWorldGenerator.CollectEncounterAnchors(
                catalog,
                campaign,
                regionIndex,
                campaign.InitialRunSeed,
                -4_096,
                minimumY,
                4_096,
                maximumY,
                repeated);

            Assert.InRange(count, 1, first.Length);
            Assert.Equal(count, repeatedCount);
            Assert.True(first.AsSpan(0, count).SequenceEqual(repeated.AsSpan(0, repeatedCount)));
            BiomeDefinition biome = catalog.MainPath[regionIndex];
            BiomeEncounterAnchor anchor = first[..count].First(candidate =>
                Math.Abs(candidate.WorldX - PlayableCavernWorldGenerator.MainPathCenterX(
                    candidate.WorldY,
                    campaign,
                    campaign.InitialRunSeed)) > campaign.MainPathHalfWidthCells + candidate.WidthCells);
            Assert.Equal(biome.Id, anchor.BiomeId);
            BiomePixelSceneDefinition scene = catalog.PixelScenes[catalog.FindPixelSceneIndex(anchor.PixelSceneId)];
            Assert.Equal(scene.EncounterId, anchor.EncounterId);
            BiomePixelSceneOperationDefinition topOperation = scene.Operations[^1];
            long operationX = anchor.WorldX - (scene.WidthCells / 2L) + topOperation.X + (topOperation.Width / 2L);
            long operationY = anchor.WorldY - (scene.HeightCells / 2L) + topOperation.Y + (topOperation.Height / 2L);
            ushort expectedOperationMaterial = ResolveRequired(materials, topOperation.Material);
            ushort actualOperationMaterial = probe.MaterialAt(operationX, operationY);
            Assert.True(
                expectedOperationMaterial == actualOperationMaterial,
                $"{biome.Id}/{scene.Id} operation 未实体化：({operationX},{operationY}) expected={expectedOperationMaterial}, actual={actualOperationMaterial}。");
        }

        int laboratoryIndex = CampaignConfig.RequiredRegionCount - 1;
        Assert.Equal(
            0,
            PlayableCavernWorldGenerator.CollectEncounterAnchors(
                catalog,
                campaign,
                laboratoryIndex,
                campaign.InitialRunSeed,
                -4_096,
                campaign.RegionStartCellY(laboratoryIndex),
                4_096,
                campaign.RegionStartCellY(laboratoryIndex) +
                    campaign.RegionHeightCellsAt(laboratoryIndex) - 1L,
                first));
    }

    /// <summary>
    /// 验证八个主路径 biome 的参考固定地标以同 seed 产生同一锚点，且 authored operation 真正写入地形；
    /// 地标不再依赖一条贯穿全层的人工直井。
    /// </summary>
    [Fact]
    public void BiomeFixedLandmarksMaterializeAndExposeDeterministicRouteAnchors()
    {
        CampaignConfig campaign = LoadConfig();
        BiomeCatalog catalog = LoadBiomes(campaign);
        IMaterialQuery materials = LoadMaterials();
        TerrainProbe probe = new(materials, campaign, catalog, campaign.InitialRunSeed);
        BiomeLandmarkAnchor[] first = new BiomeLandmarkAnchor[4];
        BiomeLandmarkAnchor[] repeated = new BiomeLandmarkAnchor[4];
        int total = 0;

        for (int regionIndex = 0; regionIndex < CampaignConfig.RequiredRegionCount; regionIndex++)
        {
            int count = PlayableCavernWorldGenerator.CollectBiomeLandmarkAnchors(
                catalog,
                campaign,
                regionIndex,
                campaign.InitialRunSeed,
                first);
            int repeatedCount = PlayableCavernWorldGenerator.CollectBiomeLandmarkAnchors(
                catalog,
                campaign,
                regionIndex,
                campaign.InitialRunSeed,
                repeated);

            Assert.InRange(count, 1, first.Length);
            Assert.Equal(count, repeatedCount);
            Assert.True(first.AsSpan(0, count).SequenceEqual(repeated.AsSpan(0, repeatedCount)));
            total += count;
            foreach (BiomeLandmarkAnchor anchor in first[..count])
            {
                int landmarkIndex = catalog.FindLandmarkIndex(anchor.LandmarkId);
                Assert.InRange(landmarkIndex, 0, catalog.Landmarks.Length - 1);
                BiomeLandmarkDefinition landmark = catalog.Landmarks[landmarkIndex];
                Assert.Equal(regionIndex, anchor.RegionIndex);
                Assert.Equal(landmark.DisplayName, anchor.DisplayName);
                Assert.Equal(landmark.EncounterId, anchor.EncounterId);
                Assert.Equal(landmark.WidthCells, anchor.WidthCells);
                Assert.Equal(landmark.HeightCells, anchor.HeightCells);

                BiomePixelSceneOperationDefinition topOperation = landmark.Operations[^1];
                long operationX = anchor.WorldX - (anchor.WidthCells / 2L) +
                    topOperation.X + (topOperation.Width / 2L);
                long operationY = anchor.WorldY - (anchor.HeightCells / 2L) +
                    topOperation.Y + (topOperation.Height / 2L);
                Assert.Equal(
                    ResolveRequired(materials, topOperation.Material),
                    probe.MaterialAt(operationX, operationY));

            }
        }

        Assert.Equal(catalog.Landmarks.Length, total);
    }

    /// <summary>
    /// 验证 Editor 动态脚本的 authoring preview 只通过公开 Config API 读取战役与 biome 数据，
    /// 不依赖静态 Demo 程序集才具备的 embedded resource。
    /// </summary>
    [Fact]
    public void CampaignAuthoringPreviewLoadsTerrainCatalogThroughPublicConfigApi()
    {
        TrackingConfigApi config = new(ContentRoot());
        CountingAuthoringWorldEditApi edit = new(width: 720, height: 480);
        AuthoringWorldPreviewContext context = new(
            LoadMaterials(),
            config,
            edit,
            WidthCells: 720,
            HeightCells: 480);

        PlayableCavernWorldGenerator.PopulateAuthoringWorld(in context);

        Assert.Equal(["campaign.json", "biomes.json", "noita-wang-terrain.json"], config.ReadPaths);
        Assert.Equal(1, edit.ClearRectCount);
        Assert.True(edit.PaintedCellCount > 0);
    }

    /// <summary>
    /// 验证 edge-compatible tile grammar 在正负坐标的共享边使用同一开口决策，
    /// 并且八个主路径区域不是同一套参数换皮。
    /// </summary>
    [Fact]
    public void BiomeTileGrammarHasSymmetricSharedEdgesAcrossNegativeCoordinates()
    {
        CampaignConfig campaign = LoadConfig();
        BiomeCatalog catalog = LoadBiomes(campaign);
        Assert.Equal(
            CampaignConfig.RequiredRegionCount,
            catalog.MainPath.Select(static biome => biome.Grammar.Kind).Distinct(StringComparer.Ordinal).Count());

        foreach (BiomeDefinition biome in catalog.MainPath)
        {
            int tileSize = biome.Grammar.TileSizeCells;
            int openVerticalEdges = 0;
            int openHorizontalEdges = 0;
            for (long tile = -24; tile <= 24; tile++)
            {
                long boundaryX = tile * tileSize;
                long centerY = (7L * tileSize) + (tileSize / 2L);
                bool left = PlayableCavernWorldGenerator.IsBiomeGrammarOpenAt(
                    boundaryX - 1,
                    centerY,
                    biome,
                    campaign.InitialRunSeed);
                bool right = PlayableCavernWorldGenerator.IsBiomeGrammarOpenAt(
                    boundaryX,
                    centerY,
                    biome,
                    campaign.InitialRunSeed);
                Assert.Equal(left, right);
                openVerticalEdges += left ? 1 : 0;

                long centerX = (tile * tileSize) + (tileSize / 2L);
                long boundaryY = 11L * tileSize;
                bool above = PlayableCavernWorldGenerator.IsBiomeGrammarOpenAt(
                    centerX,
                    boundaryY - 1,
                    biome,
                    campaign.InitialRunSeed);
                bool below = PlayableCavernWorldGenerator.IsBiomeGrammarOpenAt(
                    centerX,
                    boundaryY,
                    biome,
                    campaign.InitialRunSeed);
                Assert.Equal(above, below);
                openHorizontalEdges += above ? 1 : 0;
            }

            Assert.InRange(openVerticalEdges, 1, 48);
            Assert.InRange(openHorizontalEdges, 1, 48);
        }
    }

    /// <summary>
    /// 验证 active connection 不只存在于数据：侧区实际使用目标 palette，秘密入口保留可挖 gate，
    /// Mines→Snowy 与 Temple→Laboratory 纵向捷径逐 cell 连续。
    /// </summary>
    [Fact]
    public void ActiveBiomeConnectionsMaterializeSideAreasSecretsAndVerticalShortcuts()
    {
        CampaignConfig campaign = LoadConfig();
        BiomeCatalog catalog = LoadBiomes(campaign);
        IMaterialQuery materials = LoadMaterials();
        TerrainProbe probe = new(materials, campaign, catalog, campaign.InitialRunSeed);

        foreach (BiomeConnectionDefinition connection in catalog.Connections)
        {
            int fromIndex = catalog.FindMainPathIndex(connection.From);
            int direction = connection.Side == "west" ? -1 : 1;
            long startY = campaign.RegionStartCellY(fromIndex) + connection.FromLocalDepthCells;
            bool sideConnection = connection.Kind is "side-biome" or "secret-side-biome";
            int toIndex = sideConnection ? -1 : catalog.FindMainPathIndex(connection.To);
            long endY = sideConnection
                ? campaign.RegionStartCellY(fromIndex) + connection.ToLocalDepthCells
                : campaign.RegionStartCellY(toIndex) + connection.ToLocalDepthCells;
            long anchorY = startY + ((endY - startY) / 2);
            long centerX = PlayableCavernWorldGenerator.MainPathCenterX(
                anchorY,
                campaign,
                campaign.InitialRunSeed) + ((long)direction * connection.OffsetCells);

            if (connection.Kind == "vertical-shortcut")
            {
                for (long y = startY; y <= endY; y += 23)
                {
                    Assert.Equal(ResolveRequired(materials, "empty"), probe.MaterialAt(centerX, y));
                }

                continue;
            }

            BiomeDefinition sideBiome = connection.Kind == "vertical-side-biome"
                ? catalog.SideBiomes[catalog.FindSideBiomeIndex(connection.SideBiome)]
                : catalog.SideBiomes[catalog.FindSideBiomeIndex(connection.To)];
            int signatureCells = CountPaletteInRect(
                probe,
                materials,
                sideBiome.Palette,
                centerX - 31,
                anchorY - 31,
                centerX + 32,
                anchorY + 32);
            Assert.True(signatureCells >= 128, $"连接 {connection.Id} 未生成侧区 palette，cells={signatureCells}。 ");

            long corridorY = connection.Kind == "vertical-side-biome" ? startY : anchorY;
            long corridorPathX = PlayableCavernWorldGenerator.MainPathCenterX(
                corridorY,
                campaign,
                campaign.InitialRunSeed);
            long nearEdgeX = centerX - ((long)direction * connection.HalfWidthCells);
            long gateX = nearEdgeX - (direction * 2L);
            Assert.Equal(ResolveRequired(materials, connection.GateMaterial), probe.MaterialAt(gateX, corridorY));
            long sampleX = corridorPathX + ((nearEdgeX - corridorPathX) / 2);
            Assert.Equal(ResolveRequired(materials, "empty"), probe.MaterialAt(sampleX, corridorY));
        }
    }

    /// <summary>
    /// 验证七排 Portal 都有真实 Teleportatium 供能池、同层固定目的地，且 Holy Mountain authored 地标写入地形。
    /// </summary>
    [Fact]
    public void PortalBasinsAndHolyMountainLandmarksMaterializeFromBiomeData()
    {
        CampaignConfig campaign = LoadConfig();
        BiomeCatalog catalog = LoadBiomes(campaign);
        IMaterialQuery materials = LoadMaterials();
        TerrainProbe probe = new(materials, campaign, catalog, campaign.InitialRunSeed);
        PortalNetworkDefinition portal = catalog.PortalNetwork;
        ushort teleportatium = ResolveRequired(materials, portal.TeleportatiumMaterial);
        ushort portalShell = ResolveRequired(materials, portal.EyeShellMaterial);
        ushort empty = ResolveRequired(materials, "empty");
        ushort water = ResolveRequired(materials, "water");
        ushort metal = ResolveRequired(materials, catalog.HolyMountain.PlatformMaterial);
        ushort crystal = ResolveRequired(materials, "crystal");
        HolyMountainLandmarkAnchor[] landmarks = new HolyMountainLandmarkAnchor[16];
        HolyMountainLandmarkAnchor[] repeatedLandmarks = new HolyMountainLandmarkAnchor[16];

        for (int holyMountainIndex = 0; holyMountainIndex < CampaignConfig.RequiredRegionCount - 1; holyMountainIndex++)
        {
            CampaignPortalAnchor first = default;
            CampaignPortalAnchor previous = default;
            for (int portalIndex = 0; portalIndex < portal.PortalsPerHolyMountain; portalIndex++)
            {
                CampaignPortalAnchor anchor = PlayableCavernWorldGenerator.ResolvePortalAnchor(
                    campaign,
                    portal,
                    holyMountainIndex,
                    portalIndex,
                    campaign.InitialRunSeed);
                if (portalIndex == 0)
                {
                    first = anchor;
                }
                else
                {
                    Assert.Equal(first.DestinationX, anchor.DestinationX);
                    Assert.Equal(first.DestinationY, anchor.DestinationY);
                    Assert.Equal(
                        empty,
                        probe.MaterialAt(
                            previous.SourceX + ((anchor.SourceX - previous.SourceX) / 2),
                            anchor.SourceY));
                }

                previous = anchor;

                long liquidY = anchor.SourceY + portal.BasinTopOffsetCells + 2;
                Assert.Equal(teleportatium, probe.MaterialAt(anchor.SourceX, liquidY));
                Assert.Equal(
                    portalShell,
                    probe.MaterialAt(anchor.SourceX + portal.BasinHalfWidthCells, liquidY));
                int poweredCells = CountMaterialInRect(
                    probe,
                    teleportatium,
                    anchor.SourceX - portal.BasinHalfWidthCells + 1,
                    liquidY,
                    anchor.SourceX + portal.BasinHalfWidthCells - 1,
                    anchor.SourceY + portal.BasinTopOffsetCells + portal.BasinDepthCells - 1);
                Assert.True(poweredCells >= portal.MinimumPowerCells);
                Assert.Equal(empty, probe.MaterialAt(anchor.SourceX, anchor.SourceY));
                Assert.Equal(empty, probe.MaterialAt(anchor.DestinationX, anchor.DestinationY));
            }

            int landmarkCount = PlayableCavernWorldGenerator.CollectHolyMountainLandmarkAnchors(
                catalog,
                campaign,
                holyMountainIndex,
                campaign.InitialRunSeed,
                landmarks);
            int repeatedLandmarkCount = PlayableCavernWorldGenerator.CollectHolyMountainLandmarkAnchors(
                catalog,
                campaign,
                holyMountainIndex,
                campaign.InitialRunSeed,
                repeatedLandmarks);
            Assert.Equal(catalog.HolyMountain.Landmarks.Length, landmarkCount);
            Assert.Equal(landmarkCount, repeatedLandmarkCount);
            Assert.True(
                landmarks.AsSpan(0, landmarkCount).SequenceEqual(
                    repeatedLandmarks.AsSpan(0, repeatedLandmarkCount)));
            foreach (HolyMountainLandmarkAnchor landmark in landmarks[..landmarkCount])
            {
                switch (landmark.Kind)
                {
                    case "arrival":
                    case "exit-tunnel":
                        Assert.Equal(empty, probe.MaterialAt(landmark.WorldX, landmark.WorldY));
                        break;
                    case "water-pool":
                        Assert.Equal(water, probe.MaterialAt(landmark.WorldX, landmark.WorldY));
                        break;
                    case "shop-platform":
                        Assert.True(CountMaterialInRect(probe, metal, landmark.WorldX - 4, landmark.WorldY, landmark.WorldX + 4, landmark.WorldY + 8) > 0);
                        break;
                    case "worm-crystal-room":
                        Assert.True(CountMaterialInRect(probe, crystal, landmark.WorldX - 4, landmark.WorldY, landmark.WorldX + 4, landmark.WorldY + 12) > 0);
                        break;
                    case "perk-platform":
                    case "training-statues":
                        Assert.True(CountMaterialInRect(probe, metal, landmark.WorldX - 10, landmark.WorldY - 12, landmark.WorldX + 10, landmark.WorldY + 10) > 0);
                        break;
                    default:
                        throw new InvalidOperationException($"未覆盖 Holy Mountain landmark kind：{landmark.Kind}。");
                }
            }


            long holyMountainY = campaign.HolyMountainStartCellY(holyMountainIndex);
            long exitShaftY = holyMountainY + 88;
            long exitShaftX = PlayableCavernWorldGenerator.MainPathCenterX(
                exitShaftY,
                campaign,
                campaign.InitialRunSeed) + 158;
            Assert.Equal(empty, probe.MaterialAt(exitShaftX, exitShaftY));
            long returnCorridorY = holyMountainY + 116;
            long returnCorridorX = PlayableCavernWorldGenerator.MainPathCenterX(
                returnCorridorY,
                campaign,
                campaign.InitialRunSeed) + 80;
            Assert.Equal(empty, probe.MaterialAt(returnCorridorX, returnCorridorY));
        }
    }

    /// <summary>
    /// 验证目录已经在 Describe 阶段编译，之后八区 chunk、encounter 与地图地标查询不产生稳态托管分配。
    /// </summary>
    [Fact]
    public void BiomeChunkAndEncounterGenerationRemainAllocationFreeAfterWarmup()
    {
        CampaignConfig campaign = LoadConfig();
        BiomeCatalog catalog = LoadBiomes(campaign);
        IMaterialQuery materials = LoadMaterials();
        PlayableCavernWorldGenerator generator = new();
        EngineScriptConfigApi configApi = new(ContentRoot());
        ProceduralWorldBuildRequest request = new(
            PlayableCavernWorldGenerator.Key,
            materials,
            Config: configApi);
        _ = generator.Describe(in request);
        ushort[] materialCells = new ushort[ChunkSize * ChunkSize];
        Half[] temperatureCells = new Half[TemperatureSize * TemperatureSize];
        BiomeEncounterAnchor[] anchors = new BiomeEncounterAnchor[32];
        BiomeLandmarkAnchor[] biomeLandmarks = new BiomeLandmarkAnchor[4];
        HolyMountainLandmarkAnchor[] holyMountainLandmarks = new HolyMountainLandmarkAnchor[16];

        for (int regionIndex = 0; regionIndex < CampaignConfig.RequiredRegionCount; regionIndex++)
        {
            int chunkY = checked((int)((campaign.RegionStartCellY(regionIndex) + 256) / ChunkSize));
            generator.PopulatePreparedChunkForBenchmark(
                chunkX: 16 + regionIndex,
                chunkY,
                materialCells,
                temperatureCells,
                campaign.InitialRunSeed);
            _ = PlayableCavernWorldGenerator.CollectEncounterAnchors(
                catalog,
                campaign,
                regionIndex,
                campaign.InitialRunSeed,
                -1_024,
                campaign.RegionStartCellY(regionIndex),
                1_024,
                campaign.RegionStartCellY(regionIndex) + campaign.RegionHeightCellsAt(regionIndex) - 1L,
                anchors);
            _ = PlayableCavernWorldGenerator.CollectBiomeLandmarkAnchors(
                catalog,
                campaign,
                regionIndex,
                campaign.InitialRunSeed,
                biomeLandmarks);
            if (regionIndex < CampaignConfig.RequiredRegionCount - 1)
            {
                _ = PlayableCavernWorldGenerator.CollectHolyMountainLandmarkAnchors(
                    catalog,
                    campaign,
                    regionIndex,
                    campaign.InitialRunSeed,
                    holyMountainLandmarks);
            }
        }

        long chunkBefore = GC.GetAllocatedBytesForCurrentThread();
        int checksum = 0;
        for (int iteration = 0; iteration < 64; iteration++)
        {
            int regionIndex = iteration & 7;
            int chunkY = checked((int)((campaign.RegionStartCellY(regionIndex) + 256) / ChunkSize));
            generator.PopulatePreparedChunkForBenchmark(
                chunkX: 16 + regionIndex,
                chunkY,
                materialCells,
                temperatureCells,
                campaign.InitialRunSeed);
            checksum ^= materialCells[(iteration * 61) & (materialCells.Length - 1)];
        }

        long chunkAllocated = GC.GetAllocatedBytesForCurrentThread() - chunkBefore;
        long encounterBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 256; iteration++)
        {
            int regionIndex = iteration & 7;
            checksum ^= PlayableCavernWorldGenerator.CollectEncounterAnchors(
                catalog,
                campaign,
                regionIndex,
                campaign.InitialRunSeed,
                -1_024,
                campaign.RegionStartCellY(regionIndex),
                1_024,
                campaign.RegionStartCellY(regionIndex) + campaign.RegionHeightCellsAt(regionIndex) - 1L,
                anchors);
        }

        long encounterAllocated = GC.GetAllocatedBytesForCurrentThread() - encounterBefore;
        long biomeLandmarkBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 256; iteration++)
        {
            checksum ^= PlayableCavernWorldGenerator.CollectBiomeLandmarkAnchors(
                catalog,
                campaign,
                iteration & 7,
                campaign.InitialRunSeed,
                biomeLandmarks);
        }

        long biomeLandmarkAllocated = GC.GetAllocatedBytesForCurrentThread() - biomeLandmarkBefore;
        long landmarkBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 256; iteration++)
        {
            checksum ^= PlayableCavernWorldGenerator.CollectHolyMountainLandmarkAnchors(
                catalog,
                campaign,
                iteration % (CampaignConfig.RequiredRegionCount - 1),
                campaign.InitialRunSeed,
                holyMountainLandmarks);
        }

        long landmarkAllocated = GC.GetAllocatedBytesForCurrentThread() - landmarkBefore;
        GC.KeepAlive(checksum);
        Assert.InRange(chunkAllocated, 0, 1_024);
        Assert.InRange(encounterAllocated, 0, 1_024);
        Assert.InRange(biomeLandmarkAllocated, 0, 1_024);
        Assert.InRange(landmarkAllocated, 0, 1_024);
    }

    /// <summary>
    /// 验证主区与侧区的实际 chunk 开/实轮廓来自各自绑定的 Noita Wang 模板，
    /// 防止目录仍在但运行时悄然退回通用噪声洞穴。
    /// </summary>
    [Fact]
    public void MainAndSideBiomeChunksFollowTheirBoundNoitaWangMasks()
    {
        CampaignConfig campaign = LoadConfig();
        BiomeCatalog biomes = LoadBiomes(campaign);
        NoitaWangTerrainCatalog wang = NoitaWangTerrainCatalog.Load(new EngineScriptConfigApi(ContentRoot()));
        IMaterialQuery materials = LoadMaterials();
        TerrainProbe probe = new(materials, campaign, biomes, campaign.InitialRunSeed);
        ushort empty = ResolveRequired(materials, "empty");
        (string ReferenceBiomeId, long OriginX, long OriginY)[] cases =
        [
            ("coalmine", 256, 832),
            ("fungicave", -3_456, 1_920),
        ];

        foreach ((string referenceBiomeId, long originX, long originY) in cases)
        {
            DecodedNoitaWangTerrainSet set = wang.FindForReferenceBiome(referenceBiomeId);
            ulong salt = StableReferenceSalt(referenceBiomeId);
            int matches = 0;
            int openCells = 0;
            int solidCells = 0;
            const int SampleSide = 128;
            for (int localY = 0; localY < SampleSide; localY++)
            {
                long worldY = originY + localY;
                for (int localX = 0; localX < SampleSide; localX++)
                {
                    long worldX = originX + localX;
                    byte semantic = set.Sample(worldX, worldY, campaign.InitialRunSeed, salt);
                    bool expectedOpen = semantic == (byte)NoitaWangTerrainSemantic.Empty ||
                        DecodedNoitaWangTerrainSet.IsMarker(semantic) ||
                        (semantic == (byte)NoitaWangTerrainSemantic.RandomBinary &&
                            !DecodedNoitaWangTerrainSet.IsRandomBinarySolid(
                                worldX,
                                worldY,
                                campaign.InitialRunSeed,
                                salt));
                    bool actualOpen = probe.MaterialAt(worldX, worldY) == empty;
                    matches += expectedOpen == actualOpen ? 1 : 0;
                    openCells += expectedOpen ? 1 : 0;
                    solidCells += expectedOpen ? 0 : 1;
                }
            }

            int samples = SampleSide * SampleSide;
            Assert.InRange(openCells, 1_024, samples - 1_024);
            Assert.InRange(solidCells, 1_024, samples - 1_024);
            Assert.True(
                matches >= samples * 9 / 10,
                $"{referenceBiomeId} 实际 chunk 与绑定 Wang 开/实掩码只匹配 {matches}/{samples} cells。");
        }
    }

    /// <summary>
    /// 验证七个程序化纵深区域由一条逐 cell 连续的主通道连接，七个 Holy Mountain 具备实体地形，
    /// 且东侧 Laboratory 使用参考 boss_arena 的固定 2600x1600 空间而非第八条全宽随机地层。
    /// </summary>
    [Fact]
    public void CampaignTopologyConnectsSevenProceduralRegionsToFixedLaboratory()
    {
        CampaignConfig config = LoadConfig();
        BiomeCatalog biomes = LoadBiomes(config);
        IMaterialQuery materials = LoadMaterials();
        TerrainProbe probe = new(materials, config, biomes, config.InitialRunSeed);
        ushort empty = ResolveRequired(materials, "empty");
        ushort holyMountainShell = ResolveRequired(materials, biomes.HolyMountain.ShellMaterial);
        ushort holyMountainPlatform = ResolveRequired(materials, biomes.HolyMountain.PlatformMaterial);

        long campaignBottom = config.SurfaceY + config.CampaignEndDepthCells;
        Assert.Equal(227f, PlayableCavernWorldGenerator.PlayerSpawnX);
        Assert.Equal(PlayableCavernWorldGenerator.PlayerSpawnY, config.SurfaceY - 85f);
        Assert.Equal(empty, probe.MaterialAt(227, config.SurfaceY - 85));
        Assert.NotEqual(empty, probe.MaterialAt(227, config.SurfaceY - 70));

        for (int regionIndex = 0; regionIndex < CampaignConfig.RequiredRegionCount - 1; regionIndex++)
        {
            long regionY = RegionStart(config, regionIndex) + (config.RegionHeightCellsAt(regionIndex) / 2);
            CampaignDepthLocation location = config.ResolveLocation(regionY);
            Assert.Equal(CampaignDepthKind.Region, location.Kind);
            Assert.Equal(regionIndex, location.RegionIndex);

            CampaignRegionDefinition region = config.Regions[regionIndex];
            BiomeDefinition biome = biomes.MainPath[regionIndex];
            ushort primary = ResolveRequired(materials, biome.Palette.Primary);
            ushort secondary = ResolveRequired(materials, biome.Palette.Secondary);
            ushort loose = ResolveRequired(materials, biome.Palette.Loose);
            ushort hazard = ResolveRequired(materials, biome.Palette.Hazard);
            int signatureCells = 0;
            int hazardCells = 0;
            int topologyCells = 0;
            WorldTopologyDefinition topology = biomes.WorldTopology;
            for (int mapY = 0; mapY < topology.Height; mapY++)
            {
                for (int mapX = 0; mapX < topology.Width; mapX++)
                {
                    ReferenceBiomeDefinition referenceBiome = ReferenceBiomeAt(topology, mapX, mapY);
                    if (!string.Equals(referenceBiome.GameplayBiome, region.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    topologyCells++;
                    long cellOriginX = ((long)mapX - topology.OriginMacroX) * topology.MacroCellSize;
                    long cellOriginY = config.SurfaceY +
                        (((long)mapY - topology.OriginMacroY) * topology.MacroCellSize);
                    CampaignDepthLocation topologyLocation = config.ResolveLocation(cellOriginY + 256);
                    if (topologyLocation.Kind != CampaignDepthKind.Region ||
                        topologyLocation.RegionIndex != regionIndex)
                    {
                        continue;
                    }

                    for (int offsetY = 128; offsetY <= 384; offsetY += 256)
                    {
                        for (int offsetX = 128; offsetX <= 384; offsetX += 256)
                        {
                            ChunkSample topologyChunk = probe.ChunkAt(
                                cellOriginX + offsetX,
                                cellOriginY + offsetY);
                            signatureCells += CountAny(
                                topologyChunk.Materials,
                                primary,
                                secondary,
                                loose,
                                hazard);
                            hazardCells += Count(topologyChunk.Materials, hazard);
                        }
                    }
                }
            }

            Assert.True(topologyCells > 0, $"worldTopology 缺少区域 {region.Id}。");
            Assert.True(signatureCells >= 256, $"区域 {region.Id} 缺少可辨识的材质地层，cells={signatureCells}。");
            Assert.True(hazardCells > 0, $"区域 {region.Id} 必须实际生成危险材质 {biome.Palette.Hazard}。");

            long pathX = PlayableCavernWorldGenerator.MainPathCenterX(regionY, config, config.InitialRunSeed);
            Assert.Equal((float)(Half)biome.BaseTemperature, probe.TemperatureAt(pathX, regionY));

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

        AssertFixedLaboratoryTopology(config, biomes, materials, probe);

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
        BiomeCatalog biomes = LoadBiomes(config);
        IMaterialQuery materials = LoadMaterials();
        (int X, int Y)[] coordinates = [(-13, 4), (11, 10), (-7, 40), (5, 77)];
        Dictionary<(int X, int Y), ChunkSample> forward = [];
        Dictionary<(int X, int Y), ChunkSample> reverse = [];

        foreach ((int x, int y) in coordinates)
        {
            forward.Add((x, y), GenerateChunk(materials, config, biomes, config.InitialRunSeed, x, y));
        }

        for (int i = coordinates.Length - 1; i >= 0; i--)
        {
            (int x, int y) = coordinates[i];
            reverse.Add((x, y), GenerateChunk(materials, config, biomes, config.InitialRunSeed, x, y));
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
                biomes,
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
        BiomeCatalog biomes,
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
            config,
            biomes);
        return new ChunkSample(materialCells, temperatureCells);
    }

    private static void AssertFixedLaboratoryTopology(
        CampaignConfig config,
        BiomeCatalog biomes,
        IMaterialQuery materials,
        TerrainProbe probe)
    {
        WorldTopologyDefinition topology = biomes.WorldTopology;
        Assert.Equal(512, topology.MacroCellSize);
        Assert.Equal(70, topology.Width);
        Assert.Equal(48, topology.Height);
        Assert.Equal(35, topology.OriginMacroX);
        Assert.Equal(14, topology.OriginMacroY);
        Assert.Equal("17130612", topology.ReferenceBuildId);
        Assert.Equal("9dbd52ced019a643169a2db02f46c77f8766c6e5", topology.ReferenceVersionHash);
        Assert.Equal("legacy", topology.OutsideKind);
        Assert.Equal(129, topology.ReferenceBiomes.Length);
        Assert.Equal(48, topology.MacroRows.Length);

        HashSet<int> usedReferenceBiomes = [];
        int encodedCells = 0;
        for (int mapY = 0; mapY < topology.MacroRows.Length; mapY++)
        {
            Assert.Equal(140, topology.MacroRows[mapY].Length);
            for (int mapX = 0; mapX < topology.Width; mapX++)
            {
                int index = BiomeCatalog.DecodeReferenceBiomeIndex(topology.MacroRows[mapY], mapX);
                Assert.InRange(index, 0, topology.ReferenceBiomes.Length - 1);
                _ = usedReferenceBiomes.Add(index);
                encodedCells++;
            }
        }

        Assert.Equal(3_360, encodedCells);
        Assert.Equal(129, usedReferenceBiomes.Count);
        Assert.Equal(9, topology.ReferenceBiomes.Count(static biome => biome.ReferenceTerrainMask is not null));
        ReferenceBiomeDefinition leftEntrance = ReferenceBiomeAtMacro(topology, 0, -1);
        Assert.Equal("mountain-left-entrance", leftEntrance.Id);
        Assert.NotNull(leftEntrance.ReferenceTerrainMask);
        Assert.Equal(65_536, leftEntrance.DecodedReferenceTerrainMask.Length);
        Assert.Equal("mountain-hall", ReferenceBiomeAtMacro(topology, 1, -1).Id);
        Assert.Equal("mountain-right", ReferenceBiomeAtMacro(topology, 2, -1).Id);
        Assert.Equal("coalmine", ReferenceBiomeAtMacro(topology, 0, 0).Id);
        Assert.Equal("temple-wall", ReferenceBiomeAtMacro(topology, -3, 2).Id);
        Assert.Equal("temple-altar-left", ReferenceBiomeAtMacro(topology, -2, 2).Id);
        ReferenceBiomeDefinition templeAltar = ReferenceBiomeAtMacro(topology, -1, 2);
        Assert.Equal("temple-altar", templeAltar.Id);
        Assert.Equal(65_536, templeAltar.DecodedReferenceTerrainMask.Length);
        Assert.Equal("temple-altar-right", ReferenceBiomeAtMacro(topology, 0, 2).Id);

        FixedLaboratoryTopologyDefinition laboratoryTopology = topology.FixedLaboratory;
        Assert.Equal((1_536, 12_288, 2_600, 1_600), (
            laboratoryTopology.OriginX,
            laboratoryTopology.OriginDepthCells,
            laboratoryTopology.WidthCells,
            laboratoryTopology.HeightCells));
        Assert.NotNull(laboratoryTopology.ReferenceTerrainMask);
        Assert.Equal(2_080_000, laboratoryTopology.DecodedReferenceTerrainMask.Length);

        BiomeDefinition laboratory = biomes.MainPath[^1];
        ushort empty = ResolveRequired(materials, "empty");
        ushort boundary = ResolveRequired(materials, "boundary_stone");
        ushort structure = ResolveRequired(materials, laboratory.Palette.Structure);
        ushort hazard = ResolveRequired(materials, laboratory.Palette.Hazard);
        ushort water = ResolveRequired(materials, "water");
        ushort dirt = ResolveRequired(materials, "dirt");
        ushort crystal = ResolveRequired(materials, "crystal");
        ushort stone = ResolveRequired(materials, "stone");
        long originX = laboratoryTopology.OriginX;
        long originY = config.SurfaceY + laboratoryTopology.OriginDepthCells;

        Assert.Equal(empty, probe.MaterialAt(originX, originY + 100));
        Assert.Equal(structure, probe.MaterialAt(originX + 12, originY + 480));
        Assert.Equal(structure, probe.MaterialAt(originX + 500, originY + 760));
        Assert.Equal(empty, probe.MaterialAt(originX + 1_300, originY + 760));
        Assert.Equal(hazard, probe.MaterialAt(originX + 1_300, originY + 1_500));
        Assert.Equal(hazard, probe.MaterialAt(originX + 500, originY + 1_360));
        Assert.Equal(structure, probe.MaterialAt(originX + 500, originY + 350));
        Assert.Equal(boundary, probe.MaterialAt(originX + 1_436, originY));
        Assert.Equal(water, probe.MaterialAt(originX + 493, originY + 885));
        Assert.Equal(dirt, probe.MaterialAt(originX + 605, originY + 953));
        Assert.Equal(crystal, probe.MaterialAt(originX + 1_024, originY + 906));
        Assert.Equal(stone, probe.MaterialAt(originX + 293, originY + 852));
        Assert.Equal(boundary, probe.MaterialAt(originX + 2_000, originY + 1_700));
        Assert.Equal(
            (float)(Half)laboratory.BaseTemperature,
            probe.TemperatureAt(originX + 1_300, originY + 760));

        long defaultSolidX = (-10L * topology.MacroCellSize) + 256;
        long defaultSolidY = config.SurfaceY + (2L * topology.MacroCellSize) + 256;
        Assert.Equal(boundary, probe.MaterialAt(defaultSolidX, defaultSolidY));
        long lavaX = (4L * topology.MacroCellSize) + 256;
        long lavaY = config.SurfaceY + topology.MacroCellSize + 256;
        Assert.Equal(hazard, probe.MaterialAt(lavaX, lavaY));
    }

    private static ReferenceBiomeDefinition ReferenceBiomeAt(
        WorldTopologyDefinition topology,
        int mapX,
        int mapY)
    {
        int index = BiomeCatalog.DecodeReferenceBiomeIndex(topology.MacroRows[mapY], mapX);
        return topology.ReferenceBiomes[index];
    }

    private static ReferenceBiomeDefinition ReferenceBiomeAtMacro(
        WorldTopologyDefinition topology,
        int macroX,
        int macroY)
    {
        return ReferenceBiomeAt(
            topology,
            macroX + topology.OriginMacroX,
            macroY + topology.OriginMacroY);
    }

    private static int CountAny(
        ReadOnlySpan<ushort> materials,
        ushort first,
        ushort second,
        ushort third,
        ushort fourth)
    {
        int count = 0;
        for (int i = 0; i < materials.Length; i++)
        {
            ushort material = materials[i];
            count += material == first || material == second || material == third || material == fourth ? 1 : 0;
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

    private static int CountPaletteInRect(
        TerrainProbe probe,
        IMaterialQuery materials,
        BiomeMaterialPaletteDefinition palette,
        long minimumX,
        long minimumY,
        long maximumX,
        long maximumY)
    {
        Span<ushort> ids =
        [
            ResolveRequired(materials, palette.Primary),
            ResolveRequired(materials, palette.Secondary),
            ResolveRequired(materials, palette.Loose),
            ResolveRequired(materials, palette.Structure),
            ResolveRequired(materials, palette.Hazard),
            ResolveRequired(materials, palette.Pool),
        ];
        int count = 0;
        for (long y = minimumY; y <= maximumY; y++)
        {
            for (long x = minimumX; x <= maximumX; x++)
            {
                ushort material = probe.MaterialAt(x, y);
                for (int i = 0; i < ids.Length; i++)
                {
                    if (material == ids[i])
                    {
                        count++;
                        break;
                    }
                }
            }
        }

        return count;
    }

    private static int CountMaterialInRect(
        TerrainProbe probe,
        ushort expected,
        long minimumX,
        long minimumY,
        long maximumX,
        long maximumY)
    {
        int count = 0;
        for (long y = minimumY; y <= maximumY; y++)
        {
            for (long x = minimumX; x <= maximumX; x++)
            {
                count += probe.MaterialAt(x, y) == expected ? 1 : 0;
            }
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
        Assert.Equal(expected.Regions.Length, actual.Regions.Length);
        for (int i = 0; i < expected.Regions.Length; i++)
        {
            CampaignRegionDefinition actualRegion = actual.Regions[i];
            CampaignRegionDefinition expectedRegion = expected.Regions[i];
            Assert.Equal(expectedRegion.Id, actualRegion.Id);
            Assert.Equal(expectedRegion.DisplayName, actualRegion.DisplayName);
            Assert.Equal(expectedRegion.LegacyIds, actualRegion.LegacyIds);
            Assert.Equal(expectedRegion.StartDepthCells, actualRegion.StartDepthCells);
            Assert.Equal(expectedRegion.HeightCells, actualRegion.HeightCells);
        }
    }

    private static long RegionStart(CampaignConfig config, int regionIndex)
    {
        return config.RegionStartCellY(regionIndex);
    }

    private static long HolyMountainStart(CampaignConfig config, int holyMountainIndex)
    {
        return config.HolyMountainStartCellY(holyMountainIndex);
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

    private static BiomeCatalog LoadBiomes(CampaignConfig campaign)
    {
        return BiomeCatalog.Load(new EngineScriptConfigApi(ContentRoot()), campaign);
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

    private sealed class TerrainProbe(
        IMaterialQuery materials,
        CampaignConfig config,
        BiomeCatalog biomes,
        ulong worldSeed)
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
                chunk = GenerateChunk(materials, config, biomes, worldSeed, chunkX, chunkY);
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

    private static ulong StableReferenceSalt(string value)
    {
        ulong hash = 14_695_981_039_346_656_037UL;
        for (int i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= 1_099_511_628_211UL;
        }

        return hash;
    }

    private sealed record ChunkSample(ushort[] Materials, Half[] Temperatures);

    private sealed class TrackingConfigApi(string contentRoot) : IConfigApi
    {
        private readonly EngineScriptConfigApi _inner = new(contentRoot);
        private readonly List<string> _readPaths = [];

        public IReadOnlyList<string> ReadPaths => _readPaths;

        public string ReadText(string relativePath)
        {
            _readPaths.Add(relativePath);
            return _inner.ReadText(relativePath);
        }

        public TConfig Load<TConfig>(string relativePath, JsonTypeInfo<TConfig> typeInfo)
            where TConfig : class
        {
            _readPaths.Add(relativePath);
            return _inner.Load(relativePath, typeInfo);
        }
    }

    private sealed class CountingAuthoringWorldEditApi(int width, int height) : IAuthoringWorldEditApi
    {
        public int ClearRectCount { get; private set; }

        public long PaintedCellCount { get; private set; }

        public void PaintCell(int worldX, int worldY, MaterialId material)
        {
            ValidateCoordinate(worldX, worldY);
            PaintedCellCount++;
        }

        public int PaintRect(int minX, int minY, int maxX, int maxY, MaterialId material)
        {
            ValidateRect(minX, minY, maxX, maxY);
            int area = checked((maxX - minX + 1) * (maxY - minY + 1));
            PaintedCellCount += area;
            return area;
        }

        public void ClearCell(int worldX, int worldY)
        {
            ValidateCoordinate(worldX, worldY);
        }

        public int ClearRect(int minX, int minY, int maxX, int maxY)
        {
            ValidateRect(minX, minY, maxX, maxY);
            ClearRectCount++;
            return checked((maxX - minX + 1) * (maxY - minY + 1));
        }

        private void ValidateRect(int minX, int minY, int maxX, int maxY)
        {
            if (minX > maxX || minY > maxY)
            {
                throw new ArgumentException("authoring world 矩形边界顺序无效。");
            }

            ValidateCoordinate(minX, minY);
            ValidateCoordinate(maxX, maxY);
        }

        private void ValidateCoordinate(int x, int y)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }
        }
    }
}
