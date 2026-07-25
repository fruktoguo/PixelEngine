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
    internal const int CurrentSchemaVersion = 3;
    internal const byte MaterialSemanticBase = 10;
    internal const byte MarkerSemanticBase = 32;
    private const string EmbeddedResourceName = "PixelEngine.Demo.noita-wang-terrain.json";
    private const string RequiredReferenceBuildId = "17130612";
    private const string RequiredReferenceVersionHash = "9dbd52ced019a643169a2db02f46c77f8766c6e5";
    private const string RequiredAlgorithm = "stb-herringbone-wang-corner-v1";
    private const int BinaryHeaderLength = 19;

#if PIXELENGINE_RUNTIME_SCRIPT_COMPILATION
    private static readonly JsonSerializerOptions RuntimeSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
#endif

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
#if PIXELENGINE_RUNTIME_SCRIPT_COMPILATION
                JsonSerializer.Deserialize<NoitaWangTerrainCatalog>(json, RuntimeSerializerOptions) ??
#else
                JsonSerializer.Deserialize(
                    json,
                    DemoContentJsonContext.Default.NoitaWangTerrainCatalog) ??
#endif
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
        return FindDefinitionForReferenceBiome(referenceBiomeId).Decoded;
    }

    internal NoitaWangTerrainSetDefinition FindDefinitionForReferenceBiome(string referenceBiomeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceBiomeId);
        return TryFindDefinitionForReferenceBiome(referenceBiomeId, out NoitaWangTerrainSetDefinition definition)
            ? definition
            : throw new InvalidOperationException($"参考 biome {referenceBiomeId} 缺少 Noita Wang 模板绑定。");
    }

    internal bool TryFindDefinitionForReferenceBiome(
        string referenceBiomeId,
        out NoitaWangTerrainSetDefinition definition)
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
                    definition = sets[setIndex];
                    return true;
                }
            }
        }

        definition = null!;
        return false;
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
        Require(sets.Length == 28, "sets 必须恰好包含 Noita 全部带 Wang PNG 的 28 套 biome 模板。");
        HashSet<string> setIds = new(StringComparer.Ordinal);
        HashSet<string> referenceBiomeIds = new(StringComparer.Ordinal);
        for (int i = 0; i < sets.Length; i++)
        {
            NoitaWangTerrainSetDefinition set = sets[i] ??
                throw new InvalidDataException($"noita-wang-terrain.json 配置无效：sets[{i}] 不能为空。");
            ValidateSet(set, i, setIds, referenceBiomeIds);
        }

        Require(referenceBiomeIds.Count >= RequiredReferenceBiomeIds.Length, "Wang referenceBiomeIds 数量少于主路径权威绑定。");
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
        Require(set.WangMapWidth is >= 1 and <= 4_096, $"{label}.wangMapWidth 必须位于 [1,4096]。");
        Require(set.WangMapHeight is >= 1 and <= 4_096, $"{label}.wangMapHeight 必须位于 [1,4096]。");

        ValidateColors(set.RandomBinaryColors, $"{label}.randomBinaryColors");
        ValidateRandomMaterialMappings(set.RandomMaterialMappings, $"{label}.randomMaterialMappings");
        ValidateMaterialMappings(set.MaterialMappings, $"{label}.materialMappings");
        ValidateMaterialLayers(set.MaterialLayers, $"{label}.materialLayers");
        ValidateMarkers(set.Markers, $"{label}.markers");
        set.DecodedBitmapCaves = set.BitmapCaves is null
            ? null
            : DecodedNoitaBitmapCaves.Decode(
                set.BitmapCaves,
                set.MaterialMappings.Length,
                set.Markers.Length,
                $"{label}.bitmapCaves");
        Require(string.Equals(set.Encoding, "brotli-pewh-v3", StringComparison.Ordinal), $"{label}.encoding 必须为 brotli-pewh-v3。");
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
        Require(decoded.AsSpan(0, 4).SequenceEqual("PWH3"u8), $"{label}.data 缺少 PWH3 头。");
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
        int recordSize = checked(sizeof(uint) + (tileArea * 2));
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
            definition.MaterialMappings.Length,
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
            definition.MaterialMappings.Length,
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
            verticalOffsets,
            definition.MaterialMappings);
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
        int materialCount,
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
            ValidateSemanticPixels(decoded.AsSpan(offset, tileArea * 2), materialCount, markerCount, $"{label}[{i}]");
            offset += tileArea * 2;
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

    private static void ValidateSemanticPixels(ReadOnlySpan<byte> pixels, int materialCount, int markerCount, string label)
    {
        Require((pixels.Length & 1) == 0, $"{label} semantic/density 长度必须为偶数。");
        for (int i = 0; i < pixels.Length; i += 2)
        {
            byte semantic = pixels[i];
            byte density = pixels[i + 1];
            bool terrainSemantic = semantic is <= (byte)NoitaWangTerrainSemantic.Pool or
                (byte)NoitaWangTerrainSemantic.RandomMaterial or
                (byte)NoitaWangTerrainSemantic.RandomBinary;
            bool materialSemantic = semantic >= MaterialSemanticBase &&
                semantic - MaterialSemanticBase < materialCount;
            bool markerSemantic = semantic >= MarkerSemanticBase && semantic - MarkerSemanticBase < markerCount;
            Require(terrainSemantic || materialSemantic || markerSemantic, $"{label} 含未知 semantic {semantic}。");
            Require(semantic == (byte)NoitaWangTerrainSemantic.Primary || density == byte.MaxValue,
                $"{label} 非 Primary semantic {semantic} 的 density 必须为 255。");
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
        HashSet<byte> encodedSemantics = [];
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
            Require(
                mapping.EncodedSemantic == MaterialSemanticBase + i,
                $"{label}[{i}].encodedSemantic 必须连续从 {MaterialSemanticBase} 开始。");
            Require(encodedSemantics.Add(mapping.EncodedSemantic), $"{label} encodedSemantic 重复：{mapping.EncodedSemantic}。");
            Require(mapping.Origin is "wang-color" or "graphics-color", $"{label}[{i}].origin 不受支持：{mapping.Origin}。");
        }
    }

    private static void ValidateRandomMaterialMappings(
        NoitaWangRandomMaterialMappingDefinition[] mappings,
        string label)
    {
        if (mappings is null)
        {
            throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label} 不能为空。");
        }

        Require(mappings.Length <= 1, $"{label} 当前单字节 RandomMaterial semantic 每套最多支持一项。");
        for (int i = 0; i < mappings.Length; i++)
        {
            NoitaWangRandomMaterialMappingDefinition mapping = mappings[i] ??
                throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label}[{i}] 不能为空。");
            Require(IsArgb(mapping.InputColor), $"{label}[{i}].inputColor 必须为 8 位 ARGB hex。");
            Require(mapping.Materials is { Length: > 1 }, $"{label}[{i}].materials 必须至少包含两项。");
            HashSet<string> names = new(StringComparer.Ordinal);
            for (int materialIndex = 0; materialIndex < mapping.Materials.Length; materialIndex++)
            {
                string material = mapping.Materials[materialIndex];
                RequireStableId(material, $"{label}[{i}].materials[{materialIndex}]");
                Require(names.Add(material), $"{label}[{i}].materials 含重复材质 {material}。");
            }
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

    private static void ValidateMaterialLayers(NoitaWangMaterialLayerDefinition[] layers, string label)
    {
        if (layers is null)
        {
            throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label} 不能为空。");
        }

        Require(layers.Length > 0, $"{label} 不能为空数组。");
        for (int i = 0; i < layers.Length; i++)
        {
            NoitaWangMaterialLayerDefinition layer = layers[i] ??
                throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label}[{i}] 不能为空。");
            RequireStableId(layer.MaterialName, $"{label}[{i}].materialName");
            Require(layer.MaterialIndex is >= 0 and <= 31, $"{label}[{i}].materialIndex 必须位于 [0,31]。");
            RequireFinite(layer.MaterialMin, $"{label}[{i}].materialMin");
            RequireFinite(layer.MaterialMax, $"{label}[{i}].materialMax");
            Require(layer.MaterialMax >= layer.MaterialMin, $"{label}[{i}].materialMax 不得小于 materialMin。");
            RequireFinite(layer.LimitMinY, $"{label}[{i}].limitMinY");
            RequireFinite(layer.LimitMaxY, $"{label}[{i}].limitMaxY");
            Require(layer.LimitMaxY >= layer.LimitMinY, $"{label}[{i}].limitMaxY 不得小于 limitMinY。");
            RequireNonNegativeFinite(layer.AddPerlin, $"{label}[{i}].addPerlin");
            RequireNonNegativeFinite(layer.AddPerlinScaleX, $"{label}[{i}].addPerlinScaleX");
            RequireNonNegativeFinite(layer.AddPerlinScaleY, $"{label}[{i}].addPerlinScaleY");
            RequireNonNegativeFinite(layer.RarePolkaProbability, $"{label}[{i}].rarePolkaProbability");
            RequireNonNegativeFinite(layer.RarePolkaRadiusLow, $"{label}[{i}].rarePolkaRadiusLow");
            RequireNonNegativeFinite(layer.RarePolkaRadiusHigh, $"{label}[{i}].rarePolkaRadiusHigh");
            Require(layer.RarePolkaRadiusHigh >= layer.RarePolkaRadiusLow, $"{label}[{i}].rarePolkaRadiusHigh 不得小于 rarePolkaRadiusLow。");
            RequireFinite(layer.RareRequiredMin, $"{label}[{i}].rareRequiredMin");
            RequireFinite(layer.RareRequiredMax, $"{label}[{i}].rareRequiredMax");
            Require(layer.RareRequiredMax >= layer.RareRequiredMin, $"{label}[{i}].rareRequiredMax 不得小于 rareRequiredMin。");
            RequireNonNegativeFinite(layer.RareScaleX, $"{label}[{i}].rareScaleX");
            RequireNonNegativeFinite(layer.RareScaleY, $"{label}[{i}].rareScaleY");
        }
    }

    private static void RequireFinite(double value, string label)
    {
        Require(double.IsFinite(value), $"{label} 必须为有限数值。");
    }

    private static void RequireNonNegativeFinite(double value, string label)
    {
        RequireFinite(value, label);
        Require(value >= 0, $"{label} 不得为负数。");
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

    public int WangMapWidth { get; init; }

    public int WangMapHeight { get; init; }

    public string[] RandomBinaryColors { get; init; } = [];

    public NoitaWangRandomMaterialMappingDefinition[] RandomMaterialMappings { get; init; } = [];

    public NoitaWangMaterialMappingDefinition[] MaterialMappings { get; init; } = [];

    public NoitaWangMaterialLayerDefinition[] MaterialLayers { get; init; } = [];

    public NoitaWangMarkerDefinition[] Markers { get; init; } = [];

    public NoitaBitmapCavesDefinition? BitmapCaves { get; init; }

    public string Encoding { get; init; } = string.Empty;

    public int DecodedLength { get; init; }

    public string DecodedSha256 { get; init; } = string.Empty;

    public string Data { get; init; } = string.Empty;

    [JsonIgnore]
    internal DecodedNoitaWangTerrainSet Decoded { get; set; } = null!;

    [JsonIgnore]
    internal DecodedNoitaBitmapCaves? DecodedBitmapCaves { get; set; }
}

internal sealed class NoitaWangRandomMaterialMappingDefinition
{
    public string InputColor { get; init; } = string.Empty;

    public string[] Materials { get; init; } = [];
}

internal sealed class NoitaWangMaterialLayerDefinition
{
    public bool Enabled { get; init; }

    public double AddPerlin { get; init; }

    public double AddPerlinScaleX { get; init; }

    public double AddPerlinScaleY { get; init; }

    public bool IsPolygon { get; init; }

    public bool IsRare { get; init; }

    public double LimitMaxY { get; init; }

    public double LimitMinY { get; init; }

    public bool LimitY { get; init; }

    public int MaterialIndex { get; init; }

    public double MaterialMax { get; init; }

    public double MaterialMin { get; init; }

    public string MaterialName { get; init; } = string.Empty;

    public bool RarePolkaIsBoxed { get; init; }

    public double RarePolkaProbability { get; init; }

    public double RarePolkaRadiusHigh { get; init; }

    public double RarePolkaRadiusLow { get; init; }

    public double RareRequiredMax { get; init; }

    public double RareRequiredMin { get; init; }

    public double RareScaleX { get; init; }

    public double RareScaleY { get; init; }

    public bool RareUsePerlin { get; init; }

    public bool RareUsePolka { get; init; }
}

internal sealed class NoitaWangMaterialMappingDefinition
{
    public string Color { get; init; } = string.Empty;

    public string Material { get; init; } = string.Empty;

    public string Semantic { get; init; } = string.Empty;

    public byte EncodedSemantic { get; init; }

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
    RandomMaterial = 8,
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
    int[] verticalOffsets,
    NoitaWangMaterialMappingDefinition[] materialMappings)
{
    // Noita 的 Wang/material PNG 是逐物理像素模板：一个 source pixel 对应一个 world cell。
    // 宏观尺度由 512-cell biome map 与 BitmapCaves 提供，不能在这里再次放大模板，否则
    // 会把洞穴、材料边界和 spawn marker 粗化成“略缩图像素块”。
    internal const int SemanticPixelScale = 1;
    private const ulong CoordinateXMultiplier = 0x9E37_79B9_7F4A_7C15UL;
    private const ulong CoordinateYMultiplier = 0xBF58_476D_1CE4_E5B9UL;
    private const ulong OrientationSalt = 0x94D0_49BB_1331_11EBUL;

    public string Id { get; } = id;

    public int ShortSide { get; } = shortSide;

    public ReadOnlySpan<NoitaWangMaterialMappingDefinition> MaterialMappings => materialMappings;

    public static bool IsMaterial(byte semantic)
    {
        return semantic is >= NoitaWangTerrainCatalog.MaterialSemanticBase and
            < NoitaWangTerrainCatalog.MarkerSemanticBase;
    }

    public ReadOnlySpan<int> CornerColors => CornerColorValues;

    private int[] CornerColorValues { get; } = cornerColors;

    private byte[] Decoded { get; } = decoded;

    private uint[] HorizontalKeys { get; } = horizontalKeys;

    private int[] HorizontalOffsets { get; } = horizontalOffsets;

    private uint[] VerticalKeys { get; } = verticalKeys;

    private int[] VerticalOffsets { get; } = verticalOffsets;

    internal byte Sample(long worldX, long worldY, ulong worldSeed, ulong biomeSalt)
    {
        return SampleCore(worldX, worldY, worldSeed, biomeSalt, out _);
    }

    internal byte Sample(long worldX, long worldY, ulong worldSeed, ulong biomeSalt, out byte density)
    {
        return SampleCore(worldX, worldY, worldSeed, biomeSalt, out density);
    }

    private byte SampleCore(long worldX, long worldY, ulong worldSeed, ulong biomeSalt, out byte density)
    {
        long semanticX = FloorDivide(worldX, SemanticPixelScale, out _);
        long semanticY = FloorDivide(worldY, SemanticPixelScale, out _);
        long unitX = FloorDivide(semanticX, ShortSide, out int localX);
        long unitY = FloorDivide(semanticY, ShortSide, out int localY);
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
        int sourceIndex = offsets[tileIndex] + (pixelIndex * 2);
        density = Decoded[sourceIndex + 1];
        return Decoded[sourceIndex];
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
