using PixelEngine.Audio;
using PixelEngine.Gui;
using PixelEngine.Hosting;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.UI;

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
    private readonly EditorProject _project;
    private readonly EditorShellApp _app;
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
    private SceneViewPanel? _sceneViewPanel;
    private GameViewPanel? _gameViewPanel;
    private GameObjectInspectorPanel? _gameObjectInspectorPanel;
    private EditorConsolePanel? _consolePanel;
    private AssetBrowserPanel? _assetBrowserPanel;
    private RuntimeSceneHierarchyDataSource? _runtimeHierarchy;
    private EditorAssetBrowserDataSource? _assetBrowserDataSource;
    private EditorTextureThumbnailProvider? _textureThumbnailProvider;
    private EditorMode _lastPreparedMode = EditorMode.Edit;
    private bool _panelsRegistered;

    public EditorShellHostExtension(EditorProject project, EditorShellApp app, RenderWindow window)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _app = app ?? throw new ArgumentNullException(nameof(app));
        ArgumentNullException.ThrowIfNull(window);
        EditorFontStackPaths fonts = EditorFontAssets.Resolve();
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
        _editor.SetUiScale(_app.UiScale);
        _editor.SetLayoutPersistence(_app.Preferences.Current.SaveLayoutOnExit);
    }

    public void FlushPendingAuthoringEdits()
    {
        _gameObjectInspectorPanel?.CommitPendingEdits();
        _ = _sceneViewPanel?.CommitGizmoTransform();
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
        _undoStack.CanModifyScene = () => CapturePlayMode() == EditorMode.Edit;
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

        _editor.AddPanel(new EditorMainMenuPanel(_app));
        _editor.AddPanel(_app.PreferencesWindow);
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
        _editor.AddPanel(_consolePanel);
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
            _editor.AddPanel(new GameObjectHierarchyPanel(
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

            _editor.AddPanel(_gameObjectInspectorPanel);
        }

        MaterialBrushPalettePanel? brushPanel = null;
        if (engine.Context.TryGetService(out MaterialTable materials) &&
            engine.Context.TryGetService(out ISimulationEditApi editApi))
        {
            brushPanel = new MaterialBrushPalettePanel(materials, editApi);
            brushPanel.HostInSceneView();
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
        _editor.AddPanel(_sceneViewPanel);
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
        _editor.AddPanel(_gameViewPanel);
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
        _editor.AddPanel(_assetBrowserPanel);
        AddHiddenPanel(new UiManifestPanel(new EditorAssetManifestStore(_project)));
        MaterialReactionEditorPanel? materialReactionPanel = TryCreateMaterialReactionPanel(engine);
        if (materialReactionPanel is not null)
        {
            AddHiddenPanel(materialReactionPanel);
        }

        _projectSettingsPanel = new ProjectSettingsPanel(_project, () => _app.UiScale);
        _buildSettingsPanel = new BuildSettingsPanel(_project, console: _app.ConsoleStore);
        AddHiddenPanel(_projectSettingsPanel);
        AddHiddenPanel(_playerSettingsPanel);
        AddHiddenPanel(_buildSettingsPanel);
        AddHiddenPanel(new PerformanceHudPanel());
        AddHiddenPanel(new SimulationControlToolbar(new EditorSimulationControlAdapter(_app)));
        AddHiddenPanel(new EditorModePanel(new EditorPlaySessionAdapter(_app)));
        AddHiddenPanel(new SaveLoadPanel(new EditorWorldSaveLoadService(
            engine,
            Path.Combine(_project.ProjectRoot, "saves"))));
        if (engine.Context.TryGetService(out DebugOverlaySettings debugSettings))
        {
            AddHiddenPanel(new DebugOverlayPanel(debugSettings));
        }

        if (engine.Context.TryGetService(out ISimulationInspectApi inspectApi))
        {
            AddHiddenPanel(new WorldInspectorPanel(inspectApi));
        }

        if (brushPanel is not null)
        {
            AddHiddenPanel(brushPanel);
        }

        if (engine.Context.TryGetService(out PhysicsSystem physics))
        {
            AddHiddenPanel(new PhysicsTuningPanel(new PhysicsSystemTuningService(physics)));
        }

        if (engine.Context.TryGetService(out ParticleSystem particles))
        {
            AddHiddenPanel(new ParticleTuningPanel(new ParticleSystemTuningService(particles)));
        }

        AddHiddenPanel(new LightingTuningPanel(new RenderPipelineLightingTuningService(pipeline.Settings)));
        _panelsRegistered = true;
    }

    private void AddHiddenPanel(IEditorPanel panel)
    {
        panel.Visible = false;
        _editor.AddPanel(panel);
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
