using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using PixelEngine.UI;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Scene View ImGui 面板：使用独立 authoring 相机绘制声明式/受控 procedural preview 与编辑 overlay。
/// </summary>
internal sealed class SceneViewPanel(
    EditorSceneModel scene,
    EditorUndoStack undo,
    MaterialBrushPalettePanel? brushPanel = null,
    IAuthoringWorldTexture? worldTexture = null,
    Func<AuthoringWorldPreviewSnapshot>? authoringWorldSnapshot = null,
    SceneWebCanvasAuthoringPreview? webCanvasPreview = null) : IEditorPanel, IDisposable
{
    private const uint CanvasColor = 0xFF_18_1A_1F;
    private const uint WorldColor = 0xFF_25_2A_34;
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
    private const uint GizmoXAxisColor = 0xFF_5C_5C_E8;
    private const uint GizmoYAxisColor = 0xFF_65_CE_64;
    private const uint GizmoUniformColor = 0xFF_54_C8_E8;
    private const uint GizmoHighlightColor = 0xFF_FF_FF_FF;
    private const float GizmoAxisLength = 48f;
    private const float GizmoRotationRadius = 38f;
    private const float ToolOverlayInset = 8f;
    private const string SnapSettingsPopupName = "scene-gizmo-snap-settings";
    private static readonly Vector2 ToolOverlayDesiredSize = new(300f, 390f);
    private readonly EditorSceneModel _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly EditorUndoStack _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    private readonly MaterialBrushPalettePanel? _brushPanel = brushPanel;
    private readonly IAuthoringWorldTexture? _worldTexture = worldTexture;
    private readonly Func<AuthoringWorldPreviewSnapshot>? _authoringWorldSnapshot = authoringWorldSnapshot;
    private readonly SceneWebCanvasAuthoringPreview? _webCanvasPreview = webCanvasPreview;
    private readonly SceneAuthoringCamera _camera = new();
    private Vector2 _canvasMin;
    private Vector2 _canvasSize;
    private bool _canvasHovered;
    private bool _toolOverlayHovered;
    private bool _webCanvasPreviewHovered;
    private bool _cameraAutoFit = true;
    internal SceneToolOverlayDock ToolOverlayDock { get; private set; } = SceneToolOverlayDock.Right;
    internal Vector2 ToolOverlayFloatingOffset { get; private set; } = new(ToolOverlayInset, ToolOverlayInset);
    private int _previewVersion = -1;
    private int _previewSceneViewVersion = -1;
    private long _previewAuthoringWorldVersion = -1;
    private string _previewSceneName = string.Empty;
    private string _framedSceneName = string.Empty;
    private SceneAuthoringPreview _preview = SceneAuthoringPreviewBuilder.Build(EditorSceneModel.Empty());
    private int? _gizmoTransactionStableId;
    private long _gizmoTransactionSceneGeneration = -1;
    private EditorSceneTransform? _gizmoBefore;
    private EditorPrefabLink? _gizmoBeforePrefabLink;
    private bool _gizmoChanged;
    private bool _committingGizmoTransform;
    private bool _gizmoBeforeDirty;
    private SceneGizmoHandle _hoveredGizmoHandle;
    private SceneGizmoHandle _activeGizmoHandle;
    private Vector2 _gizmoDragStartScreen;
    private Vector2 _gizmoDragStartWorldPoint;
    private Vector2 _gizmoDragCenterScreen;
    private EditorSceneTransform? _gizmoDragStartWorldTransform;
    private EditorMode _preparedMode = EditorMode.Edit;
    private int? _preparedSelectedStableId;
    private bool _disposed;

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
                _webCanvasPreview?.Request(null, false, false, 0f, 0f);
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

    internal SceneGizmoSnapSettings SnapSettings { get; private set; } = SceneGizmoSnapSettings.Default;

    internal bool MaterialBrushActive => _brushPanel?.IsActive == true;

    internal void SetToolOverlay(SceneToolOverlayDock dock, Vector2 floatingOffset)
    {
        if (!Enum.IsDefined(dock) ||
            !float.IsFinite(floatingOffset.X) ||
            !float.IsFinite(floatingOffset.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(floatingOffset),
                "Scene tool overlay dock 与 floating offset 必须有效且有限。");
        }

        if (ToolOverlayDock == dock && ToolOverlayFloatingOffset == floatingOffset)
        {
            return;
        }

        ToolOverlayDock = dock;
        ToolOverlayFloatingOffset = floatingOffset;
    }

    internal void InvalidateWorldTexture()
    {
        _worldTexture?.Invalidate();
    }

    /// <summary>
    /// 推进与面板绘制无关的 gizmo 连续编辑生命周期。
    /// </summary>
    /// <remarks>
    /// Scene View 被关闭或切到后台时仍可能发生 selection、mode、对象删除与场景替换，
    /// 因此事务收尾不能只依赖当前帧鼠标 release 边沿。
    /// </remarks>
    internal void PrepareFrame(int? selectedStableId, EditorMode mode)
    {
        _preparedMode = mode;
        _preparedSelectedStableId = selectedStableId;
        if (!Visible || mode != EditorMode.Edit)
        {
            _webCanvasPreview?.Request(null, false, false, 0f, 0f);
        }

        if (_brushPanel is { Visible: false, IsActive: true })
        {
            _brushPanel.SetActive(false);
        }

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
        string windowTitle = EditorLocalization.GetWindowTitle("window.scene", "Scene", Title);
        if (!ImGui.Begin(windowTitle, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            InputFocused = false;
            _webCanvasPreview?.Request(null, false, false, 0f, 0f);
            ImGui.End();
            return;
        }

        DrawToolbar(context.Selection);
        DrawAuthoringCanvas();
        DrawSceneToolOverlay();
        InputFocused = _canvasHovered && (ImGui.IsWindowHovered() || ImGui.IsWindowFocused());
        HandleCameraInput();
        // gizmo 必须先更新本帧 hit-test / active 状态，Scene selection 才能正确让出左键。
        // 否则首次按下 handle 时会被 selection 路径抢走输入。
        DrawGizmo(context.Selection);
        HandleSceneMouse(context.Selection);
        ImGui.End();
    }

    internal bool FrameAll()
    {
        SceneAuthoringPreview preview = EnsurePreview();
        _camera.FrameBounds(preview.Bounds);
        _framedSceneName = preview.SceneName;
        _cameraAutoFit = true;
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
        // Frame Selected 与手动 pan/zoom 一样代表用户主动决定相机取景；后续 dock resize
        // 只改变可见范围，不应再被默认的 Frame All 覆盖。
        _cameraAutoFit = false;
        return true;
    }

    internal bool PrepareCanvas(Vector2 size)
    {
        Vector2 nextCanvasSize = new(MathF.Max(1f, size.X), MathF.Max(1f, size.Y));
        bool viewportChanged = !ApproximatelyEqual(_canvasSize, nextCanvasSize);
        _canvasSize = nextCanvasSize;
        _camera.SetViewport(_canvasSize);
        SceneAuthoringPreview preview = EnsurePreview();
        bool sceneChanged = !string.Equals(_framedSceneName, preview.SceneName, StringComparison.Ordinal);
        bool shouldFrame = _canvasSize is { X: >= 64f, Y: >= 64f } &&
            (sceneChanged || (viewportChanged && _cameraAutoFit));
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

        SceneAuthoringPreview preview = EnsurePreview();
        if (stableId == 0 &&
            preview.WorldOwnerStableId is { } ownerStableId &&
            _scene.IsScenePickable(ownerStableId) &&
            world.X >= preview.Bounds.X && world.X <= preview.Bounds.Right &&
            world.Y >= preview.Bounds.Y && world.Y <= preview.Bounds.Bottom)
        {
            stableId = ownerStableId;
            return true;
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
            _gizmoBeforePrefabLink = gameObject.PrefabLink?.Clone();
            _gizmoBeforeDirty = _scene.IsDirty;
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

        if (_scene.TryGet(stableId, out EditorGameObject? gameObject) &&
            TransformEquals(gameObject.Transform, localTransform))
        {
            _gizmoChanged = _gizmoBefore is not null && !TransformEquals(_gizmoBefore, localTransform);
            return true;
        }

        _scene.SetTransform(stableId, localTransform);
        // Edit projection 会在下一帧刷新 prefab baseline；live drag 必须立即写入 provisional
        // overrides，否则 prefab 实例会在鼠标尚未释放时跳回旧位置。
        _scene.RecordTransformPrefabOverrides(stableId, localTransform);
        _gizmoChanged = _gizmoBefore is not null && !TransformEquals(_gizmoBefore, localTransform);
        return true;
    }

    internal bool CommitGizmoTransform()
    {
        if (_committingGizmoTransform || !_gizmoTransactionStableId.HasValue || _gizmoBefore is null)
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
            _scene.SetPrefabLink(stableId, _gizmoBeforePrefabLink);
            _scene.RestoreDirtyState(_gizmoBeforeDirty);
            ClearGizmoTransaction();
            return false;
        }

        try
        {
            _committingGizmoTransform = true;
            // live drag 已更新画面；释放时只向 Undo 栈提交一条显式 before/after 命令。
            // BeforeOperation 会反向调用 FlushPendingAuthoringEdits；guard 防止该回调把同一手势提交两次。
            _undo.Execute(
                _scene,
                new SetTransformCommand(
                    stableId,
                    before,
                    _gizmoBeforePrefabLink,
                    after,
                    gameObject.PrefabLink));
            return true;
        }
        finally
        {
            _committingGizmoTransform = false;
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
            _scene.SetPrefabLink(stableId, _gizmoBeforePrefabLink);
            _scene.RestoreDirtyState(_gizmoBeforeDirty);
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
            "Frame Selected (F)"))
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

        DrawSnapControls();

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

            if (canFrameSelected && ImGui.IsKeyPressed(ImGuiKey.F))
            {
                _ = FrameSelected(selection);
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

        if (Operation == operation && !MaterialBrushActive)
        {
            return;
        }

        if (_gizmoTransactionStableId.HasValue)
        {
            _ = CommitGizmoTransform();
        }

        ClearGizmoInteraction();
        _ = SetMaterialBrushActive(false);
        Operation = operation;
    }

    internal bool SetMaterialBrushActive(bool active)
    {
        if (_brushPanel is null)
        {
            return false;
        }

        if (_brushPanel.IsActive == active && (!active || _brushPanel.Visible))
        {
            return true;
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

        if (active)
        {
            _brushPanel.Visible = true;
        }

        _brushPanel.SetActive(active);
        return _brushPanel.IsActive == active;
    }

    internal int ApplyMaterialBrushAt(int worldX, int worldY)
    {
        if (_brushPanel is null || !MaterialBrushActive || _preparedMode != EditorMode.Edit)
        {
            return 0;
        }

        SceneAuthoringBounds previewBounds = EnsurePreview().Bounds;
        MaterialBrushBounds brushBounds = new(
            (int)MathF.Ceiling(previewBounds.X),
            (int)MathF.Ceiling(previewBounds.Y),
            (int)MathF.Ceiling(previewBounds.Right) - 1,
            (int)MathF.Ceiling(previewBounds.Bottom) - 1);
        int writes = _brushPanel.ApplyAt(worldX, worldY, brushBounds);
        if (writes > 0)
        {
            InvalidateWorldTexture();
        }

        return writes;
    }

    internal void ToggleGrid()
    {
        ShowGrid = !ShowGrid;
    }

    internal void ToggleGizmoMode()
    {
        if (_gizmoTransactionStableId.HasValue)
        {
            _ = CommitGizmoTransform();
        }

        ClearGizmoInteraction();
        GizmoMode = GizmoMode == ImGuizmoMode.Local ? ImGuizmoMode.World : ImGuizmoMode.Local;
    }

    internal void SetSnapSettings(in SceneGizmoSnapSettings settings)
    {
        if (!settings.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings,
                "Scene gizmo snap 必须使用有限正步长且不超过公共上限。");
        }

        if (SnapSettings == settings)
        {
            return;
        }

        if (_gizmoTransactionStableId.HasValue)
        {
            _ = CommitGizmoTransform();
        }

        ClearGizmoInteraction();
        SnapSettings = settings;
    }

    internal void SetCamera(float centerX, float centerY, float cellsPerPixel)
    {
        SceneAuthoringCameraSnapshot current = _camera.Snapshot;
        if (current.CenterX == centerX &&
            current.CenterY == centerY &&
            current.CellsPerPixel == cellsPerPixel)
        {
            return;
        }

        if (_gizmoTransactionStableId.HasValue)
        {
            _ = CommitGizmoTransform();
        }

        ClearGizmoInteraction();
        _camera.SetView(centerX, centerY, cellsPerPixel);
        _cameraAutoFit = false;
    }

    private void DrawSnapControls()
    {
        ImGui.SameLine(0f, 6f);
        bool enabled = SnapSettings.Enabled;
        if (ImGui.Checkbox("Snap##scene-gizmo-snap", ref enabled))
        {
            SetSnapSettings(SnapSettings with { Enabled = enabled });
        }

        ImGui.SameLine(0f, 2f);
        if (ImGui.SmallButton("...##scene-gizmo-snap-settings"))
        {
            ImGui.OpenPopup(SnapSettingsPopupName);
        }

        if (!ImGui.BeginPopup(SnapSettingsPopupName))
        {
            return;
        }

        float move = SnapSettings.Move;
        float rotationDegrees = SnapSettings.RotationDegrees;
        float scale = SnapSettings.Scale;
        bool changed = ImGui.DragFloat("Move", ref move, 0.1f, "%g");
        changed |= ImGui.DragFloat("Rotate (deg)", ref rotationDegrees, 1f, "%g");
        changed |= ImGui.DragFloat("Scale", ref scale, 0.01f, "%g");
        if (changed && float.IsFinite(move) && float.IsFinite(rotationDegrees) && float.IsFinite(scale))
        {
            SetSnapSettings(new SceneGizmoSnapSettings(
                SnapSettings.Enabled,
                Math.Clamp(move, SceneGizmoSnapSettings.MinimumStep, SceneGizmoSnapSettings.MaximumMove),
                Math.Clamp(
                    rotationDegrees,
                    SceneGizmoSnapSettings.MinimumStep,
                    SceneGizmoSnapSettings.MaximumRotationDegrees),
                Math.Clamp(scale, SceneGizmoSnapSettings.MinimumStep, SceneGizmoSnapSettings.MaximumScale)));
        }

        ImGui.EndPopup();
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

        // Dummy 只占布局、不创建可抢占 ActiveId 的交互项；Scene 相机、画刷与 gizmo
        // 通过同一 canvas rect 做显式仲裁，避免画布先于 2D gizmo 吞掉左键。
        ImGui.Dummy(_canvasSize);
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 canvasMax = _canvasMin + _canvasSize;
        _canvasHovered = ImGui.IsMouseHoveringRect(_canvasMin, canvasMax, clip: true);
        drawList.AddRectFilled(_canvasMin, canvasMax, CanvasColor);
        drawList.PushClipRect(_canvasMin, canvasMax, true);
        try
        {
            DrawWorldPreview(drawList, preview);
            if (ShowGrid)
            {
                DrawGrid(drawList);
            }

            DrawBoundary(drawList, preview.Bounds);
            DrawMarkers(drawList, preview);
            DrawWebCanvasPreview(drawList);
            DrawCanvasLabel(drawList, preview);
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    private void DrawSceneToolOverlay()
    {
        _toolOverlayHovered = false;
        if (_brushPanel is not { Visible: true } brushPanel || _canvasSize.X <= 0f || _canvasSize.Y <= 0f)
        {
            return;
        }

        SceneToolOverlayLayout layout = ResolveToolOverlayLayout(
            _canvasMin,
            _canvasSize,
            ToolOverlayDesiredSize,
            ToolOverlayDock,
            ToolOverlayFloatingOffset,
            ToolOverlayInset);
        Vector2 resumeCursor = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(layout.Position);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.105f, 0.115f, 0.135f, 0.97f));
        bool drawContents = ImGui.BeginChild(
            "##scene-tool-overlay",
            layout.Size,
            ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoSavedSettings);
        _toolOverlayHovered = ImGui.IsMouseHoveringRect(
            layout.Position,
            layout.Position + layout.Size,
            clip: true);
        if (drawContents)
        {
            DrawSceneToolOverlayHeader(layout, brushPanel);
            ImGui.Separator();
            brushPanel.DrawContents();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(resumeCursor);
    }

    private void DrawSceneToolOverlayHeader(SceneToolOverlayLayout layout, MaterialBrushPalettePanel brushPanel)
    {
        float buttonWidth = ImGui.GetFrameHeight();
        float dragWidth = MathF.Max(48f, ImGui.GetContentRegionAvail().X - (buttonWidth * 4f) - 12f);
        Vector2 dragMin = ImGui.GetCursorScreenPos();
        _ = ImGui.InvisibleButton("##scene-tool-overlay-drag", new Vector2(dragWidth, buttonWidth));
        Vector2 dragMax = dragMin + new Vector2(dragWidth, buttonWidth);
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.AddText(dragMin + new Vector2(6f, 3f), ToolbarIconColor, "Brush Tool");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Drag to float inside Scene View; release near an edge to dock");
        }

        if (ImGui.IsItemActivated())
        {
            ToolOverlayDock = SceneToolOverlayDock.Floating;
            ToolOverlayFloatingOffset = layout.Position - _canvasMin;
        }

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            ToolOverlayFloatingOffset += ImGui.GetIO().MouseDelta;
        }

        if (ImGui.IsItemDeactivated())
        {
            SceneToolOverlayLayout floated = ResolveToolOverlayLayout(
                _canvasMin,
                _canvasSize,
                ToolOverlayDesiredSize,
                SceneToolOverlayDock.Floating,
                ToolOverlayFloatingOffset,
                ToolOverlayInset);
            ToolOverlayDock = ResolveToolOverlayDock(
                floated.Position,
                floated.Size,
                _canvasMin,
                _canvasSize,
                snapDistance: 24f,
                ToolOverlayInset);
            ToolOverlayFloatingOffset = floated.Position - _canvasMin;
        }

        ImGui.SameLine(0f, 4f);
        if (ImGui.SmallButton("L##scene-tool-dock-left"))
        {
            ToolOverlayDock = SceneToolOverlayDock.Left;
        }

        DrawOverlayButtonTooltip("Dock left");
        ImGui.SameLine(0f, 2f);
        if (ImGui.SmallButton("F##scene-tool-float"))
        {
            ToolOverlayDock = SceneToolOverlayDock.Floating;
            ToolOverlayFloatingOffset = layout.Position - _canvasMin;
        }

        DrawOverlayButtonTooltip("Float inside Scene View");
        ImGui.SameLine(0f, 2f);
        if (ImGui.SmallButton("R##scene-tool-dock-right"))
        {
            ToolOverlayDock = SceneToolOverlayDock.Right;
        }

        DrawOverlayButtonTooltip("Dock right");
        ImGui.SameLine(0f, 2f);
        if (ImGui.SmallButton("x##scene-tool-close"))
        {
            brushPanel.SetActive(false);
            brushPanel.Visible = false;
        }

        DrawOverlayButtonTooltip("Close Brush Tool");
        if (ToolOverlayDock != SceneToolOverlayDock.Floating)
        {
            uint accent = ToolOverlayDock == SceneToolOverlayDock.Left ? GizmoXAxisColor : GizmoYAxisColor;
            drawList.AddLine(
                new Vector2(dragMin.X, dragMax.Y),
                new Vector2(dragMax.X, dragMax.Y),
                accent,
                2f);
        }
    }

    private static void DrawOverlayButtonTooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

    internal static SceneToolOverlayLayout ResolveToolOverlayLayout(
        Vector2 canvasMin,
        Vector2 canvasSize,
        Vector2 desiredSize,
        SceneToolOverlayDock dock,
        Vector2 floatingOffset,
        float inset = ToolOverlayInset)
    {
        float availableWidth = MathF.Max(1f, canvasSize.X - (inset * 2f));
        float availableHeight = MathF.Max(1f, canvasSize.Y - (inset * 2f));
        Vector2 size = new(
            MathF.Min(MathF.Max(1f, desiredSize.X), availableWidth),
            MathF.Min(MathF.Max(1f, desiredSize.Y), availableHeight));
        Vector2 min = canvasMin + new Vector2(inset, inset);
        Vector2 max = canvasMin + canvasSize - size - new Vector2(inset, inset);
        Vector2 position = dock switch
        {
            SceneToolOverlayDock.Left => min,
            SceneToolOverlayDock.Right => new Vector2(max.X, min.Y),
            SceneToolOverlayDock.Floating => canvasMin + floatingOffset,
            _ => throw new ArgumentOutOfRangeException(nameof(dock), dock, "未知 Scene tool overlay 停靠方式。"),
        };
        position = Vector2.Clamp(position, Vector2.Min(min, max), Vector2.Max(min, max));
        return new SceneToolOverlayLayout(position, size);
    }

    internal static SceneToolOverlayDock ResolveToolOverlayDock(
        Vector2 position,
        Vector2 size,
        Vector2 canvasMin,
        Vector2 canvasSize,
        float snapDistance,
        float inset = ToolOverlayInset)
    {
        float left = canvasMin.X + inset;
        float right = canvasMin.X + canvasSize.X - inset;
        float distance = MathF.Max(0f, snapDistance);
        return MathF.Abs(position.X - left) <= distance
            ? SceneToolOverlayDock.Left
            : MathF.Abs(position.X + size.X - right) <= distance
                ? SceneToolOverlayDock.Right
                : SceneToolOverlayDock.Floating;
    }

    private void DrawWorldPreview(ImDrawListPtr drawList, SceneAuthoringPreview preview)
    {
        Vector2 min = WorldToScreen(new Vector2(preview.Bounds.X, preview.Bounds.Y));
        Vector2 max = WorldToScreen(new Vector2(preview.Bounds.Right, preview.Bounds.Bottom));
        drawList.AddRectFilled(min, max, WorldColor);
        if (!preview.HasAuthoritativeWorld || _worldTexture is null)
        {
            return;
        }

        SceneWorldTextureSnapshot snapshot = _worldTexture.GetTexture(preview.Bounds);
        Vector2 imageMin = WorldToScreen(new Vector2(snapshot.Bounds.X, snapshot.Bounds.Y));
        Vector2 imageMax = WorldToScreen(new Vector2(snapshot.Bounds.Right, snapshot.Bounds.Bottom));
        // SceneWorldTexture 是 CPU buffer 直接上传的原始纹理（无 FBO pass），数据 row 0 = 世界顶部 = 纹理 V=0，
        // 因此直通 UV (0,0)-(1,1) 即为正确朝向。运行时 ViewportPanel 采样的 CurrentViewportTexture 经过 GPU FBO
        // pass 会额外翻转一次 V，才需要 (0,1)-(1,0)；此处若照抄那份翻转会导致 Scene View 上下颠倒。
        drawList.AddImage(
            ViewportPanel.CreateTextureRef(snapshot.Texture.Handle),
            imageMin,
            imageMax,
            new Vector2(0f, 0f),
            new Vector2(1f, 1f),
            0xFFFFFFFF);
    }

    private void DrawWebCanvasPreview(ImDrawListPtr drawList)
    {
        _webCanvasPreviewHovered = false;
        if (_webCanvasPreview is null || _preparedMode != EditorMode.Edit)
        {
            _webCanvasPreview?.Request(null, false, false, 0f, 0f);
            return;
        }

        int? selectedStableId = _preparedSelectedStableId ?? _scene.SelectedStableId;
        if (!selectedStableId.HasValue ||
            !_scene.TryGet(selectedStableId.Value, out EditorGameObject? gameObject) ||
            gameObject.WebCanvas is null)
        {
            _webCanvasPreview.Request(null, false, false, 0f, 0f);
            return;
        }

        SceneWebCanvasPreviewSnapshot snapshot = _webCanvasPreview.Snapshot;
        bool matchingSnapshot = snapshot.Visible && snapshot.StableId == selectedStableId.Value;
        UiCanvasScalerSettings settings = gameObject.CanvasScaler?.Settings ?? UiCanvasScalerSettings.Default;
        (int presentationWidth, int presentationHeight) =
            SceneWebCanvasPreviewDescriptorResolver.ResolvePresentationSize(in settings);
        UiCanvasMetrics metrics = default;
        try
        {
            UiDisplayMetrics display = new(
                presentationWidth,
                presentationHeight,
                1f,
                1f,
                null,
                0,
                0);
            metrics = UiCanvasScaleResolver.Resolve(in settings, in display);
        }
        catch (ArgumentException)
        {
            // Inspector 与 preview runtime 会显示权威校验错误；Scene frame 仍保持可定位，
            // 避免一次非法草稿让整个 Editor Draw 中断。
        }

        if (matchingSnapshot && snapshot.CanvasMetrics.PresentationWidth > 0)
        {
            metrics = snapshot.CanvasMetrics;
            presentationWidth = metrics.PresentationWidth;
            presentationHeight = metrics.PresentationHeight;
        }

        SceneWebCanvasPreviewLayout layout = ResolveWebCanvasPreviewLayout(
            _canvasMin,
            _canvasSize,
            presentationWidth,
            presentationHeight);
        DrawPreviewCheckerboard(drawList, in layout);
        bool ready = matchingSnapshot && snapshot.Ready && snapshot.TextureHandle != 0;
        if (ready)
        {
            // ColorRenderTarget 由 FBO 写入，OpenGL texture V 轴与 ImGui 顶部原点相反。
            drawList.AddImage(
                ViewportPanel.CreateTextureRef(snapshot.TextureHandle),
                layout.Min,
                layout.Max,
                new Vector2(0f, 1f),
                new Vector2(1f, 0f),
                0xFFFFFFFF);
        }
        else
        {
            drawList.AddRectFilled(layout.Min, layout.Max, 0xB8_12_16_1D);
        }

        drawList.AddRect(layout.Min, layout.Max, SelectedColor, 0f, 0, 1.5f);
        Vector2 mouse = ImGui.GetIO().MousePos;
        _webCanvasPreviewHovered = _canvasHovered &&
            ImGui.IsMouseHoveringRect(layout.Min, layout.Max, clip: true);
        float previewX = 0f;
        float previewY = 0f;
        if (_webCanvasPreviewHovered)
        {
            previewX = Math.Clamp(
                (mouse.X - layout.Min.X) / layout.Size.X * presentationWidth,
                0f,
                MathF.Max(0f, presentationWidth - 0.001f));
            previewY = Math.Clamp(
                (mouse.Y - layout.Min.Y) / layout.Size.Y * presentationHeight,
                0f,
                MathF.Max(0f, presentationHeight - 0.001f));
        }

        _webCanvasPreview.Request(
            selectedStableId,
            true,
            _webCanvasPreviewHovered,
            previewX,
            previewY);

        string screen = ready ? snapshot.ScreenId : "loading / unavailable";
        string label = metrics.PresentationWidth > 0
            ? $"Canvas (Web) · {screen} · {presentationWidth}×{presentationHeight} px · layout {metrics.LayoutWidth}×{metrics.LayoutHeight}"
            : $"Canvas (Web) · {screen} · {presentationWidth}×{presentationHeight} px";
        Vector2 labelSize = ImGui.CalcTextSize(label);
        Vector2 labelMin = layout.Min + new Vector2(7f, 6f);
        drawList.AddRectFilled(
            layout.Min,
            new Vector2(layout.Max.X, layout.Min.Y + labelSize.Y + 12f),
            0xD8_18_1B_22);
        drawList.AddText(labelMin, SelectedColor, label);

        string diagnostic = matchingSnapshot ? snapshot.Diagnostic : string.Empty;
        if (!ready && !string.IsNullOrWhiteSpace(diagnostic))
        {
            string summary = diagnostic.Length <= 120 ? diagnostic : diagnostic[..117] + "...";
            Vector2 textSize = ImGui.CalcTextSize(summary);
            Vector2 textPosition = new(
                layout.Min.X + 10f,
                MathF.Max(layout.Min.Y + labelSize.Y + 18f, layout.Max.Y - textSize.Y - 10f));
            drawList.AddText(textPosition, 0xFF_5D_B5_F2, summary);
            if (_webCanvasPreviewHovered)
            {
                ImGui.SetTooltip(diagnostic);
            }
        }
    }

    internal static SceneWebCanvasPreviewLayout ResolveWebCanvasPreviewLayout(
        Vector2 canvasMin,
        Vector2 canvasSize,
        int presentationWidth,
        int presentationHeight,
        float inset = 24f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationHeight);
        float safeInset = MathF.Max(0f, float.IsFinite(inset) ? inset : 0f);
        Vector2 available = new(
            MathF.Max(1f, canvasSize.X - (safeInset * 2f)),
            MathF.Max(1f, canvasSize.Y - (safeInset * 2f)));
        float scale = MathF.Min(
            available.X / presentationWidth,
            available.Y / presentationHeight);
        Vector2 size = new(
            MathF.Max(1f, presentationWidth * scale),
            MathF.Max(1f, presentationHeight * scale));
        Vector2 min = canvasMin + ((canvasSize - size) * 0.5f);
        return new SceneWebCanvasPreviewLayout(min, size);
    }

    private static void DrawPreviewCheckerboard(
        ImDrawListPtr drawList,
        in SceneWebCanvasPreviewLayout layout)
    {
        const float TileSize = 24f;
        int columns = Math.Max(1, (int)MathF.Ceiling(layout.Size.X / TileSize));
        int rows = Math.Max(1, (int)MathF.Ceiling(layout.Size.Y / TileSize));
        for (int y = 0; y < rows; y++)
        {
            float top = layout.Min.Y + (y * TileSize);
            float bottom = MathF.Min(layout.Max.Y, top + TileSize);
            for (int x = 0; x < columns; x++)
            {
                float left = layout.Min.X + (x * TileSize);
                float right = MathF.Min(layout.Max.X, left + TileSize);
                uint color = ((x + y) & 1) == 0 ? 0xFF_2A_2E_36 : 0xFF_20_23_2A;
                drawList.AddRectFilled(new Vector2(left, top), new Vector2(right, bottom), color);
            }
        }
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
                SceneAnchorMarkerGeometry geometry = BuildAnchorMarkerGeometry(marker, markerRadius);
                drawList.AddTriangleFilled(
                    screen - geometry.AxisY,
                    screen + geometry.AxisX + geometry.AxisY,
                    screen - geometry.AxisX + geometry.AxisY,
                    color);
            }
            else if (marker.Kind == SceneAuthoringMarkerKind.Goal)
            {
                SceneAnchorMarkerGeometry geometry = BuildAnchorMarkerGeometry(marker, markerRadius);
                Vector2 topLeft = screen - geometry.AxisX - geometry.AxisY;
                Vector2 topRight = screen + geometry.AxisX - geometry.AxisY;
                Vector2 bottomRight = screen + geometry.AxisX + geometry.AxisY;
                Vector2 bottomLeft = screen - geometry.AxisX + geometry.AxisY;
                drawList.AddTriangleFilled(topLeft, topRight, bottomRight, color);
                drawList.AddTriangleFilled(topLeft, bottomRight, bottomLeft, color);
            }
            else
            {
                DrawGameObjectMarker(drawList, marker, screen, color, selected);
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
        string worldKind = preview.HasAuthoritativeWorld
            ? "authoritative cell world"
            : preview.IsExplicitEmptyScene ? "explicit empty scene" : "object bounds";
        drawList.AddText(
            _canvasMin + new Vector2(10f, 10f),
            color,
            $"{preview.StatusLabel} · {worldKind} · {preview.Bounds.Width:0}×{preview.Bounds.Height:0} cells");
    }

    private static void DrawGameObjectMarker(
        ImDrawListPtr drawList,
        SceneAuthoringMarker marker,
        Vector2 center,
        uint color,
        bool selected)
    {
        SceneGameObjectMarkerGeometry geometry = BuildGameObjectMarkerGeometry(marker);
        float thickness = selected ? 2.5f : 1.5f;
        drawList.AddLine(center - geometry.AxisX, center + geometry.AxisX, color, thickness);
        drawList.AddLine(center - geometry.AxisY, center + geometry.AxisY, color, thickness);
        drawList.AddCircleFilled(center, selected ? 4.5f : 3.5f, color);
        drawList.AddCircle(center, geometry.Radius, color, 24, selected ? 1.5f : 1f);
    }

    internal static SceneGameObjectMarkerGeometry BuildGameObjectMarkerGeometry(SceneAuthoringMarker marker)
    {
        float rotation = float.IsFinite(marker.RotationRadians) ? marker.RotationRadians : 0f;
        float scaleX = Math.Clamp(MathF.Abs(marker.ScaleX), 0.25f, 4f) * 7f;
        float scaleY = Math.Clamp(MathF.Abs(marker.ScaleY), 0.25f, 4f) * 7f;
        float cosine = MathF.Cos(rotation);
        float sine = MathF.Sin(rotation);
        Vector2 axisX = new(cosine * scaleX, sine * scaleX);
        Vector2 axisY = new(-sine * scaleY, cosine * scaleY);
        return new SceneGameObjectMarkerGeometry(axisX, axisY, MathF.Max(scaleX, scaleY));
    }

    internal static SceneAnchorMarkerGeometry BuildAnchorMarkerGeometry(
        SceneAuthoringMarker marker,
        float baseRadius = 10f)
    {
        float rotation = float.IsFinite(marker.RotationRadians) ? marker.RotationRadians : 0f;
        float scaleX = ResolveMarkerScale(marker.ScaleX) * MathF.Max(1f, baseRadius);
        float scaleY = ResolveMarkerScale(marker.ScaleY) * MathF.Max(1f, baseRadius);
        float cosine = MathF.Cos(rotation);
        float sine = MathF.Sin(rotation);
        return new SceneAnchorMarkerGeometry(
            new Vector2(cosine * scaleX, sine * scaleX),
            new Vector2(-sine * scaleY, cosine * scaleY));
    }

    private static float ResolveMarkerScale(float scale)
    {
        if (!float.IsFinite(scale))
        {
            return 1f;
        }

        float sign = scale < 0f ? -1f : 1f;
        return sign * Math.Clamp(MathF.Abs(scale), 0.25f, 4f);
    }

    private void HandleCameraInput()
    {
        if (!_canvasHovered || _toolOverlayHovered)
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (io.MouseWheel != 0f)
        {
            _camera.ZoomAt(io.MousePos - _canvasMin, io.MouseWheel);
            _cameraAutoFit = false;
        }

        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right))
        {
            _camera.PanPixels(io.MouseDelta);
            _cameraAutoFit = false;
        }
    }

    private void HandleSceneMouse(EditorSelection selection)
    {
        bool hasSelection = selection.GameObjectStableId.HasValue || _scene.SelectedStableId.HasValue;
        if (!_canvasHovered ||
            _toolOverlayHovered ||
            _webCanvasPreviewHovered ||
            (!MaterialBrushActive && hasSelection && IsGizmoCapturingMouse()))
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
            _ = ApplyMaterialBrushAt(
                (int)MathF.Round(world.X),
                (int)MathF.Round(world.Y));

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

    private bool IsGizmoCapturingMouse()
    {
        return _activeGizmoHandle != SceneGizmoHandle.None ||
            _hoveredGizmoHandle != SceneGizmoHandle.None;
    }

    private void DrawGizmo(EditorSelection selection)
    {
        int? stableId = selection.GameObjectStableId ?? _scene.SelectedStableId;
        PrepareFrame(stableId, _preparedMode);
        if ((_toolOverlayHovered && _activeGizmoHandle == SceneGizmoHandle.None) ||
            MaterialBrushActive ||
            !stableId.HasValue ||
            !_scene.TryGet(stableId.Value, out EditorGameObject? gameObject) ||
            !_scene.IsSceneVisible(stableId.Value) ||
            !_scene.IsScenePickable(stableId.Value))
        {
            if (_gizmoTransactionStableId.HasValue)
            {
                _ = CommitGizmoTransform();
            }

            ClearGizmoInteraction();
            return;
        }

        EditorSceneTransform world = _scene.ComputeWorldTransform(gameObject.StableId);
        SceneGizmoGeometry geometry = BuildGizmoGeometry(
            world,
            WorldToScreen(new Vector2(world.X, world.Y)),
            GizmoMode);
        ImGuiIOPtr io = ImGui.GetIO();
        _hoveredGizmoHandle = _canvasHovered
            ? ResolveGizmoHandle(in geometry, Operation, io.MousePos)
            : SceneGizmoHandle.None;

        if (_activeGizmoHandle == SceneGizmoHandle.None &&
            _hoveredGizmoHandle != SceneGizmoHandle.None &&
            ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
            BeginGizmoTransform(gameObject.StableId))
        {
            _activeGizmoHandle = _hoveredGizmoHandle;
            _gizmoDragStartScreen = io.MousePos;
            _gizmoDragStartWorldPoint = _camera.CanvasToWorld(io.MousePos - _canvasMin);
            _gizmoDragCenterScreen = geometry.Center;
            _gizmoDragStartWorldTransform = world.Clone();
        }

        if (_activeGizmoHandle != SceneGizmoHandle.None && _gizmoDragStartWorldTransform is not null)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _ = CancelGizmoTransform();
                ClearGizmoInteraction();
            }
            else if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                Vector2 currentWorldPoint = _camera.CanvasToWorld(io.MousePos - _canvasMin);
                EditorSceneTransform nextWorld = ApplyGizmoDrag(
                    _gizmoDragStartWorldTransform,
                    _activeGizmoHandle,
                    Operation,
                    GizmoMode,
                    _gizmoDragStartWorldPoint,
                    currentWorldPoint,
                    _gizmoDragStartScreen,
                    io.MousePos,
                    _gizmoDragCenterScreen);
                nextWorld = ApplyGizmoSnap(
                    _gizmoDragStartWorldTransform,
                    nextWorld,
                    _activeGizmoHandle,
                    Operation,
                    GizmoMode,
                    SnapSettings);
                _ = ApplyGizmoWorldTransform(gameObject.StableId, nextWorld);
                world = _scene.ComputeWorldTransform(gameObject.StableId);
                geometry = BuildGizmoGeometry(
                    world,
                    WorldToScreen(new Vector2(world.X, world.Y)),
                    GizmoMode);
            }
            else
            {
                _ = CommitGizmoTransform();
                ClearGizmoInteraction();
            }
        }

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(_canvasMin, _canvasMin + _canvasSize, true);
        try
        {
            DrawGizmoGeometry(drawList, in geometry, world.RotationRadians, Operation);
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    internal static SceneGizmoGeometry BuildGizmoGeometry(
        EditorSceneTransform worldTransform,
        Vector2 center,
        ImGuizmoMode mode)
    {
        ArgumentNullException.ThrowIfNull(worldTransform);
        ResolveGizmoAxes(worldTransform, mode, out Vector2 axisX, out Vector2 axisY);
        Vector2 diagonal = Vector2.Normalize(axisX + axisY);
        return new SceneGizmoGeometry(
            center,
            center + (axisX * GizmoAxisLength),
            center + (axisY * GizmoAxisLength),
            center + (diagonal * GizmoAxisLength),
            axisX,
            axisY,
            GizmoRotationRadius);
    }

    internal static SceneGizmoHandle ResolveGizmoHandle(
        in SceneGizmoGeometry geometry,
        ImGuizmoOperation operation,
        Vector2 pointer)
    {
        const float CenterHitRadius = 9f;
        const float AxisHitRadius = 7f;
        const float EndpointHitRadius = 10f;
        if (operation == ImGuizmoOperation.RotateZ)
        {
            float distance = Vector2.Distance(pointer, geometry.Center);
            return MathF.Abs(distance - geometry.RotationRadius) <= AxisHitRadius
                ? SceneGizmoHandle.Rotate
                : SceneGizmoHandle.None;
        }

        if (operation == ImGuizmoOperation.Scale &&
            Vector2.DistanceSquared(pointer, geometry.UniformEnd) <= EndpointHitRadius * EndpointHitRadius)
        {
            return SceneGizmoHandle.Uniform;
        }

        if (operation == ImGuizmoOperation.Translate &&
            Vector2.DistanceSquared(pointer, geometry.Center) <= CenterHitRadius * CenterHitRadius)
        {
            return SceneGizmoHandle.Both;
        }

        bool hitsAxisX = Vector2.DistanceSquared(pointer, geometry.AxisXEnd) <= EndpointHitRadius * EndpointHitRadius ||
            DistanceToSegment(pointer, geometry.Center, geometry.AxisXEnd) <= AxisHitRadius;
        bool hitsAxisY = Vector2.DistanceSquared(pointer, geometry.AxisYEnd) <= EndpointHitRadius * EndpointHitRadius ||
            DistanceToSegment(pointer, geometry.Center, geometry.AxisYEnd) <= AxisHitRadius;
        return hitsAxisX ? SceneGizmoHandle.AxisX : hitsAxisY ? SceneGizmoHandle.AxisY : SceneGizmoHandle.None;
    }

    internal static EditorSceneTransform ApplyGizmoDrag(
        EditorSceneTransform start,
        SceneGizmoHandle handle,
        ImGuizmoOperation operation,
        ImGuizmoMode mode,
        Vector2 startPointerWorld,
        Vector2 currentPointerWorld,
        Vector2 startPointerScreen,
        Vector2 currentPointerScreen,
        Vector2 centerScreen)
    {
        ArgumentNullException.ThrowIfNull(start);
        EditorSceneTransform next = start.Clone();
        ResolveGizmoAxes(start, mode, out Vector2 axisX, out Vector2 axisY);
        if (operation == ImGuizmoOperation.Translate)
        {
            Vector2 delta = currentPointerWorld - startPointerWorld;
            Vector2 applied = handle switch
            {
                SceneGizmoHandle.AxisX => axisX * Vector2.Dot(delta, axisX),
                SceneGizmoHandle.AxisY => axisY * Vector2.Dot(delta, axisY),
                SceneGizmoHandle.Both => delta,
                SceneGizmoHandle.None or SceneGizmoHandle.Rotate or SceneGizmoHandle.Uniform => Vector2.Zero,
                _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, "未知 Scene gizmo handle。"),
            };
            next.X += applied.X;
            next.Y += applied.Y;
            return next;
        }

        if (operation == ImGuizmoOperation.RotateZ && handle == SceneGizmoHandle.Rotate)
        {
            float startAngle = MathF.Atan2(startPointerScreen.Y - centerScreen.Y, startPointerScreen.X - centerScreen.X);
            float currentAngle = MathF.Atan2(currentPointerScreen.Y - centerScreen.Y, currentPointerScreen.X - centerScreen.X);
            next.RotationRadians += NormalizeAngleDelta(currentAngle - startAngle);
            return next;
        }

        if (operation != ImGuizmoOperation.Scale)
        {
            return next;
        }

        Vector2 startDelta = startPointerScreen - centerScreen;
        Vector2 currentDelta = currentPointerScreen - centerScreen;
        if (handle == SceneGizmoHandle.AxisX)
        {
            next.ScaleX *= ResolveScaleFactor(Vector2.Dot(startDelta, axisX), Vector2.Dot(currentDelta, axisX));
        }
        else if (handle == SceneGizmoHandle.AxisY)
        {
            next.ScaleY *= ResolveScaleFactor(Vector2.Dot(startDelta, axisY), Vector2.Dot(currentDelta, axisY));
        }
        else if (handle == SceneGizmoHandle.Uniform)
        {
            float factor = ResolveScaleFactor(startDelta.Length(), currentDelta.Length());
            next.ScaleX *= factor;
            next.ScaleY *= factor;
        }

        return next;
    }

    internal static EditorSceneTransform ApplyGizmoSnap(
        EditorSceneTransform start,
        EditorSceneTransform value,
        SceneGizmoHandle handle,
        ImGuizmoOperation operation,
        ImGuizmoMode mode,
        in SceneGizmoSnapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(value);
        if (!settings.Enabled)
        {
            return value;
        }

        if (!settings.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings, "Scene gizmo snap settings 无效。");
        }

        EditorSceneTransform snapped = value.Clone();
        if (operation == ImGuizmoOperation.Translate)
        {
            ResolveGizmoAxes(start, mode, out Vector2 axisX, out Vector2 axisY);
            Vector2 delta = new(value.X - start.X, value.Y - start.Y);
            float amountX = Vector2.Dot(delta, axisX);
            float amountY = Vector2.Dot(delta, axisY);
            Vector2 snappedDelta = handle switch
            {
                SceneGizmoHandle.AxisX => axisX * SnapDelta(amountX, settings.Move),
                SceneGizmoHandle.AxisY => axisY * SnapDelta(amountY, settings.Move),
                SceneGizmoHandle.Both =>
                    (axisX * SnapDelta(amountX, settings.Move)) +
                    (axisY * SnapDelta(amountY, settings.Move)),
                SceneGizmoHandle.None or SceneGizmoHandle.Rotate or SceneGizmoHandle.Uniform => delta,
                _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, "未知 Scene gizmo handle。"),
            };
            snapped.X = start.X + snappedDelta.X;
            snapped.Y = start.Y + snappedDelta.Y;
            return snapped;
        }

        if (operation == ImGuizmoOperation.RotateZ && handle == SceneGizmoHandle.Rotate)
        {
            float stepRadians = settings.RotationDegrees * (MathF.PI / 180f);
            snapped.RotationRadians = start.RotationRadians +
                SnapDelta(value.RotationRadians - start.RotationRadians, stepRadians);
            return snapped;
        }

        if (operation != ImGuizmoOperation.Scale)
        {
            return snapped;
        }

        if (handle is SceneGizmoHandle.AxisX or SceneGizmoHandle.Uniform)
        {
            snapped.ScaleX = start.ScaleX + SnapDelta(value.ScaleX - start.ScaleX, settings.Scale);
        }

        if (handle is SceneGizmoHandle.AxisY or SceneGizmoHandle.Uniform)
        {
            snapped.ScaleY = start.ScaleY + SnapDelta(value.ScaleY - start.ScaleY, settings.Scale);
        }

        return snapped;
    }

    private static float SnapDelta(float value, float step)
    {
        return MathF.Round(value / step, MidpointRounding.AwayFromZero) * step;
    }

    private void DrawGizmoGeometry(
        ImDrawListPtr drawList,
        in SceneGizmoGeometry geometry,
        float rotationRadians,
        ImGuizmoOperation operation)
    {
        uint xColor = ResolveGizmoColor(SceneGizmoHandle.AxisX, GizmoXAxisColor);
        uint yColor = ResolveGizmoColor(SceneGizmoHandle.AxisY, GizmoYAxisColor);
        if (operation == ImGuizmoOperation.RotateZ)
        {
            uint color = ResolveGizmoColor(SceneGizmoHandle.Rotate, GizmoUniformColor);
            drawList.AddCircle(geometry.Center, geometry.RotationRadius, color, 48, 2.5f);
            Vector2 direction = new(MathF.Cos(rotationRadians), MathF.Sin(rotationRadians));
            drawList.AddLine(geometry.Center, geometry.Center + (direction * geometry.RotationRadius), color, 2f);
            drawList.AddCircleFilled(geometry.Center + (direction * geometry.RotationRadius), 4.5f, color);
            return;
        }

        drawList.AddLine(geometry.Center, geometry.AxisXEnd, xColor, 3f);
        drawList.AddLine(geometry.Center, geometry.AxisYEnd, yColor, 3f);
        if (operation == ImGuizmoOperation.Translate)
        {
            DrawArrowHead(drawList, geometry.AxisXEnd, geometry.AxisX, xColor);
            DrawArrowHead(drawList, geometry.AxisYEnd, geometry.AxisY, yColor);
            uint centerColor = ResolveGizmoColor(SceneGizmoHandle.Both, GizmoUniformColor);
            drawList.AddRectFilled(geometry.Center - new Vector2(5f), geometry.Center + new Vector2(5f), centerColor, 1f);
            return;
        }

        DrawSquareHandle(drawList, geometry.AxisXEnd, xColor);
        DrawSquareHandle(drawList, geometry.AxisYEnd, yColor);
        uint uniformColor = ResolveGizmoColor(SceneGizmoHandle.Uniform, GizmoUniformColor);
        drawList.AddLine(geometry.Center, geometry.UniformEnd, uniformColor, 1.5f);
        DrawSquareHandle(drawList, geometry.UniformEnd, uniformColor);
    }

    private uint ResolveGizmoColor(SceneGizmoHandle handle, uint normalColor)
    {
        return _activeGizmoHandle == handle ||
            (_activeGizmoHandle == SceneGizmoHandle.None && _hoveredGizmoHandle == handle)
                ? GizmoHighlightColor
                : normalColor;
    }

    private static void DrawArrowHead(ImDrawListPtr drawList, Vector2 end, Vector2 axis, uint color)
    {
        Vector2 perpendicular = new(-axis.Y, axis.X);
        drawList.AddTriangleFilled(
            end,
            end - (axis * 10f) + (perpendicular * 5f),
            end - (axis * 10f) - (perpendicular * 5f),
            color);
    }

    private static void DrawSquareHandle(ImDrawListPtr drawList, Vector2 center, uint color)
    {
        drawList.AddRectFilled(center - new Vector2(5f), center + new Vector2(5f), color, 1f);
    }

    private static void ResolveGizmoAxes(
        EditorSceneTransform transform,
        ImGuizmoMode mode,
        out Vector2 axisX,
        out Vector2 axisY)
    {
        float rotation = mode == ImGuizmoMode.Local && float.IsFinite(transform.RotationRadians)
            ? transform.RotationRadians
            : 0f;
        float cosine = MathF.Cos(rotation);
        float sine = MathF.Sin(rotation);
        axisX = new Vector2(cosine, sine);
        axisY = new Vector2(-sine, cosine);
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared <= float.Epsilon)
        {
            return Vector2.Distance(point, start);
        }

        float t = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, start + (segment * t));
    }

    private static float ResolveScaleFactor(float start, float current)
    {
        if (!float.IsFinite(start) || !float.IsFinite(current) || MathF.Abs(start) <= 0.0001f)
        {
            return 1f;
        }

        float factor = current / start;
        return !float.IsFinite(factor)
            ? 1f
            : MathF.Abs(factor) < 0.01f ? MathF.CopySign(0.01f, factor == 0f ? 1f : factor) : factor;
    }

    private static float NormalizeAngleDelta(float radians)
    {
        while (radians > MathF.PI)
        {
            radians -= MathF.Tau;
        }

        while (radians < -MathF.PI)
        {
            radians += MathF.Tau;
        }

        return radians;
    }

    private SceneAuthoringPreview EnsurePreview()
    {
        AuthoringWorldPreviewSnapshot authoringWorld = _authoringWorldSnapshot?.Invoke() ?? default;
        if (_previewVersion == _scene.Version &&
            _previewSceneViewVersion == _scene.SceneViewVersion &&
            _previewAuthoringWorldVersion == authoringWorld.Version &&
            string.Equals(_previewSceneName, _scene.Name, StringComparison.Ordinal))
        {
            return _preview;
        }

        _preview = SceneAuthoringPreviewBuilder.Build(_scene, authoringWorld);
        _previewVersion = _scene.Version;
        _previewSceneViewVersion = _scene.SceneViewVersion;
        _previewAuthoringWorldVersion = authoringWorld.Version;
        _previewSceneName = _scene.Name;
        return _preview;
    }

    private Vector2 WorldToScreen(Vector2 world)
    {
        return _canvasMin + _camera.WorldToCanvas(world);
    }

    private void ClearGizmoTransaction()
    {
        _gizmoTransactionStableId = null;
        _gizmoTransactionSceneGeneration = -1;
        _gizmoBefore = null;
        _gizmoBeforePrefabLink = null;
        _gizmoChanged = false;
        _gizmoBeforeDirty = false;
        ClearGizmoInteraction();
    }

    private void ClearGizmoInteraction()
    {
        _hoveredGizmoHandle = SceneGizmoHandle.None;
        _activeGizmoHandle = SceneGizmoHandle.None;
        _gizmoDragStartScreen = default;
        _gizmoDragStartWorldPoint = default;
        _gizmoDragCenterScreen = default;
        _gizmoDragStartWorldTransform = null;
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

    private static bool ApproximatelyEqual(Vector2 left, Vector2 right)
    {
        const float Epsilon = 0.5f;
        return MathF.Abs(left.X - right.X) <= Epsilon &&
            MathF.Abs(left.Y - right.Y) <= Epsilon;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _webCanvasPreview?.Request(null, false, false, 0f, 0f);
        _worldTexture?.Dispose();
        _disposed = true;
    }
}

internal enum SceneGizmoHandle
{
    None,
    AxisX,
    AxisY,
    Both,
    Rotate,
    Uniform,
}

internal readonly record struct SceneWebCanvasPreviewLayout(
    Vector2 Min,
    Vector2 Size)
{
    public Vector2 Max => Min + Size;
}

internal readonly record struct SceneGizmoGeometry(
    Vector2 Center,
    Vector2 AxisXEnd,
    Vector2 AxisYEnd,
    Vector2 UniformEnd,
    Vector2 AxisX,
    Vector2 AxisY,
    float RotationRadius);

internal readonly record struct SceneGameObjectMarkerGeometry(
    Vector2 AxisX,
    Vector2 AxisY,
    float Radius);

internal readonly record struct SceneAnchorMarkerGeometry(
    Vector2 AxisX,
    Vector2 AxisY);

internal enum SceneToolOverlayDock
{
    Floating,
    Left,
    Right,
}

internal readonly record struct SceneToolOverlayLayout(
    Vector2 Position,
    Vector2 Size);

internal readonly record struct SceneGizmoSnapSettings(
    bool Enabled,
    float Move,
    float RotationDegrees,
    float Scale)
{
    public const float MinimumStep = 0.0001f;
    public const float MaximumMove = 1_000_000f;
    public const float MaximumRotationDegrees = 360f;
    public const float MaximumScale = 100_000f;

    public static SceneGizmoSnapSettings Default { get; } = new(
        Enabled: false,
        Move: 1f,
        RotationDegrees: 15f,
        Scale: 0.1f);

    public bool IsValid =>
        float.IsFinite(Move) && Move is >= MinimumStep and <= MaximumMove &&
        float.IsFinite(RotationDegrees) &&
        RotationDegrees is >= MinimumStep and <= MaximumRotationDegrees &&
        float.IsFinite(Scale) && Scale is >= MinimumStep and <= MaximumScale;
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
