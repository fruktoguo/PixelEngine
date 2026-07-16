using Hexa.NET.ImGui;
using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Scripting;
using PixelEngine.UI;
using System.Globalization;
using System.Numerics;
using L = PixelEngine.Editor.EditorLocalization;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Inspector 面板：Transform 与组件字段编辑。
/// </summary>
[EditorUiSurface("editor.panel.inspector")]
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
    private string _statusMessage = string.Empty;
    private string _canvasOptionsLocale = string.Empty;
    private string[] _scaleModeLabels = [];
    private string[] _screenMatchModeLabels = [];
    private string[] _physicalUnitLabels = [];
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
    private BuiltInCanvasEditTransaction? _builtInCanvasEdit;
    private DecimalFieldTextEditState? _decimalFieldTextEdit;
    private RuntimeDecimalFieldTextEditState? _runtimeDecimalFieldTextEdit;
    private int _focusDelayFrames;
    private bool _focusRequested;
    private string _assetPreviewCachePath = string.Empty;
    private long _assetPreviewCacheSizeBytes = -1;
    private long _assetPreviewCacheModifiedTicks = -1;
    private AssetBrowserDetailedPreview? _assetPreviewCache;
    private AssetPreviewThumbnailLease? _assetPreviewThumbnail;
    private ScriptedRuntimeInspectorProbeSnapshot _runtimeInspectorProbe;
    private long _runtimeInspectorRenderRevision;
    private bool _disposed;

    public string Title => EditorDockSpace.InspectorWindowTitle;

    internal string Status => string.IsNullOrWhiteSpace(_statusMessage)
        ? L.Get("status.ready", "Ready")
        : _statusMessage;

    internal ScriptedRuntimeInspectorProbeSnapshot CaptureScriptedRuntimeInspectorProbe()
    {
        return _runtimeInspectorProbe;
    }

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

    [EditorUiCommands(
        "panel.inspector",
        "context.inspector.transform.reset")]
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
        PrepareFrame(context.Selection.GameObjectStableId, context.Selection.EntityHandle);
        if (_focusDelayFrames > 0)
        {
            _focusDelayFrames--;
        }
        else if (_focusRequested)
        {
            ImGui.SetNextWindowFocus();
            _focusRequested = false;
        }

        string windowTitle = L.GetWindowTitle("window.inspector", "Inspector", Title);
        if (!ImGui.Begin(windowTitle))
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
            ImGui.TextUnformatted(L.Get("inspector.empty", "Select a GameObject or asset to inspect it."));
            ImGui.End();
            return;
        }

        EditorMode mode = CaptureMode();
        bool canModify = mode == EditorMode.Edit;
        if (!canModify)
        {
            TextColoredUnformatted(
                new Vector4(0.95f, 0.70f, 0.25f, 1f),
                mode == EditorMode.Paused
                    ? L.Get("inspector.playPausedReadOnly", "Play is paused — authoring data is read-only")
                    : L.Get("inspector.playRunningReadOnly", "Play is running — authoring data is read-only"));
            ImGui.Separator();
        }

        ImGui.BeginDisabled(!canModify);
        DrawHeader(gameObject);
        bool transformOpen = DrawInspectorComponentHeader(
            $"{L.Get("inspector.transform", "Transform")}##gameobject-transform");
        bool resetTransform = false;
        if (ImGui.BeginPopupContextItem("gameobject-transform-context"))
        {
            resetTransform = ImGui.MenuItem(L.Get("inspector.action.reset", "Reset"));
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
        ImGui.EndDisabled();
        if (!string.IsNullOrWhiteSpace(_statusMessage))
        {
            ImGui.Separator();
            TextColoredUnformatted(new Vector4(0.95f, 0.70f, 0.25f, 1f), Status);
        }

        ImGui.End();
    }

    /// <summary>
    /// 在面板可见性与绘制顺序之外收口连续 Transform 编辑。
    /// Hierarchy 先于 Inspector 绘制；若用户从 A 的 InputFloat 直接点击 B，A 的旧控件不会再被绘制，
    /// 因此不能只依赖 ImGui.IsItemDeactivatedAfterEdit 提交 Undo。
    /// </summary>
    internal void PrepareFrame(int? selectedStableId, string? selectedEntityHandle = null)
    {
        EditorMode mode = CaptureMode();
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
        bool canvasTargetReplaced = _builtInCanvasEdit is { } canvasEdit &&
            (_scene.SceneGeneration != canvasEdit.SceneGeneration ||
             !_scene.TryGet(canvasEdit.StableId, out EditorGameObject? canvasTarget) ||
             !ReferenceEquals(canvasTarget, canvasEdit.GameObject) ||
             selectedStableId != canvasEdit.StableId);
        PrepareRuntimeFieldEdit(selectedEntityHandle, mode);
        UpdateRuntimeEditLifetime(mode);
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

        if (_builtInCanvasEdit is not null &&
            (!Visible || mode != EditorMode.Edit || canvasTargetReplaced))
        {
            CommitPendingBuiltInCanvasEdit();
        }
    }

    internal bool BeginNameEdit(int stableId)
    {
        if (!CanModifyAuthoringScene())
        {
            return false;
        }

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
        if (!CanModifyAuthoringScene())
        {
            return false;
        }

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
            return new AssetInspectorSnapshot(
                assetPath,
                Found: false,
                L.Get("inspector.unknown", "Unknown"),
                null,
                0,
                null,
                null,
                null,
                null,
                L.Get("inspector.assetSourceUnavailable", "Asset data source unavailable"));
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
                    GetAssetKindLabel(item.Kind),
                    item.AssetId,
                    item.SizeBytes,
                    item.PreviewSummary,
                    GetPrimaryAssetActionLabel(item.Kind),
                    item,
                    preview,
                    L.Get("status.ready", "Ready"));
            }
        }

        return new AssetInspectorSnapshot(
            assetPath,
            Found: false,
            L.Get("inspector.unknown", "Unknown"),
            null,
            0,
            null,
            null,
            null,
            null,
            L.Format("inspector.assetMissing", "Asset not found: {0}", assetPath));
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
            return new FolderInspectorSnapshot(
                normalized,
                Found: false,
                0,
                L.Get("inspector.folderSourceUnavailable", "Folder data source unavailable"));
        }

        IReadOnlyList<AssetBrowserFolderItem> folders = folderSource.ListFolders();
        for (int i = 0; i < folders.Count; i++)
        {
            AssetBrowserFolderItem folder = folders[i];
            if (string.Equals(folder.Path, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return new FolderInspectorSnapshot(
                    folder.Path,
                    Found: true,
                    folder.AssetCount,
                    L.Get("status.ready", "Ready"));
            }
        }

        return new FolderInspectorSnapshot(
            normalized,
            Found: false,
            0,
            L.Format("inspector.folderMissing", "Folder not found: {0}", normalized));
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
            RecordInspectorStatus(
                L.Format("inspector.action.unsupportedKind", "Asset type cannot be acted on: {0}", asset.Kind),
                EditorConsoleSeverity.Warning,
                "inspector-asset-action",
                GetAssetSelectionKey(asset.Path));
            return false;
        }

        switch (item.Kind)
        {
            case AssetBrowserItemKind.Script:
                if (_openScriptAsset is null)
                {
                    RecordInspectorStatus(
                        L.Get("inspector.action.scriptEditorUnavailable", "External script editor unavailable"),
                        EditorConsoleSeverity.Warning,
                        "inspector-asset-action",
                        GetAssetSelectionKey(asset.Path));
                    return false;
                }

                bool opened = _openScriptAsset(asset.Path, out string diagnostic);
                RecordInspectorStatus(
                    string.IsNullOrWhiteSpace(diagnostic)
                        ? opened
                            ? L.Format("inspector.action.scriptOpened", "Opened script {0}", asset.Path)
                            : L.Format("inspector.action.scriptOpenFailed", "Failed to open script: {0}", asset.Path)
                        : diagnostic,
                    opened ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Warning,
                    "inspector-asset-action",
                    GetAssetSelectionKey(asset.Path),
                    writeConsole: false);
                return opened;

            case AssetBrowserItemKind.Prefab:
                if (_instantiatePrefab is null)
                {
                    RecordInspectorStatus(
                        L.Get("inspector.action.prefabServiceUnavailable", "Prefab instantiation service unavailable"),
                        EditorConsoleSeverity.Warning,
                        "inspector-asset-action",
                        GetAssetSelectionKey(asset.Path));
                    return false;
                }

                bool instantiated = _instantiatePrefab(asset.Path, out string prefabDiagnostic);
                RecordInspectorStatus(
                    string.IsNullOrWhiteSpace(prefabDiagnostic)
                        ? instantiated
                            ? L.Format("inspector.action.prefabInstantiated", "Instantiated {0}", asset.Path)
                            : L.Format("inspector.action.prefabInstantiateFailed", "Failed to instantiate Prefab: {0}", asset.Path)
                        : prefabDiagnostic,
                    instantiated ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Warning,
                    "inspector-asset-action",
                    GetAssetSelectionKey(asset.Path),
                    writeConsole: false);
                return instantiated;

            case AssetBrowserItemKind.Scene:
                if (_openSceneAsset is null)
                {
                    RecordInspectorStatus(
                        L.Get("inspector.action.sceneServiceUnavailable", "Scene open service unavailable"),
                        EditorConsoleSeverity.Warning,
                        "inspector-asset-action",
                        GetAssetSelectionKey(asset.Path));
                    return false;
                }

                bool sceneOpened = _openSceneAsset(asset.Path, out string sceneDiagnostic);
                RecordInspectorStatus(
                    string.IsNullOrWhiteSpace(sceneDiagnostic)
                        ? sceneOpened
                            ? L.Format("inspector.action.sceneOpened", "Opened scene {0}", asset.Path)
                            : L.Format("inspector.action.sceneOpenFailed", "Failed to open scene: {0}", asset.Path)
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
                RecordInspectorStatus(
                    L.Format("inspector.action.none", "This asset has no primary Inspector action: {0}", asset.Path),
                    EditorConsoleSeverity.Info,
                    "inspector-asset-action",
                    GetAssetSelectionKey(asset.Path));
                return false;
        }
    }

    [EditorUiCommands(
        "panel.inspector.asset",
        "panel.inspector.asset.primary-action",
        "panel.inspector.asset.open-script")]
    private void DrawAssetInspector(string assetPath)
    {
        AssetInspectorSnapshot asset = CaptureAssetInspector(assetPath);
        ImGui.SeparatorText(L.Get("inspector.asset", "Asset"));
        if (!asset.Found || asset.Item is not { } item || asset.DetailedPreview is not { } preview)
        {
            TextColoredUnformatted(new Vector4(0.95f, 0.55f, 0.35f, 1f), asset.Status);
            return;
        }

        ImGui.TextUnformatted(preview.Title);
        TextDisabledUnformatted(asset.Kind);
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
            SetupInspectorPropertyColumns(ImGui.GetContentRegionAvail().X);
            DrawReadOnlyProperty(L.Get("inspector.path", "Path"), asset.Path);
            DrawReadOnlyProperty(L.Get("inspector.type", "Type"), asset.Kind);
            DrawReadOnlyProperty(L.Get("inspector.stableId", "Stable ID"), asset.AssetId ?? L.Get("inspector.none", "None"));
            DrawReadOnlyProperty(L.Get("inspector.size", "Size"), FormatAssetSize(asset.SizeBytes));
            EndInspectorPropertyTable();
        }

        ImGui.Spacing();
        ImGui.SeparatorText(L.Get("inspector.preview", "Preview"));
        DrawAssetPreview(in item, preview);

        string selectionStatus = GetSelectionStatus(GetAssetSelectionKey(asset.Path), asset.Status);
        if (!string.Equals(selectionStatus, L.Get("status.ready", "Ready"), StringComparison.Ordinal))
        {
            ImGui.Spacing();
            TextColoredUnformatted(new Vector4(0.95f, 0.70f, 0.25f, 1f), selectionStatus);
        }
    }

    [EditorUiCommands(
        "panel.inspector.asset.preview",
        "panel.inspector.asset.preview-audio",
        "panel.inspector.asset.references")]
    private void DrawAssetPreview(in AssetBrowserItem item, AssetBrowserDetailedPreview preview)
    {
        TextWrappedUnformatted(preview.Summary);
        if (preview.Properties.Count != 0 && BeginInspectorPropertyTable("asset-preview-properties", 2))
        {
            SetupInspectorPropertyColumns(ImGui.GetContentRegionAvail().X);
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
                if (ImGui.Button($"▶ {L.Get("inspector.audioPreview", "Play Preview")}##inspector-audio-preview", new Vector2(-1f, 0f)))
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
            TextDisabledUnformatted(preview.Diagnostic);
        }
    }

    private void DrawAssetImagePreview(in AssetBrowserItem item)
    {
        AssetThumbnail? thumbnail = ResolveAssetPreviewThumbnail(in item);
        if (thumbnail is not { } image)
        {
            TextDisabledUnformatted(L.Get("inspector.textureUnavailable", "Texture preview unavailable"));
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
        TextWrappedUnformatted(value);
    }

    private static void TextWrappedUnformatted(string text)
    {
        float contentWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    private static void TextColoredUnformatted(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void TextDisabledUnformatted(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void SetTooltipUnformatted(string text)
    {
        _ = ImGui.BeginTooltip();
        TextWrappedUnformatted(text);
        ImGui.EndTooltip();
    }

    private static void DrawPropertyLabel(string label, bool disabled = false)
    {
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.12f, 1f)));
        string visibleLabel = ResolveVisiblePropertyLabel(label, ImGui.GetContentRegionAvail().X);
        if (disabled)
        {
            TextDisabledUnformatted(visibleLabel);
        }
        else
        {
            ImGui.TextUnformatted(visibleLabel);
        }
    }

    private static string ResolveVisiblePropertyLabel(string label, float availableWidth)
    {
        ArgumentNullException.ThrowIfNull(label);
        float width = float.IsFinite(availableWidth) ? MathF.Max(1f, availableWidth) : 1f;
        if (label.Length == 0 || ImGui.CalcTextSize(label).X <= width)
        {
            return label;
        }

        const string Ellipsis = "…";
        float ellipsisWidth = ImGui.CalcTextSize(Ellipsis).X;
        if (ellipsisWidth >= width)
        {
            return Ellipsis;
        }

        int lower = 0;
        int upper = label.Length;
        while (lower < upper)
        {
            int candidate = lower + ((upper - lower + 1) / 2);
            string prefix = label[..candidate];
            if (ImGui.CalcTextSize(prefix).X + ellipsisWidth <= width)
            {
                lower = candidate;
            }
            else
            {
                upper = candidate - 1;
            }
        }

        if (lower > 0 && char.IsHighSurrogate(label[lower - 1]))
        {
            lower--;
        }

        return string.Concat(label.AsSpan(0, lower), Ellipsis);
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
        return Math.Clamp(width * 0.44f, 72f, 144f);
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
        ImGui.SeparatorText(L.Get("inspector.folder", "Folder"));
        if (BeginInspectorPropertyTable("folder-inspector-properties", 2))
        {
            SetupInspectorPropertyColumns(ImGui.GetContentRegionAvail().X);
            DrawReadOnlyProperty(
                L.Get("inspector.path", "Path"),
                string.IsNullOrEmpty(folder.Path) ? "content/" : folder.Path + "/");
            DrawReadOnlyProperty(L.Get("inspector.type", "Type"), L.Get("inspector.folder", "Folder"));
            DrawReadOnlyProperty(
                L.Get("inspector.assetCount", "Assets"),
                folder.AssetCount.ToString(CultureInfo.InvariantCulture));
            EndInspectorPropertyTable();
        }

        string selectionStatus = GetSelectionStatus(GetFolderSelectionKey(folder.Path), folder.Status);
        if (!string.Equals(selectionStatus, L.Get("status.ready", "Ready"), StringComparison.Ordinal))
        {
            ImGui.Spacing();
            TextColoredUnformatted(new Vector4(0.95f, 0.70f, 0.25f, 1f), selectionStatus);
        }
    }

    private static string? GetPrimaryAssetActionLabel(AssetBrowserItemKind kind)
    {
        return kind switch
        {
            AssetBrowserItemKind.Script => L.Get("inspector.action.openScript", "Open Script"),
            AssetBrowserItemKind.Prefab => L.Get("inspector.action.instantiate", "Instantiate"),
            AssetBrowserItemKind.Scene => L.Get("inspector.action.openScene", "Open Scene"),
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
        _statusMessage = string.IsNullOrWhiteSpace(status) ? string.Empty : status;
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
        return !string.IsNullOrWhiteSpace(_statusMessage) &&
            string.Equals(_statusSelectionKey, selectionKey, StringComparison.Ordinal)
            ? _statusMessage
            : fallback;
    }

    private static string GetAssetKindLabel(AssetBrowserItemKind kind)
    {
        return kind switch
        {
            AssetBrowserItemKind.Folder => L.Get("inspector.kind.folder", "Folder"),
            AssetBrowserItemKind.Material => L.Get("inspector.kind.material", "Material"),
            AssetBrowserItemKind.Texture => L.Get("inspector.kind.texture", "Texture"),
            AssetBrowserItemKind.Audio => L.Get("inspector.kind.audio", "Audio"),
            AssetBrowserItemKind.Scene => L.Get("inspector.kind.scene", "Scene"),
            AssetBrowserItemKind.Prefab => L.Get("inspector.kind.prefab", "Prefab"),
            AssetBrowserItemKind.Script => L.Get("inspector.kind.script", "Script"),
            AssetBrowserItemKind.UiScreen => L.Get("inspector.kind.uiScreen", "UI Screen"),
            AssetBrowserItemKind.Json => L.Get("inspector.kind.json", "JSON"),
            AssetBrowserItemKind.Other => L.Get("inspector.kind.other", "Other"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Inspector asset kind."),
        };
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
            new(L.Get("inspector.type", "Type"), item.Descriptor?.TypeLabel ?? item.Kind.ToString()),
            new(L.Get("inspector.path", "Path"), item.Path),
            new(L.Get("inspector.size", "Size"), FormatAssetSize(item.SizeBytes)),
        ];
        return new AssetBrowserDetailedPreview(
            item.DisplayName,
            contentKind,
            item.PreviewSummary ?? item.Descriptor?.Purpose ?? L.Get("inspector.noSummary", "No summary available"),
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
                L.Get("inspector.audioUnavailable", "Audio preview unavailable"),
                EditorConsoleSeverity.Warning,
                "inspector-audio-preview",
                GetAssetSelectionKey(assetPath));
            return false;
        }

        bool played = _audioPreview.TryPlayPreview(assetPath);
        RecordInspectorStatus(
            played
                ? L.Format("inspector.audioPlayed", "Previewing {0}", assetPath)
                : L.Format("inspector.audioFailed", "Failed to preview audio: {0}", assetPath),
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

    private void PrepareRuntimeFieldEdit(string? selectedEntityHandle, EditorMode mode)
    {
        if (_runtimeDecimalFieldTextEdit is not { } edit)
        {
            return;
        }

        if (!Visible ||
            mode is not (EditorMode.Play or EditorMode.Paused) ||
            !string.Equals(edit.EntityHandle, selectedEntityHandle, StringComparison.Ordinal))
        {
            _ = CommitPendingRuntimeDecimalFieldEdit();
        }
    }

    private void UpdateRuntimeEditLifetime(EditorMode mode)
    {
        if (_modeProvider is null || _runtimeSource is null)
        {
            return;
        }

        if (_lastMode is EditorMode.Play or EditorMode.Paused &&
            mode == EditorMode.Edit)
        {
            _ = CommitPendingRuntimeDecimalFieldEdit();
            _runtimeSource.RestoreTemporaryEdits();
        }

        _lastMode = mode;
    }

    [EditorUiCommands("panel.inspector.runtime")]
    private void DrawRuntimeEntityInspector(string handle)
    {
        if (_runtimeSource is null || !_runtimeSource.TryGetEntity(handle, out ScriptEntityInspection entity))
        {
            _ = CommitPendingRuntimeDecimalFieldEdit();
            _runtimeInspectorProbe = new ScriptedRuntimeInspectorProbeSnapshot(
                handle,
                EntityResolved: false,
                TransformTableRendered: false,
                ComponentHeaderCount: 0,
                ComponentPropertyTableCount: 0,
                ComponentNumericDragFieldCount: 0,
                ComponentVectorDragFieldCount: 0,
                ComponentDecimalFieldCount: 0,
                RenderRevision: ++_runtimeInspectorRenderRevision);
            ImGui.TextUnformatted(L.Get("inspector.runtime.entityUnavailable", "Runtime entity is no longer available"));
            return;
        }

        bool transformTableRendered = false;
        int componentHeaderCount = 0;
        int componentPropertyTableCount = 0;
        int componentNumericDragFieldCount = 0;
        int componentVectorDragFieldCount = 0;
        int componentDecimalFieldCount = 0;
        TextColoredUnformatted(
            new Vector4(0.45f, 0.72f, 1f, 1f),
            L.Get("inspector.runtime.temporary", "Play Mode · changes are temporary"));
        ImGui.TextUnformatted(L.Format(
            "inspector.runtime.entityIdentity",
            "{0} · Entity {1}",
            entity.Handle,
            entity.EntityId));
        if (entity.Transform is not null)
        {
            ImGui.SeparatorText(L.Get("inspector.runtime.transform", "Transform (Runtime)"));
            transformTableRendered = DrawRuntimeTransform(entity);
        }

        ImGui.SeparatorText(L.Get("inspector.runtime.components", "Components (Runtime)"));
        for (int i = 0; i < entity.Components.Length; i++)
        {
            ScriptComponentInspection component = entity.Components[i];
            string componentName = GetComponentDisplayName(component.TypeName);
            componentHeaderCount++;
            if (!DrawInspectorComponentHeader(
                $"{componentName}##runtime_component_{entity.EntityId}_{i}"))
            {
                CommitRuntimeDecimalFieldEditIfMatches(handle, i);
                continue;
            }

            if (ImGui.IsItemHovered())
            {
                SetTooltipUnformatted(component.TypeName);
            }

            TextDisabledUnformatted(component.Faulted
                ? L.Get("inspector.component.faulted", "Faulted")
                : component.Enabled
                    ? L.Get("inspector.component.enabled", "Enabled")
                    : L.Get("inspector.component.disabled", "Disabled"));
            ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(component.Behaviour);
            float availableWidth = ImGui.GetContentRegionAvail().X;
            if (fields.Length == 0 ||
                !BeginInspectorPropertyTable($"runtime-component-fields-{entity.EntityId}-{i}", 2))
            {
                continue;
            }

            componentPropertyTableCount++;
            SetupInspectorPropertyColumns(availableWidth);
            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                ScriptFieldDescriptor field = fields[fieldIndex];
                if (field.CanWrite && field.Kind == ScriptFieldKind.Number)
                {
                    Type target = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                    if (target == typeof(decimal))
                    {
                        componentDecimalFieldCount++;
                    }
                    else if (HasValidNumericRange(field) &&
                             field.Value is not null &&
                             IsValidNumericSerializedValue(
                                 target,
                                 FormatRuntimeNumericValue(field.Value, target)))
                    {
                        componentNumericDragFieldCount++;
                    }
                }
                else if (field.CanWrite && field.Kind == ScriptFieldKind.Vector)
                {
                    componentVectorDragFieldCount++;
                }

                DrawRuntimeField(entity.Handle, i, field);
            }

            EndInspectorPropertyTable();
        }

        _runtimeInspectorProbe = new ScriptedRuntimeInspectorProbeSnapshot(
            entity.Handle,
            EntityResolved: true,
            transformTableRendered,
            componentHeaderCount,
            componentPropertyTableCount,
            componentNumericDragFieldCount,
            componentVectorDragFieldCount,
            componentDecimalFieldCount,
            RenderRevision: ++_runtimeInspectorRenderRevision);
    }

    [EditorUiCommands("panel.inspector.runtime.transform")]
    private bool DrawRuntimeTransform(ScriptEntityInspection entity)
    {
        Transform transform = entity.Transform!;
        float availableWidth = ImGui.GetContentRegionAvail().X;
        TransformFieldLayout layout = ResolveTransformFieldLayout(availableWidth);
        bool inlineAxes = layout == TransformFieldLayout.InlineAxes;
        int columnCount = inlineAxes ? 5 : 3;
        if (!BeginInspectorPropertyTable(
            inlineAxes ? "runtime-transform-fields-inline" : "runtime-transform-fields-stacked",
            columnCount))
        {
            return false;
        }

        float axisWidth = MathF.Max(18f, ImGui.GetTextLineHeight() + 4f);
        ImGui.TableSetupColumn(L.Get("settings.property", "Property"), ImGuiTableColumnFlags.WidthFixed, ResolveInspectorLabelWidth(availableWidth));
        ImGui.TableSetupColumn("AxisA", ImGuiTableColumnFlags.WidthFixed, axisWidth);
        ImGui.TableSetupColumn("ValueA", ImGuiTableColumnFlags.WidthStretch);
        if (inlineAxes)
        {
            ImGui.TableSetupColumn("AxisB", ImGuiTableColumnFlags.WidthFixed, axisWidth);
            ImGui.TableSetupColumn("ValueB", ImGuiTableColumnFlags.WidthStretch);
        }

        float x = transform.X;
        float y = transform.Y;
        float rotationDegrees = RadiansToDegrees(transform.RotationRadians);
        float scaleX = transform.ScaleX;
        float scaleY = transform.ScaleY;

        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        DrawPropertyLabel(L.Get("inspector.transform.position", "Position"));
        _ = ImGui.TableSetColumnIndex(1);
        DrawAxisLabel("X", InspectorAxis.X);
        _ = ImGui.TableSetColumnIndex(2);
        ImGui.SetNextItemWidth(-1f);
        bool anyChanged = ImGui.DragFloat("##runtime-position-x", ref x, 0.25f, "%g");
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
        anyChanged |= ImGui.DragFloat("##runtime-position-y", ref y, 0.25f, "%g");
        DrawTransformDragTooltip();

        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        DrawPropertyLabel(L.Get("inspector.transform.rotation", "Rotation"));
        _ = ImGui.TableSetColumnIndex(1);
        DrawAxisLabel("Z", InspectorAxis.Z);
        _ = ImGui.TableSetColumnIndex(2);
        ImGui.SetNextItemWidth(-1f);
        anyChanged |= ImGui.DragFloat("##runtime-rotation-z", ref rotationDegrees, 0.5f, "%g");
        DrawTransformDragTooltip();

        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        DrawPropertyLabel(L.Get("inspector.transform.scale", "Scale"));
        _ = ImGui.TableSetColumnIndex(1);
        DrawAxisLabel("X", InspectorAxis.X);
        _ = ImGui.TableSetColumnIndex(2);
        ImGui.SetNextItemWidth(-1f);
        anyChanged |= ImGui.DragFloat("##runtime-scale-x", ref scaleX, 0.01f, "%g");
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
        anyChanged |= ImGui.DragFloat("##runtime-scale-y", ref scaleY, 0.01f, "%g");
        DrawTransformDragTooltip();
        EndInspectorPropertyTable();

        if (anyChanged)
        {
            _ = _runtimeSource!.TrySetEntityTransform(
                entity.Handle,
                x,
                y,
                DegreesToRadians(rotationDegrees),
                scaleX,
                scaleY);
        }

        return true;
    }

    [EditorUiCommands("panel.inspector.runtime.field")]
    private void DrawRuntimeField(string handle, int componentIndex, ScriptFieldDescriptor field)
    {
        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        DrawPropertyLabel(field.Name, disabled: !field.CanWrite || field.Kind == ScriptFieldKind.Unsupported);
        if (ImGui.IsItemHovered())
        {
            SetTooltipUnformatted(field.Name);
        }

        _ = ImGui.TableSetColumnIndex(1);
        string id = $"##runtime_{handle}_{componentIndex}_{field.Name}";
        if (!field.CanWrite || field.Kind == ScriptFieldKind.Unsupported)
        {
            TextWrappedUnformatted(FormatRuntimeFieldValue(field.Value));
            return;
        }

        switch (field.Kind)
        {
            case ScriptFieldKind.Boolean:
                {
                    bool value = field.Value is bool current && current;
                    if (ImGui.Checkbox(id, ref value))
                    {
                        _ = _runtimeSource!.TrySetBehaviourField(handle, componentIndex, field.Name, value);
                    }

                    break;
                }
            case ScriptFieldKind.Number:
                DrawRuntimeNumber(handle, componentIndex, field, id);
                break;
            case ScriptFieldKind.String:
                {
                    string value = field.Value?.ToString() ?? string.Empty;
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.InputText(id, ref value, 256))
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
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.Combo(id, ref index, names, names.Length) && (uint)index < (uint)names.Length)
                    {
                        object value = Enum.Parse(enumType, names[index]);
                        _ = _runtimeSource!.TrySetBehaviourField(handle, componentIndex, field.Name, value);
                    }

                    break;
                }
            case ScriptFieldKind.Vector:
                DrawRuntimeVector(handle, componentIndex, field);
                break;
            case ScriptFieldKind.Material:
            case ScriptFieldKind.AssetReference:
            case ScriptFieldKind.Unsupported:
            default:
                TextWrappedUnformatted(FormatRuntimeFieldValue(field.Value));
                break;
        }
    }

    [EditorUiControlPrimitive]
    private void DrawRuntimeNumber(
        string handle,
        int componentIndex,
        ScriptFieldDescriptor field,
        string id)
    {
        Type target = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        if (!HasValidNumericRange(field))
        {
            TextColoredUnformatted(
                new Vector4(0.95f, 0.55f, 0.35f, 1f),
                L.Format(
                    "inspector.number.rangeUnsupported",
                    "The declared Range contains no value representable by {0}",
                    target.Name));
            return;
        }

        if (target == typeof(decimal))
        {
            DrawRuntimeDecimalNumber(handle, componentIndex, field, id);
            return;
        }

        if (field.Value is null)
        {
            if (Nullable.GetUnderlyingType(field.FieldType) is not null &&
                ImGui.Button($"{L.Get("inspector.number.setZero", "null · Set 0")}{id}"))
            {
                if (TryConvertRuntimeSerializedNumber("0", field.FieldType, out object? zero))
                {
                    _ = _runtimeSource!.TrySetBehaviourField(handle, componentIndex, field.Name, zero);
                }
            }

            return;
        }

        string current = FormatRuntimeNumericValue(field.Value, target);
        if (!IsValidNumericSerializedValue(target, current))
        {
            TextColoredUnformatted(
                new Vector4(0.95f, 0.55f, 0.35f, 1f),
                L.Format("inspector.number.invalid", "Invalid {0}: {1}", target.Name, current));
            return;
        }

        ImGui.SetNextItemWidth(-1f);
        if (TryDrawNumericField(id, field, current, out string serialized) &&
            TryConvertRuntimeSerializedNumber(serialized, field.FieldType, out object? converted))
        {
            _ = _runtimeSource!.TrySetBehaviourField(handle, componentIndex, field.Name, converted);
        }

        DrawComponentDragTooltip();
    }

    [EditorUiControlPrimitive]
    private void DrawRuntimeDecimalNumber(
        string handle,
        int componentIndex,
        ScriptFieldDescriptor field,
        string id)
    {
        RuntimeDecimalFieldTextEditState? state =
            _runtimeDecimalFieldTextEdit is { } existing &&
            existing.HasKey(handle, componentIndex, field.Name)
                ? existing
                : null;
        string current = field.Value is null
            ? string.Empty
            : FormatRuntimeNumericValue(field.Value, typeof(decimal));
        string edited = state?.Text ?? current;
        ImGui.SetNextItemWidth(-1f);
        bool submitted = ImGui.InputText(
            id,
            ref edited,
            128,
            ImGuiInputTextFlags.EnterReturnsTrue);
        if (ImGui.IsItemActivated() && state is null)
        {
            _ = CommitPendingRuntimeDecimalFieldEdit();
            state = _runtimeDecimalFieldTextEdit = new RuntimeDecimalFieldTextEditState(
                handle,
                componentIndex,
                field,
                current);
        }

        state?.Update(edited);
        if (state is not null && (submitted || ImGui.IsItemDeactivated()))
        {
            _ = CommitPendingRuntimeDecimalFieldEdit();
        }

        if (ImGui.IsItemHovered())
        {
            SetTooltipUnformatted(L.Get(
                "inspector.decimal.tooltip",
                "decimal uses exact text editing. Press Enter or leave the field to commit; intermediate input is preserved."));
        }
    }

    [EditorUiControlPrimitive]
    private void DrawRuntimeVector(string handle, int componentIndex, ScriptFieldDescriptor field)
    {
        Type target = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        Span<float> values = stackalloc float[4];
        int count;
        switch (field.Value)
        {
            case Vector2 vector2 when target == typeof(Vector2):
                values[0] = vector2.X;
                values[1] = vector2.Y;
                count = 2;
                break;
            case Vector3 vector3 when target == typeof(Vector3):
                values[0] = vector3.X;
                values[1] = vector3.Y;
                values[2] = vector3.Z;
                count = 3;
                break;
            case Vector4 vector4 when target == typeof(Vector4):
                values[0] = vector4.X;
                values[1] = vector4.Y;
                values[2] = vector4.Z;
                values[3] = vector4.W;
                count = 4;
                break;
            case null when Nullable.GetUnderlyingType(field.FieldType) is not null:
                if (ImGui.Button($"{L.Get("inspector.vector.setZero", "null · Set Zero")}##runtime-vector-null-{handle}-{componentIndex}-{field.Name}") &&
                    TryCreateRuntimeVector(target, values[..ResolveVectorComponentCount(target)], out object? zero))
                {
                    _ = _runtimeSource!.TrySetBehaviourField(handle, componentIndex, field.Name, zero);
                }

                return;
            default:
                TextColoredUnformatted(
                    new Vector4(0.95f, 0.55f, 0.35f, 1f),
                    L.Format(
                        "inspector.number.invalid",
                        "Invalid {0}: {1}",
                        target.Name,
                        FormatRuntimeFieldValue(field.Value)));
                return;
        }

        DrawRuntimeVectorComponents(handle, componentIndex, field, values[..count]);
    }

    [EditorUiControlPrimitive]
    private void DrawRuntimeVectorComponents(
        string handle,
        int componentIndex,
        ScriptFieldDescriptor field,
        Span<float> values)
    {
        VectorFieldLayout layout = ResolveVectorFieldLayout(ImGui.GetContentRegionAvail().X, values.Length);
        bool inline = layout == VectorFieldLayout.InlineAxes;
        int columnCount = inline ? values.Length * 2 : 2;
        if (!ImGui.BeginTable(
            $"runtime-vector-field-{handle}-{componentIndex}-{field.Name}",
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

        Type target = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        for (int i = 0; i < values.Length; i++)
        {
            if (!inline || i == 0)
            {
                ImGui.TableNextRow();
            }

            int axisColumn = inline ? i * 2 : 0;
            _ = ImGui.TableSetColumnIndex(axisColumn);
            InspectorAxis axis = (InspectorAxis)i;
            DrawAxisLabel(axis.ToString(), axis);
            _ = ImGui.TableSetColumnIndex(axisColumn + 1);
            ImGui.SetNextItemWidth(-1f);
            float value = values[i];
            bool changed = ImGui.DragFloat(
                $"##runtime-vector-{handle}-{componentIndex}-{field.Name}-{i}",
                ref value,
                0.1f,
                "%g");
            if (changed && float.IsFinite(value))
            {
                values[i] = value;
                if (TryCreateRuntimeVector(target, values, out object? vector))
                {
                    _ = _runtimeSource!.TrySetBehaviourField(handle, componentIndex, field.Name, vector);
                }
            }

            DrawComponentDragTooltip();
        }

        ImGui.EndTable();
    }

    [EditorUiCommands("panel.inspector.runtime-body")]
    private void DrawRuntimeBodyInspector(int bodyKey)
    {
        if (_runtimeSource is null || !_runtimeSource.TryGetBody(bodyKey, out RigidBodySnapshot body))
        {
            ImGui.TextUnformatted(L.Get("inspector.runtime.bodyUnavailable", "Runtime body is no longer available"));
            return;
        }

        ImGui.TextUnformatted(L.Format("inspector.runtime.body", "Body {0}", body.BodyKey));
        ImGui.SeparatorText(L.Get("inspector.runtime.transformReadOnly", "Transform (Runtime, read-only)"));
        ImGui.TextUnformatted(L.Format(
            "inspector.runtime.position",
            "Position: {0}, {1}",
            body.Transform.Position.X.ToString("0.###", CultureInfo.InvariantCulture),
            body.Transform.Position.Y.ToString("0.###", CultureInfo.InvariantCulture)));
        ImGui.TextUnformatted(L.Format(
            "inspector.runtime.rotation",
            "Rotation: sin={0}, cos={1}",
            body.Transform.Sin.ToString("0.###", CultureInfo.InvariantCulture),
            body.Transform.Cos.ToString("0.###", CultureInfo.InvariantCulture)));
        ImGui.TextUnformatted(L.Format(
            "inspector.runtime.linearVelocity",
            "Linear velocity: {0}, {1}",
            body.LinearVelocityPixelsPerSecond.X.ToString("0.###", CultureInfo.InvariantCulture),
            body.LinearVelocityPixelsPerSecond.Y.ToString("0.###", CultureInfo.InvariantCulture)));
        ImGui.TextUnformatted(L.Format(
            "inspector.runtime.angularVelocity",
            "Angular velocity: {0}",
            body.AngularVelocityRadiansPerSecond.ToString("0.###", CultureInfo.InvariantCulture)));
        ImGui.TextUnformatted(L.Format(
            "inspector.runtime.mask",
            "Mask: {0}×{1} · {2} pixels",
            body.Mask.Width,
            body.Mask.Height,
            body.Mask.SolidPixelCount));
        TextWrappedUnformatted(L.Get(
            "inspector.runtime.bodyReadOnly",
            "Rigid body editing requires a Physics phase-safe command and is intentionally read-only here."));
    }

    private static bool HasValidNumericRange(ScriptFieldDescriptor field)
    {
        Type target = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        return (!IsIntegerNumericType(target) ||
                TryResolveIntegerFieldRange(target, field, out _, out _)) &&
            ((target != typeof(float) && target != typeof(double)) ||
             TryResolveFloatingFieldRange(target, field, out _, out _)) &&
            (target != typeof(decimal) ||
             TryResolveDecimalFieldRange(field, out _, out _));
    }

    private static string FormatRuntimeNumericValue(object value, Type target)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(target);
        return !target.IsInstanceOfType(value)
            ? value.ToString() ?? string.Empty
            : Type.GetTypeCode(target) switch
            {
                TypeCode.Single => ((float)value).ToString("R", CultureInfo.InvariantCulture),
                TypeCode.Double => ((double)value).ToString("R", CultureInfo.InvariantCulture),
                TypeCode.Decimal => ((decimal)value).ToString(CultureInfo.InvariantCulture),
                TypeCode.Byte or
                TypeCode.SByte or
                TypeCode.Int16 or
                TypeCode.UInt16 or
                TypeCode.Int32 or
                TypeCode.UInt32 or
                TypeCode.Int64 or
                TypeCode.UInt64 => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
                TypeCode.Empty or
                TypeCode.Object or
                TypeCode.DBNull or
                TypeCode.Boolean or
                TypeCode.Char or
                TypeCode.DateTime or
                TypeCode.String => value.ToString() ?? string.Empty,
                _ => value.ToString() ?? string.Empty,
            };
    }

    internal static bool TryConvertRuntimeSerializedNumber(
        string? serialized,
        Type destinationType,
        out object? converted)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        Type? nullable = Nullable.GetUnderlyingType(destinationType);
        if (string.IsNullOrEmpty(serialized))
        {
            converted = null;
            return nullable is not null;
        }

        Type target = nullable ?? destinationType;
        bool success;
        switch (Type.GetTypeCode(target))
        {
            case TypeCode.Byte:
                success = byte.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte byteValue);
                converted = byteValue;
                break;
            case TypeCode.SByte:
                success = sbyte.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte sbyteValue);
                converted = sbyteValue;
                break;
            case TypeCode.Int16:
                success = short.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out short int16Value);
                converted = int16Value;
                break;
            case TypeCode.UInt16:
                success = ushort.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort uint16Value);
                converted = uint16Value;
                break;
            case TypeCode.Int32:
                success = int.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int int32Value);
                converted = int32Value;
                break;
            case TypeCode.UInt32:
                success = uint.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uint32Value);
                converted = uint32Value;
                break;
            case TypeCode.Int64:
                success = long.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out long int64Value);
                converted = int64Value;
                break;
            case TypeCode.UInt64:
                success = ulong.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong uint64Value);
                converted = uint64Value;
                break;
            case TypeCode.Single:
                success = float.TryParse(serialized, NumberStyles.Float, CultureInfo.InvariantCulture, out float singleValue) &&
                    float.IsFinite(singleValue);
                converted = singleValue;
                break;
            case TypeCode.Double:
                success = double.TryParse(serialized, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue) &&
                    double.IsFinite(doubleValue);
                converted = doubleValue;
                break;
            case TypeCode.Decimal:
                success = decimal.TryParse(serialized, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal decimalValue);
                converted = decimalValue;
                break;
            case TypeCode.Empty:
            case TypeCode.Object:
            case TypeCode.DBNull:
            case TypeCode.Boolean:
            case TypeCode.Char:
            case TypeCode.DateTime:
            case TypeCode.String:
            default:
                success = false;
                converted = null;
                break;
        }

        if (!success)
        {
            converted = null;
        }

        return success;
    }

    private static int ResolveVectorComponentCount(Type target)
    {
        return target == typeof(Vector2)
            ? 2
            : target == typeof(Vector3)
                ? 3
                : target == typeof(Vector4)
                    ? 4
                    : throw new ArgumentOutOfRangeException(
                        nameof(target),
                        target,
                        "Runtime Inspector 仅支持 Vector2/3/4。");
    }

    internal static bool TryCreateRuntimeVector(
        Type target,
        ReadOnlySpan<float> components,
        out object? vector)
    {
        ArgumentNullException.ThrowIfNull(target);
        int expectedCount;
        try
        {
            expectedCount = ResolveVectorComponentCount(Nullable.GetUnderlyingType(target) ?? target);
        }
        catch (ArgumentOutOfRangeException)
        {
            vector = null;
            return false;
        }

        if (components.Length != expectedCount)
        {
            vector = null;
            return false;
        }

        for (int i = 0; i < components.Length; i++)
        {
            if (!float.IsFinite(components[i]))
            {
                vector = null;
                return false;
            }
        }

        Type normalized = Nullable.GetUnderlyingType(target) ?? target;
        vector = normalized == typeof(Vector2)
            ? new Vector2(components[0], components[1])
            : normalized == typeof(Vector3)
                ? new Vector3(components[0], components[1], components[2])
                : new Vector4(components[0], components[1], components[2], components[3]);
        return true;
    }

    private static string FormatRuntimeFieldValue(object? value)
    {
        return value switch
        {
            null => "null",
            Vector2 vector2 => SerializedFieldValueCodec.Format(vector2),
            Vector3 vector3 => SerializedFieldValueCodec.Format(vector3),
            Vector4 vector4 => SerializedFieldValueCodec.Format(vector4),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    private void CommitRuntimeDecimalFieldEditIfMatches(string handle, int componentIndex)
    {
        if (_runtimeDecimalFieldTextEdit is { } edit &&
            string.Equals(edit.EntityHandle, handle, StringComparison.Ordinal) &&
            edit.ComponentIndex == componentIndex)
        {
            _ = CommitPendingRuntimeDecimalFieldEdit();
        }
    }

    private bool CommitPendingRuntimeDecimalFieldEdit()
    {
        if (_runtimeDecimalFieldTextEdit is not { } state)
        {
            return false;
        }

        _runtimeDecimalFieldTextEdit = null;
        if (!state.Dirty)
        {
            return false;
        }

        if (!TryNormalizeDecimalFieldValue(state.Field, state.Text, out string? serialized) ||
            !TryConvertRuntimeSerializedNumber(serialized, state.Field.FieldType, out object? converted))
        {
            _statusMessage = L.Format(
                "inspector.decimal.runtimeInvalid",
                "Invalid runtime decimal: {0}",
                state.Text.Length == 0 ? L.Get("inspector.value.empty", "<empty>") : state.Text);
            return false;
        }

        bool applied = _runtimeSource?.TrySetBehaviourField(
            state.EntityHandle,
            state.ComponentIndex,
            state.Field.Name,
            converted) == true;
        _statusMessage = applied
            ? string.Empty
            : L.Format(
                "inspector.runtime.fieldExpired",
                "Runtime field is no longer available: {0}",
                state.Field.Name);
        return applied;
    }

    [EditorUiCommands(
        "panel.inspector.enabled",
        "panel.inspector.rename",
        "panel.inspector.prefab.revert-overrides")]
    private void DrawHeader(EditorGameObject gameObject)
    {
        bool enabled = gameObject.Enabled;
        if (ImGui.Checkbox("##gameobject-active", ref enabled) && enabled != gameObject.Enabled)
        {
            _undo.Execute(_scene, new SetGameObjectEnabledCommand(gameObject.StableId, enabled));
        }

        if (ImGui.IsItemHovered())
        {
            SetTooltipUnformatted(enabled
                ? L.Get("inspector.gameObject.active", "GameObject active")
                : L.Get("inspector.gameObject.inactive", "GameObject inactive"));
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

        TextDisabledUnformatted(L.Format(
            "inspector.gameObject.identity",
            "2D GameObject · ID {0}",
            gameObject.StableId));
        if (gameObject.PrefabLink?.AssetPath is { Length: > 0 } prefab)
        {
            TextColoredUnformatted(
                new Vector4(0.45f, 0.72f, 1f, 1f),
                L.Format("inspector.prefab.source", "Prefab · {0}", prefab));
            ImGui.SameLine();
            TextDisabledUnformatted(L.Format(
                "inspector.prefab.overrides",
                "{0} overrides",
                gameObject.PrefabLink.Overrides.Count));
            if (gameObject.PrefabLink.Overrides.Count != 0 &&
                ImGui.Button(L.Get("inspector.prefab.revertOverrides", "Revert Overrides")))
            {
                _undo.Execute(_scene, new RevertPrefabOverridesCommand(gameObject.StableId));
            }
        }

        ImGui.Separator();
    }

    [EditorUiCommands("panel.inspector.transform")]
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
        ImGui.TableSetupColumn(L.Get("settings.property", "Property"), ImGuiTableColumnFlags.WidthFixed, ResolveInspectorLabelWidth(availableWidth));
        ImGui.TableSetupColumn("AxisA", ImGuiTableColumnFlags.WidthFixed, axisWidth);
        ImGui.TableSetupColumn("ValueA", ImGuiTableColumnFlags.WidthStretch);
        if (inlineAxes)
        {
            ImGui.TableSetupColumn("AxisB", ImGuiTableColumnFlags.WidthFixed, axisWidth);
            ImGui.TableSetupColumn("ValueB", ImGuiTableColumnFlags.WidthStretch);
        }

        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        DrawPropertyLabel(L.Get("inspector.transform.position", "Position"));
        _ = ImGui.TableSetColumnIndex(1);
        DrawAxisLabel("X", InspectorAxis.X);
        _ = ImGui.TableSetColumnIndex(2);
        float x = transform.X;
        float y = transform.Y;
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.DragFloat("##position-x", ref x, 0.25f, "%g");
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
        changed = ImGui.DragFloat("##position-y", ref y, 0.25f, "%g");
        if (changed)
        {
            transform.Y = y;
        }
        HandleTransformInput(gameObject, transform, changed);
        DrawTransformDragTooltip();

        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        DrawPropertyLabel(L.Get("inspector.transform.rotation", "Rotation"));
        _ = ImGui.TableSetColumnIndex(1);
        DrawAxisLabel("Z", InspectorAxis.Z);
        _ = ImGui.TableSetColumnIndex(2);
        float rotation = RadiansToDegrees(transform.RotationRadians);
        ImGui.SetNextItemWidth(-1f);
        changed = ImGui.DragFloat("##rotation-z", ref rotation, 0.5f, "%g");
        if (changed)
        {
            transform.RotationRadians = DegreesToRadians(rotation);
        }
        HandleTransformInput(gameObject, transform, changed);
        DrawTransformDragTooltip();

        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        DrawPropertyLabel(L.Get("inspector.transform.scale", "Scale"));
        _ = ImGui.TableSetColumnIndex(1);
        DrawAxisLabel("X", InspectorAxis.X);
        _ = ImGui.TableSetColumnIndex(2);
        float scaleX = transform.ScaleX;
        float scaleY = transform.ScaleY;
        ImGui.SetNextItemWidth(-1f);
        changed = ImGui.DragFloat("##scale-x", ref scaleX, 0.01f, "%g");
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
        changed = ImGui.DragFloat("##scale-y", ref scaleY, 0.01f, "%g");
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
            SetTooltipUnformatted(L.Get(
                "inspector.transform.dragTooltip",
                "Drag horizontally to adjust quickly; Ctrl+click for exact input."));
        }
    }

    private static void DrawAxisLabel(string label, InspectorAxis axis)
    {
        Vector4 color = GetAxisColor(axis);
        ImGui.TableSetBgColor(
            ImGuiTableBgTarget.CellBg,
            ImGui.GetColorU32(new Vector4(color.X * 0.22f, color.Y * 0.22f, color.Z * 0.22f, 1f)));
        TextColoredUnformatted(color, label);
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

    [EditorUiControlPrimitive]
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
        if (!CanModifyAuthoringScene())
        {
            return false;
        }

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

    private EditorMode CaptureMode()
    {
        return _modeProvider?.Invoke() ?? EditorMode.Edit;
    }

    private bool CanModifyAuthoringScene()
    {
        return CaptureMode() == EditorMode.Edit;
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
        CommitPendingBuiltInCanvasEdit();
        _ = CommitPendingRuntimeDecimalFieldEdit();
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

    [EditorUiControlPrimitive]
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

    [EditorUiCommands("panel.inspector.add-component")]
    private void DrawComponents(EditorGameObject gameObject)
    {
        // 遍历已有组件并提供 Unity 式 Add Component 搜索弹层。
        DrawBuiltInCanvasComponents(gameObject);
        for (int i = 0; i < gameObject.Components.Count; i++)
        {
            DrawComponent(gameObject, i);
        }

        ImGui.Spacing();
        float available = ImGui.GetContentRegionAvail().X;
        float addWidth = MathF.Min(220f, available);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (available - addWidth) * 0.5f));
        if (ImGui.Button(L.Get("inspector.component.add", "Add Component"), new Vector2(addWidth, 0f)))
        {
            _componentSearch = string.Empty;
            ImGui.OpenPopup("add-component-popup");
        }

        if (ImGui.BeginPopup("add-component-popup"))
        {
            ImGui.SetNextItemWidth(280f);
            _ = ImGui.InputTextWithHint(
                "##component-search",
                L.Get("inspector.component.search", "Search components"),
                ref _componentSearch,
                128);
            ImGui.Separator();
            bool hasBuiltInMatch = false;
            string webCanvasLabel = L.Get("inspector.canvas.web", "Canvas (Web)");
            bool webCanvasMatches = MatchesComponentSearch(webCanvasLabel) ||
                (!string.Equals(webCanvasLabel, "Canvas (Web)", StringComparison.Ordinal) &&
                 MatchesComponentSearch("Canvas (Web)"));
            if (gameObject.WebCanvas is null && webCanvasMatches)
            {
                hasBuiltInMatch = true;
                if (ImGui.Selectable($"{webCanvasLabel}##add-built-in-web-canvas"))
                {
                    _undo.Execute(
                        _scene,
                        new SetBuiltInCanvasComponentsCommand(
                            gameObject.StableId,
                            CreateDefaultWebCanvas(makePrimary: !HasExplicitWebCanvas()),
                            gameObject.CanvasScaler));
                    _componentSearch = string.Empty;
                    ImGui.CloseCurrentPopup();
                }
            }

            string canvasScalerLabel = L.Get("inspector.canvasScaler", "Canvas Scaler");
            bool canvasScalerMatches = MatchesComponentSearch(canvasScalerLabel) ||
                (!string.Equals(canvasScalerLabel, "Canvas Scaler", StringComparison.Ordinal) &&
                 MatchesComponentSearch("Canvas Scaler"));
            if (gameObject.CanvasScaler is null && canvasScalerMatches)
            {
                hasBuiltInMatch = true;
                if (ImGui.Selectable($"{canvasScalerLabel}##add-built-in-canvas-scaler"))
                {
                    _undo.Execute(
                        _scene,
                        new SetBuiltInCanvasComponentsCommand(
                            gameObject.StableId,
                            gameObject.WebCanvas,
                            new EditorCanvasScalerComponent()));
                    _componentSearch = string.Empty;
                    ImGui.CloseCurrentPopup();
                }
            }

            if (hasBuiltInMatch)
            {
                ImGui.Separator();
            }

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
                    SetTooltipUnformatted(label);
                }
            }

            if (behaviours.Length == 0 && !hasBuiltInMatch)
            {
                TextDisabledUnformatted(L.Get("inspector.component.noMatch", "No matching Behaviour"));
            }

            ImGui.EndPopup();
        }
    }

    [EditorUiControlPrimitive]
    private void DrawBuiltInCanvasComponents(EditorGameObject gameObject)
    {
        CanvasInspectorSnapshot snapshot = CaptureCanvasInspector(gameObject.StableId);
        if (!snapshot.HasExplicitCanvases && gameObject.WebCanvas is null)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.14f, 0.18f, 1f));
            _ = ImGui.BeginChild(
                "legacy-implicit-canvas",
                new Vector2(0f, 104f),
                ImGuiChildFlags.Borders,
                ImGuiWindowFlags.NoScrollbar);
            TextColoredUnformatted(
                new Vector4(0.45f, 0.72f, 1f, 1f),
                L.Get("inspector.canvas.legacyTitle", "Legacy implicit Web Canvas"));
            TextWrappedUnformatted(L.Get(
                "inspector.canvas.legacyHelp",
                "This older scene uses a non-persistent primary Canvas. The scene will not be rewritten until you convert it."));
            if (ImGui.Button(
                L.Get("inspector.canvas.convert", "Convert To Canvas (Web)"),
                new Vector2(-1f, 0f)))
            {
                _undo.Execute(
                    _scene,
                    new SetBuiltInCanvasComponentsCommand(
                        gameObject.StableId,
                        CreateDefaultWebCanvas(makePrimary: true),
                        new EditorCanvasScalerComponent()));
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        if (gameObject.WebCanvas is not null)
        {
            DrawWebCanvasComponent(gameObject, snapshot);
        }

        if (gameObject.CanvasScaler is not null)
        {
            DrawCanvasScalerComponent(gameObject, snapshot);
        }
    }

    [EditorUiCommands(
        "panel.inspector.canvas",
        "panel.inspector.canvas.primary",
        "context.inspector.canvas.reset",
        "context.inspector.canvas.remove")]
    private void DrawWebCanvasComponent(EditorGameObject gameObject, CanvasInspectorSnapshot snapshot)
    {
        bool open = DrawInspectorComponentHeader(
            $"{L.Get("inspector.canvas.web", "Canvas (Web)")}##built-in-web-canvas");
        bool reset = false;
        bool remove = false;
        if (ImGui.BeginPopupContextItem("built-in-web-canvas-context"))
        {
            reset = ImGui.MenuItem(L.Get("inspector.action.reset", "Reset"));
            remove = ImGui.MenuItem(L.Get("inspector.component.remove", "Remove Component"));
            ImGui.EndPopup();
        }

        if (reset)
        {
            _undo.Execute(
                _scene,
                new SetBuiltInCanvasComponentsCommand(
                    gameObject.StableId,
                    CreateDefaultWebCanvas(gameObject.WebCanvas!.Primary),
                    gameObject.CanvasScaler));
            return;
        }

        if (remove)
        {
            _undo.Execute(
                _scene,
                new SetBuiltInCanvasComponentsCommand(gameObject.StableId, null, gameObject.CanvasScaler));
            return;
        }

        if (!open)
        {
            CommitPendingBuiltInCanvasEdit();
            return;
        }

        EditorWebCanvasComponent webCanvas = gameObject.WebCanvas!.Clone();
        EditorCanvasScalerComponent? scaler = gameObject.CanvasScaler?.Clone();
        float availableWidth = ImGui.GetContentRegionAvail().X;
        if (!BeginInspectorPropertyTable("built-in-web-canvas-fields", 2))
        {
            return;
        }

        SetupInspectorPropertyColumns(availableWidth);
        bool enabled = webCanvas.Enabled;
        bool changed = DrawBooleanProperty(
            L.Get("inspector.component.enabled", "Enabled"),
            "##web-canvas-enabled",
            ref enabled);
        if (changed)
        {
            webCanvas.Enabled = enabled;
            _undo.Execute(
                _scene,
                new SetBuiltInCanvasComponentsCommand(gameObject.StableId, webCanvas, scaler));
        }

        string manifestAssetId = webCanvas.ManifestAssetId ?? string.Empty;
        changed = DrawTextProperty(
            L.Get("inspector.canvas.manifestAssetId", "Manifest Asset ID"),
            "##web-canvas-manifest-id",
            ref manifestAssetId,
            512);
        if (changed)
        {
            webCanvas.ManifestAssetId = NormalizeOptionalText(manifestAssetId);
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);

        string manifestPath = webCanvas.ManifestPath ?? string.Empty;
        changed = DrawTextProperty(
            L.Get("inspector.canvas.manifestPath", "Manifest Path"),
            "##web-canvas-manifest-path",
            ref manifestPath,
            512);
        if (changed)
        {
            webCanvas.ManifestPath = NormalizeOptionalText(manifestPath);
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);

        string initialScreen = webCanvas.InitialScreenId ?? string.Empty;
        changed = DrawTextProperty(
            L.Get("inspector.canvas.initialScreen", "Initial Screen"),
            "##web-canvas-initial-screen",
            ref initialScreen,
            256);
        if (changed)
        {
            webCanvas.InitialScreenId = NormalizeOptionalText(initialScreen);
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);

        int sortingOrder = webCanvas.SortingOrder;
        changed = DrawIntegerProperty(
            L.Get("inspector.canvas.sortingOrder", "Sorting Order"),
            "##web-canvas-sorting-order",
            ref sortingOrder,
            1f);
        if (changed)
        {
            webCanvas.SortingOrder = sortingOrder;
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);

        bool primary = webCanvas.Primary;
        changed = DrawBooleanProperty(
            L.Get("inspector.canvas.primary", "Primary"),
            "##web-canvas-primary",
            ref primary);
        if (changed)
        {
            CommitPendingBuiltInCanvasEdit();
            if (primary)
            {
                _undo.Execute(_scene, new SetPrimaryWebCanvasCommand(gameObject.StableId));
            }
            else
            {
                webCanvas.Primary = false;
                _undo.Execute(
                    _scene,
                    new SetBuiltInCanvasComponentsCommand(gameObject.StableId, webCanvas, scaler));
            }
        }

        DrawReadOnlyProperty(L.Get("inspector.canvas.derivedId", "Derived Canvas ID"), snapshot.DerivedCanvasId == 0
            ? L.Get("inspector.none", "None")
            : $"0x{snapshot.DerivedCanvasId:X16}");
        DrawReadOnlyProperty(
            L.Get("inspector.canvas.effectivePrimary", "Effective Primary"),
            snapshot.IsEffectivePrimary
                ? L.Get("inspector.value.yes", "Yes")
                : L.Get("inspector.value.no", "No"));
        DrawReadOnlyProperty(
            L.Get("inspector.canvas.runtimeState", "Runtime State"),
            snapshot.IsRuntimeEnabled
                ? L.Get("inspector.component.enabled", "Enabled")
                : L.Get("inspector.canvas.notMaterialized", "Not materialized"));
        EndInspectorPropertyTable();

        if (snapshot.UsesDefaultScaler)
        {
            if (ImGui.Button(
                L.Get("inspector.canvas.addScaler", "Add Canvas Scaler"),
                new Vector2(-1f, 0f)))
            {
                _undo.Execute(
                    _scene,
                    new SetBuiltInCanvasComponentsCommand(
                        gameObject.StableId,
                        gameObject.WebCanvas,
                        new EditorCanvasScalerComponent()));
            }
        }

        DrawCanvasSnapshotDiagnostic(snapshot);
    }

    [EditorUiCommands(
        "panel.inspector.canvas-scaler",
        "context.inspector.canvas-scaler.reset",
        "context.inspector.canvas-scaler.remove")]
    private void DrawCanvasScalerComponent(EditorGameObject gameObject, CanvasInspectorSnapshot snapshot)
    {
        bool open = DrawInspectorComponentHeader(
            $"{L.Get("inspector.canvasScaler", "Canvas Scaler")}##built-in-canvas-scaler");
        bool reset = false;
        bool remove = false;
        if (ImGui.BeginPopupContextItem("built-in-canvas-scaler-context"))
        {
            reset = ImGui.MenuItem(L.Get("inspector.action.reset", "Reset"));
            remove = ImGui.MenuItem(L.Get("inspector.component.remove", "Remove Component"));
            ImGui.EndPopup();
        }

        if (reset)
        {
            _undo.Execute(
                _scene,
                new SetBuiltInCanvasComponentsCommand(
                    gameObject.StableId,
                    gameObject.WebCanvas,
                    new EditorCanvasScalerComponent()));
            return;
        }

        if (remove)
        {
            _undo.Execute(
                _scene,
                new SetBuiltInCanvasComponentsCommand(gameObject.StableId, gameObject.WebCanvas, null));
            return;
        }

        if (!open)
        {
            CommitPendingBuiltInCanvasEdit();
            return;
        }

        EditorWebCanvasComponent? webCanvas = gameObject.WebCanvas?.Clone();
        EditorCanvasScalerComponent scaler = gameObject.CanvasScaler!.Clone();
        UiCanvasScalerSettings settings = scaler.Settings;
        float availableWidth = ImGui.GetContentRegionAvail().X;
        if (!BeginInspectorPropertyTable("built-in-canvas-scaler-fields", 2))
        {
            return;
        }

        SetupInspectorPropertyColumns(availableWidth);
        RefreshCanvasLocalizedOptions();
        int scaleMode = (int)settings.ScaleMode;
        bool changed = DrawComboProperty(
            L.Get("inspector.canvasScaler.scaleMode", "UI Scale Mode"),
            "##canvas-scaler-mode",
            ref scaleMode,
            _scaleModeLabels);
        if (changed)
        {
            settings = settings with { ScaleMode = (UiScaleMode)scaleMode };
            scaler.Settings = settings;
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);

        switch (settings.ScaleMode)
        {
            case UiScaleMode.ConstantPixelSize:
                DrawConstantPixelSizeSettings(gameObject, webCanvas, scaler);
                break;
            case UiScaleMode.ScaleWithScreenSize:
                DrawScaleWithScreenSizeSettings(gameObject, webCanvas, scaler);
                break;
            case UiScaleMode.ConstantPhysicalSize:
                DrawConstantPhysicalSizeSettings(gameObject, webCanvas, scaler);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings.ScaleMode, "未知 CanvasScaler 模式。");
        }

        EndInspectorPropertyTable();
        if (snapshot.IsOrphanScaler)
        {
            DrawCanvasDiagnostic(
                L.Get(
                    "inspector.canvasScaler.orphan",
                    "This Canvas Scaler has no Canvas (Web) on the same GameObject. It remains serialized but is currently inactive."),
                warning: true);
        }
    }

    private void DrawConstantPixelSizeSettings(
        EditorGameObject gameObject,
        EditorWebCanvasComponent? webCanvas,
        EditorCanvasScalerComponent scaler)
    {
        UiCanvasScalerSettings settings = scaler.Settings;
        float scaleFactor = settings.ScaleFactor;
        bool changed = DrawPositiveFloatProperty(
            L.Get("inspector.canvasScaler.scaleFactor", "Scale Factor"),
            "##canvas-scale-factor",
            ref scaleFactor,
            0.01f);
        if (changed)
        {
            scaler.Settings = settings with { ScaleFactor = scaleFactor };
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);
        DrawReferencePixelsPerUnit(gameObject, webCanvas, scaler);
    }

    private void DrawScaleWithScreenSizeSettings(
        EditorGameObject gameObject,
        EditorWebCanvasComponent? webCanvas,
        EditorCanvasScalerComponent scaler)
    {
        UiCanvasScalerSettings settings = scaler.Settings;
        float referenceWidth = settings.ReferenceWidth;
        bool changed = DrawPositiveFloatProperty(
            L.Get("inspector.canvasScaler.referenceWidth", "Reference Width"),
            "##canvas-reference-width",
            ref referenceWidth,
            1f);
        if (changed)
        {
            settings = settings with { ReferenceWidth = referenceWidth };
            scaler.Settings = settings;
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);

        float referenceHeight = settings.ReferenceHeight;
        changed = DrawPositiveFloatProperty(
            L.Get("inspector.canvasScaler.referenceHeight", "Reference Height"),
            "##canvas-reference-height",
            ref referenceHeight,
            1f);
        if (changed)
        {
            settings = settings with { ReferenceHeight = referenceHeight };
            scaler.Settings = settings;
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);

        int matchMode = (int)settings.ScreenMatchMode;
        changed = DrawComboProperty(
            L.Get("inspector.canvasScaler.screenMatchMode", "Screen Match Mode"),
            "##canvas-screen-match",
            ref matchMode,
            _screenMatchModeLabels);
        if (changed)
        {
            settings = settings with { ScreenMatchMode = (UiScreenMatchMode)matchMode };
            scaler.Settings = settings;
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);
        if (settings.ScreenMatchMode == UiScreenMatchMode.MatchWidthOrHeight)
        {
            float match = settings.MatchWidthOrHeight;
            changed = DrawUnitFloatProperty(
                L.Get("inspector.canvasScaler.match", "Match"),
                "##canvas-match-width-height",
                ref match);
            if (changed)
            {
                scaler.Settings = settings with { MatchWidthOrHeight = match };
            }

            HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);
        }

        DrawReferencePixelsPerUnit(gameObject, webCanvas, scaler);
    }

    private void DrawConstantPhysicalSizeSettings(
        EditorGameObject gameObject,
        EditorWebCanvasComponent? webCanvas,
        EditorCanvasScalerComponent scaler)
    {
        UiCanvasScalerSettings settings = scaler.Settings;
        int physicalUnit = (int)settings.PhysicalUnit;
        bool changed = DrawComboProperty(
            L.Get("inspector.canvasScaler.physicalUnit", "Physical Unit"),
            "##canvas-physical-unit",
            ref physicalUnit,
            _physicalUnitLabels);
        if (changed)
        {
            settings = settings with { PhysicalUnit = (UiPhysicalUnit)physicalUnit };
            scaler.Settings = settings;
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);

        float fallbackDpi = settings.FallbackScreenDpi;
        changed = DrawPositiveFloatProperty(
            L.Get("inspector.canvasScaler.fallbackDpi", "Fallback Screen DPI"),
            "##canvas-fallback-dpi",
            ref fallbackDpi,
            1f);
        if (changed)
        {
            settings = settings with { FallbackScreenDpi = fallbackDpi };
            scaler.Settings = settings;
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);

        float spriteDpi = settings.DefaultSpriteDpi;
        changed = DrawPositiveFloatProperty(
            L.Get("inspector.canvasScaler.defaultSpriteDpi", "Default Sprite DPI"),
            "##canvas-default-sprite-dpi",
            ref spriteDpi,
            1f);
        if (changed)
        {
            scaler.Settings = settings with { DefaultSpriteDpi = spriteDpi };
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);
        DrawReferencePixelsPerUnit(gameObject, webCanvas, scaler);
    }

    private void DrawReferencePixelsPerUnit(
        EditorGameObject gameObject,
        EditorWebCanvasComponent? webCanvas,
        EditorCanvasScalerComponent scaler)
    {
        UiCanvasScalerSettings settings = scaler.Settings;
        float referencePixelsPerUnit = settings.ReferencePixelsPerUnit;
        bool changed = DrawPositiveFloatProperty(
            L.Get("inspector.canvasScaler.referencePixelsPerUnit", "Reference Pixels Per Unit"),
            "##canvas-reference-ppu",
            ref referencePixelsPerUnit,
            1f);
        if (changed)
        {
            scaler.Settings = settings with { ReferencePixelsPerUnit = referencePixelsPerUnit };
        }

        HandleBuiltInCanvasInput(gameObject, webCanvas, scaler, changed);
    }

    private void RefreshCanvasLocalizedOptions()
    {
        string locale = L.CurrentLocale;
        if (string.Equals(_canvasOptionsLocale, locale, StringComparison.Ordinal))
        {
            return;
        }

        _canvasOptionsLocale = locale;
        _scaleModeLabels =
        [
            L.Get("inspector.canvasScaler.mode.constantPixel", "Constant Pixel Size"),
            L.Get("inspector.canvasScaler.mode.screen", "Scale With Screen Size"),
            L.Get("inspector.canvasScaler.mode.physical", "Constant Physical Size"),
        ];
        _screenMatchModeLabels =
        [
            L.Get("inspector.canvasScaler.matchMode.widthHeight", "Match Width Or Height"),
            L.Get("inspector.canvasScaler.matchMode.expand", "Expand"),
            L.Get("inspector.canvasScaler.matchMode.shrink", "Shrink"),
        ];
        _physicalUnitLabels =
        [
            L.Get("inspector.canvasScaler.unit.centimeters", "Centimeters"),
            L.Get("inspector.canvasScaler.unit.millimeters", "Millimeters"),
            L.Get("inspector.canvasScaler.unit.inches", "Inches"),
            L.Get("inspector.canvasScaler.unit.points", "Points"),
            L.Get("inspector.canvasScaler.unit.picas", "Picas"),
        ];
    }

    private static void SetupInspectorPropertyColumns(float availableWidth)
    {
        ImGui.TableSetupColumn(
            L.Get("settings.property", "Property"),
            ImGuiTableColumnFlags.WidthFixed,
            ResolveInspectorLabelWidth(availableWidth));
        ImGui.TableSetupColumn(L.Get("settings.value", "Value"), ImGuiTableColumnFlags.WidthStretch);
    }

    [EditorUiControlPrimitive]
    private static bool DrawBooleanProperty(string label, string id, ref bool value)
    {
        BeginPropertyRow(label);
        return ImGui.Checkbox(id, ref value);
    }

    [EditorUiControlPrimitive]
    private static bool DrawTextProperty(string label, string id, ref string value, uint capacity)
    {
        BeginPropertyRow(label);
        ImGui.SetNextItemWidth(-1f);
        return ImGui.InputText(id, ref value, capacity);
    }

    [EditorUiControlPrimitive]
    private static bool DrawIntegerProperty(string label, string id, ref int value, float speed)
    {
        BeginPropertyRow(label);
        ImGui.SetNextItemWidth(-1f);
        return ImGui.DragInt(id, ref value, speed);
    }

    [EditorUiControlPrimitive]
    private static bool DrawPositiveFloatProperty(string label, string id, ref float value, float speed)
    {
        BeginPropertyRow(label);
        ImGui.SetNextItemWidth(-1f);
        float candidate = value;
        bool changed = ImGui.DragFloat(id, ref candidate, speed, "%g");
        if (changed && float.IsFinite(candidate))
        {
            value = MathF.Max(0.0001f, candidate);
            return true;
        }

        return false;
    }

    [EditorUiControlPrimitive]
    private static bool DrawUnitFloatProperty(string label, string id, ref float value)
    {
        BeginPropertyRow(label);
        ImGui.SetNextItemWidth(-1f);
        float candidate = value;
        bool changed = ImGui.SliderFloat(id, ref candidate, 0f, 1f, "%.2f");
        if (changed && float.IsFinite(candidate))
        {
            value = Math.Clamp(candidate, 0f, 1f);
            return true;
        }

        return false;
    }

    [EditorUiControlPrimitive]
    private static bool DrawComboProperty(string label, string id, ref int value, string[] labels)
    {
        BeginPropertyRow(label);
        ImGui.SetNextItemWidth(-1f);
        return ImGui.Combo(id, ref value, labels, labels.Length);
    }

    private static void BeginPropertyRow(string label)
    {
        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        DrawPropertyLabel(label);
        _ = ImGui.TableSetColumnIndex(1);
    }

    private void DrawCanvasSnapshotDiagnostic(CanvasInspectorSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Diagnostic))
        {
            DrawCanvasDiagnostic(snapshot.Diagnostic, snapshot.HasConflict || snapshot.PrimaryNone);
        }
    }

    private static void DrawCanvasDiagnostic(string diagnostic, bool warning)
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(
            ImGuiCol.Text,
            warning ? new Vector4(0.95f, 0.70f, 0.25f, 1f) : new Vector4(0.55f, 0.75f, 0.95f, 1f));
        TextWrappedUnformatted(diagnostic);
        ImGui.PopStyleColor();
    }

    internal CanvasInspectorSnapshot CaptureCanvasInspector(int stableId)
    {
        EditorGameObject gameObject = _scene.Get(stableId);
        bool hasWebCanvas = gameObject.WebCanvas is not null;
        ulong derivedId = hasWebCanvas ? GameUiCanvasIdentity.FromStableId(stableId).Value : 0;
        try
        {
            EngineSceneCanvasSet set = EngineSceneCanvasResolver.Resolve(_scene.ToDocument());
            bool runtimeEnabled = false;
            bool effectivePrimary = false;
            ReadOnlySpan<EngineSceneCanvasDefinition> canvases = set.Canvases;
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].StableId == stableId)
                {
                    runtimeEnabled = true;
                    effectivePrimary = canvases[i].IsPrimary;
                    break;
                }
            }

            List<string> messages = [];
            ReadOnlySpan<EngineSceneCanvasDiagnostic> diagnostics = set.Diagnostics;
            for (int i = 0; i < diagnostics.Length; i++)
            {
                if (diagnostics[i].StableId == 0 || diagnostics[i].StableId == stableId)
                {
                    messages.Add(diagnostics[i].Message);
                }
            }

            return new CanvasInspectorSnapshot(
                set.HasExplicitCanvases,
                hasWebCanvas,
                gameObject.CanvasScaler is not null,
                runtimeEnabled,
                effectivePrimary,
                hasWebCanvas && gameObject.CanvasScaler is null,
                !hasWebCanvas && gameObject.CanvasScaler is not null,
                set.HasExplicitCanvases && set.Count == 0,
                HasConflict: false,
                derivedId,
                string.Join(Environment.NewLine, messages));
        }
        catch (Exception exception) when (EditorProjectSession.IsRecoverableAuthoringSceneValidationFailure(exception))
        {
            return new CanvasInspectorSnapshot(
                HasExplicitCanvases: hasWebCanvas || HasExplicitWebCanvas(),
                HasWebCanvas: hasWebCanvas,
                HasCanvasScaler: gameObject.CanvasScaler is not null,
                IsRuntimeEnabled: false,
                IsEffectivePrimary: false,
                UsesDefaultScaler: hasWebCanvas && gameObject.CanvasScaler is null,
                IsOrphanScaler: !hasWebCanvas && gameObject.CanvasScaler is not null,
                PrimaryNone: false,
                HasConflict: true,
                DerivedCanvasId: derivedId,
                Diagnostic: exception.Message);
        }
    }

    private bool HasExplicitWebCanvas()
    {
        foreach (EditorGameObject gameObject in _scene.EnumerateDepthFirst())
        {
            if (gameObject.WebCanvas is not null)
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesComponentSearch(string displayName)
    {
        return string.IsNullOrWhiteSpace(_componentSearch) ||
            displayName.Contains(_componentSearch, StringComparison.OrdinalIgnoreCase);
    }

    private static EditorWebCanvasComponent CreateDefaultWebCanvas(bool makePrimary)
    {
        return new EditorWebCanvasComponent { Primary = makePrimary };
    }

    private static string? NormalizeOptionalText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private bool BeginBuiltInCanvasEdit(int stableId)
    {
        if (!CanModifyAuthoringScene())
        {
            return false;
        }

        CommitPendingNameEdit();
        CommitPendingTransformEdit();
        CommitPendingComponentFieldEdit();
        if (_builtInCanvasEdit is { } active && active.StableId != stableId)
        {
            CommitPendingBuiltInCanvasEdit();
        }

        if (_builtInCanvasEdit is not null)
        {
            return true;
        }

        if (!_scene.TryGet(stableId, out EditorGameObject? gameObject))
        {
            return false;
        }

        _builtInCanvasEdit = new BuiltInCanvasEditTransaction(
            stableId,
            _scene.SceneGeneration,
            gameObject,
            gameObject.WebCanvas?.Clone(),
            gameObject.CanvasScaler?.Clone(),
            gameObject.PrefabLink?.Clone(),
            _scene.IsDirty,
            Applied: false);
        return true;
    }

    private bool ApplyBuiltInCanvasEdit(
        int stableId,
        EditorWebCanvasComponent? webCanvas,
        EditorCanvasScalerComponent? scaler)
    {
        if (!BeginBuiltInCanvasEdit(stableId) ||
            _builtInCanvasEdit is not { } active ||
            _scene.SceneGeneration != active.SceneGeneration ||
            !_scene.TryGet(stableId, out EditorGameObject? gameObject) ||
            !ReferenceEquals(gameObject, active.GameObject))
        {
            return false;
        }

        _scene.SetBuiltInCanvasComponents(stableId, webCanvas, scaler);
        _scene.RecordBuiltInCanvasPrefabOverrides(stableId, webCanvas, scaler);
        _builtInCanvasEdit = active with { Applied = true };
        return true;
    }

    [EditorUiControlPrimitive]
    private void HandleBuiltInCanvasInput(
        EditorGameObject gameObject,
        EditorWebCanvasComponent? webCanvas,
        EditorCanvasScalerComponent? scaler,
        bool changed)
    {
        if (ImGui.IsItemActivated())
        {
            _ = BeginBuiltInCanvasEdit(gameObject.StableId);
        }

        if (changed)
        {
            _ = ApplyBuiltInCanvasEdit(gameObject.StableId, webCanvas, scaler);
        }

        if (ImGui.IsItemDeactivated())
        {
            CommitPendingBuiltInCanvasEdit();
        }
    }

    private void CommitPendingBuiltInCanvasEdit()
    {
        if (_builtInCanvasEdit is not { } active)
        {
            return;
        }

        _builtInCanvasEdit = null;
        if (_scene.SceneGeneration != active.SceneGeneration ||
            !_scene.TryGet(active.StableId, out EditorGameObject? gameObject) ||
            !ReferenceEquals(gameObject, active.GameObject))
        {
            return;
        }

        bool unchanged = EditorWebCanvasComponent.ContentEquals(active.OldWebCanvas, gameObject.WebCanvas) &&
            EditorCanvasScalerComponent.ContentEquals(active.OldCanvasScaler, gameObject.CanvasScaler);
        if (unchanged)
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
            new SetBuiltInCanvasComponentsCommand(
                active.StableId,
                active.OldWebCanvas,
                active.OldCanvasScaler,
                active.OldPrefabLink,
                gameObject.WebCanvas,
                gameObject.CanvasScaler,
                gameObject.PrefabLink));
    }

    [EditorUiCommands(
        "panel.inspector.component.enabled",
        "panel.inspector.component.move",
        "panel.inspector.component.remove",
        "context.inspector.component.move-up",
        "context.inspector.component.move-down",
        "context.inspector.component.remove")]
    private void DrawComponent(EditorGameObject gameObject, int componentIndex)
    {
        EditorComponentModel component = gameObject.Components[componentIndex];
        string displayName = GetComponentDisplayName(component.TypeName);
        bool open = DrawInspectorComponentHeader($"##component_{componentIndex}");
        Vector2 headerMin = ImGui.GetItemRectMin();
        Vector2 headerMax = ImGui.GetItemRectMax();
        if (ImGui.IsItemHovered())
        {
            SetTooltipUnformatted(L.Format(
                "inspector.component.actionsTooltip",
                "{0}\nRight-click for component actions",
                component.TypeName));
        }

        int moveTarget = -1;
        bool remove = false;
        if (ImGui.BeginPopupContextItem($"component_context_{componentIndex}"))
        {
            if (ImGui.MenuItem(
                L.Get("inspector.component.moveUp", "Move Up"),
                string.Empty,
                selected: false,
                enabled: componentIndex > 0))
            {
                moveTarget = componentIndex - 1;
            }

            if (ImGui.MenuItem(
                L.Get("inspector.component.moveDown", "Move Down"),
                string.Empty,
                selected: false,
                enabled: componentIndex < gameObject.Components.Count - 1))
            {
                moveTarget = componentIndex + 1;
            }

            ImGui.Separator();
            remove = ImGui.MenuItem(L.Get("inspector.component.remove", "Remove Component"));
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
            SetTooltipUnformatted(componentEnabled
                ? L.Get("inspector.component.enabledTooltip", "Component enabled")
                : L.Get("inspector.component.disabledTooltip", "Component disabled"));
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
            ImGui.TextUnformatted(L.Get("inspector.component.typeUnavailable", "Behaviour type unavailable"));
            return;
        }

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);
        float availableWidth = ImGui.GetContentRegionAvail().X;
        if (!BeginInspectorPropertyTable($"component-fields-{componentIndex}", 2))
        {
            return;
        }

        SetupInspectorPropertyColumns(availableWidth);
        for (int i = 0; i < fields.Length; i++)
        {
            DrawField(gameObject.StableId, componentIndex, component, fields[i]);
        }

        EndInspectorPropertyTable();
    }

    [EditorUiControlPrimitive]
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

    [EditorUiCommands("panel.inspector.component.field")]
    private void DrawField(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        ImGui.TableNextRow();
        _ = ImGui.TableSetColumnIndex(0);
        DrawPropertyLabel(field.Name);
        if (ImGui.IsItemHovered())
        {
            SetTooltipUnformatted(field.Name);
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

    [EditorUiControlPrimitive]
    private void DrawBoolean(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        bool value = bool.TryParse(ReadFieldValue(component, field), out bool parsed) && parsed;
        if (ImGui.Checkbox($"##field-{stableId}-{componentIndex}-{field.Name}", ref value))
        {
            _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, value.ToString()));
        }
    }

    [EditorUiControlPrimitive]
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
            TextColoredUnformatted(
                new Vector4(0.95f, 0.55f, 0.35f, 1f),
                L.Format(
                    "inspector.number.rangeUnsupported",
                    "The declared Range contains no value representable by {0}",
                    target.Name));
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
            if (ImGui.Button($"{L.Get("inspector.number.setZero", "null · Set 0")}##field-{stableId}-{componentIndex}-{field.Name}"))
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

    [EditorUiControlPrimitive]
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
            SetTooltipUnformatted(L.Get(
                "inspector.decimal.tooltip",
                "decimal uses exact text editing. Press Enter or leave the field to commit; intermediate input is preserved."));
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
                _statusMessage = string.Empty;
            }
            else
            {
                _statusMessage = L.Format(
                    "inspector.decimal.invalid",
                    "Invalid decimal: {0}",
                    state.Text.Length == 0 ? L.Get("inspector.value.empty", "<empty>") : state.Text);
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

    [EditorUiControlPrimitive]
    private void DrawInvalidNumericValue(
        int stableId,
        int componentIndex,
        ScriptFieldDescriptor field,
        Type target,
        string current)
    {
        TextColoredUnformatted(
            new Vector4(0.95f, 0.55f, 0.35f, 1f),
            L.Format(
                "inspector.number.invalid",
                "Invalid {0}: {1}",
                target.Name,
                current.Length == 0 ? L.Get("inspector.value.empty", "<empty>") : current));
        ImGui.SameLine();
        if (ImGui.SmallButton($"{L.Get("inspector.action.reset", "Reset")}##number-reset-{stableId}-{componentIndex}-{field.Name}"))
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
                    if (!DragScalarValue(id, ImGuiDataType.Float, ref value, 0.1f, "%g") ||
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
                    if (!DragScalarValue(id, ImGuiDataType.Double, ref value, 0.1f, "%g") ||
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

    [EditorUiControlPrimitive]
    private static unsafe bool DragScalarValue<T>(
        string id,
        ImGuiDataType dataType,
        ref T value,
        float speed,
        string? format = null)
        where T : unmanaged
    {
        fixed (T* valuePointer = &value)
        {
            return format is null
                ? ImGui.DragScalar(id, dataType, valuePointer, speed)
                : ImGui.DragScalar(id, dataType, valuePointer, speed, format);
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

    [EditorUiControlPrimitive]
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
            TextColoredUnformatted(
                new Vector4(0.95f, 0.55f, 0.35f, 1f),
                L.Format("inspector.number.invalid", "Invalid {0}: {1}", target.Name, current));
            if (ImGui.Button($"{L.Get("inspector.vector.resetZero", "Reset to Zero")}##vector-reset-{stableId}-{componentIndex}-{field.Name}"))
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

    [EditorUiControlPrimitive]
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
                0.1f,
                "%g");
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
            SetTooltipUnformatted(L.Get(
                "inspector.component.dragTooltip",
                "Drag horizontally to adjust quickly; Ctrl+click for exact input. One drag creates one Undo step."));
        }
    }

    [EditorUiControlPrimitive]
    private void DrawString(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        string value = ReadFieldValue(component, field);
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.InputText($"##field-{stableId}-{componentIndex}-{field.Name}", ref value, 256);
        HandleComponentFieldInput(stableId, componentIndex, field.Name, value, changed);
    }

    [EditorUiControlPrimitive]
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

    [EditorUiCommands("panel.inspector.asset-reference.clear")]
    private void DrawAssetReference(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        string value = ReadFieldValue(component, field);
        string typeLabel = field.AssetKind?.ToString() ?? L.Get("inspector.unknown", "Unknown");
        ImGui.TextUnformatted($"{FormatAssetReferenceDisplay(field, value)} ({typeLabel})");
        DrawAssetReferenceDropTarget(stableId, componentIndex, field);
        if (!string.IsNullOrWhiteSpace(value))
        {
            ImGui.SameLine();
            if (ImGui.Button($"{L.Get("inspector.assetReference.clear", "Clear")}##assetref_clear_{stableId}_{componentIndex}_{field.Name}"))
            {
                _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, null));
                RecordAssetDropResult(EditorAssetDropResult.Success(
                    L.Format(
                        "inspector.assetReference.cleared",
                        "Cleared the asset reference in field {0}.",
                        field.Name),
                    stableId));
            }
        }
    }

    [EditorUiCommands("panel.inspector.asset-reference.drop")]
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
            : EditorAssetDropResult.Failure(L.Get(
                "inspector.assetReference.invalidPayload",
                "The Project Window drag payload is missing a stable asset ID or logical path."));
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
            : EditorAssetDropResult.Failure(L.Format(
                "inspector.assetReference.notTyped",
                "Field {0} is not a typed asset reference field.",
                field.Name));
    }

    private void RecordAssetDropResult(EditorAssetDropResult result)
    {
        _statusMessage = string.IsNullOrWhiteSpace(result.Diagnostic) ? string.Empty : result.Diagnostic;
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
            ? L.Get("inspector.none", "None")
            : !ScriptAssetReference.TryDecode(value, out ScriptAssetReference reference)
            ? L.Format("inspector.assetReference.invalid", "Invalid reference: {0}", value)
            : field.AssetKind is ScriptAssetKind expected && reference.AssetType != expected
            ? L.Format(
                "inspector.assetReference.typeMismatch",
                "Type mismatch: {0} {1}",
                reference.AssetType,
                reference.LogicalPath)
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

/// <summary>
/// 真实窗口 runtime Inspector 探针快照；只在对应实体的 Inspector 窗口实际完成 Draw 时递增 revision。
/// </summary>
internal readonly record struct ScriptedRuntimeInspectorProbeSnapshot(
    string EntityHandle,
    bool EntityResolved,
    bool TransformTableRendered,
    int ComponentHeaderCount,
    int ComponentPropertyTableCount,
    int ComponentNumericDragFieldCount,
    int ComponentVectorDragFieldCount,
    int ComponentDecimalFieldCount,
    long RenderRevision)
{
    public bool SatisfiesAcceptance(string expectedHandle)
    {
        return RenderRevision > 0 &&
            EntityResolved &&
            TransformTableRendered &&
            ComponentHeaderCount > 0 &&
            ComponentPropertyTableCount > 0 &&
            ComponentNumericDragFieldCount > 0 &&
            string.Equals(EntityHandle, expectedHandle, StringComparison.Ordinal);
    }
}

internal sealed class RuntimeDecimalFieldTextEditState(
    string entityHandle,
    int componentIndex,
    ScriptFieldDescriptor field,
    string text)
{
    public string EntityHandle { get; } =
        entityHandle ?? throw new ArgumentNullException(nameof(entityHandle));

    public int ComponentIndex { get; } = componentIndex;

    public ScriptFieldDescriptor Field { get; } = field;

    public string Text { get; private set; } =
        text ?? throw new ArgumentNullException(nameof(text));

    public bool Dirty { get; private set; }

    public bool HasKey(string entityHandle, int componentIndex, string fieldName)
    {
        return string.Equals(EntityHandle, entityHandle, StringComparison.Ordinal) &&
            ComponentIndex == componentIndex &&
            string.Equals(Field.Name, fieldName, StringComparison.Ordinal);
    }

    public void Update(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Dirty |= !string.Equals(Text, text, StringComparison.Ordinal);
        Text = text;
    }
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

internal readonly record struct BuiltInCanvasEditTransaction(
    int StableId,
    long SceneGeneration,
    EditorGameObject GameObject,
    EditorWebCanvasComponent? OldWebCanvas,
    EditorCanvasScalerComponent? OldCanvasScaler,
    EditorPrefabLink? OldPrefabLink,
    bool WasDirty,
    bool Applied);

internal readonly record struct CanvasInspectorSnapshot(
    bool HasExplicitCanvases,
    bool HasWebCanvas,
    bool HasCanvasScaler,
    bool IsRuntimeEnabled,
    bool IsEffectivePrimary,
    bool UsesDefaultScaler,
    bool IsOrphanScaler,
    bool PrimaryNone,
    bool HasConflict,
    ulong DerivedCanvasId,
    string Diagnostic);

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
