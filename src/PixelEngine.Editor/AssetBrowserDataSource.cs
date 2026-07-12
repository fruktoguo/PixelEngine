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
/// 外部文件进入 Project Window 时使用的确定性资产类型映射。
/// </summary>
public static class AssetBrowserExternalImportClassifier
{
    /// <summary>
    /// 根据源文件名和扩展名推导 PixelEngine 已有资产类型。未知扩展保留为 <see cref="AssetBrowserItemKind.Other" />，
    /// 与 Unity 将未知文件作为可见资产保留的心智模型一致。
    /// </summary>
    /// <param name="path">外部文件路径。</param>
    /// <returns>推导出的 Project Window 资产类型。</returns>
    public static AssetBrowserItemKind Classify(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fileName = System.IO.Path.GetFileName(path);
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
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
                ".xhtml" or ".html" => AssetBrowserItemKind.UiScreen,
                ".json" => AssetBrowserItemKind.Json,
                _ => AssetBrowserItemKind.Other,
            };
    }
}

/// <summary>
/// 一次系统 file-drop 导入的汇总结果。
/// </summary>
/// <param name="DiscoveredFileCount">从文件与目录源中发现的普通文件数。</param>
/// <param name="ImportedFileCount">成功复制并写入资产数据库的文件数。</param>
/// <param name="RejectedFileCount">缺失、不可访问或导入失败的文件/源数。</param>
/// <param name="Diagnostic">可直接展示到 Project footer 与 Console 的汇总诊断。</param>
public readonly record struct AssetBrowserExternalImportResult(
    int DiscoveredFileCount,
    int ImportedFileCount,
    int RejectedFileCount,
    string Diagnostic)
{
    /// <summary>
    /// 所有发现的文件均成功导入且至少导入一个文件时为 true。
    /// </summary>
    public bool Succeeded => ImportedFileCount > 0 && RejectedFileCount == 0;
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
/// Project Window 右侧内容区的 Unity-like 展示模式。
/// </summary>
public enum AssetBrowserViewMode
{
    /// <summary>
    /// 使用可缩放图标网格展示当前文件夹内容。
    /// </summary>
    Grid,

    /// <summary>
    /// 使用紧凑列表展示当前文件夹内容与类型信息。
    /// </summary>
    List,
}

/// <summary>
/// Project Window 的稳定矢量图标语义；UI 可据此绘制图标而不依赖字体 glyph。
/// </summary>
public enum AssetBrowserIconKind
{
    /// <summary>文件夹。</summary>
    Folder,
    /// <summary>材质或反应定义。</summary>
    Material,
    /// <summary>纹理；有真实缩略图时由缩略图取代。</summary>
    Texture,
    /// <summary>音频。</summary>
    Audio,
    /// <summary>场景。</summary>
    Scene,
    /// <summary>Prefab。</summary>
    Prefab,
    /// <summary>C# 脚本。</summary>
    Script,
    /// <summary>Web-first UI Screen。</summary>
    UiScreen,
    /// <summary>JSON 配置。</summary>
    Json,
    /// <summary>字体。</summary>
    Font,
    /// <summary>文本文档。</summary>
    Text,
    /// <summary>Editor 或工程配置文件。</summary>
    Configuration,
    /// <summary>未知文件。</summary>
    Other,
}

/// <summary>
/// Project Window 资产的上下文 badge。
/// </summary>
[Flags]
public enum AssetBrowserBadge
{
    /// <summary>
    /// 无特殊上下文。
    /// </summary>
    None = 0,

    /// <summary>
    /// 工程启动入口或启动配置。
    /// </summary>
    Startup = 1 << 0,

    /// <summary>
    /// Editor 当前正在编辑的场景。
    /// </summary>
    Current = 1 << 1,

    /// <summary>
    /// 测试、probe 或验收专用资产。
    /// </summary>
    Test = 1 << 2,
}

/// <summary>
/// Project Window 面向用户的资源语义描述。
/// </summary>
/// <param name="TypeLabel">本地化类型名。</param>
/// <param name="Purpose">资源在工程中的用途。</param>
/// <param name="Badges">不依赖当前 Session 的静态 badge。</param>
public readonly record struct AssetBrowserDescriptor(
    string TypeLabel,
    string Purpose,
    AssetBrowserBadge Badges = AssetBrowserBadge.None);

/// <summary>
/// ImGui 可展示的纹理缩略图。
/// </summary>
/// <param name="TextureHandle">GL 纹理句柄。</param>
/// <param name="Width">缩略图宽度。</param>
/// <param name="Height">缩略图高度。</param>
public readonly record struct AssetThumbnail(uint TextureHandle, int Width, int Height);

/// <summary>
/// Project Window 底部资产预览的主要内容形态。
/// </summary>
public enum AssetBrowserPreviewContentKind
{
    /// <summary>以类型图标、摘要和元数据为主。</summary>
    Summary,

    /// <summary>显示真实纹理缩略图。</summary>
    Image,

    /// <summary>显示音频元数据与试听入口。</summary>
    Audio,

    /// <summary>显示有界只读文本内容。</summary>
    Text,
}

/// <summary>
/// Project Window 预览中的一项只读元数据。
/// </summary>
/// <param name="Label">短标签。</param>
/// <param name="Value">面向用户的值。</param>
public readonly record struct AssetBrowserPreviewProperty(string Label, string Value);

/// <summary>
/// 按选择懒加载的 Project Window 详细预览。
/// </summary>
/// <param name="Title">预览标题。</param>
/// <param name="ContentKind">主要内容形态。</param>
/// <param name="Summary">类型化摘要。</param>
/// <param name="Properties">只读元数据。</param>
/// <param name="TextContent">可选的有界文本片段。</param>
/// <param name="Diagnostic">可选的非致命预览诊断。</param>
public sealed record AssetBrowserDetailedPreview(
    string Title,
    AssetBrowserPreviewContentKind ContentKind,
    string Summary,
    IReadOnlyList<AssetBrowserPreviewProperty> Properties,
    string? TextContent = null,
    string? Diagnostic = null);

/// <summary>
/// 资源浏览器资产项。
/// </summary>
/// <param name="Path">生产数据源使用 Content/... 或 ScriptSource/... rooted path；legacy 数据源可使用相对路径。</param>
/// <param name="Kind">资产类型。</param>
/// <param name="SizeBytes">文件大小。</param>
/// <param name="LastModifiedUtc">最后修改 UTC 时间。</param>
/// <param name="Thumbnail">由快照拥有且生命周期稳定的缩略图；生产数据源通常为 null，并通过 <see cref="IAssetBrowserThumbnailDataSource"/> 懒加载。</param>
/// <param name="AssetId">工程级 stable asset id；旧文件系统数据源可为空。</param>
/// <param name="PreviewSummary">Project Window 可展示的只读资产预览摘要。</param>
/// <param name="Descriptor">面向用户的类型、用途与静态 badge。</param>
public readonly record struct AssetBrowserItem(
    string Path,
    AssetBrowserItemKind Kind,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc,
    AssetThumbnail? Thumbnail,
    string? AssetId = null,
    string? PreviewSummary = null,
    AssetBrowserDescriptor? Descriptor = null)
{
    /// <summary>
    /// UI 显示名。
    /// </summary>
    public string DisplayName => System.IO.Path.GetFileName(Path);

    /// <summary>
    /// 返回当前资产应使用的稳定矢量图标语义。
    /// </summary>
    public AssetBrowserIconKind IconKind => AssetBrowserPresentation.ResolveIconKind(this);
}

/// <summary>
/// Project Window 可作为拖拽移动目标的逻辑文件夹。
/// </summary>
/// <param name="Path">生产数据源的 rooted logical folder；空字符串表示 Project Window 总根。</param>
/// <param name="AssetCount">该文件夹及其子文件夹下的资产数量。</param>
public readonly record struct AssetBrowserFolderItem(
    string Path,
    int AssetCount)
{
    /// <summary>
    /// UI 显示名。
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return "Project";
            }

            string normalized = Path.TrimEnd('/').Replace('\\', '/');
            int separator = normalized.LastIndexOf('/');
            return separator < 0 ? normalized : normalized[(separator + 1)..];
        }
    }
}

/// <summary>
/// Project Window 与具体 ImGui 绘制解耦的展示语义解析器。
/// </summary>
public static class AssetBrowserPresentation
{
    /// <summary>
    /// 根据资产类型与扩展名解析稳定图标语义。
    /// </summary>
    /// <param name="item">资产项。</param>
    /// <returns>应绘制的图标类型。</returns>
    public static AssetBrowserIconKind ResolveIconKind(in AssetBrowserItem item)
    {
        if (item.Kind != AssetBrowserItemKind.Other)
        {
            return item.Kind switch
            {
                AssetBrowserItemKind.Folder => AssetBrowserIconKind.Folder,
                AssetBrowserItemKind.Material => AssetBrowserIconKind.Material,
                AssetBrowserItemKind.Texture => AssetBrowserIconKind.Texture,
                AssetBrowserItemKind.Audio => AssetBrowserIconKind.Audio,
                AssetBrowserItemKind.Scene => AssetBrowserIconKind.Scene,
                AssetBrowserItemKind.Prefab => AssetBrowserIconKind.Prefab,
                AssetBrowserItemKind.Script => AssetBrowserIconKind.Script,
                AssetBrowserItemKind.UiScreen => AssetBrowserIconKind.UiScreen,
                AssetBrowserItemKind.Json => AssetBrowserIconKind.Json,
                AssetBrowserItemKind.Other => AssetBrowserIconKind.Other,
                _ => throw new ArgumentOutOfRangeException(nameof(item), item.Kind, "未知 Project Window 资产类型。"),
            };
        }

        string extension = System.IO.Path.GetExtension(item.Path).ToLowerInvariant();
        return extension switch
        {
            ".ttf" or ".otf" or ".woff" or ".woff2" => AssetBrowserIconKind.Font,
            ".txt" or ".md" => AssetBrowserIconKind.Text,
            ".ini" or ".editorconfig" or ".config" or ".props" or ".targets" => AssetBrowserIconKind.Configuration,
            _ => AssetBrowserIconKind.Other,
        };
    }
}

/// <summary>
/// Project Window breadcrumb 中的一个可导航节点。
/// </summary>
/// <param name="Label">节点显示名。</param>
/// <param name="Path">选择该节点后的 logical folder path。</param>
public readonly record struct AssetBrowserBreadcrumbItem(string Label, string Path);

/// <summary>
/// Project Window 可传递给 Shell 的 typed drag payload。
/// </summary>
/// <param name="AssetId">工程级 stable asset id。</param>
/// <param name="Path">资产 rooted logical path。</param>
/// <param name="Kind">资产类型。</param>
public readonly record struct AssetBrowserDragPayload(
    string AssetId,
    string Path,
    AssetBrowserItemKind Kind);

/// <summary>
/// Project Window 资产删除请求。
/// </summary>
/// <param name="Path">资产 rooted logical path。</param>
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
/// <param name="Path">当前资产 rooted logical path。</param>
/// <param name="AssetId">工程级 stable asset id。</param>
/// <param name="Kind">资产类型。</param>
/// <param name="NewPath">移动 / 重命名后的同根 rooted logical path。</param>
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
/// <param name="Path">新资产 rooted logical path；legacy 输入可由数据源按类型选择默认根。</param>
/// <param name="Kind">要创建的资产类型。</param>
public readonly record struct AssetBrowserCreateRequest(
    string Path,
    AssetBrowserItemKind Kind);

/// <summary>
/// Project Window 资产导入请求。
/// </summary>
/// <param name="SourceFullPath">要导入的外部源文件完整路径。</param>
/// <param name="Path">目标 rooted logical path；Script 使用 ScriptSource，其余类型使用 Content。</param>
/// <param name="Kind">要导入的资产类型。</param>
public readonly record struct AssetBrowserImportRequest(
    string SourceFullPath,
    string Path,
    AssetBrowserItemKind Kind);

/// <summary>
/// Project Window 导入源文件选择结果。
/// </summary>
/// <param name="Succeeded">是否选择了源文件。</param>
/// <param name="SourceFullPath">选择的源文件完整路径；取消或失败时为空。</param>
/// <param name="Diagnostic">失败诊断；取消时为空。</param>
public readonly record struct AssetBrowserImportSourcePickResult(
    bool Succeeded,
    string SourceFullPath,
    string Diagnostic);

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
/// Project Window 资产导入结果。
/// </summary>
/// <param name="Succeeded">导入是否已执行。</param>
/// <param name="Diagnostic">可展示给用户的导入诊断。</param>
/// <param name="AssetId">导入后的 stable asset id；失败时为空。</param>
/// <param name="Path">导入后的 logical path；失败时为空。</param>
public readonly record struct AssetBrowserImportResult(
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
/// Project Window 资产导入回调。
/// </summary>
/// <param name="request">导入请求。</param>
/// <returns>导入结果。</returns>
public delegate AssetBrowserImportResult AssetBrowserImportHandler(AssetBrowserImportRequest request);

/// <summary>
/// Project Window 导入源文件选择回调。
/// </summary>
/// <param name="initialPath">当前源文件输入，用作原生对话框默认位置。</param>
/// <param name="kind">当前导入资产类型。</param>
/// <returns>源文件选择结果。</returns>
public delegate AssetBrowserImportSourcePickResult AssetBrowserImportSourcePickHandler(string initialPath, AssetBrowserItemKind kind);

/// <summary>
/// 资源浏览器只读数据源。
/// </summary>
public interface IAssetBrowserDataSource
{
    /// <summary>
    /// 枚举当前缓存的可见资产；只读调用不得隐式触发完整磁盘扫描。
    /// </summary>
    /// <returns>资产快照。</returns>
    IReadOnlyList<AssetBrowserItem> ListAssets();
}

/// <summary>
/// 可显式完整刷新并逐帧泵送增量变更的 Project Window 数据源扩展。
/// </summary>
public interface IAssetBrowserRefreshableDataSource
{
    /// <summary>
    /// 执行一次显式完整资产刷新；用于用户主动 Refresh 等低频入口。
    /// </summary>
    void RefreshAssets();

    /// <summary>
    /// 应用已经排队的增量资产变更，不得退化为逐帧完整扫描。
    /// </summary>
    /// <returns>数据源缓存发生变化、调用方需要重新读取快照时返回 true。</returns>
    bool ApplyPendingChanges();
}

/// <summary>
/// 暴露 Asset Database 可展示诊断的数据源扩展。
/// </summary>
public interface IAssetBrowserDiagnosticDataSource
{
    /// <summary>
    /// 当前需要用户关注的 Asset Database 诊断；无诊断时为空。
    /// </summary>
    string AssetDatabaseDiagnostic { get; }
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
/// 提供随当前工程 Session 变化的 Project Window badge。
/// </summary>
public interface IAssetBrowserContextDataSource
{
    /// <summary>
    /// 返回指定 rooted asset path 的动态 badge；不得触发磁盘扫描。
    /// </summary>
    /// <param name="assetPath">资产 rooted logical path。</param>
    /// <returns>当前上下文 badge。</returns>
    AssetBrowserBadge GetContextBadges(string assetPath);
}

/// <summary>
/// 为 Project Window 可见项提供带生命周期的纹理缩略图。
/// </summary>
/// <remarks>
/// 资产快照不得长期保存可能被 LRU 淘汰的原始 GL handle。面板仅为本帧实际可见项申请 lease，
/// 并在离开可见区域后释放；实现方在 lease 释放前必须保证句柄持续有效。
/// </remarks>
public interface IAssetBrowserThumbnailDataSource
{
    /// <summary>
    /// 为指定资产申请一个缩略图 lease。
    /// </summary>
    /// <param name="assetPath">数据源使用的资产 logical path。</param>
    /// <param name="thumbnail">lease 期间有效的缩略图。</param>
    /// <returns>缩略图可用时返回 true。</returns>
    bool TryAcquireThumbnail(string assetPath, out AssetThumbnail thumbnail);

    /// <summary>
    /// 释放先前申请的缩略图 lease。
    /// </summary>
    /// <param name="assetPath">申请时使用的资产 logical path。</param>
    /// <param name="textureHandle">申请返回的 GL texture handle。</param>
    void ReleaseThumbnail(string assetPath, uint textureHandle);
}

/// <summary>
/// 为当前选择提供按需详细预览的数据源扩展。
/// </summary>
/// <remarks>
/// 普通 <see cref="IAssetBrowserDataSource.ListAssets"/> 查询不得调用本接口；Project Window 仅在选择或文件版本变化时读取一次并缓存。
/// </remarks>
public interface IAssetBrowserPreviewDataSource
{
    /// <summary>
    /// 尝试读取指定资产的类型化详细预览。
    /// </summary>
    /// <param name="assetPath">数据源使用的 rooted logical path。</param>
    /// <param name="preview">成功时返回的详细预览。</param>
    /// <returns>资产存在并能建立预览模型时返回 true。</returns>
    bool TryGetPreview(string assetPath, out AssetBrowserDetailedPreview preview);
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
    /// <param name="assetPath">数据源使用的音频资产 logical path。</param>
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
