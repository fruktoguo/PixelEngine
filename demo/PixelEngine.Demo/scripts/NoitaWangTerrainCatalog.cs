using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Noita Build 17130612 的 Herringbone Wang 模板派生目录；只保存可验证的语义像素，
/// 不在运行时读取 Noita 安装目录或原始资产。
/// </summary>
internal sealed class NoitaWangTerrainCatalog
{
    internal const int CurrentSchemaVersion = 1;
    internal const byte MarkerSemanticBase = 32;
    private const string EmbeddedResourceName = "PixelEngine.Demo.noita-wang-terrain.json";
    private const string RequiredReferenceBuildId = "17130612";
    private const string RequiredReferenceVersionHash = "9dbd52ced019a643169a2db02f46c77f8766c6e5";
    private const string RequiredAlgorithm = "stb-herringbone-wang-corner-v1";
    private const int BinaryHeaderLength = 19;

    private static readonly string[] RequiredReferenceBiomeIds =
    [
        "coalmine",
        "coalmine-alt",
        "excavationsite",
        "excavationsite-cube-chamber",
        "fungicave",
        "fungiforest",
        "snowcave",
        "snowcave-secret-chamber",
        "snowcastle",
        "snowcastle-hourglass-chamber",
        "snowcastle-cavern",
        "rainforest",
        "rainforest-open",
        "rainforest-dark",
        "vault",
        "vault-frozen",
        "crypt",
        "wandcave",
        "wizardcave",
        "wizardcave-entrance",
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly Lazy<NoitaWangTerrainCatalog> Builtin = new(LoadBuiltin, isThreadSafe: true);

    public int SchemaVersion { get; init; }

    public string ReferenceBuildId { get; init; } = string.Empty;

    public string ReferenceVersionHash { get; init; } = string.Empty;

    public string Algorithm { get; init; } = string.Empty;

    public string AlgorithmLicensePath { get; init; } = string.Empty;

    public string AlgorithmLicenseSha256 { get; init; } = string.Empty;

    public string SourceMaterialsPath { get; init; } = string.Empty;

    public string SourceMaterialsSha256 { get; init; } = string.Empty;

    public string[] MaterialAliasConflicts { get; init; } = [];

    public NoitaWangTerrainSetDefinition[] Sets { get; init; } = [];

    internal static NoitaWangTerrainCatalog BuiltinDefault => Builtin.Value;

    internal static NoitaWangTerrainCatalog Load(IConfigApi config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Parse(config.ReadText("noita-wang-terrain.json"));
    }

    internal static NoitaWangTerrainCatalog Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            NoitaWangTerrainCatalog catalog =
                JsonSerializer.Deserialize<NoitaWangTerrainCatalog>(json, SerializerOptions) ??
                throw new InvalidDataException("noita-wang-terrain.json 根节点不能为 null。");
            return catalog.Validate();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"noita-wang-terrain.json JSON 无效：{exception.Message}",
                exception);
        }
    }

    internal DecodedNoitaWangTerrainSet FindForReferenceBiome(string referenceBiomeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceBiomeId);
        NoitaWangTerrainSetDefinition[] sets = Sets;
        for (int setIndex = 0; setIndex < sets.Length; setIndex++)
        {
            string[] referenceBiomeIds = sets[setIndex].ReferenceBiomeIds;
            for (int biomeIndex = 0; biomeIndex < referenceBiomeIds.Length; biomeIndex++)
            {
                if (string.Equals(referenceBiomeIds[biomeIndex], referenceBiomeId, StringComparison.Ordinal))
                {
                    return sets[setIndex].Decoded;
                }
            }
        }

        throw new InvalidOperationException($"参考 biome {referenceBiomeId} 缺少 Noita Wang 模板绑定。");
    }

    private static NoitaWangTerrainCatalog LoadBuiltin()
    {
        using Stream stream = typeof(NoitaWangTerrainCatalog).Assembly.GetManifestResourceStream(EmbeddedResourceName) ??
            throw new InvalidOperationException($"Demo 程序集缺少嵌入资源 {EmbeddedResourceName}。");
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd());
    }

    private NoitaWangTerrainCatalog Validate()
    {
        Require(SchemaVersion == CurrentSchemaVersion, $"schemaVersion 必须为 {CurrentSchemaVersion}。");
        Require(
            string.Equals(ReferenceBuildId, RequiredReferenceBuildId, StringComparison.Ordinal),
            $"referenceBuildId 必须为 {RequiredReferenceBuildId}。");
        Require(
            string.Equals(ReferenceVersionHash, RequiredReferenceVersionHash, StringComparison.Ordinal),
            "referenceVersionHash 与权威解包版本不一致。");
        Require(string.Equals(Algorithm, RequiredAlgorithm, StringComparison.Ordinal), $"algorithm 必须为 {RequiredAlgorithm}。");
        Require(
            string.Equals(AlgorithmLicensePath, "licenses/stb_herringbone_wang_tile.txt", StringComparison.Ordinal),
            "algorithmLicensePath 必须绑定 Noita 随附的 STB license。");
        Require(IsSha256(AlgorithmLicenseSha256), "algorithmLicenseSha256 必须为 64 位 SHA256 hex。");
        Require(string.Equals(SourceMaterialsPath, "data/materials.xml", StringComparison.Ordinal), "sourceMaterialsPath 必须为 data/materials.xml。");
        Require(IsSha256(SourceMaterialsSha256), "sourceMaterialsSha256 必须为 64 位 SHA256 hex。");
        _ = MaterialAliasConflicts ?? throw new InvalidDataException("noita-wang-terrain.json 配置无效：materialAliasConflicts 不能为空。");

        NoitaWangTerrainSetDefinition[] sets = Sets ??
            throw new InvalidDataException("noita-wang-terrain.json 配置无效：sets 不能为空。");
        Require(sets.Length == 15, "sets 必须恰好包含 Noita 当前主区/侧区使用的 15 套 Wang 模板。");
        HashSet<string> setIds = new(StringComparer.Ordinal);
        HashSet<string> referenceBiomeIds = new(StringComparer.Ordinal);
        for (int i = 0; i < sets.Length; i++)
        {
            NoitaWangTerrainSetDefinition set = sets[i] ??
                throw new InvalidDataException($"noita-wang-terrain.json 配置无效：sets[{i}] 不能为空。");
            ValidateSet(set, i, setIds, referenceBiomeIds);
        }

        Require(referenceBiomeIds.Count == RequiredReferenceBiomeIds.Length, "Wang referenceBiomeIds 数量与权威绑定不一致。");
        for (int i = 0; i < RequiredReferenceBiomeIds.Length; i++)
        {
            Require(
                referenceBiomeIds.Contains(RequiredReferenceBiomeIds[i]),
                $"缺少参考 biome {RequiredReferenceBiomeIds[i]} 的 Wang 模板绑定。");
        }

        return this;
    }

    private static void ValidateSet(
        NoitaWangTerrainSetDefinition set,
        int setIndex,
        HashSet<string> setIds,
        HashSet<string> allReferenceBiomeIds)
    {
        string label = $"sets[{setIndex}]";
        RequireStableId(set.Id, $"{label}.id");
        Require(setIds.Add(set.Id), $"Wang set id 重复：{set.Id}。");
        string[] referenceBiomeIds = set.ReferenceBiomeIds ??
            throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label}.referenceBiomeIds 不能为空。");
        Require(referenceBiomeIds.Length > 0, $"{label}.referenceBiomeIds 不能为空数组。");
        for (int i = 0; i < referenceBiomeIds.Length; i++)
        {
            RequireStableId(referenceBiomeIds[i], $"{label}.referenceBiomeIds[{i}]");
            Require(allReferenceBiomeIds.Add(referenceBiomeIds[i]), $"Wang reference biome 绑定重复：{referenceBiomeIds[i]}。");
        }

        RequireSourcePath(set.SourceBiomePath, "data/biome/", ".xml", $"{label}.sourceBiomePath");
        RequireSourcePath(set.SourceWangPath, "data/wang_tiles/", ".png", $"{label}.sourceWangPath");
        RequireSourcePath(set.SpawnSourcePath, "data/scripts/biomes/", ".lua", $"{label}.spawnSourcePath");
        Require(IsSha256(set.SourceBiomeSha256), $"{label}.sourceBiomeSha256 必须为 64 位 SHA256 hex。");
        Require(IsSha256(set.SourceWangSha256), $"{label}.sourceWangSha256 必须为 64 位 SHA256 hex。");
        Require(IsSha256(set.SpawnSourceSha256), $"{label}.spawnSourceSha256 必须为 64 位 SHA256 hex。");

        Require(set.ShortSide is >= 1 and <= 64, $"{label}.shortSide 必须位于 [1,64]。");
        Require(set.VaryX is >= 1 and <= 64 && set.VaryY is >= 1 and <= 64, $"{label}.varyX/varyY 必须位于 [1,64]。");
        int[] colors = set.CornerColors ??
            throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label}.cornerColors 不能为空。");
        Require(colors.Length == 4, $"{label}.cornerColors 必须恰好包含 4 项。");
        for (int i = 0; i < colors.Length; i++)
        {
            Require(colors[i] is >= 1 and <= 32, $"{label}.cornerColors[{i}] 必须位于 [1,32]。");
        }

        int horizontalTilesPerRow = checked(colors[1] * colors[2] * colors[3] * set.VaryX);
        int horizontalRows = checked(colors[0] * colors[1] * colors[2] * set.VaryY);
        int verticalTilesPerRow = checked(colors[0] * colors[3] * colors[2] * set.VaryY);
        int verticalRows = checked(colors[1] * colors[0] * colors[3] * set.VaryX);
        int expectedHorizontalCount = checked(horizontalTilesPerRow * horizontalRows);
        int expectedVerticalCount = checked(verticalTilesPerRow * verticalRows);
        int expectedWidth = Math.Max(
            checked(horizontalTilesPerRow * ((2 * set.ShortSide) + 3)),
            checked(verticalTilesPerRow * (set.ShortSide + 3)));
        int expectedHeight = checked(
            2 +
            (horizontalRows * (set.ShortSide + 3)) +
            2 +
            (verticalRows * ((2 * set.ShortSide) + 3)));
        Require(set.HorizontalTileCount == expectedHorizontalCount, $"{label}.horizontalTileCount 与 STB 模板头不一致。");
        Require(set.VerticalTileCount == expectedVerticalCount, $"{label}.verticalTileCount 与 STB 模板头不一致。");
        Require(set.SourceWidth == expectedWidth && set.SourceHeight == expectedHeight, $"{label}.sourceWidth/sourceHeight 与 STB 模板布局不一致。");

        ValidateColors(set.RandomBinaryColors, $"{label}.randomBinaryColors");
        ValidateMaterialMappings(set.MaterialMappings, $"{label}.materialMappings");
        ValidateMarkers(set.Markers, $"{label}.markers");
        Require(string.Equals(set.Encoding, "brotli-pewh-v1", StringComparison.Ordinal), $"{label}.encoding 必须为 brotli-pewh-v1。");
        Require(set.DecodedLength > BinaryHeaderLength, $"{label}.decodedLength 非法。");
        Require(IsSha256(set.DecodedSha256), $"{label}.decodedSha256 必须为 64 位 SHA256 hex。");
        byte[] decoded = DecodeBrotli(set.Data, set.DecodedLength, set.DecodedSha256, label);
        set.Decoded = ParseDecodedSet(set, decoded, label);
    }

    private static DecodedNoitaWangTerrainSet ParseDecodedSet(
        NoitaWangTerrainSetDefinition definition,
        byte[] decoded,
        string label)
    {
        Require(decoded.AsSpan(0, 4).SequenceEqual("PWH1"u8), $"{label}.data 缺少 PWH1 头。");
        Require(decoded[4] == definition.ShortSide, $"{label}.data shortSide 与 JSON 不一致。");
        for (int i = 0; i < 4; i++)
        {
            Require(decoded[5 + i] == definition.CornerColors[i], $"{label}.data cornerColors[{i}] 与 JSON 不一致。");
        }

        Require(decoded[9] == definition.VaryX && decoded[10] == definition.VaryY, $"{label}.data varyX/varyY 与 JSON 不一致。");
        int horizontalCount = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(11, 4));
        int verticalCount = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(15, 4));
        Require(horizontalCount == definition.HorizontalTileCount, $"{label}.data horizontalTileCount 与 JSON 不一致。");
        Require(verticalCount == definition.VerticalTileCount, $"{label}.data verticalTileCount 与 JSON 不一致。");
        int tileArea = checked(2 * definition.ShortSide * definition.ShortSide);
        int recordSize = checked(sizeof(uint) + tileArea);
        int expectedLength = checked(BinaryHeaderLength + ((horizontalCount + verticalCount) * recordSize));
        Require(decoded.Length == expectedLength, $"{label}.data 长度与 tile 数量不一致。");

        uint[] horizontalKeys = new uint[horizontalCount];
        int[] horizontalOffsets = new int[horizontalCount];
        uint[] verticalKeys = new uint[verticalCount];
        int[] verticalOffsets = new int[verticalCount];
        int offset = BinaryHeaderLength;
        ParseTileRecords(
            decoded,
            ref offset,
            horizontalKeys,
            horizontalOffsets,
            tileArea,
            definition.CornerColors,
            [1, 2, 3, 0, 1, 2],
            definition.VaryX * definition.VaryY,
            definition.Markers.Length,
            $"{label}.horizontal");
        ParseTileRecords(
            decoded,
            ref offset,
            verticalKeys,
            verticalOffsets,
            tileArea,
            definition.CornerColors,
            [0, 3, 2, 1, 0, 3],
            definition.VaryX * definition.VaryY,
            definition.Markers.Length,
            $"{label}.vertical");
        Require(offset == decoded.Length, $"{label}.data 含未消费尾部数据。");
        return new DecodedNoitaWangTerrainSet(
            definition.Id,
            definition.ShortSide,
            definition.CornerColors,
            decoded,
            horizontalKeys,
            horizontalOffsets,
            verticalKeys,
            verticalOffsets);
    }

    private static void ParseTileRecords(
        byte[] decoded,
        ref int offset,
        uint[] keys,
        int[] pixelOffsets,
        int tileArea,
        int[] cornerColors,
        ReadOnlySpan<int> constraintTypes,
        int expectedVariants,
        int markerCount,
        string label)
    {
        uint previous = 0;
        int uniqueKeys = 0;
        int runLength = 0;
        for (int i = 0; i < keys.Length; i++)
        {
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(decoded.AsSpan(offset, sizeof(uint)));
            offset += sizeof(uint);
            Require(i == 0 || key >= previous, $"{label} tile key 必须升序。");
            for (int field = 0; field < constraintTypes.Length; field++)
            {
                int value = (int)((key >> (field * 5)) & 31u);
                Require(value < cornerColors[constraintTypes[field]], $"{label}[{i}] constraint {field} 越界。");
            }

            if (i == 0 || key != previous)
            {
                if (i > 0)
                {
                    Require(runLength == expectedVariants, $"{label} key 0x{previous:x8} 的 variant 数量不正确。");
                }

                uniqueKeys++;
                runLength = 1;
            }
            else
            {
                runLength++;
            }

            keys[i] = key;
            pixelOffsets[i] = offset;
            ValidateSemanticPixels(decoded.AsSpan(offset, tileArea), markerCount, $"{label}[{i}]");
            offset += tileArea;
            previous = key;
        }

        if (keys.Length > 0)
        {
            Require(runLength == expectedVariants, $"{label} key 0x{previous:x8} 的 variant 数量不正确。");
        }

        int expectedUniqueKeys = 1;
        for (int i = 0; i < constraintTypes.Length; i++)
        {
            expectedUniqueKeys = checked(expectedUniqueKeys * cornerColors[constraintTypes[i]]);
        }

        Require(uniqueKeys == expectedUniqueKeys, $"{label} 未覆盖全部 corner constraint 组合。");
    }

    private static void ValidateSemanticPixels(ReadOnlySpan<byte> pixels, int markerCount, string label)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            byte semantic = pixels[i];
            bool terrainSemantic = semantic is <= (byte)NoitaWangTerrainSemantic.Pool or
                (byte)NoitaWangTerrainSemantic.RandomBinary;
            bool markerSemantic = semantic >= MarkerSemanticBase && semantic - MarkerSemanticBase < markerCount;
            Require(terrainSemantic || markerSemantic, $"{label} 含未知 semantic {semantic}。");
        }
    }

    private static byte[] DecodeBrotli(string data, int decodedLength, string expectedSha256, string label)
    {
        byte[] compressed;
        try
        {
            compressed = Convert.FromBase64String(data);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label}.data 不是合法 Base64。", exception);
        }

        Require(compressed.Length > 0, $"{label}.data 不能为空。");
        byte[] decoded = new byte[decodedLength];
        using MemoryStream source = new(compressed, writable: false);
        using BrotliStream brotli = new(source, CompressionMode.Decompress, leaveOpen: false);
        int offset = 0;
        while (offset < decoded.Length)
        {
            int read = brotli.Read(decoded, offset, decoded.Length - offset);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        Require(offset == decoded.Length && brotli.ReadByte() < 0, $"{label}.data 解压后必须恰好为 {decodedLength} 字节。");
        string actualSha256 = Convert.ToHexString(SHA256.HashData(decoded));
        Require(string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase), $"{label}.decodedSha256 与解码内容不一致。");
        return decoded;
    }

    private static void ValidateColors(string[] colors, string label)
    {
        if (colors is null)
        {
            throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label} 不能为空。");
        }

        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < colors.Length; i++)
        {
            Require(IsArgb(colors[i]), $"{label}[{i}] 必须为 8 位 ARGB hex。");
            Require(unique.Add(colors[i]), $"{label} 颜色重复：{colors[i]}。");
        }
    }

    private static void ValidateMaterialMappings(NoitaWangMaterialMappingDefinition[] mappings, string label)
    {
        if (mappings is null)
        {
            throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label} 不能为空。");
        }

        HashSet<string> colors = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < mappings.Length; i++)
        {
            NoitaWangMaterialMappingDefinition mapping = mappings[i] ??
                throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label}[{i}] 不能为空。");
            Require(IsArgb(mapping.Color), $"{label}[{i}].color 必须为 8 位 ARGB hex。");
            Require(colors.Add(mapping.Color), $"{label} 颜色重复：{mapping.Color}。");
            RequireStableId(mapping.Material, $"{label}[{i}].material");
            Require(
                mapping.Semantic is "secondary" or "loose" or "structure" or "hazard" or "pool",
                $"{label}[{i}].semantic 不受支持：{mapping.Semantic}。");
            Require(mapping.Origin is "wang-color" or "graphics-color", $"{label}[{i}].origin 不受支持：{mapping.Origin}。");
        }
    }

    private static void ValidateMarkers(NoitaWangMarkerDefinition[] markers, string label)
    {
        if (markers is null)
        {
            throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label} 不能为空。");
        }

        Require(markers.Length <= byte.MaxValue - MarkerSemanticBase + 1, $"{label} 超出单字节 marker semantic 容量。");
        HashSet<string> colors = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < markers.Length; i++)
        {
            NoitaWangMarkerDefinition marker = markers[i] ??
                throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label}[{i}] 不能为空。");
            Require(IsArgb(marker.Color), $"{label}[{i}].color 必须为 8 位 ARGB hex。");
            Require(colors.Add(marker.Color), $"{label} 颜色重复：{marker.Color}。");
            Require(!string.IsNullOrWhiteSpace(marker.Function), $"{label}[{i}].function 不能为空。");
            Require(marker.Origin is "lua" or "builtin-or-unresolved", $"{label}[{i}].origin 不受支持：{marker.Origin}。");
        }
    }

    private static void RequireSourcePath(string value, string prefix, string suffix, string label)
    {
        Require(
            value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(suffix, StringComparison.Ordinal),
            $"{label} 必须位于 {prefix} 且以 {suffix} 结尾。");
    }

    private static void RequireStableId(string value, string label)
    {
        Require(!string.IsNullOrWhiteSpace(value), $"{label} 不能为空。");
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            Require(
                character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_',
                $"{label} 只能包含小写 ASCII、数字、- 或 _。");
        }
    }

    private static bool IsArgb(string value)
    {
        return value is { Length: 8 } && IsHex(value);
    }

    private static bool IsSha256(string value)
    {
        return value is { Length: 64 } && IsHex(value);
    }

    private static bool IsHex(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f') and not (>= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{message}");
        }
    }
}

internal sealed class NoitaWangTerrainSetDefinition
{
    public string Id { get; init; } = string.Empty;

    public string[] ReferenceBiomeIds { get; init; } = [];

    public string SourceBiomePath { get; init; } = string.Empty;

    public string SourceBiomeSha256 { get; init; } = string.Empty;

    public string SourceWangPath { get; init; } = string.Empty;

    public string SourceWangSha256 { get; init; } = string.Empty;

    public string SpawnSourcePath { get; init; } = string.Empty;

    public string SpawnSourceSha256 { get; init; } = string.Empty;

    public int SourceWidth { get; init; }

    public int SourceHeight { get; init; }

    public int ShortSide { get; init; }

    public int[] CornerColors { get; init; } = [];

    public int VaryX { get; init; }

    public int VaryY { get; init; }

    public int HorizontalTileCount { get; init; }

    public int VerticalTileCount { get; init; }

    public string[] RandomBinaryColors { get; init; } = [];

    public NoitaWangMaterialMappingDefinition[] MaterialMappings { get; init; } = [];

    public NoitaWangMarkerDefinition[] Markers { get; init; } = [];

    public string Encoding { get; init; } = string.Empty;

    public int DecodedLength { get; init; }

    public string DecodedSha256 { get; init; } = string.Empty;

    public string Data { get; init; } = string.Empty;

    [JsonIgnore]
    internal DecodedNoitaWangTerrainSet Decoded { get; set; } = null!;
}

internal sealed class NoitaWangMaterialMappingDefinition
{
    public string Color { get; init; } = string.Empty;

    public string Material { get; init; } = string.Empty;

    public string Semantic { get; init; } = string.Empty;

    public string Origin { get; init; } = string.Empty;
}

internal sealed class NoitaWangMarkerDefinition
{
    public string Color { get; init; } = string.Empty;

    public string Function { get; init; } = string.Empty;

    public string Origin { get; init; } = string.Empty;
}

internal enum NoitaWangTerrainSemantic : byte
{
    Empty = 0,
    Primary = 1,
    Secondary = 2,
    Loose = 3,
    Structure = 4,
    Hazard = 5,
    Pool = 6,
    RandomBinary = 9,
}

/// <summary>
/// 经来源 hash 和内部 SHA 校验后的只读 Wang tile 集。约束颜色由全局坐标散列得到，
/// 因而 chunk 加载顺序不影响共享边，且稳态采样不分配。
/// </summary>
internal sealed class DecodedNoitaWangTerrainSet(
    string id,
    int shortSide,
    int[] cornerColors,
    byte[] decoded,
    uint[] horizontalKeys,
    int[] horizontalOffsets,
    uint[] verticalKeys,
    int[] verticalOffsets)
{
    private const ulong CoordinateXMultiplier = 0x9E37_79B9_7F4A_7C15UL;
    private const ulong CoordinateYMultiplier = 0xBF58_476D_1CE4_E5B9UL;
    private const ulong OrientationSalt = 0x94D0_49BB_1331_11EBUL;

    public string Id { get; } = id;

    public int ShortSide { get; } = shortSide;

    public ReadOnlySpan<int> CornerColors => CornerColorValues;

    private int[] CornerColorValues { get; } = cornerColors;

    private byte[] Decoded { get; } = decoded;

    private uint[] HorizontalKeys { get; } = horizontalKeys;

    private int[] HorizontalOffsets { get; } = horizontalOffsets;

    private uint[] VerticalKeys { get; } = verticalKeys;

    private int[] VerticalOffsets { get; } = verticalOffsets;

    internal byte Sample(long worldX, long worldY, ulong worldSeed, ulong biomeSalt)
    {
        long unitX = FloorDivide(worldX, ShortSide, out int localX);
        long unitY = FloorDivide(worldY, ShortSide, out int localY);
        int phase = (int)(unitY & 3L);
        int relative = ((int)(unitX & 3L) - phase) & 3;
        bool horizontal = relative is 0 or 1;
        long startX;
        long startY;
        int pixelX;
        int pixelY;
        if (relative == 0)
        {
            startX = unitX;
            startY = unitY;
            pixelX = localX;
            pixelY = localY;
        }
        else if (relative == 1)
        {
            startX = unitX - 1;
            startY = unitY;
            pixelX = ShortSide + localX;
            pixelY = localY;
        }
        else if (relative == 2)
        {
            startX = unitX;
            startY = unitY - 1;
            pixelX = localX;
            pixelY = ShortSide + localY;
        }
        else
        {
            startX = unitX;
            startY = unitY;
            pixelX = localX;
            pixelY = localY;
        }

        uint key = horizontal
            ? PackConstraints(
                CornerColor(startX, startY, worldSeed, biomeSalt),
                CornerColor(startX + 1, startY, worldSeed, biomeSalt),
                CornerColor(startX + 2, startY, worldSeed, biomeSalt),
                CornerColor(startX, startY + 1, worldSeed, biomeSalt),
                CornerColor(startX + 1, startY + 1, worldSeed, biomeSalt),
                CornerColor(startX + 2, startY + 1, worldSeed, biomeSalt))
            : PackConstraints(
                CornerColor(startX, startY, worldSeed, biomeSalt),
                CornerColor(startX, startY + 1, worldSeed, biomeSalt),
                CornerColor(startX, startY + 2, worldSeed, biomeSalt),
                CornerColor(startX + 1, startY, worldSeed, biomeSalt),
                CornerColor(startX + 1, startY + 1, worldSeed, biomeSalt),
                CornerColor(startX + 1, startY + 2, worldSeed, biomeSalt));
        uint[] keys = horizontal ? HorizontalKeys : VerticalKeys;
        int[] offsets = horizontal ? HorizontalOffsets : VerticalOffsets;
        int first = LowerBound(keys, key);
        if ((uint)first >= (uint)keys.Length || keys[first] != key)
        {
            throw new InvalidOperationException($"Wang set {Id} 缺少 constraint key 0x{key:x8}。");
        }

        int afterLast = UpperBound(keys, key, first + 1);
        ulong variantHash = HashCoordinates(
            startX,
            startY,
            worldSeed,
            biomeSalt ^ (horizontal ? 0UL : OrientationSalt));
        int tileIndex = first + (int)(variantHash % (uint)(afterLast - first));
        int pixelIndex = horizontal
            ? (pixelY * ShortSide * 2) + pixelX
            : (pixelY * ShortSide) + pixelX;
        return Decoded[offsets[tileIndex] + pixelIndex];
    }

    internal static bool IsMarker(byte semantic)
    {
        return semantic >= NoitaWangTerrainCatalog.MarkerSemanticBase;
    }

    internal static bool IsRandomBinarySolid(long worldX, long worldY, ulong worldSeed, ulong biomeSalt)
    {
        return (HashCoordinates(worldX, worldY, worldSeed, biomeSalt ^ 0xA076_1D64_78BD_642FUL) & 1UL) == 0;
    }

    private byte CornerColor(long x, long y, ulong worldSeed, ulong biomeSalt)
    {
        int type = ((int)(x & 3L) - (int)(y & 3L) + 1) & 3;
        return (byte)(HashCoordinates(x, y, worldSeed, biomeSalt ^ ((ulong)type * OrientationSalt)) % (uint)CornerColorValues[type]);
    }

    private static uint PackConstraints(byte a, byte b, byte c, byte d, byte e, byte f)
    {
        return (uint)(a | (b << 5) | (c << 10) | (d << 15) | (e << 20) | (f << 25));
    }

    private static int LowerBound(uint[] keys, uint key)
    {
        int low = 0;
        int high = keys.Length;
        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            if (keys[middle] < key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int UpperBound(uint[] keys, uint key, int low)
    {
        int high = keys.Length;
        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            if (keys[middle] <= key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static long FloorDivide(long value, int divisor, out int remainder)
    {
        long quotient = Math.DivRem(value, divisor, out long signedRemainder);
        if (signedRemainder < 0)
        {
            quotient--;
            signedRemainder += divisor;
        }

        remainder = (int)signedRemainder;
        return quotient;
    }

    private static ulong HashCoordinates(long x, long y, ulong worldSeed, ulong salt)
    {
        ulong value = worldSeed ^ salt;
        value ^= unchecked((ulong)x) * CoordinateXMultiplier;
        value ^= BitOperations.RotateLeft(unchecked((ulong)y) * CoordinateYMultiplier, 29);
        value ^= value >> 30;
        value *= 0xBF58_476D_1CE4_E5B9UL;
        value ^= value >> 27;
        value *= 0x94D0_49BB_1331_11EBUL;
        return value ^ (value >> 31);
    }
}
