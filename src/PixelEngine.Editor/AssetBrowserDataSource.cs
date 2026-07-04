namespace PixelEngine.Editor;

/// <summary>
/// 资源浏览器资产类型。
/// </summary>
public enum AssetBrowserItemKind
{
    /// <summary>
    /// 材质定义资产。
    /// </summary>
    Material,

    /// <summary>
    /// 纹理资产。
    /// </summary>
    Texture,

    /// <summary>
    /// 音频资产。
    /// </summary>
    Audio,

    /// <summary>
    /// 场景资产。
    /// </summary>
    Scene,

    /// <summary>
    /// 预制体资产。
    /// </summary>
    Prefab,

    /// <summary>
    /// 普通 JSON 资产。
    /// </summary>
    Json,

    /// <summary>
    /// 其它资产。
    /// </summary>
    Other,
}

/// <summary>
/// ImGui 可展示的纹理缩略图。
/// </summary>
/// <param name="TextureHandle">GL 纹理句柄。</param>
/// <param name="Width">缩略图宽度。</param>
/// <param name="Height">缩略图高度。</param>
public readonly record struct AssetThumbnail(uint TextureHandle, int Width, int Height);

/// <summary>
/// 资源浏览器资产项。
/// </summary>
/// <param name="Path">相对 content 根目录的资产路径。</param>
/// <param name="Kind">资产类型。</param>
/// <param name="SizeBytes">文件大小。</param>
/// <param name="LastModifiedUtc">最后修改 UTC 时间。</param>
/// <param name="Thumbnail">可用缩略图；没有时为 null。</param>
public readonly record struct AssetBrowserItem(
    string Path,
    AssetBrowserItemKind Kind,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc,
    AssetThumbnail? Thumbnail)
{
    /// <summary>
    /// UI 显示名。
    /// </summary>
    public string DisplayName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// 资源浏览器只读数据源。
/// </summary>
public interface IAssetBrowserDataSource
{
    /// <summary>
    /// 枚举当前可见资产。
    /// </summary>
    /// <returns>资产快照。</returns>
    IReadOnlyList<AssetBrowserItem> ListAssets();
}

/// <summary>
/// 纹理缩略图提供器。
/// </summary>
public interface ITextureThumbnailProvider
{
    /// <summary>
    /// 尝试为资产创建或获取缩略图。
    /// </summary>
    /// <param name="assetPath">相对 content 根目录的资产路径。</param>
    /// <param name="thumbnail">缩略图。</param>
    /// <returns>缩略图可用时返回 true。</returns>
    bool TryGetThumbnail(string assetPath, out AssetThumbnail thumbnail);
}

/// <summary>
/// 音频资源试听服务。
/// </summary>
public interface IAudioPreviewService
{
    /// <summary>
    /// 尝试试听指定音频资产。
    /// </summary>
    /// <param name="assetPath">相对 content 根目录的音频资产路径。</param>
    /// <returns>成功开始试听时返回 true。</returns>
    bool TryPlayPreview(string assetPath);
}

/// <summary>
/// 基于 content 目录的资源浏览器数据源。
/// </summary>
/// <param name="contentRoot">内容根目录。</param>
/// <param name="thumbnailProvider">纹理缩略图提供器。</param>
public sealed class FileSystemAssetBrowserDataSource(string contentRoot, ITextureThumbnailProvider? thumbnailProvider = null) : IAssetBrowserDataSource
{
    private readonly string _contentRoot = string.IsNullOrWhiteSpace(contentRoot)
        ? throw new ArgumentException("content 根目录不能为空。", nameof(contentRoot))
        : Path.GetFullPath(contentRoot);
    private readonly ITextureThumbnailProvider? _thumbnailProvider = thumbnailProvider;

    /// <inheritdoc />
    public IReadOnlyList<AssetBrowserItem> ListAssets()
    {
        if (!Directory.Exists(_contentRoot))
        {
            return [];
        }

        string[] files =
        [
            .. Directory.EnumerateFiles(_contentRoot, "*", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        AssetBrowserItem[] items = new AssetBrowserItem[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            string fullPath = files[i];
            FileInfo info = new(fullPath);
            string relative = Path.GetRelativePath(_contentRoot, fullPath).Replace('\\', '/');
            AssetThumbnail? thumbnail = TryResolveThumbnail(relative, out AssetThumbnail resolved) ? resolved : null;
            items[i] = new AssetBrowserItem(
                relative,
                Classify(relative),
                info.Length,
                info.LastWriteTimeUtc,
                thumbnail);
        }

        return items;
    }

    private bool TryResolveThumbnail(string relativePath, out AssetThumbnail thumbnail)
    {
        thumbnail = default;
        return _thumbnailProvider is not null && _thumbnailProvider.TryGetThumbnail(relativePath, out thumbnail);
    }

    private static AssetBrowserItemKind Classify(string path)
    {
        string fileName = Path.GetFileName(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return string.Equals(fileName, "materials.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "reactions.json", StringComparison.OrdinalIgnoreCase)
            ? AssetBrowserItemKind.Material
            : extension switch
            {
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".webp" => AssetBrowserItemKind.Texture,
                ".wav" or ".ogg" or ".flac" or ".mp3" => AssetBrowserItemKind.Audio,
                ".scene" or ".world" => AssetBrowserItemKind.Scene,
                ".prefab" => AssetBrowserItemKind.Prefab,
                ".json" => AssetBrowserItemKind.Json,
                _ => AssetBrowserItemKind.Other,
            };
    }
}
