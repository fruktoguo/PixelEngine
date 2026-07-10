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
    private readonly EditorSceneModel _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly EditorUndoStack _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    private readonly MaterialBrushPalettePanel? _brushPanel = brushPanel;
    private readonly SceneAuthoringCamera _camera = new();
    private ImGuizmoOperation _operation = ImGuizmoOperation.Translate;
    private Vector2 _canvasMin;
    private Vector2 _canvasSize;
    private bool _canvasHovered;
    private int _previewVersion = -1;
    private string _previewSceneName = string.Empty;
    private string _framedSceneName = string.Empty;
    private SceneAuthoringPreview _preview = SceneAuthoringPreviewBuilder.Build(EditorSceneModel.Empty());

    public string Title => EditorDockSpace.ViewportWindowTitle;

    public bool Visible
    {
        get;
        set
        {
            field = value;
            if (!value)
            {
                InputFocused = false;
            }
        }
    } = true;

    public bool InputFocused { get; private set; }

    public SceneAuthoringPreview Preview => EnsurePreview();

    public SceneAuthoringCameraSnapshot CameraSnapshot => _camera.Snapshot;

    public void Draw(in EditorContext context)
    {
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            Visible = visible;
            InputFocused = false;
            ImGui.End();
            return;
        }

        Visible = visible;
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
        stableId = 0;
        Vector2 world = _camera.CanvasToWorld(panelPoint);
        float pickRadius = MathF.Max(8f, _camera.CellsPerPixel * 12f);
        float bestDistanceSquared = pickRadius * pickRadius;
        foreach (EditorGameObject gameObject in _scene.EnumerateDepthFirst())
        {
            EditorSceneTransform transform = _scene.ComputeWorldTransform(gameObject.StableId);
            float dx = transform.X - world.X;
            float dy = transform.Y - world.Y;
            float distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared <= bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                stableId = gameObject.StableId;
            }
        }

        return stableId != 0;
    }

    private void DrawToolbar(EditorSelection selection)
    {
        if (ImGui.Button("W"))
        {
            _operation = ImGuizmoOperation.Translate;
        }

        ImGui.SameLine();
        if (ImGui.Button("E"))
        {
            _operation = ImGuizmoOperation.RotateZ;
        }

        ImGui.SameLine();
        if (ImGui.Button("R"))
        {
            _operation = ImGuizmoOperation.Scale;
        }

        ImGui.SameLine();
        if (ImGui.Button("Frame All"))
        {
            _ = FrameAll();
        }

        ImGui.SameLine();
        if (ImGui.Button("Frame Selected"))
        {
            _ = FrameSelected(selection);
        }

        SceneAuthoringPreview preview = EnsurePreview();
        ImGui.SameLine();
        ImGui.TextUnformatted($"{preview.StatusLabel} · {preview.SceneName}");
        if (ImGui.IsKeyPressed(ImGuiKey.W))
        {
            _operation = ImGuizmoOperation.Translate;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.E))
        {
            _operation = ImGuizmoOperation.RotateZ;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.R))
        {
            _operation = ImGuizmoOperation.Scale;
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
        DrawGrid(drawList);
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
            drawList.AddCircleFilled(screen, selected ? 6f : 4f, color);
            drawList.AddText(screen + new Vector2(8f, -8f), color, marker.Name);
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
        if (!_canvasHovered || (hasSelection && IsGizmoCapturingMouse()))
        {
            return;
        }

        Vector2 mouse = ImGui.GetIO().MousePos;
        bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        if (clicked && TryPick(mouse - _canvasMin, out int stableId))
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

        if (_brushPanel is not null && (clicked || ImGui.IsMouseDragging(ImGuiMouseButton.Left)))
        {
            Vector2 world = _camera.CanvasToWorld(mouse - _canvasMin);
            _ = _brushPanel.ApplyAt((int)MathF.Round(world.X), (int)MathF.Round(world.Y));
        }
    }

    private static bool IsGizmoCapturingMouse()
    {
        return ImGuizmo.IsUsing() || ImGuizmo.IsOver();
    }

    private void DrawGizmo(EditorSelection selection)
    {
        int? stableId = selection.GameObjectStableId ?? _scene.SelectedStableId;
        if (!stableId.HasValue || !_scene.TryGet(stableId.Value, out EditorGameObject? gameObject))
        {
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
        if (ImGuizmo.Manipulate(ref view, ref projection, _operation, ImGuizmoMode.Local, ref model))
        {
            EditorSceneTransform next = DecomposeModel(model, gameObject.Transform);
            _undo.Execute(_scene, new SetTransformCommand(gameObject.StableId, next));
        }
    }

    private SceneAuthoringPreview EnsurePreview()
    {
        if (_previewVersion == _scene.Version && string.Equals(_previewSceneName, _scene.Name, StringComparison.Ordinal))
        {
            return _preview;
        }

        _preview = SceneAuthoringPreviewBuilder.Build(_scene);
        _previewVersion = _scene.Version;
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
}
