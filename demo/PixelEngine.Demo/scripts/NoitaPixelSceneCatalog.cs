namespace PixelEngine.Demo;

/// <summary>
/// Noita 全局 buffered pixel scene 的纯 C# 编译期目录。
/// </summary>
internal static partial class NoitaPixelSceneCatalog
{
    internal static bool TryFindAt(long worldX, long worldY, out NoitaPixelSceneDefinition scene)
    {
        ReadOnlySpan<NoitaPixelSceneDefinition> scenes = Scenes;
        for (int i = 0; i < scenes.Length; i++)
        {
            ref readonly NoitaPixelSceneDefinition candidate = ref scenes[i];
            if (worldX >= candidate.WorldX && worldY >= candidate.WorldY &&
                worldX < candidate.WorldX + candidate.Width &&
                worldY < candidate.WorldY + candidate.Height)
            {
                scene = candidate;
                return true;
            }
        }

        scene = default;
        return false;
    }
}

internal readonly record struct NoitaPixelSceneDefinition(
    int Ordinal,
    int WorldX,
    int WorldY,
    int Width,
    int Height,
    string MaterialPath,
    string ColorsPath,
    string BackgroundPath,
    string MaterialSha256,
    string ColorsSha256,
    string BackgroundSha256,
    bool CleanAreaBefore,
    bool SkipBiomeChecks,
    bool SkipEdgeTextures,
    string StableId);
