using PixelEngine.Gui;
using PixelEngine.Hosting;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.UI;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorShellHostExtension : IEditorHostExtension, IEditorInputCaptureSource, IGameUiInputSourceFactory, IUiPresentTargetProvider
{
    private readonly EditorProject _project;
    private readonly EditorShellApp _app;
    private readonly EditorApp _editor;
    private readonly GameViewUiPresentTargetProvider _gameUiPresentTargetProvider;
    private EditorSceneModel? _sceneModel;
    private EditorUndoStack? _undoStack;
    private EditorPrefabAssetStore? _prefabs;
    private ProjectSettingsPanel? _projectSettingsPanel;
    private PlayerSettingsPanel? _playerSettingsPanel;
    private BuildSettingsPanel? _buildSettingsPanel;
    private SceneViewPanel? _sceneViewPanel;
    private GameViewPanel? _gameViewPanel;
    private bool _panelsRegistered;

    public EditorShellHostExtension(EditorProject project, EditorShellApp app)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _editor = new EditorApp(
            new PixelEngine.Editor.HexaImGuiBackend(),
            new EditorAppOptions
            {
                LayoutPath = EditorShellWindow.DefaultLayoutPath,
                EnableMultiViewport = false,
            });
        _gameUiPresentTargetProvider = new GameViewUiPresentTargetProvider(
            CapturePlayMode,
            () => _gameViewPanel?.LastViewportSnapshot ?? GameViewViewportSnapshot.Empty,
            () => _gameViewPanel?.LastPanelOriginFramebuffer ?? default,
            () => _gameViewPanel?.LastFramebufferScale ?? System.Numerics.Vector2.One,
            () => _gameViewPanel is { Visible: true });
    }

    public int PanelCount => _editor.PanelCount;

    public long BridgeFrameCount => Bridge?.FrameIndex ?? 0;

    public EditorRenderBridge? Bridge { get; private set; }

    public bool TryShowPanel(string title)
    {
        return _editor.TryShowPanel(title);
    }

    public void ResetLayout()
    {
        _editor.ResetDockLayout();
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

    public ScriptedBuildProbeSnapshot CaptureScriptedBuildProbe()
    {
        return _buildSettingsPanel?.CaptureScriptedBuildProbe() ?? new ScriptedBuildProbeSnapshot();
    }

    public void CancelScriptedBuildProbe()
    {
        _buildSettingsPanel?.CancelScriptedBuildProbe();
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

    public void ConfigureAuthoring(EditorSceneModel sceneModel, EditorUndoStack undoStack, EditorPrefabAssetStore prefabs)
    {
        if (_panelsRegistered)
        {
            throw new InvalidOperationException("Authoring 服务必须在 Editor 面板注册前配置。");
        }

        _sceneModel = sceneModel ?? throw new ArgumentNullException(nameof(sceneModel));
        _undoStack = undoStack ?? throw new ArgumentNullException(nameof(undoStack));
        _prefabs = prefabs ?? throw new ArgumentNullException(nameof(prefabs));
    }

    public IDisposable? Attach(Engine engine, RenderWindow window, RenderPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(pipeline);
        engine.Context.RegisterService<IEditorInputCaptureSource>(this);
        RegisterPanels(engine, pipeline);
        EditorWindowInputConnector input = new(window, _editor.Input);
        Bridge = EditorRenderBridge.AttachIfEnabled(
            pipeline,
            _editor,
            engine.Context.Counters,
            engine.Context.Profiler,
            () => BuildRuntimeDiagnostics(engine),
            engine.Context.TryGetService(out IScriptRuntime scriptRuntime) ? scriptRuntime : null);
        return new CompositeDisposable(input, Bridge, _editor);
    }

    public bool TryGetInputCapture(out EditorHostInputCapture capture)
    {
        PixelEngine.Editor.EditorInputSnapshot editorCapture = _editor.Input.Capture;
        PixelEngine.Editor.EditorMode mode = CapturePlayMode();
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
            viewportHasInputFocus: _gameViewPanel is { Visible: true, InputFocused: true });
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
            () => _gameViewPanel is { Visible: true, InputFocused: true });
    }

    public bool TryGetPresentTarget(out UiPresentTarget target)
    {
        return _gameUiPresentTargetProvider.TryGetPresentTarget(out target);
    }

    private PixelEngine.Editor.EditorMode CapturePlayMode()
    {
        return _app.CurrentSession?.CaptureEditorPlaySession().Mode == PixelEngine.Hosting.EditorMode.Play
            ? PixelEngine.Editor.EditorMode.Play
            : PixelEngine.Editor.EditorMode.Edit;
    }

    private void RegisterPanels(Engine engine, RenderPipeline pipeline)
    {
        if (_panelsRegistered)
        {
            return;
        }

        _editor.AddPanel(new EditorMainMenuPanel(_app));
        if (_sceneModel is not null && _undoStack is not null && _prefabs is not null)
        {
            _editor.AddPanel(new GameObjectHierarchyPanel(_sceneModel, _undoStack, _prefabs));
            _editor.AddPanel(new GameObjectInspectorPanel(
                _sceneModel,
                _undoStack,
                engine.Context.GetService<ScriptAssemblyRegistry>(),
                _app.ConsoleStore));
        }

        MaterialBrushPalettePanel? brushPanel = null;
        if (engine.Context.TryGetService(out MaterialTable materials) &&
            engine.Context.TryGetService(out ISimulationEditApi editApi))
        {
            brushPanel = new MaterialBrushPalettePanel(materials, editApi);
        }

        _sceneViewPanel = new SceneViewPanel(
            () => pipeline.CurrentViewportTexture,
            engine.Context.GetService<ScriptCameraApi>(),
            _sceneModel ?? throw new InvalidOperationException("Scene View 需要先配置 authoring scene model。"),
            _undoStack ?? throw new InvalidOperationException("Scene View 需要先配置 authoring undo stack。"),
            brushPanel);
        _editor.AddPanel(_sceneViewPanel);
        _gameViewPanel = new GameViewPanel(() => pipeline.CurrentViewportTexture);
        _editor.AddPanel(_gameViewPanel);
        EditorAssetBrowserDataSource assetBrowserDataSource = new(_project);
        _editor.AddPanel(new AssetBrowserPanel(
            assetBrowserDataSource,
            instantiatePrefab: _app.InstantiatePrefab,
            openScriptAsset: _app.OpenScriptAsset,
            deleteAsset: request => assetBrowserDataSource.DeleteAsset(request, _sceneModel),
            moveAsset: request => assetBrowserDataSource.MoveAsset(request, _sceneModel)));
        MaterialReactionEditorPanel? materialReactionPanel = TryCreateMaterialReactionPanel(engine);
        if (materialReactionPanel is not null)
        {
            _editor.AddPanel(materialReactionPanel);
        }

        _projectSettingsPanel = new ProjectSettingsPanel(_project);
        _playerSettingsPanel = new PlayerSettingsPanel(_project);
        _buildSettingsPanel = new BuildSettingsPanel(_project, console: _app.ConsoleStore);
        _editor.AddPanel(_projectSettingsPanel);
        _editor.AddPanel(_playerSettingsPanel);
        _editor.AddPanel(_buildSettingsPanel);
        _editor.AddPanel(new EditorConsolePanel(_app));
        _editor.AddPanel(new PerformanceHudPanel());
        _editor.AddPanel(new SimulationControlToolbar(new EditorSimulationControlAdapter(_app)));
        _editor.AddPanel(new EditorModePanel(new EditorPlaySessionAdapter(_app)));
        _editor.AddPanel(new SaveLoadPanel(new EditorWorldSaveLoadService(
            engine,
            Path.Combine(_project.ProjectRoot, "saves"))));
        if (engine.Context.TryGetService(out DebugOverlaySettings debugSettings))
        {
            _editor.AddPanel(new DebugOverlayPanel(debugSettings));
        }

        if (engine.Context.TryGetService(out ISimulationInspectApi inspectApi))
        {
            _editor.AddPanel(new WorldInspectorPanel(inspectApi));
        }

        if (brushPanel is not null)
        {
            _editor.AddPanel(brushPanel);
        }

        if (engine.Context.TryGetService(out PhysicsSystem physics))
        {
            _editor.AddPanel(new PhysicsTuningPanel(new PhysicsSystemTuningService(physics)));
        }

        if (engine.Context.TryGetService(out ParticleSystem particles))
        {
            _editor.AddPanel(new ParticleTuningPanel(new ParticleSystemTuningService(particles)));
        }

        _editor.AddPanel(new LightingTuningPanel(new RenderPipelineLightingTuningService(pipeline.Settings)));
        _panelsRegistered = true;
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
            reactions.ReloadReactions,
            kernel.ReloadMaterialHotTable,
            counters: engine.Context.Counters);
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

    private static EditorRuntimeDiagnostics BuildRuntimeDiagnostics(Engine engine)
    {
        return engine.Context.TryGetService(out EngineOverloadController overload)
            ? new EditorRuntimeDiagnostics(
                TimeScale: overload.QualityTier == EngineQualityTier.SlowMotion ? 0.5 : 1.0,
                DegradationLevel: (int)overload.QualityTier,
                DegradationName: overload.QualityTier.ToString(),
                overload.ConsecutiveOverBudgetFrames)
            : EditorRuntimeDiagnostics.FullQuality;
    }

    private sealed class EditorMainMenuPanel(EditorShellApp app) : IEditorPanel
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
            }
        }
    }

    private sealed class EditorSimulationControlAdapter(EditorShellApp app) : PixelEngine.Editor.ISimulationControlService
    {
        public PixelEngine.Editor.SimulationControlSnapshot Capture()
        {
            PixelEngine.Hosting.SimulationControlSnapshot snapshot = app.CurrentSession?.CaptureSimulationControl() ?? default;
            return new PixelEngine.Editor.SimulationControlSnapshot(
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
        }
    }

    private sealed class EditorPlaySessionAdapter(EditorShellApp app) : PixelEngine.Editor.IEditorPlaySessionService
    {
        public PixelEngine.Editor.EditorPlaySessionSnapshot Capture()
        {
            return app.CurrentSession is { } session
                ? Convert(session.CaptureEditorPlaySession())
                : new PixelEngine.Editor.EditorPlaySessionSnapshot(
                    PixelEngine.Editor.EditorMode.Edit,
                    PixelEngine.Editor.EditorPlaySource.CurrentState,
                    false,
                    "没有打开工程。");
        }

        public PixelEngine.Editor.EditorPlaySessionResult EnterPlayCurrent()
        {
            return app.CurrentSession is { } session
                ? Convert(session.EnterPlayCurrent())
                : MissingProjectResult();
        }

        public PixelEngine.Editor.EditorPlaySessionResult EnterPlayTemporary()
        {
            return app.CurrentSession is { } session
                ? Convert(session.EnterPlayTemporary())
                : MissingProjectResult();
        }

        public PixelEngine.Editor.EditorPlaySessionResult ExitPlay()
        {
            return app.CurrentSession is { } session
                ? Convert(session.ExitEditorPlay())
                : MissingProjectResult();
        }

        private static PixelEngine.Editor.EditorPlaySessionResult MissingProjectResult()
        {
            PixelEngine.Editor.EditorPlaySessionSnapshot snapshot = new(
                PixelEngine.Editor.EditorMode.Edit,
                PixelEngine.Editor.EditorPlaySource.CurrentState,
                false,
                "没有打开工程。");
            return new PixelEngine.Editor.EditorPlaySessionResult(false, snapshot, snapshot.StatusMessage);
        }

        private static PixelEngine.Editor.EditorPlaySessionResult Convert(PixelEngine.Hosting.EditorPlaySessionResult result)
        {
            return new PixelEngine.Editor.EditorPlaySessionResult(
                result.Succeeded,
                Convert(result.Snapshot),
                result.Message);
        }

        private static PixelEngine.Editor.EditorPlaySessionSnapshot Convert(PixelEngine.Hosting.EditorPlaySessionSnapshot snapshot)
        {
            return new PixelEngine.Editor.EditorPlaySessionSnapshot(
                snapshot.Mode == PixelEngine.Hosting.EditorMode.Play
                    ? PixelEngine.Editor.EditorMode.Play
                    : PixelEngine.Editor.EditorMode.Edit,
                snapshot.Source == PixelEngine.Hosting.EditorPlaySource.TemporarySnapshot
                    ? PixelEngine.Editor.EditorPlaySource.TemporarySnapshot
                    : PixelEngine.Editor.EditorPlaySource.CurrentState,
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
