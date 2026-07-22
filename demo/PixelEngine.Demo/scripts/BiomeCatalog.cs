using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Noita 主路径、侧区、edge-compatible tile grammar 与 authored pixel-scene 的严格数据目录。
/// </summary>
internal sealed class BiomeCatalog
{
    internal const int CurrentSchemaVersion = 2;
    private const string EmbeddedResourceName = "PixelEngine.Demo.biomes.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly Lazy<BiomeCatalog> Builtin = new(LoadBuiltin, isThreadSafe: true);

    public int SchemaVersion { get; init; }

    public BiomeExpansionStages ExpansionStages { get; init; } = new();

    public HolyMountainDefinition HolyMountain { get; init; } = new();

    public PortalNetworkDefinition PortalNetwork { get; init; } = new();

    public BiomeDefinition[] MainPath { get; init; } = [];

    public BiomeDefinition[] SideBiomes { get; init; } = [];

    public BiomePixelSceneDefinition[] PixelScenes { get; init; } = [];

    public BiomeConnectionDefinition[] Connections { get; init; } = [];

    internal static BiomeCatalog BuiltinDefault => Builtin.Value;

    internal static BiomeCatalog Load(IConfigApi config, CampaignConfig campaign)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(campaign);
        return Parse(config.ReadText("biomes.json"), campaign);
    }

    internal static BiomeCatalog Parse(string json, CampaignConfig campaign)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(campaign);
        try
        {
            BiomeCatalog catalog = JsonSerializer.Deserialize<BiomeCatalog>(json, SerializerOptions) ??
                throw new InvalidDataException("biomes.json 根节点不能为 null。");
            return catalog.Validate(campaign);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"biomes.json JSON 无效：{exception.Message}",
                exception);
        }
    }

    internal int FindMainPathIndex(string id)
    {
        for (int i = 0; i < MainPath.Length; i++)
        {
            if (string.Equals(MainPath[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    internal int FindSideBiomeIndex(string id)
    {
        for (int i = 0; i < SideBiomes.Length; i++)
        {
            if (string.Equals(SideBiomes[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    internal int FindPixelSceneIndex(string id)
    {
        for (int i = 0; i < PixelScenes.Length; i++)
        {
            if (string.Equals(PixelScenes[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static BiomeCatalog LoadBuiltin()
    {
        using Stream stream = typeof(BiomeCatalog).Assembly.GetManifestResourceStream(EmbeddedResourceName) ??
            throw new InvalidOperationException($"Demo 程序集缺少嵌入资源 {EmbeddedResourceName}。");
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd(), CampaignConfig.BuiltinDefault);
    }

    private BiomeCatalog Validate(CampaignConfig campaign)
    {
        Require(SchemaVersion == CurrentSchemaVersion, $"schemaVersion 必须为 {CurrentSchemaVersion}。");
        ArgumentNullException.ThrowIfNull(ExpansionStages);
        ValidateStage(ExpansionStages.Surface, "expansionStages.surface");
        ValidateStage(ExpansionStages.SideBiomes, "expansionStages.sideBiomes");
        ValidateStage(ExpansionStages.SecretConnections, "expansionStages.secretConnections");
        ValidateStage(ExpansionStages.ParallelWorlds, "expansionStages.parallelWorlds");
        ValidateStage(ExpansionStages.NewGamePlus, "expansionStages.newGamePlus");
        Require(
            string.Equals(ExpansionStages.Surface, "active", StringComparison.Ordinal) &&
            string.Equals(ExpansionStages.SideBiomes, "active", StringComparison.Ordinal) &&
            string.Equals(ExpansionStages.SecretConnections, "active", StringComparison.Ordinal),
            "surface、sideBiomes 与 secretConnections 必须处于 active。 ");
        Require(
            string.Equals(ExpansionStages.ParallelWorlds, "planned", StringComparison.Ordinal) &&
            string.Equals(ExpansionStages.NewGamePlus, "planned", StringComparison.Ordinal),
            "parallelWorlds 与 newGamePlus 在当前阶段必须明确标记 planned。 ");
        ValidateHolyMountain(campaign);
        ValidatePortalNetwork(campaign);

        BiomeDefinition[] mainPath = MainPath ??
            throw new InvalidDataException("biomes.json 配置无效：mainPath 不能为空。");
        BiomeDefinition[] sideBiomes = SideBiomes ??
            throw new InvalidDataException("biomes.json 配置无效：sideBiomes 不能为空。");
        BiomePixelSceneDefinition[] pixelScenes = PixelScenes ??
            throw new InvalidDataException("biomes.json 配置无效：pixelScenes 不能为空。");
        BiomeConnectionDefinition[] connections = Connections ??
            throw new InvalidDataException("biomes.json 配置无效：connections 不能为空。");
        Require(mainPath.Length == CampaignConfig.RequiredRegionCount, "mainPath 必须恰好包含八个主路径 biome。");
        Require(sideBiomes.Length >= 3, "sideBiomes 必须至少声明 Fungal Caverns、Magical Temple 与 Lukki Lair。");
        Require(pixelScenes.Length >= mainPath.Length, "pixelScenes 必须至少为每个主路径 biome 提供一份 authored scene。");
        Require(connections.Length >= 4, "connections 必须同时覆盖侧区、秘密连接与跨区捷径。");
        Require(connections.Length <= 8, "connections 最多允许八项，以满足每行固定容量的零分配生成约束。");

        HashSet<string> biomeIds = new(StringComparer.Ordinal);
        for (int i = 0; i < mainPath.Length; i++)
        {
            BiomeDefinition biome = mainPath[i] ??
                throw new InvalidDataException($"biomes.json 配置无效：mainPath[{i}] 不能为空。");
            ValidateBiome(biome, $"mainPath[{i}]", biomeIds);
            CampaignRegionDefinition campaignRegion = campaign.Regions[i];
            Require(
                string.Equals(biome.Id, campaignRegion.Id, StringComparison.Ordinal),
                $"mainPath[{i}].id 必须与 campaign.json 的 {campaignRegion.Id} 一致。");
            Require(
                string.Equals(biome.DisplayName, campaignRegion.DisplayName, StringComparison.Ordinal),
                $"mainPath[{i}].displayName 必须与 campaign.json 的 {campaignRegion.DisplayName} 一致。");
        }

        for (int i = 0; i < sideBiomes.Length; i++)
        {
            BiomeDefinition biome = sideBiomes[i] ??
                throw new InvalidDataException($"biomes.json 配置无效：sideBiomes[{i}] 不能为空。");
            ValidateBiome(biome, $"sideBiomes[{i}]", biomeIds);
        }

        HashSet<string> sceneIds = new(StringComparer.Ordinal);
        for (int i = 0; i < pixelScenes.Length; i++)
        {
            ValidatePixelScene(
                pixelScenes[i] ?? throw new InvalidDataException($"biomes.json 配置无效：pixelScenes[{i}] 不能为空。"),
                i,
                sceneIds);
        }

        HashSet<string> referencedSceneIds = new(StringComparer.Ordinal);
        for (int i = 0; i < mainPath.Length; i++)
        {
            ValidateSceneReferences(mainPath[i], $"mainPath[{i}]", sceneIds, referencedSceneIds);
        }

        for (int i = 0; i < sideBiomes.Length; i++)
        {
            ValidateSceneReferences(sideBiomes[i], $"sideBiomes[{i}]", sceneIds, referencedSceneIds);
        }

        foreach (string sceneId in sceneIds)
        {
            Require(referencedSceneIds.Contains(sceneId), $"pixel scene {sceneId} 未被任何 biome 使用。");
        }

        HashSet<string> connectionIds = new(StringComparer.Ordinal);
        bool hasSideBiome = false;
        bool hasSecret = false;
        bool hasShortcut = false;
        for (int i = 0; i < connections.Length; i++)
        {
            BiomeConnectionDefinition connection = connections[i] ??
                throw new InvalidDataException($"biomes.json 配置无效：connections[{i}] 不能为空。");
            ValidateConnection(connection, i, campaign, connectionIds);
            hasSideBiome |= string.Equals(connection.Kind, "side-biome", StringComparison.Ordinal) ||
                string.Equals(connection.Kind, "vertical-side-biome", StringComparison.Ordinal);
            hasSecret |= string.Equals(connection.Kind, "secret-side-biome", StringComparison.Ordinal);
            hasShortcut |= string.Equals(connection.Kind, "vertical-shortcut", StringComparison.Ordinal);
        }

        Require(hasSideBiome, "connections 缺少 active side-biome。 ");
        Require(hasSecret, "connections 缺少 active secret-side-biome。 ");
        Require(hasShortcut, "connections 缺少 active vertical-shortcut。 ");
        return this;
    }

    private void ValidateHolyMountain(CampaignConfig campaign)
    {
        HolyMountainDefinition holyMountain = HolyMountain ??
            throw new InvalidDataException("biomes.json 配置无效：holyMountain 不能为空。");
        Require(
            float.IsFinite(holyMountain.BaseTemperature) &&
            holyMountain.BaseTemperature is >= 0f and <= 255f,
            "holyMountain.baseTemperature 必须位于 [0,255]。");
        RequireMaterialKey(holyMountain.ShellMaterial, "holyMountain.shellMaterial");
        RequireMaterialKey(holyMountain.PlatformMaterial, "holyMountain.platformMaterial");
        Require(
            holyMountain.ShellThicknessCells is >= 4 and <= 16 &&
            holyMountain.ShellThicknessCells < campaign.HolyMountainHeightCells / 4,
            "holyMountain.shellThicknessCells 必须位于 [4,16] 且能容纳内部房间。");

        HolyMountainOperationDefinition[] operations = holyMountain.LayoutOperations ??
            throw new InvalidDataException("biomes.json 配置无效：holyMountain.layoutOperations 不能为空。");
        Require(operations.Length is >= 8 and <= 32, "holyMountain.layoutOperations 必须包含 [8,32] 项。");
        int minimumX = -campaign.HolyMountainHalfWidthCells;
        int maximumXExclusive = campaign.HolyMountainHalfWidthCells + 9;
        for (int i = 0; i < operations.Length; i++)
        {
            HolyMountainOperationDefinition operation = operations[i] ??
                throw new InvalidDataException($"biomes.json 配置无效：holyMountain.layoutOperations[{i}] 不能为空。");
            string label = $"holyMountain.layoutOperations[{i}]";
            Require(operation.Kind is "fillRect" or "carveRect", $"{label}.kind 不受支持。");
            RequireMaterialKey(operation.Material, $"{label}.material");
            Require(operation.Width >= 1 && operation.Height >= 1, $"{label} 尺寸必须为正。");
            Require(
                operation.X >= minimumX &&
                operation.X + operation.Width <= maximumXExclusive,
                $"{label} 超出 Holy Mountain 横向布局。");
            Require(
                operation.Y >= 0 &&
                operation.Y + operation.Height <= campaign.HolyMountainHeightCells,
                $"{label} 超出 Holy Mountain 纵向布局。");
            if (operation.Kind == "carveRect")
            {
                Require(string.Equals(operation.Material, "empty", StringComparison.Ordinal), $"{label} carve 操作必须使用 empty。");
            }
        }

        HolyMountainLandmarkDefinition[] landmarks = holyMountain.Landmarks ??
            throw new InvalidDataException("biomes.json 配置无效：holyMountain.landmarks 不能为空。");
        Require(landmarks.Length is >= 8 and <= 16, "holyMountain.landmarks 必须包含 [8,16] 项。");
        HashSet<string> landmarkIds = new(StringComparer.Ordinal);
        HashSet<string> landmarkKinds = new(StringComparer.Ordinal);
        for (int i = 0; i < landmarks.Length; i++)
        {
            HolyMountainLandmarkDefinition landmark = landmarks[i] ??
                throw new InvalidDataException($"biomes.json 配置无效：holyMountain.landmarks[{i}] 不能为空。");
            string label = $"holyMountain.landmarks[{i}]";
            RequireStableId(landmark.Id, $"{label}.id");
            Require(landmarkIds.Add(landmark.Id), $"Holy Mountain landmark id 重复：{landmark.Id}。");
            Require(
                landmark.Kind is "arrival" or "water-pool" or "shop-platform" or "worm-crystal-room" or
                    "perk-platform" or "training-statues" or "exit-tunnel",
                $"{label}.kind 不受支持：{landmark.Kind}。");
            _ = landmarkKinds.Add(landmark.Kind);
            Require(
                landmark.OffsetXCells >= minimumX && landmark.OffsetXCells < maximumXExclusive,
                $"{label}.offsetXCells 超出 Holy Mountain 横向布局。");
            Require(
                landmark.LocalDepthCells >= 0 && landmark.LocalDepthCells < campaign.HolyMountainHeightCells,
                $"{label}.localDepthCells 超出 Holy Mountain 纵向布局。");
        }

        string[] requiredLandmarkKinds =
        [
            "arrival",
            "water-pool",
            "shop-platform",
            "worm-crystal-room",
            "perk-platform",
            "training-statues",
            "exit-tunnel",
        ];
        for (int i = 0; i < requiredLandmarkKinds.Length; i++)
        {
            Require(landmarkKinds.Contains(requiredLandmarkKinds[i]), $"holyMountain.landmarks 缺少 {requiredLandmarkKinds[i]}。");
        }
    }

    private void ValidatePortalNetwork(CampaignConfig campaign)
    {
        PortalNetworkDefinition portal = PortalNetwork ??
            throw new InvalidDataException("biomes.json 配置无效：portalNetwork 不能为空。");
        Require(
            portal.PortalsPerHolyMountain is >= 1 and <= 5 && (portal.PortalsPerHolyMountain & 1) == 1,
            "portalNetwork.portalsPerHolyMountain 必须是 [1,5] 内的奇数。");
        Require(portal.SpacingCells is >= 24 and <= 96, "portalNetwork.spacingCells 必须位于 [24,96]。");
        Require(
            portal.SourceOffsetAboveBoundaryCells is >= 16 and <= 96,
            "portalNetwork.sourceOffsetAboveBoundaryCells 必须位于 [16,96]。");
        Require(
            portal.DestinationLocalDepthCells is >= 12 &&
            portal.DestinationLocalDepthCells < campaign.HolyMountainHeightCells - 12,
            "portalNetwork.destinationLocalDepthCells 必须位于 Holy Mountain 安全内部。");
        Require(
            Math.Abs(portal.DestinationOffsetCells) <= campaign.HolyMountainHalfWidthCells - 16,
            "portalNetwork.destinationOffsetCells 必须位于 Holy Mountain 房间内部。");
        Require(portal.TriggerHalfWidthCells is >= 3 and <= 16, "portalNetwork.triggerHalfWidthCells 必须位于 [3,16]。");
        Require(portal.TriggerHalfHeightCells is >= 4 and <= 20, "portalNetwork.triggerHalfHeightCells 必须位于 [4,20]。");
        RequireMaterialKey(portal.EyeShellMaterial, "portalNetwork.eyeShellMaterial");
        RequireMaterialKey(portal.TeleportatiumMaterial, "portalNetwork.teleportatiumMaterial");
        Require(portal.BasinTopOffsetCells is >= 4 and <= 16, "portalNetwork.basinTopOffsetCells 必须位于 [4,16]。");
        Require(portal.BasinHalfWidthCells is >= 5 and <= 16, "portalNetwork.basinHalfWidthCells 必须位于 [5,16]。");
        Require(portal.BasinDepthCells is >= 5 and <= 14, "portalNetwork.basinDepthCells 必须位于 [5,14]。");
        Require(
            portal.SourceOffsetAboveBoundaryCells >=
                portal.BasinTopOffsetCells + portal.BasinDepthCells + 4,
            "portalNetwork 供能池必须完整位于来源 biome 内。");
        int maximumPowerCells = ((portal.BasinHalfWidthCells * 2) - 1) * (portal.BasinDepthCells - 2);
        Require(
            portal.MinimumPowerCells is >= 1 && portal.MinimumPowerCells <= maximumPowerCells,
            "portalNetwork.minimumPowerCells 超出供能池可采样容量。");
        RequireFiniteRange(portal.TransitionSeconds, 0.05, 1.0, "portalNetwork.transitionSeconds");
        RequireFiniteRange(portal.CooldownSeconds, 0.10, 2.0, "portalNetwork.cooldownSeconds");
        RequireFiniteRange(portal.InvulnerabilitySeconds, 1.0 / 120.0, 0.25, "portalNetwork.invulnerabilitySeconds");
    }

    private void ValidateConnection(
        BiomeConnectionDefinition connection,
        int index,
        CampaignConfig campaign,
        HashSet<string> connectionIds)
    {
        string label = $"connections[{index}]";
        RequireStableId(connection.Id, $"{label}.id");
        Require(connectionIds.Add(connection.Id), $"connection id 重复：{connection.Id}。 ");
        ValidateStage(connection.Stage, $"{label}.stage");
        Require(string.Equals(connection.Stage, "active", StringComparison.Ordinal), $"{label}.stage 当前必须为 active。");
        Require(
            connection.Kind is "side-biome" or "secret-side-biome" or "vertical-shortcut" or "vertical-side-biome",
            $"{label}.kind 不受支持：{connection.Kind}。 ");
        Require(connection.Side is "west" or "east", $"{label}.side 必须为 west 或 east。");
        int fromIndex = FindMainPathIndex(connection.From);
        Require(fromIndex >= 0, $"{label}.from 必须引用主路径 biome。");
        Require(connection.OffsetCells is >= 128 and <= 2_048, $"{label}.offsetCells 必须位于 [128,2048]。");
        Require(connection.HalfWidthCells is >= 8 and <= 384, $"{label}.halfWidthCells 必须位于 [8,384]。");
        Require(connection.CorridorHalfWidthCells is >= 2 and <= 32, $"{label}.corridorHalfWidthCells 必须位于 [2,32]。");
        RequireMaterialKey(connection.GateMaterial, $"{label}.gateMaterial");
        Require(
            connection.FromLocalDepthCells is >= 0 && connection.FromLocalDepthCells < campaign.RegionHeightCells,
            $"{label}.fromLocalDepthCells 必须位于来源 biome 内。");

        if (connection.Kind is "side-biome" or "secret-side-biome")
        {
            Require(FindSideBiomeIndex(connection.To) >= 0, $"{label}.to 必须引用侧区 biome。");
            Require(
                connection.ToLocalDepthCells > connection.FromLocalDepthCells &&
                connection.ToLocalDepthCells <= campaign.RegionHeightCells,
                $"{label}.toLocalDepthCells 必须是来源 biome 内更深的侧区终点。");
            Require(string.IsNullOrEmpty(connection.SideBiome), $"{label}.sideBiome 仅供 vertical-side-biome 使用。");
            return;
        }

        int toIndex = FindMainPathIndex(connection.To);
        Require(toIndex > fromIndex, $"{label}.to 必须引用更深的主路径 biome。");
        Require(
            connection.ToLocalDepthCells is >= 0 && connection.ToLocalDepthCells <= campaign.RegionHeightCells,
            $"{label}.toLocalDepthCells 必须位于目标 biome 内。");
        if (connection.Kind == "vertical-side-biome")
        {
            Require(FindSideBiomeIndex(connection.SideBiome) >= 0, $"{label}.sideBiome 必须引用侧区 biome。");
        }
        else
        {
            Require(string.IsNullOrEmpty(connection.SideBiome), $"{label}.sideBiome 仅供 vertical-side-biome 使用。");
        }
    }

    private static void ValidateBiome(BiomeDefinition biome, string label, HashSet<string> biomeIds)
    {
        RequireStableId(biome.Id, $"{label}.id");
        Require(biomeIds.Add(biome.Id), $"biome id 重复：{biome.Id}。 ");
        Require(!string.IsNullOrWhiteSpace(biome.DisplayName), $"{label}.displayName 不能为空。");
        Require(
            float.IsFinite(biome.BaseTemperature) && biome.BaseTemperature is >= 0f and <= 255f,
            $"{label}.baseTemperature 必须位于 [0,255]。");
        Require(
            double.IsFinite(biome.HazardFrequency) && biome.HazardFrequency is >= 0.0 and <= 0.2,
            $"{label}.hazardFrequency 必须位于 [0,0.2]。");
        BiomeMaterialPaletteDefinition palette = biome.Palette ??
            throw new InvalidDataException($"biomes.json 配置无效：{label}.palette 不能为空。");
        RequireMaterialKey(palette.Primary, $"{label}.palette.primary");
        RequireMaterialKey(palette.Secondary, $"{label}.palette.secondary");
        RequireMaterialKey(palette.Loose, $"{label}.palette.loose");
        RequireMaterialKey(palette.Structure, $"{label}.palette.structure");
        RequireMaterialKey(palette.Hazard, $"{label}.palette.hazard");
        RequireMaterialKey(palette.Pool, $"{label}.palette.pool");

        BiomeTerrainGrammarDefinition grammar = biome.Grammar ??
            throw new InvalidDataException($"biomes.json 配置无效：{label}.grammar 不能为空。");
        RequireStableId(grammar.Kind, $"{label}.grammar.kind");
        Require(grammar.TileSizeCells is >= 32 and <= 128, $"{label}.grammar.tileSizeCells 必须位于 [32,128]。");
        Require(grammar.CorridorHalfWidthCells is >= 2 and <= 16, $"{label}.grammar.corridorHalfWidthCells 必须位于 [2,16]。");
        Require(
            grammar.ChamberRadiusCells > grammar.CorridorHalfWidthCells &&
            grammar.ChamberRadiusCells <= (grammar.TileSizeCells / 2) - 2,
            $"{label}.grammar.chamberRadiusCells 必须容纳在 tile 内并宽于 corridor。");
        RequireFiniteRange(grammar.EdgeOpenChance, 0.20, 0.95, $"{label}.grammar.edgeOpenChance");
        RequireFiniteRange(grammar.NoiseOpenThreshold, -0.90, 0.90, $"{label}.grammar.noiseOpenThreshold");
        RequireFiniteRange(grammar.HorizontalScale, 0.001, 0.10, $"{label}.grammar.horizontalScale");
        RequireFiniteRange(grammar.VerticalScale, 0.001, 0.10, $"{label}.grammar.verticalScale");
        RequireFiniteRange(grammar.VerticalBias, -0.50, 0.50, $"{label}.grammar.verticalBias");
        Require((biome.PixelScenes?.Length ?? 0) > 0, $"{label}.pixelScenes 至少包含一项。");
    }

    private static void ValidatePixelScene(
        BiomePixelSceneDefinition scene,
        int index,
        HashSet<string> sceneIds)
    {
        string label = $"pixelScenes[{index}]";
        RequireStableId(scene.Id, $"{label}.id");
        Require(sceneIds.Add(scene.Id), $"pixel scene id 重复：{scene.Id}。 ");
        Require(scene.WidthCells is >= 8 and <= 128, $"{label}.widthCells 必须位于 [8,128]。");
        Require(scene.HeightCells is >= 8 and <= 128, $"{label}.heightCells 必须位于 [8,128]。");
        RequireFiniteRange(scene.SpawnChance, 0.001, 1.0, $"{label}.spawnChance");
        RequireStableId(scene.EncounterId, $"{label}.encounterId");
        BiomePixelSceneOperationDefinition[] operations = scene.Operations ??
            throw new InvalidDataException($"biomes.json 配置无效：{label}.operations 不能为空。");
        Require(operations.Length is >= 1 and <= 32, $"{label}.operations 必须包含 [1,32] 项。");
        for (int operationIndex = 0; operationIndex < operations.Length; operationIndex++)
        {
            BiomePixelSceneOperationDefinition operation = operations[operationIndex] ??
                throw new InvalidDataException($"biomes.json 配置无效：{label}.operations[{operationIndex}] 不能为空。");
            string operationLabel = $"{label}.operations[{operationIndex}]";
            Require(operation.Kind is "fillRect" or "carveRect" or "carveEllipse", $"{operationLabel}.kind 不受支持。");
            RequireMaterialKey(operation.Material, $"{operationLabel}.material");
            Require(operation.X >= 0 && operation.Y >= 0, $"{operationLabel} 坐标不能为负。");
            Require(operation.Width >= 1 && operation.Height >= 1, $"{operationLabel} 尺寸必须为正。");
            Require(operation.X + operation.Width <= scene.WidthCells, $"{operationLabel} 超出 scene 宽度。");
            Require(operation.Y + operation.Height <= scene.HeightCells, $"{operationLabel} 超出 scene 高度。");
            if (operation.Kind is "carveRect" or "carveEllipse")
            {
                Require(string.Equals(operation.Material, "empty", StringComparison.Ordinal), $"{operationLabel} carve 操作必须使用 empty。");
            }
        }
    }

    private static void ValidateSceneReferences(
        BiomeDefinition biome,
        string label,
        HashSet<string> sceneIds,
        HashSet<string> referencedSceneIds)
    {
        string[] references = biome.PixelScenes ?? [];
        HashSet<string> local = new(StringComparer.Ordinal);
        for (int i = 0; i < references.Length; i++)
        {
            string sceneId = references[i];
            RequireStableId(sceneId, $"{label}.pixelScenes[{i}]");
            Require(sceneIds.Contains(sceneId), $"{label}.pixelScenes[{i}] 引用了不存在的 scene {sceneId}。 ");
            Require(local.Add(sceneId), $"{label}.pixelScenes 重复引用 {sceneId}。 ");
            _ = referencedSceneIds.Add(sceneId);
        }
    }

    private static void ValidateStage(string value, string field)
    {
        Require(value is "active" or "planned", $"{field} 必须为 active 或 planned。");
    }

    private static void RequireStableId(string value, string field)
    {
        Require(!string.IsNullOrWhiteSpace(value), $"{field} 不能为空。");
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            bool valid = character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-';
            Require(valid, $"{field} 只能使用小写 ASCII、数字、短横线与下划线。");
        }
    }

    private static void RequireMaterialKey(string value, string field)
    {
        RequireStableId(value, field);
    }

    private static void RequireFiniteRange(double value, double minimum, double maximum, string field)
    {
        Require(double.IsFinite(value) && value >= minimum && value <= maximum, $"{field} 必须位于 [{minimum},{maximum}]。");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException($"biomes.json 配置无效：{message}");
        }
    }
}

internal sealed class BiomeExpansionStages
{
    public string Surface { get; init; } = string.Empty;

    public string SideBiomes { get; init; } = string.Empty;

    public string SecretConnections { get; init; } = string.Empty;

    public string ParallelWorlds { get; init; } = string.Empty;

    public string NewGamePlus { get; init; } = string.Empty;
}

internal sealed class HolyMountainDefinition
{
    public float BaseTemperature { get; init; }

    public string ShellMaterial { get; init; } = string.Empty;

    public string PlatformMaterial { get; init; } = string.Empty;

    public int ShellThicknessCells { get; init; }

    public HolyMountainOperationDefinition[] LayoutOperations { get; init; } = [];

    public HolyMountainLandmarkDefinition[] Landmarks { get; init; } = [];
}

internal sealed class HolyMountainOperationDefinition
{
    public string Kind { get; init; } = string.Empty;

    public string Material { get; init; } = string.Empty;

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}

internal sealed class HolyMountainLandmarkDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public int OffsetXCells { get; init; }

    public int LocalDepthCells { get; init; }
}

internal sealed class PortalNetworkDefinition
{
    public int PortalsPerHolyMountain { get; init; }

    public int SpacingCells { get; init; }

    public int SourceOffsetAboveBoundaryCells { get; init; }

    public int DestinationLocalDepthCells { get; init; }

    public int DestinationOffsetCells { get; init; }

    public int TriggerHalfWidthCells { get; init; }

    public int TriggerHalfHeightCells { get; init; }

    public string EyeShellMaterial { get; init; } = string.Empty;

    public string TeleportatiumMaterial { get; init; } = string.Empty;

    public int BasinTopOffsetCells { get; init; }

    public int BasinHalfWidthCells { get; init; }

    public int BasinDepthCells { get; init; }

    public int MinimumPowerCells { get; init; }

    public double TransitionSeconds { get; init; }

    public double CooldownSeconds { get; init; }

    public double InvulnerabilitySeconds { get; init; }
}

internal sealed class BiomeDefinition
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public float BaseTemperature { get; init; }

    public double HazardFrequency { get; init; }

    public BiomeMaterialPaletteDefinition Palette { get; init; } = new();

    public BiomeTerrainGrammarDefinition Grammar { get; init; } = new();

    public string[] PixelScenes { get; init; } = [];
}

internal sealed class BiomeMaterialPaletteDefinition
{
    public string Primary { get; init; } = string.Empty;

    public string Secondary { get; init; } = string.Empty;

    public string Loose { get; init; } = string.Empty;

    public string Structure { get; init; } = string.Empty;

    public string Hazard { get; init; } = string.Empty;

    public string Pool { get; init; } = string.Empty;
}

internal sealed class BiomeTerrainGrammarDefinition
{
    public string Kind { get; init; } = string.Empty;

    public int TileSizeCells { get; init; }

    public int CorridorHalfWidthCells { get; init; }

    public int ChamberRadiusCells { get; init; }

    public double EdgeOpenChance { get; init; }

    public double NoiseOpenThreshold { get; init; }

    public double HorizontalScale { get; init; }

    public double VerticalScale { get; init; }

    public double VerticalBias { get; init; }
}

internal sealed class BiomePixelSceneDefinition
{
    public string Id { get; init; } = string.Empty;

    public int WidthCells { get; init; }

    public int HeightCells { get; init; }

    public double SpawnChance { get; init; }

    public string EncounterId { get; init; } = string.Empty;

    public BiomePixelSceneOperationDefinition[] Operations { get; init; } = [];
}

internal sealed class BiomePixelSceneOperationDefinition
{
    public string Kind { get; init; } = string.Empty;

    public string Material { get; init; } = string.Empty;

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}

internal sealed class BiomeConnectionDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Stage { get; init; } = string.Empty;

    public string From { get; init; } = string.Empty;

    public string To { get; init; } = string.Empty;

    public string Side { get; init; } = string.Empty;

    public int OffsetCells { get; init; }

    public int HalfWidthCells { get; init; }

    public int CorridorHalfWidthCells { get; init; }

    public int FromLocalDepthCells { get; init; }

    public int ToLocalDepthCells { get; init; }

    public string SideBiome { get; init; } = string.Empty;

    public string GateMaterial { get; init; } = string.Empty;
}
