namespace PixelEngine.Editor.Shell;

/// <summary>
/// 可撤销的编辑器场景命令接口。
/// </summary>
internal interface IEditorCommand
{
    string Name { get; }

    void Execute(EditorSceneModel scene);

    void Undo(EditorSceneModel scene);
}

/// <summary>Undo history 中一次已经应用到 scene 的变化方向。</summary>
internal enum EditorUndoMutationKind
{
    Execute,
    Undo,
    Redo,
}

/// <summary>
/// 编辑器场景操作的 Undo/Redo 栈。
/// </summary>
internal sealed class EditorUndoStack
{
    private readonly Stack<IEditorCommand> _undo = new();
    private readonly Stack<IEditorCommand> _redo = new();
    private bool _preparingOperation;

    /// <summary>
    /// 在新命令或 Undo/Redo 真正改变场景前收口仍处于 live-preview 状态的连续编辑。
    /// 回调内部允许向本栈提交收口命令；重入时不会再次触发回调。
    /// </summary>
    public Action? BeforeOperation { get; set; }

    /// <summary>
    /// 返回当前 authoring scene 是否允许写入。Play/Paused 时由 Editor session 关闭，
    /// 作为菜单、面板与脚本化入口之外的最后一道统一写屏障。
    /// </summary>
    public Func<bool>? CanModifyScene { get; set; }

    /// <summary>手动命令 execute/undo/redo 完成后的 revision/event 通知。</summary>
    public Action<IEditorCommand, EditorUndoMutationKind>? HistoryApplied { get; set; }

    public bool CanUndo => IsModificationAllowed() && _undo.Count != 0;

    public bool CanRedo => IsModificationAllowed() && _redo.Count != 0;

    public string? UndoName => _undo.Count == 0 ? null : _undo.Peek().Name;

    public string? RedoName => _redo.Count == 0 ? null : _redo.Peek().Name;

    public void Execute(EditorSceneModel scene, IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(command);
        if (!IsModificationAllowed())
        {
            return;
        }

        PrepareOperation();
        command.Execute(scene);
        _undo.Push(command);
        _redo.Clear();
        HistoryApplied?.Invoke(command, EditorUndoMutationKind.Execute);
    }

    public bool Undo(EditorSceneModel scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!IsModificationAllowed())
        {
            return false;
        }

        PrepareOperation();
        if (_undo.Count == 0)
        {
            return false;
        }

        IEditorCommand command = _undo.Pop();
        command.Undo(scene);
        _redo.Push(command);
        HistoryApplied?.Invoke(command, EditorUndoMutationKind.Undo);
        return true;
    }

    public bool Redo(EditorSceneModel scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!IsModificationAllowed())
        {
            return false;
        }

        PrepareOperation();
        if (_redo.Count == 0)
        {
            return false;
        }

        IEditorCommand command = _redo.Pop();
        command.Execute(scene);
        _undo.Push(command);
        HistoryApplied?.Invoke(command, EditorUndoMutationKind.Redo);
        return true;
    }

    /// <summary>
    /// 登记一个已经在同一 Editor 主线程执行完毕的命令，供 automation scheduler
    /// 与手动编辑共用唯一 Undo/Redo 历史。
    /// </summary>
    public void RecordExecuted(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        // 这里只接受 scheduler 已经在同一主线程完成并校验过的命令。transaction commit
        // 记录 composite 时写租约仍处于 active，不能复用面向手动入口的 CanModifyScene 屏障。
        _undo.Push(command);
        _redo.Clear();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private bool IsModificationAllowed()
    {
        return CanModifyScene?.Invoke() ?? true;
    }

    private void PrepareOperation()
    {
        if (_preparingOperation || BeforeOperation is null)
        {
            return;
        }

        try
        {
            _preparingOperation = true;
            BeforeOperation();
        }
        finally
        {
            _preparingOperation = false;
        }
    }
}

/// <summary>
/// Undo 命令：CreateGameObject。
/// </summary>
internal sealed class CreateGameObjectCommand(string name, int? parentId = null, int? insertIndex = null) : IEditorCommand
{
    private EditorSceneObjectSnapshot? _created;

    public string Name => "Create GameObject";

    public void Execute(EditorSceneModel scene)
    {
        if (_created is null)
        {
            EditorGameObject created = scene.Create(name, parentId, insertIndex);
            _created = scene.CaptureSubtree(created.StableId);
            return;
        }

        scene.RestoreSubtree(_created);
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_created is not null)
        {
            _created = scene.DeleteSubtree(_created.Objects[0].StableId);
        }
    }
}

/// <summary>
/// Undo 命令：DeleteGameObject。
/// </summary>
internal sealed class DeleteGameObjectCommand(int stableId) : IEditorCommand
{
    private EditorSceneObjectSnapshot? _deleted;

    public string Name => "Delete GameObject";

    public void Execute(EditorSceneModel scene)
    {
        _deleted = scene.DeleteSubtree(stableId);
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_deleted is not null)
        {
            scene.RestoreSubtree(_deleted);
        }
    }
}

/// <summary>
/// Undo 命令：RenameGameObject。
/// </summary>
internal sealed class RenameGameObjectCommand(int stableId, string newName) : IEditorCommand
{
    private string? _oldName;
    private EditorPrefabLink? _oldPrefabLink;
    private EditorPrefabLink? _newPrefabLink;
    private bool _captured;

    public string Name => "Rename GameObject";

    public void Execute(EditorSceneModel scene)
    {
        EditorGameObject gameObject = scene.Get(stableId);
        if (!_captured)
        {
            _oldName = gameObject.Name;
            _oldPrefabLink = gameObject.PrefabLink?.Clone();
        }

        scene.Rename(stableId, newName);
        scene.RecordPrefabOverride(stableId, "Name", newName);
        if (!_captured)
        {
            _newPrefabLink = scene.Get(stableId).PrefabLink?.Clone();
            _captured = true;
        }
        else
        {
            scene.SetPrefabLink(stableId, _newPrefabLink);
        }
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_oldName is not null)
        {
            scene.Rename(stableId, _oldName);
            scene.SetPrefabLink(stableId, _oldPrefabLink);
        }
    }
}

/// <summary>
/// Undo 命令：SetGameObjectEnabled。
/// </summary>
internal sealed class SetGameObjectEnabledCommand(int stableId, bool enabled) : IEditorCommand
{
    private bool? _oldEnabled;
    private EditorPrefabLink? _oldPrefabLink;
    private EditorPrefabLink? _newPrefabLink;
    private bool _captured;

    public string Name => "Set GameObject Enabled";

    public void Execute(EditorSceneModel scene)
    {
        EditorGameObject gameObject = scene.Get(stableId);
        if (!_captured)
        {
            _oldEnabled = gameObject.Enabled;
            _oldPrefabLink = gameObject.PrefabLink?.Clone();
        }

        scene.SetEnabled(stableId, enabled);
        scene.RecordPrefabOverride(stableId, "Enabled", enabled.ToString());
        if (!_captured)
        {
            _newPrefabLink = scene.Get(stableId).PrefabLink?.Clone();
            _captured = true;
        }
        else
        {
            scene.SetPrefabLink(stableId, _newPrefabLink);
        }
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_oldEnabled.HasValue)
        {
            scene.SetEnabled(stableId, _oldEnabled.Value);
            scene.SetPrefabLink(stableId, _oldPrefabLink);
        }
    }
}

/// <summary>
/// Undo 命令：SetTransform。
/// </summary>
internal sealed class SetTransformCommand : IEditorCommand
{
    private readonly int _stableId;
    private readonly EditorSceneTransform _newTransform;
    private EditorSceneTransform? _oldTransform;
    private EditorPrefabLink? _oldPrefabLink;
    private EditorPrefabLink? _newPrefabLink;
    private bool _captured;
    private readonly bool _hasExplicitPrefabState;

    public SetTransformCommand(int stableId, EditorSceneTransform newTransform)
    {
        if (stableId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stableId));
        }

        _stableId = stableId;
        _newTransform = (newTransform ?? throw new ArgumentNullException(nameof(newTransform))).Clone();
    }

    public SetTransformCommand(int stableId, EditorSceneTransform oldTransform, EditorSceneTransform newTransform)
        : this(stableId, newTransform)
    {
        _oldTransform = (oldTransform ?? throw new ArgumentNullException(nameof(oldTransform))).Clone();
    }

    public SetTransformCommand(
        int stableId,
        EditorSceneTransform oldTransform,
        EditorPrefabLink? oldPrefabLink,
        EditorSceneTransform newTransform,
        EditorPrefabLink? newPrefabLink)
        : this(stableId, oldTransform, newTransform)
    {
        _oldPrefabLink = oldPrefabLink?.Clone();
        _newPrefabLink = newPrefabLink?.Clone();
        _hasExplicitPrefabState = true;
    }

    public string Name => "Set Transform";

    public void Execute(EditorSceneModel scene)
    {
        EditorGameObject gameObject = scene.Get(_stableId);
        if (!_captured && !_hasExplicitPrefabState)
        {
            _oldTransform ??= gameObject.Transform.Clone();
            _oldPrefabLink = gameObject.PrefabLink?.Clone();
        }

        scene.SetTransform(_stableId, _newTransform);
        if (_hasExplicitPrefabState)
        {
            scene.SetPrefabLink(_stableId, _newPrefabLink);
            return;
        }

        scene.RecordTransformPrefabOverrides(_stableId, _newTransform);
        if (!_captured)
        {
            _newPrefabLink = scene.Get(_stableId).PrefabLink?.Clone();
            _captured = true;
        }
        else
        {
            scene.SetPrefabLink(_stableId, _newPrefabLink);
        }
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_oldTransform is not null)
        {
            scene.SetTransform(_stableId, _oldTransform);
            scene.SetPrefabLink(_stableId, _oldPrefabLink);
        }
    }
}

/// <summary>
/// Undo 命令：原子替换内建 Canvas (Web) 与 Canvas Scaler，并保持 prefab override 状态。
/// </summary>
internal sealed class SetBuiltInCanvasComponentsCommand : IEditorCommand
{
    private readonly int _stableId;
    private readonly EditorWebCanvasComponent? _newWebCanvas;
    private readonly EditorCanvasScalerComponent? _newCanvasScaler;
    private EditorWebCanvasComponent? _oldWebCanvas;
    private EditorCanvasScalerComponent? _oldCanvasScaler;
    private EditorPrefabLink? _oldPrefabLink;
    private EditorPrefabLink? _newPrefabLink;
    private bool _captured;
    private readonly bool _hasExplicitState;

    public SetBuiltInCanvasComponentsCommand(
        int stableId,
        EditorWebCanvasComponent? webCanvas,
        EditorCanvasScalerComponent? canvasScaler)
    {
        if (stableId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stableId));
        }

        _stableId = stableId;
        _newWebCanvas = webCanvas?.Clone();
        _newCanvasScaler = canvasScaler?.Clone();
    }

    public SetBuiltInCanvasComponentsCommand(
        int stableId,
        EditorWebCanvasComponent? oldWebCanvas,
        EditorCanvasScalerComponent? oldCanvasScaler,
        EditorPrefabLink? oldPrefabLink,
        EditorWebCanvasComponent? newWebCanvas,
        EditorCanvasScalerComponent? newCanvasScaler,
        EditorPrefabLink? newPrefabLink)
        : this(stableId, newWebCanvas, newCanvasScaler)
    {
        _oldWebCanvas = oldWebCanvas?.Clone();
        _oldCanvasScaler = oldCanvasScaler?.Clone();
        _oldPrefabLink = oldPrefabLink?.Clone();
        _newPrefabLink = newPrefabLink?.Clone();
        _hasExplicitState = true;
    }

    public string Name => "Set Canvas Components";

    public void Execute(EditorSceneModel scene)
    {
        EditorGameObject gameObject = scene.Get(_stableId);
        if (!_captured && !_hasExplicitState)
        {
            _oldWebCanvas = gameObject.WebCanvas?.Clone();
            _oldCanvasScaler = gameObject.CanvasScaler?.Clone();
            _oldPrefabLink = gameObject.PrefabLink?.Clone();
        }

        scene.SetBuiltInCanvasComponents(_stableId, _newWebCanvas, _newCanvasScaler);
        if (_hasExplicitState)
        {
            scene.SetPrefabLink(_stableId, _newPrefabLink);
            return;
        }

        scene.RecordBuiltInCanvasPrefabOverrides(_stableId, _newWebCanvas, _newCanvasScaler);
        if (!_captured)
        {
            _newPrefabLink = scene.Get(_stableId).PrefabLink?.Clone();
            _captured = true;
        }
        else
        {
            scene.SetPrefabLink(_stableId, _newPrefabLink);
        }
    }

    public void Undo(EditorSceneModel scene)
    {
        scene.SetBuiltInCanvasComponents(_stableId, _oldWebCanvas, _oldCanvasScaler);
        scene.SetPrefabLink(_stableId, _oldPrefabLink);
    }
}

/// <summary>
/// Undo 命令：把一个 Web Canvas 设为显式 primary，并在同一事务中清除其他 primary。
/// </summary>
internal sealed class SetPrimaryWebCanvasCommand(int stableId) : IEditorCommand
{
    private CanvasPrimaryState[]? _before;
    private CanvasPrimaryState[]? _after;

    public string Name => "Set Primary Web Canvas";

    public void Execute(EditorSceneModel scene)
    {
        if (_after is not null)
        {
            Apply(scene, _after);
            return;
        }

        if (scene.Get(stableId).WebCanvas is null)
        {
            throw new InvalidOperationException($"GameObject {stableId} 没有 Canvas (Web)。");
        }

        List<CanvasPrimaryState> before = [];
        List<CanvasPrimaryState> after = [];
        foreach (EditorGameObject gameObject in scene.EnumerateDepthFirst())
        {
            if (gameObject.WebCanvas is null)
            {
                continue;
            }

            before.Add(Capture(gameObject));
            EditorWebCanvasComponent webCanvas = gameObject.WebCanvas.Clone();
            webCanvas.Primary = gameObject.StableId == stableId;
            scene.SetBuiltInCanvasComponents(gameObject.StableId, webCanvas, gameObject.CanvasScaler);
            scene.RecordPrefabOverride(gameObject.StableId, "WebCanvas.Primary", webCanvas.Primary.ToString());
            after.Add(Capture(scene.Get(gameObject.StableId)));
        }

        _before = [.. before];
        _after = [.. after];
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_before is not null)
        {
            Apply(scene, _before);
        }
    }

    private static CanvasPrimaryState Capture(EditorGameObject gameObject)
    {
        return new CanvasPrimaryState(
            gameObject.StableId,
            gameObject.WebCanvas?.Clone(),
            gameObject.CanvasScaler?.Clone(),
            gameObject.PrefabLink?.Clone());
    }

    private static void Apply(EditorSceneModel scene, ReadOnlySpan<CanvasPrimaryState> states)
    {
        for (int i = 0; i < states.Length; i++)
        {
            ref readonly CanvasPrimaryState state = ref states[i];
            scene.SetBuiltInCanvasComponents(state.StableId, state.WebCanvas, state.CanvasScaler);
            scene.SetPrefabLink(state.StableId, state.PrefabLink);
        }
    }

    private readonly record struct CanvasPrimaryState(
        int StableId,
        EditorWebCanvasComponent? WebCanvas,
        EditorCanvasScalerComponent? CanvasScaler,
        EditorPrefabLink? PrefabLink);
}

/// <summary>
/// Undo 命令：AddComponent。
/// </summary>
internal sealed class AddComponentCommand(int stableId, EditorComponentModel component, int? insertIndex = null) : IEditorCommand
{
    public string Name => "Add Component";

    public void Execute(EditorSceneModel scene)
    {
        scene.AddComponent(stableId, component, insertIndex);
    }

    public void Undo(EditorSceneModel scene)
    {
        int index = insertIndex ?? (scene.Get(stableId).Components.Count - 1);
        _ = scene.RemoveComponent(stableId, index);
    }
}

/// <summary>
/// Undo 命令：RemoveComponent。
/// </summary>
internal sealed class RemoveComponentCommand(int stableId, int componentIndex) : IEditorCommand
{
    private EditorComponentModel? _removed;

    public string Name => "Remove Component";

    public void Execute(EditorSceneModel scene)
    {
        _removed = scene.RemoveComponent(stableId, componentIndex);
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_removed is not null)
        {
            scene.AddComponent(stableId, _removed, componentIndex);
        }
    }
}

/// <summary>
/// Undo 命令：MoveComponent。
/// </summary>
internal sealed class MoveComponentCommand(int stableId, int fromIndex, int toIndex) : IEditorCommand
{
    public string Name => "Move Component";

    public void Execute(EditorSceneModel scene)
    {
        scene.MoveComponent(stableId, fromIndex, toIndex);
    }

    public void Undo(EditorSceneModel scene)
    {
        scene.MoveComponent(stableId, toIndex, fromIndex);
    }
}

/// <summary>
/// Undo 命令：SetComponentField。
/// </summary>
internal sealed class SetComponentFieldCommand(
    int stableId,
    int componentIndex,
    string fieldName,
    string? value) : IEditorCommand
{
    private readonly int _stableId = stableId;
    private readonly int _componentIndex = componentIndex;
    private readonly string _fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
    private readonly string? _value = value;
    private string? _oldValue;
    private bool _hadOldValue;
    private readonly bool _hasExplicitOldValue;
    private EditorPrefabLink? _oldPrefabLink;
    private EditorPrefabLink? _newPrefabLink;
    private bool _captured;
    private readonly bool _hasExplicitPrefabState;

    public SetComponentFieldCommand(
        int stableId,
        int componentIndex,
        string fieldName,
        bool hadOldValue,
        string? oldValue,
        string? value)
        : this(stableId, componentIndex, fieldName, value)
    {
        _hadOldValue = hadOldValue;
        _oldValue = oldValue;
        _hasExplicitOldValue = true;
    }

    public SetComponentFieldCommand(
        int stableId,
        int componentIndex,
        string fieldName,
        bool hadOldValue,
        string? oldValue,
        EditorPrefabLink? oldPrefabLink,
        string? value,
        EditorPrefabLink? newPrefabLink)
        : this(stableId, componentIndex, fieldName, value)
    {
        _hadOldValue = hadOldValue;
        _oldValue = oldValue;
        _oldPrefabLink = oldPrefabLink?.Clone();
        _newPrefabLink = newPrefabLink?.Clone();
        _hasExplicitOldValue = true;
        _hasExplicitPrefabState = true;
    }

    public string Name => "Set Component Field";

    public void Execute(EditorSceneModel scene)
    {
        EditorComponentModel component = scene.Get(_stableId).Components[_componentIndex];
        if (!_captured && !_hasExplicitPrefabState)
        {
            if (!_hasExplicitOldValue)
            {
                _hadOldValue = component.SerializedFields.TryGetValue(_fieldName, out _oldValue);
            }

            _oldPrefabLink = scene.Get(_stableId).PrefabLink?.Clone();
        }

        scene.SetComponentField(_stableId, _componentIndex, _fieldName, _value);
        if (_hasExplicitPrefabState)
        {
            scene.SetPrefabLink(_stableId, _newPrefabLink);
            return;
        }

        scene.RecordPrefabOverride(_stableId, $"Component:{component.TypeName}:{_fieldName}", _value ?? string.Empty);
        if (!_captured)
        {
            _newPrefabLink = scene.Get(_stableId).PrefabLink?.Clone();
            _captured = true;
        }
        else
        {
            scene.SetPrefabLink(_stableId, _newPrefabLink);
        }
    }

    public void Undo(EditorSceneModel scene)
    {
        scene.SetComponentField(_stableId, _componentIndex, _fieldName, _hadOldValue ? _oldValue : null);
        scene.SetPrefabLink(_stableId, _oldPrefabLink);
    }
}

/// <summary>
/// Undo 命令：ReparentGameObject。
/// </summary>
internal sealed class ReparentGameObjectCommand(int stableId, int? newParentId, int? newIndex = null) : IEditorCommand
{
    private int? _oldParentId;
    private int? _oldIndex;

    public string Name => "Reparent GameObject";

    public void Execute(EditorSceneModel scene)
    {
        EditorGameObject gameObject = scene.Get(stableId);
        _oldParentId ??= gameObject.ParentId;
        _oldIndex ??= scene.IndexInParent(stableId);
        scene.Move(stableId, newParentId, newIndex);
        scene.Select(stableId);
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_oldIndex.HasValue)
        {
            scene.Move(stableId, _oldParentId, _oldIndex);
            scene.Select(stableId);
        }
    }
}

/// <summary>
/// Undo 命令：DuplicateGameObject。
/// </summary>
internal sealed class DuplicateGameObjectCommand(int stableId) : IEditorCommand
{
    private EditorSceneObjectSnapshot? _duplicate;

    public string Name => "Duplicate GameObject";

    public void Execute(EditorSceneModel scene)
    {
        if (_duplicate is null)
        {
            EditorGameObject duplicate = scene.DuplicateSubtree(stableId);
            _duplicate = scene.CaptureSubtree(duplicate.StableId);
            return;
        }

        scene.RestoreSubtree(_duplicate);
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_duplicate is not null)
        {
            _duplicate = scene.DeleteSubtree(_duplicate.Objects[0].StableId);
        }
    }
}

/// <summary>
/// Undo 命令：CreatePrefabAsset。
/// </summary>
internal sealed class CreatePrefabAssetCommand(EditorPrefabAssetStore prefabs, int stableId, string assetPath) : IEditorCommand
{
    private EditorSceneObjectSnapshot? _before;
    private byte[]? _previousAssetBytes;
    private bool _hadPreviousAsset;
    private bool _capturedAsset;

    public string Name => "Create Prefab";

    public void Execute(EditorSceneModel scene)
    {
        _before ??= scene.CaptureSubtree(stableId);
        if (!_capturedAsset)
        {
            _hadPreviousAsset = prefabs.TryReadAsset(assetPath, out _previousAssetBytes);
            _capturedAsset = true;
        }

        prefabs.CreatePrefabFromSubtree(scene, stableId, assetPath);
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_before is null)
        {
            return;
        }

        _ = scene.DeleteSubtree(stableId);
        scene.RestoreSubtree(_before);
        if (_hadPreviousAsset && _previousAssetBytes is not null)
        {
            prefabs.RestoreAsset(assetPath, _previousAssetBytes);
        }
        else
        {
            prefabs.DeleteAsset(assetPath);
        }
    }
}

/// <summary>
/// Undo 命令：InstantiatePrefab。
/// </summary>
internal sealed class InstantiatePrefabCommand(
    EditorPrefabAssetStore prefabs,
    string assetPath,
    int? parentId,
    EditorSceneTransform? initialTransform = null) : IEditorCommand
{
    private EditorSceneObjectSnapshot? _created;

    public string Name => "Instantiate Prefab";

    public void Execute(EditorSceneModel scene)
    {
        if (_created is null)
        {
            EditorGameObject created = prefabs.InstantiatePrefab(scene, assetPath, parentId);
            if (initialTransform is not null)
            {
                scene.SetTransform(created.StableId, initialTransform);
                RecordTransformOverrides(scene, created.StableId, initialTransform);
            }

            _created = scene.CaptureSubtree(created.StableId);
            return;
        }

        scene.RestoreSubtree(_created);
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_created is not null)
        {
            _created = scene.DeleteSubtree(_created.Objects[0].StableId);
        }
    }

    private static void RecordTransformOverrides(EditorSceneModel scene, int stableId, EditorSceneTransform transform)
    {
        scene.RecordPrefabOverride(stableId, "Transform.X", transform.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(stableId, "Transform.Y", transform.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(stableId, "Transform.RotationRadians", transform.RotationRadians.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(stableId, "Transform.ScaleX", transform.ScaleX.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(stableId, "Transform.ScaleY", transform.ScaleY.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Undo 命令：RevertPrefabOverrides。
/// </summary>
internal sealed class RevertPrefabOverridesCommand(int stableId) : IEditorCommand
{
    private EditorPrefabLink? _oldLink;

    public string Name => "Revert Prefab Overrides";

    public void Execute(EditorSceneModel scene)
    {
        _oldLink ??= scene.Get(stableId).PrefabLink?.Clone();
        scene.ClearPrefabOverrides(stableId);
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_oldLink is not null)
        {
            scene.SetPrefabLink(stableId, _oldLink);
        }
    }
}
