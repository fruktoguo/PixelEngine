using System.IO.Compression;
using System.Security.Cryptography;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Build 17130612 biome 随机 Pixel Scene 的纯 C# 目录；运行时不读取 Lua 或参考资产。
/// </summary>
internal static partial class NoitaRandomPixelSceneCatalog
{
    private static readonly Lazy<DecodedNoitaRandomPixelScene[]> DecodedValues = new(DecodeScenes);

    internal static ReadOnlySpan<DecodedNoitaRandomPixelScene> DecodedScenes => DecodedValues.Value;

    internal static bool Supports(in NoitaWangMarkerAnchor anchor)
    {
        string markerFunction = ResolveMarkerFunction(in anchor);
        ReadOnlySpan<NoitaRandomPixelSceneTableDefinition> tables = Tables;
        for (int i = 0; i < tables.Length; i++)
        {
            ref readonly NoitaRandomPixelSceneTableDefinition table = ref tables[i];
            if (BiomeIdsMatch(table.BiomeId, anchor.ReferenceBiomeId) &&
                table.Supports(markerFunction))
            {
                return true;
            }
        }

        return false;
    }

    internal static string ResolveMarkerFunction(in NoitaWangMarkerAnchor anchor)
    {
        return !anchor.Function.StartsWith("builtin-or-unresolved", StringComparison.Ordinal)
            ? anchor.Function
            : anchor.MarkerColor.ToLowerInvariant() switch
            {
                "ffff0000" => "load_pixel_scene",
                "ffffff00" => "load_pixel_scene2",
                "ff00ffff" => "load_pixel_scene3",
                "ff00ff00" => "load_pixel_scene4",
                "ff0000ff" => "load_pixel_scene5",
                _ => anchor.Function,
            };
    }

    internal static CompiledNoitaRandomPixelSceneCatalog Compile(IMaterialQuery materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ReadOnlySpan<string> names = MaterialNames;
        ushort[] materialIds = new ushort[names.Length + 1];
        for (int i = 0; i < names.Length; i++)
        {
            if (!materials.TryResolve(names[i], out MaterialId material))
            {
                throw new InvalidDataException($"Random Pixel Scene 缺少材质 {names[i]}。");
            }

            materialIds[i + 1] = material.Value;
        }

        ReadOnlySpan<NoitaRandomPixelSceneOverrideDefinition> overrides = Overrides;
        ushort[][] overrideMaterialIds = new ushort[overrides.Length][];
        for (int i = 0; i < overrides.Length; i++)
        {
            ref readonly NoitaRandomPixelSceneOverrideDefinition definition = ref overrides[i];
            ushort[] choices = new ushort[definition.MaterialNameIndices.Length];
            for (int choiceIndex = 0; choiceIndex < choices.Length; choiceIndex++)
            {
                choices[choiceIndex] = materialIds[definition.MaterialNameIndices[choiceIndex]];
            }

            overrideMaterialIds[i] = choices;
        }

        return new CompiledNoitaRandomPixelSceneCatalog(materialIds, overrideMaterialIds, [.. DecodedScenes]);
    }

    internal static int SelectSceneIndex(
        string biomeId,
        string markerFunction,
        long worldX,
        long worldY,
        ulong worldSeed)
    {
        ReadOnlySpan<NoitaRandomPixelSceneTableDefinition> tables = Tables;
        Span<int> matches = stackalloc int[8];
        int matchCount = 0;
        for (int i = 0; i < tables.Length; i++)
        {
            ref readonly NoitaRandomPixelSceneTableDefinition table = ref tables[i];
            if (!BiomeIdsMatch(table.BiomeId, biomeId) || !table.Supports(markerFunction))
            {
                continue;
            }

            if (matchCount >= matches.Length)
            {
                throw new InvalidDataException($"{biomeId}/{markerFunction} 的随机 Pixel Scene 表超过固定容量。");
            }

            matches[matchCount++] = i;
        }

        if (matchCount == 0)
        {
            return -1;
        }

        int selectedTableOffset = Math.Min(
            matchCount - 1,
            (int)(PlayableCavernWorldGenerator.PixelSceneRandomUnit(worldX, worldY, worldSeed ^ 0x5441_424C_45UL) * matchCount));
        return SelectFromTable(tables[matches[selectedTableOffset]], worldX, worldY, worldSeed, includeUnique: true);
    }

    internal static int SelectSceneIndex(
        string biomeId,
        string markerFunction,
        long worldX,
        long worldY,
        ulong worldSeed,
        ReadOnlySpan<NoitaRandomPixelSceneUniqueAnchor> uniqueAnchors)
    {
        int selected = SelectSceneIndex(biomeId, markerFunction, worldX, worldY, worldSeed);
        if (selected < 0 || !Scenes[selected].IsUnique)
        {
            return selected;
        }

        for (int i = 0; i < uniqueAnchors.Length; i++)
        {
            ref readonly NoitaRandomPixelSceneUniqueAnchor anchor = ref uniqueAnchors[i];
            if (anchor.SceneIndex == selected && anchor.WorldX == worldX && anchor.WorldY == worldY)
            {
                return selected;
            }
        }

        ref readonly NoitaRandomPixelSceneDefinition scene = ref Scenes[selected];
        return SelectFromTable(Tables[scene.TableIndex], worldX, worldY, worldSeed, includeUnique: false);
    }

    internal static bool BiomeIdsMatch(string tableBiomeId, string referenceBiomeId)
    {
        if (tableBiomeId.Length != referenceBiomeId.Length)
        {
            return false;
        }

        for (int i = 0; i < tableBiomeId.Length; i++)
        {
            char left = tableBiomeId[i] == '_' ? '-' : tableBiomeId[i];
            if (left != referenceBiomeId[i])
            {
                return false;
            }
        }

        return true;
    }

    private static int SelectFromTable(
        in NoitaRandomPixelSceneTableDefinition selectedTable,
        long worldX,
        long worldY,
        ulong worldSeed,
        bool includeUnique)
    {
        ReadOnlySpan<NoitaRandomPixelSceneDefinition> scenes = Scenes.Slice(
            selectedTable.FirstSceneIndex,
            selectedTable.SceneCount);
        double total = 0d;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (includeUnique || !scenes[i].IsUnique)
            {
                total += scenes[i].Probability;
            }
        }

        if (total <= 0d)
        {
            return -1;
        }

        double choice = PlayableCavernWorldGenerator.PixelSceneRandomUnit(
            worldX,
            worldY,
            worldSeed ^ 0x5343_454E_45UL) * total;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (!includeUnique && scenes[i].IsUnique)
            {
                continue;
            }

            if (choice <= scenes[i].Probability)
            {
                return selectedTable.FirstSceneIndex + i;
            }

            choice -= scenes[i].Probability;
        }

        for (int i = scenes.Length - 1; i >= 0; i--)
        {
            if (includeUnique || !scenes[i].IsUnique)
            {
                return selectedTable.FirstSceneIndex + i;
            }
        }

        return -1;
    }

    internal static ushort ResolveMaterial(
        CompiledNoitaRandomPixelSceneCatalog catalog,
        byte pixelCode,
        long anchorX,
        long anchorY,
        int localX,
        int localY,
        ulong worldSeed)
    {
        if (pixelCode == 0)
        {
            return 0;
        }

        if (pixelCode < catalog.MaterialIds.Length)
        {
            return catalog.MaterialIds[pixelCode];
        }

        int overrideIndex = pixelCode - catalog.MaterialIds.Length;
        ushort[] choices = catalog.OverrideMaterialIds[overrideIndex];
        int choiceIndex = Math.Min(
            choices.Length - 1,
            (int)(PlayableCavernWorldGenerator.PixelSceneRandomUnit(
                anchorX + localX,
                anchorY + localY,
                worldSeed ^ ((ulong)pixelCode << 48)) * choices.Length));
        return choices[choiceIndex];
    }

    private static DecodedNoitaRandomPixelScene[] DecodeScenes()
    {
        ReadOnlySpan<NoitaRandomPixelSceneDefinition> scenes = Scenes;
        DecodedNoitaRandomPixelScene[] result = new DecodedNoitaRandomPixelScene[scenes.Length];
        for (int i = 0; i < scenes.Length; i++)
        {
            ref readonly NoitaRandomPixelSceneDefinition scene = ref scenes[i];
            byte[] compressed = Convert.FromBase64String(scene.Data);
            byte[] pixels = new byte[checked(scene.Width * scene.Height)];
            using MemoryStream source = new(compressed, writable: false);
            using BrotliStream brotli = new(source, CompressionMode.Decompress, leaveOpen: false);
            brotli.ReadExactly(pixels);
            if (brotli.ReadByte() >= 0 ||
                !string.Equals(
                    Convert.ToHexString(SHA256.HashData(pixels)),
                    scene.DecodedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Random Pixel Scene {scene.Id} 掩码校验失败。");
            }

            result[i] = new DecodedNoitaRandomPixelScene(scene, pixels);
        }

        return result;
    }
}

internal sealed class CompiledNoitaRandomPixelSceneCatalog(
    ushort[] materialIds,
    ushort[][] overrideMaterialIds,
    DecodedNoitaRandomPixelScene[] scenes)
{
    public ushort[] MaterialIds { get; } = materialIds;

    public ushort[][] OverrideMaterialIds { get; } = overrideMaterialIds;

    public DecodedNoitaRandomPixelScene[] Scenes { get; } = scenes;
}

internal readonly record struct NoitaRandomPixelSceneOverrideDefinition(
    byte PixelCode,
    byte[] MaterialNameIndices);

internal readonly record struct NoitaRandomPixelSceneTableDefinition(
    string BiomeId,
    string SourcePath,
    string Name,
    string[] Functions,
    int FirstSceneIndex,
    int SceneCount)
{
    public bool Supports(string function)
    {
        for (int i = 0; i < Functions.Length; i++)
        {
            if (string.Equals(Functions[i], function, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

internal readonly record struct NoitaRandomPixelSceneDefinition(
    string Id,
    int TableIndex,
    double Probability,
    bool IsUnique,
    int Width,
    int Height,
    string DecodedSha256,
    string Data,
    ScriptAssetReference VisualAsset,
    ScriptAssetReference BackgroundAsset);

internal readonly record struct DecodedNoitaRandomPixelScene(
    NoitaRandomPixelSceneDefinition Definition,
    byte[] PixelCodes);

internal readonly record struct NoitaRandomPixelSceneUniqueAnchor(
    int SceneIndex,
    long WorldX,
    long WorldY);
