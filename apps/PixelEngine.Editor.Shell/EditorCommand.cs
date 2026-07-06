namespace PixelEngine.Editor.Shell;

internal interface IEditorCommand
{
    string Name { get; }

    void Execute(EditorSceneModel scene);

    void Undo(EditorSceneModel scene);
}

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

internal sealed class RenameGameObjectCommand(int stableId, string newName) : IEditorCommand
{
    private string? _oldName;

    public string Name => "Rename GameObject";

    public void Execute(EditorSceneModel scene)
    {
        EditorGameObject gameObject = scene.Get(stableId);
        _oldName ??= gameObject.Name;
        scene.Rename(stableId, newName);
        scene.RecordPrefabOverride(stableId, "Name", newName);
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_oldName is not null)
        {
            scene.Rename(stableId, _oldName);
        }
    }
}

internal sealed class SetGameObjectEnabledCommand(int stableId, bool enabled) : IEditorCommand
{
    private bool? _oldEnabled;

    public string Name => "Set GameObject Enabled";

    public void Execute(EditorSceneModel scene)
    {
        EditorGameObject gameObject = scene.Get(stableId);
        _oldEnabled ??= gameObject.Enabled;
        scene.SetEnabled(stableId, enabled);
        scene.RecordPrefabOverride(stableId, "Enabled", enabled.ToString());
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_oldEnabled.HasValue)
        {
            scene.SetEnabled(stableId, _oldEnabled.Value);
        }
    }
}

internal sealed class SetTransformCommand(int stableId, EditorSceneTransform newTransform) : IEditorCommand
{
    private EditorSceneTransform? _oldTransform;

    public string Name => "Set Transform";

    public void Execute(EditorSceneModel scene)
    {
        _oldTransform ??= scene.Get(stableId).Transform.Clone();
        scene.SetTransform(stableId, newTransform);
        scene.RecordPrefabOverride(stableId, "Transform.X", newTransform.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(stableId, "Transform.Y", newTransform.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(stableId, "Transform.RotationRadians", newTransform.RotationRadians.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(stableId, "Transform.ScaleX", newTransform.ScaleX.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scene.RecordPrefabOverride(stableId, "Transform.ScaleY", newTransform.ScaleY.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_oldTransform is not null)
        {
            scene.SetTransform(stableId, _oldTransform);
        }
    }
}

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

internal sealed class SetComponentFieldCommand(int stableId, int componentIndex, string fieldName, string? value) : IEditorCommand
{
    private string? _oldValue;
    private bool _hadOldValue;

    public string Name => "Set Component Field";

    public void Execute(EditorSceneModel scene)
    {
        EditorComponentModel component = scene.Get(stableId).Components[componentIndex];
        if (!_hadOldValue && component.SerializedFields.TryGetValue(fieldName, out string? existing))
        {
            _oldValue = existing;
            _hadOldValue = true;
        }

        scene.SetComponentField(stableId, componentIndex, fieldName, value);
        scene.RecordPrefabOverride(stableId, $"Component:{component.TypeName}:{fieldName}", value ?? string.Empty);
    }

    public void Undo(EditorSceneModel scene)
    {
        scene.SetComponentField(stableId, componentIndex, fieldName, _hadOldValue ? _oldValue : null);
    }
}

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
