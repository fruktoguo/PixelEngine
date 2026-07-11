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

/// <summary>
/// 编辑器场景操作的 Undo/Redo 栈。
/// </summary>
internal sealed class EditorUndoStack
{
    private readonly Stack<IEditorCommand> _undo = new();
    private readonly Stack<IEditorCommand> _redo = new();

    public bool CanUndo => _undo.Count != 0;

    public bool CanRedo => _redo.Count != 0;

    public string? UndoName => _undo.Count == 0 ? null : _undo.Peek().Name;

    public string? RedoName => _redo.Count == 0 ? null : _redo.Peek().Name;

    public void Execute(EditorSceneModel scene, IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(command);
        command.Execute(scene);
        _undo.Push(command);
        _redo.Clear();
    }

    public bool Undo(EditorSceneModel scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_undo.Count == 0)
        {
            return false;
        }

        IEditorCommand command = _undo.Pop();
        command.Undo(scene);
        _redo.Push(command);
        return true;
    }

    public bool Redo(EditorSceneModel scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_redo.Count == 0)
        {
            return false;
        }

        IEditorCommand command = _redo.Pop();
        command.Execute(scene);
        _undo.Push(command);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
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

    public string Name => "Set Transform";

    public void Execute(EditorSceneModel scene)
    {
        EditorGameObject gameObject = scene.Get(_stableId);
        if (!_captured)
        {
            _oldTransform ??= gameObject.Transform.Clone();
            _oldPrefabLink = gameObject.PrefabLink?.Clone();
        }

        scene.SetTransform(_stableId, _newTransform);
        scene.RecordPrefabOverride(_stableId, "Transform.X", _newTransform.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(_stableId, "Transform.Y", _newTransform.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(_stableId, "Transform.RotationRadians", _newTransform.RotationRadians.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(_stableId, "Transform.ScaleX", _newTransform.ScaleX.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(_stableId, "Transform.ScaleY", _newTransform.ScaleY.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
internal sealed class SetComponentFieldCommand(int stableId, int componentIndex, string fieldName, string? value) : IEditorCommand
{
    private string? _oldValue;
    private bool _hadOldValue;
    private EditorPrefabLink? _oldPrefabLink;
    private EditorPrefabLink? _newPrefabLink;
    private bool _captured;

    public string Name => "Set Component Field";

    public void Execute(EditorSceneModel scene)
    {
        EditorComponentModel component = scene.Get(stableId).Components[componentIndex];
        if (!_captured)
        {
            _hadOldValue = component.SerializedFields.TryGetValue(fieldName, out _oldValue);
            _oldPrefabLink = scene.Get(stableId).PrefabLink?.Clone();
        }

        scene.SetComponentField(stableId, componentIndex, fieldName, value);
        scene.RecordPrefabOverride(stableId, $"Component:{component.TypeName}:{fieldName}", value ?? string.Empty);
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
        scene.SetComponentField(stableId, componentIndex, fieldName, _hadOldValue ? _oldValue : null);
        scene.SetPrefabLink(stableId, _oldPrefabLink);
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
