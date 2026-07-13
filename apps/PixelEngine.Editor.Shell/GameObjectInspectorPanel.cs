using Hexa.NET.ImGui;
using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Scripting;
using System.Globalization;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Inspector 面板：Transform 与组件字段编辑。
/// </summary>
internal sealed class GameObjectInspectorPanel(
    EditorSceneModel scene,
    EditorUndoStack undo,
    ScriptAssemblyRegistry scripts,
    IEditorConsoleSink? console = null,
    IAssetBrowserDataSource? assetSource = null,
    PrefabAssetInstantiateHandler? instantiatePrefab = null,
    ScriptAssetOpenHandler? openScriptAsset = null,
    SceneAssetOpenHandler? openSceneAsset = null,
    IAudioPreviewService? audioPreview = null,
    IRuntimeSceneEditorDataSource? runtimeSource = null,
    Func<EditorMode>? modeProvider = null) : IEditorPanel, IDisposable
{
    private const string ReadyStatus = "就绪";
    private readonly EditorSceneModel _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly EditorUndoStack _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    private readonly ScriptAssemblyRegistry _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
    private readonly IEditorConsoleSink? _console = console;
    private readonly IAssetBrowserDataSource? _assetSource = assetSource;
    private readonly IAssetBrowserPreviewDataSource? _assetPreviewSource = assetSource as IAssetBrowserPreviewDataSource;
    private readonly IAssetBrowserThumbnailDataSource? _assetThumbnailSource = assetSource as IAssetBrowserThumbnailDataSource;
    private readonly PrefabAssetInstantiateHandler? _instantiatePrefab = instantiatePrefab;
    private readonly ScriptAssetOpenHandler? _openScriptAsset = openScriptAsset;
    private readonly SceneAssetOpenHandler? _openSceneAsset = openSceneAsset;
    private readonly IAudioPreviewService? _audioPreview = audioPreview;
    private readonly IRuntimeSceneEditorDataSource? _runtimeSource = runtimeSource;
    private readonly Func<EditorMode>? _modeProvider = modeProvider;
    private string _componentSearch = string.Empty;
    private string? _statusSelectionKey;
    private EditorMode _lastMode = EditorMode.Edit;
    private int? _transformEditStableId;
    private EditorSceneTransform? _transformEditBefore;
    private EditorGameObject? _transformEditTarget;
    private int? _nameEditStableId;
    private string _nameEditBuffer = string.Empty;
    private EditorGameObject? _nameEditTarget;
    private int _focusDelayFrames;
    private bool _focusRequested;
    private string _assetPreviewCachePath = string.Empty;
    private long _assetPreviewCacheSizeBytes = -1;
    private long _assetPreviewCacheModifiedTicks = -1;
    private AssetBrowserDetailedPreview? _assetPreviewCache;
    private AssetPreviewThumbnailLease? _assetPreviewThumbnail;
    private bool _disposed;

    public string Title => EditorDockSpace.InspectorWindowTitle;

    internal string Status { get; private set; } = ReadyStatus;

    public bool Visible
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            if (!value)
            {
                ClearAssetPreviewState();
            }
        }
    } = true;

    internal void RequestFocus()
    {
        // 先让 Scene View 用稳定 dock 尺寸完成首次 framing，再切换右侧 Inspector。
        // 在 dock 创建帧直接 SetNextWindowFocus 会让 Scene 当帧不可见并按错误尺寸 framing。
        _focusDelayFrames = 2;
        _focusRequested = true;
    }

    public void Draw(in EditorContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PrepareAssetPreviewSelection(
            !context.Selection.GameObjectStableId.HasValue &&
            !context.Selection.BodyId.HasValue &&
            string.IsNullOrWhiteSpace(context.Selection.EntityHandle) &&
            context.Selection.FolderPath is null
                ? context.Selection.AssetPath
                : null);
        PrepareFrame(context.Selection.GameObjectStableId);
        if (_focusDelayFrames > 0)
        {
            _focusDelayFrames--;
        }
        else if (_focusRequested)
        {
            ImGui.SetNextWindowFocus();
            _focusRequested = false;
        }

        if (!ImGui.Begin(Title))
        {
            ImGui.End();
            return;
        }

        if (!string.IsNullOrWhiteSpace(context.Selection.EntityHandle))
        {
            DrawRuntimeEntityInspector(context.Selection.EntityHandle);
            ImGui.End();
            return;
        }

        if (context.Selection.BodyId.HasValue)
        {
            DrawRuntimeBodyInspector(context.Selection.BodyId.Value);
            ImGui.End();
            return;
        }

        // 无选中时早退；否则绘制头部、Transform 与组件列表
        if (!context.Selection.GameObjectStableId.HasValue && context.Selection.FolderPath is not null)
        {
            DrawFolderInspector(context.Selection.FolderPath);
            ImGui.End();
            return;
        }

        if (!context.Selection.GameObjectStableId.HasValue && !string.IsNullOrWhiteSpace(context.Selection.AssetPath))
        {
            DrawAssetInspector(context.Selection.AssetPath);
            ImGui.End();
            return;
        }

        int? stableId = context.Selection.GameObjectStableId ?? _scene.SelectedStableId;
        if (!stableId.HasValue || !_scene.TryGet(stableId.Value, out EditorGameObject? gameObject))
        {
            ImGui.TextUnformatted("未选中 GameObject 或 Asset");
            ImGui.End();
            return;
        }

        DrawHeader(gameObject);
        bool transformOpen = DrawInspectorComponentHeader("Transform##gameobject-transform");
        bool resetTransform = false;
        if (ImGui.BeginPopupContextItem("gameobject-transform-context"))
        {
            resetTransform = ImGui.MenuItem("Reset");
            ImGui.EndPopup();
        }

        if (resetTransform)
        {
            _undo.Execute(_scene, new SetTransformCommand(gameObject.StableId, new EditorSceneTransform()));
        }

        if (transformOpen)
        {
            DrawTransform(gameObject);
        }

        DrawComponents(gameObject);
        if (!string.Equals(Status, ReadyStatus, StringComparison.Ordinal))
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.95f, 0.70f, 0.25f, 1f), Status);
        }

        ImGui.End();
    }

    /// <summary>
    /// 在面板可见性与绘制顺序之外收口连续 Transform 编辑。
    /// Hierarchy 先于 Inspector 绘制；若用户从 A 的 InputFloat 直接点击 B，A 的旧控件不会再被绘制，
    /// 因此不能只依赖 ImGui.IsItemDeactivatedAfterEdit 提交 Undo。
    /// </summary>
    internal void PrepareFrame(int? selectedStableId)
    {
        EditorMode mode = _modeProvider?.Invoke() ?? EditorMode.Edit;
        bool targetReplaced = _transformEditStableId.HasValue &&
            (!_scene.TryGet(_transformEditStableId.Value, out EditorGameObject? currentTarget) ||
             !ReferenceEquals(currentTarget, _transformEditTarget));
        bool nameTargetReplaced = _nameEditStableId.HasValue &&
            (!_scene.TryGet(_nameEditStableId.Value, out EditorGameObject? currentNameTarget) ||
             !ReferenceEquals(currentNameTarget, _nameEditTarget));
        UpdateRuntimeEditLifetime();
        if (_nameEditStableId.HasValue &&
            (!Visible || mode != EditorMode.Edit || selectedStableId != _nameEditStableId || nameTargetReplaced))
        {
            CommitPendingNameEdit();
        }

        if (_transformEditStableId.HasValue &&
            (!Visible || mode != EditorMode.Edit || selectedStableId != _transformEditStableId || targetReplaced))
        {
            CommitPendingTransformEdit();
        }
    }

    internal bool BeginTransformEdit(int stableId)
    {
        if (_transformEditStableId.HasValue && _transformEditStableId != stableId)
        {
            CommitPendingTransformEdit();
        }

        if (_transformEditStableId.HasValue)
        {
            return true;
        }

        if (!_scene.TryGet(stableId, out EditorGameObject? gameObject))
        {
            return false;
        }

        _transformEditStableId = stableId;
        _transformEditBefore = gameObject.Transform.Clone();
        _transformEditTarget = gameObject;
        return true;
    }

    internal bool ApplyTransformEdit(int stableId, EditorSceneTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!BeginTransformEdit(stableId) ||
            !_scene.TryGet(stableId, out EditorGameObject? gameObject) ||
            !ReferenceEquals(gameObject, _transformEditTarget))
        {
            return false;
        }

        _scene.SetTransform(stableId, transform);
        return true;
    }

    internal AssetInspectorSnapshot CaptureAssetInspector(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        if (_assetSource is null)
        {
            return new AssetInspectorSnapshot(assetPath, Found: false, "Unknown", null, 0, null, null, null, null, "资产数据源不可用");
        }

        IReadOnlyList<AssetBrowserItem> assets = _assetSource.ListAssets();
        for (int i = 0; i < assets.Count; i++)
        {
            AssetBrowserItem item = assets[i];
            if (string.Equals(item.Path, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                AssetBrowserDetailedPreview preview = ResolveAssetPreview(in item);
                return new AssetInspectorSnapshot(
                    item.Path,
                    Found: true,
                    item.Kind.ToString(),
                    item.AssetId,
                    item.SizeBytes,
                    item.PreviewSummary,
                    GetPrimaryAssetActionLabel(item.Kind),
                    item,
                    preview,
                    "就绪");
            }
        }

        return new AssetInspectorSnapshot(assetPath, Found: false, "Unknown", null, 0, null, null, null, null, $"资产不存在：{assetPath}");
    }

    internal AssetThumbnail? CaptureAssetPreviewThumbnail(string assetPath)
    {
        AssetInspectorSnapshot snapshot = CaptureAssetInspector(assetPath);
        return snapshot.Item is { } item &&
            snapshot.DetailedPreview?.ContentKind == AssetBrowserPreviewContentKind.Image
                ? ResolveAssetPreviewThumbnail(in item)
                : null;
    }

    internal FolderInspectorSnapshot CaptureFolderInspector(string folderPath)
    {
        string normalized = (folderPath ?? string.Empty).Trim().Replace('\\', '/');
        if (_assetSource is not IAssetBrowserFolderDataSource folderSource)
        {
            return new FolderInspectorSnapshot(normalized, Found: false, 0, "文件夹数据源不可用");
        }

        IReadOnlyList<AssetBrowserFolderItem> folders = folderSource.ListFolders();
        for (int i = 0; i < folders.Count; i++)
        {
            AssetBrowserFolderItem folder = folders[i];
            if (string.Equals(folder.Path, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return new FolderInspectorSnapshot(folder.Path, Found: true, folder.AssetCount, "就绪");
            }
        }

        return new FolderInspectorSnapshot(normalized, Found: false, 0, $"文件夹不存在：{normalized}");
    }

    internal bool TryInvokePrimaryAssetAction(string assetPath)
    {
        AssetInspectorSnapshot asset = CaptureAssetInspector(assetPath);
        if (!asset.Found)
        {
            RecordInspectorStatus(asset.Status, EditorConsoleSeverity.Warning, "inspector-asset-action", GetAssetSelectionKey(asset.Path));
            return false;
        }

        if (asset.Item is not { } item)
        {
            RecordInspectorStatus($"资产类型不可操作：{asset.Kind}", EditorConsoleSeverity.Warning, "inspector-asset-action", GetAssetSelectionKey(asset.Path));
            return false;
        }

        switch (item.Kind)
        {
            case AssetBrowserItemKind.Script:
                if (_openScriptAsset is null)
                {
                    RecordInspectorStatus("脚本外部编辑器不可用", EditorConsoleSeverity.Warning, "inspector-asset-action", GetAssetSelectionKey(asset.Path));
                    return false;
                }

                bool opened = _openScriptAsset(asset.Path, out string diagnostic);
                RecordInspectorStatus(
                    string.IsNullOrWhiteSpace(diagnostic)
                        ? opened ? $"打开脚本 {asset.Path}" : $"脚本外部编辑器打开失败：{asset.Path}"
                        : diagnostic,
                    opened ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Warning,
                    "inspector-asset-action",
                    GetAssetSelectionKey(asset.Path),
                    writeConsole: false);
                return opened;

            case AssetBrowserItemKind.Prefab:
                if (_instantiatePrefab is null)
                {
                    RecordInspectorStatus("Prefab 实例化服务不可用", EditorConsoleSeverity.Warning, "inspector-asset-action", GetAssetSelectionKey(asset.Path));
                    return false;
                }

                bool instantiated = _instantiatePrefab(asset.Path, out string prefabDiagnostic);
                RecordInspectorStatus(
                    string.IsNullOrWhiteSpace(prefabDiagnostic)
                        ? instantiated ? $"实例化 {asset.Path}" : $"Prefab 实例化失败：{asset.Path}"
                        : prefabDiagnostic,
                    instantiated ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Warning,
                    "inspector-asset-action",
                    GetAssetSelectionKey(asset.Path),
                    writeConsole: false);
                return instantiated;

            case AssetBrowserItemKind.Scene:
                if (_openSceneAsset is null)
                {
                    RecordInspectorStatus("场景打开服务不可用", EditorConsoleSeverity.Warning, "inspector-asset-action", GetAssetSelectionKey(asset.Path));
                    return false;
                }

                bool sceneOpened = _openSceneAsset(asset.Path, out string sceneDiagnostic);
                RecordInspectorStatus(
                    string.IsNullOrWhiteSpace(sceneDiagnostic)
                        ? sceneOpened ? $"打开场景 {asset.Path}" : $"场景打开失败：{asset.Path}"
                        : sceneDiagnostic,
                    sceneOpened ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Warning,
                    "inspector-asset-action",
                    GetAssetSelectionKey(asset.Path));
                return sceneOpened;

            case AssetBrowserItemKind.Material:
            case AssetBrowserItemKind.Texture:
            case AssetBrowserItemKind.Audio:
            case AssetBrowserItemKind.UiScreen:
            case AssetBrowserItemKind.Json:
            case AssetBrowserItemKind.Folder:
            case AssetBrowserItemKind.Other:
            default:
                RecordInspectorStatus($"当前资产没有 Inspector 主操作：{asset.Path}", EditorConsoleSeverity.Info, "inspector-asset-action", GetAssetSelectionKey(asset.Path));
                return false;
        }
    }

    private void DrawAssetInspector(string assetPath)
    {
        AssetInspectorSnapshot asset = CaptureAssetInspector(assetPath);
        ImGui.SeparatorText("Asset");
        if (!asset.Found || asset.Item is not { } item || asset.DetailedPreview is not { } preview)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.55f, 0.35f, 1f), asset.Status);
            return;
        }

        ImGui.TextUnformatted(preview.Title);
        ImGui.TextDisabled(asset.Kind);
        if (!string.IsNullOrWhiteSpace(asset.PrimaryActionLabel))
        {
            ImGui.Spacing();
            if (ImGui.Button($"{asset.PrimaryActionLabel}##asset-primary-action", new Vector2(-1f, 0f)))
            {
                _ = TryInvokePrimaryAssetAction(asset.Path);
            }
        }

        if (ImGui.BeginTable(
            "asset-inspector-properties",
            2,
            ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, ResolveInspectorLabelWidth(ImGui.GetContentRegionAvail().X));
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            DrawReadOnlyProperty("Path", asset.Path);
            DrawReadOnlyProperty("Type", asset.Kind);
            DrawReadOnlyProperty("Stable ID", asset.AssetId ?? "none");
            DrawReadOnlyProperty("Size", FormatAssetSize(asset.SizeBytes));
            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Preview");
        DrawAssetPreview(in item, preview);

        string selectionStatus = GetSelectionStatus(GetAssetSelectionKey(asset.Path), asset.Status);
        if (!string.Equals(selectionStatus, ReadyStatus, StringComparison.Ordinal))
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.95f, 0.70f, 0.25f, 1f), selectionStatus);
        }
    }

    private void DrawAssetPreview(in AssetBrowserItem item, AssetBrowserDetailedPreview preview)
    {
        ImGui.TextWrapped(preview.Summary);
        if (preview.Properties.Count != 0 && ImGui.BeginTable(
            "asset-preview-properties",
            2,
            ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, ResolveInspectorLabelWidth(ImGui.GetContentRegionAvail().X));
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            for (int i = 0; i < preview.Properties.Count; i++)
            {
                AssetBrowserPreviewProperty property = preview.Properties[i];
                DrawReadOnlyProperty(property.Label, property.Value);
            }

            ImGui.EndTable();
        }

        switch (preview.ContentKind)
        {
            case AssetBrowserPreviewContentKind.Image:
                DrawAssetImagePreview(in item);
                break;

            case AssetBrowserPreviewContentKind.Audio:
                ImGui.Spacing();
                if (ImGui.Button("▶ 试听##inspector-audio-preview", new Vector2(-1f, 0f)))
                {
                    _ = TryPreviewAudioAsset(item.Path);
                }

                break;

            case AssetBrowserPreviewContentKind.Text:
                if (!string.IsNullOrWhiteSpace(preview.TextContent))
                {
                    ImGui.Spacing();
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.075f, 0.08f, 0.09f, 1f));
                    _ = ImGui.BeginChild(
                        "inspector-asset-text-preview",
                        new Vector2(0f, ResolveTextPreviewHeight(ImGui.GetContentRegionAvail().Y)),
                        ImGuiChildFlags.Borders,
                        ImGuiWindowFlags.HorizontalScrollbar);
                    ImGui.TextUnformatted(preview.TextContent);
                    ImGui.EndChild();
                    ImGui.PopStyleColor();
                }

                break;

            case AssetBrowserPreviewContentKind.Summary:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(preview), preview.ContentKind, "未知 Inspector 资产预览类型。");
        }

        if (!string.IsNullOrWhiteSpace(preview.Diagnostic))
        {
            ImGui.Spacing();
            ImGui.TextDisabled(preview.Diagnostic);
        }
    }

    private void DrawAssetImagePreview(in AssetBrowserItem item)
    {
        AssetThumbnail? thumbnail = ResolveAssetPreviewThumbnail(in item);
        if (thumbnail is not { } image)
        {
            ImGui.TextDisabled("纹理缩略图不可用");
            return;
        }

        float availableWidth = MathF.Max(32f, ImGui.GetContentRegionAvail().X);
        float maximumWidth = MathF.Min(availableWidth, 256f);
        float maximumHeight = 220f;
        float sourceWidth = Math.Max(1, image.Width);
        float sourceHeight = Math.Max(1, image.Height);
        float scale = MathF.Min(maximumWidth / sourceWidth, maximumHeight / sourceHeight);
        Vector2 imageSize = new(sourceWidth * scale, sourceHeight * scale);
        float cursorX = ImGui.GetCursorPosX() + MathF.Max(0f, (availableWidth - imageSize.X) * 0.5f);
        ImGui.SetCursorPosX(cursorX);
        ImGui.Image(CreateTextureRef(image.TextureHandle), imageSize, new Vector2(0f, 1f), new Vector2(1f, 0f));
    }

    private static void DrawReadOnlyProperty(string label, string value)
    {
        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        ImGui.TextDisabled(label);
        _ = ImGui.TableSetColumnIndex(1);
        ImGui.TextWrapped(value);
    }

    internal static float ResolveInspectorLabelWidth(float availableWidth)
    {
        float width = float.IsFinite(availableWidth) ? MathF.Max(1f, availableWidth) : 1f;
        return Math.Clamp(width * 0.36f, 72f, 128f);
    }

    internal static float ResolveTextPreviewHeight(float availableHeight)
    {
        float height = float.IsFinite(availableHeight) ? MathF.Max(1f, availableHeight) : 1f;
        return Math.Clamp(height * 0.45f, 96f, 260f);
    }

    private static string FormatAssetSize(long bytes)
    {
        return bytes < 1024
            ? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
            : $"{(bytes / 1024d).ToString("0.##", CultureInfo.InvariantCulture)} KB";
    }

    private void DrawFolderInspector(string folderPath)
    {
        FolderInspectorSnapshot folder = CaptureFolderInspector(folderPath);
        ImGui.SeparatorText("Folder");
        ImGui.TextUnformatted($"Path: {(string.IsNullOrEmpty(folder.Path) ? "content/" : folder.Path + "/")}");
        ImGui.TextUnformatted("Type: Folder");
        ImGui.TextUnformatted($"Assets: {folder.AssetCount}");
        ImGui.SeparatorText("Inspector 状态");
        ImGui.TextUnformatted(GetSelectionStatus(GetFolderSelectionKey(folder.Path), folder.Status));
    }

    private static string? GetPrimaryAssetActionLabel(AssetBrowserItemKind kind)
    {
        return kind switch
        {
            AssetBrowserItemKind.Script => "Open Script",
            AssetBrowserItemKind.Prefab => "Instantiate",
            AssetBrowserItemKind.Scene => "Open Scene",
            AssetBrowserItemKind.Material or
            AssetBrowserItemKind.Texture or
            AssetBrowserItemKind.Audio or
            AssetBrowserItemKind.UiScreen or
            AssetBrowserItemKind.Json or
            AssetBrowserItemKind.Folder or
            AssetBrowserItemKind.Other => null,
            _ => null,
        };
    }

    private void RecordInspectorStatus(
        string status,
        EditorConsoleSeverity severity,
        string source,
        string selectionKey,
        bool writeConsole = true)
    {
        Status = string.IsNullOrWhiteSpace(status) ? ReadyStatus : status;
        _statusSelectionKey = selectionKey;
        if (writeConsole)
        {
            _console?.Add(new EditorConsoleEntry(
                DateTimeOffset.UtcNow,
                EditorConsoleCategory.Asset,
                severity,
                source,
                Status));
        }
    }

    private string GetSelectionStatus(string selectionKey, string fallback)
    {
        return Status != ReadyStatus && string.Equals(_statusSelectionKey, selectionKey, StringComparison.Ordinal)
            ? Status
            : fallback;
    }

    private static string GetAssetSelectionKey(string assetPath)
    {
        return "asset:" + assetPath;
    }

    private static string GetFolderSelectionKey(string folderPath)
    {
        return "folder:" + folderPath;
    }

    private void PrepareAssetPreviewSelection(string? assetPath)
    {
        if (!string.IsNullOrWhiteSpace(assetPath) &&
            (string.Equals(_assetPreviewCachePath, assetPath, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(_assetPreviewThumbnail?.Path, assetPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ClearAssetPreviewState();
    }

    private AssetBrowserDetailedPreview ResolveAssetPreview(in AssetBrowserItem item)
    {
        long modifiedTicks = item.LastModifiedUtc.UtcTicks;
        if (_assetPreviewCache is not null &&
            string.Equals(_assetPreviewCachePath, item.Path, StringComparison.OrdinalIgnoreCase) &&
            _assetPreviewCacheSizeBytes == item.SizeBytes &&
            _assetPreviewCacheModifiedTicks == modifiedTicks)
        {
            return _assetPreviewCache;
        }

        ReleaseAssetPreviewThumbnail();
        AssetBrowserDetailedPreview preview = _assetPreviewSource is not null &&
            _assetPreviewSource.TryGetPreview(item.Path, out AssetBrowserDetailedPreview detailed)
                ? detailed
                : BuildFallbackAssetPreview(in item);
        _assetPreviewCachePath = item.Path;
        _assetPreviewCacheSizeBytes = item.SizeBytes;
        _assetPreviewCacheModifiedTicks = modifiedTicks;
        _assetPreviewCache = preview;
        return preview;
    }

    private static AssetBrowserDetailedPreview BuildFallbackAssetPreview(in AssetBrowserItem item)
    {
        AssetBrowserPreviewContentKind contentKind = item.Kind switch
        {
            AssetBrowserItemKind.Texture => AssetBrowserPreviewContentKind.Image,
            AssetBrowserItemKind.Audio => AssetBrowserPreviewContentKind.Audio,
            AssetBrowserItemKind.Material or
            AssetBrowserItemKind.Scene or
            AssetBrowserItemKind.Prefab or
            AssetBrowserItemKind.Script or
            AssetBrowserItemKind.UiScreen or
            AssetBrowserItemKind.Json => AssetBrowserPreviewContentKind.Text,
            AssetBrowserItemKind.Folder or AssetBrowserItemKind.Other => AssetBrowserPreviewContentKind.Summary,
            _ => throw new ArgumentOutOfRangeException(nameof(item), item.Kind, "未知 Inspector 资产类型。"),
        };
        AssetBrowserPreviewProperty[] properties =
        [
            new("类型", item.Descriptor?.TypeLabel ?? item.Kind.ToString()),
            new("路径", item.Path),
            new("大小", FormatAssetSize(item.SizeBytes)),
        ];
        return new AssetBrowserDetailedPreview(
            item.DisplayName,
            contentKind,
            item.PreviewSummary ?? item.Descriptor?.Purpose ?? "暂无摘要",
            properties);
    }

    private AssetThumbnail? ResolveAssetPreviewThumbnail(in AssetBrowserItem item)
    {
        if (item.Thumbnail is { } snapshotThumbnail)
        {
            ReleaseAssetPreviewThumbnail();
            return snapshotThumbnail;
        }

        if (item.Kind != AssetBrowserItemKind.Texture || _assetThumbnailSource is null)
        {
            ReleaseAssetPreviewThumbnail();
            return null;
        }

        long modifiedTicks = item.LastModifiedUtc.UtcTicks;
        if (_assetPreviewThumbnail is { } existing &&
            string.Equals(existing.Path, item.Path, StringComparison.OrdinalIgnoreCase) &&
            existing.SizeBytes == item.SizeBytes &&
            existing.LastModifiedTicks == modifiedTicks)
        {
            return existing.Thumbnail;
        }

        ReleaseAssetPreviewThumbnail();
        if (!_assetThumbnailSource.TryAcquireThumbnail(item.Path, out AssetThumbnail thumbnail))
        {
            return null;
        }

        _assetPreviewThumbnail = new AssetPreviewThumbnailLease(
            item.Path,
            item.SizeBytes,
            modifiedTicks,
            thumbnail);
        return thumbnail;
    }

    private bool TryPreviewAudioAsset(string assetPath)
    {
        if (_audioPreview is null)
        {
            RecordInspectorStatus(
                "音频试听不可用",
                EditorConsoleSeverity.Warning,
                "inspector-audio-preview",
                GetAssetSelectionKey(assetPath));
            return false;
        }

        bool played = _audioPreview.TryPlayPreview(assetPath);
        RecordInspectorStatus(
            played ? $"试听 {assetPath}" : $"音频试听失败：{assetPath}",
            played ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Warning,
            "inspector-audio-preview",
            GetAssetSelectionKey(assetPath));
        return played;
    }

    private void ClearAssetPreviewState()
    {
        _assetPreviewCachePath = string.Empty;
        _assetPreviewCacheSizeBytes = -1;
        _assetPreviewCacheModifiedTicks = -1;
        _assetPreviewCache = null;
        ReleaseAssetPreviewThumbnail();
    }

    private void ReleaseAssetPreviewThumbnail()
    {
        if (_assetPreviewThumbnail is not { } thumbnail)
        {
            return;
        }

        _assetThumbnailSource!.ReleaseThumbnail(thumbnail.Path, thumbnail.Thumbnail.TextureHandle);
        _assetPreviewThumbnail = null;
    }

    private static unsafe ImTextureRef CreateTextureRef(uint handle)
    {
        return new ImTextureRef(null, (ImTextureID)(ulong)handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ClearAssetPreviewState();
        _disposed = true;
    }

    private void UpdateRuntimeEditLifetime()
    {
        if (_modeProvider is null || _runtimeSource is null)
        {
            return;
        }

        EditorMode mode = _modeProvider();
        if (_lastMode is EditorMode.Play or EditorMode.Paused &&
            mode == EditorMode.Edit)
        {
            _runtimeSource.RestoreTemporaryEdits();
        }

        _lastMode = mode;
    }

    private void DrawRuntimeEntityInspector(string handle)
    {
        if (_runtimeSource is null || !_runtimeSource.TryGetEntity(handle, out ScriptEntityInspection entity))
        {
            ImGui.TextUnformatted("Runtime entity is no longer available");
            return;
        }

        ImGui.TextColored(new Vector4(0.45f, 0.72f, 1f, 1f), "Play Mode · changes are temporary");
        ImGui.TextUnformatted($"{entity.Handle} · Entity {entity.EntityId}");
        if (entity.Transform is not null)
        {
            ImGui.SeparatorText("Transform (Runtime)");
            DrawRuntimeTransform(entity);
        }

        ImGui.SeparatorText("Components (Runtime)");
        for (int i = 0; i < entity.Components.Length; i++)
        {
            ScriptComponentInspection component = entity.Components[i];
            if (!ImGui.CollapsingHeader(
                $"{component.TypeName}##runtime_component_{entity.EntityId}_{i}",
                ImGuiTreeNodeFlags.DefaultOpen))
            {
                continue;
            }

            ImGui.TextUnformatted(component.Faulted
                ? "Faulted"
                : component.Enabled ? "Enabled" : "Disabled");
            ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(component.Behaviour);
            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                DrawRuntimeField(entity.Handle, i, fields[fieldIndex]);
            }
        }
    }

    private void DrawRuntimeTransform(ScriptEntityInspection entity)
    {
        Transform transform = entity.Transform!;
        float x = transform.X;
        float y = transform.Y;
        float rotation = transform.RotationRadians;
        float scaleX = transform.ScaleX;
        float scaleY = transform.ScaleY;
        bool changed = ImGui.InputFloat("X##runtime_transform", ref x);
        changed |= ImGui.InputFloat("Y##runtime_transform", ref y);
        changed |= ImGui.InputFloat("Rotation##runtime_transform", ref rotation);
        changed |= ImGui.InputFloat("Scale X##runtime_transform", ref scaleX);
        changed |= ImGui.InputFloat("Scale Y##runtime_transform", ref scaleY);
        if (changed)
        {
            _ = _runtimeSource!.TrySetEntityTransform(entity.Handle, x, y, rotation, scaleX, scaleY);
        }
    }

    private void DrawRuntimeField(string handle, int componentIndex, ScriptFieldDescriptor field)
    {
        string label = $"{field.Name}##runtime_{handle}_{componentIndex}_{field.Name}";
        if (!field.CanWrite || field.Kind == ScriptFieldKind.Unsupported)
        {
            ImGui.TextUnformatted($"{field.Name}: {field.Value}");
            return;
        }

        switch (field.Kind)
        {
            case ScriptFieldKind.Boolean:
                {
                    bool value = field.Value is bool current && current;
                    if (ImGui.Checkbox(label, ref value))
                    {
                        _ = _runtimeSource!.TrySetBehaviourField(handle, componentIndex, field.Name, value);
                    }

                    break;
                }
            case ScriptFieldKind.Number:
                {
                    float value = field.Value is IConvertible convertible
                        ? convertible.ToSingle(System.Globalization.CultureInfo.InvariantCulture)
                        : 0f;
                    if (ImGui.InputFloat(label, ref value) && TryConvertRuntimeNumber(value, field.FieldType, out object? converted))
                    {
                        _ = _runtimeSource!.TrySetBehaviourField(handle, componentIndex, field.Name, converted);
                    }

                    break;
                }
            case ScriptFieldKind.String:
                {
                    string value = field.Value?.ToString() ?? string.Empty;
                    if (ImGui.InputText(label, ref value, 256))
                    {
                        _ = _runtimeSource!.TrySetBehaviourField(handle, componentIndex, field.Name, value);
                    }

                    break;
                }
            case ScriptFieldKind.Enum:
                {
                    Type enumType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                    string[] names = Enum.GetNames(enumType);
                    int index = Math.Max(0, Array.IndexOf(names, field.Value?.ToString()));
                    if (ImGui.Combo(label, ref index, names, names.Length) && (uint)index < (uint)names.Length)
                    {
                        object value = Enum.Parse(enumType, names[index]);
                        _ = _runtimeSource!.TrySetBehaviourField(handle, componentIndex, field.Name, value);
                    }

                    break;
                }
            case ScriptFieldKind.Vector:
            case ScriptFieldKind.Material:
            case ScriptFieldKind.AssetReference:
            case ScriptFieldKind.Unsupported:
            default:
                ImGui.TextUnformatted($"{field.Name}: {field.Value}");
                break;
        }
    }

    private void DrawRuntimeBodyInspector(int bodyKey)
    {
        if (_runtimeSource is null || !_runtimeSource.TryGetBody(bodyKey, out RigidBodySnapshot body))
        {
            ImGui.TextUnformatted("Runtime body is no longer available");
            return;
        }

        ImGui.TextUnformatted($"Body {body.BodyKey}");
        ImGui.SeparatorText("Transform (Runtime, read-only)");
        ImGui.TextUnformatted($"Position: {body.Transform.Position.X:0.###}, {body.Transform.Position.Y:0.###}");
        ImGui.TextUnformatted($"Rotation: sin={body.Transform.Sin:0.###}, cos={body.Transform.Cos:0.###}");
        ImGui.TextUnformatted($"Linear velocity: {body.LinearVelocityPixelsPerSecond.X:0.###}, {body.LinearVelocityPixelsPerSecond.Y:0.###}");
        ImGui.TextUnformatted($"Angular velocity: {body.AngularVelocityRadiansPerSecond:0.###}");
        ImGui.TextUnformatted($"Mask: {body.Mask.Width}×{body.Mask.Height} · {body.Mask.SolidPixelCount} pixels");
        ImGui.TextWrapped("Rigid body editing requires a Physics phase-safe command and is intentionally read-only here.");
    }

    private static bool TryConvertRuntimeNumber(float value, Type destinationType, out object? converted)
    {
        Type target = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
        try
        {
            converted = target == typeof(float)
                ? value
                : Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            converted = null;
            return false;
        }
    }

    private void DrawHeader(EditorGameObject gameObject)
    {
        bool enabled = gameObject.Enabled;
        if (ImGui.Checkbox("##gameobject-active", ref enabled) && enabled != gameObject.Enabled)
        {
            _undo.Execute(_scene, new SetGameObjectEnabledCommand(gameObject.StableId, enabled));
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(enabled ? "GameObject active" : "GameObject inactive");
        }

        if (!_nameEditStableId.HasValue)
        {
            _nameEditBuffer = gameObject.Name;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        bool submitted = ImGui.InputText(
            "##gameobject-name",
            ref _nameEditBuffer,
            128,
            ImGuiInputTextFlags.EnterReturnsTrue);
        if (ImGui.IsItemActivated())
        {
            _nameEditStableId = gameObject.StableId;
            _nameEditTarget = gameObject;
        }

        if (submitted || ImGui.IsItemDeactivatedAfterEdit())
        {
            CommitPendingNameEdit();
        }

        ImGui.TextDisabled($"2D GameObject  ·  ID {gameObject.StableId}");
        if (gameObject.PrefabLink?.AssetPath is { Length: > 0 } prefab)
        {
            ImGui.TextColored(new Vector4(0.45f, 0.72f, 1f, 1f), $"Prefab  ·  {prefab}");
            ImGui.SameLine();
            ImGui.TextDisabled($"{gameObject.PrefabLink.Overrides.Count} overrides");
            if (gameObject.PrefabLink.Overrides.Count != 0 && ImGui.Button("Revert Overrides"))
            {
                _undo.Execute(_scene, new RevertPrefabOverridesCommand(gameObject.StableId));
            }
        }

        ImGui.Separator();
    }

    private void DrawTransform(EditorGameObject gameObject)
    {
        EditorSceneTransform transform = gameObject.Transform.Clone();
        TransformFieldLayout layout = ResolveTransformFieldLayout(ImGui.GetContentRegionAvail().X);
        bool inlineAxes = layout == TransformFieldLayout.InlineAxes;
        string tableId = inlineAxes
            ? "gameobject-transform-fields-inline"
            : "gameobject-transform-fields-stacked";
        int columnCount = inlineAxes ? 5 : 3;
        if (!ImGui.BeginTable(tableId, columnCount, ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 68f);
        ImGui.TableSetupColumn("AxisA", ImGuiTableColumnFlags.WidthFixed, 12f);
        ImGui.TableSetupColumn("ValueA", ImGuiTableColumnFlags.WidthStretch);
        if (inlineAxes)
        {
            ImGui.TableSetupColumn("AxisB", ImGuiTableColumnFlags.WidthFixed, 12f);
            ImGui.TableSetupColumn("ValueB", ImGuiTableColumnFlags.WidthStretch);
        }

        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted("Position");
        _ = ImGui.TableSetColumnIndex(1);
        ImGui.TextDisabled("X");
        _ = ImGui.TableSetColumnIndex(2);
        float x = transform.X;
        float y = transform.Y;
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.DragFloat("##position-x", ref x, 0.25f);
        if (changed)
        {
            transform.X = x;
        }
        HandleTransformInput(gameObject, transform, changed);
        DrawTransformDragTooltip();

        if (inlineAxes)
        {
            _ = ImGui.TableSetColumnIndex(3);
        }
        else
        {
            ImGui.TableNextRow();
            _ = ImGui.TableSetColumnIndex(1);
        }

        ImGui.TextDisabled("Y");
        _ = ImGui.TableSetColumnIndex(inlineAxes ? 4 : 2);
        ImGui.SetNextItemWidth(-1f);
        changed = ImGui.DragFloat("##position-y", ref y, 0.25f);
        if (changed)
        {
            transform.Y = y;
        }
        HandleTransformInput(gameObject, transform, changed);
        DrawTransformDragTooltip();

        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted("Rotation");
        _ = ImGui.TableSetColumnIndex(1);
        ImGui.TextDisabled("Z");
        _ = ImGui.TableSetColumnIndex(2);
        float rotation = RadiansToDegrees(transform.RotationRadians);
        ImGui.SetNextItemWidth(-1f);
        changed = ImGui.DragFloat("##rotation-z", ref rotation, 0.5f);
        if (changed)
        {
            transform.RotationRadians = DegreesToRadians(rotation);
        }
        HandleTransformInput(gameObject, transform, changed);
        DrawTransformDragTooltip();

        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted("Scale");
        _ = ImGui.TableSetColumnIndex(1);
        ImGui.TextDisabled("X");
        _ = ImGui.TableSetColumnIndex(2);
        float scaleX = transform.ScaleX;
        float scaleY = transform.ScaleY;
        ImGui.SetNextItemWidth(-1f);
        changed = ImGui.DragFloat("##scale-x", ref scaleX, 0.01f);
        if (changed)
        {
            transform.ScaleX = scaleX;
        }
        HandleTransformInput(gameObject, transform, changed);
        DrawTransformDragTooltip();

        if (inlineAxes)
        {
            _ = ImGui.TableSetColumnIndex(3);
        }
        else
        {
            ImGui.TableNextRow();
            _ = ImGui.TableSetColumnIndex(1);
        }

        ImGui.TextDisabled("Y");
        _ = ImGui.TableSetColumnIndex(inlineAxes ? 4 : 2);
        ImGui.SetNextItemWidth(-1f);
        changed = ImGui.DragFloat("##scale-y", ref scaleY, 0.01f);
        if (changed)
        {
            transform.ScaleY = scaleY;
        }
        HandleTransformInput(gameObject, transform, changed);
        DrawTransformDragTooltip();
        ImGui.EndTable();
    }

    internal static TransformFieldLayout ResolveTransformFieldLayout(float availableWidth)
    {
        const float InlineAxesMinimumWidth = 300f;
        return float.IsFinite(availableWidth) && availableWidth >= InlineAxesMinimumWidth
            ? TransformFieldLayout.InlineAxes
            : TransformFieldLayout.StackedAxes;
    }

    private static void DrawTransformDragTooltip()
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("左右拖动快速修改；Ctrl+单击后可精确输入。");
        }
    }

    internal static float RadiansToDegrees(float radians)
    {
        return radians * (180f / MathF.PI);
    }

    internal static float DegreesToRadians(float degrees)
    {
        return degrees * (MathF.PI / 180f);
    }

    private void CommitPendingNameEdit()
    {
        if (!_nameEditStableId.HasValue)
        {
            return;
        }

        int stableId = _nameEditStableId.Value;
        EditorGameObject? expectedTarget = _nameEditTarget;
        string name = _nameEditBuffer.Trim();
        _nameEditStableId = null;
        _nameEditTarget = null;
        bool found = _scene.TryGet(stableId, out EditorGameObject? gameObject);
        if (name.Length == 0 ||
            !found ||
            !ReferenceEquals(gameObject, expectedTarget) ||
            string.Equals(gameObject.Name, name, StringComparison.Ordinal))
        {
            if (gameObject is not null && ReferenceEquals(gameObject, expectedTarget))
            {
                _nameEditBuffer = gameObject.Name;
            }

            return;
        }

        _undo.Execute(_scene, new RenameGameObjectCommand(stableId, name));
        _nameEditBuffer = name;
    }

    private void HandleTransformInput(EditorGameObject gameObject, EditorSceneTransform transform, bool changed)
    {
        if (ImGui.IsItemActivated())
        {
            _ = BeginTransformEdit(gameObject.StableId);
        }

        if (changed)
        {
            _ = ApplyTransformEdit(gameObject.StableId, transform);
        }

        if (!ImGui.IsItemDeactivatedAfterEdit() ||
            _transformEditStableId != gameObject.StableId ||
            _transformEditBefore is null)
        {
            return;
        }

        CommitPendingTransformEdit();
    }

    private void CommitPendingTransformEdit()
    {
        if (!_transformEditStableId.HasValue || _transformEditBefore is null)
        {
            return;
        }

        int stableId = _transformEditStableId.Value;
        EditorSceneTransform before = _transformEditBefore;
        EditorGameObject? expectedTarget = _transformEditTarget;
        _transformEditStableId = null;
        _transformEditBefore = null;
        _transformEditTarget = null;
        if (!_scene.TryGet(stableId, out EditorGameObject? gameObject) ||
            !ReferenceEquals(gameObject, expectedTarget))
        {
            return;
        }

        EditorSceneTransform after = gameObject.Transform.Clone();
        if (!TransformEquals(before, after))
        {
            _undo.Execute(_scene, new SetTransformCommand(stableId, before, after));
        }
    }

    private static bool TransformEquals(EditorSceneTransform left, EditorSceneTransform right)
    {
        const float Epsilon = 0.0001f;
        return MathF.Abs(left.X - right.X) <= Epsilon &&
            MathF.Abs(left.Y - right.Y) <= Epsilon &&
            MathF.Abs(left.RotationRadians - right.RotationRadians) <= Epsilon &&
            MathF.Abs(left.ScaleX - right.ScaleX) <= Epsilon &&
            MathF.Abs(left.ScaleY - right.ScaleY) <= Epsilon;
    }

    private void DrawComponents(EditorGameObject gameObject)
    {
        // 遍历已有组件并提供 Unity 式 Add Component 搜索弹层。
        for (int i = 0; i < gameObject.Components.Count; i++)
        {
            DrawComponent(gameObject, i);
        }

        ImGui.Spacing();
        float available = ImGui.GetContentRegionAvail().X;
        float addWidth = MathF.Min(220f, available);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (available - addWidth) * 0.5f));
        if (ImGui.Button("Add Component", new Vector2(addWidth, 0f)))
        {
            _componentSearch = string.Empty;
            ImGui.OpenPopup("add-component-popup");
        }

        if (ImGui.BeginPopup("add-component-popup"))
        {
            ImGui.SetNextItemWidth(280f);
            _ = ImGui.InputTextWithHint("##component-search", "Search components", ref _componentSearch, 128);
            ImGui.Separator();
            Type[] behaviours = GetBehaviourTypes(_componentSearch);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Type behaviour = behaviours[i];
                string label = behaviour.FullName ?? behaviour.Name;
                if (ImGui.Selectable($"{GetComponentDisplayName(label)}##add-component-{label}"))
                {
                    _undo.Execute(_scene, new AddComponentCommand(gameObject.StableId, new EditorComponentModel(label)));
                    _componentSearch = string.Empty;
                    ImGui.CloseCurrentPopup();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(label);
                }
            }

            if (behaviours.Length == 0)
            {
                ImGui.TextDisabled("No matching Behaviour");
            }

            ImGui.EndPopup();
        }
    }

    private void DrawComponent(EditorGameObject gameObject, int componentIndex)
    {
        EditorComponentModel component = gameObject.Components[componentIndex];
        string displayName = GetComponentDisplayName(component.TypeName);
        bool open = DrawInspectorComponentHeader($"##component_{componentIndex}");
        Vector2 headerMin = ImGui.GetItemRectMin();
        Vector2 headerMax = ImGui.GetItemRectMax();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{component.TypeName}\nRight-click for component actions");
        }

        int moveTarget = -1;
        bool remove = false;
        if (ImGui.BeginPopupContextItem($"component_context_{componentIndex}"))
        {
            if (ImGui.MenuItem("Move Up", string.Empty, selected: false, enabled: componentIndex > 0))
            {
                moveTarget = componentIndex - 1;
            }

            if (ImGui.MenuItem(
                "Move Down",
                string.Empty,
                selected: false,
                enabled: componentIndex < gameObject.Components.Count - 1))
            {
                moveTarget = componentIndex + 1;
            }

            ImGui.Separator();
            remove = ImGui.MenuItem("Remove Component");
            ImGui.EndPopup();
        }

        Vector2 cursorAfterHeader = ImGui.GetCursorScreenPos();
        bool componentEnabled = IsComponentEnabled(component);
        ComponentHeaderLayout headerLayout = ResolveComponentHeaderLayout(
            headerMin,
            headerMax,
            ImGui.GetFrameHeight(),
            ImGui.GetStyle().ItemInnerSpacing.X,
            ImGui.GetTextLineHeight());
        ImGui.SetCursorScreenPos(headerLayout.CheckboxPosition);
        if (ImGui.Checkbox($"##component-enabled-{componentIndex}", ref componentEnabled))
        {
            _undo.Execute(
                _scene,
                new SetComponentFieldCommand(
                    gameObject.StableId,
                    componentIndex,
                    nameof(Behaviour.Enabled),
                    componentEnabled.ToString()));
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(componentEnabled ? "Component enabled" : "Component disabled");
        }

        ImGui.GetWindowDrawList().AddText(
            headerLayout.LabelPosition,
            componentEnabled ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(ImGuiCol.TextDisabled),
            displayName);

        ImGui.SetCursorScreenPos(cursorAfterHeader);

        if (remove)
        {
            _undo.Execute(_scene, new RemoveComponentCommand(gameObject.StableId, componentIndex));
            return;
        }

        if (moveTarget >= 0)
        {
            _undo.Execute(_scene, new MoveComponentCommand(gameObject.StableId, componentIndex, moveTarget));
            return;
        }

        if (!open)
        {
            return;
        }

        if (!TryCreateBehaviour(component.TypeName, out Behaviour? behaviour))
        {
            ImGui.TextUnformatted("Behaviour type unavailable");
            return;
        }

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);
        if (!ImGui.BeginTable(
            $"component-fields-{componentIndex}",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX))
        {
            return;
        }

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 116f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        for (int i = 0; i < fields.Length; i++)
        {
            DrawField(gameObject.StableId, componentIndex, component, fields[i]);
        }

        ImGui.EndTable();
    }

    private static bool DrawInspectorComponentHeader(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.22f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.27f, 0.27f, 0.27f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.30f, 0.30f, 0.30f, 1f));
        bool open = ImGui.CollapsingHeader(
            label,
            ImGuiTreeNodeFlags.DefaultOpen |
            ImGuiTreeNodeFlags.SpanAvailWidth |
            ImGuiTreeNodeFlags.AllowOverlap);
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();
        return open;
    }

    internal static ComponentHeaderLayout ResolveComponentHeaderLayout(
        Vector2 headerMin,
        Vector2 headerMax,
        float frameHeight,
        float innerSpacingX,
        float textLineHeight)
    {
        float safeFrameHeight = MathF.Max(1f, frameHeight);
        float headerHeight = MathF.Max(safeFrameHeight, headerMax.Y - headerMin.Y);
        float checkboxX = headerMin.X + safeFrameHeight + MathF.Max(2f, innerSpacingX * 0.5f);
        float checkboxY = headerMin.Y + MathF.Max(0f, (headerHeight - safeFrameHeight) * 0.5f);
        float labelX = checkboxX + safeFrameHeight + MathF.Max(2f, innerSpacingX);
        float labelY = headerMin.Y + MathF.Max(0f, (headerHeight - MathF.Max(1f, textLineHeight)) * 0.5f);
        return new ComponentHeaderLayout(
            new Vector2(checkboxX, checkboxY),
            new Vector2(labelX, labelY),
            headerMin.X + safeFrameHeight);
    }

    internal static bool IsComponentEnabled(EditorComponentModel component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return !component.SerializedFields.TryGetValue(nameof(Behaviour.Enabled), out string? serialized) ||
            !bool.TryParse(serialized, out bool enabled) ||
            enabled;
    }

    internal static string GetComponentDisplayName(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        int separator = Math.Max(typeName.LastIndexOf('.'), typeName.LastIndexOf('+'));
        return separator >= 0 && separator < typeName.Length - 1
            ? typeName[(separator + 1)..]
            : typeName;
    }

    private void DrawField(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(field.Name);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(field.Name);
        }

        _ = ImGui.TableSetColumnIndex(1);
        if (!field.CanWrite || field.Kind == ScriptFieldKind.Unsupported)
        {
            ImGui.TextUnformatted(ReadFieldValue(component, field));
            return;
        }

        // 按 ScriptFieldKind 分派到布尔/数值/字符串/枚举/资产引用编辑器
        switch (field.Kind)
        {
            case ScriptFieldKind.Boolean:
                DrawBoolean(stableId, componentIndex, component, field);
                break;
            case ScriptFieldKind.Number:
                DrawNumber(stableId, componentIndex, component, field);
                break;
            case ScriptFieldKind.String:
                DrawString(stableId, componentIndex, component, field);
                break;
            case ScriptFieldKind.Enum:
                DrawEnum(stableId, componentIndex, component, field);
                break;
            case ScriptFieldKind.Vector:
            case ScriptFieldKind.Material:
                DrawString(stableId, componentIndex, component, field);
                break;
            case ScriptFieldKind.AssetReference:
                DrawAssetReference(stableId, componentIndex, component, field);
                break;
            case ScriptFieldKind.Unsupported:
            default:
                DrawString(stableId, componentIndex, component, field);
                break;
        }
    }

    private void DrawBoolean(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        bool value = bool.TryParse(ReadFieldValue(component, field), out bool parsed) && parsed;
        if (ImGui.Checkbox($"##field-{stableId}-{componentIndex}-{field.Name}", ref value))
        {
            _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, value.ToString()));
        }
    }

    private void DrawNumber(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        float value = float.TryParse(ReadFieldValue(component, field), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : 0f;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputFloat($"##field-{stableId}-{componentIndex}-{field.Name}", ref value))
        {
            _undo.Execute(_scene, new SetComponentFieldCommand(
                stableId,
                componentIndex,
                field.Name,
                value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private void DrawString(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        string value = ReadFieldValue(component, field);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText($"##field-{stableId}-{componentIndex}-{field.Name}", ref value, 256))
        {
            _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, value));
        }
    }

    private void DrawEnum(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        Type enumType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        string[] names = Enum.GetNames(enumType);
        string current = ReadFieldValue(component, field);
        int index = Math.Max(0, Array.IndexOf(names, current));
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo($"##field-{stableId}-{componentIndex}-{field.Name}", ref index, names, names.Length) && index >= 0 && index < names.Length)
        {
            _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, names[index]));
        }
    }

    private void DrawAssetReference(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        string value = ReadFieldValue(component, field);
        string typeLabel = field.AssetKind?.ToString() ?? "Unknown";
        ImGui.TextUnformatted($"{FormatAssetReferenceDisplay(field, value)} ({typeLabel})");
        DrawAssetReferenceDropTarget(stableId, componentIndex, field);
        if (!string.IsNullOrWhiteSpace(value))
        {
            ImGui.SameLine();
            if (ImGui.Button($"Clear##assetref_clear_{stableId}_{componentIndex}_{field.Name}"))
            {
                _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, null));
                RecordAssetDropResult(EditorAssetDropResult.Success($"已清除字段 {field.Name} 的资产引用。", stableId));
            }
        }
    }

    private void DrawAssetReferenceDropTarget(int stableId, int componentIndex, ScriptFieldDescriptor field)
    {
        if (!ImGui.BeginDragDropTarget())
        {
            return;
        }

        try
        {
            if (AssetBrowserDragPayloadImGui.TryAcceptPayload(out AssetBrowserDragPayload payload))
            {
                _ = AcceptAssetBrowserDragPayloadToField(stableId, componentIndex, field, payload);
            }
        }
        finally
        {
            ImGui.EndDragDropTarget();
        }
    }

    internal EditorAssetDropResult AcceptAssetBrowserDragPayloadToField(
        int stableId,
        int componentIndex,
        ScriptFieldDescriptor field,
        AssetBrowserDragPayload browserPayload)
    {
        EditorAssetDropResult result = EditorAssetDropPayload.TryFromBrowserPayload(browserPayload, out EditorAssetDropPayload payload)
            ? ApplyAssetDropPayloadToField(stableId, componentIndex, field, payload)
            : EditorAssetDropResult.Failure("Project Window 拖拽 payload 缺少 stable asset id 或 logical path。");
        RecordAssetDropResult(result);
        return result;
    }

    internal EditorAssetDropResult ApplyAssetDropPayloadToField(
        int stableId,
        int componentIndex,
        ScriptFieldDescriptor field,
        EditorAssetDropPayload payload)
    {
        return EditorAssetInspectorFieldTarget.TryCreate(stableId, componentIndex, field, out EditorAssetInspectorFieldTarget target)
            ? EditorAssetDropService.DropOnInspectorField(_scene, _undo, payload, target)
            : EditorAssetDropResult.Failure($"字段 {field.Name} 不是 typed asset reference 字段。");
    }

    private void RecordAssetDropResult(EditorAssetDropResult result)
    {
        Status = string.IsNullOrWhiteSpace(result.Diagnostic) ? ReadyStatus : result.Diagnostic;
        _console?.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Asset,
            result.Succeeded ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Warning,
            "inspector-asset-drop",
            Status));
    }

    internal static string FormatAssetReferenceDisplay(ScriptFieldDescriptor field, string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "none"
            : !ScriptAssetReference.TryDecode(value, out ScriptAssetReference reference)
            ? $"invalid reference: {value}"
            : field.AssetKind is ScriptAssetKind expected && reference.AssetType != expected
            ? $"type mismatch: {reference.AssetType} {reference.LogicalPath}"
            : $"{reference.LogicalPath} [{reference.AssetId}]";
    }

    private static string ReadFieldValue(EditorComponentModel component, ScriptFieldDescriptor field)
    {
        return component.SerializedFields.TryGetValue(field.Name, out string? value)
            ? value
            : SerializeDefaultValue(field.Value);
    }

    private static string SerializeDefaultValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool boolean => boolean.ToString(),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private Type[] GetBehaviourTypes(string filter)
    {
        List<Type> result = [];
        for (int i = 0; i < _scripts.Assemblies.Count; i++)
        {
            foreach (Type type in _scripts.Assemblies[i].GetTypes())
            {
                if (!IsConcreteBehaviour(type))
                {
                    continue;
                }

                string name = type.FullName ?? type.Name;
                if (string.IsNullOrWhiteSpace(filter) || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(type);
                }
            }
        }

        result.Sort(static (a, b) => string.CompareOrdinal(a.FullName ?? a.Name, b.FullName ?? b.Name));
        return [.. result];
    }

    private bool TryCreateBehaviour(string typeName, out Behaviour behaviour)
    {
        for (int i = 0; i < _scripts.Assemblies.Count; i++)
        {
            Type? type = _scripts.Assemblies[i].GetType(typeName, throwOnError: false);
            if (IsConcreteBehaviour(type))
            {
                behaviour = (Behaviour)Activator.CreateInstance(type!)!;
                return true;
            }
        }

        behaviour = null!;
        return false;
    }

    private static bool IsConcreteBehaviour(Type? type)
    {
        return type is not null &&
            !type.IsAbstract &&
            typeof(Behaviour).IsAssignableFrom(type) &&
            type.GetConstructor(Type.EmptyTypes) is not null;
    }
}

internal readonly record struct ComponentHeaderLayout(
    Vector2 CheckboxPosition,
    Vector2 LabelPosition,
    float ArrowLaneRight);

internal enum TransformFieldLayout
{
    InlineAxes,
    StackedAxes,
}

internal readonly record struct AssetInspectorSnapshot(
    string Path,
    bool Found,
    string Kind,
    string? AssetId,
    long SizeBytes,
    string? PreviewSummary,
    string? PrimaryActionLabel,
    AssetBrowserItem? Item,
    AssetBrowserDetailedPreview? DetailedPreview,
    string Status);

internal readonly record struct AssetPreviewThumbnailLease(
    string Path,
    long SizeBytes,
    long LastModifiedTicks,
    AssetThumbnail Thumbnail);

internal readonly record struct FolderInspectorSnapshot(
    string Path,
    bool Found,
    int AssetCount,
    string Status);
