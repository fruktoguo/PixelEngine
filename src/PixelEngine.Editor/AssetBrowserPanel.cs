using Hexa.NET.ImGui;
using System.Globalization;

namespace PixelEngine.Editor;

/// <summary>
/// Project Window 脚本资产打开回调。
/// </summary>
/// <param name="assetPath">资产逻辑路径。</param>
/// <param name="diagnostic">可展示给用户的打开诊断。</param>
/// <returns>成功发起打开时返回 true。</returns>
public delegate bool ScriptAssetOpenHandler(string assetPath, out string diagnostic);

/// <summary>
/// Project Window 场景资产打开回调。
/// </summary>
/// <param name="assetPath">场景资产 rooted logical path。</param>
/// <param name="diagnostic">可展示给用户的打开或 dirty-guard 诊断。</param>
/// <returns>场景打开或 dirty-guard 转场已受理时返回 true。</returns>
public delegate bool SceneAssetOpenHandler(string assetPath, out string diagnostic);

/// <summary>
/// 工程级 Project Window，展示 Content 与 ScriptSource logical root。
/// </summary>
/// <param name="source">资产数据源。</param>
/// <param name="audioPreview">音频试听服务。</param>
/// <param name="instantiatePrefab">可选 prefab 实例化回调。</param>
/// <param name="openScriptAsset">可选脚本资产打开回调。</param>
/// <param name="openSceneAsset">可选场景资产打开回调。</param>
/// <param name="deleteAsset">可选资产删除回调。</param>
/// <param name="deleteFolder">可选文件夹递归删除回调。</param>
/// <param name="moveAsset">可选资产移动 / 重命名回调。</param>
/// <param name="moveFolder">可选文件夹移动 / 重命名回调。</param>
/// <param name="createAsset">可选资产创建回调。</param>
/// <param name="importAsset">可选资产导入回调。</param>
/// <param name="pickImportSource">可选导入源文件选择回调。</param>
public sealed class AssetBrowserPanel(
    IAssetBrowserDataSource source,
    IAudioPreviewService? audioPreview = null,
    Action<string>? instantiatePrefab = null,
    ScriptAssetOpenHandler? openScriptAsset = null,
    SceneAssetOpenHandler? openSceneAsset = null,
    AssetBrowserDeleteHandler? deleteAsset = null,
    AssetBrowserFolderDeleteHandler? deleteFolder = null,
    AssetBrowserMoveHandler? moveAsset = null,
    AssetBrowserFolderMoveHandler? moveFolder = null,
    AssetBrowserCreateHandler? createAsset = null,
    AssetBrowserImportHandler? importAsset = null,
    AssetBrowserImportSourcePickHandler? pickImportSource = null) : IEditorPanel
{
    private readonly IAssetBrowserDataSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IAudioPreviewService? _audioPreview = audioPreview;
    private readonly Action<string>? _instantiatePrefab = instantiatePrefab;
    private readonly ScriptAssetOpenHandler? _openScriptAsset = openScriptAsset;
    private readonly SceneAssetOpenHandler? _openSceneAsset = openSceneAsset;
    private readonly AssetBrowserDeleteHandler? _deleteAsset = deleteAsset;
    private readonly AssetBrowserFolderDeleteHandler? _deleteFolder = deleteFolder;
    private readonly AssetBrowserMoveHandler? _moveAsset = moveAsset;
    private readonly AssetBrowserFolderMoveHandler? _moveFolder = moveFolder;
    private readonly AssetBrowserCreateHandler? _createAsset = createAsset;
    private readonly AssetBrowserImportHandler? _importAsset = importAsset;
    private readonly AssetBrowserImportSourcePickHandler? _pickImportSource = pickImportSource;
    private static readonly string[] KindFilterLabels = ["全部", "Folder", "Material", "Texture", "Audio", "Scene", "Prefab", "Script", "UI Screen", "Json", "Other"];
    private static readonly string[] SortModeLabels = ["路径", "类型 / 路径", "最近修改", "大小"];
    private static readonly AssetBrowserItemKind[] CreateKinds =
    [
        AssetBrowserItemKind.Folder,
        AssetBrowserItemKind.Material,
        AssetBrowserItemKind.Scene,
        AssetBrowserItemKind.Prefab,
        AssetBrowserItemKind.Script,
        AssetBrowserItemKind.UiScreen,
        AssetBrowserItemKind.Json,
    ];

    private static readonly string[] CreateKindLabels = ["Folder", "Material", "Scene", "Prefab", "Script", "UI Screen", "Json"];
    private static readonly AssetBrowserItemKind[] ImportKinds =
    [
        AssetBrowserItemKind.Texture,
        AssetBrowserItemKind.Audio,
    ];

    private static readonly string[] ImportKindLabels = ["Texture", "Audio"];
    private string _search = string.Empty;
    private AssetBrowserDeleteRequest? _pendingDeleteRequest;
    private AssetBrowserFolderDeleteRequest? _pendingFolderDeleteRequest;
    private AssetBrowserMoveRequest? _pendingMoveRequest;
    private string _pendingMoveTargetPath = string.Empty;
    private AssetBrowserFolderMoveRequest? _pendingFolderMoveRequest;
    private string _pendingFolderMoveTargetPath = string.Empty;
    private EditorSelection? _trackedSelection;
    private bool _snapshotLoaded;
    private bool _showCreateEditor;
    private bool _showImportEditor;

    /// <inheritdoc />
    public string Title => EditorDockSpace.AssetBrowserWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 最近一次完整资产快照。
    /// </summary>
    public IReadOnlyList<AssetBrowserItem> LastAssets { get; private set; } = [];

    /// <summary>
    /// 最近一次筛选后的资产快照。
    /// </summary>
    public IReadOnlyList<AssetBrowserItem> FilteredAssets { get; private set; } = [];

    /// <summary>
    /// 当前可作为拖拽移动目标的逻辑文件夹快照。
    /// </summary>
    public IReadOnlyList<AssetBrowserFolderItem> FolderTargets { get; private set; } = [];

    /// <summary>
    /// 当前 folder scope 的直接子文件夹；搜索时仍保持目录导航而不混入深层结果。
    /// </summary>
    public IReadOnlyList<AssetBrowserFolderItem> VisibleFolders { get; private set; } = [];

    /// <summary>
    /// 当前 folder scope 的可点击 breadcrumb。
    /// </summary>
    public IReadOnlyList<AssetBrowserBreadcrumbItem> Breadcrumbs { get; private set; } =
        [new AssetBrowserBreadcrumbItem("工程", string.Empty)];

    /// <summary>
    /// 当前 Project Window 文件夹作用域；空字符串表示双根总览。
    /// </summary>
    public string ActiveFolderPath { get; private set; } = string.Empty;

    /// <summary>
    /// 当前 Create Type 输入。
    /// </summary>
    public AssetBrowserItemKind CreateKind { get; private set; } = AssetBrowserItemKind.Script;

    /// <summary>
    /// 当前 New Asset 输入。
    /// </summary>
    public string CreatePath { get; private set; } = "scripts/NewBehaviour.cs";

    /// <summary>
    /// 当前 Import Type 输入。
    /// </summary>
    public AssetBrowserItemKind ImportKind { get; private set; } = AssetBrowserItemKind.Texture;

    /// <summary>
    /// 当前外部源文件输入。
    /// </summary>
    public string ImportSourcePath { get; private set; } = string.Empty;

    /// <summary>
    /// 当前导入目标 logical path 输入。
    /// </summary>
    public string ImportDestinationPath { get; private set; } = "textures/NewTexture.png";

    /// <summary>
    /// 当前资产类型过滤；null 表示全部类型。
    /// </summary>
    public AssetBrowserItemKind? KindFilter { get; private set; }

    /// <summary>
    /// 当前排序模式。
    /// </summary>
    public AssetBrowserSortMode SortMode { get; private set; } = AssetBrowserSortMode.PathAscending;

    /// <summary>
    /// 最近一次面板状态。
    /// </summary>
    public string Status { get; private set; } = "就绪";

    /// <summary>
    /// 设置搜索文本并刷新筛选结果。
    /// </summary>
    /// <param name="search">搜索文本。</param>
    public void SetSearch(string search)
    {
        _search = search ?? string.Empty;
        ApplyFilter();
    }

    /// <summary>
    /// 设置资产类型过滤并刷新筛选结果。
    /// </summary>
    /// <param name="kind">目标资产类型；null 表示全部类型。</param>
    public void SetKindFilter(AssetBrowserItemKind? kind)
    {
        KindFilter = kind;
        ApplyFilter();
    }

    /// <summary>
    /// 设置资产排序模式并刷新筛选结果。
    /// </summary>
    /// <param name="mode">排序模式。</param>
    public void SetSortMode(AssetBrowserSortMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知 Project Window 排序模式。");
        }

        SortMode = mode;
        ApplyFilter();
    }

    /// <summary>
    /// 刷新资产列表。
    /// </summary>
    /// <returns>完整资产快照。</returns>
    public IReadOnlyList<AssetBrowserItem> Refresh()
    {
        if (_source is IAssetBrowserRefreshableDataSource refreshable)
        {
            refreshable.RefreshAssets();
        }

        return ReloadSnapshot();
    }

    /// <summary>
    /// 刷新资产列表，并按 stable asset id 对共享选择态重新定位。
    /// </summary>
    /// <param name="selection">要跟随资产移动、重命名或删除的 Editor 选择态。</param>
    /// <returns>完整资产快照。</returns>
    public IReadOnlyList<AssetBrowserItem> Refresh(EditorSelection selection)
    {
        _trackedSelection = selection ?? throw new ArgumentNullException(nameof(selection));
        return Refresh();
    }

    /// <summary>
    /// 泵送数据源的增量变更，并在首帧或缓存变化时重载 Project Window 快照。
    /// </summary>
    /// <returns>本次调用重载了 UI 快照时返回 true。</returns>
    public bool ApplyPendingChanges()
    {
        bool changed = _source is IAssetBrowserRefreshableDataSource refreshable &&
            refreshable.ApplyPendingChanges();
        if (_source is IAssetBrowserDiagnosticDataSource diagnosticSource &&
            !string.IsNullOrWhiteSpace(diagnosticSource.AssetDatabaseDiagnostic))
        {
            Status = diagnosticSource.AssetDatabaseDiagnostic;
        }

        if (!changed && _snapshotLoaded)
        {
            return false;
        }

        _ = ReloadSnapshot();
        return true;
    }

    /// <summary>
    /// 选择资源并同步 Editor 选择态。
    /// </summary>
    /// <param name="path">资产路径。</param>
    /// <param name="selection">Editor 选择态。</param>
    /// <returns>资产存在时返回 true。</returns>
    public bool SelectAsset(string path, EditorSelection selection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(selection);
        _trackedSelection = selection;
        AssetBrowserItem? item = FindAsset(path);
        if (item is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.Value.AssetId))
        {
            selection.SelectAsset(item.Value.Path);
        }
        else
        {
            selection.SelectAsset(item.Value.AssetId, item.Value.Path);
        }

        Status = $"选中 {item.Value.Path}";
        return true;
    }

    /// <summary>
    /// 选择 Project Window 文件夹并同步 Editor 选择态。
    /// </summary>
    /// <param name="path">文件夹逻辑路径；空字符串表示 content 根目录。</param>
    /// <param name="selection">Editor 选择态。</param>
    /// <returns>文件夹存在时返回 true。</returns>
    public bool SelectFolder(string path, EditorSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        _trackedSelection = selection;
        EnsureSnapshotLoaded();

        string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        for (int i = 0; i < FolderTargets.Count; i++)
        {
            AssetBrowserFolderItem folder = FolderTargets[i];
            if (!string.Equals(folder.Path, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            selection.SelectFolder(folder.Path);
            ActiveFolderPath = folder.Path;
            ApplyFolderInputContext(folder.Path);
            ApplyFilter();
            Status = $"选中文件夹 {folder.DisplayName}";
            return true;
        }

        Status = $"文件夹不存在：{path}";
        return false;
    }

    /// <summary>
    /// 将 Project Window 的创建输入重定向到指定文件夹。
    /// </summary>
    /// <param name="folderPath">目标文件夹；空字符串表示 content 根目录。</param>
    /// <param name="kind">要创建的资产类型。</param>
    /// <returns>文件夹存在且创建类型受支持时返回 true。</returns>
    public bool BeginCreateAssetInFolder(string folderPath, AssetBrowserItemKind kind)
    {
        if (!IsCreateKindSupported(kind))
        {
            Status = $"Project Window 暂不支持直接创建 {kind} 资产。";
            return false;
        }

        if (!TryFindFolder(folderPath, out AssetBrowserFolderItem requestedFolder))
        {
            Status = $"文件夹不存在：{folderPath}";
            return false;
        }

        string compatiblePath = ResolveCompatibleCreateFolder(requestedFolder.Path, kind);
        if (!TryFindFolder(compatiblePath, out AssetBrowserFolderItem folder))
        {
            Status = $"工程缺少可创建 {kind} 的 logical root：{compatiblePath}";
            return false;
        }

        CreateKind = kind;
        CreatePath = MakeCreatePathUnique(ApplyFolderToCreatePath(folder.Path, GetDefaultCreatePath(kind)));
        Status = $"准备在 {folder.DisplayName} 创建 {kind}";
        return true;
    }

    /// <summary>
    /// 开始移动 / 重命名指定文件夹。
    /// </summary>
    /// <param name="path">当前文件夹路径。</param>
    /// <returns>文件夹存在且可移动时返回 true。</returns>
    public bool BeginMoveFolder(string path)
    {
        if (!TryFindFolder(path, out AssetBrowserFolderItem folder))
        {
            Status = $"文件夹不存在：{path}";
            return false;
        }

        if (string.IsNullOrEmpty(folder.Path))
        {
            Status = "content 根目录不能移动。";
            return false;
        }

        _pendingFolderMoveRequest = new AssetBrowserFolderMoveRequest(folder.Path, folder.Path);
        _pendingFolderMoveTargetPath = folder.Path;
        Status = $"准备移动文件夹 {folder.DisplayName}";
        return true;
    }

    /// <summary>
    /// 直接移动 / 重命名指定文件夹。
    /// </summary>
    /// <param name="path">当前文件夹路径。</param>
    /// <param name="newPath">移动后的文件夹路径。</param>
    /// <returns>移动已经执行时返回 true。</returns>
    public bool TryMoveFolder(string path, string newPath)
    {
        if (!TryFindFolder(path, out AssetBrowserFolderItem folder))
        {
            Status = $"文件夹不存在：{path}";
            return false;
        }

        if (string.IsNullOrEmpty(folder.Path))
        {
            Status = "content 根目录不能移动。";
            return false;
        }

        return MoveFolder(new AssetBrowserFolderMoveRequest(folder.Path, newPath));
    }

    /// <summary>
    /// 使用当前待编辑目标路径确认文件夹移动 / 重命名。
    /// </summary>
    /// <param name="path">当前文件夹路径。</param>
    /// <returns>移动已经执行时返回 true。</returns>
    public bool TryConfirmMoveFolder(string path)
    {
        if (!TryGetPendingFolderMove(path, out AssetBrowserFolderMoveRequest request))
        {
            _pendingFolderMoveRequest = null;
            _pendingFolderMoveTargetPath = string.Empty;
            Status = $"文件夹移动目标已失效，请重新请求移动：{path}";
            return false;
        }

        return MoveFolder(request with { NewPath = _pendingFolderMoveTargetPath });
    }


    /// <summary>
    /// 试听指定音频资产。
    /// </summary>
    /// <param name="path">资产路径。</param>
    /// <returns>开始试听时返回 true。</returns>
    public bool TryPreviewAudio(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AssetBrowserItem? item = FindAsset(path);
        if (item is null || item.Value.Kind != AssetBrowserItemKind.Audio || _audioPreview is null)
        {
            Status = "音频试听不可用";
            return false;
        }

        bool played = _audioPreview.TryPlayPreview(item.Value.Path);
        Status = played ? $"试听 {item.Value.Path}" : "音频试听失败";
        return played;
    }

    /// <summary>
    /// 为 Shell 层资产拖拽语义创建 typed payload。
    /// </summary>
    /// <param name="path">资产路径。</param>
    /// <param name="payload">可传递给 Shell drop 服务的 payload。</param>
    /// <returns>资产存在且有 stable asset id 时返回 true。</returns>
    public bool TryCreateDragPayload(string path, out AssetBrowserDragPayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AssetBrowserItem? item = FindAsset(path);
        if (item is null)
        {
            payload = default;
            Status = $"资产不存在：{path}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.Value.AssetId))
        {
            payload = default;
            Status = $"资产缺少 stable asset id，不能拖拽：{item.Value.Path}";
            return false;
        }

        // 拖拽语义绑定 stable assetId，移动时须与当前列表项交叉校验以防陈旧 payload。
        payload = new AssetBrowserDragPayload(item.Value.AssetId, item.Value.Path, item.Value.Kind);
        Status = $"拖拽 {item.Value.Path}";
        return true;
    }

    /// <summary>
    /// 打开指定脚本资产。
    /// </summary>
    /// <param name="path">资产路径。</param>
    /// <returns>成功发起打开时返回 true。</returns>
    public bool TryOpenScriptAsset(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AssetBrowserItem? item = FindAsset(path);
        if (item is null)
        {
            Status = $"资产不存在：{path}";
            return false;
        }

        if (item.Value.Kind != AssetBrowserItemKind.Script)
        {
            Status = $"仅 script 资产可外部打开：{item.Value.Path}";
            return false;
        }

        if (_openScriptAsset is null)
        {
            Status = "脚本外部编辑器不可用";
            return false;
        }

        bool opened = _openScriptAsset(item.Value.Path, out string diagnostic);
        Status = string.IsNullOrWhiteSpace(diagnostic)
            ? opened ? $"打开脚本 {item.Value.Path}" : $"脚本外部编辑器打开失败：{item.Value.Path}"
            : diagnostic;
        return opened;
    }

    /// <summary>
    /// 通过 Shell dirty-guard 转场打开指定 Scene，且不修改 Project StartScene。
    /// </summary>
    /// <param name="path">场景资产 rooted logical path。</param>
    /// <returns>打开或待确认转场已受理时返回 true。</returns>
    public bool TryOpenSceneAsset(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AssetBrowserItem? item = FindAsset(path);
        if (item is null)
        {
            Status = $"资产不存在：{path}";
            return false;
        }

        if (item.Value.Kind != AssetBrowserItemKind.Scene)
        {
            Status = $"仅 Scene 资产可在 Editor 中打开：{item.Value.Path}";
            return false;
        }

        if (_openSceneAsset is null)
        {
            Status = "场景打开服务不可用";
            return false;
        }

        bool opened = _openSceneAsset(item.Value.Path, out string diagnostic);
        Status = string.IsNullOrWhiteSpace(diagnostic)
            ? opened ? $"打开场景 {item.Value.Path}" : $"场景打开失败：{item.Value.Path}"
            : diagnostic;
        return opened;
    }

    /// <summary>
    /// 返回资产静态 descriptor 与当前 Session 合并后的 badge。
    /// </summary>
    /// <param name="path">资产 logical path。</param>
    /// <returns>资产存在时的 badge；否则为 <see cref="AssetBrowserBadge.None"/>。</returns>
    public AssetBrowserBadge GetBadges(string path)
    {
        AssetBrowserItem? item = FindAsset(path);
        return item is null ? AssetBrowserBadge.None : GetBadges(item.Value);
    }

    /// <summary>
    /// 请求删除指定资产；如果数据源要求确认，则只记录待确认状态。
    /// </summary>
    /// <param name="path">资产路径。</param>
    /// <returns>删除已经执行时返回 true。</returns>
    public bool TryRequestDeleteAsset(string path)
    {
        return TryDeleteAsset(path, confirmed: false);
    }

    /// <summary>
    /// 确认删除指定资产。
    /// </summary>
    /// <param name="path">资产路径。</param>
    /// <returns>删除已经执行时返回 true。</returns>
    public bool TryConfirmDeleteAsset(string path)
    {
        return TryDeleteAsset(path, confirmed: true);
    }

    /// <summary>
    /// 请求递归删除指定文件夹；如果数据源要求确认，则只记录待确认状态。
    /// </summary>
    /// <param name="path">文件夹路径。</param>
    /// <returns>删除已经执行时返回 true。</returns>
    public bool TryRequestDeleteFolder(string path)
    {
        return TryDeleteFolder(path, confirmed: false);
    }

    /// <summary>
    /// 确认递归删除指定文件夹。
    /// </summary>
    /// <param name="path">文件夹路径。</param>
    /// <returns>删除已经执行时返回 true。</returns>
    public bool TryConfirmDeleteFolder(string path)
    {
        return TryDeleteFolder(path, confirmed: true);
    }

    /// <summary>
    /// 开始移动 / 重命名指定资产；确认前绑定当前 stable asset id。
    /// </summary>
    /// <param name="path">当前资产路径。</param>
    /// <returns>资产存在且可移动时返回 true。</returns>
    public bool BeginMoveAsset(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AssetBrowserItem? item = FindAsset(path);
        if (item is null)
        {
            Status = $"资产不存在：{path}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.Value.AssetId))
        {
            Status = $"资产缺少 stable asset id，不能移动：{item.Value.Path}";
            return false;
        }

        _pendingMoveRequest = new AssetBrowserMoveRequest(
            item.Value.Path,
            item.Value.AssetId,
            item.Value.Kind,
            item.Value.Path);
        _pendingMoveTargetPath = item.Value.Path;
        Status = $"准备移动 {item.Value.Path}";
        return true;
    }

    /// <summary>
    /// 直接移动 / 重命名指定资产。
    /// </summary>
    /// <param name="path">当前资产路径。</param>
    /// <param name="newPath">移动后的资产路径。</param>
    /// <returns>移动已经执行时返回 true。</returns>
    public bool TryMoveAsset(string path, string newPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);
        AssetBrowserItem? item = FindAsset(path);
        if (item is null)
        {
            Status = $"资产不存在：{path}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.Value.AssetId))
        {
            Status = $"资产缺少 stable asset id，不能移动：{item.Value.Path}";
            return false;
        }

        return MoveAsset(new AssetBrowserMoveRequest(
            item.Value.Path,
            item.Value.AssetId,
            item.Value.Kind,
            newPath));
    }

    /// <summary>
    /// 创建指定类型的 Project Window 资产。
    /// </summary>
    /// <param name="path">新资产 logical path。</param>
    /// <param name="kind">新资产类型。</param>
    /// <returns>创建成功时返回 true。</returns>
    public bool TryCreateAsset(string path, AssetBrowserItemKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsCreateKindSupported(kind))
        {
            Status = $"Project Window 暂不支持直接创建 {kind} 资产。";
            return false;
        }

        if (_createAsset is null)
        {
            Status = "资产创建服务不可用";
            return false;
        }

        AssetBrowserCreateResult result = _createAsset(new AssetBrowserCreateRequest(path, kind));
        Status = string.IsNullOrWhiteSpace(result.Diagnostic)
            ? result.Succeeded ? $"已创建资产 {path}" : $"创建未执行：{path}"
            : result.Diagnostic;
        if (result.Succeeded)
        {
            _ = Refresh();
            CreatePath = SuggestNextCreatePath(kind, path);
        }

        return result.Succeeded;
    }

    /// <summary>
    /// 使用当前 Create Type / New Asset 输入创建资产。
    /// </summary>
    /// <returns>创建成功时返回 true。</returns>
    public bool TryCreateCurrentAsset()
    {
        return TryCreateAsset(CreatePath, CreateKind);
    }

    /// <summary>
    /// 准备从外部源文件导入到指定 Project Window 文件夹。
    /// </summary>
    /// <param name="sourceFullPath">外部源文件完整路径。</param>
    /// <param name="folderPath">目标逻辑文件夹；空字符串表示 content 根目录。</param>
    /// <param name="kind">导入资产类型。</param>
    /// <returns>支持该导入类型时返回 true。</returns>
    public bool BeginImportAssetInFolder(string sourceFullPath, string folderPath, AssetBrowserItemKind kind)
    {
        if (!IsImportKindSupported(kind))
        {
            Status = $"Project Window 暂不支持导入 {kind} 资产。";
            return false;
        }

        string candidateName = Path.GetFileName((sourceFullPath ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(candidateName))
        {
            candidateName = Path.GetFileName(GetDefaultCreatePath(kind));
        }

        string compatibleFolder = ResolveCompatibleCreateFolder(folderPath, kind);
        ImportKind = kind;
        ImportSourcePath = sourceFullPath ?? string.Empty;
        ImportDestinationPath = MakePathUnique(ApplyFolderToCreatePath(compatibleFolder, candidateName), kind);
        Status = $"准备导入 {kind} 到 {ImportDestinationPath}";
        return true;
    }

    /// <summary>
    /// 导入外部 Texture / Audio 文件到 content。
    /// </summary>
    /// <param name="sourceFullPath">外部源文件完整路径。</param>
    /// <param name="destinationPath">目标 logical path。</param>
    /// <param name="kind">导入资产类型。</param>
    /// <returns>导入成功时返回 true。</returns>
    public bool TryImportAsset(string sourceFullPath, string destinationPath, AssetBrowserItemKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!IsImportKindSupported(kind))
        {
            Status = $"Project Window 暂不支持导入 {kind} 资产。";
            return false;
        }

        if (_importAsset is null)
        {
            Status = "资产导入服务不可用";
            return false;
        }

        AssetBrowserImportResult result = _importAsset(new AssetBrowserImportRequest(sourceFullPath, destinationPath, kind));
        Status = string.IsNullOrWhiteSpace(result.Diagnostic)
            ? result.Succeeded ? $"已导入资产 {destinationPath}" : $"导入未执行：{destinationPath}"
            : result.Diagnostic;
        if (result.Succeeded)
        {
            _ = Refresh();
            ImportSourcePath = sourceFullPath;
            ImportKind = kind;
            ImportDestinationPath = SuggestNextImportPath(kind, destinationPath);
        }

        return result.Succeeded;
    }

    /// <summary>
    /// 使用当前 Import Type / Source / Destination 输入导入资产。
    /// </summary>
    /// <returns>导入成功时返回 true。</returns>
    public bool TryImportCurrentAsset()
    {
        return TryImportAsset(ImportSourcePath, ImportDestinationPath, ImportKind);
    }

    /// <summary>
    /// 打开导入源文件选择器，并把结果映射到当前 Project Window 文件夹。
    /// </summary>
    /// <param name="folderPath">目标逻辑文件夹；空字符串表示 content 根目录。</param>
    /// <returns>成功选择源文件时返回 true。</returns>
    public bool TryPickImportSource(string folderPath)
    {
        if (_pickImportSource is null)
        {
            Status = "导入源文件选择器不可用";
            return false;
        }

        AssetBrowserImportSourcePickResult result = _pickImportSource(ImportSourcePath, ImportKind);
        if (!result.Succeeded)
        {
            Status = string.IsNullOrWhiteSpace(result.Diagnostic)
                ? "已取消选择导入源文件"
                : result.Diagnostic;
            return false;
        }

        return BeginImportAssetInFolder(result.SourceFullPath, folderPath, ImportKind);
    }

    /// <summary>
    /// 把 Project Window typed drag payload 移动到目标逻辑文件夹。
    /// </summary>
    /// <param name="payload">资产拖拽 payload。</param>
    /// <param name="targetFolderPath">目标逻辑文件夹；空字符串表示 content 根目录。</param>
    /// <returns>移动已经执行时返回 true。</returns>
    public bool TryMoveDragPayloadToFolder(AssetBrowserDragPayload payload, string targetFolderPath)
    {
        if (string.IsNullOrWhiteSpace(payload.AssetId) || string.IsNullOrWhiteSpace(payload.Path))
        {
            Status = "拖拽移动 payload 缺少 stable asset id 或 logical path。";
            return false;
        }

        AssetBrowserItem? item = FindAsset(payload.Path);
        if (item is null)
        {
            Status = $"拖拽移动资产不存在：{payload.Path}";
            return false;
        }

        if (!string.Equals(item.Value.AssetId, payload.AssetId, StringComparison.OrdinalIgnoreCase) ||
            item.Value.Kind != payload.Kind)
        {
            Status = $"拖拽移动 payload 已失效：{payload.Path}";
            return false;
        }

        // 目标文件夹归一化为 content 逻辑路径，再拼接文件名生成新 logical path。
        if (!TryNormalizeTargetFolder(targetFolderPath, out string normalizedFolder, out string diagnostic))
        {
            Status = diagnostic;
            return false;
        }

        string fileName = Path.GetFileName(item.Value.Path.Replace('/', Path.DirectorySeparatorChar));
        string targetPath = string.IsNullOrEmpty(normalizedFolder)
            ? fileName
            : normalizedFolder + "/" + fileName;
        return MoveAsset(new AssetBrowserMoveRequest(
            item.Value.Path,
            item.Value.AssetId ?? payload.AssetId,
            item.Value.Kind,
            targetPath));
    }

    /// <summary>
    /// 使用当前待编辑目标路径确认移动 / 重命名。
    /// </summary>
    /// <param name="path">当前资产路径。</param>
    /// <returns>移动已经执行时返回 true。</returns>
    public bool TryConfirmMoveAsset(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AssetBrowserItem? item = FindAsset(path);
        if (item is null)
        {
            _pendingMoveRequest = null;
            _pendingMoveTargetPath = string.Empty;
            Status = $"资产不存在：{path}";
            return false;
        }

        if (!TryGetPendingMoveFor(item.Value, out AssetBrowserMoveRequest request))
        {
            _pendingMoveRequest = null;
            _pendingMoveTargetPath = string.Empty;
            Status = $"移动目标已失效，请重新请求移动：{item.Value.Path}";
            return false;
        }

        return MoveAsset(request with { NewPath = _pendingMoveTargetPath });
    }

    /// <inheritdoc />
    public void Draw(in EditorContext context)
    {
        _trackedSelection = context.Selection;
        _ = ApplyPendingChanges();
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        DrawToolbar(context.Selection);
        if (!string.IsNullOrWhiteSpace(_search))
        {
            // 搜索可能命中动态“启动 / 当前” badge；只在搜索态重算，普通浏览不逐帧分配投影。
            ApplyFilter();
        }

        if (ImGui.BeginTable(
            "project_window_layout",
            2,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Folders", ImGuiTableColumnFlags.WidthFixed, 220f);
            ImGui.TableSetupColumn("Contents", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            _ = ImGui.TableNextColumn();
            _ = ImGui.BeginChild("project_folder_tree");
            DrawFolderTree(context.Selection);
            ImGui.EndChild();

            _ = ImGui.TableNextColumn();
            DrawBreadcrumbs(context.Selection);
            ImGui.Separator();
            _ = ImGui.BeginChild("project_folder_contents");
            for (int i = 0; i < VisibleFolders.Count; i++)
            {
                DrawFolderContentRow(VisibleFolders[i], context.Selection);
            }

            for (int i = 0; i < FilteredAssets.Count; i++)
            {
                DrawAssetRow(FilteredAssets[i], context.Selection);
            }

            ImGui.EndChild();
            ImGui.EndTable();
        }

        DrawPendingActionEditors();
        ImGui.TextUnformatted(Status);
        ImGui.End();
    }

    private void DrawToolbar(EditorSelection selection)
    {
        if (ImGui.Button("刷新"))
        {
            _ = Refresh(selection);
        }

        ImGui.SameLine();
        if (ImGui.Button("新建..."))
        {
            _showCreateEditor = !_showCreateEditor;
            _showImportEditor = false;
            _ = BeginCreateAssetInFolder(ActiveFolderPath, CreateKind);
        }

        ImGui.SameLine();
        if (ImGui.Button("导入..."))
        {
            _showImportEditor = !_showImportEditor;
            _showCreateEditor = false;
        }

        AssetBrowserItem? selectedAsset = string.IsNullOrWhiteSpace(selection.AssetPath)
            ? null
            : FindAsset(selection.AssetPath);
        if (selectedAsset is { } selected)
        {
            ImGui.SameLine();
            DrawPrimaryAssetAction(selected);
            ImGui.SameLine();
            if (ImGui.Button("重命名"))
            {
                _ = BeginMoveAsset(selected.Path);
            }

            ImGui.SameLine();
            if (ImGui.Button("删除"))
            {
                _ = TryRequestDeleteAsset(selected.Path);
            }
        }

        if (_showCreateEditor)
        {
            DrawCreateControls(selection);
        }
        else if (_showImportEditor)
        {
            DrawImportControls(selection);
        }

        string search = _search;
        if (ImGui.InputText("搜索", ref search, 128))
        {
            SetSearch(search);
        }
        ImGui.SameLine();
        int kindIndex = KindFilter.HasValue ? (int)KindFilter.Value + 1 : 0;
        if (ImGui.Combo("类型", ref kindIndex, KindFilterLabels, KindFilterLabels.Length))
        {
            SetKindFilter(kindIndex == 0 ? null : (AssetBrowserItemKind)(kindIndex - 1));
        }

        ImGui.SameLine();
        int sortMode = (int)SortMode;
        if (ImGui.Combo("排序", ref sortMode, SortModeLabels, SortModeLabels.Length) &&
            sortMode >= 0 &&
            sortMode < SortModeLabels.Length)
        {
            SetSortMode((AssetBrowserSortMode)sortMode);
        }
    }

    private void DrawCreateControls(EditorSelection selection)
    {
        int createKindIndex = Array.IndexOf(CreateKinds, CreateKind);
        if (createKindIndex < 0)
        {
            createKindIndex = Array.IndexOf(CreateKinds, AssetBrowserItemKind.Script);
            CreateKind = AssetBrowserItemKind.Script;
        }

        if (ImGui.Combo("Create Type", ref createKindIndex, CreateKindLabels, CreateKindLabels.Length) &&
            createKindIndex >= 0 &&
            createKindIndex < CreateKinds.Length)
        {
            CreateKind = CreateKinds[createKindIndex];
            _ = BeginCreateAssetInFolder(selection.FolderPath ?? ActiveFolderPath, CreateKind);
        }

        ImGui.SameLine();
        string createPath = CreatePath;
        if (ImGui.InputText("New Asset", ref createPath, 256))
        {
            CreatePath = createPath;
        }

        ImGui.SameLine();
        if (ImGui.Button("Create"))
        {
            _ = TryCreateAsset(CreatePath, CreateKind);
        }
    }

    private void DrawImportControls(EditorSelection selection)
    {
        int importKindIndex = Array.IndexOf(ImportKinds, ImportKind);
        if (importKindIndex < 0)
        {
            importKindIndex = Array.IndexOf(ImportKinds, AssetBrowserItemKind.Texture);
            ImportKind = AssetBrowserItemKind.Texture;
        }

        if (ImGui.Combo("Import Type", ref importKindIndex, ImportKindLabels, ImportKindLabels.Length) &&
            importKindIndex >= 0 &&
            importKindIndex < ImportKinds.Length)
        {
            ImportKind = ImportKinds[importKindIndex];
            string folder = ResolveCompatibleCreateFolder(selection.FolderPath ?? ActiveFolderPath, ImportKind);
            ImportDestinationPath = MakePathUnique(ApplyFolderToCreatePath(folder, GetDefaultCreatePath(ImportKind)), ImportKind);
        }

        string importSourcePath = ImportSourcePath;
        if (ImGui.InputText("Source File", ref importSourcePath, 512))
        {
            ImportSourcePath = importSourcePath;
        }

        ImGui.SameLine();
        if (ImGui.Button("Browse Source"))
        {
            _ = TryPickImportSource(selection.FolderPath ?? string.Empty);
        }

        string importDestinationPath = ImportDestinationPath;
        if (ImGui.InputText("Import As", ref importDestinationPath, 256))
        {
            ImportDestinationPath = importDestinationPath;
        }

        if (ImGui.Button("Import"))
        {
            _ = TryImportAsset(ImportSourcePath, ImportDestinationPath, ImportKind);
        }
    }

    private void DrawFolderTree(EditorSelection selection)
    {
        bool rootSelected = string.IsNullOrWhiteSpace(selection.FolderPath);
        if (ImGui.Selectable("工程##project-root", rootSelected))
        {
            _ = SelectFolder(string.Empty, selection);
        }

        for (int i = 0; i < FolderTargets.Count; i++)
        {
            if (IsDirectFolderChild(FolderTargets[i].Path, string.Empty))
            {
                DrawFolderTreeNode(FolderTargets[i], selection);
            }
        }
    }

    private void DrawFolderTreeNode(AssetBrowserFolderItem folder, EditorSelection selection)
    {
        bool hasChildren = FolderTargets.Any(candidate => IsDirectFolderChild(candidate.Path, folder.Path));
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (!hasChildren)
        {
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        }

        if (string.Equals(selection.FolderPath, folder.Path, StringComparison.OrdinalIgnoreCase))
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        if (GetLogicalDirectoryName(folder.Path) is null)
        {
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        }

        bool open = ImGui.TreeNodeEx($"{folder.DisplayName} ({folder.AssetCount})##tree-{folder.Path}", flags);
        if (ImGui.IsItemClicked())
        {
            _ = SelectFolder(folder.Path, selection);
        }

        DrawFolderDropTarget(folder);
        DrawFolderContextMenu(folder);
        if (hasChildren && open)
        {
            for (int i = 0; i < FolderTargets.Count; i++)
            {
                if (IsDirectFolderChild(FolderTargets[i].Path, folder.Path))
                {
                    DrawFolderTreeNode(FolderTargets[i], selection);
                }
            }

            ImGui.TreePop();
        }
    }

    private void DrawFolderContentRow(AssetBrowserFolderItem folder, EditorSelection selection)
    {
        bool selected = string.Equals(selection.FolderPath, folder.Path, StringComparison.OrdinalIgnoreCase);
        if (ImGui.Selectable($"[文件夹] {folder.DisplayName}  {folder.AssetCount} 项##content-folder-{folder.Path}", selected))
        {
            _ = SelectFolder(folder.Path, selection);
        }

        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && ImGui.IsItemHovered())
        {
            _ = SelectFolder(folder.Path, selection);
        }

        DrawFolderDropTarget(folder);
        DrawFolderContextMenu(folder);
    }

    private void DrawFolderDropTarget(AssetBrowserFolderItem folder)
    {
        if (!ImGui.BeginDragDropTarget())
        {
            return;
        }

        // Shell 层 drop 服务消费 typed payload；此处只校验 assetId 并委托移动服务。
        if (AssetBrowserDragPayloadImGui.TryAcceptPayload(out AssetBrowserDragPayload payload))
        {
            _ = TryMoveDragPayloadToFolder(payload, folder.Path);
        }

        ImGui.EndDragDropTarget();
    }

    private void DrawFolderContextMenu(AssetBrowserFolderItem folder)
    {
        if (!ImGui.BeginPopupContextItem($"folder-context-{folder.Path}"))
        {
            return;
        }

        if (ImGui.MenuItem("新建资产..."))
        {
            _showCreateEditor = true;
            _showImportEditor = false;
            _ = BeginCreateAssetInFolder(folder.Path, CreateKind);
        }

        if (ImGui.MenuItem("导入..."))
        {
            _showImportEditor = true;
            _showCreateEditor = false;
            ApplyFolderInputContext(folder.Path);
        }

        if (!string.IsNullOrEmpty(folder.Path) && !IsProtectedLogicalRoot(folder.Path))
        {
            ImGui.Separator();
            if (ImGui.MenuItem("移动 / 重命名"))
            {
                _ = BeginMoveFolder(folder.Path);
            }

            if (ImGui.MenuItem("删除"))
            {
                _ = TryRequestDeleteFolder(folder.Path);
            }
        }

        ImGui.EndPopup();
    }

    private void DrawBreadcrumbs(EditorSelection selection)
    {
        for (int i = 0; i < Breadcrumbs.Count; i++)
        {
            AssetBrowserBreadcrumbItem breadcrumb = Breadcrumbs[i];
            if (i > 0)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted(">");
                ImGui.SameLine();
            }

            if (ImGui.Button($"{breadcrumb.Label}##breadcrumb-{breadcrumb.Path}"))
            {
                _ = SelectFolder(breadcrumb.Path, selection);
            }
        }
    }

    private void DrawAssetRow(AssetBrowserItem item, EditorSelection selection)
    {
        if (item.Thumbnail is AssetThumbnail thumbnail && thumbnail.TextureHandle != 0)
        {
            ImGui.Image(CreateTextureRef(thumbnail.TextureHandle), new System.Numerics.Vector2(32, 32));
            ImGui.SameLine();
        }

        string typeLabel = item.Descriptor?.TypeLabel ?? GetDefaultTypeLabel(item.Kind);
        string badgeLabel = FormatBadges(GetBadges(item));
        string presentation = string.IsNullOrWhiteSpace(badgeLabel)
            ? $"{item.DisplayName}  [{typeLabel}]"
            : $"{item.DisplayName}  [{typeLabel}]  [{badgeLabel}]";
        string label = string.IsNullOrWhiteSpace(item.AssetId)
            ? presentation
            : $"{presentation}##{item.AssetId}";
        bool selected = IsAssetSelected(selection, item);
        if (ImGui.Selectable(label, selected))
        {
            _ = SelectAsset(item.Path, selection);
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (item.Kind == AssetBrowserItemKind.Scene)
            {
                _ = TryOpenSceneAsset(item.Path);
            }
            else if (item.Kind == AssetBrowserItemKind.Script)
            {
                _ = TryOpenScriptAsset(item.Path);
            }
        }

        if (ImGui.BeginDragDropSource())
        {
            if (TryCreateDragPayload(item.Path, out AssetBrowserDragPayload payload))
            {
                _ = AssetBrowserDragPayloadImGui.SetPayload(payload);
                ImGui.TextUnformatted(item.Path);
            }

            ImGui.EndDragDropSource();
        }

        DrawAssetContextMenu(item);
        if (!string.IsNullOrWhiteSpace(_search))
        {
            ImGui.TextUnformatted($"路径：{item.Path}");
        }

        if (!string.IsNullOrWhiteSpace(item.Descriptor?.Purpose))
        {
            ImGui.TextUnformatted($"用途：{item.Descriptor.Value.Purpose}");
        }

        if (!string.IsNullOrWhiteSpace(item.PreviewSummary))
        {
            ImGui.TextUnformatted($"摘要：{item.PreviewSummary}");
        }
    }

    private void DrawPrimaryAssetAction(AssetBrowserItem item)
    {
        if (item.Kind == AssetBrowserItemKind.Scene)
        {
            if (ImGui.Button("打开场景"))
            {
                _ = TryOpenSceneAsset(item.Path);
            }
        }
        else if (item.Kind == AssetBrowserItemKind.Script)
        {
            if (ImGui.Button("打开脚本"))
            {
                _ = TryOpenScriptAsset(item.Path);
            }
        }
        else if (item.Kind == AssetBrowserItemKind.Audio)
        {
            if (ImGui.Button("试听"))
            {
                _ = TryPreviewAudio(item.Path);
            }
        }
        else if (item.Kind == AssetBrowserItemKind.Prefab && ImGui.Button("实例化"))
        {
            _instantiatePrefab?.Invoke(item.Path);
            Status = $"实例化 {item.Path}";
        }
    }

    private void DrawAssetContextMenu(AssetBrowserItem item)
    {
        if (!ImGui.BeginPopupContextItem($"asset-context-{item.AssetId ?? item.Path}"))
        {
            return;
        }

        if (item.Kind == AssetBrowserItemKind.Scene && ImGui.MenuItem("打开场景"))
        {
            _ = TryOpenSceneAsset(item.Path);
        }

        if (item.Kind == AssetBrowserItemKind.Script && ImGui.MenuItem("打开脚本"))
        {
            _ = TryOpenScriptAsset(item.Path);
        }

        if (item.Kind == AssetBrowserItemKind.Audio && ImGui.MenuItem("试听"))
        {
            _ = TryPreviewAudio(item.Path);
        }

        if (item.Kind == AssetBrowserItemKind.Prefab && ImGui.MenuItem("实例化"))
        {
            _instantiatePrefab?.Invoke(item.Path);
            Status = $"实例化 {item.Path}";
        }

        ImGui.Separator();
        if (ImGui.MenuItem("移动 / 重命名"))
        {
            _ = BeginMoveAsset(item.Path);
        }

        if (ImGui.MenuItem("删除"))
        {
            _ = TryRequestDeleteAsset(item.Path);
        }

        ImGui.EndPopup();
    }

    private void DrawPendingActionEditors()
    {
        if (_pendingMoveRequest is { } move)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"移动 / 重命名：{move.Path}");
            _ = ImGui.InputText("目标路径##asset-move-target", ref _pendingMoveTargetPath, 256);
            if (ImGui.Button("确认移动##asset-move"))
            {
                _ = TryConfirmMoveAsset(move.Path);
            }

            ImGui.SameLine();
            if (ImGui.Button("取消##asset-move"))
            {
                _pendingMoveRequest = null;
                _pendingMoveTargetPath = string.Empty;
            }
        }

        if (_pendingFolderMoveRequest is { } folderMove)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"移动 / 重命名文件夹：{folderMove.Path}");
            _ = ImGui.InputText("目标路径##folder-move-target", ref _pendingFolderMoveTargetPath, 256);
            if (ImGui.Button("确认移动##folder-move"))
            {
                _ = TryConfirmMoveFolder(folderMove.Path);
            }

            ImGui.SameLine();
            if (ImGui.Button("取消##folder-move"))
            {
                _pendingFolderMoveRequest = null;
                _pendingFolderMoveTargetPath = string.Empty;
            }
        }

        if (_pendingDeleteRequest is { } delete)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"确认删除资产：{delete.Path}");
            if (ImGui.Button("确认删除##asset-delete"))
            {
                _ = TryConfirmDeleteAsset(delete.Path);
            }

            ImGui.SameLine();
            if (ImGui.Button("取消##asset-delete"))
            {
                _pendingDeleteRequest = null;
            }
        }

        if (_pendingFolderDeleteRequest is { } folderDelete)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"确认递归删除文件夹：{folderDelete.Path}");
            if (ImGui.Button("确认删除##folder-delete"))
            {
                _ = TryConfirmDeleteFolder(folderDelete.Path);
            }

            ImGui.SameLine();
            if (ImGui.Button("取消##folder-delete"))
            {
                _pendingFolderDeleteRequest = null;
            }
        }
    }

    private bool TryDeleteAsset(string path, bool confirmed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AssetBrowserItem? item = FindAsset(path);
        if (item is null)
        {
            Status = $"资产不存在：{path}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.Value.AssetId))
        {
            Status = $"资产缺少 stable asset id，不能删除：{item.Value.Path}";
            return false;
        }

        if (_deleteAsset is null)
        {
            Status = "资产删除服务不可用";
            return false;
        }

        AssetBrowserDeleteRequest request;
        if (confirmed)
        {
            if (!TryGetPendingDeleteFor(item.Value, out request))
            {
                _pendingDeleteRequest = null;
                Status = $"删除确认已失效，请重新请求删除：{item.Value.Path}";
                return false;
            }

            request = request with { Confirmed = true };
        }
        else
        {
            request = new AssetBrowserDeleteRequest(
                item.Value.Path,
                item.Value.AssetId,
                item.Value.Kind,
                Confirmed: false);
        }

        AssetBrowserDeleteResult result = _deleteAsset(request);
        Status = string.IsNullOrWhiteSpace(result.Diagnostic)
            ? result.Succeeded ? $"已删除 {item.Value.Path}" : $"删除未执行：{item.Value.Path}"
            : result.Diagnostic;
        if (result.RequiresConfirmation)
        {
            // 删除需二次确认时保留 pending 状态，由 UI 按钮触发 Confirm 路径。
            _pendingDeleteRequest = request with { Confirmed = false };
            return false;
        }

        _pendingDeleteRequest = null;
        if (result.Succeeded)
        {
            _ = Refresh();
        }

        return result.Succeeded;
    }

    private bool MoveAsset(AssetBrowserMoveRequest request)
    {
        if (_moveAsset is null)
        {
            Status = "资产移动服务不可用";
            return false;
        }

        if (string.Equals(request.Path, request.NewPath, StringComparison.OrdinalIgnoreCase))
        {
            Status = $"移动目标与源路径相同：{request.Path}";
            return false;
        }

        AssetBrowserMoveResult result = _moveAsset(request);
        Status = string.IsNullOrWhiteSpace(result.Diagnostic)
            ? result.Succeeded ? $"已移动资产 {request.Path}" : $"移动未执行：{request.Path}"
            : result.Diagnostic;
        if (result.Succeeded)
        {
            _pendingMoveRequest = null;
            _pendingMoveTargetPath = string.Empty;
            _ = Refresh();
        }

        return result.Succeeded;
    }

    private bool MoveFolder(AssetBrowserFolderMoveRequest request)
    {
        if (_moveFolder is null)
        {
            Status = "文件夹移动服务不可用";
            return false;
        }

        if (string.Equals(request.Path, request.NewPath, StringComparison.OrdinalIgnoreCase))
        {
            Status = $"文件夹移动目标与源路径相同：{request.Path}";
            return false;
        }

        AssetBrowserFolderMoveResult result = _moveFolder(request);
        Status = string.IsNullOrWhiteSpace(result.Diagnostic)
            ? result.Succeeded ? $"已移动文件夹 {request.Path}" : $"文件夹移动未执行：{request.Path}"
            : result.Diagnostic;
        if (result.Succeeded)
        {
            _pendingFolderMoveRequest = null;
            _pendingFolderMoveTargetPath = string.Empty;
            RemapFolderContextAfterMove(request.Path, request.NewPath);
            _ = Refresh();
        }

        return result.Succeeded;
    }

    private bool TryDeleteFolder(string path, bool confirmed)
    {
        if (!TryFindFolder(path, out AssetBrowserFolderItem folder))
        {
            Status = $"文件夹不存在：{path}";
            return false;
        }

        if (string.IsNullOrEmpty(folder.Path))
        {
            Status = "content 根目录不能删除。";
            return false;
        }

        if (_deleteFolder is null)
        {
            Status = "文件夹删除服务不可用";
            return false;
        }

        AssetBrowserFolderDeleteRequest request;
        if (confirmed)
        {
            if (!TryGetPendingFolderDelete(folder.Path, out request))
            {
                _pendingFolderDeleteRequest = null;
                Status = $"文件夹删除确认已失效，请重新请求删除：{folder.DisplayName}";
                return false;
            }

            request = request with { Confirmed = true };
        }
        else if (!TryBuildFolderDeleteRequest(folder.Path, confirmed: false, out request))
        {
            return false;
        }

        AssetBrowserFolderDeleteResult result = _deleteFolder(request);
        Status = string.IsNullOrWhiteSpace(result.Diagnostic)
            ? result.Succeeded ? $"已删除文件夹 {folder.Path}" : $"文件夹删除未执行：{folder.Path}"
            : result.Diagnostic;
        if (result.RequiresConfirmation)
        {
            _pendingFolderDeleteRequest = request with { Confirmed = false };
            return false;
        }

        _pendingFolderDeleteRequest = null;
        if (result.Succeeded)
        {
            _ = Refresh();
        }

        return result.Succeeded;
    }

    private static bool IsCreateKindSupported(AssetBrowserItemKind kind)
    {
        return Array.IndexOf(CreateKinds, kind) >= 0;
    }

    private static bool IsImportKindSupported(AssetBrowserItemKind kind)
    {
        return Array.IndexOf(ImportKinds, kind) >= 0;
    }

    private static string GetDefaultCreatePath(AssetBrowserItemKind kind)
    {
        return kind switch
        {
            AssetBrowserItemKind.Folder => "New Folder",
            AssetBrowserItemKind.Material => "materials.json",
            AssetBrowserItemKind.Texture => "textures/NewTexture.png",
            AssetBrowserItemKind.Audio => "audio/NewAudio.wav",
            AssetBrowserItemKind.Scene => "scenes/NewScene.scene",
            AssetBrowserItemKind.Prefab => "prefabs/NewPrefab.prefab",
            AssetBrowserItemKind.Script => "scripts/NewBehaviour.cs",
            AssetBrowserItemKind.UiScreen => "ui/screens/NewScreen.xhtml",
            AssetBrowserItemKind.Json => "data/NewAsset.json",
            AssetBrowserItemKind.Other => "NewAsset",
            _ => "NewAsset",
        };
    }

    private void ApplyFolderInputContext(string folderPath)
    {
        string createFolder = ResolveCompatibleCreateFolder(folderPath, CreateKind);
        string importFolder = ResolveCompatibleCreateFolder(folderPath, ImportKind);
        CreatePath = MakeCreatePathUnique(RebasePathToFolder(createFolder, CreatePath));
        ImportDestinationPath = MakePathUnique(
            RebasePathToFolder(importFolder, ImportDestinationPath),
            ImportKind);
    }

    private string ResolveCompatibleCreateFolder(string folderPath, AssetBrowserItemKind kind)
    {
        string normalized = NormalizeFolderPath(folderPath);
        bool hasContentRoot = FolderTargets.Any(folder =>
            string.Equals(folder.Path, "Content", StringComparison.OrdinalIgnoreCase));
        bool hasScriptRoot = FolderTargets.Any(folder =>
            string.Equals(folder.Path, "ScriptSource", StringComparison.OrdinalIgnoreCase));
        return !hasContentRoot || !hasScriptRoot
            ? normalized
            : kind == AssetBrowserItemKind.Script
            ? IsSameOrChildFolder(normalized, "ScriptSource") ? normalized : "ScriptSource"
            : IsSameOrChildFolder(normalized, "Content") ? normalized : "Content";
    }

    private static bool IsSameOrChildFolder(string candidate, string root)
    {
        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    private void RemapFolderContextAfterMove(string oldPath, string newPath)
    {
        string normalizedOldPath = NormalizeFolderPath(oldPath);
        string normalizedNewPath = ResolveFolderMoveTarget(normalizedOldPath, newPath);
        bool activeChanged = TryRemapFolderPath(
            ActiveFolderPath,
            normalizedOldPath,
            normalizedNewPath,
            out string remappedActivePath);
        string remappedSelectionPath = string.Empty;
        bool selectionChanged = _trackedSelection?.FolderPath is { } selectedFolder &&
            TryRemapFolderPath(
                selectedFolder,
                normalizedOldPath,
                normalizedNewPath,
                out remappedSelectionPath);

        if (activeChanged)
        {
            ActiveFolderPath = remappedActivePath;
        }

        if (selectionChanged)
        {
            _trackedSelection!.SelectFolder(remappedSelectionPath);
        }

        if (activeChanged || selectionChanged)
        {
            string folderContext = selectionChanged ? remappedSelectionPath : remappedActivePath;
            ApplyFolderInputContext(folderContext);
        }
    }

    private string FindNearestExistingFolder(string path)
    {
        string candidate = NormalizeFolderPath(path);
        while (true)
        {
            for (int i = 0; i < FolderTargets.Count; i++)
            {
                if (string.Equals(FolderTargets[i].Path, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return FolderTargets[i].Path;
                }
            }

            if (candidate.Length == 0)
            {
                return string.Empty;
            }

            candidate = GetLogicalDirectoryName(candidate) ?? string.Empty;
        }
    }

    private static bool TryRemapFolderPath(
        string path,
        string oldPath,
        string newPath,
        out string remappedPath)
    {
        string normalizedPath = NormalizeFolderPath(path);
        if (string.Equals(normalizedPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            remappedPath = newPath;
            return true;
        }

        if (oldPath.Length > 0 &&
            normalizedPath.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
        {
            remappedPath = newPath + normalizedPath[oldPath.Length..];
            return true;
        }

        remappedPath = normalizedPath;
        return false;
    }

    private static string ResolveFolderMoveTarget(string oldPath, string newPath)
    {
        string normalizedTarget = NormalizeFolderPath(newPath);
        string? rootedPrefix = oldPath.StartsWith("Content/", StringComparison.OrdinalIgnoreCase)
            ? "Content"
            : oldPath.StartsWith("ScriptSource/", StringComparison.OrdinalIgnoreCase)
                ? "ScriptSource"
                : null;
        return rootedPrefix is null ||
            normalizedTarget.Equals("Content", StringComparison.OrdinalIgnoreCase) ||
            normalizedTarget.StartsWith("Content/", StringComparison.OrdinalIgnoreCase) ||
            normalizedTarget.Equals("ScriptSource", StringComparison.OrdinalIgnoreCase) ||
            normalizedTarget.StartsWith("ScriptSource/", StringComparison.OrdinalIgnoreCase)
                ? normalizedTarget
                : rootedPrefix + "/" + normalizedTarget;
    }

    private static string NormalizeFolderPath(string path)
    {
        return (path ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
    }

    private static string RebasePathToFolder(string folderPath, string path)
    {
        string normalizedFolder = NormalizeFolderPath(folderPath);
        string normalizedPath = (path ?? string.Empty).Trim().Replace('\\', '/');
        string fileName = Path.GetFileName(normalizedPath);
        return string.IsNullOrWhiteSpace(fileName)
            ? normalizedPath
            : normalizedFolder.Length == 0
                ? fileName
                : normalizedFolder + "/" + fileName;
    }

    private string SuggestNextCreatePath(AssetBrowserItemKind kind, string previousPath)
    {
        return MakeCreatePathUnique(SuggestNextCreatePathCandidate(kind, previousPath));
    }

    private string MakeCreatePathUnique(string path)
    {
        return MakePathUnique(path, CreateKind);
    }

    private string MakePathUnique(string path, AssetBrowserItemKind kind)
    {
        string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (normalized.Length == 0)
        {
            return GetDefaultCreatePath(kind);
        }

        if (!CreatePathExists(normalized))
        {
            return normalized;
        }

        string directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? string.Empty;
        string extension = Path.GetExtension(normalized);
        string name = Path.GetFileNameWithoutExtension(normalized);
        if (kind == AssetBrowserItemKind.Folder)
        {
            extension = string.Empty;
            name = Path.GetFileName(normalized);
        }

        string baseName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(GetDefaultCreatePath(kind))
            : name;
        for (int index = 1; index < 10_000; index++)
        {
            string candidateName = IncrementTrailingNumber(baseName, index);
            string candidate = string.IsNullOrWhiteSpace(directory)
                ? candidateName + extension
                : directory + "/" + candidateName + extension;
            if (!CreatePathExists(candidate))
            {
                return candidate;
            }
        }

        return normalized;
    }

    private bool CreatePathExists(string path)
    {
        for (int i = 0; i < LastAssets.Count; i++)
        {
            if (string.Equals(LastAssets[i].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        for (int i = 0; i < FolderTargets.Count; i++)
        {
            if (string.Equals(FolderTargets[i].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string SuggestNextCreatePathCandidate(AssetBrowserItemKind kind, string previousPath)
    {
        if (string.IsNullOrWhiteSpace(previousPath))
        {
            return GetDefaultCreatePath(kind);
        }

        string normalized = previousPath.Replace('\\', '/');
        string directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? string.Empty;
        string extension = Path.GetExtension(normalized);
        string name = Path.GetFileNameWithoutExtension(normalized);
        if (kind == AssetBrowserItemKind.Folder)
        {
            extension = string.Empty;
            name = Path.GetFileName(normalized);
        }

        string nextName = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(GetDefaultCreatePath(kind)) : name + "1";
        if (!string.IsNullOrWhiteSpace(name))
        {
            nextName = IncrementTrailingNumber(name, 1);
        }

        return string.IsNullOrWhiteSpace(directory)
            ? nextName + extension
            : directory + "/" + nextName + extension;
    }

    private string SuggestNextImportPath(AssetBrowserItemKind kind, string previousPath)
    {
        return MakePathUnique(SuggestNextCreatePathCandidate(kind, previousPath), kind);
    }

    private static string IncrementTrailingNumber(string value, int offset)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return offset.ToString(CultureInfo.InvariantCulture);
        }

        int digitStart = value.Length;
        while (digitStart > 0 && char.IsDigit(value[digitStart - 1]))
        {
            digitStart--;
        }

        if (digitStart == value.Length)
        {
            return value + offset.ToString(CultureInfo.InvariantCulture);
        }

        string prefix = value[..digitStart];
        string numberText = value[digitStart..];
        return int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            ? prefix + (number + offset).ToString(CultureInfo.InvariantCulture)
            : value + offset.ToString(CultureInfo.InvariantCulture);
    }

    private static string ApplyFolderToCreatePath(string folderPath, string createPath)
    {
        string normalizedFolder = (folderPath ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
        string normalizedPath = (createPath ?? string.Empty).Trim().Replace('\\', '/');
        if (normalizedFolder.Length == 0 || normalizedPath.Length == 0)
        {
            return normalizedPath;
        }

        string fileName = Path.GetFileName(normalizedPath);
        return normalizedFolder + "/" + fileName;
    }

    private static bool IsProtectedLogicalRoot(string folderPath)
    {
        return string.Equals(folderPath, "Content", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(folderPath, "ScriptSource", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetPendingDeleteFor(AssetBrowserItem item, out AssetBrowserDeleteRequest request)
    {
        if (_pendingDeleteRequest is { } pending &&
            string.Equals(pending.Path, item.Path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pending.AssetId, item.AssetId, StringComparison.OrdinalIgnoreCase) &&
            pending.Kind == item.Kind)
        {
            request = pending;
            return true;
        }

        request = default;
        return false;
    }

    private bool TryGetPendingFolderDelete(string path, out AssetBrowserFolderDeleteRequest request)
    {
        string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (_pendingFolderDeleteRequest is { } pending &&
            string.Equals(pending.Path, normalized, StringComparison.OrdinalIgnoreCase))
        {
            request = pending;
            return true;
        }

        request = default;
        return false;
    }

    private bool TryGetPendingFolderMove(string path, out AssetBrowserFolderMoveRequest request)
    {
        string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (_pendingFolderMoveRequest is { } pending &&
            string.Equals(pending.Path, normalized, StringComparison.OrdinalIgnoreCase))
        {
            request = pending;
            return true;
        }

        request = default;
        return false;
    }

    private bool TryGetPendingMoveFor(AssetBrowserItem item, out AssetBrowserMoveRequest request)
    {
        if (_pendingMoveRequest is { } pending &&
            string.Equals(pending.Path, item.Path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pending.AssetId, item.AssetId, StringComparison.OrdinalIgnoreCase) &&
            pending.Kind == item.Kind)
        {
            request = pending;
            return true;
        }

        request = default;
        return false;
    }

    private void ApplyFilter()
    {
        RebuildNavigationProjection();
        if (LastAssets.Count == 0)
        {
            FilteredAssets = [];
            return;
        }

        IEnumerable<AssetBrowserItem> query = LastAssets;
        bool searching = !string.IsNullOrWhiteSpace(_search);
        if (searching)
        {
            if (!string.IsNullOrWhiteSpace(ActiveFolderPath))
            {
                query = query.Where(item => IsAssetVisibleInFolderScope(item.Path, ActiveFolderPath));
            }

            query = query.Where(MatchesSearch);
        }
        else
        {
            query = query.Where(item => IsDirectAssetChild(item.Path, ActiveFolderPath));
        }

        if (KindFilter.HasValue)
        {
            query = query.Where(item => item.Kind == KindFilter.Value);
        }

        FilteredAssets =
        [
            .. ApplySort(query),
        ];
    }

    private bool MatchesSearch(AssetBrowserItem item)
    {
        string typeLabel = item.Descriptor?.TypeLabel ?? GetDefaultTypeLabel(item.Kind);
        string purpose = item.Descriptor?.Purpose ?? string.Empty;
        string summary = item.PreviewSummary ?? string.Empty;
        string badges = FormatBadges(GetBadges(item));
        return item.Path.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
            item.Kind.ToString().Contains(_search, StringComparison.OrdinalIgnoreCase) ||
            typeLabel.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
            purpose.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
            summary.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
            badges.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(item.AssetId) && item.AssetId.Contains(_search, StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildNavigationProjection()
    {
        VisibleFolders =
        [
            .. FolderTargets.Where(folder => IsDirectFolderChild(folder.Path, ActiveFolderPath)),
        ];

        List<AssetBrowserBreadcrumbItem> breadcrumbs = [new("工程", string.Empty)];
        if (!string.IsNullOrWhiteSpace(ActiveFolderPath))
        {
            string[] segments = ActiveFolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string path = string.Empty;
            for (int i = 0; i < segments.Length; i++)
            {
                path = path.Length == 0 ? segments[i] : path + "/" + segments[i];
                breadcrumbs.Add(new AssetBrowserBreadcrumbItem(segments[i], path));
            }
        }

        Breadcrumbs = breadcrumbs;
    }

    private void ReconcileFolderSelection(EditorSelection? selection)
    {
        bool tracksFolderSelection = selection?.FolderPath is not null;
        string requestedPath = tracksFolderSelection
            ? selection!.FolderPath!
            : ActiveFolderPath;
        string resolvedPath = FindNearestExistingFolder(requestedPath);
        bool activeChanged = !string.Equals(ActiveFolderPath, resolvedPath, StringComparison.Ordinal);
        bool selectionChanged = tracksFolderSelection &&
            !string.Equals(selection!.FolderPath, resolvedPath, StringComparison.Ordinal);

        ActiveFolderPath = resolvedPath;
        if (selectionChanged)
        {
            selection!.SelectFolder(resolvedPath);
        }

        if (activeChanged || selectionChanged)
        {
            ApplyFolderInputContext(resolvedPath);
        }
    }

    private void RebuildFolderTargets()
    {
        Dictionary<string, int> folders = new(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = LastAssets.Count,
        };
        HashSet<string> authoritativeFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            string.Empty,
        };
        if (_source is IAssetBrowserFolderDataSource folderSource)
        {
            IReadOnlyList<AssetBrowserFolderItem> explicitFolders = folderSource.ListFolders();
            for (int i = 0; i < explicitFolders.Count; i++)
            {
                string folderPath = explicitFolders[i].Path;
                folders[folderPath] = folders.TryGetValue(folderPath, out int count)
                    ? Math.Max(count, explicitFolders[i].AssetCount)
                    : explicitFolders[i].AssetCount;
                _ = authoritativeFolders.Add(folderPath);
            }
        }

        for (int i = 0; i < LastAssets.Count; i++)
        {
            string? folder = GetLogicalDirectoryName(LastAssets[i].Path);
            while (!string.IsNullOrEmpty(folder))
            {
                if (!authoritativeFolders.Contains(folder))
                {
                    folders[folder] = folders.TryGetValue(folder, out int count) ? count + 1 : 1;
                }

                folder = GetLogicalDirectoryName(folder);
            }
        }

        FolderTargets =
        [
            .. folders
                .Select(static item => new AssetBrowserFolderItem(item.Key, item.Value))
                .OrderBy(static item => item.Path.Length == 0 ? 0 : 1)
                .ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private IEnumerable<AssetBrowserItem> ApplySort(IEnumerable<AssetBrowserItem> query)
    {
        return SortMode switch
        {
            AssetBrowserSortMode.PathAscending => query.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase),
            AssetBrowserSortMode.KindThenPath => query
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase),
            AssetBrowserSortMode.LastModifiedDescending => query
                .OrderByDescending(item => item.LastModifiedUtc)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase),
            AssetBrowserSortMode.SizeDescending => query
                .OrderByDescending(item => item.SizeBytes)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase),
            _ => query,
        };
    }

    private AssetBrowserItem? FindAsset(string path)
    {
        EnsureSnapshotLoaded();

        for (int i = 0; i < LastAssets.Count; i++)
        {
            if (string.Equals(LastAssets[i].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return LastAssets[i];
            }
        }

        return null;
    }

    private IReadOnlyList<AssetBrowserItem> ReloadSnapshot()
    {
        LastAssets = _source.ListAssets();
        _snapshotLoaded = true;
        ReconcileAssetSelection(_trackedSelection);
        RebuildFolderTargets();
        ReconcileFolderSelection(_trackedSelection);
        ApplyFilter();
        return LastAssets;
    }

    private void EnsureSnapshotLoaded()
    {
        if (!_snapshotLoaded)
        {
            _ = ReloadSnapshot();
        }
    }

    private void ReconcileAssetSelection(EditorSelection? selection)
    {
        if (selection is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(selection.AssetId))
        {
            for (int i = 0; i < LastAssets.Count; i++)
            {
                AssetBrowserItem item = LastAssets[i];
                if (!string.IsNullOrWhiteSpace(item.AssetId) &&
                    string.Equals(item.AssetId, selection.AssetId, StringComparison.OrdinalIgnoreCase))
                {
                    selection.SelectAsset(item.AssetId, item.Path);
                    return;
                }
            }

            selection.ClearAsset();
            return;
        }

        if (string.IsNullOrWhiteSpace(selection.AssetPath))
        {
            return;
        }

        for (int i = 0; i < LastAssets.Count; i++)
        {
            AssetBrowserItem item = LastAssets[i];
            if (!string.Equals(item.Path, selection.AssetPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.AssetId))
            {
                selection.SelectAsset(item.Path);
            }
            else
            {
                selection.SelectAsset(item.AssetId, item.Path);
            }

            return;
        }

        selection.ClearAsset();
    }

    private static bool IsAssetSelected(EditorSelection selection, AssetBrowserItem item)
    {
        return !string.IsNullOrWhiteSpace(selection.AssetId)
            ? !string.IsNullOrWhiteSpace(item.AssetId) &&
                string.Equals(selection.AssetId, item.AssetId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(selection.AssetPath, item.Path, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryFindFolder(string path, out AssetBrowserFolderItem folder)
    {
        EnsureSnapshotLoaded();

        string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        for (int i = 0; i < FolderTargets.Count; i++)
        {
            if (string.Equals(FolderTargets[i].Path, normalized, StringComparison.OrdinalIgnoreCase))
            {
                folder = FolderTargets[i];
                return true;
            }
        }

        folder = default;
        return false;
    }

    private bool TryBuildFolderDeleteRequest(string folderPath, bool confirmed, out AssetBrowserFolderDeleteRequest request)
    {
        string normalized = (folderPath ?? string.Empty).Trim().Replace('\\', '/');
        List<string> assetIds = [];
        for (int i = 0; i < LastAssets.Count; i++)
        {
            AssetBrowserItem item = LastAssets[i];
            if (!IsAssetUnderFolder(item.Path, normalized))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.AssetId))
            {
                Status = $"文件夹包含缺少 stable asset id 的资产，不能递归删除：{item.Path}";
                request = default;
                return false;
            }

            assetIds.Add(item.AssetId);
        }

        request = new AssetBrowserFolderDeleteRequest(
            normalized,
            [.. assetIds.Order(StringComparer.OrdinalIgnoreCase)],
            confirmed);
        return true;
    }

    private static bool IsAssetUnderFolder(string assetPath, string folderPath)
    {
        string normalizedAsset = assetPath.Replace('\\', '/');
        string normalizedFolder = folderPath.Trim().Replace('\\', '/').TrimEnd('/');
        return normalizedFolder.Length > 0 &&
            normalizedAsset.StartsWith(normalizedFolder + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAssetVisibleInFolderScope(string assetPath, string folderPath)
    {
        string normalizedFolder = folderPath.Trim().Replace('\\', '/').TrimEnd('/');
        return normalizedFolder.Length == 0 || IsAssetUnderFolder(assetPath, normalizedFolder);
    }

    private static bool IsDirectAssetChild(string assetPath, string folderPath)
    {
        string normalizedAsset = assetPath.Replace('\\', '/').Trim('/');
        string normalizedFolder = NormalizeFolderPath(folderPath);
        string? parent = GetLogicalDirectoryName(normalizedAsset);
        return string.Equals(parent ?? string.Empty, normalizedFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectFolderChild(string candidatePath, string folderPath)
    {
        string candidate = NormalizeFolderPath(candidatePath);
        string parent = NormalizeFolderPath(folderPath);
        return candidate.Length > 0 &&
            !string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetLogicalDirectoryName(candidate) ?? string.Empty, parent, StringComparison.OrdinalIgnoreCase);
    }

    private AssetBrowserBadge GetBadges(AssetBrowserItem item)
    {
        AssetBrowserBadge badges = item.Descriptor?.Badges ?? AssetBrowserBadge.None;
        if (_source is IAssetBrowserContextDataSource contextSource)
        {
            badges |= contextSource.GetContextBadges(item.Path);
        }

        return badges;
    }

    private static string GetDefaultTypeLabel(AssetBrowserItemKind kind)
    {
        return kind switch
        {
            AssetBrowserItemKind.Folder => "文件夹",
            AssetBrowserItemKind.Material => "材质定义",
            AssetBrowserItemKind.Texture => "纹理",
            AssetBrowserItemKind.Audio => "音频",
            AssetBrowserItemKind.Scene => "场景",
            AssetBrowserItemKind.Prefab => "Prefab",
            AssetBrowserItemKind.Script => "C# 脚本",
            AssetBrowserItemKind.UiScreen => "UI Screen",
            AssetBrowserItemKind.Json => "JSON 配置",
            AssetBrowserItemKind.Other => "文件",
            _ => kind.ToString(),
        };
    }

    private static string FormatBadges(AssetBrowserBadge badges)
    {
        List<string> labels = [];
        if ((badges & AssetBrowserBadge.Startup) != 0)
        {
            labels.Add("启动");
        }

        if ((badges & AssetBrowserBadge.Current) != 0)
        {
            labels.Add("当前");
        }

        if ((badges & AssetBrowserBadge.Test) != 0)
        {
            labels.Add("测试");
        }

        return string.Join(" · ", labels);
    }

    private static string? GetLogicalDirectoryName(string logicalPath)
    {
        string normalized = logicalPath.Replace('\\', '/');
        int separator = normalized.LastIndexOf('/');
        return separator <= 0 ? null : normalized[..separator];
    }

    private static bool TryNormalizeTargetFolder(string targetFolderPath, out string normalized, out string diagnostic)
    {
        string candidate = (targetFolderPath ?? string.Empty).Trim().Replace('\\', '/');
        if (candidate.Length == 0 || candidate == "." || candidate == "/")
        {
            normalized = string.Empty;
            diagnostic = string.Empty;
            return true;
        }

        if (Path.IsPathRooted(candidate) || candidate.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = string.Empty;
            diagnostic = $"拖拽移动目标必须是 content 内相对文件夹：{targetFolderPath}";
            return false;
        }

        string[] parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> normalizedParts = new(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                normalized = string.Empty;
                diagnostic = $"拖拽移动目标不能越过 content 根目录：{targetFolderPath}";
                return false;
            }

            normalizedParts.Add(part);
        }

        normalized = string.Join("/", normalizedParts);
        diagnostic = string.Empty;
        return true;
    }

    private static unsafe ImTextureRef CreateTextureRef(uint handle)
    {
        return new ImTextureRef(null, (ImTextureID)(ulong)handle);
    }
}
