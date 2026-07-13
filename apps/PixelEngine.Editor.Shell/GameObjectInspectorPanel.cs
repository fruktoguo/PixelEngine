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
    private EditorPrefabLink? _transformEditOldPrefabLink;
    private long _transformEditSceneGeneration;
    private bool _transformEditWasDirty;
    private bool _transformEditApplied;
    private int? _nameEditStableId;
    private string _nameEditBuffer = string.Empty;
    private EditorGameObject? _nameEditTarget;
    private ComponentFieldEditTransaction? _componentFieldEdit;
    private DecimalFieldTextEditState? _decimalFieldTextEdit;
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
                CommitPendingEdits();
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
            CommitPendingEdits();
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
            (_scene.SceneGeneration != _transformEditSceneGeneration ||
             !_scene.TryGet(_transformEditStableId.Value, out EditorGameObject? currentTarget) ||
             !ReferenceEquals(currentTarget, _transformEditTarget));
        bool nameTargetReplaced = _nameEditStableId.HasValue &&
            (!_scene.TryGet(_nameEditStableId.Value, out EditorGameObject? currentNameTarget) ||
             !ReferenceEquals(currentNameTarget, _nameEditTarget));
        bool componentTargetReplaced = _componentFieldEdit is { } componentEdit &&
            (!TryResolveComponentEditTarget(in componentEdit, out _, out _) ||
             selectedStableId != componentEdit.StableId);
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

        if (_componentFieldEdit is not null &&
            (!Visible || mode != EditorMode.Edit || componentTargetReplaced))
        {
            CommitPendingComponentFieldEdit();
        }
    }

    internal bool BeginNameEdit(int stableId)
    {
        CommitPendingTransformEdit();
        CommitPendingComponentFieldEdit();
        if (_nameEditStableId.HasValue && _nameEditStableId != stableId)
        {
            CommitPendingNameEdit();
        }

        if (_nameEditStableId.HasValue)
        {
            return true;
        }

        if (!_scene.TryGet(stableId, out EditorGameObject? gameObject))
        {
            return false;
        }

        _nameEditStableId = stableId;
        _nameEditTarget = gameObject;
        _nameEditBuffer = gameObject.Name;
        return true;
    }

    internal bool ApplyNameEdit(int stableId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!BeginNameEdit(stableId) ||
            !_scene.TryGet(stableId, out EditorGameObject? gameObject) ||
            !ReferenceEquals(gameObject, _nameEditTarget))
        {
            return false;
        }

        _nameEditBuffer = name;
        return true;
    }

    internal bool BeginTransformEdit(int stableId)
    {
        CommitPendingNameEdit();
        CommitPendingComponentFieldEdit();
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
        _transformEditOldPrefabLink = gameObject.PrefabLink?.Clone();
        _transformEditSceneGeneration = _scene.SceneGeneration;
        _transformEditWasDirty = _scene.IsDirty;
        _transformEditApplied = false;
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
        _scene.RecordTransformPrefabOverrides(stableId, transform);
        _transformEditApplied = true;
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

        if (BeginInspectorPropertyTable("asset-inspector-properties", 2))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, ResolveInspectorLabelWidth(ImGui.GetContentRegionAvail().X));
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            DrawReadOnlyProperty("Path", asset.Path);
            DrawReadOnlyProperty("Type", asset.Kind);
            DrawReadOnlyProperty("Stable ID", asset.AssetId ?? "none");
            DrawReadOnlyProperty("Size", FormatAssetSize(asset.SizeBytes));
            EndInspectorPropertyTable();
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
        if (preview.Properties.Count != 0 && BeginInspectorPropertyTable("asset-preview-properties", 2))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, ResolveInspectorLabelWidth(ImGui.GetContentRegionAvail().X));
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            for (int i = 0; i < preview.Properties.Count; i++)
            {
                AssetBrowserPreviewProperty property = preview.Properties[i];
                DrawReadOnlyProperty(property.Label, property.Value);
            }

            EndInspectorPropertyTable();
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
        DrawPropertyLabel(label, disabled: true);
        _ = ImGui.TableSetColumnIndex(1);
        ImGui.TextWrapped(value);
    }

    private static void DrawPropertyLabel(string label, bool disabled = false)
    {
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.12f, 1f)));
        if (disabled)
        {
            ImGui.TextDisabled(label);
        }
        else
        {
            ImGui.TextUnformatted(label);
        }
    }

    private static bool BeginInspectorPropertyTable(string id, int columns, ImGuiTableFlags extraFlags = ImGuiTableFlags.None)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.10f, 0.10f, 0.10f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(0.175f, 0.175f, 0.175f, 1f));
        bool opened = ImGui.BeginTable(
            id,
            columns,
            ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.BordersInnerH |
            extraFlags);
        if (!opened)
        {
            PopInspectorPropertyTableStyle();
        }

        return opened;
    }

    private static void EndInspectorPropertyTable()
    {
        ImGui.EndTable();
        PopInspectorPropertyTableStyle();
    }

    private static void PopInspectorPropertyTableStyle()
    {
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
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

        CommitPendingEdits();
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
            _ = ApplyNameEdit(gameObject.StableId, _nameEditBuffer);
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
        float availableWidth = ImGui.GetContentRegionAvail().X;
        TransformFieldLayout layout = ResolveTransformFieldLayout(availableWidth);
        bool inlineAxes = layout == TransformFieldLayout.InlineAxes;
        string tableId = inlineAxes
            ? "gameobject-transform-fields-inline"
            : "gameobject-transform-fields-stacked";
        int columnCount = inlineAxes ? 5 : 3;
        if (!BeginInspectorPropertyTable(tableId, columnCount))
        {
            return;
        }

        float axisWidth = MathF.Max(18f, ImGui.GetTextLineHeight() + 4f);
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, ResolveInspectorLabelWidth(availableWidth));
        ImGui.TableSetupColumn("AxisA", ImGuiTableColumnFlags.WidthFixed, axisWidth);
        ImGui.TableSetupColumn("ValueA", ImGuiTableColumnFlags.WidthStretch);
        if (inlineAxes)
        {
            ImGui.TableSetupColumn("AxisB", ImGuiTableColumnFlags.WidthFixed, axisWidth);
            ImGui.TableSetupColumn("ValueB", ImGuiTableColumnFlags.WidthStretch);
        }

        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        DrawPropertyLabel("Position");
        _ = ImGui.TableSetColumnIndex(1);
        DrawAxisLabel("X", InspectorAxis.X);
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

        DrawAxisLabel("Y", InspectorAxis.Y);
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
        DrawPropertyLabel("Rotation");
        _ = ImGui.TableSetColumnIndex(1);
        DrawAxisLabel("Z", InspectorAxis.Z);
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
        DrawPropertyLabel("Scale");
        _ = ImGui.TableSetColumnIndex(1);
        DrawAxisLabel("X", InspectorAxis.X);
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

        DrawAxisLabel("Y", InspectorAxis.Y);
        _ = ImGui.TableSetColumnIndex(inlineAxes ? 4 : 2);
        ImGui.SetNextItemWidth(-1f);
        changed = ImGui.DragFloat("##scale-y", ref scaleY, 0.01f);
        if (changed)
        {
            transform.ScaleY = scaleY;
        }
        HandleTransformInput(gameObject, transform, changed);
        DrawTransformDragTooltip();
        EndInspectorPropertyTable();
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

    private static void DrawAxisLabel(string label, InspectorAxis axis)
    {
        Vector4 color = GetAxisColor(axis);
        ImGui.TableSetBgColor(
            ImGuiTableBgTarget.CellBg,
            ImGui.GetColorU32(new Vector4(color.X * 0.22f, color.Y * 0.22f, color.Z * 0.22f, 1f)));
        ImGui.TextColored(color, label);
    }

    private static Vector4 GetAxisColor(InspectorAxis axis)
    {
        return axis switch
        {
            InspectorAxis.X => new Vector4(0.95f, 0.42f, 0.40f, 1f),
            InspectorAxis.Y => new Vector4(0.45f, 0.82f, 0.48f, 1f),
            InspectorAxis.Z => new Vector4(0.38f, 0.62f, 0.96f, 1f),
            InspectorAxis.W => new Vector4(0.76f, 0.55f, 0.95f, 1f),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "未知 Inspector 向量轴。"),
        };
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
        EditorPrefabLink? oldPrefabLink = _transformEditOldPrefabLink;
        long sceneGeneration = _transformEditSceneGeneration;
        bool wasDirty = _transformEditWasDirty;
        bool applied = _transformEditApplied;
        _transformEditStableId = null;
        _transformEditBefore = null;
        _transformEditTarget = null;
        _transformEditOldPrefabLink = null;
        _transformEditWasDirty = false;
        _transformEditApplied = false;
        if (_scene.SceneGeneration != sceneGeneration ||
            !_scene.TryGet(stableId, out EditorGameObject? gameObject) ||
            !ReferenceEquals(gameObject, expectedTarget))
        {
            return;
        }

        EditorSceneTransform after = gameObject.Transform.Clone();
        if (TransformEquals(before, after))
        {
            if (applied)
            {
                _scene.SetPrefabLink(stableId, oldPrefabLink);
                _scene.RestoreDirtyState(wasDirty);
            }

            return;
        }

        _undo.Execute(
            _scene,
            new SetTransformCommand(stableId, before, oldPrefabLink, after, gameObject.PrefabLink));
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

    internal bool BeginComponentFieldEdit(int stableId, int componentIndex, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        if (_decimalFieldTextEdit is { } decimalEdit &&
            !decimalEdit.HasKey(stableId, componentIndex, fieldName))
        {
            _ = CommitPendingDecimalTextEdit();
        }

        CommitPendingNameEdit();
        CommitPendingTransformEdit();
        if (_componentFieldEdit is { } active &&
            (active.StableId != stableId ||
             active.ComponentIndex != componentIndex ||
             !string.Equals(active.FieldName, fieldName, StringComparison.Ordinal)))
        {
            CommitPendingComponentFieldEdit();
        }

        if (_componentFieldEdit is not null)
        {
            return true;
        }

        if (!_scene.TryGet(stableId, out EditorGameObject? gameObject) ||
            (uint)componentIndex >= (uint)gameObject.Components.Count)
        {
            return false;
        }

        EditorComponentModel component = gameObject.Components[componentIndex];
        bool hadOldValue = component.SerializedFields.TryGetValue(fieldName, out string? oldValue);
        _componentFieldEdit = new ComponentFieldEditTransaction(
            stableId,
            componentIndex,
            fieldName,
            _scene.SceneGeneration,
            gameObject,
            component.TypeName,
            hadOldValue,
            oldValue,
            gameObject.PrefabLink?.Clone(),
            _scene.IsDirty,
            PreserveEmptyPrefabOverride: false,
            Applied: false);
        return true;
    }

    internal bool ApplyComponentFieldEdit(int stableId, int componentIndex, string fieldName, string? value)
    {
        if (!BeginComponentFieldEdit(stableId, componentIndex, fieldName) ||
            _componentFieldEdit is not { } active ||
            !TryResolveComponentEditTarget(in active, out _, out _))
        {
            return false;
        }

        EditorComponentModel component = _scene.Get(stableId).Components[componentIndex];
        _scene.SetComponentField(stableId, componentIndex, fieldName, value);
        _scene.RecordPrefabOverride(
            stableId,
            $"Component:{component.TypeName}:{fieldName}",
            value ?? string.Empty);
        _componentFieldEdit = active with
        {
            PreserveEmptyPrefabOverride = value is null,
            Applied = true,
        };
        return true;
    }

    internal void CommitPendingComponentFieldEdit()
    {
        if (_decimalFieldTextEdit is not null)
        {
            _ = CommitPendingDecimalTextEdit();
            return;
        }

        if (_componentFieldEdit is not { } active)
        {
            return;
        }

        _componentFieldEdit = null;
        if (!TryResolveComponentEditTarget(in active, out EditorGameObject? gameObject, out EditorComponentModel? component) ||
            gameObject is null ||
            component is null)
        {
            return;
        }

        bool hasAfterValue = component.SerializedFields.TryGetValue(active.FieldName, out string? afterValue);
        bool fieldUnchanged = active.HadOldValue == hasAfterValue &&
            string.Equals(active.OldValue, afterValue, StringComparison.Ordinal);
        bool hasNewExplicitNullOverride = fieldUnchanged &&
            active.PreserveEmptyPrefabOverride &&
            !PrefabLinksEqual(active.OldPrefabLink, gameObject.PrefabLink);
        if (fieldUnchanged && !hasNewExplicitNullOverride)
        {
            if (active.Applied)
            {
                _scene.SetPrefabLink(active.StableId, active.OldPrefabLink);
                _scene.RestoreDirtyState(active.WasDirty);
            }

            return;
        }

        _undo.Execute(
            _scene,
            new SetComponentFieldCommand(
                active.StableId,
                active.ComponentIndex,
                active.FieldName,
                active.HadOldValue,
                active.OldValue,
                active.OldPrefabLink,
                hasAfterValue ? afterValue : null,
                gameObject.PrefabLink));
    }

    private static bool PrefabLinksEqual(EditorPrefabLink? left, EditorPrefabLink? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null ||
            !string.Equals(left.AssetId, right.AssetId, StringComparison.Ordinal) ||
            !string.Equals(left.AssetPath, right.AssetPath, StringComparison.Ordinal) ||
            !string.Equals(left.SourceStableId, right.SourceStableId, StringComparison.Ordinal) ||
            left.Overrides.Count != right.Overrides.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Overrides.Count; i++)
        {
            EditorPrefabOverride leftOverride = left.Overrides[i];
            EditorPrefabOverride rightOverride = right.Overrides[i];
            if (!string.Equals(leftOverride.SourceStableId, rightOverride.SourceStableId, StringComparison.Ordinal) ||
                !string.Equals(leftOverride.PropertyPath, rightOverride.PropertyPath, StringComparison.Ordinal) ||
                !string.Equals(leftOverride.Value, rightOverride.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    internal bool CommitComponentFieldEditIfMatches(int stableId, int componentIndex, string fieldName)
    {
        if (_componentFieldEdit is not { } active ||
            active.StableId != stableId ||
            active.ComponentIndex != componentIndex ||
            !string.Equals(active.FieldName, fieldName, StringComparison.Ordinal))
        {
            return false;
        }

        CommitPendingComponentFieldEdit();
        return true;
    }

    internal void CommitPendingEdits()
    {
        CommitPendingNameEdit();
        CommitPendingTransformEdit();
        CommitPendingComponentFieldEdit();
    }

    private bool TryResolveComponentEditTarget(
        in ComponentFieldEditTransaction edit,
        out EditorGameObject? gameObject,
        out EditorComponentModel? component)
    {
        if (!_scene.TryGet(edit.StableId, out gameObject) ||
            _scene.SceneGeneration != edit.SceneGeneration ||
            !ReferenceEquals(gameObject, edit.GameObject) ||
            (uint)edit.ComponentIndex >= (uint)gameObject.Components.Count)
        {
            component = null;
            return false;
        }

        component = gameObject.Components[edit.ComponentIndex];
        return string.Equals(component.TypeName, edit.ComponentTypeName, StringComparison.Ordinal);
    }

    private void HandleComponentFieldInput(
        int stableId,
        int componentIndex,
        string fieldName,
        string? serializedValue,
        bool changed)
    {
        if (ImGui.IsItemActivated())
        {
            _ = BeginComponentFieldEdit(stableId, componentIndex, fieldName);
        }

        if (changed)
        {
            _ = ApplyComponentFieldEdit(stableId, componentIndex, fieldName, serializedValue);
        }

        if (ImGui.IsItemDeactivated())
        {
            _ = CommitComponentFieldEditIfMatches(stableId, componentIndex, fieldName);
        }
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
            CommitPendingComponentFieldEdit();
            return;
        }

        if (!TryCreateBehaviour(component.TypeName, out Behaviour? behaviour))
        {
            ImGui.TextUnformatted("Behaviour type unavailable");
            return;
        }

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);
        float availableWidth = ImGui.GetContentRegionAvail().X;
        if (!BeginInspectorPropertyTable($"component-fields-{componentIndex}", 2))
        {
            return;
        }

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, ResolveInspectorLabelWidth(availableWidth));
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        for (int i = 0; i < fields.Length; i++)
        {
            DrawField(gameObject.StableId, componentIndex, component, fields[i]);
        }

        EndInspectorPropertyTable();
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
        DrawPropertyLabel(field.Name);
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
                DrawVector(stableId, componentIndex, component, field);
                break;
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
        string current = ReadFieldValue(component, field);
        Type target = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        bool validRange =
            (!IsIntegerNumericType(target) ||
             TryResolveIntegerFieldRange(target, field, out _, out _)) &&
            ((target != typeof(float) && target != typeof(double)) ||
             TryResolveFloatingFieldRange(target, field, out _, out _)) &&
            (target != typeof(decimal) ||
             TryResolveDecimalFieldRange(field, out _, out _));
        if (!validRange)
        {
            ImGui.TextColored(
                new Vector4(0.95f, 0.55f, 0.35f, 1f),
                $"Range 中不存在 {target.Name} 可表示的值");
            return;
        }

        bool nullableNull = Nullable.GetUnderlyingType(field.FieldType) is not null && current.Length == 0;
        if (target == typeof(decimal))
        {
            DrawDecimalNumber(stableId, componentIndex, field, current);
            return;
        }

        if (nullableNull)
        {
            if (ImGui.Button($"null  ·  Set 0##field-{stableId}-{componentIndex}-{field.Name}"))
            {
                _undo.Execute(
                    _scene,
                    new SetComponentFieldCommand(stableId, componentIndex, field.Name, "0"));
            }

            return;
        }

        if (!IsValidNumericSerializedValue(target, current))
        {
            DrawInvalidNumericValue(stableId, componentIndex, field, target, current);
            return;
        }

        ImGui.SetNextItemWidth(-1f);
        bool changed = TryDrawNumericField(
            $"##field-{stableId}-{componentIndex}-{field.Name}",
            field,
            current,
            out string serialized);
        HandleComponentFieldInput(
            stableId,
            componentIndex,
            field.Name,
            serialized,
            changed);
        DrawComponentDragTooltip();
    }

    private void DrawDecimalNumber(
        int stableId,
        int componentIndex,
        ScriptFieldDescriptor field,
        string current)
    {
        DecimalFieldTextEditState? state =
            _decimalFieldTextEdit is { } existing &&
            existing.Matches(_scene, stableId, componentIndex, field.Name)
                ? existing
                : null;
        string edited = state?.Text ?? current;
        ImGui.SetNextItemWidth(-1f);
        bool submitted = ImGui.InputText(
            $"##field-{stableId}-{componentIndex}-{field.Name}",
            ref edited,
            128,
            ImGuiInputTextFlags.EnterReturnsTrue);
        if (ImGui.IsItemActivated())
        {
            if (state is null)
            {
                _ = CommitPendingDecimalTextEdit();
                state = CreateDecimalFieldTextEdit(
                    stableId,
                    componentIndex,
                    field,
                    current);
            }

            _ = BeginComponentFieldEdit(stableId, componentIndex, field.Name);
        }

        state?.Update(edited);
        bool commit = state is not null && (submitted || ImGui.IsItemDeactivated());
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("decimal 使用精确文本编辑；Enter 或失去焦点时提交，中间输入不会被重置。");
        }

        if (commit)
        {
            _ = CommitPendingDecimalTextEdit();
        }
    }

    private DecimalFieldTextEditState? CreateDecimalFieldTextEdit(
        int stableId,
        int componentIndex,
        ScriptFieldDescriptor field,
        string current)
    {
        if (!_scene.TryGet(stableId, out EditorGameObject? gameObject) ||
            (uint)componentIndex >= (uint)gameObject.Components.Count)
        {
            return null;
        }

        EditorComponentModel component = gameObject.Components[componentIndex];
        return _decimalFieldTextEdit = new DecimalFieldTextEditState(
            stableId,
            componentIndex,
            field,
            _scene.SceneGeneration,
            gameObject,
            component.TypeName,
            current);
    }

    private bool CommitPendingDecimalTextEdit()
    {
        if (_decimalFieldTextEdit is not { } state)
        {
            return false;
        }

        _decimalFieldTextEdit = null;
        bool applied = false;
        if (state.Matches(_scene, state.StableId, state.ComponentIndex, state.Field.Name) &&
            state.Dirty)
        {
            if (TryNormalizeDecimalFieldValue(state.Field, state.Text, out string? serialized))
            {
                applied = ApplyComponentFieldEdit(
                    state.StableId,
                    state.ComponentIndex,
                    state.Field.Name,
                    serialized);
                Status = ReadyStatus;
            }
            else
            {
                Status = $"无效 decimal：{(state.Text.Length == 0 ? "<empty>" : state.Text)}";
            }
        }

        _ = CommitComponentFieldEditIfMatches(
            state.StableId,
            state.ComponentIndex,
            state.Field.Name);
        return applied;
    }

    internal static bool TryNormalizeDecimalFieldValue(
        ScriptFieldDescriptor field,
        string text,
        out string? serialized)
    {
        if (Nullable.GetUnderlyingType(field.FieldType) is not null && text.Length == 0)
        {
            serialized = null;
            return true;
        }

        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed) ||
            !TryResolveDecimalFieldRange(field, out decimal minimum, out decimal maximum))
        {
            serialized = null;
            return false;
        }

        serialized = Math.Clamp(parsed, minimum, maximum).ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private void DrawInvalidNumericValue(
        int stableId,
        int componentIndex,
        ScriptFieldDescriptor field,
        Type target,
        string current)
    {
        ImGui.TextColored(
            new Vector4(0.95f, 0.55f, 0.35f, 1f),
            $"无效 {target.Name}：{(current.Length == 0 ? "<empty>" : current)}");
        ImGui.SameLine();
        if (ImGui.SmallButton($"Reset##number-reset-{stableId}-{componentIndex}-{field.Name}"))
        {
            _undo.Execute(
                _scene,
                new SetComponentFieldCommand(
                    stableId,
                    componentIndex,
                    field.Name,
                    ResolveNumericResetValue(field, target)));
        }
    }

    internal static bool IsValidNumericSerializedValue(Type target, string value)
    {
        return Type.GetTypeCode(target) switch
        {
            TypeCode.Byte => byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            TypeCode.SByte => sbyte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            TypeCode.Int16 => short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            TypeCode.UInt16 => ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            TypeCode.Int32 => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            TypeCode.UInt32 => uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            TypeCode.Int64 => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            TypeCode.UInt64 => ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            TypeCode.Single => float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedSingle) && float.IsFinite(parsedSingle),
            TypeCode.Double => double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsedDouble) && double.IsFinite(parsedDouble),
            TypeCode.Decimal => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            TypeCode.Empty or
            TypeCode.Object or
            TypeCode.DBNull or
            TypeCode.Boolean or
            TypeCode.Char or
            TypeCode.DateTime or
            TypeCode.String => false,
            _ => false,
        };
    }

    private static string? ResolveNumericResetValue(ScriptFieldDescriptor field, Type target)
    {
        if (Nullable.GetUnderlyingType(field.FieldType) is not null)
        {
            return null;
        }

        if (IsIntegerNumericType(target) &&
            TryResolveIntegerFieldRange(target, field, out double integerMinimum, out double integerMaximum))
        {
            return Math.Clamp(0d, integerMinimum, integerMaximum)
                .ToString("0", CultureInfo.InvariantCulture);
        }

        if ((target == typeof(float) || target == typeof(double)) &&
            TryResolveFloatingFieldRange(target, field, out double floatingMinimum, out double floatingMaximum))
        {
            double value = Math.Clamp(0d, floatingMinimum, floatingMaximum);
            return target == typeof(float)
                ? ((float)value).ToString("R", CultureInfo.InvariantCulture)
                : value.ToString("R", CultureInfo.InvariantCulture);
        }

        return target == typeof(decimal) &&
               TryResolveDecimalFieldRange(field, out decimal decimalMinimum, out decimal decimalMaximum)
            ? Math.Clamp(decimal.Zero, decimalMinimum, decimalMaximum)
                .ToString(CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"无法为 {target.FullName} 构造合法的 Inspector reset 值。");
    }

    private static unsafe bool TryDrawNumericField(
        string id,
        ScriptFieldDescriptor field,
        string current,
        out string serialized)
    {
        Type target = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        serialized = current;
        switch (Type.GetTypeCode(target))
        {
            case TypeCode.Byte:
                {
                    byte value = byte.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte parsed) ? parsed : (byte)0;
                    if (!DragScalarValue(id, ImGuiDataType.U8, ref value, 1f))
                    {
                        return false;
                    }

                    value = (byte)ClampIntegerToFieldRange(value, field, byte.MinValue, byte.MaxValue);
                    serialized = value.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

            case TypeCode.SByte:
                {
                    sbyte value = sbyte.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte parsed) ? parsed : (sbyte)0;
                    if (!DragScalarValue(id, ImGuiDataType.S8, ref value, 1f))
                    {
                        return false;
                    }

                    value = (sbyte)ClampIntegerToFieldRange(value, field, sbyte.MinValue, sbyte.MaxValue);
                    serialized = value.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

            case TypeCode.Int16:
                {
                    short value = short.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out short parsed) ? parsed : (short)0;
                    if (!DragScalarValue(id, ImGuiDataType.S16, ref value, 1f))
                    {
                        return false;
                    }

                    value = (short)ClampIntegerToFieldRange(value, field, short.MinValue, short.MaxValue);
                    serialized = value.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

            case TypeCode.UInt16:
                {
                    ushort value = ushort.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort parsed) ? parsed : (ushort)0;
                    if (!DragScalarValue(id, ImGuiDataType.U16, ref value, 1f))
                    {
                        return false;
                    }

                    value = (ushort)ClampIntegerToFieldRange(value, field, ushort.MinValue, ushort.MaxValue);
                    serialized = value.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

            case TypeCode.Int32:
                {
                    int value = int.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
                    if (!DragScalarValue(id, ImGuiDataType.S32, ref value, 1f))
                    {
                        return false;
                    }

                    value = (int)ClampIntegerToFieldRange(value, field, int.MinValue, int.MaxValue);
                    serialized = value.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

            case TypeCode.UInt32:
                {
                    uint value = uint.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed) ? parsed : 0u;
                    if (!DragScalarValue(id, ImGuiDataType.U32, ref value, 1f))
                    {
                        return false;
                    }

                    value = (uint)ClampIntegerToFieldRange(value, field, uint.MinValue, uint.MaxValue);
                    serialized = value.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

            case TypeCode.Int64:
                {
                    long value = long.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0L;
                    if (!DragScalarValue(id, ImGuiDataType.S64, ref value, 1f))
                    {
                        return false;
                    }

                    value = ClampInt64ToFieldRange(value, field);
                    serialized = value.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

            case TypeCode.UInt64:
                {
                    ulong value = ulong.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed) ? parsed : 0UL;
                    if (!DragScalarValue(id, ImGuiDataType.U64, ref value, 1f))
                    {
                        return false;
                    }

                    value = ClampUInt64ToFieldRange(value, field);
                    serialized = value.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

            case TypeCode.Single:
                {
                    float value = float.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && float.IsFinite(parsed)
                        ? parsed
                        : 0f;
                    if (!DragScalarValue(id, ImGuiDataType.Float, ref value, 0.1f) ||
                        !float.IsFinite(value))
                    {
                        return false;
                    }

                    value = (float)ClampToFieldRange(value, field, -float.MaxValue, float.MaxValue);
                    serialized = value.ToString("R", CultureInfo.InvariantCulture);
                    return true;
                }

            case TypeCode.Double:
                {
                    double value = double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed)
                        ? parsed
                        : 0d;
                    if (!DragScalarValue(id, ImGuiDataType.Double, ref value, 0.1f) ||
                        !double.IsFinite(value))
                    {
                        return false;
                    }

                    value = ClampToFieldRange(value, field, -double.MaxValue, double.MaxValue);
                    serialized = value.ToString("R", CultureInfo.InvariantCulture);
                    return true;
                }

            case TypeCode.Decimal:
                throw new InvalidOperationException("decimal 必须走精确文本编辑路径。");

            case TypeCode.Empty:
            case TypeCode.Object:
            case TypeCode.DBNull:
            case TypeCode.Boolean:
            case TypeCode.Char:
            case TypeCode.DateTime:
            case TypeCode.String:
            default:
                throw new NotSupportedException($"Inspector 不支持数值字段类型：{field.FieldType.FullName}");
        }
    }

    private static unsafe bool DragScalarValue<T>(
        string id,
        ImGuiDataType dataType,
        ref T value,
        float speed)
        where T : unmanaged
    {
        fixed (T* valuePointer = &value)
        {
            return ImGui.DragScalar(id, dataType, valuePointer, speed);
        }
    }

    private static bool IsIntegerNumericType(Type type)
    {
        Type normalized = Nullable.GetUnderlyingType(type) ?? type;
        return normalized == typeof(byte) ||
            normalized == typeof(sbyte) ||
            normalized == typeof(short) ||
            normalized == typeof(ushort) ||
            normalized == typeof(int) ||
            normalized == typeof(uint) ||
            normalized == typeof(long) ||
            normalized == typeof(ulong);
    }

    internal static bool TryResolveIntegerFieldRange(
        Type fieldType,
        ScriptFieldDescriptor field,
        out double minimum,
        out double maximum)
    {
        Type normalized = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
        (double TypeMinimum, double TypeMaximum) bounds = normalized == typeof(byte)
            ? (byte.MinValue, byte.MaxValue)
            : normalized == typeof(sbyte)
                ? (sbyte.MinValue, sbyte.MaxValue)
                : normalized == typeof(short)
                    ? (short.MinValue, short.MaxValue)
                    : normalized == typeof(ushort)
                        ? (ushort.MinValue, ushort.MaxValue)
                        : normalized == typeof(int)
                            ? (int.MinValue, int.MaxValue)
                            : normalized == typeof(uint)
                                ? (uint.MinValue, uint.MaxValue)
                                : normalized == typeof(long)
                                    ? (long.MinValue, long.MaxValue)
                                    : normalized == typeof(ulong)
                                        ? (ulong.MinValue, ulong.MaxValue)
                                        : throw new ArgumentOutOfRangeException(
                                            nameof(fieldType),
                                            fieldType,
                                            "字段类型不是 Inspector 支持的整数类型。");
        return TryResolveIntegerBounds(
            field,
            bounds.TypeMinimum,
            bounds.TypeMaximum,
            out minimum,
            out maximum);
    }

    internal static bool TryResolveFloatingFieldRange(
        Type fieldType,
        ScriptFieldDescriptor field,
        out double minimum,
        out double maximum)
    {
        Type normalized = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
        (double typeMinimum, double typeMaximum) = normalized == typeof(float)
            ? (-float.MaxValue, float.MaxValue)
            : normalized == typeof(double)
                ? (-double.MaxValue, double.MaxValue)
                : throw new ArgumentOutOfRangeException(
                    nameof(fieldType),
                    fieldType,
                    "字段类型不是 Inspector 支持的浮点类型。");
        return TryResolveFloatingBounds(
            field,
            typeMinimum,
            typeMaximum,
            out minimum,
            out maximum);
    }

    private static bool TryResolveFloatingBounds(
        ScriptFieldDescriptor field,
        double typeMinimum,
        double typeMaximum,
        out double minimum,
        out double maximum)
    {
        if ((field.RangeMinimum is double rangeMinimum && double.IsNaN(rangeMinimum)) ||
            (field.RangeMaximum is double rangeMaximum && double.IsNaN(rangeMaximum)))
        {
            minimum = default;
            maximum = default;
            return false;
        }

        minimum = Math.Max(typeMinimum, field.RangeMinimum ?? typeMinimum);
        maximum = Math.Min(typeMaximum, field.RangeMaximum ?? typeMaximum);
        return minimum <= maximum;
    }

    internal static bool TryResolveDecimalFieldRange(
        ScriptFieldDescriptor field,
        out decimal minimum,
        out decimal maximum)
    {
        if ((field.RangeMinimum is double rangeMinimum &&
             (double.IsNaN(rangeMinimum) || rangeMinimum > (double)decimal.MaxValue)) ||
            (field.RangeMaximum is double rangeMaximum &&
             (double.IsNaN(rangeMaximum) || rangeMaximum < (double)decimal.MinValue)))
        {
            minimum = default;
            maximum = default;
            return false;
        }

        minimum = ConvertDoubleToDecimalBound(field.RangeMinimum, decimal.MinValue);
        maximum = ConvertDoubleToDecimalBound(field.RangeMaximum, decimal.MaxValue);
        return minimum <= maximum;
    }

    private static bool TryResolveIntegerBounds(
        ScriptFieldDescriptor field,
        double typeMinimum,
        double typeMaximum,
        out double minimum,
        out double maximum)
    {
        minimum = field.RangeMinimum.HasValue
            ? Math.Max(typeMinimum, Math.Ceiling(field.RangeMinimum.Value))
            : typeMinimum;
        maximum = field.RangeMaximum.HasValue
            ? Math.Min(typeMaximum, Math.Floor(field.RangeMaximum.Value))
            : typeMaximum;
        return minimum <= maximum;
    }

    private static double ClampIntegerToFieldRange(
        double value,
        ScriptFieldDescriptor field,
        double typeMinimum,
        double typeMaximum)
    {
        return TryResolveIntegerBounds(field, typeMinimum, typeMaximum, out double minimum, out double maximum)
            ? Math.Clamp(value, minimum, maximum)
            : value;
    }

    private static decimal ConvertDoubleToDecimalBound(double? value, decimal fallback)
    {
        return !value.HasValue
            ? fallback
            : value.Value <= (double)decimal.MinValue
                ? decimal.MinValue
                : value.Value >= (double)decimal.MaxValue ? decimal.MaxValue : (decimal)value.Value;
    }

    private static double ClampToFieldRange(
        double value,
        ScriptFieldDescriptor field,
        double typeMinimum,
        double typeMaximum)
    {
        return TryResolveFloatingBounds(field, typeMinimum, typeMaximum, out double minimum, out double maximum)
            ? Math.Clamp(value, minimum, maximum)
            : value;
    }

    private static long ClampInt64ToFieldRange(long value, ScriptFieldDescriptor field)
    {
        return !TryResolveIntegerBounds(field, long.MinValue, long.MaxValue, out double minimum, out double maximum)
            ? value
            : Math.Clamp(
                value,
                ConvertDoubleToInt64Bound(minimum),
                ConvertDoubleToInt64Bound(maximum));
    }

    private static ulong ClampUInt64ToFieldRange(ulong value, ScriptFieldDescriptor field)
    {
        return !TryResolveIntegerBounds(field, ulong.MinValue, ulong.MaxValue, out double minimum, out double maximum)
            ? value
            : Math.Clamp(
                value,
                ConvertDoubleToUInt64Bound(minimum),
                ConvertDoubleToUInt64Bound(maximum));
    }

    private static long ConvertDoubleToInt64Bound(double value)
    {
        return double.IsNaN(value)
            ? 0L
            : value <= long.MinValue
                ? long.MinValue
                : value >= long.MaxValue ? long.MaxValue : (long)value;
    }

    private static ulong ConvertDoubleToUInt64Bound(double value)
    {
        return double.IsNaN(value) || value <= 0d
            ? 0UL
            : value >= ulong.MaxValue ? ulong.MaxValue : (ulong)value;
    }

    private void DrawVector(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        Type target = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        string current = ReadFieldValue(component, field);
        Span<float> values = stackalloc float[4];
        int count;
        if (target == typeof(Vector2) && SerializedFieldValueCodec.TryParseVector2(current, out Vector2 vector2))
        {
            values[0] = vector2.X;
            values[1] = vector2.Y;
            count = 2;
        }
        else if (target == typeof(Vector3) && SerializedFieldValueCodec.TryParseVector3(current, out Vector3 vector3))
        {
            values[0] = vector3.X;
            values[1] = vector3.Y;
            values[2] = vector3.Z;
            count = 3;
        }
        else if (target == typeof(Vector4) && SerializedFieldValueCodec.TryParseVector4(current, out Vector4 vector4))
        {
            values[0] = vector4.X;
            values[1] = vector4.Y;
            values[2] = vector4.Z;
            values[3] = vector4.W;
            count = 4;
        }
        else
        {
            ImGui.TextColored(new Vector4(0.95f, 0.55f, 0.35f, 1f), $"无效 {target.Name}：{current}");
            if (ImGui.Button($"Reset to Zero##vector-reset-{stableId}-{componentIndex}-{field.Name}"))
            {
                string reset = target == typeof(Vector2)
                    ? SerializedFieldValueCodec.Format(Vector2.Zero)
                    : target == typeof(Vector3)
                        ? SerializedFieldValueCodec.Format(Vector3.Zero)
                        : target == typeof(Vector4)
                            ? SerializedFieldValueCodec.Format(Vector4.Zero)
                            : throw new NotSupportedException($"Inspector 不支持向量类型：{target.FullName}");
                _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, reset));
            }
            return;
        }

        DrawVectorComponents(stableId, componentIndex, field.Name, values[..count]);
    }

    private void DrawVectorComponents(
        int stableId,
        int componentIndex,
        string fieldName,
        Span<float> values)
    {
        VectorFieldLayout layout = ResolveVectorFieldLayout(ImGui.GetContentRegionAvail().X, values.Length);
        bool inline = layout == VectorFieldLayout.InlineAxes;
        int columnCount = inline ? values.Length * 2 : 2;
        if (!ImGui.BeginTable(
            $"vector-field-{stableId}-{componentIndex}-{fieldName}",
            columnCount,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            return;
        }

        float axisWidth = MathF.Max(18f, ImGui.GetTextLineHeight() + 4f);
        if (inline)
        {
            for (int i = 0; i < values.Length; i++)
            {
                ImGui.TableSetupColumn($"Axis{i}", ImGuiTableColumnFlags.WidthFixed, axisWidth);
                ImGui.TableSetupColumn($"Value{i}", ImGuiTableColumnFlags.WidthStretch);
            }
        }
        else
        {
            ImGui.TableSetupColumn("Axis", ImGuiTableColumnFlags.WidthFixed, axisWidth);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (!inline || i == 0)
            {
                ImGui.TableNextRow();
            }

            int axisColumn = inline ? i * 2 : 0;
            int valueColumn = axisColumn + 1;
            _ = ImGui.TableSetColumnIndex(axisColumn);
            InspectorAxis axis = (InspectorAxis)i;
            DrawAxisLabel(axis.ToString(), axis);
            _ = ImGui.TableSetColumnIndex(valueColumn);
            ImGui.SetNextItemWidth(-1f);
            float componentValue = values[i];
            bool changed = ImGui.DragFloat(
                $"##vector-{stableId}-{componentIndex}-{fieldName}-{i}",
                ref componentValue,
                0.1f);
            bool validChange = changed && float.IsFinite(componentValue);
            if (validChange)
            {
                values[i] = componentValue;
            }

            string? serialized = null;
            if (validChange)
            {
                serialized = values.Length switch
                {
                    2 => SerializedFieldValueCodec.Format(new Vector2(values[0], values[1])),
                    3 => SerializedFieldValueCodec.Format(new Vector3(values[0], values[1], values[2])),
                    4 => SerializedFieldValueCodec.Format(new Vector4(values[0], values[1], values[2], values[3])),
                    _ => throw new ArgumentOutOfRangeException(nameof(values), values.Length, "Inspector 仅支持 Vector2/3/4。"),
                };
            }

            HandleComponentFieldInput(stableId, componentIndex, fieldName, serialized, validChange);
            DrawComponentDragTooltip();
        }

        ImGui.EndTable();
    }

    internal static VectorFieldLayout ResolveVectorFieldLayout(float availableWidth, int componentCount)
    {
        if (componentCount is < 2 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(componentCount));
        }

        float width = float.IsFinite(availableWidth) ? availableWidth : 0f;
        return width >= componentCount * 76f
            ? VectorFieldLayout.InlineAxes
            : VectorFieldLayout.StackedAxes;
    }

    private static void DrawComponentDragTooltip()
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("左右拖动快速修改；Ctrl+单击后可精确输入。一次拖动只生成一条 Undo。");
        }
    }

    private void DrawString(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        string value = ReadFieldValue(component, field);
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.InputText($"##field-{stableId}-{componentIndex}-{field.Name}", ref value, 256);
        HandleComponentFieldInput(stableId, componentIndex, field.Name, value, changed);
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
            Vector2 vector2 => SerializedFieldValueCodec.Format(vector2),
            Vector3 vector3 => SerializedFieldValueCodec.Format(vector3),
            Vector4 vector4 => SerializedFieldValueCodec.Format(vector4),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
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

internal enum VectorFieldLayout
{
    InlineAxes,
    StackedAxes,
}

internal enum InspectorAxis
{
    X,
    Y,
    Z,
    W,
}

internal sealed class DecimalFieldTextEditState(
    int stableId,
    int componentIndex,
    ScriptFieldDescriptor field,
    long sceneGeneration,
    EditorGameObject gameObject,
    string componentTypeName,
    string text)
{
    private readonly long _sceneGeneration = sceneGeneration;
    private readonly EditorGameObject _gameObject =
        gameObject ?? throw new ArgumentNullException(nameof(gameObject));
    private readonly string _componentTypeName =
        componentTypeName ?? throw new ArgumentNullException(nameof(componentTypeName));

    public int StableId { get; } = stableId;

    public int ComponentIndex { get; } = componentIndex;

    public ScriptFieldDescriptor Field { get; } = field;

    public string Text { get; private set; } =
        text ?? throw new ArgumentNullException(nameof(text));

    public bool Dirty { get; private set; }

    public bool HasKey(int stableId, int componentIndex, string fieldName)
    {
        return StableId == stableId &&
            ComponentIndex == componentIndex &&
            string.Equals(Field.Name, fieldName, StringComparison.Ordinal);
    }

    public bool Matches(EditorSceneModel scene, int stableId, int componentIndex, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return HasKey(stableId, componentIndex, fieldName) &&
            scene.SceneGeneration == _sceneGeneration &&
            scene.TryGet(stableId, out EditorGameObject? currentGameObject) &&
            ReferenceEquals(currentGameObject, _gameObject) &&
            (uint)componentIndex < (uint)currentGameObject.Components.Count &&
            string.Equals(
                currentGameObject.Components[componentIndex].TypeName,
                _componentTypeName,
                StringComparison.Ordinal);
    }

    public void Update(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Dirty |= !string.Equals(Text, text, StringComparison.Ordinal);
        Text = text;
    }
}

internal readonly record struct ComponentFieldEditTransaction(
    int StableId,
    int ComponentIndex,
    string FieldName,
    long SceneGeneration,
    EditorGameObject GameObject,
    string ComponentTypeName,
    bool HadOldValue,
    string? OldValue,
    EditorPrefabLink? OldPrefabLink,
    bool WasDirty,
    bool PreserveEmptyPrefabOverride,
    bool Applied);

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
