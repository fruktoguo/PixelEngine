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
    }

    public void Undo(EditorSceneModel scene)
    {
        if (_oldEnabled.HasValue)
        {
            scene.SetEnabled(stableId, _oldEnabled.Value);
        }
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
