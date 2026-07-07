using Hexa.NET.ImGui;

namespace PixelEngine.Editor;

/// <summary>
/// Project Window 脚本资产打开回调。
/// </summary>
/// <param name="assetPath">资产逻辑路径。</param>
/// <param name="diagnostic">可展示给用户的打开诊断。</param>
/// <returns>成功发起打开时返回 true。</returns>
public delegate bool ScriptAssetOpenHandler(string assetPath, out string diagnostic);

/// <summary>
/// content 资源浏览器面板。
/// </summary>
/// <param name="source">资产数据源。</param>
/// <param name="audioPreview">音频试听服务。</param>
/// <param name="instantiatePrefab">可选 prefab 实例化回调。</param>
/// <param name="openScriptAsset">可选脚本资产打开回调。</param>
/// <param name="deleteAsset">可选资产删除回调。</param>
public sealed class AssetBrowserPanel(
    IAssetBrowserDataSource source,
    IAudioPreviewService? audioPreview = null,
    Action<string>? instantiatePrefab = null,
    ScriptAssetOpenHandler? openScriptAsset = null,
    AssetBrowserDeleteHandler? deleteAsset = null) : IEditorPanel
{
    private readonly IAssetBrowserDataSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IAudioPreviewService? _audioPreview = audioPreview;
    private readonly Action<string>? _instantiatePrefab = instantiatePrefab;
    private readonly ScriptAssetOpenHandler? _openScriptAsset = openScriptAsset;
    private readonly AssetBrowserDeleteHandler? _deleteAsset = deleteAsset;
    private string _search = string.Empty;
    private AssetBrowserDeleteRequest? _pendingDeleteRequest;

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
    /// 刷新资产列表。
    /// </summary>
    /// <returns>完整资产快照。</returns>
    public IReadOnlyList<AssetBrowserItem> Refresh()
    {
        LastAssets = _source.ListAssets();
        ApplyFilter();
        return LastAssets;
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
        AssetBrowserItem? item = FindAsset(path);
        if (item is null)
        {
            return false;
        }

        selection.SelectAsset(item.Value.Path);
        Status = $"选中 {item.Value.Path}";
        return true;
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

    /// <inheritdoc />
    public void Draw(in EditorContext context)
    {
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        if (ImGui.Button("刷新"))
        {
            _ = Refresh();
        }

        ImGui.SameLine();
        _ = ImGui.InputText("搜索", ref _search, 128);
        ApplyFilter();

        IReadOnlyList<AssetBrowserItem> assets = FilteredAssets.Count == 0 && LastAssets.Count == 0
            ? Refresh()
            : FilteredAssets;
        for (int i = 0; i < assets.Count; i++)
        {
            DrawAssetRow(assets[i], context.Selection);
        }

        ImGui.TextUnformatted(Status);
        ImGui.End();
    }

    private void DrawAssetRow(AssetBrowserItem item, EditorSelection selection)
    {
        if (item.Thumbnail is AssetThumbnail thumbnail && thumbnail.TextureHandle != 0)
        {
            ImGui.Image(CreateTextureRef(thumbnail.TextureHandle), new System.Numerics.Vector2(32, 32));
            ImGui.SameLine();
        }

        string label = string.IsNullOrWhiteSpace(item.AssetId)
            ? $"{item.DisplayName} [{item.Kind}]"
            : $"{item.DisplayName} [{item.Kind}]##{item.AssetId}";
        bool selected = string.Equals(selection.AssetPath, item.Path, StringComparison.Ordinal);
        if (ImGui.Selectable(label, selected))
        {
            _ = SelectAsset(item.Path, selection);
            if (item.Kind == AssetBrowserItemKind.Script && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _ = TryOpenScriptAsset(item.Path);
            }
        }

        if (item.Kind == AssetBrowserItemKind.Audio)
        {
            ImGui.SameLine();
            if (ImGui.Button($"试听##{item.Path}"))
            {
                _ = TryPreviewAudio(item.Path);
            }
        }
        else if (item.Kind == AssetBrowserItemKind.Prefab)
        {
            ImGui.SameLine();
            if (ImGui.Button($"实例化##{item.Path}"))
            {
                _instantiatePrefab?.Invoke(item.Path);
                Status = $"实例化 {item.Path}";
            }
        }

        if (!string.IsNullOrWhiteSpace(item.PreviewSummary))
        {
            ImGui.TextUnformatted($"预览：{item.PreviewSummary}");
        }

        ImGui.SameLine();
        if (IsPendingDeleteFor(item))
        {
            if (ImGui.Button($"确认删除##{item.Path}"))
            {
                _ = TryConfirmDeleteAsset(item.Path);
            }

            ImGui.SameLine();
            if (ImGui.Button($"取消##{item.Path}"))
            {
                _pendingDeleteRequest = null;
                Status = $"已取消删除 {item.Path}";
            }
        }
        else if (ImGui.Button($"删除##{item.Path}"))
        {
            _ = TryRequestDeleteAsset(item.Path);
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

    private bool IsPendingDeleteFor(AssetBrowserItem item)
    {
        return TryGetPendingDeleteFor(item, out _);
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

    private void ApplyFilter()
    {
        if (LastAssets.Count == 0)
        {
            FilteredAssets = [];
            return;
        }

        if (string.IsNullOrWhiteSpace(_search))
        {
            FilteredAssets = LastAssets;
            return;
        }

        FilteredAssets =
        [
            .. LastAssets.Where(item =>
                item.Path.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                item.Kind.ToString().Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(item.AssetId) && item.AssetId.Contains(_search, StringComparison.OrdinalIgnoreCase))),
        ];
    }

    private AssetBrowserItem? FindAsset(string path)
    {
        if (LastAssets.Count == 0)
        {
            _ = Refresh();
        }

        for (int i = 0; i < LastAssets.Count; i++)
        {
            if (string.Equals(LastAssets[i].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return LastAssets[i];
            }
        }

        return null;
    }

    private static unsafe ImTextureRef CreateTextureRef(uint handle)
    {
        return new ImTextureRef(null, (ImTextureID)(ulong)handle);
    }
}
