namespace PixelEngine.Editor;

/// <summary>
/// 资源浏览器资产类型。
/// </summary>
public enum AssetBrowserItemKind
{
    /// <summary>
    /// Project Window 逻辑文件夹。
    /// </summary>
    Folder,

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
    /// 脚本资产。
    /// </summary>
    Script,

    /// <summary>
    /// Web-first UI Screen 文档资产。
    /// </summary>
    UiScreen,

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
/// Project Window 资产排序模式。
/// </summary>
public enum AssetBrowserSortMode
{
    /// <summary>
    /// 按 logical path 升序排序。
    /// </summary>
    PathAscending,

    /// <summary>
    /// 先按资产类型、再按 logical path 排序。
    /// </summary>
    KindThenPath,

    /// <summary>
    /// 按最后修改时间倒序排序。
    /// </summary>
    LastModifiedDescending,

    /// <summary>
    /// 按文件大小倒序排序。
    /// </summary>
    SizeDescending,
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
/// <param name="AssetId">工程级 stable asset id；旧文件系统数据源可为空。</param>
/// <param name="PreviewSummary">Project Window 可展示的只读资产预览摘要。</param>
public readonly record struct AssetBrowserItem(
    string Path,
    AssetBrowserItemKind Kind,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc,
    AssetThumbnail? Thumbnail,
    string? AssetId = null,
    string? PreviewSummary = null)
{
    /// <summary>
    /// UI 显示名。
    /// </summary>
    public string DisplayName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Project Window 可作为拖拽移动目标的逻辑文件夹。
/// </summary>
/// <param name="Path">相对 content 根目录的逻辑文件夹路径；空字符串表示 content 根目录。</param>
/// <param name="AssetCount">该文件夹及其子文件夹下的资产数量。</param>
public readonly record struct AssetBrowserFolderItem(
    string Path,
    int AssetCount)
{
    /// <summary>
    /// UI 显示名。
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Path) ? "content/" : Path + "/";
}

/// <summary>
/// Project Window 可传递给 Shell 的 typed drag payload。
/// </summary>
/// <param name="AssetId">工程级 stable asset id。</param>
/// <param name="Path">相对 content 根目录的 logical path。</param>
/// <param name="Kind">资产类型。</param>
public readonly record struct AssetBrowserDragPayload(
    string AssetId,
    string Path,
    AssetBrowserItemKind Kind);

/// <summary>
/// Project Window 资产删除请求。
/// </summary>
/// <param name="Path">相对 content 根目录的 logical path。</param>
/// <param name="AssetId">工程级 stable asset id。</param>
/// <param name="Kind">资产类型。</param>
/// <param name="Confirmed">用户是否已确认删除。</param>
public readonly record struct AssetBrowserDeleteRequest(
    string Path,
    string AssetId,
    AssetBrowserItemKind Kind,
    bool Confirmed);

/// <summary>
/// Project Window 文件夹递归删除请求。
/// </summary>
/// <param name="Path">当前相对 content 根目录的文件夹路径。</param>
/// <param name="AssetIds">请求发起时文件夹内全部子资产 stable id，用于防止旧 UI 状态误删新资产。</param>
/// <param name="Confirmed">用户是否已确认删除。</param>
public readonly record struct AssetBrowserFolderDeleteRequest(
    string Path,
    string[] AssetIds,
    bool Confirmed);

/// <summary>
/// Project Window 资产移动 / 重命名请求。
/// </summary>
/// <param name="Path">当前相对 content 根目录的 logical path。</param>
/// <param name="AssetId">工程级 stable asset id。</param>
/// <param name="Kind">资产类型。</param>
/// <param name="NewPath">移动 / 重命名后的 logical path。</param>
public readonly record struct AssetBrowserMoveRequest(
    string Path,
    string AssetId,
    AssetBrowserItemKind Kind,
    string NewPath);

/// <summary>
/// Project Window 文件夹移动 / 重命名请求。
/// </summary>
/// <param name="Path">当前相对 content 根目录的文件夹路径。</param>
/// <param name="NewPath">移动 / 重命名后的文件夹路径。</param>
public readonly record struct AssetBrowserFolderMoveRequest(
    string Path,
    string NewPath);

/// <summary>
/// Project Window 资产创建请求。
/// </summary>
/// <param name="Path">相对 content 根目录的新资产 logical path。</param>
/// <param name="Kind">要创建的资产类型。</param>
public readonly record struct AssetBrowserCreateRequest(
    string Path,
    AssetBrowserItemKind Kind);

/// <summary>
/// Project Window 资产删除结果。
/// </summary>
/// <param name="Succeeded">删除是否已执行。</param>
/// <param name="RequiresConfirmation">是否需要二次确认。</param>
/// <param name="Diagnostic">可展示给用户的删除诊断。</param>
public readonly record struct AssetBrowserDeleteResult(
    bool Succeeded,
    bool RequiresConfirmation,
    string Diagnostic);

/// <summary>
/// Project Window 文件夹递归删除结果。
/// </summary>
/// <param name="Succeeded">删除是否已执行。</param>
/// <param name="RequiresConfirmation">是否需要二次确认。</param>
/// <param name="Diagnostic">可展示给用户的删除诊断。</param>
public readonly record struct AssetBrowserFolderDeleteResult(
    bool Succeeded,
    bool RequiresConfirmation,
    string Diagnostic);

/// <summary>
/// Project Window 资产移动 / 重命名结果。
/// </summary>
/// <param name="Succeeded">移动是否已执行。</param>
/// <param name="Diagnostic">可展示给用户的移动诊断。</param>
public readonly record struct AssetBrowserMoveResult(
    bool Succeeded,
    string Diagnostic);

/// <summary>
/// Project Window 文件夹移动 / 重命名结果。
/// </summary>
/// <param name="Succeeded">移动是否已执行。</param>
/// <param name="Diagnostic">可展示给用户的移动诊断。</param>
public readonly record struct AssetBrowserFolderMoveResult(
    bool Succeeded,
    string Diagnostic);

/// <summary>
/// Project Window 资产创建结果。
/// </summary>
/// <param name="Succeeded">创建是否已执行。</param>
/// <param name="Diagnostic">可展示给用户的创建诊断。</param>
/// <param name="AssetId">创建后的 stable asset id；失败时为空。</param>
/// <param name="Path">创建后的 logical path；失败时为空。</param>
public readonly record struct AssetBrowserCreateResult(
    bool Succeeded,
    string Diagnostic,
    string? AssetId = null,
    string? Path = null);

/// <summary>
/// Project Window 资产删除回调。
/// </summary>
/// <param name="request">删除请求。</param>
/// <returns>删除结果。</returns>
public delegate AssetBrowserDeleteResult AssetBrowserDeleteHandler(AssetBrowserDeleteRequest request);

/// <summary>
/// Project Window 文件夹递归删除回调。
/// </summary>
/// <param name="request">删除请求。</param>
/// <returns>删除结果。</returns>
public delegate AssetBrowserFolderDeleteResult AssetBrowserFolderDeleteHandler(AssetBrowserFolderDeleteRequest request);

/// <summary>
/// Project Window 资产移动 / 重命名回调。
/// </summary>
/// <param name="request">移动请求。</param>
/// <returns>移动结果。</returns>
public delegate AssetBrowserMoveResult AssetBrowserMoveHandler(AssetBrowserMoveRequest request);

/// <summary>
/// Project Window 文件夹移动 / 重命名回调。
/// </summary>
/// <param name="request">移动请求。</param>
/// <returns>移动结果。</returns>
public delegate AssetBrowserFolderMoveResult AssetBrowserFolderMoveHandler(AssetBrowserFolderMoveRequest request);

/// <summary>
/// Project Window 资产创建回调。
/// </summary>
/// <param name="request">创建请求。</param>
/// <returns>创建结果。</returns>
public delegate AssetBrowserCreateResult AssetBrowserCreateHandler(AssetBrowserCreateRequest request);

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
/// 可枚举空文件夹的 Project Window 数据源扩展。
/// </summary>
public interface IAssetBrowserFolderDataSource
{
    /// <summary>
    /// 枚举当前可见逻辑文件夹。
    /// </summary>
    /// <returns>文件夹快照。</returns>
    IReadOnlyList<AssetBrowserFolderItem> ListFolders();
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
public sealed class FileSystemAssetBrowserDataSource(string contentRoot, ITextureThumbnailProvider? thumbnailProvider = null) : IAssetBrowserDataSource, IAssetBrowserFolderDataSource
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

    /// <inheritdoc />
    public IReadOnlyList<AssetBrowserFolderItem> ListFolders()
    {
        if (!Directory.Exists(_contentRoot))
        {
            return [];
        }

        List<AssetBrowserFolderItem> folders =
        [
            new AssetBrowserFolderItem(string.Empty, Directory.EnumerateFiles(_contentRoot, "*", SearchOption.AllDirectories).Count()),
        ];
        foreach (string directory in Directory.EnumerateDirectories(_contentRoot, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            string relative = Path.GetRelativePath(_contentRoot, directory).Replace('\\', '/');
            int assetCount = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count();
            folders.Add(new AssetBrowserFolderItem(relative, assetCount));
        }

        return folders;
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
                ".cs" => AssetBrowserItemKind.Script,
                ".xhtml" or ".html" when IsUnderLogicalFolder(path, "ui/screens") => AssetBrowserItemKind.UiScreen,
                ".json" => AssetBrowserItemKind.Json,
                _ => AssetBrowserItemKind.Other,
            };
    }

    private static bool IsUnderLogicalFolder(string logicalPath, string folderName)
    {
        string normalized = logicalPath.Replace('\\', '/');
        return normalized.StartsWith(folderName + "/", StringComparison.OrdinalIgnoreCase);
    }
}
