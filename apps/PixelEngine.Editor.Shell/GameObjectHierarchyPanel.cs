using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Hierarchy 面板：场景树选择与 GameObject 操作。
/// </summary>
internal sealed class GameObjectHierarchyPanel(
    EditorSceneModel scene,
    EditorUndoStack undo,
    EditorPrefabAssetStore prefabs,
    Func<SceneHierarchySnapshot>? runtimeSnapshot = null,
    Func<EditorMode>? modeProvider = null) : IEditorPanel
{
    private readonly EditorSceneModel _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly EditorUndoStack _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    private readonly EditorPrefabAssetStore _prefabs = prefabs ?? throw new ArgumentNullException(nameof(prefabs));
    private readonly Func<SceneHierarchySnapshot>? _runtimeSnapshot = runtimeSnapshot;
    private readonly Func<EditorMode>? _modeProvider = modeProvider;
    private int _renameTarget;
    private int? _draggingStableId;
    private string _renameBuffer = string.Empty;

    public string Title => EditorDockSpace.SceneHierarchyWindowTitle;

    public bool Visible { get; set; } = true;

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
        SyncSelection(context.Selection);
        DrawToolbar();
        ImGui.Separator();
        if (ImGui.Selectable(EditorLocalization.Get("hierarchy.sceneRoot", "Scene Root"), _scene.SelectedStableId is null))
        {
            _scene.Select(null);
            context.Selection.Clear();
        }

        DrawRootDropTarget();
        for (int i = 0; i < _scene.RootIds.Count; i++)
        {
            DrawNode(_scene.RootIds[i], context.Selection);
        }

        DrawGeneratedMarkers();
        DrawRuntimeHierarchy();

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _draggingStableId = null;
        }

        DrawContextMenu(null);
        ImGui.End();
    }

    private void DrawGeneratedMarkers()
    {
        SceneAuthoringPreview preview = SceneAuthoringPreviewBuilder.Build(_scene);
        int generatedCount = 0;
        for (int i = 0; i < preview.Markers.Length; i++)
        {
            if (!preview.Markers[i].StableId.HasValue)
            {
                generatedCount++;
            }
        }

        if (generatedCount == 0 || !ImGui.TreeNodeEx(
            EditorLocalization.Get("hierarchy.generated", "Generated Markers"),
            ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            return;
        }

        for (int i = 0; i < preview.Markers.Length; i++)
        {
            SceneAuthoringMarker marker = preview.Markers[i];
            if (!marker.StableId.HasValue)
            {
                ImGui.BulletText($"{marker.Name}  ({marker.Position.X:0}, {marker.Position.Y:0})");
            }
        }

        ImGui.TreePop();
    }

    private void DrawRuntimeHierarchy()
    {
        if (_runtimeSnapshot is null || _modeProvider?.Invoke() == EditorMode.Edit)
        {
            return;
        }

        SceneHierarchySnapshot snapshot = _runtimeSnapshot();
        if (!ImGui.TreeNodeEx(
            EditorLocalization.Format("hierarchy.runtime", "Runtime (Play) · {0} entities · {1} bodies", snapshot.Entities.Count, snapshot.Bodies.Count),
            ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            return;
        }

        for (int i = 0; i < snapshot.Entities.Count; i++)
        {
            SceneHierarchyEntityItem entity = snapshot.Entities[i];
            ImGui.BulletText($"{entity.DisplayName}  [{entity.ComponentCount} components]");
        }

        for (int i = 0; i < snapshot.Bodies.Count; i++)
        {
            SceneHierarchyBodyItem body = snapshot.Bodies[i];
            ImGui.BulletText($"{body.DisplayName}  ({body.X:0.0}, {body.Y:0.0})");
        }

        ImGui.TreePop();
    }

    internal void SyncSelection(EditorSelection selection)
    {
        if (!string.IsNullOrWhiteSpace(selection.AssetPath) || selection.FolderPath is not null)
        {
            if (_scene.SelectedStableId.HasValue)
            {
                _scene.Select(null);
            }

            return;
        }

        if (selection.GameObjectStableId.HasValue && _scene.TryGet(selection.GameObjectStableId.Value, out _))
        {
            if (_scene.SelectedStableId != selection.GameObjectStableId)
            {
                _scene.Select(selection.GameObjectStableId);
            }

            return;
        }

        if (_scene.SelectedStableId.HasValue)
        {
            selection.SelectGameObject(_scene.SelectedStableId.Value);
        }
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Create"))
        {
            _undo.Execute(_scene, new CreateGameObjectCommand("GameObject", _scene.SelectedStableId));
        }

        ImGui.SameLine();
        if (ImGui.Button("Undo") && _undo.CanUndo)
        {
            _ = _undo.Undo(_scene);
        }

        ImGui.SameLine();
        if (ImGui.Button("Redo") && _undo.CanRedo)
        {
            _ = _undo.Redo(_scene);
        }

        if (_undo.UndoName is not null || _undo.RedoName is not null)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"U:{_undo.UndoName ?? "-"}  R:{_undo.RedoName ?? "-"}");
        }
    }

    private void DrawNode(int stableId, EditorSelection selection)
    {
        EditorGameObject gameObject = _scene.Get(stableId);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (gameObject.Children.Count == 0)
        {
            flags |= ImGuiTreeNodeFlags.Leaf;
        }

        if (_scene.SelectedStableId == stableId)
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        bool enabled = gameObject.Enabled;
        if (ImGui.Checkbox($"##enabled_{stableId}", ref enabled))
        {
            _undo.Execute(_scene, new SetGameObjectEnabledCommand(stableId, enabled));
        }

        ImGui.SameLine();
        bool open = ImGui.TreeNodeEx($"{gameObject.Name}##go_{stableId}", flags);
        if (ImGui.IsItemClicked())
        {
            Select(stableId, selection);
        }

        TrackManualDrag(stableId, gameObject.Name);
        DrawManualDropTarget(stableId);
        DrawContextMenu(stableId);
        if (_renameTarget == stableId)
        {
            DrawRenameInline(stableId);
        }

        if (open)
        {
            for (int i = 0; i < gameObject.Children.Count; i++)
            {
                DrawNode(gameObject.Children[i], selection);
            }

            ImGui.TreePop();
        }
    }

    private void Select(int stableId, EditorSelection selection)
    {
        _scene.Select(stableId);
        selection.SelectGameObject(stableId);
    }

    private void BeginRename(EditorGameObject gameObject)
    {
        _renameTarget = gameObject.StableId;
        _renameBuffer = gameObject.Name;
    }

    private void DrawRenameInline(int stableId)
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText($"##rename_{stableId}", ref _renameBuffer, 128, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            CommitRename(stableId);
        }

        if (!ImGui.IsItemActive() && ImGui.IsKeyPressed(ImGuiKey.Enter))
        {
            CommitRename(stableId);
        }
    }

    private void CommitRename(int stableId)
    {
        if (!string.IsNullOrWhiteSpace(_renameBuffer))
        {
            _undo.Execute(_scene, new RenameGameObjectCommand(stableId, _renameBuffer));
        }

        _renameTarget = 0;
        _renameBuffer = string.Empty;
    }

    private void TrackManualDrag(int stableId, string label)
    {
        if (ImGui.IsItemHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 6f))
        {
            _draggingStableId = stableId;
            ImGui.SetTooltip($"Move {label}");
        }
    }

    private void DrawManualDropTarget(int targetStableId)
    {
        if (_draggingStableId is not { } sourceStableId ||
            sourceStableId == targetStableId ||
            !ImGui.IsItemHovered() ||
            !ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            return;
        }

        TryReparent(sourceStableId, targetStableId);
        _draggingStableId = null;
    }

    private void DrawRootDropTarget()
    {
        if (_draggingStableId is not { } sourceStableId ||
            !ImGui.IsItemHovered() ||
            !ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            return;
        }

        TryReparent(sourceStableId, null);
        _draggingStableId = null;
    }

    private void TryReparent(int stableId, int? parentId)
    {
        try
        {
            _undo.Execute(_scene, new ReparentGameObjectCommand(stableId, parentId));
        }
        catch (InvalidOperationException)
        {
            // 防环失败由模型拒绝；UI 不吞掉其它异常类型。
        }
    }

    private void DrawContextMenu(int? stableId)
    {
        string popupId = stableId.HasValue ? $"go_context_{stableId.Value}" : "scene_root_context";
        if (stableId.HasValue && ImGui.BeginPopupContextItem(popupId))
        {
            EditorGameObject gameObject = _scene.Get(stableId.Value);
            if (ImGui.MenuItem("Create Child"))
            {
                _undo.Execute(_scene, new CreateGameObjectCommand("GameObject", stableId));
            }

            if (ImGui.MenuItem("Rename"))
            {
                BeginRename(gameObject);
            }

            if (ImGui.MenuItem("Duplicate"))
            {
                _undo.Execute(_scene, new DuplicateGameObjectCommand(stableId.Value));
            }

            if (ImGui.MenuItem("Create Prefab"))
            {
                string assetPath = _prefabs.AllocatePrefabPath(gameObject.Name);
                _undo.Execute(_scene, new CreatePrefabAssetCommand(_prefabs, stableId.Value, assetPath));
            }

            if (ImGui.MenuItem("Delete"))
            {
                _undo.Execute(_scene, new DeleteGameObjectCommand(stableId.Value));
            }

            ImGui.EndPopup();
            return;
        }

        if (!stableId.HasValue && ImGui.BeginPopupContextWindow("scene_root_context_window", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            if (ImGui.MenuItem("Create GameObject"))
            {
                _undo.Execute(_scene, new CreateGameObjectCommand("GameObject"));
            }

            ImGui.EndPopup();
        }
    }
}
