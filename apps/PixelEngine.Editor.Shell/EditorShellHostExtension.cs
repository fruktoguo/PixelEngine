using PixelEngine.Audio;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Gui;
using PixelEngine.Hosting;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.UI;
using Silk.NET.OpenGL;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Hosting 扩展：将 Editor Shell 接入 Engine 的输入、UI present 与 Game View 契约。
/// </summary>
internal sealed class EditorShellHostExtension :
    IEditorHostExtension,
    IEditorInputCaptureSource,
    IGameUiInputSourceFactory,
    IGameplayViewportInputMapper,
    IUiPresentTargetProvider,
    IGamePresentationOverride,
    IGameUiCompositionPolicy
{
    private const int MaximumAutomationCaptureBytes = 64 * 1024 * 1024;
    private readonly EditorProject _project;
    private readonly EditorShellApp _app;
    private readonly RenderWindow _window;
    private readonly EditorApp _editor;
    private readonly GameViewUiPresentTargetProvider _gameUiPresentTargetProvider;
    private readonly bool _focusInspectorOnInitialLayout;
    private EditorSceneModel? _sceneModel;
    private EditorUndoStack? _undoStack;
    private EditorPrefabAssetStore? _prefabs;
    private AuthoringWorldPreviewRuntime? _authoringWorld;
    private SceneWebCanvasAuthoringPreview? _sceneWebCanvasPreview;
    private ProjectSettingsPanel? _projectSettingsPanel;
    private PlayerSettingsPanel? _playerSettingsPanel;
    private BuildSettingsPanel? _buildSettingsPanel;
    private EditorPlayerProcessManager? _playerProcessManager;
    private SceneViewPanel? _sceneViewPanel;
    private GameViewPanel? _gameViewPanel;
    private GameObjectInspectorPanel? _gameObjectInspectorPanel;
    private EditorConsolePanel? _consolePanel;
    private AssetBrowserPanel? _assetBrowserPanel;
    private MaterialBrushPalettePanel? _brushPanel;
    private MaterialReactionEditorPanel? _materialReactionPanel;
    private FileMaterialReactionContentService? _materialReactionContentService;
    private WorldInspectorPanel? _worldInspectorPanel;
    private PerformanceHudPanel? _performanceHudPanel;
    private EditorWorldSaveLoadService? _saveLoadService;
    private RuntimeSceneHierarchyDataSource? _runtimeHierarchy;
    private ISimulationInspectApi? _simulationInspectApi;
    private IPhysicsTuningService? _physicsTuningService;
    private IParticleTuningService? _particleTuningService;
    private ILightingTuningService? _lightingTuningService;
    private EditorAssetBrowserDataSource? _assetBrowserDataSource;
    private EditorTextureThumbnailProvider? _textureThumbnailProvider;
    private Engine? _engine;
    private RenderPipeline? _pipeline;
    private EditorMode _lastPreparedMode = EditorMode.Edit;
    private bool _panelsRegistered;

    public EditorShellHostExtension(EditorProject project, EditorShellApp app, RenderWindow window)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _app = app ?? throw new ArgumentNullException(nameof(app));
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        EditorFontStackPaths fonts = EditorFontAssets.ResolveRuntime();
        _focusInspectorOnInitialLayout = !File.Exists(app.LayoutPath);
        _editor = new EditorApp(
            new HexaImGuiBackend(window),
            new EditorAppOptions
            {
                LayoutPath = app.LayoutPath,
                EnableMultiViewport = false,
                DpiScale = app.UiScale,
                PrimaryFontPath = fonts.PrimaryFontPath,
                CjkFallbackFontPath = fonts.CjkFallbackFontPath,
                FontSizePixels = EditorFontAssets.BaseFontSizePixels,
            });
        _gameUiPresentTargetProvider = new GameViewUiPresentTargetProvider(
            CapturePlayMode,
            () => _gameViewPanel?.LastViewportSnapshot ?? GameViewViewportSnapshot.Empty,
            () => _gameViewPanel is { Visible: true });
    }

    public int PanelCount => _editor.PanelCount;

    public long BridgeFrameCount => Bridge?.FrameIndex ?? 0;

    /// <summary>捕获 Game View toolbar、Hosting presentation 与 viewport 的同帧脚本化验收快照。</summary>
    public ScriptedGameViewPresentationSnapshot CaptureScriptedGameViewPresentation()
    {
        return _gameViewPanel?.CaptureScriptedPresentationSnapshot() ??
            ScriptedGameViewPresentationSnapshot.Missing;
    }

    internal EditorGameViewAutomationState CaptureAutomationGameViewState()
    {
        return (_gameViewPanel ?? throw new InvalidOperationException("Game View panel 尚未初始化。"))
            .CaptureAutomationState();
    }

    internal bool TryApplyAutomationGameViewState(
        EditorGameViewAutomationState state,
        out string diagnostic)
    {
        if (_gameViewPanel is null)
        {
            diagnostic = "Game View panel 尚未初始化。";
            return false;
        }

        return _gameViewPanel.TryApplyAutomationState(state, out diagnostic);
    }

    internal bool TryApplyAutomationRuntimeTransform(
        string handle,
        float x,
        float y,
        float rotationRadians,
        float scaleX,
        float scaleY,
        out string diagnostic)
    {
        if (CapturePlayMode() == EditorMode.Edit || _runtimeHierarchy is null)
        {
            diagnostic = "Runtime Transform 只在已初始化的 Play/Paused session 中可用。";
            return false;
        }

        bool applied = _runtimeHierarchy.TrySetEntityTransform(
            handle,
            x,
            y,
            rotationRadians,
            scaleX,
            scaleY);
        diagnostic = applied ? string.Empty : "Runtime entity Transform 已失效或包含非有限值。";
        return applied;
    }

    internal bool TryApplyAutomationRuntimeField(
        string handle,
        int componentIndex,
        string fieldName,
        object? value,
        out string diagnostic)
    {
        if (CapturePlayMode() == EditorMode.Edit || _runtimeHierarchy is null)
        {
            diagnostic = "Runtime Behaviour field 只在已初始化的 Play/Paused session 中可用。";
            return false;
        }

        bool applied = _runtimeHierarchy.TrySetBehaviourField(
            handle,
            componentIndex,
            fieldName,
            value);
        diagnostic = applied ? string.Empty : "Runtime Behaviour field 已失效、不可写或值类型不兼容。";
        return applied;
    }

    internal bool TryBeginAutomationSceneCapture(
        out EditorAutomationFrameCapture capture,
        out string diagnostic)
    {
        capture = null!;
        if (_engine is null || _sceneViewPanel is null)
        {
            diagnostic = "Scene View capture runtime 尚未初始化。";
            return false;
        }

        if (!_sceneViewPanel.TryGetAutomationCaptureRect(_window, out _, out diagnostic))
        {
            return false;
        }

        capture = new EditorAutomationFrameCapture();
        EditorAutomationFrameCapture pending = capture;
        IDisposable registration = _engine.Probe.RegisterBeforeSwapBuffers(
            () => pending.Complete(CaptureSceneFrame));
        capture.Attach(registration);
        diagnostic = string.Empty;
        return true;
    }

    internal bool TryGetAutomationMaterialEditor(
        out MaterialReactionEditorPanel panel,
        out FileMaterialReactionContentService contentService)
    {
        panel = _materialReactionPanel!;
        contentService = _materialReactionContentService!;
        return panel is not null && contentService is not null;
    }

    internal bool TryGetAutomationWorldInspector(out WorldInspectorPanel panel)
    {
        panel = _worldInspectorPanel!;
        return panel is not null;
    }

    internal void ApplyAutomationWorldInspectorState(
        bool followSelection,
        int worldX,
        int worldY)
    {
        (_worldInspectorPanel ?? throw new InvalidOperationException("World Inspector 尚未初始化。"))
            .ApplyState(followSelection, worldX, worldY, _editor.Selection);
    }

    internal PerformanceHudHistorySnapshot CaptureAutomationProfilerHistory()
    {
        return (_performanceHudPanel ??
            throw new InvalidOperationException("Profiler 面板尚未初始化。"))
            .CaptureHistory();
    }

    internal bool TryBeginAutomationGameCapture(
        out EditorAutomationFrameCapture capture,
        out string diagnostic)
    {
        capture = null!;
        if (_engine is null || _pipeline is null || !_pipeline.CurrentViewportTexture.IsValid)
        {
            diagnostic = "Game presentation 尚未产生可捕获的完整纹理。";
            return false;
        }

        capture = new EditorAutomationFrameCapture();
        EditorAutomationFrameCapture pending = capture;
        IDisposable registration = _engine.Probe.RegisterBeforeSwapBuffers(
            () => pending.Complete(CaptureGameFrame));
        capture.Attach(registration);
        diagnostic = string.Empty;
        return true;
    }

    private EditorAutomationRawCapture CaptureSceneFrame()
    {
        SceneViewPanel panel = _sceneViewPanel ??
            throw new InvalidOperationException("Scene View panel 已不可用。");
        if (!panel.TryGetAutomationCaptureRect(_window, out EditorFramebufferCaptureRect rect, out string diagnostic))
        {
            throw new InvalidOperationException(diagnostic);
        }

        byte[] rgba = AllocateCaptureBuffer(rect.Width, rect.Height);
        GL gl = _window.Gl;
        gl.GetInteger(GLEnum.ReadFramebufferBinding, out int previousFramebuffer);
        gl.GetInteger(GLEnum.ReadBuffer, out int previousReadBuffer);
        try
        {
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _window.PresentationFramebuffer);
            gl.ReadBuffer(_window.PresentationFramebuffer == 0
                ? ReadBufferMode.Back
                : ReadBufferMode.ColorAttachment0);
            gl.ReadPixels(
                rect.X,
                rect.Y,
                (uint)rect.Width,
                (uint)rect.Height,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                rgba);
            ThrowIfCaptureGlError(gl, "Scene View ReadPixels");
        }
        finally
        {
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)previousFramebuffer);
            gl.ReadBuffer((ReadBufferMode)previousReadBuffer);
        }

        return new EditorAutomationRawCapture
        {
            Kind = "scene-view",
            Width = rect.Width,
            Height = rect.Height,
            ContentRevision = _sceneModel?.Version ?? 0,
            RgbaBottomUp = rgba,
        };
    }

    private EditorAutomationRawCapture CaptureGameFrame()
    {
        RenderPipeline pipeline = _pipeline ??
            throw new InvalidOperationException("Game render pipeline 已不可用。");
        RenderViewportTexture texture = pipeline.CurrentViewportTexture;
        if (!texture.IsValid)
        {
            throw new InvalidOperationException("Game presentation 尚未产生可捕获的完整纹理。");
        }

        byte[] rgba = AllocateCaptureBuffer(texture.Width, texture.Height);
        GL gl = _window.Gl;
        gl.GetInteger(GLEnum.ReadFramebufferBinding, out int previousFramebuffer);
        gl.GetInteger(GLEnum.DrawFramebufferBinding, out int previousDrawFramebuffer);
        gl.GetInteger(GLEnum.ReadBuffer, out int previousReadBuffer);
        try
        {
            gl.BindFramebuffer(
                FramebufferTarget.Framebuffer,
                pipeline.CurrentViewportFramebuffer);
            GLEnum status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
            {
                throw new InvalidOperationException($"Game capture framebuffer 不完整：{status}。");
            }

            gl.ReadBuffer(ReadBufferMode.ColorAttachment0);
            gl.ReadPixels(
                0,
                0,
                (uint)texture.Width,
                (uint)texture.Height,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                rgba);
            ThrowIfCaptureGlError(gl, "Game presentation ReadPixels");
        }
        finally
        {
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)previousFramebuffer);
            gl.ReadBuffer((ReadBufferMode)previousReadBuffer);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)previousDrawFramebuffer);
        }

        return new EditorAutomationRawCapture
        {
            Kind = "game-presentation",
            Width = texture.Width,
            Height = texture.Height,
            ContentRevision = texture.Revision,
            RgbaBottomUp = rgba,
        };
    }

    private static byte[] AllocateCaptureBuffer(int width, int height)
    {
        long length = checked((long)width * height * 4);
        return width <= 0 || height <= 0 || length > MaximumAutomationCaptureBytes
            ? throw new InvalidOperationException(
                $"Capture {width}x{height} 需要 {length} bytes，超过 {MaximumAutomationCaptureBytes} bytes 上限。")
            : GC.AllocateUninitializedArray<byte>(checked((int)length));
    }

    private static void ThrowIfCaptureGlError(GL gl, string operation)
    {
        GLEnum error = gl.GetError();
        if (error != GLEnum.NoError)
        {
            throw new InvalidOperationException($"{operation} 失败：OpenGL {error}。");
        }
    }

    /// <summary>在 Play/Paused 中选择包含指定 Behaviour 的 runtime entity，并把 Inspector 切到前台。</summary>
    public bool TrySelectRuntimeInspectorEntity(string behaviourTypeSuffix, out string entityHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviourTypeSuffix);
        entityHandle = string.Empty;
        if (CapturePlayMode() == EditorMode.Edit ||
            _runtimeHierarchy is null ||
            _gameObjectInspectorPanel is null)
        {
            return false;
        }

        SceneHierarchySnapshot hierarchy = _runtimeHierarchy.Capture();
        for (int i = 0; i < hierarchy.Entities.Count; i++)
        {
            SceneHierarchyEntityItem item = hierarchy.Entities[i];
            if (!_runtimeHierarchy.TryGetEntity(item.Handle, out Scripting.ScriptEntityInspection entity))
            {
                continue;
            }

            bool matched = false;
            for (int componentIndex = 0; componentIndex < entity.Components.Length; componentIndex++)
            {
                if (entity.Components[componentIndex].TypeName.EndsWith(
                    behaviourTypeSuffix,
                    StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            _sceneModel?.Select(null);
            _editor.Selection.SelectEntity(item.Handle);
            _ = _editor.TryShowPanel(EditorDockSpace.SceneHierarchyWindowTitle);
            _ = _editor.TryShowPanel(EditorDockSpace.InspectorWindowTitle);
            _gameObjectInspectorPanel.RequestFocus();
            entityHandle = item.Handle;
            return true;
        }

        return false;
    }

    /// <summary>捕获最后一次实际完成 Draw 的 runtime Inspector 结构快照。</summary>
    public ScriptedRuntimeInspectorProbeSnapshot CaptureScriptedRuntimeInspectorProbe()
    {
        return _gameObjectInspectorPanel?.CaptureScriptedRuntimeInspectorProbe() ?? default;
    }

    internal SceneHierarchySnapshot CaptureAutomationRuntimeHierarchy()
    {
        return (_runtimeHierarchy ??
            throw new InvalidOperationException("Runtime hierarchy 数据源尚未注册。"))
            .Capture();
    }

    internal bool TryGetAutomationRuntimeBody(int bodyKey, out RigidBodySnapshot body)
    {
        if (_runtimeHierarchy is null)
        {
            body = default;
            return false;
        }

        return _runtimeHierarchy.TryGetBody(bodyKey, out body);
    }

    internal bool TryInspectAutomationCell(
        int worldX,
        int worldY,
        out SimulationCellInspection inspection)
    {
        if (_simulationInspectApi is null)
        {
            inspection = default;
            return false;
        }

        return _simulationInspectApi.TryInspectCell(worldX, worldY, out inspection);
    }

    internal PhysicsTuningState CaptureAutomationPhysicsTuning()
    {
        return (_physicsTuningService ??
            throw new InvalidOperationException("Physics tuning service 尚未注册。"))
            .Capture();
    }

    internal void ApplyAutomationPhysicsTuning(PhysicsTuningState state)
    {
        (_physicsTuningService ??
            throw new InvalidOperationException("Physics tuning service 尚未注册。"))
            .Apply(state);
    }

    internal ParticleTuningState CaptureAutomationParticleTuning()
    {
        return (_particleTuningService ??
            throw new InvalidOperationException("Particle tuning service 尚未注册。"))
            .Capture();
    }

    internal void ApplyAutomationParticleTuning(ParticleTuningState state)
    {
        (_particleTuningService ??
            throw new InvalidOperationException("Particle tuning service 尚未注册。"))
            .Apply(state);
    }

    internal LightingTuningState CaptureAutomationLightingTuning()
    {
        return (_lightingTuningService ??
            throw new InvalidOperationException("Lighting tuning service 尚未注册。"))
            .Capture();
    }

    internal void ApplyAutomationLightingTuning(LightingTuningState state)
    {
        (_lightingTuningService ??
            throw new InvalidOperationException("Lighting tuning service 尚未注册。"))
            .Apply(state);
    }

    public EditorRenderBridge? Bridge { get; private set; }

    public bool TryShowPanel(string title)
    {
        return _editor.TryShowPanel(title);
    }

    public bool TryGetPanelVisibility(string title, out bool visible)
    {
        return _editor.TryGetPanelVisibility(title, out visible);
    }

    public bool TrySetPanelVisibility(string title, bool visible)
    {
        return _editor.TrySetPanelVisibility(title, visible);
    }

    internal EditorPanelSnapshot[] CaptureAutomationPanels()
    {
        return _editor.CapturePanels();
    }

    internal bool TrySetAutomationPanel(string panelId, bool visible, bool focus)
    {
        if (string.Equals(panelId, EditorPanelIds.Brush, StringComparison.Ordinal) && !visible)
        {
            _ = _sceneViewPanel?.SetMaterialBrushActive(false);
        }

        return _editor.TrySetPanelById(panelId, visible, focus);
    }

    internal bool TryRestoreAutomationPanels(IReadOnlyList<EditorPanelSnapshot> snapshots)
    {
        return _editor.TryRestorePanels(snapshots);
    }

    internal string CaptureAutomationDockLayout()
    {
        return _editor.CaptureDockLayout();
    }

    internal void ApplyAutomationDockLayout(string layout)
    {
        _editor.ApplyDockLayout(layout);
    }

    internal bool TrySetAutomationPanelDock(
        string panelId,
        string? targetPanelId,
        EditorDockWindowRequest request,
        out string diagnostic)
    {
        return _editor.TrySetPanelDock(panelId, targetPanelId, request, out diagnostic);
    }

    internal bool TryCaptureAutomationSceneTool(out AutomationSceneToolSnapshot snapshot)
    {
        if (_sceneViewPanel is null)
        {
            snapshot = null!;
            return false;
        }

        SceneAuthoringCameraSnapshot camera = _sceneViewPanel.CameraSnapshot;
        SceneGizmoSnapSettings snap = _sceneViewPanel.SnapSettings;
        snapshot = new AutomationSceneToolSnapshot
        {
            Tool = _sceneViewPanel.MaterialBrushActive
                ? AutomationSceneTool.Brush
                : ResolveAutomationSceneTool(_sceneViewPanel.Operation),
            GizmoSpace = _sceneViewPanel.GizmoMode == Hexa.NET.ImGuizmo.ImGuizmoMode.Local
                ? AutomationGizmoSpace.Local
                : AutomationGizmoSpace.World,
            GridVisible = _sceneViewPanel.ShowGrid,
            SnapEnabled = snap.Enabled,
            MoveSnap = snap.Move,
            RotationSnapDegrees = snap.RotationDegrees,
            ScaleSnap = snap.Scale,
            CameraCenterX = camera.CenterX,
            CameraCenterY = camera.CenterY,
            CameraCellsPerPixel = camera.CellsPerPixel,
            BrushPanelVisible = _brushPanel?.Visible == true,
            OverlayDock = _sceneViewPanel.ToolOverlayDock switch
            {
                SceneToolOverlayDock.Left => AutomationSceneToolOverlayDock.Left,
                SceneToolOverlayDock.Floating => AutomationSceneToolOverlayDock.Floating,
                SceneToolOverlayDock.Right => AutomationSceneToolOverlayDock.Right,
                _ => throw new InvalidOperationException("未知 Scene tool overlay dock。"),
            },
            OverlayOffsetX = _sceneViewPanel.ToolOverlayFloatingOffset.X,
            OverlayOffsetY = _sceneViewPanel.ToolOverlayFloatingOffset.Y,
            Brush = _brushPanel is null ? null : CaptureBrushSettings(_brushPanel.Settings),
        };
        return true;
    }

    private static AutomationSceneTool ResolveAutomationSceneTool(
        Hexa.NET.ImGuizmo.ImGuizmoOperation operation)
    {
        return operation == Hexa.NET.ImGuizmo.ImGuizmoOperation.Translate
            ? AutomationSceneTool.Move
            : operation == Hexa.NET.ImGuizmo.ImGuizmoOperation.RotateZ
                ? AutomationSceneTool.Rotate
                : operation == Hexa.NET.ImGuizmo.ImGuizmoOperation.Scale
                    ? AutomationSceneTool.Scale
                    : throw new InvalidOperationException(
                        "Scene View gizmo operation 违反仅支持 Move、Rotate Z 与 Scale 的内部不变式。");
    }

    internal bool TrySetAutomationSceneTool(
        AutomationSceneToolSetRequest request,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_sceneViewPanel is null)
        {
            diagnostic = "Scene View 尚未注册。";
            return false;
        }

        if (request.Tool is { } requestedTool &&
            requestedTool is not AutomationSceneTool.Move and
            not AutomationSceneTool.Rotate and
            not AutomationSceneTool.Scale and
            not AutomationSceneTool.Brush)
        {
            diagnostic = $"未知 Scene tool：{requestedTool}。";
            return false;
        }

        if (request.Tool == AutomationSceneTool.Brush &&
            (_brushPanel is null || CapturePlayMode() != EditorMode.Edit))
        {
            diagnostic = "当前工程没有可用世界画刷，或 Editor 不在 Edit mode。";
            return false;
        }

        if (request.Tool == AutomationSceneTool.Brush && request.BrushPanelVisible == false)
        {
            diagnostic = "Brush tool 激活时 BrushPanelVisible 不能为 false。";
            return false;
        }

        if (request.BrushPanelVisible == true && _brushPanel is null)
        {
            diagnostic = "当前工程没有 Brush 参数面板。";
            return false;
        }

        bool changesSnap = request.SnapEnabled.HasValue ||
            request.MoveSnap.HasValue ||
            request.RotationSnapDegrees.HasValue ||
            request.ScaleSnap.HasValue;
        SceneGizmoSnapSettings currentSnap = _sceneViewPanel.SnapSettings;
        SceneGizmoSnapSettings nextSnap = new(
            request.SnapEnabled ?? currentSnap.Enabled,
            request.MoveSnap ?? currentSnap.Move,
            request.RotationSnapDegrees ?? currentSnap.RotationDegrees,
            request.ScaleSnap ?? currentSnap.Scale);
        if (changesSnap && !nextSnap.IsValid)
        {
            diagnostic =
                "Scene gizmo snap 步长必须是有限正数；Move/Rotate/Scale 分别不得超过 1000000/360/100000。";
            return false;
        }

        bool hasCameraX = request.CameraCenterX.HasValue;
        bool hasCameraY = request.CameraCenterY.HasValue;
        SceneAuthoringCameraSnapshot currentCamera = _sceneViewPanel.CameraSnapshot;
        float cameraX = request.CameraCenterX ?? currentCamera.CenterX;
        float cameraY = request.CameraCenterY ?? currentCamera.CenterY;
        float cellsPerPixel = request.CameraCellsPerPixel ?? currentCamera.CellsPerPixel;
        bool changesCamera = hasCameraX || request.CameraCellsPerPixel.HasValue;
        if (hasCameraX != hasCameraY ||
            (changesCamera &&
             (!float.IsFinite(cameraX) ||
              !float.IsFinite(cameraY) ||
              MathF.Abs(cameraX) > 100_000_000f ||
              MathF.Abs(cameraY) > 100_000_000f ||
              !float.IsFinite(cellsPerPixel) ||
              cellsPerPixel is < SceneAuthoringCamera.MinCellsPerPixel or
                  > SceneAuthoringCamera.MaxCellsPerPixel)))
        {
            diagnostic =
                "Scene camera X/Y 必须同时提供且中心有限，cellsPerPixel 必须在 0.05..64。";
            return false;
        }

        bool hasOffsetX = request.OverlayOffsetX.HasValue;
        bool hasOffsetY = request.OverlayOffsetY.HasValue;
        if (hasOffsetX != hasOffsetY ||
            (request.OverlayDock is { } overlayDock && !Enum.IsDefined(overlayDock)) ||
            (hasOffsetX &&
             (!float.IsFinite(request.OverlayOffsetX!.Value) ||
              !float.IsFinite(request.OverlayOffsetY!.Value) ||
              MathF.Abs(request.OverlayOffsetX.Value) > 1_000_000f ||
              MathF.Abs(request.OverlayOffsetY.Value) > 1_000_000f)))
        {
            diagnostic = "Scene tool overlay dock/offset 无效，X/Y 必须同时提供有限值。";
            return false;
        }

        if (request.Brush is not null && !TryApplyBrushSettings(request.Brush, out diagnostic))
        {
            return false;
        }

        if (request.GizmoSpace is { } gizmoSpace)
        {
            bool wantsLocal = gizmoSpace == AutomationGizmoSpace.Local;
            if (_sceneViewPanel.GizmoMode == Hexa.NET.ImGuizmo.ImGuizmoMode.Local != wantsLocal)
            {
                _sceneViewPanel.ToggleGizmoMode();
            }
        }

        if (request.GridVisible is { } gridVisible && _sceneViewPanel.ShowGrid != gridVisible)
        {
            _sceneViewPanel.ToggleGrid();
        }

        if (changesSnap)
        {
            _sceneViewPanel.SetSnapSettings(nextSnap);
        }

        if (changesCamera)
        {
            _sceneViewPanel.SetCamera(cameraX, cameraY, cellsPerPixel);
        }

        if (request.OverlayDock.HasValue || hasOffsetX)
        {
            SceneToolOverlayDock dock = request.OverlayDock switch
            {
                AutomationSceneToolOverlayDock.Left => SceneToolOverlayDock.Left,
                AutomationSceneToolOverlayDock.Floating => SceneToolOverlayDock.Floating,
                AutomationSceneToolOverlayDock.Right => SceneToolOverlayDock.Right,
                null => _sceneViewPanel.ToolOverlayDock,
                _ => throw new InvalidOperationException("未知 Scene tool overlay dock。"),
            };
            System.Numerics.Vector2 offset = hasOffsetX
                ? new System.Numerics.Vector2(
                    request.OverlayOffsetX!.Value,
                    request.OverlayOffsetY!.Value)
                : _sceneViewPanel.ToolOverlayFloatingOffset;
            _sceneViewPanel.SetToolOverlay(dock, offset);
        }

        if (request.BrushPanelVisible is { } brushPanelVisible && _brushPanel is not null)
        {
            if (!brushPanelVisible)
            {
                _ = _sceneViewPanel.SetMaterialBrushActive(false);
            }

            _brushPanel.Visible = brushPanelVisible;
        }

        if (request.Tool is { } tool)
        {
            switch (tool)
            {
                case AutomationSceneTool.Move:
                    _sceneViewPanel.SetOperation(Hexa.NET.ImGuizmo.ImGuizmoOperation.Translate);
                    break;
                case AutomationSceneTool.Rotate:
                    _sceneViewPanel.SetOperation(Hexa.NET.ImGuizmo.ImGuizmoOperation.RotateZ);
                    break;
                case AutomationSceneTool.Scale:
                    _sceneViewPanel.SetOperation(Hexa.NET.ImGuizmo.ImGuizmoOperation.Scale);
                    break;
                case AutomationSceneTool.Brush:
                    if (!_sceneViewPanel.SetMaterialBrushActive(true))
                    {
                        throw new InvalidOperationException(
                            "Scene tool 已通过 Brush preflight，但无法激活世界画刷。");
                    }

                    break;
                default:
                    throw new InvalidOperationException($"未知 Scene tool：{tool}。");
            }
        }

        diagnostic = string.Empty;
        return true;
    }

    internal bool TryFrameAutomationScene(
        AutomationSceneFrameTarget target,
        out string diagnostic)
    {
        if (_sceneViewPanel is null)
        {
            diagnostic = "Scene View 尚未注册。";
            return false;
        }

        bool framed = target switch
        {
            AutomationSceneFrameTarget.All => _sceneViewPanel.FrameAll(),
            AutomationSceneFrameTarget.Selected => _sceneViewPanel.FrameSelected(_editor.Selection),
            _ => false,
        };
        diagnostic = framed ? string.Empty : $"Scene View 无法 frame {target}。";
        return framed;
    }

    internal bool TryApplyAutomationBrush(
        int worldX,
        int worldY,
        out AutomationBrushApplyResult result,
        out string diagnostic)
    {
        if (_sceneViewPanel is null || _brushPanel is null)
        {
            result = null!;
            diagnostic = "当前工程没有可用世界画刷。";
            return false;
        }

        if (!_sceneViewPanel.MaterialBrushActive || CapturePlayMode() != EditorMode.Edit)
        {
            result = null!;
            diagnostic = "世界画刷必须在 Edit mode 显式激活后才能应用。";
            return false;
        }

        int written = _sceneViewPanel.ApplyMaterialBrushAt(worldX, worldY);
        result = new AutomationBrushApplyResult
        {
            WrittenCells = written,
            SkippedNonResidentCells = _brushPanel.LastSkippedNonResidentCells,
            SkippedOutOfBoundsCells = _brushPanel.LastSkippedOutOfBoundsCells,
            Diagnostic = _brushPanel.Status,
        };
        diagnostic = string.Empty;
        return true;
    }

    internal bool TryApplyAutomationBrushStroke(
        IReadOnlyList<AutomationWorldPoint> points,
        out AutomationBrushStrokeResult result,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (_sceneViewPanel is null || _brushPanel is null)
        {
            result = null!;
            diagnostic = "当前工程没有可用世界画刷。";
            return false;
        }

        if (!_sceneViewPanel.MaterialBrushActive || CapturePlayMode() != EditorMode.Edit)
        {
            result = null!;
            diagnostic = "世界画刷必须在 Edit mode 显式激活后才能应用。";
            return false;
        }

        long writtenCells = 0;
        long skippedNonResidentCells = 0;
        long skippedOutOfBoundsCells = 0;
        int sampleCount = 0;
        ApplySample(points[0].X, points[0].Y);
        for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
        {
            int x = points[pointIndex - 1].X;
            int y = points[pointIndex - 1].Y;
            int targetX = points[pointIndex].X;
            int targetY = points[pointIndex].Y;
            int deltaX = Math.Abs(targetX - x);
            int stepX = x < targetX ? 1 : -1;
            int deltaY = -Math.Abs(targetY - y);
            int stepY = y < targetY ? 1 : -1;
            int error = deltaX + deltaY;
            while (x != targetX || y != targetY)
            {
                int doubledError = error * 2;
                if (doubledError >= deltaY)
                {
                    error += deltaY;
                    x += stepX;
                }

                if (doubledError <= deltaX)
                {
                    error += deltaX;
                    y += stepY;
                }

                ApplySample(x, y);
            }
        }

        result = new AutomationBrushStrokeResult
        {
            ControlPointCount = points.Count,
            SampleCount = sampleCount,
            WrittenCells = writtenCells,
            SkippedNonResidentCells = skippedNonResidentCells,
            SkippedOutOfBoundsCells = skippedOutOfBoundsCells,
            Diagnostic = _brushPanel.Status,
        };
        diagnostic = string.Empty;
        return true;

        void ApplySample(int worldX, int worldY)
        {
            writtenCells += _sceneViewPanel.ApplyMaterialBrushAt(worldX, worldY);
            skippedNonResidentCells += _brushPanel.LastSkippedNonResidentCells;
            skippedOutOfBoundsCells += _brushPanel.LastSkippedOutOfBoundsCells;
            sampleCount++;
        }
    }

    private AutomationBrushSettings CaptureBrushSettings(MaterialBrushSettings settings)
    {
        MaterialTable materials = _engine?.Context.GetService<MaterialTable>() ??
            throw new InvalidOperationException("Brush Tool 缺少 MaterialTable。");
        return new AutomationBrushSettings
        {
            Tool = settings.Tool.ToString(),
            Shape = settings.Shape.ToString(),
            MaterialName = materials.GetName(settings.MaterialId),
            MaterialId = settings.MaterialId,
            Radius = settings.Radius,
            Probability = settings.Probability,
            TemperatureMode = settings.TemperatureMode.ToString(),
            TemperatureCelsius = settings.TemperatureCelsius,
        };
    }

    private bool TryApplyBrushSettings(AutomationBrushSettings settings, out string diagnostic)
    {
        if (_brushPanel is null)
        {
            diagnostic = "当前工程没有可用世界画刷。";
            return false;
        }

        if (_engine is null || !_engine.Context.TryGetService(out MaterialTable materials) ||
            string.IsNullOrWhiteSpace(settings.MaterialName) || settings.MaterialName.Length > 256 ||
            !materials.TryGetId(settings.MaterialName.Trim(), out ushort materialId))
        {
            diagnostic = "画刷设置无效：materialName 必须引用当前 Engine 中的稳定 live 材质名称。";
            return false;
        }

        if (!Enum.TryParse(settings.Tool, ignoreCase: true, out EditorBrushTool tool) ||
            !Enum.IsDefined(tool) ||
            !Enum.TryParse(settings.Shape, ignoreCase: true, out EditorBrushShape shape) ||
            !Enum.IsDefined(shape) ||
            !Enum.TryParse(settings.TemperatureMode, ignoreCase: true, out TemperatureBrushMode temperatureMode) ||
            !Enum.IsDefined(temperatureMode) ||
            settings.Radius is < 0 or > 128 ||
            !float.IsFinite(settings.Probability) || settings.Probability is < 0f or > 1f ||
            !float.IsFinite(settings.TemperatureCelsius))
        {
            diagnostic = "画刷设置无效：枚举、radius、probability 或 temperature 超出公共 API 契约。";
            return false;
        }

        MaterialBrushSettings target = _brushPanel.Settings;
        target.Tool = tool;
        target.Shape = shape;
        target.MaterialId = materialId;
        target.Radius = settings.Radius;
        target.Probability = settings.Probability;
        target.TemperatureMode = temperatureMode;
        target.TemperatureCelsius = settings.TemperatureCelsius;
        diagnostic = string.Empty;
        return true;
    }

    public void ResetLayout()
    {
        _editor.ResetDockLayout();
        _gameObjectInspectorPanel?.RequestFocus();
    }

    public void PrepareFrame()
    {
        EditorMode mode = CapturePlayMode();
        if (_assetBrowserDataSource?.ApplyPendingChanges() == true)
        {
            _app.NotifyAutomationAssetsChanged();
            // Project 面板被关闭时文件 watcher 仍必须推进；Scene XHTML/CSS/字体/图片预览
            // 不能依赖 Project Window 是否正在 Draw。
            _sceneWebCanvasPreview?.InvalidateAssets();
        }

        if (_lastPreparedMode is EditorMode.Play or EditorMode.Paused && mode == EditorMode.Edit)
        {
            // Runtime Inspector 即使被用户关闭，也必须在退出 Play 时结束临时编辑事务，
            // 不能把恢复/清理职责绑定到面板是否继续 Draw。
            _runtimeHierarchy?.RestoreTemporaryEdits();
            _sceneViewPanel?.InvalidateWorldTexture();
        }

        _lastPreparedMode = mode;
        _gameViewPanel?.PrepareFrame(mode);
        _gameObjectInspectorPanel?.PrepareFrame(
            _editor.Selection.GameObjectStableId,
            _editor.Selection.EntityHandle);
        // Scene View 关闭后 EditorApp 不再 Draw 面板；gizmo 事务仍须响应 selection/mode/scene 生命周期。
        _sceneViewPanel?.PrepareFrame(_editor.Selection.GameObjectStableId, mode);
        _consolePanel?.PrepareFrame();
        if (_buildSettingsPanel?.HasPendingWork == true)
        {
            _buildSettingsPanel.PrepareFrame();
        }
        _editor.SetUiScale(_app.UiScale);
        _editor.SetLayoutPersistence(_app.Preferences.Current.SaveLayoutOnExit);
    }

    public void FlushPendingAuthoringEdits()
    {
        _gameObjectInspectorPanel?.CommitPendingEdits();
        _ = _sceneViewPanel?.CommitGizmoTransform();
    }

    internal EditorAutomationSelectionSnapshot CaptureAutomationSelection()
    {
        EditorSelection selection = _editor.Selection;
        return new EditorAutomationSelectionSnapshot(
            selection.CellX,
            selection.CellY,
            selection.MaterialId,
            selection.AssetId,
            selection.AssetPath,
            selection.FolderPath,
            selection.EntityHandle,
            selection.GameObjectStableId,
            selection.BodyId);
    }

    internal bool TrySetAutomationProjectAssetSelection(string path)
    {
        return (_assetBrowserPanel ??
            throw new InvalidOperationException("Project Window 面板尚未注册。"))
            .SelectAsset(path, _editor.Selection);
    }

    internal bool TrySetAutomationProjectFolderSelection(string path)
    {
        return (_assetBrowserPanel ??
            throw new InvalidOperationException("Project Window 面板尚未注册。"))
            .SelectFolder(path, _editor.Selection);
    }

    internal AssetBrowserViewState CaptureAutomationProjectWindowViewState()
    {
        return (_assetBrowserPanel ??
            throw new InvalidOperationException("Project Window 面板尚未注册。"))
            .CaptureViewState();
    }

    internal string CaptureAutomationProjectWindowActiveFolderPath()
    {
        return (_assetBrowserPanel ??
            throw new InvalidOperationException("Project Window 面板尚未注册。"))
            .ActiveFolderPath;
    }

    internal bool ApplyAutomationProjectWindowViewState(
        in AssetBrowserViewState state,
        bool notifyChanged)
    {
        return (_assetBrowserPanel ??
            throw new InvalidOperationException("Project Window 面板尚未注册。"))
            .ApplyViewState(state, notifyChanged);
    }

    internal void ReloadAutomationAssetBrowserSnapshot()
    {
        _ = (_assetBrowserPanel ??
            throw new InvalidOperationException("Project Window 面板尚未注册。"))
            .ReloadCachedSnapshot(_editor.Selection);
    }

    internal void ClearAutomationProjectSelection()
    {
        _editor.Selection.ClearProject();
    }

    internal bool TryPreviewAutomationAudio(string path, out string diagnostic)
    {
        AssetBrowserPanel panel = _assetBrowserPanel ??
            throw new InvalidOperationException("Project Window 面板尚未注册。");
        bool succeeded = panel.TryPreviewAudio(path);
        diagnostic = panel.Status;
        return succeeded;
    }

    internal string CaptureAutomationSaveRoot()
    {
        return _saveLoadService?.SaveRoot ??
            throw new InvalidOperationException("Save / Load 服务尚未注册。");
    }

    internal void RestoreAutomationSelection(in EditorAutomationSelectionSnapshot snapshot)
    {
        EditorSelection selection = _editor.Selection;
        selection.Clear();
        if (snapshot.CellX is { } cellX && snapshot.CellY is { } cellY)
        {
            selection.SelectCell(cellX, cellY);
        }

        if (snapshot.MaterialId is { } materialId)
        {
            selection.SelectMaterial(materialId);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.AssetId) && !string.IsNullOrWhiteSpace(snapshot.AssetPath))
        {
            selection.SelectAsset(snapshot.AssetId, snapshot.AssetPath);
        }
        else if (!string.IsNullOrWhiteSpace(snapshot.AssetPath))
        {
            selection.SelectAsset(snapshot.AssetPath);
        }
        else if (snapshot.FolderPath is not null)
        {
            selection.SelectFolder(snapshot.FolderPath);
        }
        else if (!string.IsNullOrWhiteSpace(snapshot.EntityHandle))
        {
            selection.SelectEntity(snapshot.EntityHandle);
        }
        else if (snapshot.GameObjectStableId is { } stableId)
        {
            selection.SelectGameObject(stableId);
        }
        else if (snapshot.BodyId is { } bodyId)
        {
            selection.SelectBody(bodyId);
        }
    }

    internal void SetAutomationGameObjectSelection(int? stableId)
    {
        EditorSelection selection = _editor.Selection;
        selection.Clear();
        if (stableId is { } value)
        {
            selection.SelectGameObject(value);
        }
    }

    public void RequestGameViewFocus()
    {
        if (_gameViewPanel is not null)
        {
            _gameViewPanel.Visible = true;
            _gameViewPanel.RequestFocus();
        }
    }

    public void InvalidateAuthoringWorld()
    {
        _sceneViewPanel?.InvalidateWorldTexture();
    }

    internal EditorAssetBrowserDataSource RequireAutomationAssetDatabase()
    {
        return _assetBrowserDataSource ??
            throw new InvalidOperationException("Project Window Asset Database 尚未注册。");
    }

    internal ProjectSettingsDto CaptureAutomationProjectSettings()
    {
        return _projectSettingsPanel?.AppliedSettings ??
            throw new InvalidOperationException("Project Settings 面板尚未注册。");
    }

    internal EditorProjectSettingsAutomationState CaptureAutomationProjectSettingsState()
    {
        ProjectSettingsPanel panel = _projectSettingsPanel ??
            throw new InvalidOperationException("Project Settings 面板尚未注册。");
        return new EditorProjectSettingsAutomationState(
            _project.CaptureAutomationSnapshot(),
            panel.CaptureAutomationState());
    }

    internal EditorProjectSettingsAutomationState CreateAutomationProjectSettingsState(
        EditorProjectSettingsAutomationState source,
        ProjectSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ProjectSettingsPanel panel = _projectSettingsPanel ??
            throw new InvalidOperationException("Project Settings 面板尚未注册。");
        return new EditorProjectSettingsAutomationState(
            EditorProject.CreateAutomationProjectSettingsSnapshot(source.Project, settings),
            panel.CreateAutomationAppliedState(settings));
    }

    internal void RestoreAutomationProjectSettingsState(EditorProjectSettingsAutomationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ProjectSettingsPanel panel = _projectSettingsPanel ??
            throw new InvalidOperationException("Project Settings 面板尚未注册。");
        _project.RestoreAutomationSnapshot(state.Project);
        panel.RestoreAutomationState(state.Panel);
    }

    internal bool TryApplyAutomationProjectSettings(ProjectSettingsDto settings, out string diagnostic)
    {
        return (_projectSettingsPanel ??
            throw new InvalidOperationException("Project Settings 面板尚未注册。"))
            .TryApplyProjectSettings(settings, out diagnostic);
    }

    internal PlayerSettingsDto CaptureAutomationPlayerSettings()
    {
        return _playerSettingsPanel?.AppliedSettings ??
            throw new InvalidOperationException("Player Settings 面板尚未注册。");
    }

    internal PlayerSettingsPanelAutomationSnapshot CaptureAutomationPlayerSettingsState()
    {
        return (_playerSettingsPanel ??
            throw new InvalidOperationException("Player Settings 面板尚未注册."))
            .CaptureAutomationState();
    }

    internal PlayerSettingsPanelAutomationSnapshot CreateAutomationPlayerSettingsState(
        PlayerSettingsDto settings)
    {
        return (_playerSettingsPanel ??
            throw new InvalidOperationException("Player Settings 面板尚未注册."))
            .CreateAutomationAppliedState(settings);
    }

    internal void RestoreAutomationPlayerSettingsState(PlayerSettingsPanelAutomationSnapshot state)
    {
        (_playerSettingsPanel ??
            throw new InvalidOperationException("Player Settings 面板尚未注册."))
            .RestoreAutomationState(state);
    }

    internal bool TryApplyAutomationPlayerSettings(PlayerSettingsDto settings, out string diagnostic)
    {
        return (_playerSettingsPanel ??
            throw new InvalidOperationException("Player Settings 面板尚未注册。"))
            .TryApplyPlayerSettings(settings, out diagnostic);
    }

    internal BuildProfileDto CaptureAutomationBuildSettings()
    {
        return (_buildSettingsPanel ??
            throw new InvalidOperationException("Build Settings 面板尚未注册。"))
            .CaptureAutomationSettings();
    }

    internal BuildSettingsPanelAutomationSnapshot CaptureAutomationBuildSettingsState()
    {
        return (_buildSettingsPanel ??
            throw new InvalidOperationException("Build Settings 面板尚未注册。"))
            .CaptureAutomationState();
    }

    internal BuildSettingsPanelUiSnapshot CaptureAutomationBuildPanelState()
    {
        return (_buildSettingsPanel ??
            throw new InvalidOperationException("Build Settings 面板尚未注册。"))
            .CaptureAutomationUiState();
    }

    internal void SetAutomationBuildLogAutoScroll(bool enabled)
    {
        (_buildSettingsPanel ??
            throw new InvalidOperationException("Build Settings 面板尚未注册。"))
            .SetAutomationLogAutoScroll(enabled);
    }

    internal BuildSettingsPanelAutomationSnapshot CreateAutomationBuildSettingsState(
        BuildProfileDto settings)
    {
        return (_buildSettingsPanel ??
            throw new InvalidOperationException("Build Settings 面板尚未注册。"))
            .CreateAutomationAppliedState(settings);
    }

    internal void RestoreAutomationBuildSettingsState(BuildSettingsPanelAutomationSnapshot state)
    {
        (_buildSettingsPanel ??
            throw new InvalidOperationException("Build Settings 面板尚未注册。"))
            .RestoreAutomationState(state);
    }

    internal bool TryApplyAutomationBuildSettings(BuildProfileDto settings, out string diagnostic)
    {
        return (_buildSettingsPanel ??
            throw new InvalidOperationException("Build Settings 面板尚未注册。"))
            .TryApplyAutomationSettings(settings, out diagnostic);
    }

    public bool TryStartScriptedBuildProbe(string outputDirectory, bool runAfterBuild, out string diagnostic)
    {
        if (_buildSettingsPanel is null)
        {
            diagnostic = "Build Settings 面板尚未注册。";
            return false;
        }

        _ = _editor.TryShowPanel(BuildSettingsPanel.PanelTitle);
        return _buildSettingsPanel.TryStartScriptedBuildProbe(outputDirectory, runAfterBuild, out diagnostic);
    }

    public bool TryStartBuild(bool runAfterBuild, out string diagnostic)
    {
        if (_buildSettingsPanel is null)
        {
            diagnostic = "Build Settings 面板尚未注册。";
            return false;
        }

        _ = _editor.TryShowPanel(BuildSettingsPanel.PanelTitle);
        return _buildSettingsPanel.TryStartBuild(runAfterBuild, out diagnostic);
    }

    internal EditorBuildPreflightWorkspace CaptureAutomationBuildPreflightWorkspace()
    {
        return RequireBuildSettingsPanel().CaptureAutomationBuildPreflightWorkspace();
    }

    internal bool TryStartAutomationBuild(
        string buildId,
        bool launchOnSuccess,
        out EditorBuildExecutionSnapshot snapshot,
        out string diagnostic)
    {
        return RequireBuildSettingsPanel().TryStartAutomationBuild(
            buildId,
            launchOnSuccess,
            out snapshot,
            out diagnostic);
    }

    internal EditorBuildExecutionSnapshot CaptureAutomationBuild(string buildId)
    {
        return RequireBuildSettingsPanel().CaptureAutomationBuild(buildId);
    }

    internal EditorBuildExecutionSnapshot[] CaptureAutomationBuilds()
    {
        return RequireBuildSettingsPanel().CaptureAutomationBuilds();
    }

    internal EditorBuildExecutionLogSnapshot CaptureAutomationBuildLog(string buildId)
    {
        return RequireBuildSettingsPanel().CaptureAutomationBuildLog(buildId);
    }

    internal Task<BuildResult> CaptureAutomationBuildCompletion(string buildId)
    {
        return RequireBuildSettingsPanel().CaptureAutomationBuildCompletion(buildId);
    }

    internal bool RequestAutomationBuildCancellation(
        string buildId,
        bool notifyChanged,
        out EditorBuildExecutionSnapshot snapshot)
    {
        return RequireBuildSettingsPanel().RequestAutomationBuildCancellation(
            buildId,
            notifyChanged,
            out snapshot);
    }

    internal EditorPlayerProcessSnapshot LaunchAutomationPlayer(
        string buildId,
        bool notifyChanged,
        string? playerProcessId = null)
    {
        EditorBuildExecutionSnapshot build = RequireBuildSettingsPanel().CaptureAutomationBuild(buildId);
        BuildResult result = build.State == EditorBuildExecutionState.Succeeded && build.Result is not null
            ? build.Result
            : throw new InvalidOperationException($"Build '{buildId}' 尚未成功，不能启动 player。");

        return RequirePlayerProcessManager().Launch(
            buildId,
            result,
            notifyChanged,
            playerProcessId);
    }

    internal EditorPlayerProcessSnapshot CaptureAutomationPlayer(string playerProcessId)
    {
        return RequirePlayerProcessManager().Capture(playerProcessId);
    }

    internal EditorPlayerProcessSnapshot[] CaptureAutomationPlayers()
    {
        return RequirePlayerProcessManager().CaptureAll();
    }

    internal EditorPlayerProcessWaitWorkspace CaptureAutomationPlayerWaitWorkspace(
        string playerProcessId)
    {
        return RequirePlayerProcessManager().CaptureWaitWorkspace(playerProcessId);
    }

    internal bool RequestAutomationPlayerTermination(
        string playerProcessId,
        bool entireProcessTree,
        out EditorPlayerProcessSnapshot snapshot)
    {
        return RequirePlayerProcessManager().RequestTermination(
            playerProcessId,
            entireProcessTree,
            notifyChanged: false,
            out snapshot);
    }

    public ScriptedBuildProbeSnapshot CaptureScriptedBuildProbe()
    {
        return _buildSettingsPanel?.CaptureScriptedBuildProbe() ?? new ScriptedBuildProbeSnapshot();
    }

    public void CancelScriptedBuildProbe()
    {
        _buildSettingsPanel?.CancelScriptedBuildProbe();
    }

    private BuildSettingsPanel RequireBuildSettingsPanel()
    {
        return _buildSettingsPanel ??
            throw new InvalidOperationException("Build Settings 面板尚未注册。");
    }

    private EditorPlayerProcessManager RequirePlayerProcessManager()
    {
        return _playerProcessManager ??
            throw new InvalidOperationException("Player process manager 尚未初始化。");
    }

    public ScriptedBuildSettingsProbeSnapshot ApplyScriptedBuildSettingsProbe(string outputDirectory)
    {
        if (_buildSettingsPanel is null)
        {
            throw new InvalidOperationException("Build Settings 面板尚未注册。");
        }

        _ = _editor.TryShowPanel(BuildSettingsPanel.PanelTitle);
        return _buildSettingsPanel.ApplyScriptedBuildSettingsProbe(outputDirectory);
    }

    public ScriptedBuildSettingsProbeSnapshot CaptureScriptedBuildSettingsProbe()
    {
        return _buildSettingsPanel?.CaptureScriptedBuildSettingsProbe() ??
            throw new InvalidOperationException("Build Settings 面板尚未注册。");
    }

    public ScriptedBuildSettingsFooterProbeSnapshot CaptureScriptedBuildSettingsFooterProbe()
    {
        return _buildSettingsPanel?.CaptureScriptedBuildSettingsFooterProbe() ??
            throw new InvalidOperationException("Build Settings 面板尚未注册。");
    }

    public bool RequestScriptedBuildSettingsActionsOverflow()
    {
        return _buildSettingsPanel?.RequestScriptedBuildSettingsActionsOverflow() == true;
    }

    public ScriptedProjectSettingsProbeSnapshot ApplyScriptedProjectSettingsProbe()
    {
        if (_projectSettingsPanel is null)
        {
            throw new InvalidOperationException("Project Settings 面板尚未注册。");
        }

        _ = _editor.TryShowPanel(ProjectSettingsPanel.PanelTitle);
        return _projectSettingsPanel.ApplyScriptedProjectSettingsProbe();
    }

    public ScriptedProjectSettingsProbeSnapshot CaptureScriptedProjectSettingsProbe()
    {
        return _projectSettingsPanel?.CaptureScriptedProjectSettingsProbe() ??
            throw new InvalidOperationException("Project Settings 面板尚未注册。");
    }

    public ScriptedPlayerSettingsProbeSnapshot ApplyScriptedPlayerSettingsProbe()
    {
        if (_playerSettingsPanel is null)
        {
            throw new InvalidOperationException("Player Settings 面板尚未注册。");
        }

        _ = _editor.TryShowPanel(PlayerSettingsPanel.PanelTitle);
        return _playerSettingsPanel.ApplyScriptedPlayerSettingsProbe();
    }

    public ScriptedPlayerSettingsProbeSnapshot CaptureScriptedPlayerSettingsProbe()
    {
        return _playerSettingsPanel?.CaptureScriptedPlayerSettingsProbe() ??
            throw new InvalidOperationException("Player Settings 面板尚未注册。");
    }

    /// <summary>
    /// 捕获 Project/Player Settings 最近一帧的真实窗口几何与草稿状态。
    /// </summary>
    public ScriptedSettingsPanelPresentationSnapshot CaptureScriptedSettingsPanelPresentation(string target)
    {
        return target switch
        {
            "project" when _projectSettingsPanel is not null => new ScriptedSettingsPanelPresentationSnapshot(
                target,
                _projectSettingsPanel.Visible,
                _projectSettingsPanel.LastWindowPosition,
                _projectSettingsPanel.LastWindowSize,
                _projectSettingsPanel.HasPendingChanges,
                _projectSettingsPanel.ValidationMessage),
            "player" when _playerSettingsPanel is not null => new ScriptedSettingsPanelPresentationSnapshot(
                target,
                _playerSettingsPanel.Visible,
                _playerSettingsPanel.LastWindowPosition,
                _playerSettingsPanel.LastWindowSize,
                _playerSettingsPanel.HasPendingChanges,
                _playerSettingsPanel.ValidationMessage),
            "project" => throw new InvalidOperationException("Project Settings 面板尚未注册。"),
            "player" => throw new InvalidOperationException("Player Settings 面板尚未注册。"),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "设置面板探针仅支持 project 或 player。"),
        };
    }

    /// <summary>
    /// 绑定场景模型、撤销栈与 Prefab 存储，供后续面板注册使用。
    /// </summary>
    public void ConfigureAuthoring(
        EditorSceneModel sceneModel,
        EditorUndoStack undoStack,
        EditorPrefabAssetStore prefabs,
        AuthoringWorldPreviewRuntime authoringWorld)
    {
        if (_panelsRegistered)
        {
            throw new InvalidOperationException("Authoring 服务必须在 Editor 面板注册前配置。");
        }

        _sceneModel = sceneModel ?? throw new ArgumentNullException(nameof(sceneModel));
        _undoStack = undoStack ?? throw new ArgumentNullException(nameof(undoStack));
        _undoStack.CanModifyScene = () =>
            CapturePlayMode() == EditorMode.Edit && !_app.IsAutomationTransactionActive;
        _prefabs = prefabs ?? throw new ArgumentNullException(nameof(prefabs));
        _authoringWorld = authoringWorld ?? throw new ArgumentNullException(nameof(authoringWorld));
    }

    /// <summary>
    /// 将 Editor 面板、输入桥接与渲染桥接挂载到 Engine 窗口运行时。
    /// </summary>
    public IDisposable? Attach(Engine engine, RenderWindow window, RenderPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (!ReferenceEquals(window, _window))
        {
            throw new InvalidOperationException("Editor host 必须挂载到构造时的同一个 RenderWindow。");
        }

        _engine = engine;
        _pipeline = pipeline;
        engine.Context.RegisterService<IEditorInputCaptureSource>(this);
        _textureThumbnailProvider ??= new EditorTextureThumbnailProvider(_project.ContentRootPath, window);
        // 注册层级/Inspector/资产浏览器/构建设置等 ImGui 面板
        RegisterPanels(engine, window, pipeline);
        EditorWindowInputConnector input = new(window, _editor.Input);
        EditorExternalAssetDropConnector externalAssetDrop = new(
            window,
            _assetBrowserPanel ?? throw new InvalidOperationException("Project Window 尚未注册，无法绑定系统 file-drop。"),
            _app.ConsoleStore);
        Bridge = EditorRenderBridge.AttachIfEnabled(
            pipeline,
            _editor,
            engine.Context.Counters,
            engine.Context.Profiler,
            () => BuildRuntimeDiagnostics(engine));
        return new CompositeDisposable(
            input,
            Bridge,
            _assetBrowserDataSource,
            _textureThumbnailProvider,
            _editor,
            _gameObjectInspectorPanel,
            _sceneWebCanvasPreview,
            _sceneViewPanel,
            _playerProcessManager,
            _buildSettingsPanel,
            externalAssetDrop);
    }

    public bool TryGetInputCapture(out EditorHostInputCapture capture)
    {
        EditorInputSnapshot editorCapture = _editor.Input.Capture;
        EditorMode mode = CapturePlayMode();
        if (_sceneViewPanel is { Visible: true, InputFocused: true })
        {
            capture = EditorGameViewContract.ResolveEditorInputCapture(
                EditorGameViewContract.SceneView(mode),
                editorCapture,
                viewportHasInputFocus: true);
            return true;
        }

        EditorViewportContract contract = _gameViewPanel?.CaptureContract(mode) ?? EditorGameViewContract.SceneView(mode);
        capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            pointerHasInputFocus: _gameViewPanel is { Visible: true, PointerHovered: true },
            keyboardHasInputFocus: _gameViewPanel is { Visible: true, KeyboardFocused: true });
        return true;
    }

    public bool TryMapPointerToViewport(out float viewportX, out float viewportY)
    {
        viewportX = 0f;
        viewportY = 0f;
        EditorMode mode = CapturePlayMode();
        if (mode is not (EditorMode.Play or EditorMode.Paused) ||
            _gameViewPanel is not { Visible: true, PointerHovered: true } gameView ||
            !gameView.LastViewportSnapshot.TryMapPanelToWorld(gameView.LastPointerPanelPoint, out System.Numerics.Vector2 viewportPoint))
        {
            return false;
        }

        viewportX = viewportPoint.X;
        viewportY = viewportPoint.Y;
        return true;
    }

    public bool AllowsRuntimeGuiKeyboardInput =>
        CapturePlayMode() is EditorMode.Play or EditorMode.Paused &&
        _gameViewPanel is { Visible: true, KeyboardFocused: true };

    public bool TryMapFramebufferPointerToViewport(
        float framebufferX,
        float framebufferY,
        out float viewportX,
        out float viewportY)
    {
        viewportX = 0f;
        viewportY = 0f;
        if (CapturePlayMode() is not (EditorMode.Play or EditorMode.Paused) ||
            _gameViewPanel is not { Visible: true } gameView ||
            !gameView.LastViewportSnapshot.TryMapFramebufferToWorld(
                new System.Numerics.Vector2(framebufferX, framebufferY),
                gameView.LastPanelOriginFramebuffer,
                gameView.LastFramebufferScale,
                out System.Numerics.Vector2 viewportPoint))
        {
            return false;
        }

        viewportX = viewportPoint.X;
        viewportY = viewportPoint.Y;
        return true;
    }

    public IUiInputSource CreateGameUiInputSource(RenderWindow window, IUiInputSource fallback)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(fallback);
        return new GameViewUiInputSource(
            fallback,
            CapturePlayMode,
            () => _gameViewPanel?.LastViewportSnapshot ?? GameViewViewportSnapshot.Empty,
            () => _gameViewPanel?.LastPointerPanelPoint ?? default,
            () => _gameViewPanel is { Visible: true, PointerHovered: true },
            () => _gameViewPanel?.LastPanelOriginFramebuffer ?? default,
            () => _gameViewPanel?.LastFramebufferScale ?? System.Numerics.Vector2.One,
            () => _gameViewPanel is { Visible: true, KeyboardFocused: true });
    }

    public bool TryGetPresentTarget(out UiPresentTarget target)
    {
        return _gameUiPresentTargetProvider.TryGetPresentTarget(out target);
    }

    public bool TryGetPendingPresentation(out GamePresentationOverride request)
    {
        if (_gameViewPanel is not null)
        {
            return _gameViewPanel.TryGetPendingPresentation(out request);
        }

        request = default;
        return false;
    }

    public bool AllowsGameUiComposition => CapturePlayMode() is EditorMode.Play or EditorMode.Paused;

    private EditorMode CapturePlayMode()
    {
        Hosting.EditorMode mode = _app.CurrentSession?.CaptureEditorPlaySession().Mode ?? Hosting.EditorMode.Edit;
        return mode == Hosting.EditorMode.Play
            ? EditorMode.Play
            : mode == Hosting.EditorMode.Paused
                ? EditorMode.Paused
                : EditorMode.Edit;
    }

    private void RegisterPanels(Engine engine, RenderWindow window, RenderPipeline pipeline)
    {
        if (_panelsRegistered)
        {
            return;
        }

        _editor.AddPanel(EditorPanelIds.MainMenu, new EditorMainMenuPanel(_app));
        _editor.AddPanel(EditorPanelIds.Preferences, _app.PreferencesWindow);
        _assetBrowserDataSource = new EditorAssetBrowserDataSource(
            _project,
            _textureThumbnailProvider,
            activeScene: _sceneModel,
            currentScenePath: () => _app.CurrentSession?.CurrentSceneRelativePath);
        EditorAssetBrowserDataSource assetBrowserDataSource = _assetBrowserDataSource;
        if (!string.IsNullOrWhiteSpace(assetBrowserDataSource.LastDiagnostic))
        {
            _app.ConsoleStore.Add(new EditorConsoleEntry(
                DateTimeOffset.UtcNow,
                EditorConsoleCategory.Asset,
                EditorConsoleSeverity.Warning,
                "asset-database",
                assetBrowserDataSource.LastDiagnostic));
        }

        _consolePanel = new EditorConsolePanel(_app);
        _editor.AddPanel(EditorPanelIds.Console, _consolePanel);
        IAudioPreviewService? audioPreview =
            engine.Context.TryGetService(out AudioSystem audioSystem) &&
            engine.Context.TryGetService(out AudioClipCache audioClips)
                ? new EditorAudioPreviewService(audioSystem, audioClips)
                : null;
        if (_sceneModel is not null && _undoStack is not null && _prefabs is not null)
        {
            PhysicsSystem? runtimePhysics = engine.Context.TryGetService(out PhysicsSystem registeredPhysics)
                ? registeredPhysics
                : null;
            RuntimeSceneHierarchyDataSource runtimeHierarchy = RuntimeSceneHierarchyDataSource.CreateDynamic(
                () => engine.CurrentScene?.ScriptScene,
                runtimePhysics);
            _runtimeHierarchy = runtimeHierarchy;
            _editor.AddPanel(EditorPanelIds.Hierarchy, new GameObjectHierarchyPanel(
                _sceneModel,
                _undoStack,
                _prefabs,
                runtimeHierarchy.Capture,
                CapturePlayMode,
                () => _authoringWorld?.Snapshot ?? default));
            _gameObjectInspectorPanel = new GameObjectInspectorPanel(
                _sceneModel,
                _undoStack,
                engine.Context.GetService<ScriptAssemblyRegistry>(),
                _app.ConsoleStore,
                assetBrowserDataSource,
                _app.InstantiatePrefab,
                _app.OpenScriptAsset,
                _app.OpenSceneAsset,
                audioPreview,
                runtimeSource: runtimeHierarchy,
                modeProvider: CapturePlayMode);
            // Console 先注册、Inspector 后注册，使共享右侧 dock 默认落在选择上下文；
            // Inspector 仍在 Scene View 前绘制，保持首帧 dock 尺寸和相机 framing 稳定。
            if (_focusInspectorOnInitialLayout)
            {
                _gameObjectInspectorPanel.RequestFocus();
            }

            _editor.AddPanel(EditorPanelIds.Inspector, _gameObjectInspectorPanel);
        }

        MaterialBrushPalettePanel? brushPanel = null;
        if (engine.Context.TryGetService(out MaterialTable materials) &&
            engine.Context.TryGetService(out ISimulationEditApi editApi))
        {
            brushPanel = new MaterialBrushPalettePanel(materials, editApi);
            brushPanel.HostInSceneView();
            _brushPanel = brushPanel;
        }

        SceneWorldTexture? sceneWorldTexture =
            engine.Context.TryGetService(out IChunkSource sceneChunks) &&
            engine.Context.TryGetService(out MaterialTable sceneMaterials) &&
            engine.Context.TryGetService(out TemperatureField sceneTemperature)
                ? new SceneWorldTexture(
                    window.Gl,
                    sceneChunks,
                    sceneMaterials,
                    sceneTemperature,
                    engine.Context.Jobs)
                : null;
        Func<string, string?>? manifestAssetResolver = null;
        if (engine.Context.TryGetService(out IGameUiManifestAssetResolver registeredManifestResolver))
        {
            manifestAssetResolver = assetId =>
                registeredManifestResolver.TryResolveManifest(assetId, out string path) ? path : null;
        }

        _sceneWebCanvasPreview = new SceneWebCanvasAuthoringPreview(
            _sceneModel ?? throw new InvalidOperationException("Scene Web Canvas 预览需要先配置 authoring scene model。"),
            _project.ContentRootPath,
            window,
            pipeline,
            manifestAssetResolver);
        _sceneViewPanel = new SceneViewPanel(
            _sceneModel ?? throw new InvalidOperationException("Scene View 需要先配置 authoring scene model。"),
            _undoStack ?? throw new InvalidOperationException("Scene View 需要先配置 authoring undo stack。"),
            brushPanel,
            sceneWorldTexture,
            () => _authoringWorld?.Snapshot ?? default,
            _sceneWebCanvasPreview);
        _undoStack.BeforeOperation = FlushPendingAuthoringEdits;
        _editor.AddPanel(EditorPanelIds.Scene, _sceneViewPanel);
        _playerSettingsPanel = new PlayerSettingsPanel(_project, () => _app.UiScale);
        GamePresentationCoordinator presentation = engine.Context.GetService<GamePresentationCoordinator>();
        _gameViewPanel = new GameViewPanel(
            () => pipeline.CurrentViewportTexture,
            () => presentation.Current,
            () => (
                _playerSettingsPanel.AppliedSettings.WindowWidth,
                _playerSettingsPanel.AppliedSettings.WindowHeight),
            pipeline.MaximumTextureSize,
            _app.Workspace,
            _project.ProjectRoot);
        _editor.AddPanel(EditorPanelIds.Game, _gameViewPanel);
        _assetBrowserPanel = new AssetBrowserPanel(
            assetBrowserDataSource,
            audioPreview: audioPreview,
            openScriptAsset: _app.OpenScriptAsset,
            openSceneAsset: _app.OpenSceneAsset,
            deleteAsset: request => assetBrowserDataSource.DeleteAsset(request, _sceneModel),
            deleteFolder: request => assetBrowserDataSource.DeleteFolder(request, _sceneModel),
            moveAsset: request => assetBrowserDataSource.MoveAsset(request, _sceneModel),
            moveFolder: request => assetBrowserDataSource.MoveFolder(request, _sceneModel),
            createAsset: assetBrowserDataSource.CreateAsset,
            importAsset: assetBrowserDataSource.ImportAsset,
            pickImportSource: static (initialPath, _) => NativeFolderPicker.TryPickFile(initialPath, out string selectedPath, out string diagnostic)
                ? new AssetBrowserImportSourcePickResult(true, selectedPath, string.Empty)
                : new AssetBrowserImportSourcePickResult(false, string.Empty, diagnostic),
            tryInstantiatePrefab: _app.InstantiatePrefab);
        _assetBrowserPanel.ViewStateChanged += _ => _app.NotifyAutomationSettingsChanged(
            "project.window.changed",
            ["editor:project:window"]);
        _editor.AddPanel(EditorPanelIds.Project, _assetBrowserPanel);
        UiManifestPanel uiManifestPanel = new(new EditorAssetManifestStore(_project));
        uiManifestPanel.Changed += _app.NotifyAutomationUiManifestChanged;
        AddHiddenPanel(EditorPanelIds.UiManifest, uiManifestPanel);
        _materialReactionPanel = TryCreateMaterialReactionPanel(engine);
        if (_materialReactionPanel is not null)
        {
            AddHiddenPanel(EditorPanelIds.Materials, _materialReactionPanel);
        }

        _projectSettingsPanel = new ProjectSettingsPanel(_project, () => _app.UiScale);
        _playerProcessManager = new EditorPlayerProcessManager();
        _buildSettingsPanel = new BuildSettingsPanel(
            _project,
            console: _app.ConsoleStore,
            prepareScene: _app.PrepareSceneForBuild,
            playerProcesses: _playerProcessManager);
        _projectSettingsPanel.SettingsApplied += () => _app.NotifyAutomationSettingsChanged(
            "settings.project.changed",
            ["editor:project", "editor:project-settings"]);
        _playerSettingsPanel.SettingsApplied += () => _app.NotifyAutomationSettingsChanged(
            "settings.player.changed",
            ["editor:project", "editor:player-settings"]);
        _buildSettingsPanel.SettingsApplied += () => _app.NotifyAutomationSettingsChanged(
            "settings.build.changed",
            ["editor:project", "editor:build-settings"]);
        _buildSettingsPanel.BuildChanged += _app.NotifyAutomationBuildChanged;
        _playerProcessManager.Changed += snapshot =>
            _app.NotifyAutomationPlayerChanged(snapshot.PlayerProcessId);
        AddHiddenPanel(EditorPanelIds.ProjectSettings, _projectSettingsPanel);
        AddHiddenPanel(EditorPanelIds.PlayerSettings, _playerSettingsPanel);
        AddHiddenPanel(EditorPanelIds.BuildSettings, _buildSettingsPanel);
        _performanceHudPanel = new PerformanceHudPanel();
        _performanceHudPanel.VSyncChanged += enabled =>
        {
            engine.Context.Counters.VSyncEnabled = enabled;
            _app.NotifyAutomationProfilerChanged();
        };
        AddHiddenPanel(EditorPanelIds.Profiler, _performanceHudPanel);
        AddHiddenPanel(EditorPanelIds.Simulation, new SimulationControlToolbar(new EditorSimulationControlAdapter(_app)));
        AddHiddenPanel(EditorPanelIds.PlayMode, new EditorModePanel(new EditorPlaySessionAdapter(_app)));
        _saveLoadService = new EditorWorldSaveLoadService(
            engine,
            Path.Combine(_project.ProjectRoot, "saves"));
        AddHiddenPanel(EditorPanelIds.SaveLoad, new SaveLoadPanel(_saveLoadService));
        if (engine.Context.TryGetService(out DebugOverlaySettings debugSettings))
        {
            AddHiddenPanel(EditorPanelIds.Overlays, new DebugOverlayPanel(debugSettings));
        }

        if (engine.Context.TryGetService(out ISimulationInspectApi inspectApi))
        {
            _simulationInspectApi = inspectApi;
            _worldInspectorPanel = new WorldInspectorPanel(inspectApi);
            AddHiddenPanel(EditorPanelIds.WorldInspector, _worldInspectorPanel);
        }

        if (brushPanel is not null)
        {
            AddHiddenPanel(EditorPanelIds.Brush, brushPanel);
        }

        if (engine.Context.TryGetService(out PhysicsSystem physics))
        {
            _physicsTuningService = new PhysicsSystemTuningService(physics);
            PhysicsTuningPanel physicsPanel = new(_physicsTuningService);
            physicsPanel.StateApplied += _ => _app.NotifyAutomationRuntimeTuningChanged(
                "runtime.physics.changed",
                "editor:runtime:physics");
            AddHiddenPanel(EditorPanelIds.Physics, physicsPanel);
        }

        if (engine.Context.TryGetService(out ParticleSystem particles))
        {
            _particleTuningService = new ParticleSystemTuningService(particles);
            ParticleTuningPanel particlePanel = new(_particleTuningService);
            particlePanel.StateApplied += _ => _app.NotifyAutomationRuntimeTuningChanged(
                "runtime.particles.changed",
                "editor:runtime:particles");
            AddHiddenPanel(EditorPanelIds.Particles, particlePanel);
        }

        _lightingTuningService = new RenderPipelineLightingTuningService(pipeline.Settings);
        LightingTuningPanel lightingPanel = new(_lightingTuningService);
        lightingPanel.StateApplied += _ => _app.NotifyAutomationRuntimeTuningChanged(
            "runtime.lighting.changed",
            "editor:runtime:lighting");
        AddHiddenPanel(EditorPanelIds.Lighting, lightingPanel);
        _panelsRegistered = true;
    }

    private void AddHiddenPanel(string panelId, IEditorPanel panel)
    {
        panel.Visible = false;
        _editor.AddPanel(panelId, panel);
    }

    private MaterialReactionEditorPanel? TryCreateMaterialReactionPanel(Engine engine)
    {
        if (!engine.Context.TryGetService(out MaterialTable materials) ||
            !engine.Context.TryGetService(out ReactionEngine reactions) ||
            !engine.Context.TryGetService(out SimulationKernel kernel) ||
            !engine.Context.TryGetService(out IChunkSource chunks) ||
            !TryResolveMaterialFallback(materials, out ushort fallbackMaterialId))
        {
            return null;
        }

        string materialsPath = Path.Combine(_project.ContentRootPath, EngineContentLoader.MaterialsFileName);
        string reactionsPath = Path.Combine(_project.ContentRootPath, EngineContentLoader.ReactionsFileName);
        if (!File.Exists(materialsPath) || !File.Exists(reactionsPath))
        {
            return null;
        }

        FileMaterialReactionContentService content = new(
            materialsPath,
            reactionsPath,
            materials,
            chunks,
            fallbackMaterialId,
            () => reactions.Reactions,
            reactions.ReloadReactions,
            kernel.ReloadMaterialHotTable,
            counters: engine.Context.Counters);
        _materialReactionContentService = content;
        MaterialReactionEditorPanel panel = new(content);
        panel.Reload();
        return panel;
    }

    private static bool TryResolveMaterialFallback(MaterialTable materials, out ushort fallbackMaterialId)
    {
        if (materials.TryGetId("empty", out fallbackMaterialId) ||
            materials.TryGetId("air", out fallbackMaterialId))
        {
            return true;
        }

        fallbackMaterialId = 0;
        return materials.Count != 0;
    }

    internal static EditorRuntimeDiagnostics BuildRuntimeDiagnostics(Engine engine)
    {
        return engine.Context.TryGetService(out EngineOverloadController overload)
            ? new EditorRuntimeDiagnostics(
                TimeScale: overload.QualityTier == EngineQualityTier.SlowMotion ? 0.5 : 1.0,
                DegradationLevel: (int)overload.QualityTier,
                DegradationName: overload.QualityTier.ToString(),
                overload.ConsecutiveOverBudgetFrames)
            : EditorRuntimeDiagnostics.FullQuality;
    }

    private sealed class EditorMainMenuPanel(EditorShellApp app) : IEditorChromePanel
    {
        private readonly EditorMainMenuBar _menu = new();

        public string Title => "Main Menu";

        public bool Visible { get; set; } = true;

        public void Draw(in EditorContext context)
        {
            _ = context;
            if (Visible)
            {
                _menu.Draw(app);
                app.DrawTransientWindows();
            }
        }
    }

    private sealed class EditorSimulationControlAdapter(EditorShellApp app) : ISimulationControlService
    {
        public SimulationControlSnapshot Capture()
        {
            Hosting.SimulationControlSnapshot snapshot = app.CurrentSession?.CaptureSimulationControl() ?? default;
            return new SimulationControlSnapshot(
                snapshot.IsPlaying,
                snapshot.SimHz,
                snapshot.FrameIndex,
                snapshot.SimTickIndex,
                snapshot.RunSimThisFrame);
        }

        public void EnterPlayMode()
        {
            app.EnterPlayMode();
        }

        public void EnterEditMode()
        {
            app.EnterEditMode();
        }

        public void StepOnce()
        {
            app.StepOnce();
        }

        public void SetSimHz(double simHz)
        {
            app.CurrentSession?.SetSimHz(simHz);
            app.NotifyAutomationSimulationChanged();
        }
    }

    private sealed class EditorPlaySessionAdapter(EditorShellApp app) : IEditorPlaySessionService
    {
        public EditorPlaySessionSnapshot Capture()
        {
            return app.CurrentSession is { } session
                ? Convert(session.CaptureEditorPlaySession())
                : new EditorPlaySessionSnapshot(
                    EditorMode.Edit,
                    EditorPlaySource.CurrentState,
                    false,
                    "没有打开工程。");
        }

        public EditorPlaySessionResult EnterPlayCurrent()
        {
            return app.CurrentSession is { } session
                ? Convert(session.EnterPlayCurrent())
                : MissingProjectResult();
        }

        public EditorPlaySessionResult EnterPlayTemporary()
        {
            return app.CurrentSession is { } session
                ? Convert(session.EnterPlayTemporary())
                : MissingProjectResult();
        }

        public EditorPlaySessionResult ExitPlay()
        {
            return app.CurrentSession is { } session
                ? Convert(session.ExitEditorPlay())
                : MissingProjectResult();
        }

        private static EditorPlaySessionResult MissingProjectResult()
        {
            EditorPlaySessionSnapshot snapshot = new(
                EditorMode.Edit,
                EditorPlaySource.CurrentState,
                false,
                "没有打开工程。");
            return new EditorPlaySessionResult(false, snapshot, snapshot.StatusMessage);
        }

        private static EditorPlaySessionResult Convert(Hosting.EditorPlaySessionResult result)
        {
            return new EditorPlaySessionResult(
                result.Succeeded,
                Convert(result.Snapshot),
                result.Message);
        }

        private static EditorPlaySessionSnapshot Convert(Hosting.EditorPlaySessionSnapshot snapshot)
        {
            return new EditorPlaySessionSnapshot(
                snapshot.Mode == Hosting.EditorMode.Play
                    ? EditorMode.Play
                    : snapshot.Mode == Hosting.EditorMode.Paused
                        ? EditorMode.Paused
                        : EditorMode.Edit,
                snapshot.Source == Hosting.EditorPlaySource.TemporarySnapshot
                    ? EditorPlaySource.TemporarySnapshot
                    : EditorPlaySource.CurrentState,
                snapshot.TemporarySnapshotActive,
                snapshot.StatusMessage);
        }
    }

    private sealed class CompositeDisposable(params IDisposable?[] disposables) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            for (int i = disposables.Length - 1; i >= 0; i--)
            {
                disposables[i]?.Dispose();
            }

            _disposed = true;
        }
    }
}

/// <summary>Project/Player Settings 提交绑定窗口探针快照。</summary>
internal readonly record struct ScriptedSettingsPanelPresentationSnapshot(
    string Target,
    bool Visible,
    System.Numerics.Vector2 WindowPosition,
    System.Numerics.Vector2 WindowSize,
    bool HasPendingChanges,
    string ValidationMessage);
