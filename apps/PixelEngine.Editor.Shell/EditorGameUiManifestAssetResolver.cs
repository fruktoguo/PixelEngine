using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 使用 Editor 稳定资产清单解析 Web Canvas manifest id；移动/重命名后无需改场景中的稳定 id。
/// </summary>
internal sealed class EditorGameUiManifestAssetResolver(
    EditorAssetManifestStore assets,
    string contentRoot) : IGameUiManifestAssetResolver
{
    private readonly EditorAssetManifestStore _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    private readonly string _contentRoot = Path.GetFullPath(contentRoot ?? throw new ArgumentNullException(nameof(contentRoot)));

    /// <inheritdoc />
    public bool TryResolveManifest(string assetId, out string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (!_assets.TryResolveAssetId(assetId, out EditorAssetRecord record))
        {
            manifestPath = string.Empty;
            return false;
        }

        string fullPath = Path.GetFullPath(Path.Combine(_contentRoot, record.LogicalPath));
        string rootWithSeparator = Path.TrimEndingDirectorySeparator(_contentRoot) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(rootWithSeparator, comparison) || !File.Exists(fullPath))
        {
            manifestPath = string.Empty;
            return false;
        }

        manifestPath = fullPath;
        return true;
    }
}
