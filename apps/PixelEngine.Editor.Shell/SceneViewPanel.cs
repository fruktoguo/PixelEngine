using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

internal sealed class SceneViewPanel(
    Func<RenderViewportTexture> textureProvider,
    ScriptCameraApi camera,
    EditorSceneModel scene,
    EditorUndoStack undo,
    MaterialBrushPalettePanel? brushPanel = null) : IEditorPanel
{
    private readonly Func<RenderViewportTexture> _textureProvider = textureProvider ?? throw new ArgumentNullException(nameof(textureProvider));
    private readonly ScriptCameraApi _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly EditorSceneModel _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly EditorUndoStack _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    private readonly MaterialBrushPalettePanel? _brushPanel = brushPanel;
    private ImGuizmoOperation _operation = ImGuizmoOperation.Translate;
    private Vector2 _lastImageMin;
    private Vector2 _lastImageSize;
    private RenderViewportTexture _lastTexture;

    public string Title => EditorDockSpace.ViewportWindowTitle;

    public bool Visible { get; set; } = true;

    public void Draw(in EditorContext context)
    {
        _lastTexture = _textureProvider();
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        DrawToolbar();
        if (!_lastTexture.IsValid)
        {
            ImGui.TextUnformatted("等待渲染纹理");
            ImGui.End();
            return;
        }

        DrawTexture();
        HandleCameraInput();
        HandleSceneMouse(context.Selection);
        DrawGizmo(context.Selection);
        ImGui.End();
    }

    internal bool TryPick(Vector2 panelPoint, out int stableId)
    {
        stableId = 0;
        Point2F world = PanelToWorld(panelPoint);
        float bestDistanceSquared = 144f;
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

    private void DrawToolbar()
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

    private void DrawTexture()
    {
        Vector2 available = ImGui.GetContentRegionAvail();
        Vector2 size = ViewportPanel.FitTexture(_lastTexture.Width, _lastTexture.Height, available);
        _lastImageMin = ImGui.GetCursorScreenPos();
        _lastImageSize = size;
        ImGui.Image(ViewportPanel.CreateTextureRef(_lastTexture.Handle), size, new Vector2(0f, 1f), new Vector2(1f, 0f));
    }

    private void HandleCameraInput()
    {
        if (!IsMouseOverImage())
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (io.MouseWheel != 0f)
        {
            float zoom = Math.Clamp(_camera.Zoom * MathF.Pow(1.12f, io.MouseWheel), 0.1f, 16f);
            _camera.SetZoom(zoom);
        }

        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right))
        {
            Vector2 delta = io.MouseDelta;
            float textureScaleX = _lastTexture.Width / MathF.Max(1f, _lastImageSize.X);
            float textureScaleY = _lastTexture.Height / MathF.Max(1f, _lastImageSize.Y);
            CameraSnapshot snapshot = _camera.Snapshot();
            _camera.SetCenter(
                _camera.CenterX - (delta.X * textureScaleX * snapshot.CellsPerPixel),
                _camera.CenterY - (delta.Y * textureScaleY * snapshot.CellsPerPixel));
        }
    }

    private void HandleSceneMouse(EditorSelection selection)
    {
        bool hasSelection = selection.GameObjectStableId.HasValue || _scene.SelectedStableId.HasValue;
        if ((ImGui.GetIO().WantCaptureMouse && !IsMouseOverImage()) ||
            !IsMouseOverImage() ||
            (hasSelection && IsGizmoCapturingMouse()))
        {
            return;
        }

        Vector2 mouse = ImGui.GetIO().MousePos;
        bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        if (clicked && TryPick(mouse - _lastImageMin, out int stableId))
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

        if (_brushPanel is not null &&
            (clicked || ImGui.IsMouseDragging(ImGuiMouseButton.Left)))
        {
            Point2F world = PanelToWorld(mouse - _lastImageMin);
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

        CameraSnapshot snapshot = _camera.Snapshot();
        float widthCells = _lastTexture.Width * snapshot.CellsPerPixel;
        float heightCells = _lastTexture.Height * snapshot.CellsPerPixel;
        Matrix4x4 view = Matrix4x4.Identity;
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(
            snapshot.OriginWorldX,
            snapshot.OriginWorldX + widthCells,
            snapshot.OriginWorldY + heightCells,
            snapshot.OriginWorldY,
            -1f,
            1f);
        EditorSceneTransform world = _scene.ComputeWorldTransform(gameObject.StableId);
        Matrix4x4 model = ComposeModel(world);

        ImGuizmo.BeginFrame();
        ImGuizmo.SetOrthographic(true);
        ImGuizmo.SetDrawlist();
        ImGuizmo.SetRect(_lastImageMin.X, _lastImageMin.Y, _lastImageSize.X, _lastImageSize.Y);
        if (ImGuizmo.Manipulate(ref view, ref projection, _operation, ImGuizmoMode.Local, ref model))
        {
            EditorSceneTransform next = DecomposeModel(model, gameObject.Transform);
            _undo.Execute(_scene, new SetTransformCommand(gameObject.StableId, next));
        }
    }

    private Point2F PanelToWorld(Vector2 panelPoint)
    {
        CameraSnapshot snapshot = _camera.Snapshot();
        float textureX = panelPoint.X * _lastTexture.Width / MathF.Max(1f, _lastImageSize.X);
        float textureY = panelPoint.Y * _lastTexture.Height / MathF.Max(1f, _lastImageSize.Y);
        return new Point2F(
            snapshot.OriginWorldX + (textureX * snapshot.CellsPerPixel),
            snapshot.OriginWorldY + (textureY * snapshot.CellsPerPixel));
    }

    private bool IsMouseOverImage()
    {
        Vector2 mouse = ImGui.GetIO().MousePos;
        return mouse.X >= _lastImageMin.X &&
            mouse.Y >= _lastImageMin.Y &&
            mouse.X <= _lastImageMin.X + _lastImageSize.X &&
            mouse.Y <= _lastImageMin.Y + _lastImageSize.Y;
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
