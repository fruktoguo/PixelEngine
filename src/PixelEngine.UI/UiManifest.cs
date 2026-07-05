namespace PixelEngine.UI;

/// <summary>
/// 已校验并规范化的 content/ui 清单。
/// </summary>
public sealed class UiManifest
{
    private readonly UiManifestScreen[] _screens;
    private readonly UiManifestImage[] _images;

    internal UiManifest(
        string rootDirectory,
        UiAssetDirectories assetDirectories,
        UiManifestScreen[] screens,
        UiManifestImage[] images)
    {
        RootDirectory = rootDirectory;
        AssetDirectories = assetDirectories;
        _screens = screens;
        _images = images;
    }

    /// <summary>
    /// content/ui 根目录绝对路径。
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    /// content/ui 下标准资产目录。
    /// </summary>
    public UiAssetDirectories AssetDirectories { get; }

    /// <summary>
    /// content/ui/fonts 目录。
    /// </summary>
    public string FontsDirectory => AssetDirectories.FontsDirectory;

    /// <summary>
    /// content/ui/images 目录。
    /// </summary>
    public string ImagesDirectory => AssetDirectories.ImagesDirectory;

    /// <summary>
    /// 屏幕条目数量。
    /// </summary>
    public int ScreenCount => _screens.Length;

    /// <summary>
    /// 屏幕条目序列。
    /// </summary>
    public ReadOnlySpan<UiManifestScreen> Screens => _screens;

    /// <summary>
    /// 图片资产条目数量。
    /// </summary>
    public int ImageCount => _images.Length;

    /// <summary>
    /// 图片资产条目序列。
    /// </summary>
    public ReadOnlySpan<UiManifestImage> Images => _images;

    /// <summary>
    /// 查找屏幕条目。
    /// </summary>
    /// <param name="screenId">屏幕稳定字符串 id。</param>
    /// <param name="screen">查找到的屏幕条目。</param>
    /// <returns>找到则返回 true。</returns>
    public bool TryGetScreen(string screenId, out UiManifestScreen screen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        for (int i = 0; i < _screens.Length; i++)
        {
            if (string.Equals(_screens[i].Id, screenId, StringComparison.Ordinal))
            {
                screen = _screens[i];
                return true;
            }
        }

        screen = default;
        return false;
    }

    /// <summary>
    /// 获取屏幕条目；不存在时抛出明确异常。
    /// </summary>
    /// <param name="screenId">屏幕稳定字符串 id。</param>
    /// <returns>屏幕条目。</returns>
    public UiManifestScreen GetRequiredScreen(string screenId)
    {
        return TryGetScreen(screenId, out UiManifestScreen screen)
            ? screen
            : throw new KeyNotFoundException($"UI 清单中不存在屏幕 '{screenId}'。");
    }

    /// <summary>
    /// 查找图片资产条目。
    /// </summary>
    /// <param name="imageId">图片稳定字符串 id。</param>
    /// <param name="image">查找到的图片条目。</param>
    /// <returns>找到则返回 true。</returns>
    public bool TryGetImage(string imageId, out UiManifestImage image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageId);
        for (int i = 0; i < _images.Length; i++)
        {
            if (string.Equals(_images[i].Id, imageId, StringComparison.Ordinal))
            {
                image = _images[i];
                return true;
            }
        }

        image = default;
        return false;
    }

    /// <summary>
    /// 获取图片资产条目；不存在时抛出明确异常。
    /// </summary>
    /// <param name="imageId">图片稳定字符串 id。</param>
    /// <returns>图片资产条目。</returns>
    public UiManifestImage GetRequiredImage(string imageId)
    {
        return TryGetImage(imageId, out UiManifestImage image)
            ? image
            : throw new KeyNotFoundException($"UI 清单中不存在图片 '{imageId}'。");
    }

    /// <summary>
    /// 查找屏幕并输出后端文档来源。
    /// </summary>
    /// <param name="screenId">屏幕稳定字符串 id。</param>
    /// <param name="source">文档来源。</param>
    /// <returns>找到则返回 true。</returns>
    public bool TryResolveDocumentSource(string screenId, out UiDocumentSource source)
    {
        if (TryGetScreen(screenId, out UiManifestScreen screen))
        {
            source = screen.ToDocumentSource();
            return true;
        }

        source = default;
        return false;
    }

    /// <summary>
    /// 获取屏幕对应的后端文档来源；不存在时抛出明确异常。
    /// </summary>
    /// <param name="screenId">屏幕稳定字符串 id。</param>
    /// <returns>文档来源。</returns>
    public UiDocumentSource ResolveDocumentSource(string screenId)
    {
        return GetRequiredScreen(screenId).ToDocumentSource();
    }
}
