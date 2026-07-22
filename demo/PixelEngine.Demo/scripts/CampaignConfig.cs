using System.Text.Json;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Noita 复刻战役的纵深拓扑与 run 初始参数；由 content/campaign.json 提供。
/// </summary>
public sealed class CampaignConfig
{
    /// <summary>当前支持的 schema 版本。</summary>
    public const int CurrentSchemaVersion = 5;

    private const int LegacySchemaVersion = 1;
    private const int TerrainSchemaVersion = 2;
    private const int RegionIdentitySchemaVersion = 3;
    private const int TerrainAuthoritySchemaVersion = 4;

    private static readonly int[] CanonicalRegionStartDepthCells =
    [
        0,
        1_536,
        3_072,
        5_120,
        6_656,
        8_704,
        10_752,
        13_312,
    ];

    private static readonly int[] CanonicalRegionHeightCells =
    [
        1_024,
        1_024,
        1_536,
        1_024,
        1_536,
        1_536,
        2_048,
        1_600,
    ];

    private static readonly string[] CanonicalRegionIds =
    [
        "mines",
        "coal-pits",
        "snowy-depths",
        "hiisi-base",
        "underground-jungle",
        "the-vault",
        "temple-of-the-art",
        "the-laboratory",
    ];

    private static readonly string[] CanonicalRegionDisplayNames =
    [
        "Mines",
        "Coal Pits",
        "Snowy Depths",
        "Hiisi Base",
        "Underground Jungle",
        "The Vault",
        "Temple of the Art",
        "The Laboratory",
    ];

    private static readonly string[] LegacyRegionIds =
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

    /// <summary>战役区域数量。</summary>
    public const int RequiredRegionCount = 8;

    /// <summary>配置 schema 版本。</summary>
    public int SchemaVersion { get; init; }

    /// <summary>主菜单默认模式；允许 campaign 或 infiniteSandbox。</summary>
    public string DefaultMode { get; init; } = string.Empty;

    /// <summary>首次启动使用的确定性 run seed。</summary>
    public ulong InitialRunSeed { get; init; }

    /// <summary>安全出生区地表 Y。</summary>
    public int SurfaceY { get; init; }

    /// <summary>地表以下多少 cell 开始第一个主路径区域。</summary>
    public int CampaignStartDepthCells { get; init; }

    /// <summary>v1-v4 等高地图的迁移高度；v5 运行时使用各 region 的 HeightCells。</summary>
    public int RegionHeightCells { get; init; }

    /// <summary>相邻区域之间 Holy Mountain 带的高度。</summary>
    public int HolyMountainHeightCells { get; init; }

    /// <summary>v1 脚本使用的 Holy Mountain 高度只读 alias。</summary>
    public int ForgeHeightCells => HolyMountainHeightCells;

    /// <summary>确定性主通道半宽。</summary>
    public int MainPathHalfWidthCells { get; init; }

    /// <summary>主通道相对原点的入口 X。</summary>
    public int MainPathEntranceX { get; init; }

    /// <summary>主通道横向摆动的最大幅度。</summary>
    public int MainPathWanderCells { get; init; }

    /// <summary>Holy Mountain 主房间半宽。</summary>
    public int HolyMountainHalfWidthCells { get; init; }

    /// <summary>v1 脚本使用的 Holy Mountain 半宽只读 alias。</summary>
    public int ForgeHalfWidthCells => HolyMountainHalfWidthCells;

    /// <summary>按 Noita 主路径顺序排列的八个区域。</summary>
    public CampaignRegionDefinition[] Regions { get; init; } = [];

    /// <summary>
    /// 经脚本公开 Config API 加载并校验正式配置。
    /// </summary>
    /// <param name="config">当前 ContentRoot 的配置 API。</param>
    /// <returns>已通过语义校验的战役配置。</returns>
    public static CampaignConfig Load(IConfigApi config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Parse(config.ReadText("campaign.json"));
    }

    private static CampaignConfig Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("campaign.json 根节点必须是对象。");
        }

        int sourceSchemaVersion = ReadInt32(root, "schemaVersion");
        if (sourceSchemaVersion is not (
            LegacySchemaVersion or
            TerrainSchemaVersion or
            RegionIdentitySchemaVersion or
            TerrainAuthoritySchemaVersion or
            CurrentSchemaVersion))
        {
            throw new InvalidDataException(
                $"campaign.json schemaVersion 必须位于 [{LegacySchemaVersion},{CurrentSchemaVersion}]。");
        }

        int campaignStartDepthCells = ReadInt32(root, "campaignStartDepthCells");
        int sourceRegionHeightCells = ReadInt32(root, "regionHeightCells");
        int sourceHolyMountainHeightCells = ReadInt32(
            root,
            sourceSchemaVersion == LegacySchemaVersion ? "forgeHeightCells" : "holyMountainHeightCells");
        int regionHeightCells = sourceSchemaVersion == CurrentSchemaVersion
            ? sourceRegionHeightCells
            : 512;
        int holyMountainHeightCells = sourceSchemaVersion == CurrentSchemaVersion
            ? sourceHolyMountainHeightCells
            : 512;
        JsonElement regionsElement = ReadRequired(root, "regions");
        if (regionsElement.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidDataException("campaign.json 配置无效：regions 不能为空。");
        }

        if (regionsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("campaign.json 字段 regions 必须是数组。");
        }

        CampaignRegionDefinition[] regions = new CampaignRegionDefinition[regionsElement.GetArrayLength()];
        int regionIndex = 0;
        foreach (JsonElement regionElement in regionsElement.EnumerateArray())
        {
            if (regionElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"campaign.json regions[{regionIndex}] 必须是对象。");
            }

            CampaignRegionDefinition region = new()
            {
                Id = ReadString(regionElement, "id"),
                DisplayName = ReadString(regionElement, "displayName"),
                LegacyIds = sourceSchemaVersion == LegacySchemaVersion
                    ? []
                    : ReadStringArray(regionElement, "legacyIds"),
                StartDepthCells = sourceSchemaVersion == CurrentSchemaVersion
                    ? ReadInt32(regionElement, "startDepthCells")
                    : checked(campaignStartDepthCells + CanonicalRegionStartDepthCells[regionIndex]),
                HeightCells = sourceSchemaVersion == CurrentSchemaVersion
                    ? ReadInt32(regionElement, "heightCells")
                    : CanonicalRegionHeightCells[regionIndex],
            };
            if (sourceSchemaVersion is LegacySchemaVersion or TerrainSchemaVersion)
            {
                _ = ReadString(regionElement, "rockMaterial");
                _ = ReadString(regionElement, "looseMaterial");
                _ = ReadString(regionElement, "hazardMaterial");
                _ = ReadDouble(regionElement, "hazardFrequency");
                _ = ReadSingle(regionElement, "baseTemperature");
            }

            regions[regionIndex] = sourceSchemaVersion == LegacySchemaVersion
                ? UpgradeLegacyRegion(region, regionIndex)
                : region;
            regionIndex++;
        }

        if (sourceSchemaVersion != CurrentSchemaVersion)
        {
            _ = ReadString(
                root,
                sourceSchemaVersion == LegacySchemaVersion ? "forgeShellMaterial" : "holyMountainShellMaterial");
            _ = ReadString(
                root,
                sourceSchemaVersion == LegacySchemaVersion ? "forgePlatformMaterial" : "holyMountainPlatformMaterial");
        }

        return new CampaignConfig
        {
            SchemaVersion = CurrentSchemaVersion,
            DefaultMode = ReadString(root, "defaultMode"),
            InitialRunSeed = ReadUInt64(root, "initialRunSeed"),
            SurfaceY = ReadInt32(root, "surfaceY"),
            CampaignStartDepthCells = campaignStartDepthCells,
            RegionHeightCells = regionHeightCells,
            HolyMountainHeightCells = holyMountainHeightCells,
            MainPathHalfWidthCells = ReadInt32(root, "mainPathHalfWidthCells"),
            MainPathEntranceX = ReadInt32(root, "mainPathEntranceX"),
            MainPathWanderCells = ReadInt32(root, "mainPathWanderCells"),
            HolyMountainHalfWidthCells = ReadInt32(
                root,
                sourceSchemaVersion == LegacySchemaVersion ? "forgeHalfWidthCells" : "holyMountainHalfWidthCells"),
            Regions = regions,
        }.Validate();
    }

    /// <summary>
    /// 校验 schema、拓扑尺寸、模式与八区唯一性。
    /// </summary>
    /// <returns>当前已校验配置。</returns>
    public CampaignConfig Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"campaign.json schemaVersion 必须为 {CurrentSchemaVersion}。");
        }

        if (!string.Equals(DefaultMode, "campaign", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(DefaultMode, "infiniteSandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("campaign.json defaultMode 必须为 campaign 或 infiniteSandbox。");
        }

        Require(InitialRunSeed != 0, "initialRunSeed 不能为 0。");
        Require(SurfaceY == PlayableCavernWorldGenerator.SafeSurfaceY, $"surfaceY 必须与安全出生区 {PlayableCavernWorldGenerator.SafeSurfaceY} 一致。");
        Require(CampaignStartDepthCells is >= 0 and <= 64, "campaignStartDepthCells 必须位于 [0,64]。");
        Require(RegionHeightCells is >= 256 and <= 2_048, "regionHeightCells 必须位于 [256,2048]。");
        Require(HolyMountainHeightCells is >= 64 and <= 512, "holyMountainHeightCells 必须位于 [64,512]。");
        Require(MainPathHalfWidthCells is >= 12 and <= 64, "mainPathHalfWidthCells 必须位于 [12,64]。");
        Require(Math.Abs(MainPathEntranceX) <= 512, "mainPathEntranceX 必须位于原点 512 cell 内。");
        Require(MainPathWanderCells is >= 0 and <= 512, "mainPathWanderCells 必须位于 [0,512]。");
        Require(HolyMountainHalfWidthCells is >= 96 and <= 384, "holyMountainHalfWidthCells 必须位于 [96,384]。");
        Require(HolyMountainHalfWidthCells > MainPathHalfWidthCells + 32, "holyMountainHalfWidthCells 必须显著宽于主通道。");
        CampaignRegionDefinition[] regions = Regions ??
            throw new InvalidDataException("campaign.json 配置无效：regions 不能为空。");
        Require(regions.Length == RequiredRegionCount, $"regions 必须恰好包含 {RequiredRegionCount} 个区域。");

        HashSet<string> idsAndAliases = new(StringComparer.Ordinal);
        int expectedStartDepthCells = CampaignStartDepthCells;
        for (int i = 0; i < regions.Length; i++)
        {
            CampaignRegionDefinition region = regions[i] ??
                throw new InvalidDataException($"campaign.json regions[{i}] 不能为空。");
            string label = string.IsNullOrWhiteSpace(region.Id) ? $"regions[{i}]" : region.Id;
            Require(!string.IsNullOrWhiteSpace(region.Id), $"{label}.id 不能为空。");
            Require(
                string.Equals(region.Id, CanonicalRegionIds[i], StringComparison.Ordinal),
                $"regions[{i}].id 必须为 {CanonicalRegionIds[i]}。");
            RequireStableId(region.Id, $"{label}.id");
            Require(idsAndAliases.Add(region.Id), $"区域 id 或 alias 重复：{region.Id}。");
            Require(!string.IsNullOrWhiteSpace(region.DisplayName), $"{label}.displayName 不能为空。");
            string[] legacyIds = region.LegacyIds ??
                throw new InvalidDataException($"campaign.json 配置无效：{label}.legacyIds 不能为空。");
            bool containsRequiredLegacyId = false;
            for (int aliasIndex = 0; aliasIndex < legacyIds.Length; aliasIndex++)
            {
                string alias = legacyIds[aliasIndex];
                RequireStableId(alias, $"{label}.legacyIds[{aliasIndex}]");
                Require(idsAndAliases.Add(alias), $"区域 id 或 alias 重复：{alias}。");
                containsRequiredLegacyId |= string.Equals(alias, LegacyRegionIds[i], StringComparison.Ordinal);
            }

            Require(containsRequiredLegacyId, $"{label}.legacyIds 必须包含历史 id {LegacyRegionIds[i]}。");
            Require(
                region.StartDepthCells == expectedStartDepthCells,
                $"{label}.startDepthCells 必须为 {expectedStartDepthCells}，以保持主路径与 Holy Mountain 连续。");
            Require(
                region.HeightCells is >= 256 and <= 2_048,
                $"{label}.heightCells 必须位于 [256,2048]。");
            expectedStartDepthCells = checked(
                region.StartDepthCells +
                region.HeightCells +
                (i < RequiredRegionCount - 1 ? HolyMountainHeightCells : 0));
        }

        return this;
    }

    /// <summary>
    /// 将 canonical biome id 或历史 alias 解析为固定主路径索引。
    /// </summary>
    /// <param name="id">canonical id 或历史 alias。</param>
    /// <param name="regionIndex">解析成功时的区域索引。</param>
    /// <returns>是否找到对应区域。</returns>
    public bool TryResolveRegionIndex(string id, out int regionIndex)
    {
        regionIndex = -1;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        for (int i = 0; i < Regions.Length; i++)
        {
            CampaignRegionDefinition region = Regions[i];
            if (string.Equals(id, region.Id, StringComparison.Ordinal))
            {
                regionIndex = i;
                return true;
            }

            string[] aliases = region.LegacyIds;
            for (int aliasIndex = 0; aliasIndex < aliases.Length; aliasIndex++)
            {
                if (string.Equals(id, aliases[aliasIndex], StringComparison.Ordinal))
                {
                    regionIndex = i;
                    return true;
                }
            }
        }

        return false;
    }

    internal CampaignDepthLocation ResolveLocation(long worldY)
    {
        long depth = worldY - SurfaceY;
        if (depth < Regions[0].StartDepthCells)
        {
            return new CampaignDepthLocation(CampaignDepthKind.Surface, 0, -1, depth, depth);
        }

        for (int regionIndex = 0; regionIndex < RequiredRegionCount; regionIndex++)
        {
            CampaignRegionDefinition region = Regions[regionIndex];
            if (depth < region.StartDepthCells)
            {
                CampaignRegionDefinition previous = Regions[regionIndex - 1];
                long holyMountainStart = (long)previous.StartDepthCells + previous.HeightCells;
                return new CampaignDepthLocation(
                    CampaignDepthKind.HolyMountain,
                    regionIndex - 1,
                    regionIndex - 1,
                    depth,
                    depth - holyMountainStart);
            }

            long localDepth = depth - region.StartDepthCells;
            if (localDepth < region.HeightCells || regionIndex == RequiredRegionCount - 1)
            {
                return new CampaignDepthLocation(
                    CampaignDepthKind.Region,
                    regionIndex,
                    -1,
                    depth,
                    localDepth);
            }
        }

        throw new InvalidOperationException("战役纵深解析未覆盖当前坐标。");
    }

    internal long RegionStartCellY(int regionIndex)
    {
        return (uint)regionIndex >= RequiredRegionCount
            ? throw new ArgumentOutOfRangeException(nameof(regionIndex))
            : checked(SurfaceY + (long)Regions[regionIndex].StartDepthCells);
    }

    internal int RegionHeightCellsAt(int regionIndex)
    {
        return (uint)regionIndex >= RequiredRegionCount
            ? throw new ArgumentOutOfRangeException(nameof(regionIndex))
            : Regions[regionIndex].HeightCells;
    }

    internal long HolyMountainStartCellY(int holyMountainIndex)
    {
        return (uint)holyMountainIndex >= RequiredRegionCount - 1
            ? throw new ArgumentOutOfRangeException(nameof(holyMountainIndex))
            : checked(RegionStartCellY(holyMountainIndex) + RegionHeightCellsAt(holyMountainIndex));
    }

    internal long CampaignEndDepthCells => checked(
        (long)Regions[^1].StartDepthCells + Regions[^1].HeightCells);

    internal static CampaignConfig BuiltinDefault { get; } = CreateBuiltinDefault();

    private static CampaignConfig CreateBuiltinDefault()
    {
        return new CampaignConfig
        {
            SchemaVersion = CurrentSchemaVersion,
            DefaultMode = "campaign",
            InitialRunSeed = PlayableCavernWorldGenerator.Seed,
            SurfaceY = PlayableCavernWorldGenerator.SafeSurfaceY,
            CampaignStartDepthCells = 0,
            RegionHeightCells = 512,
            HolyMountainHeightCells = 512,
            MainPathHalfWidthCells = 22,
            MainPathEntranceX = 96,
            MainPathWanderCells = 176,
            HolyMountainHalfWidthCells = 176,
            Regions =
            [
                Region(0),
                Region(1),
                Region(2),
                Region(3),
                Region(4),
                Region(5),
                Region(6),
                Region(7),
            ],
        }.Validate();
    }

    private static CampaignRegionDefinition Region(int index)
    {
        return new CampaignRegionDefinition
        {
            Id = CanonicalRegionIds[index],
            DisplayName = CanonicalRegionDisplayNames[index],
            LegacyIds = [LegacyRegionIds[index]],
            StartDepthCells = CanonicalRegionStartDepthCells[index],
            HeightCells = CanonicalRegionHeightCells[index],
        };
    }

    private static CampaignRegionDefinition UpgradeLegacyRegion(CampaignRegionDefinition region, int index)
    {
        if ((uint)index >= RequiredRegionCount)
        {
            return region;
        }

        string canonicalId = CanonicalRegionIds[index];
        string legacyId = LegacyRegionIds[index];
        bool isKnownId = string.Equals(region.Id, canonicalId, StringComparison.Ordinal) ||
            string.Equals(region.Id, legacyId, StringComparison.Ordinal);
        return isKnownId
            ? new CampaignRegionDefinition
            {
                Id = canonicalId,
                DisplayName = CanonicalRegionDisplayNames[index],
                LegacyIds = [legacyId],
                StartDepthCells = region.StartDepthCells,
                HeightCells = region.HeightCells,
            }
            : region;
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException($"campaign.json 配置无效：{message}");
        }
    }

    private static JsonElement ReadRequired(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new InvalidDataException($"campaign.json 缺少必需字段 {name}。");
    }

    private static string ReadString(JsonElement element, string name)
    {
        JsonElement value = ReadRequired(element, name);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw new InvalidDataException($"campaign.json 字段 {name} 必须是字符串。");
    }

    private static string[] ReadStringArray(JsonElement element, string name)
    {
        JsonElement value = ReadRequired(element, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"campaign.json 字段 {name} 必须是字符串数组。");
        }

        string[] result = new string[value.GetArrayLength()];
        int index = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"campaign.json 字段 {name}[{index}] 必须是字符串。");
            }

            result[index++] = item.GetString() ?? string.Empty;
        }

        return result;
    }

    private static int ReadInt32(JsonElement element, string name)
    {
        JsonElement value = ReadRequired(element, name);
        return value.TryGetInt32(out int result)
            ? result
            : throw new InvalidDataException($"campaign.json 字段 {name} 必须是 Int32 整数。");
    }

    private static ulong ReadUInt64(JsonElement element, string name)
    {
        JsonElement value = ReadRequired(element, name);
        return value.TryGetUInt64(out ulong result)
            ? result
            : throw new InvalidDataException($"campaign.json 字段 {name} 必须是 UInt64 整数。");
    }

    private static double ReadDouble(JsonElement element, string name)
    {
        JsonElement value = ReadRequired(element, name);
        return value.TryGetDouble(out double result) && double.IsFinite(result)
            ? result
            : throw new InvalidDataException($"campaign.json 字段 {name} 必须是有限数值。");
    }

    private static float ReadSingle(JsonElement element, string name)
    {
        JsonElement value = ReadRequired(element, name);
        return value.TryGetSingle(out float result) && float.IsFinite(result)
            ? result
            : throw new InvalidDataException($"campaign.json 字段 {name} 必须是有限 Single 数值。");
    }
}

/// <summary>
/// 单个战役纵深区域的稳定身份；材质与生成语法由 biomes.json 独占。
/// </summary>
public sealed class CampaignRegionDefinition
{
    /// <summary>稳定区域 id。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>玩家可见的 Noita canonical biome 名。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>转向前区域 id 等历史读取 alias；新数据始终写入 canonical id。</summary>
    public string[] LegacyIds { get; init; } = [];

    /// <summary>相对安全地表的区域起始纵深，单位 cell。</summary>
    public int StartDepthCells { get; init; }

    /// <summary>区域纵深跨度，单位 cell。</summary>
    public int HeightCells { get; init; }

}

internal enum CampaignDepthKind : byte
{
    Surface,
    Region,
    HolyMountain,
    StillForge = HolyMountain,
}

internal readonly record struct CampaignDepthLocation(
    CampaignDepthKind Kind,
    int RegionIndex,
    int HolyMountainIndex,
    long DepthCells,
    long LocalDepthCells)
{
    /// <summary>v1 脚本使用的 Holy Mountain 索引只读 alias。</summary>
    public int ForgeIndex => HolyMountainIndex;
}
