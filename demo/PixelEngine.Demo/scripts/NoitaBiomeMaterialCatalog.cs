namespace PixelEngine.Demo;

/// <summary>
/// Noita Build 17130612 全部 biome 的 MaterialComponent 层目录。
/// 数据由离线工具编译为 C#，运行时不读取参考 XML 或执行 Lua。
/// </summary>
internal static partial class NoitaBiomeMaterialCatalog
{
    internal static ReadOnlySpan<NoitaBiomeMaterialProfile> Profiles => ProfileValues;

    internal static bool TryFindBySourcePath(string sourcePath, out NoitaBiomeMaterialProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        for (int i = 0; i < ProfileValues.Length; i++)
        {
            if (string.Equals(ProfileValues[i].SourcePath, sourcePath, StringComparison.Ordinal))
            {
                profile = ProfileValues[i];
                return true;
            }
        }

        profile = default;
        return false;
    }
}

internal readonly record struct NoitaBiomeMaterialProfile(
    string Id,
    string SourcePath,
    NoitaWangMaterialLayerDefinition[] Layers);
