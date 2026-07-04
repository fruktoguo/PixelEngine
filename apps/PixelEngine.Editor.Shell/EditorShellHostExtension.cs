using PixelEngine.Gui;
using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorShellHostExtension : IEditorHostExtension
{
    private readonly EditorProject _project;
    private readonly EditorShellApp _app;
    private readonly EditorApp _editor;
    private EditorSceneModel? _sceneModel;
    private EditorUndoStack? _undoStack;
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
    }

    public int PanelCount => _editor.PanelCount;

    public long BridgeFrameCount => Bridge?.FrameIndex ?? 0;

    public EditorRenderBridge? Bridge { get; private set; }

    public void ConfigureAuthoring(EditorSceneModel sceneModel, EditorUndoStack undoStack)
    {
        if (_panelsRegistered)
        {
            throw new InvalidOperationException("Authoring 服务必须在 Editor 面板注册前配置。");
        }

        _sceneModel = sceneModel ?? throw new ArgumentNullException(nameof(sceneModel));
        _undoStack = undoStack ?? throw new ArgumentNullException(nameof(undoStack));
    }

    public IDisposable? Attach(Engine engine, RenderWindow window, RenderPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(pipeline);
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

    private void RegisterPanels(Engine engine, RenderPipeline pipeline)
    {
        if (_panelsRegistered)
        {
            return;
        }

        _editor.AddPanel(new EditorMainMenuPanel(_app));
        if (_sceneModel is not null && _undoStack is not null)
        {
            _editor.AddPanel(new GameObjectHierarchyPanel(_sceneModel, _undoStack));
            _editor.AddPanel(new GameObjectInspectorPanel(
                _sceneModel,
                _undoStack,
                engine.Context.GetService<ScriptAssemblyRegistry>()));
        }

        _editor.AddPanel(new ViewportPanel(() => pipeline.CurrentViewportTexture));
        _editor.AddPanel(new AssetBrowserPanel(new FileSystemAssetBrowserDataSource(_project.ContentRootPath)));
        _editor.AddPanel(new PerformanceHudPanel());
        _editor.AddPanel(new SimulationControlToolbar(new EditorSimulationControlAdapter(_app)));
        if (engine.Context.TryGetService(out DebugOverlaySettings debugSettings))
        {
            _editor.AddPanel(new DebugOverlayPanel(debugSettings));
        }

        if (engine.Context.TryGetService(out ISimulationInspectApi inspectApi))
        {
            _editor.AddPanel(new WorldInspectorPanel(inspectApi));
        }

        if (engine.Context.TryGetService(out MaterialTable materials) &&
            engine.Context.TryGetService(out ISimulationEditApi editApi))
        {
            _editor.AddPanel(new MaterialBrushPalettePanel(materials, editApi));
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
