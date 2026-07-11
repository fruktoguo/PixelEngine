using Hexa.NET.ImGui;
using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Scripting;
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
    Action<string>? instantiatePrefab = null,
    ScriptAssetOpenHandler? openScriptAsset = null,
    IRuntimeSceneEditorDataSource? runtimeSource = null,
    Func<EditorMode>? modeProvider = null) : IEditorPanel
{
    private const string ReadyStatus = "就绪";
    private readonly EditorSceneModel _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly EditorUndoStack _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    private readonly ScriptAssemblyRegistry _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
    private readonly IEditorConsoleSink? _console = console;
    private readonly IAssetBrowserDataSource? _assetSource = assetSource;
    private readonly Action<string>? _instantiatePrefab = instantiatePrefab;
    private readonly ScriptAssetOpenHandler? _openScriptAsset = openScriptAsset;
    private readonly IRuntimeSceneEditorDataSource? _runtimeSource = runtimeSource;
    private readonly Func<EditorMode>? _modeProvider = modeProvider;
    private string _componentSearch = string.Empty;
    private string? _statusSelectionKey;
    private EditorMode _lastMode = EditorMode.Edit;
    private int? _transformEditStableId;
    private EditorSceneTransform? _transformEditBefore;
    private EditorGameObject? _transformEditTarget;

    public string Title => EditorDockSpace.InspectorWindowTitle;

    internal string Status { get; private set; } = ReadyStatus;

    public bool Visible { get; set; } = true;

    public void Draw(in EditorContext context)
    {
        PrepareFrame(context.Selection.GameObjectStableId);
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
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
        ImGui.SeparatorText("Transform");
        DrawTransform(gameObject);
        ImGui.SeparatorText("Components");
        DrawComponents(gameObject);
        ImGui.SeparatorText("Inspector 状态");
        ImGui.TextUnformatted(Status);
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
        UpdateRuntimeEditLifetime();
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
            return new AssetInspectorSnapshot(assetPath, Found: false, "Unknown", null, 0, null, null, "资产数据源不可用");
        }

        IReadOnlyList<AssetBrowserItem> assets = _assetSource.ListAssets();
        for (int i = 0; i < assets.Count; i++)
        {
            AssetBrowserItem item = assets[i];
            if (string.Equals(item.Path, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                return new AssetInspectorSnapshot(
                    item.Path,
                    Found: true,
                    item.Kind.ToString(),
                    item.AssetId,
                    item.SizeBytes,
                    item.PreviewSummary,
                    GetPrimaryAssetActionLabel(item.Kind),
                    "就绪");
            }
        }

        return new AssetInspectorSnapshot(assetPath, Found: false, "Unknown", null, 0, null, null, $"资产不存在：{assetPath}");
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

        if (!Enum.TryParse(asset.Kind, ignoreCase: false, out AssetBrowserItemKind kind))
        {
            RecordInspectorStatus($"资产类型不可操作：{asset.Kind}", EditorConsoleSeverity.Warning, "inspector-asset-action", GetAssetSelectionKey(asset.Path));
            return false;
        }

        switch (kind)
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
                    GetAssetSelectionKey(asset.Path));
                return opened;

            case AssetBrowserItemKind.Prefab:
                if (_instantiatePrefab is null)
                {
                    RecordInspectorStatus("Prefab 实例化服务不可用", EditorConsoleSeverity.Warning, "inspector-asset-action", GetAssetSelectionKey(asset.Path));
                    return false;
                }

                _instantiatePrefab(asset.Path);
                RecordInspectorStatus($"实例化 {asset.Path}", EditorConsoleSeverity.Info, "inspector-asset-action", GetAssetSelectionKey(asset.Path));
                return true;

            case AssetBrowserItemKind.Material:
            case AssetBrowserItemKind.Texture:
            case AssetBrowserItemKind.Audio:
            case AssetBrowserItemKind.Scene:
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
        ImGui.TextUnformatted($"Path: {asset.Path}");
        ImGui.TextUnformatted($"Type: {asset.Kind}");
        ImGui.TextUnformatted($"StableId: {asset.AssetId ?? "none"}");
        ImGui.TextUnformatted($"Size: {asset.SizeBytes} bytes");
        ImGui.TextUnformatted($"Preview: {asset.PreviewSummary ?? "none"}");
        if (!string.IsNullOrWhiteSpace(asset.PrimaryActionLabel) && ImGui.Button($"{asset.PrimaryActionLabel}##asset-primary-action"))
        {
            _ = TryInvokePrimaryAssetAction(asset.Path);
        }

        ImGui.SeparatorText("Inspector 状态");
        ImGui.TextUnformatted(GetSelectionStatus(GetAssetSelectionKey(asset.Path), asset.Status));
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
            AssetBrowserItemKind.Script => "Open",
            AssetBrowserItemKind.Prefab => "Instantiate",
            AssetBrowserItemKind.Material or
            AssetBrowserItemKind.Texture or
            AssetBrowserItemKind.Audio or
            AssetBrowserItemKind.Scene or
            AssetBrowserItemKind.UiScreen or
            AssetBrowserItemKind.Json or
            AssetBrowserItemKind.Folder or
            AssetBrowserItemKind.Other => null,
            _ => null,
        };
    }

    private void RecordInspectorStatus(string status, EditorConsoleSeverity severity, string source, string selectionKey)
    {
        Status = string.IsNullOrWhiteSpace(status) ? ReadyStatus : status;
        _statusSelectionKey = selectionKey;
        _console?.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Asset,
            severity,
            source,
            Status));
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
        string name = gameObject.Name;
        if (ImGui.InputText("Name", ref name, 128) && !string.IsNullOrWhiteSpace(name) && name != gameObject.Name)
        {
            _undo.Execute(_scene, new RenameGameObjectCommand(gameObject.StableId, name));
        }

        bool enabled = gameObject.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled) && enabled != gameObject.Enabled)
        {
            _undo.Execute(_scene, new SetGameObjectEnabledCommand(gameObject.StableId, enabled));
        }

        ImGui.TextUnformatted($"StableId: {gameObject.StableId}");
        if (gameObject.PrefabLink?.AssetPath is { Length: > 0 } prefab)
        {
            ImGui.TextUnformatted($"Prefab: {prefab}");
            ImGui.TextUnformatted($"Overrides: {gameObject.PrefabLink.Overrides.Count}");
            if (gameObject.PrefabLink.Overrides.Count != 0 && ImGui.Button("Revert Overrides"))
            {
                _undo.Execute(_scene, new RevertPrefabOverridesCommand(gameObject.StableId));
            }
        }
        else
        {
            ImGui.TextUnformatted("Prefab: none");
        }
    }

    private void DrawTransform(EditorGameObject gameObject)
    {
        EditorSceneTransform transform = gameObject.Transform.Clone();
        float x = transform.X;
        float y = transform.Y;
        bool changed = ImGui.InputFloat("X", ref x);
        if (changed)
        {
            transform.X = x;
        }
        HandleTransformInput(gameObject, transform, changed);

        changed = ImGui.InputFloat("Y", ref y);
        if (changed)
        {
            transform.Y = y;
        }
        HandleTransformInput(gameObject, transform, changed);

        float rotation = transform.RotationRadians;
        changed = ImGui.InputFloat("Rotation", ref rotation);
        if (changed)
        {
            transform.RotationRadians = rotation;
        }
        HandleTransformInput(gameObject, transform, changed);

        float scaleX = transform.ScaleX;
        float scaleY = transform.ScaleY;
        changed = ImGui.InputFloat("Scale X", ref scaleX);
        if (changed)
        {
            transform.ScaleX = scaleX;
        }
        HandleTransformInput(gameObject, transform, changed);

        changed = ImGui.InputFloat("Scale Y", ref scaleY);
        if (changed)
        {
            transform.ScaleY = scaleY;
        }
        HandleTransformInput(gameObject, transform, changed);
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
        // 遍历已有组件并提供 Add Component 搜索下拉
        for (int i = 0; i < gameObject.Components.Count; i++)
        {
            DrawComponent(gameObject, i);
        }

        ImGui.Separator();
        _ = ImGui.InputText("Search", ref _componentSearch, 128);
        Type[] behaviours = GetBehaviourTypes(_componentSearch);
        if (ImGui.BeginCombo("Add Component", behaviours.Length == 0 ? "No Behaviour" : "Select Behaviour"))
        {
            for (int i = 0; i < behaviours.Length; i++)
            {
                Type behaviour = behaviours[i];
                string label = behaviour.FullName ?? behaviour.Name;
                if (ImGui.Selectable(label))
                {
                    _undo.Execute(_scene, new AddComponentCommand(gameObject.StableId, new EditorComponentModel(label)));
                    _componentSearch = string.Empty;
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawComponent(EditorGameObject gameObject, int componentIndex)
    {
        EditorComponentModel component = gameObject.Components[componentIndex];
        if (!ImGui.CollapsingHeader($"{component.TypeName}##component_{componentIndex}", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (ImGui.Button($"Remove##component_remove_{componentIndex}"))
        {
            _undo.Execute(_scene, new RemoveComponentCommand(gameObject.StableId, componentIndex));
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button($"Up##component_up_{componentIndex}") && componentIndex > 0)
        {
            _undo.Execute(_scene, new MoveComponentCommand(gameObject.StableId, componentIndex, componentIndex - 1));
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button($"Down##component_down_{componentIndex}") && componentIndex < gameObject.Components.Count - 1)
        {
            _undo.Execute(_scene, new MoveComponentCommand(gameObject.StableId, componentIndex, componentIndex + 1));
            return;
        }

        if (!TryCreateBehaviour(component.TypeName, out Behaviour? behaviour))
        {
            ImGui.TextUnformatted("Behaviour type unavailable");
            return;
        }

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);
        for (int i = 0; i < fields.Length; i++)
        {
            DrawField(gameObject.StableId, componentIndex, component, fields[i]);
        }
    }

    private void DrawField(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        if (!field.CanWrite || field.Kind == ScriptFieldKind.Unsupported)
        {
            ImGui.TextUnformatted($"{field.Name}: {ReadFieldValue(component, field)}");
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
        if (ImGui.Checkbox(field.Name, ref value))
        {
            _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, value.ToString()));
        }
    }

    private void DrawNumber(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        float value = float.TryParse(ReadFieldValue(component, field), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : 0f;
        if (ImGui.InputFloat(field.Name, ref value))
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
        if (ImGui.InputText(field.Name, ref value, 256))
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
        if (ImGui.Combo(field.Name, ref index, names, names.Length) && index >= 0 && index < names.Length)
        {
            _undo.Execute(_scene, new SetComponentFieldCommand(stableId, componentIndex, field.Name, names[index]));
        }
    }

    private void DrawAssetReference(int stableId, int componentIndex, EditorComponentModel component, ScriptFieldDescriptor field)
    {
        string value = ReadFieldValue(component, field);
        string typeLabel = field.AssetKind?.ToString() ?? "Unknown";
        ImGui.TextUnformatted($"{field.Name} ({typeLabel}): {FormatAssetReferenceDisplay(field, value)}");
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

internal readonly record struct AssetInspectorSnapshot(
    string Path,
    bool Found,
    string Kind,
    string? AssetId,
    long SizeBytes,
    string? PreviewSummary,
    string? PrimaryActionLabel,
    string Status);

internal readonly record struct FolderInspectorSnapshot(
    string Path,
    bool Found,
    int AssetCount,
    string Status);
