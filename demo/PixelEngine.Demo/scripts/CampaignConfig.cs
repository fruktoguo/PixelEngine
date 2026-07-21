using System.Text.Json;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 原创 Roguelite 战役的纵深拓扑与 run 初始参数；由 content/campaign.json 提供。
/// </summary>
public sealed class CampaignConfig
{
    /// <summary>当前支持的 schema 版本。</summary>
    public const int CurrentSchemaVersion = 1;

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

    /// <summary>每个主路径区域带的高度。</summary>
    public int RegionHeightCells { get; init; }

    /// <summary>相邻区域之间 Still Forge 带的高度。</summary>
    public int ForgeHeightCells { get; init; }

    /// <summary>确定性主通道半宽。</summary>
    public int MainPathHalfWidthCells { get; init; }

    /// <summary>主通道相对原点的入口 X。</summary>
    public int MainPathEntranceX { get; init; }

    /// <summary>主通道横向摆动的最大幅度。</summary>
    public int MainPathWanderCells { get; init; }

    /// <summary>Still Forge 主房间半宽。</summary>
    public int ForgeHalfWidthCells { get; init; }

    /// <summary>Still Forge 边界材质稳定字符串键。</summary>
    public string ForgeShellMaterial { get; init; } = string.Empty;

    /// <summary>Still Forge 平台材质稳定字符串键。</summary>
    public string ForgePlatformMaterial { get; init; } = string.Empty;

    /// <summary>按纵深排列的八个原创区域。</summary>
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

            regions[regionIndex++] = new CampaignRegionDefinition
            {
                Id = ReadString(regionElement, "id"),
                DisplayName = ReadString(regionElement, "displayName"),
                RockMaterial = ReadString(regionElement, "rockMaterial"),
                LooseMaterial = ReadString(regionElement, "looseMaterial"),
                HazardMaterial = ReadString(regionElement, "hazardMaterial"),
                HazardFrequency = ReadDouble(regionElement, "hazardFrequency"),
                BaseTemperature = ReadSingle(regionElement, "baseTemperature"),
            };
        }

        return new CampaignConfig
        {
            SchemaVersion = ReadInt32(root, "schemaVersion"),
            DefaultMode = ReadString(root, "defaultMode"),
            InitialRunSeed = ReadUInt64(root, "initialRunSeed"),
            SurfaceY = ReadInt32(root, "surfaceY"),
            CampaignStartDepthCells = ReadInt32(root, "campaignStartDepthCells"),
            RegionHeightCells = ReadInt32(root, "regionHeightCells"),
            ForgeHeightCells = ReadInt32(root, "forgeHeightCells"),
            MainPathHalfWidthCells = ReadInt32(root, "mainPathHalfWidthCells"),
            MainPathEntranceX = ReadInt32(root, "mainPathEntranceX"),
            MainPathWanderCells = ReadInt32(root, "mainPathWanderCells"),
            ForgeHalfWidthCells = ReadInt32(root, "forgeHalfWidthCells"),
            ForgeShellMaterial = ReadString(root, "forgeShellMaterial"),
            ForgePlatformMaterial = ReadString(root, "forgePlatformMaterial"),
            Regions = regions,
        }.Validate();
    }

    /// <summary>
    /// 校验 schema、拓扑尺寸、模式、材质键与八区唯一性。
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
        Require(ForgeHeightCells is >= 64 and <= 256, "forgeHeightCells 必须位于 [64,256]。");
        Require(MainPathHalfWidthCells is >= 12 and <= 64, "mainPathHalfWidthCells 必须位于 [12,64]。");
        Require(Math.Abs(MainPathEntranceX) <= 512, "mainPathEntranceX 必须位于原点 512 cell 内。");
        Require(MainPathWanderCells is >= 0 and <= 512, "mainPathWanderCells 必须位于 [0,512]。");
        Require(ForgeHalfWidthCells is >= 96 and <= 384, "forgeHalfWidthCells 必须位于 [96,384]。");
        Require(ForgeHalfWidthCells > MainPathHalfWidthCells + 32, "forgeHalfWidthCells 必须显著宽于主通道。");
        RequireMaterialKey(ForgeShellMaterial, nameof(ForgeShellMaterial));
        RequireMaterialKey(ForgePlatformMaterial, nameof(ForgePlatformMaterial));
        CampaignRegionDefinition[] regions = Regions ??
            throw new InvalidDataException("campaign.json 配置无效：regions 不能为空。");
        Require(regions.Length == RequiredRegionCount, $"regions 必须恰好包含 {RequiredRegionCount} 个区域。");

        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int i = 0; i < regions.Length; i++)
        {
            CampaignRegionDefinition region = regions[i] ??
                throw new InvalidDataException($"campaign.json regions[{i}] 不能为空。");
            string label = string.IsNullOrWhiteSpace(region.Id) ? $"regions[{i}]" : region.Id;
            Require(!string.IsNullOrWhiteSpace(region.Id), $"{label}.id 不能为空。");
            Require(ids.Add(region.Id), $"区域 id 重复：{region.Id}。");
            Require(!string.IsNullOrWhiteSpace(region.DisplayName), $"{label}.displayName 不能为空。");
            RequireMaterialKey(region.RockMaterial, $"{label}.rockMaterial");
            RequireMaterialKey(region.LooseMaterial, $"{label}.looseMaterial");
            RequireMaterialKey(region.HazardMaterial, $"{label}.hazardMaterial");
            Require(region.HazardFrequency is >= 0.0 and <= 0.2, $"{label}.hazardFrequency 必须位于 [0,0.2]。");
            Require(float.IsFinite(region.BaseTemperature) && region.BaseTemperature is >= 0f and <= 255f, $"{label}.baseTemperature 必须位于 [0,255]。");
        }

        return this;
    }

    internal CampaignDepthLocation ResolveLocation(long worldY)
    {
        long depth = worldY - SurfaceY;
        if (depth < CampaignStartDepthCells)
        {
            return new CampaignDepthLocation(CampaignDepthKind.Surface, 0, -1, depth, depth);
        }

        long remaining = depth - CampaignStartDepthCells;
        for (int regionIndex = 0; regionIndex < RequiredRegionCount - 1; regionIndex++)
        {
            if (remaining < RegionHeightCells)
            {
                return new CampaignDepthLocation(CampaignDepthKind.Region, regionIndex, -1, depth, remaining);
            }

            remaining -= RegionHeightCells;
            if (remaining < ForgeHeightCells)
            {
                return new CampaignDepthLocation(CampaignDepthKind.StillForge, regionIndex, regionIndex, depth, remaining);
            }

            remaining -= ForgeHeightCells;
        }

        return new CampaignDepthLocation(CampaignDepthKind.Region, RequiredRegionCount - 1, -1, depth, remaining);
    }

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
            ForgeHeightCells = 128,
            MainPathHalfWidthCells = 22,
            MainPathEntranceX = 96,
            MainPathWanderCells = 176,
            ForgeHalfWidthCells = 176,
            ForgeShellMaterial = "boundary_stone",
            ForgePlatformMaterial = "metal",
            Regions =
            [
                Region("shattered-lode", "碎脉矿层 / Shattered Lode", "stone", "gravel", "water", 0.020, 12f),
                Region("ember-fungal-reach", "余烬菌谷 / Ember Fungal Reach", "dirt", "oil", "fire", 0.018, 58f),
                Region("frostfall-chasm", "霜降深谷 / Frostfall Chasm", "ice", "gravel", "water", 0.022, 0f),
                Region("ironworks-bastion", "铸炉堡垒 / Ironworks Bastion", "metal", "stone", "lava", 0.014, 82f),
                Region("rootsea-wilds", "根海雨林 / Rootsea Wilds", "dirt", "wood", "acid", 0.018, 24f),
                Region("reactive-vault", "反应穹库 / Reactive Vault", "glass", "metal", "acid", 0.020, 42f),
                Region("null-temple", "失序神殿 / Null Temple", "boundary_stone", "stone", "smoke", 0.016, 16f),
                Region("origin-crucible", "源核实验庭 / Origin Crucible", "metal", "crystal", "lava", 0.024, 96f),
            ],
        }.Validate();
    }

    private static CampaignRegionDefinition Region(
        string id,
        string displayName,
        string rock,
        string loose,
        string hazard,
        double hazardFrequency,
        float baseTemperature)
    {
        return new CampaignRegionDefinition
        {
            Id = id,
            DisplayName = displayName,
            RockMaterial = rock,
            LooseMaterial = loose,
            HazardMaterial = hazard,
            HazardFrequency = hazardFrequency,
            BaseTemperature = baseTemperature,
        };
    }

    private static void RequireMaterialKey(string value, string field)
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
/// 单个战役纵深区域的材质与环境参数。
/// </summary>
public sealed class CampaignRegionDefinition
{
    /// <summary>稳定区域 id。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>玩家可见的原创区域名。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>主要岩层材质名。</summary>
    public string RockMaterial { get; init; } = string.Empty;

    /// <summary>松散夹层材质名。</summary>
    public string LooseMaterial { get; init; } = string.Empty;

    /// <summary>环境危险材质名。</summary>
    public string HazardMaterial { get; init; } = string.Empty;

    /// <summary>危险矿囊出现频率。</summary>
    public double HazardFrequency { get; init; }

    /// <summary>区域基础温度。</summary>
    public float BaseTemperature { get; init; }
}

internal enum CampaignDepthKind : byte
{
    Surface,
    Region,
    StillForge,
}

internal readonly record struct CampaignDepthLocation(
    CampaignDepthKind Kind,
    int RegionIndex,
    int ForgeIndex,
    long DepthCells,
    long LocalDepthCells);
