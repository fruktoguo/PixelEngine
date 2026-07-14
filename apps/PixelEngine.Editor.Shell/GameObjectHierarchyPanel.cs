using Hexa.NET.ImGui;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Hierarchy 面板：场景树选择与 GameObject 操作。
/// </summary>
internal sealed class GameObjectHierarchyPanel(
    EditorSceneModel scene,
    EditorUndoStack undo,
    EditorPrefabAssetStore prefabs,
    Func<SceneHierarchySnapshot>? runtimeSnapshot = null,
    Func<EditorMode>? modeProvider = null,
    Func<AuthoringWorldPreviewSnapshot>? authoringWorldSnapshot = null) : IEditorPanel
{
    private readonly EditorSceneModel _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly EditorUndoStack _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    private readonly EditorPrefabAssetStore _prefabs = prefabs ?? throw new ArgumentNullException(nameof(prefabs));
    private readonly Func<SceneHierarchySnapshot>? _runtimeSnapshot = runtimeSnapshot;
    private readonly Func<EditorMode>? _modeProvider = modeProvider;
    private readonly Func<AuthoringWorldPreviewSnapshot>? _authoringWorldSnapshot = authoringWorldSnapshot;
    private int _renameTarget;
    private int? _draggingStableId;
    private string _renameBuffer = string.Empty;
    private string _search = string.Empty;

    public string Title => EditorDockSpace.SceneHierarchyWindowTitle;

    public bool Visible { get; set; } = true;

    public void Draw(in EditorContext context)
    {
        string windowTitle = $"{EditorLocalization.Get("window.hierarchy", "Hierarchy")}###{Title}";
        if (!ImGui.Begin(windowTitle))
        {
            ImGui.End();
            return;
        }

        SyncSelection(context.Selection);
        bool canModify = CanModifyAuthoringScene();
        if (!canModify)
        {
            _renameTarget = 0;
            _renameBuffer = string.Empty;
            _draggingStableId = null;
        }

        DrawToolbar(canModify);
        if (!canModify)
        {
            ImGui.TextDisabled("Play/Paused：Authoring 层级只读");
        }

        ImGui.Separator();
        DrawSceneStateHeader(canModify);
        ImGuiTreeNodeFlags sceneFlags = ImGuiTreeNodeFlags.DefaultOpen |
            ImGuiTreeNodeFlags.OpenOnArrow |
            ImGuiTreeNodeFlags.SpanAvailWidth;
        if (_scene.SelectedStableId is null &&
            context.Selection.AssetPath is null &&
            context.Selection.FolderPath is null)
        {
            sceneFlags |= ImGuiTreeNodeFlags.Selected;
        }

        float stateColumnSize = ImGui.GetFrameHeight();
        ImGui.Dummy(new Vector2((stateColumnSize * 2f) + ImGui.GetStyle().ItemSpacing.X, stateColumnSize));
        ImGui.SameLine();
        Vector2 sceneRowMin = ImGui.GetCursorScreenPos();
        bool sceneOpen = ImGui.TreeNodeEx(
            $"   {_scene.Name}{(_scene.IsDirty ? "*" : string.Empty)}##scene_root",
            sceneFlags);
        DrawObjectIcon(sceneRowMin, scene: true, enabled: true);
        if (ImGui.IsItemClicked())
        {
            _scene.Select(null);
            context.Selection.Clear();
        }

        if (canModify)
        {
            DrawRootDropTarget();
        }

        if (sceneOpen)
        {
            for (int i = 0; i < _scene.RootIds.Count; i++)
            {
                DrawNode(_scene.RootIds[i], context.Selection, canModify);
            }

            DrawGeneratedMarkers();
            DrawRuntimeHierarchy(context.Selection);
            ImGui.TreePop();
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _draggingStableId = null;
        }

        if (canModify)
        {
            DrawContextMenu(null);
        }

        ImGui.End();
    }

    private void DrawGeneratedMarkers()
    {
        SceneAuthoringPreview preview = SceneAuthoringPreviewBuilder.Build(
            _scene,
            _authoringWorldSnapshot?.Invoke() ?? default);
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

    private void DrawRuntimeHierarchy(EditorSelection selection)
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
            bool selected = string.Equals(selection.EntityHandle, entity.Handle, StringComparison.Ordinal);
            if (ImGui.Selectable(
                $"{entity.DisplayName}  [{entity.ComponentCount} components]##runtime_entity_{entity.Handle}",
                selected))
            {
                _scene.Select(null);
                selection.SelectEntity(entity.Handle);
            }
        }

        for (int i = 0; i < snapshot.Bodies.Count; i++)
        {
            SceneHierarchyBodyItem body = snapshot.Bodies[i];
            bool selected = selection.BodyId == body.BodyKey;
            if (ImGui.Selectable(
                $"{body.DisplayName}  ({body.X:0.0}, {body.Y:0.0})##runtime_body_{body.BodyKey}",
                selected))
            {
                _scene.Select(null);
                selection.SelectBody(body.BodyKey);
            }
        }

        ImGui.TreePop();
    }

    internal void SyncSelection(EditorSelection selection)
    {
        if (_modeProvider?.Invoke() == EditorMode.Edit &&
            (!string.IsNullOrWhiteSpace(selection.EntityHandle) || selection.BodyId.HasValue))
        {
            _scene.Select(null);
            selection.Clear();
            return;
        }

        if (!string.IsNullOrWhiteSpace(selection.AssetPath) ||
            selection.FolderPath is not null ||
            !string.IsNullOrWhiteSpace(selection.EntityHandle) ||
            selection.BodyId.HasValue)
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

        if (selection.GameObjectStableId.HasValue)
        {
            selection.Clear();
        }

        if (_scene.SelectedStableId.HasValue)
        {
            selection.SelectGameObject(_scene.SelectedStableId.Value);
        }
    }

    private bool CanModifyAuthoringScene()
    {
        return (_modeProvider?.Invoke() ?? EditorMode.Edit) == EditorMode.Edit;
    }

    private void DrawToolbar(bool canModify)
    {
        ImGui.BeginDisabled(!canModify);
        if (ImGui.Button("+##hierarchy-create"))
        {
            ImGui.OpenPopup("hierarchy-create-menu");
        }

        bool createHovered = ImGui.IsItemHovered();

        if (ImGui.BeginPopup("hierarchy-create-menu"))
        {
            if (ImGui.MenuItem("Create Empty"))
            {
                _undo.Execute(_scene, new CreateGameObjectCommand("GameObject"));
            }

            bool hasParent = _scene.SelectedStableId.HasValue;
            if (ImGui.MenuItem("Create Empty Child", string.Empty, selected: false, enabled: hasParent))
            {
                _undo.Execute(_scene, new CreateGameObjectCommand("GameObject", _scene.SelectedStableId));
            }

            ImGui.EndPopup();
        }

        if (createHovered)
        {
            ImGui.SetTooltip("Create GameObject");
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        _ = ImGui.InputTextWithHint("##hierarchy-search", "Search", ref _search, 128);
    }

    private void DrawSceneStateHeader(bool canModify)
    {
        bool allVisible = true;
        bool allPickable = true;
        foreach (EditorGameObject gameObject in _scene.EnumerateDepthFirst())
        {
            allVisible &= gameObject.SceneVisible;
            allPickable &= gameObject.ScenePickable;
        }

        ImGui.BeginDisabled(!canModify);
        if (DrawVisibilityToggle("hierarchy-visible-all", allVisible, allObjects: true))
        {
            _scene.SetAllSceneVisible(!allVisible);
        }

        ImGui.SameLine();
        if (DrawPickingToggle("hierarchy-pickable-all", allPickable, allObjects: true))
        {
            _scene.SetAllScenePickable(!allPickable);
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextDisabled("Name");
        ImGui.Separator();
    }

    private void DrawNode(int stableId, EditorSelection selection, bool canModify)
    {
        EditorGameObject gameObject = _scene.Get(stableId);
        if (!MatchesSearch(_scene, stableId, _search))
        {
            return;
        }

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (!gameObject.ParentId.HasValue || !string.IsNullOrWhiteSpace(_search))
        {
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        }

        if (gameObject.Children.Count == 0)
        {
            flags |= ImGuiTreeNodeFlags.Leaf;
        }

        if (_scene.SelectedStableId == stableId)
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        ImGui.BeginDisabled(!canModify);
        if (DrawVisibilityToggle($"hierarchy-visible-{stableId}", gameObject.SceneVisible, allObjects: false))
        {
            _scene.SetSceneVisible(stableId, !gameObject.SceneVisible);
        }

        ImGui.SameLine();
        if (DrawPickingToggle($"hierarchy-pickable-{stableId}", gameObject.ScenePickable, allObjects: false))
        {
            _scene.SetScenePickable(stableId, !gameObject.ScenePickable);
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        bool effectivelyVisible = _scene.IsSceneVisible(stableId);
        bool dimmed = !gameObject.Enabled || !effectivelyVisible;
        if (dimmed)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        }

        Vector2 rowMin = ImGui.GetCursorScreenPos();
        bool open = ImGui.TreeNodeEx($"   {gameObject.Name}##go_{stableId}", flags);
        DrawObjectIcon(rowMin, scene: false, enabled: gameObject.Enabled && effectivelyVisible);
        if (dimmed)
        {
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemClicked())
        {
            Select(stableId, selection);
        }

        if (canModify)
        {
            TrackManualDrag(stableId, gameObject.Name);
            DrawManualDropTarget(stableId);
            DrawContextMenu(stableId);
            if (_renameTarget == stableId)
            {
                DrawRenameInline(stableId);
            }
        }

        if (open)
        {
            for (int i = 0; i < gameObject.Children.Count; i++)
            {
                DrawNode(gameObject.Children[i], selection, canModify);
            }

            ImGui.TreePop();
        }
    }

    internal static bool MatchesSearch(EditorSceneModel scene, int stableId, string? search)
    {
        ArgumentNullException.ThrowIfNull(scene);
        EditorGameObject gameObject = scene.Get(stableId);
        string query = search?.Trim() ?? string.Empty;
        if (query.Length == 0 || gameObject.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (int i = 0; i < gameObject.Children.Count; i++)
        {
            if (MatchesSearch(scene, gameObject.Children[i], query))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DrawVisibilityToggle(string id, bool visible, bool allObjects)
    {
        float size = ImGui.GetFrameHeight();
        Vector2 min = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton($"##{id}", new Vector2(size));
        bool hovered = ImGui.IsItemHovered();
        uint color = visible ? 0xFFD7D7D7 : 0xFF777777;
        if (hovered)
        {
            color = 0xFFFFFFFF;
            ImGui.SetTooltip(allObjects
                ? visible ? "Hide all objects in Scene View" : "Show all objects in Scene View"
                : visible ? "Hide in Scene View" : "Show in Scene View");
        }

        Vector2 center = min + new Vector2(size * 0.5f);
        float unit = size / 16f;
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.AddLine(center + new Vector2(-unit * 5f, 0f), center + new Vector2(0f, -unit * 3f), color, 1.2f);
        drawList.AddLine(center + new Vector2(0f, -unit * 3f), center + new Vector2(unit * 5f, 0f), color, 1.2f);
        drawList.AddLine(center + new Vector2(unit * 5f, 0f), center + new Vector2(0f, unit * 3f), color, 1.2f);
        drawList.AddLine(center + new Vector2(0f, unit * 3f), center + new Vector2(-unit * 5f, 0f), color, 1.2f);
        drawList.AddCircleFilled(center, unit * 1.5f, color);
        if (!visible)
        {
            drawList.AddLine(
                center + new Vector2(-unit * 4f, -unit * 4f),
                center + new Vector2(unit * 4f, unit * 4f),
                color,
                1.5f);
        }

        return clicked;
    }

    private static bool DrawPickingToggle(string id, bool pickable, bool allObjects)
    {
        float size = ImGui.GetFrameHeight();
        Vector2 min = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton($"##{id}", new Vector2(size));
        bool hovered = ImGui.IsItemHovered();
        uint color = pickable ? 0xFFD7D7D7 : 0xFF777777;
        if (hovered)
        {
            color = 0xFFFFFFFF;
            ImGui.SetTooltip(allObjects
                ? pickable ? "Disable picking for all objects" : "Enable picking for all objects"
                : pickable ? "Disable Scene picking" : "Enable Scene picking");
        }

        Vector2 center = min + new Vector2(size * 0.5f);
        float unit = size / 16f;
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 palmMin = center + new Vector2(-unit * 2.5f, -unit * 0.5f);
        Vector2 palmMax = center + new Vector2(unit * 3.5f, unit * 4.5f);
        drawList.AddRect(palmMin, palmMax, color, unit, ImDrawFlags.None, 1.2f);
        for (int finger = 0; finger < 4; finger++)
        {
            float x = center.X + ((finger - 1.5f) * unit * 1.6f);
            float top = center.Y - (finger is 1 or 2 ? unit * 4.5f : unit * 3.5f);
            drawList.AddLine(new Vector2(x, center.Y), new Vector2(x, top), color, 1.2f);
        }

        drawList.AddLine(
            center + new Vector2(-unit * 2.5f, unit * 1.5f),
            center + new Vector2(-unit * 5f, -unit * 0.5f),
            color,
            1.2f);
        if (!pickable)
        {
            drawList.AddLine(
                center + new Vector2(-unit * 5f, -unit * 5f),
                center + new Vector2(unit * 5f, unit * 5f),
                color,
                1.5f);
        }

        return clicked;
    }

    private static void DrawObjectIcon(Vector2 rowMin, bool scene, bool enabled)
    {
        float frameHeight = ImGui.GetFrameHeight();
        float size = MathF.Max(6f, frameHeight * 0.42f);
        Vector2 center = rowMin + new Vector2(frameHeight + (size * 0.5f), frameHeight * 0.5f);
        uint color = enabled ? (scene ? 0xFFB3B7BD : 0xFF8EB8D8) : 0xFF686A6E;
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        if (scene)
        {
            drawList.AddRect(
                center - new Vector2(size * 0.5f),
                center + new Vector2(size * 0.5f),
                color,
                1f,
                ImDrawFlags.None,
                1.2f);
            return;
        }

        drawList.AddRectFilled(
            center - new Vector2(size * 0.5f),
            center + new Vector2(size * 0.5f),
            color,
            1f);
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
