using Hexa.NET.ImGui;

namespace PixelEngine.Editor;

/// <summary>
/// content 资源浏览器面板。
/// </summary>
/// <param name="source">资产数据源。</param>
/// <param name="audioPreview">音频试听服务。</param>
/// <param name="instantiatePrefab">可选 prefab 实例化回调。</param>
public sealed class AssetBrowserPanel(
    IAssetBrowserDataSource source,
    IAudioPreviewService? audioPreview = null,
    Action<string>? instantiatePrefab = null) : IEditorPanel
{
    private readonly IAssetBrowserDataSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IAudioPreviewService? _audioPreview = audioPreview;
    private readonly Action<string>? _instantiatePrefab = instantiatePrefab;
    private string _search = string.Empty;

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

        string label = $"{item.DisplayName} [{item.Kind}]";
        bool selected = string.Equals(selection.AssetPath, item.Path, StringComparison.Ordinal);
        if (ImGui.Selectable(label, selected))
        {
            _ = SelectAsset(item.Path, selection);
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
                item.Kind.ToString().Contains(_search, StringComparison.OrdinalIgnoreCase)),
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
