using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Scene View ImGui 面板：使用独立 authoring 相机绘制声明式/受控 procedural preview 与编辑 overlay。
/// </summary>
internal sealed class SceneViewPanel(
    EditorSceneModel scene,
    EditorUndoStack undo,
    MaterialBrushPalettePanel? brushPanel = null) : IEditorPanel
{
    private const uint CanvasColor = 0xFF_18_1A_1F;
    private const uint WorldColor = 0xFF_25_2A_34;
    private const uint CaveColor = 0xFF_31_35_3D;
    private const uint GroundColor = 0xFF_49_4B_43;
    private const uint LavaColor = 0xFF_31_62_E8;
    private const uint GridMinorColor = 0x24_FF_FF_FF;
    private const uint GridMajorColor = 0x48_FF_FF_FF;
    private const uint BoundaryColor = 0xFF_8B_A4_B8;
    private const uint ObjectColor = 0xFF_EA_C2_69;
    private const uint SelectedColor = 0xFF_66_E8_FF;
    private const uint SpawnColor = 0xFF_74_D6_7A;
    private const uint GoalColor = 0xFF_72_D8_FF;
    private const uint TestColor = 0xFF_AE_83_F2;
    private const uint ToolbarSelectedColor = 0xFF_C5_85_3B;
    private const uint ToolbarHoveredColor = 0xFF_4A_4A_4A;
    private const uint ToolbarIconColor = 0xFF_D2_D2_D2;
    private const uint ToolbarIconDisabledColor = 0xFF_78_78_78;
    private readonly EditorSceneModel _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly EditorUndoStack _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    private readonly MaterialBrushPalettePanel? _brushPanel = brushPanel;
    private readonly SceneAuthoringCamera _camera = new();
    private Vector2 _canvasMin;
    private Vector2 _canvasSize;
    private bool _canvasHovered;
    private int _previewVersion = -1;
    private int _previewSceneViewVersion = -1;
    private string _previewSceneName = string.Empty;
    private string _framedSceneName = string.Empty;
    private SceneAuthoringPreview _preview = SceneAuthoringPreviewBuilder.Build(EditorSceneModel.Empty());
    private int? _gizmoTransactionStableId;
    private long _gizmoTransactionSceneGeneration = -1;
    private EditorSceneTransform? _gizmoBefore;
    private bool _gizmoChanged;
    private EditorMode _preparedMode = EditorMode.Edit;

    public string Title => EditorDockSpace.ViewportWindowTitle;

    public bool Visible
    {
        get;
        set
        {
            bool closing = field && !value;
            field = value;
            if (!value)
            {
                InputFocused = false;
                if (closing)
                {
                    _ = CommitGizmoTransform();
                }
            }
        }
    } = true;

    public bool InputFocused { get; private set; }

    public SceneAuthoringPreview Preview => EnsurePreview();

    public SceneAuthoringCameraSnapshot CameraSnapshot => _camera.Snapshot;

    internal ImGuizmoOperation Operation { get; private set; } = ImGuizmoOperation.Translate;

    internal ImGuizmoMode GizmoMode { get; private set; } = ImGuizmoMode.Local;

    internal bool ShowGrid { get; private set; } = true;

    internal bool MaterialBrushActive => _brushPanel?.IsActive == true;

    /// <summary>
    /// 推进与面板绘制无关的 gizmo 连续编辑生命周期。
    /// </summary>
    /// <remarks>
    /// Scene View 被关闭或切到后台时仍可能发生 selection、mode、对象删除与场景替换，
    /// 因此事务收尾不能只依赖 ImGuizmo 的 IsUsing 边沿。
    /// </remarks>
    internal void PrepareFrame(int? selectedStableId, EditorMode mode)
    {
        _preparedMode = mode;
        if (mode != EditorMode.Edit && MaterialBrushActive)
        {
            _ = SetMaterialBrushActive(false);
        }

        if (!_gizmoTransactionStableId.HasValue)
        {
            return;
        }

        if (!IsGizmoTransactionTargetAlive())
        {
            ClearGizmoTransaction();
            return;
        }

        if (mode != EditorMode.Edit || selectedStableId != _gizmoTransactionStableId)
        {
            _ = CommitGizmoTransform();
        }
    }

    public void Draw(in EditorContext context)
    {
        PrepareFrame(context.Selection.GameObjectStableId ?? _scene.SelectedStableId, _preparedMode);
        if (!ImGui.Begin(Title, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            InputFocused = false;
            ImGui.End();
            return;
        }

        DrawToolbar(context.Selection);
        DrawAuthoringCanvas();
        InputFocused = _canvasHovered && (ImGui.IsWindowHovered() || ImGui.IsWindowFocused());
        HandleCameraInput();
        HandleSceneMouse(context.Selection);
        DrawGizmo(context.Selection);
        ImGui.End();
    }

    internal bool FrameAll()
    {
        SceneAuthoringPreview preview = EnsurePreview();
        _camera.FrameBounds(preview.Bounds);
        _framedSceneName = preview.SceneName;
        return true;
    }

    internal bool FrameSelected(EditorSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        int? stableId = selection.GameObjectStableId ?? _scene.SelectedStableId;
        if (!stableId.HasValue || !_scene.TryGet(stableId.Value, out EditorGameObject? gameObject))
        {
            return false;
        }

        if (!_scene.IsSceneVisible(stableId.Value))
        {
            return false;
        }

        EditorSceneTransform transform = _scene.ComputeWorldTransform(gameObject.StableId);
        _camera.FramePoint(new Vector2(transform.X, transform.Y));
        return true;
    }

    internal bool PrepareCanvas(Vector2 size)
    {
        _canvasSize = new Vector2(MathF.Max(1f, size.X), MathF.Max(1f, size.Y));
        _camera.SetViewport(_canvasSize);
        SceneAuthoringPreview preview = EnsurePreview();
        bool shouldFrame = !string.Equals(_framedSceneName, preview.SceneName, StringComparison.Ordinal) &&
            _canvasSize is { X: >= 64f, Y: >= 64f };
        return shouldFrame && FrameAll();
    }

    internal bool TryPick(Vector2 panelPoint, out int stableId)
    {
        Vector2 world = _camera.CanvasToWorld(panelPoint);
        return TryPickWorld(world, out stableId);
    }

    internal bool TryPickWorld(Vector2 world, out int stableId)
    {
        stableId = 0;
        float pickRadius = MathF.Max(8f, _camera.CellsPerPixel * 12f);
        float bestDistanceSquared = pickRadius * pickRadius;
        SceneAuthoringMarker[] markers = EnsurePreview().Markers;
        for (int i = 0; i < markers.Length; i++)
        {
            SceneAuthoringMarker marker = markers[i];
            if (!marker.StableId.HasValue)
            {
                continue;
            }

            if (!_scene.IsScenePickable(marker.StableId.Value))
            {
                continue;
            }

            float dx = marker.Position.X - world.X;
            float dy = marker.Position.Y - world.Y;
            float distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared <= bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                stableId = marker.StableId.Value;
            }
        }

        return stableId != 0;
    }

    internal bool BeginGizmoTransform(int stableId)
    {
        if (_preparedMode != EditorMode.Edit || MaterialBrushActive)
        {
            _ = CommitGizmoTransform();
            return false;
        }

        if (_gizmoTransactionStableId.HasValue && !IsGizmoTransactionTargetAlive())
        {
            ClearGizmoTransaction();
        }

        if (!_scene.TryGet(stableId, out EditorGameObject? gameObject) ||
            !_scene.IsSceneVisible(stableId) ||
            !_scene.IsScenePickable(stableId))
        {
            return false;
        }

        if (_gizmoTransactionStableId.HasValue && _gizmoTransactionStableId != stableId)
        {
            _ = CommitGizmoTransform();
        }

        if (!_gizmoTransactionStableId.HasValue)
        {
            _gizmoTransactionStableId = stableId;
            _gizmoTransactionSceneGeneration = _scene.SceneGeneration;
            _gizmoBefore = gameObject.Transform.Clone();
            _gizmoChanged = false;
        }

        return true;
    }

    internal bool ApplyGizmoWorldTransform(int stableId, EditorSceneTransform worldTransform)
    {
        ArgumentNullException.ThrowIfNull(worldTransform);
        if (!BeginGizmoTransform(stableId) ||
            !_scene.TryConvertWorldToLocalTransform(stableId, worldTransform, out EditorSceneTransform localTransform))
        {
            return false;
        }

        _scene.SetTransform(stableId, localTransform);
        _gizmoChanged = _gizmoBefore is not null && !TransformEquals(_gizmoBefore, localTransform);
        return true;
    }

    internal bool CommitGizmoTransform()
    {
        if (!_gizmoTransactionStableId.HasValue || _gizmoBefore is null)
        {
            return false;
        }

        int stableId = _gizmoTransactionStableId.Value;
        EditorSceneTransform before = _gizmoBefore;
        if (!IsGizmoTransactionTargetAlive() || !_scene.TryGet(stableId, out EditorGameObject? gameObject))
        {
            ClearGizmoTransaction();
            return false;
        }

        EditorSceneTransform after = gameObject.Transform.Clone();
        bool committed = _gizmoChanged && !TransformEquals(before, after);
        if (!committed)
        {
            ClearGizmoTransaction();
            return false;
        }

        try
        {
            // live drag 已更新画面；释放时只向 Undo 栈提交一条显式 before/after 命令。
            _undo.Execute(_scene, new SetTransformCommand(stableId, before, after));
            return true;
        }
        finally
        {
            // 命令异常也不能把旧 scene/stableId 事务遗留到下一帧。
            ClearGizmoTransaction();
        }
    }

    internal bool CancelGizmoTransform()
    {
        if (!_gizmoTransactionStableId.HasValue || _gizmoBefore is null)
        {
            return false;
        }

        int stableId = _gizmoTransactionStableId.Value;
        if (!IsGizmoTransactionTargetAlive() || !_scene.TryGet(stableId, out _))
        {
            ClearGizmoTransaction();
            return false;
        }

        try
        {
            _scene.SetTransform(stableId, _gizmoBefore);
            return true;
        }
        finally
        {
            ClearGizmoTransaction();
        }
    }

    private void DrawToolbar(EditorSelection selection)
    {
        if (SceneToolbarButton(
            SceneToolbarIcon.Move,
            !MaterialBrushActive && Operation == ImGuizmoOperation.Translate,
            enabled: true,
            "Move (W)"))
        {
            SetOperation(ImGuizmoOperation.Translate);
        }

        ImGui.SameLine(0f, 2f);
        if (SceneToolbarButton(
            SceneToolbarIcon.Rotate,
            !MaterialBrushActive && Operation == ImGuizmoOperation.RotateZ,
            enabled: true,
            "Rotate (E)"))
        {
            SetOperation(ImGuizmoOperation.RotateZ);
        }

        ImGui.SameLine(0f, 2f);
        if (SceneToolbarButton(
            SceneToolbarIcon.Scale,
            !MaterialBrushActive && Operation == ImGuizmoOperation.Scale,
            enabled: true,
            "Scale (R)"))
        {
            SetOperation(ImGuizmoOperation.Scale);
        }

        ImGui.SameLine(0f, 2f);
        if (SceneToolbarButton(
            SceneToolbarIcon.Brush,
            MaterialBrushActive,
            _brushPanel is not null && _preparedMode == EditorMode.Edit,
            "Material Brush (B)"))
        {
            _ = SetMaterialBrushActive(true);
        }

        ImGui.SameLine(0f, 8f);
        if (SceneToolbarButton(SceneToolbarIcon.FrameAll, selected: false, enabled: true, "Frame All"))
        {
            _ = FrameAll();
        }

        int? selectedStableId = selection.GameObjectStableId ?? _scene.SelectedStableId;
        bool canFrameSelected = selectedStableId.HasValue && _scene.TryGet(selectedStableId.Value, out _);
        ImGui.SameLine(0f, 2f);
        if (SceneToolbarButton(
            SceneToolbarIcon.FrameSelected,
            selected: false,
            canFrameSelected,
            "Frame Selected"))
        {
            _ = FrameSelected(selection);
        }

        ImGui.SameLine(0f, 8f);
        if (SceneToolbarButton(SceneToolbarIcon.Grid, ShowGrid, enabled: true, "Show Grid"))
        {
            ToggleGrid();
        }

        ImGui.SameLine(0f, 4f);
        if (ImGui.Button(GizmoMode == ImGuizmoMode.Local ? "Local" : "Global"))
        {
            ToggleGizmoMode();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Toggle Local / Global gizmo orientation");
        }

        SceneAuthoringPreview preview = EnsurePreview();
        ImGui.SameLine();
        ImGui.TextUnformatted($"{preview.StatusLabel} · {preview.SceneName}");
        ImGuiIOPtr io = ImGui.GetIO();
        if (SceneToolShortcutPolicy.IsAllowed(
            io.WantTextInput,
            io.KeyCtrl,
            io.KeyShift,
            io.KeyAlt,
            io.KeySuper))
        {
            if (ImGui.IsKeyPressed(ImGuiKey.W))
            {
                SetOperation(ImGuizmoOperation.Translate);
            }

            if (ImGui.IsKeyPressed(ImGuiKey.E))
            {
                SetOperation(ImGuizmoOperation.RotateZ);
            }

            if (ImGui.IsKeyPressed(ImGuiKey.R))
            {
                SetOperation(ImGuizmoOperation.Scale);
            }

            if (ImGui.IsKeyPressed(ImGuiKey.B))
            {
                _ = SetMaterialBrushActive(true);
            }
        }
    }

    internal void SetOperation(ImGuizmoOperation operation)
    {
        if (operation is not ImGuizmoOperation.Translate and
            not ImGuizmoOperation.RotateZ and
            not ImGuizmoOperation.Scale)
        {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "Scene View 仅支持 Move、Rotate Z 与 Scale gizmo。");
        }

        _ = SetMaterialBrushActive(false);
        Operation = operation;
    }

    internal bool SetMaterialBrushActive(bool active)
    {
        if (_brushPanel is null)
        {
            return false;
        }

        if (active && _preparedMode != EditorMode.Edit)
        {
            _brushPanel.SetActive(false);
            return false;
        }

        if (active && _gizmoTransactionStableId.HasValue)
        {
            _ = CommitGizmoTransform();
        }

        _brushPanel.SetActive(active);
        return _brushPanel.IsActive == active;
    }

    internal void ToggleGrid()
    {
        ShowGrid = !ShowGrid;
    }

    internal void ToggleGizmoMode()
    {
        GizmoMode = GizmoMode == ImGuizmoMode.Local ? ImGuizmoMode.World : ImGuizmoMode.Local;
    }

    private static bool SceneToolbarButton(
        SceneToolbarIcon icon,
        bool selected,
        bool enabled,
        string tooltip)
    {
        float size = ImGui.GetFrameHeight();
        Vector2 min = ImGui.GetCursorScreenPos();
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }

        bool clicked = ImGui.InvisibleButton($"scene-toolbar-{icon}", new Vector2(size));
        bool hovered = ImGui.IsItemHovered();
        if (!enabled)
        {
            ImGui.EndDisabled();
        }

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        if (selected || (hovered && enabled))
        {
            drawList.AddRectFilled(
                min,
                min + new Vector2(size),
                selected ? ToolbarSelectedColor : ToolbarHoveredColor,
                2f);
        }

        DrawToolbarIcon(
            drawList,
            icon,
            min + new Vector2(size * 0.5f),
            size / 16f,
            enabled ? ToolbarIconColor : ToolbarIconDisabledColor);
        if (hovered)
        {
            ImGui.SetTooltip(tooltip);
        }

        return clicked && enabled;
    }

    private static void DrawToolbarIcon(
        ImDrawListPtr drawList,
        SceneToolbarIcon icon,
        Vector2 center,
        float unit,
        uint color)
    {
        float thickness = MathF.Max(1f, unit * 1.2f);
        switch (icon)
        {
            case SceneToolbarIcon.Move:
                drawList.AddLine(center + new Vector2(-unit * 4f, 0f), center + new Vector2(unit * 4f, 0f), color, thickness);
                drawList.AddLine(center + new Vector2(0f, unit * 4f), center + new Vector2(0f, -unit * 4f), color, thickness);
                drawList.AddTriangleFilled(
                    center + new Vector2(unit * 5f, 0f),
                    center + new Vector2(unit * 2.5f, -unit * 1.6f),
                    center + new Vector2(unit * 2.5f, unit * 1.6f),
                    color);
                drawList.AddTriangleFilled(
                    center + new Vector2(0f, -unit * 5f),
                    center + new Vector2(-unit * 1.6f, -unit * 2.5f),
                    center + new Vector2(unit * 1.6f, -unit * 2.5f),
                    color);
                break;
            case SceneToolbarIcon.Rotate:
                drawList.AddCircle(center, unit * 4.2f, color, 16, thickness);
                drawList.AddTriangleFilled(
                    center + new Vector2(unit * 4.8f, -unit * 2.4f),
                    center + new Vector2(unit * 1.8f, -unit * 2.5f),
                    center + new Vector2(unit * 3.9f, unit * 0.1f),
                    color);
                break;
            case SceneToolbarIcon.Scale:
                drawList.AddLine(
                    center + new Vector2(-unit * 3.5f, unit * 3.5f),
                    center + new Vector2(unit * 3.5f, -unit * 3.5f),
                    color,
                    thickness);
                drawList.AddRectFilled(
                    center + new Vector2(unit * 2.2f, -unit * 4.8f),
                    center + new Vector2(unit * 4.8f, -unit * 2.2f),
                    color);
                drawList.AddRect(
                    center + new Vector2(-unit * 4.8f, unit * 2.2f),
                    center + new Vector2(-unit * 2.2f, unit * 4.8f),
                    color,
                    0f,
                    ImDrawFlags.None,
                    thickness);
                break;
            case SceneToolbarIcon.Brush:
                drawList.AddLine(
                    center + new Vector2(-unit * 4.2f, unit * 4.2f),
                    center + new Vector2(unit * 2.2f, -unit * 2.2f),
                    color,
                    thickness * 1.4f);
                drawList.AddTriangleFilled(
                    center + new Vector2(unit * 1.2f, -unit * 3.2f),
                    center + new Vector2(unit * 4.8f, -unit * 4.8f),
                    center + new Vector2(unit * 3.2f, -unit * 1.2f),
                    color);
                drawList.AddTriangleFilled(
                    center + new Vector2(-unit * 4.8f, unit * 4.8f),
                    center + new Vector2(-unit * 1.7f, unit * 4.1f),
                    center + new Vector2(-unit * 4.1f, unit * 1.7f),
                    color);
                break;
            case SceneToolbarIcon.FrameAll:
                drawList.AddRect(
                    center - new Vector2(unit * 4f),
                    center + new Vector2(unit * 4f),
                    color,
                    0f,
                    ImDrawFlags.None,
                    thickness);
                drawList.AddCircleFilled(center, unit, color);
                break;
            case SceneToolbarIcon.FrameSelected:
                drawList.AddCircle(center, unit * 4f, color, 16, thickness);
                drawList.AddCircleFilled(center, unit * 1.5f, color);
                break;
            case SceneToolbarIcon.Grid:
                for (int i = -1; i <= 1; i++)
                {
                    float offset = i * unit * 3f;
                    drawList.AddLine(
                        center + new Vector2(-unit * 4.5f, offset),
                        center + new Vector2(unit * 4.5f, offset),
                        color,
                        thickness);
                    drawList.AddLine(
                        center + new Vector2(offset, -unit * 4.5f),
                        center + new Vector2(offset, unit * 4.5f),
                        color,
                        thickness);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(icon), icon, "未知 Scene View 工具栏图标。");
        }
    }

    private void DrawAuthoringCanvas()
    {
        _ = PrepareCanvas(ImGui.GetContentRegionAvail());
        _canvasMin = ImGui.GetCursorScreenPos();
        SceneAuthoringPreview preview = EnsurePreview();

        _ = ImGui.InvisibleButton("scene-authoring-canvas", _canvasSize);
        _canvasHovered = ImGui.IsItemHovered();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 canvasMax = _canvasMin + _canvasSize;
        drawList.AddRectFilled(_canvasMin, canvasMax, CanvasColor);
        DrawWorldPreview(drawList, preview);
        if (ShowGrid)
        {
            DrawGrid(drawList);
        }
        DrawBoundary(drawList, preview.Bounds);
        DrawMarkers(drawList, preview);
        DrawCanvasLabel(drawList, preview);
    }

    private void DrawWorldPreview(ImDrawListPtr drawList, SceneAuthoringPreview preview)
    {
        Vector2 min = WorldToScreen(new Vector2(preview.Bounds.X, preview.Bounds.Y));
        Vector2 max = WorldToScreen(new Vector2(preview.Bounds.Right, preview.Bounds.Bottom));
        drawList.AddRectFilled(min, max, WorldColor);
        if (!preview.HasProceduralWorld)
        {
            return;
        }

        float width = preview.Bounds.Width;
        float height = preview.Bounds.Height;
        DrawWorldRect(drawList, new SceneAuthoringBounds(width * 0.08f, height * 0.18f, width * 0.84f, height * 0.64f), CaveColor);
        DrawWorldRect(drawList, new SceneAuthoringBounds(0f, height * 0.82f, width, height * 0.18f), GroundColor);
        DrawWorldRect(drawList, new SceneAuthoringBounds(width * 0.14f, height * 0.91f, width * 0.72f, height * 0.09f), LavaColor);
        DrawWorldRect(drawList, new SceneAuthoringBounds(width * 0.20f, height * 0.62f, width * 0.20f, height * 0.035f), GroundColor);
        DrawWorldRect(drawList, new SceneAuthoringBounds(width * 0.58f, height * 0.54f, width * 0.22f, height * 0.035f), GroundColor);
    }

    private void DrawGrid(ImDrawListPtr drawList)
    {
        SceneAuthoringCameraSnapshot snapshot = _camera.Snapshot;
        float targetCells = snapshot.CellsPerPixel * 48f;
        float step = 8f;
        while (step < targetCells && step < 4096f)
        {
            step *= 2f;
        }

        float right = snapshot.OriginX + (snapshot.ViewportWidth * snapshot.CellsPerPixel);
        float bottom = snapshot.OriginY + (snapshot.ViewportHeight * snapshot.CellsPerPixel);
        float firstX = MathF.Floor(snapshot.OriginX / step) * step;
        float firstY = MathF.Floor(snapshot.OriginY / step) * step;
        for (float x = firstX; x <= right; x += step)
        {
            bool major = MathF.Abs(x % (step * 4f)) < 0.001f;
            float screenX = WorldToScreen(new Vector2(x, 0f)).X;
            drawList.AddLine(
                new Vector2(screenX, _canvasMin.Y),
                new Vector2(screenX, _canvasMin.Y + _canvasSize.Y),
                major ? GridMajorColor : GridMinorColor);
        }

        for (float y = firstY; y <= bottom; y += step)
        {
            bool major = MathF.Abs(y % (step * 4f)) < 0.001f;
            float screenY = WorldToScreen(new Vector2(0f, y)).Y;
            drawList.AddLine(
                new Vector2(_canvasMin.X, screenY),
                new Vector2(_canvasMin.X + _canvasSize.X, screenY),
                major ? GridMajorColor : GridMinorColor);
        }
    }

    private void DrawBoundary(ImDrawListPtr drawList, SceneAuthoringBounds bounds)
    {
        Vector2 min = WorldToScreen(new Vector2(bounds.X, bounds.Y));
        Vector2 max = WorldToScreen(new Vector2(bounds.Right, bounds.Bottom));
        drawList.AddRect(min, max, BoundaryColor, 0f, ImDrawFlags.None, 2f);
    }

    private void DrawMarkers(ImDrawListPtr drawList, SceneAuthoringPreview preview)
    {
        for (int i = 0; i < preview.Markers.Length; i++)
        {
            SceneAuthoringMarker marker = preview.Markers[i];
            Vector2 screen = WorldToScreen(marker.Position);
            bool selected = marker.StableId.HasValue && marker.StableId == _scene.SelectedStableId;
            uint color = selected
                ? SelectedColor
                : marker.Kind switch
                {
                    SceneAuthoringMarkerKind.PlayerSpawn => SpawnColor,
                    SceneAuthoringMarkerKind.Goal => GoalColor,
                    SceneAuthoringMarkerKind.GameObject => ObjectColor,
                    _ => ObjectColor,
                };
            float markerRadius = marker.Kind == SceneAuthoringMarkerKind.GameObject ? 6f : 10f;
            if (marker.Kind == SceneAuthoringMarkerKind.PlayerSpawn)
            {
                drawList.AddTriangleFilled(
                    screen + new Vector2(0f, -markerRadius),
                    screen + new Vector2(markerRadius, markerRadius),
                    screen + new Vector2(-markerRadius, markerRadius),
                    color);
            }
            else if (marker.Kind == SceneAuthoringMarkerKind.Goal)
            {
                drawList.AddRectFilled(
                    screen - new Vector2(markerRadius, markerRadius),
                    screen + new Vector2(markerRadius, markerRadius),
                    color,
                    2f);
            }
            else
            {
                drawList.AddCircleFilled(screen, selected ? 8f : markerRadius, color);
            }

            Vector2 textSize = ImGui.CalcTextSize(marker.Name);
            Vector2 labelMin = screen + new Vector2(14f, -(textSize.Y * 0.5f));
            float labelMinX = _canvasMin.X + 4f;
            float labelMinY = _canvasMin.Y + 4f;
            float labelMaxX = Math.Max(labelMinX, _canvasMin.X + _canvasSize.X - textSize.X - 12f);
            float labelMaxY = Math.Max(labelMinY, _canvasMin.Y + _canvasSize.Y - textSize.Y - 8f);
            labelMin.X = Math.Clamp(labelMin.X, labelMinX, labelMaxX);
            labelMin.Y = Math.Clamp(labelMin.Y, labelMinY, labelMaxY);
            drawList.AddRectFilled(labelMin - new Vector2(4f, 2f), labelMin + textSize + new Vector2(4f, 2f), 0xD9222429, 3f);
            drawList.AddText(labelMin, color, marker.Name);
        }
    }

    private void DrawCanvasLabel(ImDrawListPtr drawList, SceneAuthoringPreview preview)
    {
        uint color = preview.IsTestScene ? TestColor : BoundaryColor;
        string worldKind = preview.HasProceduralWorld
            ? "LevelDirector procedural preview"
            : preview.IsExplicitEmptyScene ? "explicit empty scene" : "object bounds";
        drawList.AddText(
            _canvasMin + new Vector2(10f, 10f),
            color,
            $"{preview.StatusLabel} · {worldKind} · {preview.Bounds.Width:0}×{preview.Bounds.Height:0} cells");
    }

    private void DrawWorldRect(ImDrawListPtr drawList, SceneAuthoringBounds bounds, uint color)
    {
        drawList.AddRectFilled(
            WorldToScreen(new Vector2(bounds.X, bounds.Y)),
            WorldToScreen(new Vector2(bounds.Right, bounds.Bottom)),
            color);
    }

    private void HandleCameraInput()
    {
        if (!_canvasHovered)
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (io.MouseWheel != 0f)
        {
            _camera.ZoomAt(io.MousePos - _canvasMin, io.MouseWheel);
        }

        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right))
        {
            _camera.PanPixels(io.MouseDelta);
        }
    }

    private void HandleSceneMouse(EditorSelection selection)
    {
        bool hasSelection = selection.GameObjectStableId.HasValue || _scene.SelectedStableId.HasValue;
        if (!_canvasHovered || (!MaterialBrushActive && hasSelection && IsGizmoCapturingMouse()))
        {
            return;
        }

        Vector2 mouse = ImGui.GetIO().MousePos;
        bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool dragging = ImGui.IsMouseDragging(ImGuiMouseButton.Left);
        HandleScenePointer(selection, mouse - _canvasMin, clicked, dragging);
    }

    internal void HandleScenePointer(
        EditorSelection selection,
        Vector2 panelPoint,
        bool clicked,
        bool dragging)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!clicked && !dragging)
        {
            return;
        }

        // plan 19：对象工具与世界画刷互斥。画刷激活时左键只编辑世界，
        // 不允许同一次输入再拾取或清空 GameObject selection。
        if (MaterialBrushActive && _preparedMode == EditorMode.Edit)
        {
            Vector2 world = _camera.CanvasToWorld(panelPoint);
            MaterialBrushPalettePanel brushPanel = _brushPanel!;
            SceneAuthoringBounds previewBounds = EnsurePreview().Bounds;
            MaterialBrushBounds brushBounds = new(
                (int)MathF.Ceiling(previewBounds.X),
                (int)MathF.Ceiling(previewBounds.Y),
                (int)MathF.Ceiling(previewBounds.Right) - 1,
                (int)MathF.Ceiling(previewBounds.Bottom) - 1);
            _ = brushPanel.ApplyAt(
                (int)MathF.Round(world.X),
                (int)MathF.Round(world.Y),
                brushBounds);
            return;
        }

        if (!clicked)
        {
            return;
        }

        if (TryPick(panelPoint, out int stableId))
        {
            _scene.Select(stableId);
            selection.SelectGameObject(stableId);
            return;
        }

        if (clicked)
        {
            _scene.Select(null);
            selection.Clear();
        }
    }

    private static bool IsGizmoCapturingMouse()
    {
        return ImGuizmo.IsUsing() || ImGuizmo.IsOver();
    }

    private void DrawGizmo(EditorSelection selection)
    {
        int? stableId = selection.GameObjectStableId ?? _scene.SelectedStableId;
        PrepareFrame(stableId, _preparedMode);
        if (MaterialBrushActive ||
            !stableId.HasValue ||
            !_scene.TryGet(stableId.Value, out EditorGameObject? gameObject) ||
            !_scene.IsSceneVisible(stableId.Value) ||
            !_scene.IsScenePickable(stableId.Value))
        {
            if (_gizmoTransactionStableId.HasValue)
            {
                _ = CommitGizmoTransform();
            }

            return;
        }

        SceneAuthoringCameraSnapshot snapshot = _camera.Snapshot;
        float widthCells = snapshot.ViewportWidth * snapshot.CellsPerPixel;
        float heightCells = snapshot.ViewportHeight * snapshot.CellsPerPixel;
        Matrix4x4 view = Matrix4x4.Identity;
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(
            snapshot.OriginX,
            snapshot.OriginX + widthCells,
            snapshot.OriginY + heightCells,
            snapshot.OriginY,
            -1f,
            1f);
        EditorSceneTransform world = _scene.ComputeWorldTransform(gameObject.StableId);
        Matrix4x4 model = ComposeModel(world);

        ImGuizmo.BeginFrame();
        ImGuizmo.SetOrthographic(true);
        ImGuizmo.SetDrawlist();
        ImGuizmo.SetRect(_canvasMin.X, _canvasMin.Y, _canvasSize.X, _canvasSize.Y);
        bool manipulated = ImGuizmo.Manipulate(ref view, ref projection, Operation, GizmoMode, ref model);
        bool usingGizmo = ImGuizmo.IsUsing();
        if (manipulated)
        {
            EditorSceneTransform nextWorld = DecomposeModel(model, world);
            _ = ApplyGizmoWorldTransform(gameObject.StableId, nextWorld);
        }

        if (_gizmoTransactionStableId.HasValue)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _ = CancelGizmoTransform();
            }
            else if (!usingGizmo)
            {
                _ = CommitGizmoTransform();
            }
        }
    }

    private SceneAuthoringPreview EnsurePreview()
    {
        if (_previewVersion == _scene.Version &&
            _previewSceneViewVersion == _scene.SceneViewVersion &&
            string.Equals(_previewSceneName, _scene.Name, StringComparison.Ordinal))
        {
            return _preview;
        }

        _preview = SceneAuthoringPreviewBuilder.Build(_scene);
        _previewVersion = _scene.Version;
        _previewSceneViewVersion = _scene.SceneViewVersion;
        _previewSceneName = _scene.Name;
        return _preview;
    }

    private Vector2 WorldToScreen(Vector2 world)
    {
        return _canvasMin + _camera.WorldToCanvas(world);
    }

    private static Matrix4x4 ComposeModel(EditorSceneTransform transform)
    {
        return Matrix4x4.CreateScale(transform.ScaleX, transform.ScaleY, 1f) *
            Matrix4x4.CreateRotationZ(transform.RotationRadians) *
            Matrix4x4.CreateTranslation(transform.X, transform.Y, 0f);
    }

    private static EditorSceneTransform DecomposeModel(Matrix4x4 model, EditorSceneTransform fallback)
    {
        return Matrix4x4.Decompose(model, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
            ? new EditorSceneTransform
            {
                X = translation.X,
                Y = translation.Y,
                RotationRadians = MathF.Atan2(
                    2f * ((rotation.W * rotation.Z) + (rotation.X * rotation.Y)),
                    1f - (2f * ((rotation.Y * rotation.Y) + (rotation.Z * rotation.Z)))),
                ScaleX = scale.X,
                ScaleY = scale.Y,
            }
            : fallback.Clone();
    }

    private void ClearGizmoTransaction()
    {
        _gizmoTransactionStableId = null;
        _gizmoTransactionSceneGeneration = -1;
        _gizmoBefore = null;
        _gizmoChanged = false;
    }

    private bool IsGizmoTransactionTargetAlive()
    {
        return _gizmoTransactionStableId.HasValue &&
            _gizmoTransactionSceneGeneration == _scene.SceneGeneration &&
            _scene.TryGet(_gizmoTransactionStableId.Value, out _);
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
}

internal enum SceneToolbarIcon
{
    Move,
    Rotate,
    Scale,
    Brush,
    FrameAll,
    FrameSelected,
    Grid,
}
