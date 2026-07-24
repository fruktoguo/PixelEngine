namespace PixelEngine.Demo;

/// <summary>
/// Noita Build 17130612 的 biome 背景与边缘纹理纯 C# 目录。
/// </summary>
internal static partial class NoitaBiomeBackgroundCatalog
{
    private static readonly Lazy<Dictionary<string, int>> BiomeIndices = new(BuildBiomeIndices);

    internal static ReadOnlySpan<NoitaBiomeBackgroundAssetDefinition> Assets => AssetValues;

    internal static ReadOnlySpan<NoitaBiomeBackgroundDefinition> Biomes => BiomeValues;

    internal static bool TryGet(string biomeId, out NoitaBiomeBackgroundDefinition definition)
    {
        if (BiomeIndices.Value.TryGetValue(biomeId, out int index))
        {
            definition = BiomeValues[index];
            return true;
        }

        definition = default;
        return false;
    }

    internal static ref readonly NoitaBiomeBackgroundAssetDefinition Asset(int index)
    {
        return ref AssetValues[index];
    }

    private static Dictionary<string, int> BuildBiomeIndices()
    {
        Dictionary<string, int> indices = new(BiomeValues.Length, StringComparer.Ordinal);
        for (int i = 0; i < BiomeValues.Length; i++)
        {
            if (!indices.TryAdd(BiomeValues[i].BiomeId, i))
            {
                throw new InvalidDataException($"Noita biome background 重复 biome：{BiomeValues[i].BiomeId}。");
            }
        }

        return indices;
    }
}

internal readonly record struct NoitaBiomeBackgroundAssetDefinition(
    int Width,
    int Height,
    string SourcePath,
    string ContentSha256,
    PixelEngine.Scripting.ScriptAssetReference Asset);

internal readonly record struct NoitaBiomeBackgroundDefinition(
    string BiomeId,
    int ImageAssetIndex,
    int LeftAssetIndex,
    int RightAssetIndex,
    int TopAssetIndex,
    int BottomAssetIndex,
    bool UseNeighbor,
    bool LimitImage,
    int EdgePriority);
