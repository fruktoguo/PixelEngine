using PixelEngine.Hosting;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.Simulation;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 当前打开项目的运行时会话，持有 Engine、场景与撤销栈。
/// </summary>
internal sealed class EditorProjectSession : IDisposable
{
    private const int DefaultEditorWorldWidth = 720;
    private const int DefaultEditorWorldHeight = 480;
    private const int DefaultParticleCapacity = 32768;
    private readonly EditorShellHostExtension _editorHost;
    private readonly EditorShellApp _app;
    private readonly IEditorConsoleSink _console;
    private readonly EngineWorldSnapshotStore _snapshotStore;
    private readonly EngineEditorPlaySessionService _playSession;
    private readonly EngineSimulationControlService _simulationControl;
    private readonly AuthoringWorldPreviewRuntime _authoringWorld;
    private readonly EditorScriptAssetOpenService _scriptAssetOpenService;
    private readonly EditorCodeWorkspaceOpenService _codeWorkspaceOpenService;
    private int _runtimeProjectionVersion;
    private bool _authoringProjectionFailureActive;
    private bool _disposed;

    private EditorProjectSession(
        EditorProject project,
        EditorShellApp app,
        Engine engine,
        EditorShellHostExtension editorHost,
        IEditorConsoleSink console,
        EditorSceneModel sceneModel,
        EditorUndoStack undoStack,
        EditorSceneRuntimeProjection runtimeProjection,
        AuthoringWorldPreviewRuntime authoringWorld,
        EditorPrefabAssetStore prefabs,
        EditorScriptAssetOpenService scriptAssetOpenService,
        EditorCodeWorkspaceOpenService codeWorkspaceOpenService,
        string currentSceneRelativePath)
    {
        Project = project;
        _app = app ?? throw new ArgumentNullException(nameof(app));
        Engine = engine;
        _editorHost = editorHost;
        _console = console ?? throw new ArgumentNullException(nameof(console));
        SceneModel = sceneModel;
        UndoStack = undoStack;
        RuntimeProjection = runtimeProjection;
        _authoringWorld = authoringWorld ?? throw new ArgumentNullException(nameof(authoringWorld));
        Prefabs = prefabs;
        _scriptAssetOpenService = scriptAssetOpenService ?? throw new ArgumentNullException(nameof(scriptAssetOpenService));
        _codeWorkspaceOpenService = codeWorkspaceOpenService ?? throw new ArgumentNullException(nameof(codeWorkspaceOpenService));
        AutomationActiveContentRoot = project.ContentRoot;
        AutomationActiveScriptSourceDir = project.ScriptSourceDir;
        CurrentSceneRelativePath = currentSceneRelativePath;
        _runtimeProjectionVersion = sceneModel.Version;
        _snapshotStore = new EngineWorldSnapshotStore(engine);
        _playSession = new EngineEditorPlaySessionService(engine, _snapshotStore);
        _simulationControl = new EngineSimulationControlService(engine);
    }

    public EditorProject Project { get; }

    public Engine Engine { get; }

    public EditorSceneModel SceneModel { get; }

    public EditorUndoStack UndoStack { get; }

    public EditorSceneRuntimeProjection RuntimeProjection { get; private set; }

    public EditorPrefabAssetStore Prefabs { get; }

    public string CurrentSceneRelativePath { get; private set; }

    public string CurrentSceneDisplayName => Project.ResolveDisplaySceneName(CurrentSceneRelativePath);

    public int PanelCount => _editorHost.PanelCount;

    public long EditorBridgeFrameCount => _editorHost.BridgeFrameCount;

    internal string? AutomationPlaySessionId { get; private set; }

    internal string AutomationActiveContentRoot { get; }

    internal string AutomationActiveScriptSourceDir { get; }

    internal EditorAssetBrowserDataSource AutomationAssetDatabase =>
        _editorHost.RequireAutomationAssetDatabase();

    internal bool TrySetAutomationProjectAssetSelection(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TrySetAutomationProjectAssetSelection(path);
    }

    internal bool TrySetAutomationProjectFolderSelection(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TrySetAutomationProjectFolderSelection(path);
    }

    internal AssetBrowserViewState CaptureAutomationProjectWindowViewState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationProjectWindowViewState();
    }

    internal string CaptureAutomationProjectWindowActiveFolderPath()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationProjectWindowActiveFolderPath();
    }

    internal bool ApplyAutomationProjectWindowViewState(
        in AssetBrowserViewState state,
        bool notifyChanged)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.ApplyAutomationProjectWindowViewState(state, notifyChanged);
    }

    internal void ReloadAutomationAssetBrowserSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.ReloadAutomationAssetBrowserSnapshot();
    }

    internal void ClearAutomationProjectSelection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.ClearAutomationProjectSelection();
    }

    internal bool TryPreviewAutomationAudio(string path, out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryPreviewAutomationAudio(path, out diagnostic);
    }

    internal string CaptureAutomationSaveRoot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationSaveRoot();
    }

    internal bool TryGetAutomationMaterialEditor(
        out MaterialReactionEditorPanel panel,
        out FileMaterialReactionContentService contentService)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryGetAutomationMaterialEditor(out panel, out contentService);
    }

    internal bool TryGetAutomationWorldInspector(out WorldInspectorPanel panel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryGetAutomationWorldInspector(out panel);
    }

    internal void ApplyAutomationWorldInspectorState(
        bool followSelection,
        int worldX,
        int worldY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.ApplyAutomationWorldInspectorState(followSelection, worldX, worldY);
    }

    internal ProjectSettingsDto CaptureAutomationProjectSettings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationProjectSettings();
    }

    internal EditorProjectSettingsAutomationState CaptureAutomationProjectSettingsState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationProjectSettingsState();
    }

    internal EditorProjectSettingsAutomationState CreateAutomationProjectSettingsState(
        EditorProjectSettingsAutomationState source,
        ProjectSettingsDto settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CreateAutomationProjectSettingsState(source, settings);
    }

    internal void RestoreAutomationProjectSettingsState(EditorProjectSettingsAutomationState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.RestoreAutomationProjectSettingsState(state);
    }

    internal bool TryApplyAutomationProjectSettings(ProjectSettingsDto settings, out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryApplyAutomationProjectSettings(settings, out diagnostic);
    }

    internal PlayerSettingsDto CaptureAutomationPlayerSettings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationPlayerSettings();
    }

    internal PlayerSettingsPanelAutomationSnapshot CaptureAutomationPlayerSettingsState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationPlayerSettingsState();
    }

    internal PlayerSettingsPanelAutomationSnapshot CreateAutomationPlayerSettingsState(
        PlayerSettingsDto settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CreateAutomationPlayerSettingsState(settings);
    }

    internal void RestoreAutomationPlayerSettingsState(PlayerSettingsPanelAutomationSnapshot state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.RestoreAutomationPlayerSettingsState(state);
    }

    internal bool TryApplyAutomationPlayerSettings(PlayerSettingsDto settings, out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryApplyAutomationPlayerSettings(settings, out diagnostic);
    }

    internal BuildProfileDto CaptureAutomationBuildSettings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationBuildSettings();
    }

    internal BuildSettingsPanelAutomationSnapshot CaptureAutomationBuildSettingsState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationBuildSettingsState();
    }

    internal BuildSettingsPanelUiSnapshot CaptureAutomationBuildPanelState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationBuildPanelState();
    }

    internal void SetAutomationBuildLogAutoScroll(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.SetAutomationBuildLogAutoScroll(enabled);
    }

    internal BuildSettingsPanelAutomationSnapshot CreateAutomationBuildSettingsState(
        BuildProfileDto settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CreateAutomationBuildSettingsState(settings);
    }

    internal void RestoreAutomationBuildSettingsState(BuildSettingsPanelAutomationSnapshot state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.RestoreAutomationBuildSettingsState(state);
    }

    internal bool TryApplyAutomationBuildSettings(BuildProfileDto settings, out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryApplyAutomationBuildSettings(settings, out diagnostic);
    }

    internal PerformanceHudSample CaptureAutomationProfilerSample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IRenderPresentationControl? presentation = Engine.Context.TryGetService(
            out IRenderPresentationControl registeredPresentation)
                ? registeredPresentation
                : null;
        EditorPerformanceSnapshot snapshot = EditorPerformanceSnapshot.Create(
            Engine.Context.Counters,
            Engine.Context.Profiler,
            EditorShellHostExtension.BuildRuntimeDiagnostics(Engine),
            presentation);
        return PerformanceHudPanel.BuildSample(snapshot);
    }

    internal PerformanceHudHistorySnapshot CaptureAutomationProfilerHistory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationProfilerHistory();
    }

    internal RuntimeSettingsSnapshot CaptureAutomationRuntimeSettings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Engine.Context.TryGetService(out EngineScriptRuntimeControlApi runtimeControl)
            ? runtimeControl.CaptureSettings()
            : throw new InvalidOperationException("Engine runtime control service 尚未注册。");
    }

    internal RuntimeControlResult SetAutomationVSyncEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Engine.Context.TryGetService(out EngineScriptRuntimeControlApi runtimeControl)
            ? runtimeControl.SetVSyncEnabled(enabled)
            : new RuntimeControlResult(false, "Engine runtime control service 尚未注册。");
    }

    internal void ReportAutomationCleanupFailure(string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _console.AddProjectError("automation-undo-cleanup", diagnostic);
    }

    /// <summary>捕获 Game View 实际提交和显示的 presentation，同步性由探针快照 fail-closed 表示。</summary>
    public ScriptedGameViewPresentationSnapshot CaptureScriptedGameViewPresentation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureScriptedGameViewPresentation();
    }

    internal GameViewUiInputDiagnostics CapturePhysicalUiInputDiagnostics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CapturePhysicalUiInputDiagnostics();
    }

    internal long CaptureTotalDrainedGameUiEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Engine.Context.TryGetService(out GameUiPhaseDriver driver)
            ? driver.TotalDrainedEventCount
            : 0;
    }

    internal GameUiCanvasInputDiagnostics CaptureGameUiCanvasInputDiagnostics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Engine.Context.TryGetService(out GameUiCanvasRegistry registry)
            ? registry.CaptureInputDiagnostics()
            : default;
    }

    internal UI.UiInputCapture CaptureGameUiInputCapture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Engine.Context.TryGetService(out UI.UiInputRouter router)
            ? router.Capture
            : UI.UiInputCapture.None;
    }

    internal EditorGameViewAutomationState CaptureAutomationGameViewState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationGameViewState();
    }

    internal bool TryApplyAutomationGameViewState(
        EditorGameViewAutomationState state,
        out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryApplyAutomationGameViewState(state, out diagnostic);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryApplyAutomationRuntimeTransform(
            handle,
            x,
            y,
            rotationRadians,
            scaleX,
            scaleY,
            out diagnostic);
    }

    internal bool TryApplyAutomationRuntimeField(
        string handle,
        int componentIndex,
        string fieldName,
        object? value,
        out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryApplyAutomationRuntimeField(
            handle,
            componentIndex,
            fieldName,
            value,
            out diagnostic);
    }

    internal SceneHierarchySnapshot CaptureAutomationRuntimeHierarchy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationRuntimeHierarchy();
    }

    internal bool TryGetAutomationRuntimeBody(
        int bodyKey,
        out PixelEngine.Physics.RigidBodySnapshot body)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryGetAutomationRuntimeBody(bodyKey, out body);
    }

    internal bool TryInspectAutomationCell(
        int worldX,
        int worldY,
        out SimulationCellInspection inspection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryInspectAutomationCell(worldX, worldY, out inspection);
    }

    internal PhysicsTuningState CaptureAutomationPhysicsTuning()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationPhysicsTuning();
    }

    internal void ApplyAutomationPhysicsTuning(PhysicsTuningState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.ApplyAutomationPhysicsTuning(state);
    }

    internal ParticleTuningState CaptureAutomationParticleTuning()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationParticleTuning();
    }

    internal void ApplyAutomationParticleTuning(ParticleTuningState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.ApplyAutomationParticleTuning(state);
    }

    internal LightingTuningState CaptureAutomationLightingTuning()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationLightingTuning();
    }

    internal void ApplyAutomationLightingTuning(LightingTuningState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.ApplyAutomationLightingTuning(state);
    }

    internal bool TryBeginAutomationSceneCapture(
        out EditorAutomationFrameCapture capture,
        out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryBeginAutomationSceneCapture(out capture, out diagnostic);
    }

    internal bool TryBeginAutomationGameCapture(
        out EditorAutomationFrameCapture capture,
        out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryBeginAutomationGameCapture(out capture, out diagnostic);
    }

    /// <summary>选择包含指定 Behaviour 的 Play Mode 实体，供真实窗口 Inspector 探针使用。</summary>
    public bool TrySelectRuntimeInspectorEntity(string behaviourTypeSuffix, out string entityHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TrySelectRuntimeInspectorEntity(behaviourTypeSuffix, out entityHandle);
    }

    /// <summary>捕获最后一次实际完成绘制的 Play Mode Inspector 快照。</summary>
    public ScriptedRuntimeInspectorProbeSnapshot CaptureScriptedRuntimeInspectorProbe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureScriptedRuntimeInspectorProbe();
    }

    internal void FlushPendingAuthoringEdits()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.FlushPendingAuthoringEdits();
    }

    internal EditorAutomationTransactionState CaptureAutomationTransactionState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new EditorAutomationTransactionState(
            this,
            SceneModel.SelectedStableId,
            SceneModel.IsDirty,
            _editorHost.CaptureAutomationSelection());
    }

    internal void RestoreAutomationTransactionState(EditorAutomationTransactionState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(state);
        int? selectedStableId = state.SceneSelectedStableId;
        SceneModel.Select(selectedStableId is { } stableId && SceneModel.TryGet(stableId, out _)
            ? stableId
            : null);
        SceneModel.RestoreDirtyState(state.SceneWasDirty);
        _editorHost.RestoreAutomationSelection(state.Selection);
    }

    internal void SetAutomationGameObjectSelection(int? stableId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (stableId is { } value)
        {
            _ = SceneModel.Get(value);
        }

        SceneModel.Select(stableId);
        _editorHost.SetAutomationGameObjectSelection(stableId);
    }

    internal EditorAutomationSelectionSnapshot CaptureAutomationSelection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationSelection();
    }

    internal void SetAutomationRuntimeSelection(string? entityHandle, int? bodyId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SceneModel.Select(null);
        _editorHost.SetAutomationRuntimeSelection(entityHandle, bodyId);
    }

    public static EditorProjectSession Open(EditorProject project, RenderWindow window, EditorShellApp app)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(app);
        EditorShellHostExtension editorHost = new(project, app, window);
        string sceneRelativePath = project.ResolveSceneRelativePath(app.SceneOverridePath);
        PlayerSettingsDto playerSettings = new PlayerSettingsStore(project).LoadRecoverable(
            out string playerSettingsDiagnostic);
        if (!string.IsNullOrWhiteSpace(playerSettingsDiagnostic))
        {
            app.ConsoleStore.AddProjectError("player-settings", playerSettingsDiagnostic);
        }
        // 按 PlayerSettings 构造 Engine，并挂载 Editor 扩展与内容包
        EngineBuilder engineBuilder = new EngineBuilder()
            .WithProject(project.ToEngineProject(sceneRelativePath))
            .ApplyRuntimeDefaults(playerSettings, applyStartupScene: false)
            .WithGuiLayoutPath(app.RuntimeGuiLayoutPath)
            .UseGuiRuntime()
            .EnableGameUi()
            .AddEditorHostExtension(editorHost);
        if (app.AutomationScheduler is { } automationScheduler)
        {
            _ = engineBuilder.AddPhaseDriver(new EditorAutomationPhaseDriver(automationScheduler));
        }

        Engine engine = engineBuilder.Build();
        try
        {
            AttachContentAndWorld(engine);
            AttachProjectAudio(engine);
            _ = engine.AttachPhysics();
            EditorSceneModel sceneModel = LoadSceneModel(project, sceneRelativePath);
            EditorUndoStack undoStack = app.SharedUndoStack;
            undoStack.Clear();
            app.ConfigureAutomationUndoStack(undoStack);
            EditorAssetManifestStore assets = new(project);
            engine.Context.RegisterService<IGameUiManifestAssetResolver>(
                new EditorGameUiManifestAssetResolver(assets, project.ContentRootPath));
            EditorPrefabAssetStore prefabs = new(project.ContentRootPath, assets);
            EditorScriptAssetOpenService scriptAssetOpenService = new(
                project,
                () => app.Preferences.Current.ExternalScriptEditor);
            EditorCodeWorkspaceOpenService codeWorkspaceOpenService = new(
                project,
                () => app.Preferences.Current.ExternalScriptEditor);
            engine.Context.RegisterService<IScriptHotReloadDiagnosticSink>(new EditorConsoleScriptHotReloadDiagnosticSink(app.ConsoleStore));
            RegisterInitialProjectScriptAssembly(project, engine, app.ConsoleStore);
            EditorSceneRuntimeProjection projection = ProjectAuthoringScene(engine, sceneModel);
            AuthoringWorldPreviewRuntime authoringWorld = new(
                engine.Context.GetService<IChunkSource>(),
                engine.Context.GetService<IMaterialQuery>(),
                engine.Context.GetService<ISimulationEditApi>());
            _ = authoringWorld.Refresh(projection.Scene, projection.StableIdToEntityId);
            editorHost.ConfigureAuthoring(sceneModel, undoStack, prefabs, authoringWorld);
            _ = engine.AttachScriptingFromServices(
                hotReload: new ScriptHotReloadRuntimeOptions($"{project.Name}.EditorScripts", project.ScriptSourcePath));
            engine.EnterEditMode();
            _ = engine.AttachWindowRuntime(window);
            if (engine.Context.TryGetService(out GameUiBackendSelection uiBackendSelection))
            {
                app.ConsoleStore.AddUiBackendSelection(uiBackendSelection);
            }

            return new EditorProjectSession(project, app, engine, editorHost, app.ConsoleStore, sceneModel, undoStack, projection, authoringWorld, prefabs, scriptAssetOpenService, codeWorkspaceOpenService, sceneRelativePath);
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 推进一帧编辑器运行：先同步场景投影，再驱动 Engine tick。
    /// </summary>
    public void RunOneTick(double deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.PrepareFrame();
        // 编辑模式下场景图变更后重建 ScriptScene 投影
        RefreshEditProjectionIfNeeded();
        _ = Engine.RunOneTick(deltaSeconds);
    }

    /// <summary>
    /// 进入游玩模式（临时 Play 会话，退出后恢复编辑快照）。
    /// </summary>
    public void EnterPlayMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Engine.Mode == EngineExecutionMode.Paused)
        {
            _ = _playSession.ResumePlay();
        }
        else if (Engine.Mode != EngineExecutionMode.Play)
        {
            _ = EnterPlayTemporary();
        }

        _editorHost.RequestGameViewFocus();
    }

    public void EnterEditMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ExitEditorPlay();
    }

    public void TogglePauseMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Engine.Mode == EngineExecutionMode.Play)
        {
            _ = _playSession.PausePlay();
        }
        else if (Engine.Mode == EngineExecutionMode.Paused)
        {
            _ = _playSession.ResumePlay();
        }

        _editorHost.RequestGameViewFocus();
    }

    public Hosting.EditorPlaySessionSnapshot CaptureEditorPlaySession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _playSession.Capture();
    }

    public Hosting.EditorPlaySessionResult EnterPlayCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.FlushPendingAuthoringEdits();
        RefreshEditProjectionIfNeeded();
        Hosting.EditorPlaySessionResult result = !TryValidateAuthoringScene(SceneModel, out string diagnostic)
            ? RejectPlayForInvalidAuthoringScene(diagnostic)
            : _playSession.EnterPlayCurrent();
        CaptureAutomationPlayIdentity(result);
        return result;
    }

    public Hosting.EditorPlaySessionResult EnterPlayTemporary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.FlushPendingAuthoringEdits();
        RefreshEditProjectionIfNeeded();
        Hosting.EditorPlaySessionResult result = !TryValidateAuthoringScene(SceneModel, out string diagnostic)
            ? RejectPlayForInvalidAuthoringScene(diagnostic)
            : _playSession.EnterPlayTemporary();
        CaptureAutomationPlayIdentity(result);
        return result;
    }

    public Hosting.EditorPlaySessionResult ExitEditorPlay()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Hosting.EditorPlaySessionSnapshot beforeExit = _playSession.Capture();
        Hosting.EditorPlaySessionResult result = _playSession.ExitPlay();
        // 临时 Play 的字段快照只负责运行时可持久字段与 world 回滚；脚本可能还持有私有
        // session 状态、动态实体与 ISystem（例如 LevelDirector 的实体构建门闩）。退出后必须
        // 从 authoring SceneModel 重建完整 projection，才能保证下一次 Play 使用全新 Behaviour
        // 与 system 集合，而不是带着上一次会话的私有门闩进入空运行态。
        bool rebuildTemporaryProjection = result.Succeeded &&
            beforeExit.Source == Hosting.EditorPlaySource.TemporarySnapshot &&
            beforeExit.TemporarySnapshotActive;
        RefreshEditProjectionIfNeeded(force: rebuildTemporaryProjection);
        _editorHost.InvalidateAuthoringWorld();
        if (result.Succeeded && result.Snapshot.Mode == Hosting.EditorMode.Edit)
        {
            AutomationPlaySessionId = null;
        }

        return result;
    }

    internal Hosting.EditorPlaySessionResult PauseEditorPlay()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _playSession.PausePlay();
    }

    internal Hosting.EditorPlaySessionResult ResumeEditorPlay()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _playSession.ResumePlay();
    }

    internal Hosting.EditorPlaySessionResult StepEditorPlay()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Hosting.EditorPlaySessionSnapshot before = _playSession.Capture();
        if (before.Mode == Hosting.EditorMode.Play)
        {
            Hosting.EditorPlaySessionResult paused = _playSession.PausePlay();
            if (!paused.Succeeded)
            {
                return paused;
            }
        }

        if (_playSession.Capture().Mode != Hosting.EditorMode.Paused)
        {
            Hosting.EditorPlaySessionSnapshot snapshot = _playSession.Capture();
            return new Hosting.EditorPlaySessionResult(
                false,
                snapshot,
                "当前没有可单步的 Play session。");
        }

        _ = Engine.StepOnce();
        _editorHost.InvalidateAuthoringWorld();
        _editorHost.RequestGameViewFocus();
        Hosting.EditorPlaySessionSnapshot after = _playSession.Capture();
        return new Hosting.EditorPlaySessionResult(true, after, "Play session 已执行一个 step。");
    }

    public void StepOnce()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Engine.Mode == EngineExecutionMode.Play)
        {
            _ = _playSession.PausePlay();
        }

        if (Engine.Mode is not (EngineExecutionMode.Edit or EngineExecutionMode.Paused))
        {
            return;
        }

        _ = Engine.StepOnce();
        _editorHost.InvalidateAuthoringWorld();
        _editorHost.RequestGameViewFocus();
    }

    private void CaptureAutomationPlayIdentity(Hosting.EditorPlaySessionResult result)
    {
        if (result.Succeeded && result.Snapshot.Mode is Hosting.EditorMode.Play or Hosting.EditorMode.Paused)
        {
            AutomationPlaySessionId ??= Guid.NewGuid().ToString("N");
        }
    }

    public void CreateGameObject()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UndoStack.Execute(SceneModel, new CreateGameObjectCommand("GameObject", SceneModel.SelectedStableId));
    }

    public void CreateRootGameObject()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UndoStack.Execute(SceneModel, new CreateGameObjectCommand("GameObject"));
    }

    public void CreateChildGameObject()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UndoStack.Execute(SceneModel, new CreateGameObjectCommand("GameObject", SceneModel.SelectedStableId));
    }

    public void DeleteSelectedGameObject()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SceneModel.SelectedStableId is { } stableId)
        {
            UndoStack.Execute(SceneModel, new DeleteGameObjectCommand(stableId));
        }
    }

    public void DuplicateSelectedGameObject()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SceneModel.SelectedStableId is { } stableId)
        {
            UndoStack.Execute(SceneModel, new DuplicateGameObjectCommand(stableId));
        }
    }

    public void RenameSelectedGameObject()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SceneModel.SelectedStableId is { } stableId)
        {
            EditorGameObject gameObject = SceneModel.Get(stableId);
            UndoStack.Execute(SceneModel, new RenameGameObjectCommand(stableId, $"{gameObject.Name} Renamed"));
        }
    }

    public void AddComponentToSelected(string typeName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        if (SceneModel.SelectedStableId is { } stableId)
        {
            UndoStack.Execute(SceneModel, new AddComponentCommand(stableId, new EditorComponentModel(typeName)));
        }
    }

    public string[] GetBehaviourTypeNames()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ScriptAssemblyRegistry scripts = Engine.Context.GetService<ScriptAssemblyRegistry>();
        List<string> result = [];
        for (int i = 0; i < scripts.Assemblies.Count; i++)
        {
            foreach (Type type in scripts.Assemblies[i].GetTypes())
            {
                if (type is { IsAbstract: false } &&
                    typeof(Behaviour).IsAssignableFrom(type) &&
                    type.GetConstructor(Type.EmptyTypes) is not null)
                {
                    result.Add(type.FullName ?? type.Name);
                }
            }
        }

        result.Sort(StringComparer.Ordinal);
        return [.. result];
    }

    public void CreatePrefabFromSelection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SceneModel.SelectedStableId is not { } stableId)
        {
            return;
        }

        string assetPath = Prefabs.AllocatePrefabPath(SceneModel.Get(stableId).Name);
        UndoStack.Execute(SceneModel, new CreatePrefabAssetCommand(Prefabs, stableId, assetPath));
    }

    public void InstantiatePrefab(string assetPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string logicalPath = assetPath;
        if (EditorRootedBrowserPath.TryParse(assetPath, out EditorAssetPath rootedPath, out _))
        {
            if (rootedPath.Root != EditorAssetRootKind.Content)
            {
                throw new InvalidOperationException("Prefab 必须来自 Content logical root。");
            }

            logicalPath = rootedPath.RelativePath;
        }

        // Project / Inspector 的显式 Instantiate 与 Hierarchy drop 语义分离：资产操作始终创建根节点，
        // 不能依赖旧 Scene 选择或面板绘制顺序偷偷改变父节点。
        UndoStack.Execute(SceneModel, new InstantiatePrefabCommand(Prefabs, logicalPath, parentId: null));
    }

    public EditorScriptAssetOpenResult OpenScriptAsset(string assetPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _scriptAssetOpenService.OpenScriptAsset(assetPath);
    }

    public EditorScriptAssetOpenResult OpenScriptAsset(string assetPath, int line, int column = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _scriptAssetOpenService.OpenScriptAsset(assetPath, line, column);
    }

    public EditorCodeWorkspaceOpenResult OpenCodeProject()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _codeWorkspaceOpenService.OpenCodeProject();
    }

    internal EditorCodeWorkspaceAutomationWorkspace CaptureAutomationCodeWorkspacePreparation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new EditorCodeWorkspaceAutomationWorkspace(this, _codeWorkspaceOpenService);
    }

    public bool ShowProjectSettings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryShowPanel(ProjectSettingsPanel.PanelTitle);
    }

    public bool ShowPlayerSettings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryShowPanel(PlayerSettingsPanel.PanelTitle);
    }

    public bool ShowBuildSettings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryShowPanel(BuildSettingsPanel.PanelTitle);
    }

    public bool TryStartBuild(bool runAfterBuild, out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryStartBuild(runAfterBuild, out diagnostic);
    }

    internal EditorBuildPreflightWorkspace CaptureAutomationBuildPreflightWorkspace()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationBuildPreflightWorkspace();
    }

    internal bool TryStartAutomationBuild(
        string buildId,
        bool launchOnSuccess,
        out EditorBuildExecutionSnapshot snapshot,
        out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryStartAutomationBuild(
            buildId,
            launchOnSuccess,
            out snapshot,
            out diagnostic);
    }

    internal EditorBuildExecutionSnapshot CaptureAutomationBuild(string buildId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationBuild(buildId);
    }

    internal EditorBuildExecutionSnapshot[] CaptureAutomationBuilds()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationBuilds();
    }

    internal EditorBuildExecutionLogSnapshot CaptureAutomationBuildLog(string buildId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationBuildLog(buildId);
    }

    internal Task<BuildResult> CaptureAutomationBuildCompletion(string buildId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationBuildCompletion(buildId);
    }

    internal bool RequestAutomationBuildCancellation(
        string buildId,
        bool notifyChanged,
        out EditorBuildExecutionSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.RequestAutomationBuildCancellation(
            buildId,
            notifyChanged,
            out snapshot);
    }

    internal EditorPlayerProcessSnapshot LaunchAutomationPlayer(
        string buildId,
        bool notifyChanged,
        string? playerProcessId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.LaunchAutomationPlayer(buildId, notifyChanged, playerProcessId);
    }

    internal EditorPlayerProcessSnapshot CaptureAutomationPlayer(string playerProcessId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationPlayer(playerProcessId);
    }

    internal EditorPlayerProcessSnapshot[] CaptureAutomationPlayers()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationPlayers();
    }

    internal EditorPlayerProcessWaitWorkspace CaptureAutomationPlayerWaitWorkspace(
        string playerProcessId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationPlayerWaitWorkspace(playerProcessId);
    }

    internal bool RequestAutomationPlayerTermination(
        string playerProcessId,
        bool entireProcessTree,
        out EditorPlayerProcessSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.RequestAutomationPlayerTermination(
            playerProcessId,
            entireProcessTree,
            out snapshot);
    }

    public bool ShowPanel(string title)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryShowPanel(title);
    }

    public bool TryGetPanelVisibility(string title, out bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryGetPanelVisibility(title, out visible);
    }

    public bool TrySetPanelVisibility(string title, bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TrySetPanelVisibility(title, visible);
    }

    internal EditorPanelSnapshot[] CaptureAutomationPanels()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationPanels();
    }

    internal bool TrySetAutomationPanel(string panelId, bool visible, bool focus)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TrySetAutomationPanel(panelId, visible, focus);
    }

    internal bool TryRestoreAutomationPanels(IReadOnlyList<EditorPanelSnapshot> snapshots)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryRestoreAutomationPanels(snapshots);
    }

    internal string CaptureAutomationDockLayout()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureAutomationDockLayout();
    }

    internal void ApplyAutomationDockLayout(string layout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.ApplyAutomationDockLayout(layout);
    }

    internal bool TrySetAutomationPanelDock(
        string panelId,
        string? targetPanelId,
        EditorDockWindowRequest request,
        out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TrySetAutomationPanelDock(panelId, targetPanelId, request, out diagnostic);
    }

    internal bool TryCaptureAutomationSceneTool(out AutomationSceneToolSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryCaptureAutomationSceneTool(out snapshot);
    }

    internal bool TrySetAutomationSceneTool(
        AutomationSceneToolSetRequest request,
        out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TrySetAutomationSceneTool(request, out diagnostic);
    }

    internal bool TryFrameAutomationScene(
        AutomationSceneFrameTarget target,
        out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryFrameAutomationScene(target, out diagnostic);
    }

    internal bool TryApplyAutomationBrush(
        int worldX,
        int worldY,
        out AutomationBrushApplyResult result,
        out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryApplyAutomationBrush(worldX, worldY, out result, out diagnostic);
    }

    internal bool TryApplyAutomationBrushStroke(
        IReadOnlyList<AutomationWorldPoint> points,
        out AutomationBrushStrokeResult result,
        out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryApplyAutomationBrushStroke(points, out result, out diagnostic);
    }

    public void ResetLayout()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _editorHost.ResetLayout();
    }

    public bool TryStartScriptedBuildProbe(string outputDirectory, bool runAfterBuild, out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryStartScriptedBuildProbe(outputDirectory, runAfterBuild, out diagnostic);
    }

    public ScriptedBuildProbeSnapshot CaptureScriptedBuildProbe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureScriptedBuildProbe();
    }

    public void CancelScriptedBuildProbe()
    {
        if (_disposed)
        {
            return;
        }

        _editorHost.CancelScriptedBuildProbe();
    }

    public ScriptedBuildSettingsProbeSnapshot ApplyScriptedBuildSettingsProbe(string outputDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.ApplyScriptedBuildSettingsProbe(outputDirectory);
    }

    public ScriptedBuildSettingsProbeSnapshot CaptureScriptedBuildSettingsProbe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureScriptedBuildSettingsProbe();
    }

    public ScriptedBuildSettingsFooterProbeSnapshot CaptureScriptedBuildSettingsFooterProbe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureScriptedBuildSettingsFooterProbe();
    }

    public bool RequestScriptedBuildSettingsActionsOverflow()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.RequestScriptedBuildSettingsActionsOverflow();
    }

    public ScriptedProjectSettingsProbeSnapshot ApplyScriptedProjectSettingsProbe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.ApplyScriptedProjectSettingsProbe();
    }

    public ScriptedProjectSettingsProbeSnapshot CaptureScriptedProjectSettingsProbe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureScriptedProjectSettingsProbe();
    }

    public ScriptedPlayerSettingsProbeSnapshot ApplyScriptedPlayerSettingsProbe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.ApplyScriptedPlayerSettingsProbe();
    }

    public ScriptedPlayerSettingsProbeSnapshot CaptureScriptedPlayerSettingsProbe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureScriptedPlayerSettingsProbe();
    }

    public ScriptedSettingsPanelPresentationSnapshot CaptureScriptedSettingsPanelPresentation(string target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.CaptureScriptedSettingsPanelPresentation(target);
    }

    public bool Undo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return UndoStack.Undo(SceneModel);
    }

    public bool Redo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return UndoStack.Redo(SceneModel);
    }

    public void SetSimHz(double simHz)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _simulationControl.SetSimHz(simHz);
    }

    public Hosting.SimulationControlSnapshot CaptureSimulationControl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _simulationControl.Capture();
    }

    public string SceneFilePath => Project.ResolveSceneFullPath(CurrentSceneRelativePath);

    /// <summary>
    /// 将当前场景图序列化到磁盘并清除脏标记。
    /// </summary>
    public void SaveScene()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSceneTransitionAllowed("保存场景");
        _editorHost.FlushPendingAuthoringEdits();
        Prefabs.RefreshPrefabInstances(SceneModel);
        Engine.SaveSceneDocument(SceneModel.ToDocument(), SceneFilePath);
        SceneModel.MarkSaved();
    }

    public string SaveSceneAsAuto()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string next = AllocateCopyScenePath(CurrentSceneRelativePath);
        SaveSceneAs(next, makeStartScene: false);
        return next;
    }

    public void SaveSceneAs(string sceneRelativePath, bool makeStartScene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSceneTransitionAllowed("另存场景");
        _editorHost.FlushPendingAuthoringEdits();
        string normalized = Project.ResolveSceneRelativePath(sceneRelativePath);
        Prefabs.RefreshPrefabInstances(SceneModel);
        Engine.SaveSceneDocument(SceneModel.ToDocument(), Project.ResolveSceneFullPath(normalized));
        Project.UpsertScene(normalized, makeStartScene);
        CurrentSceneRelativePath = normalized;
        SceneModel.Name = Project.ResolveDisplaySceneName(normalized);
        SceneModel.MarkSaved();
        _app.NotifyAutomationProjectChanged();
    }

    public string NewSceneAuto()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSceneTransitionAllowed("新建场景");
        _editorHost.FlushPendingAuthoringEdits();
        string relative = AllocateNewScenePath();
        EditorSceneModel empty = EditorSceneModel.Empty(Path.GetFileNameWithoutExtension(relative) ?? "scene");
        string normalized = Project.ResolveSceneRelativePath(relative);
        Engine.SaveSceneDocument(empty.ToDocument(), Project.ResolveSceneFullPath(normalized));
        Project.RegisterScene(normalized);
        SceneModel.ReplaceWith(empty, markDirty: false);
        UndoStack.Clear();
        CurrentSceneRelativePath = normalized;
        _app.NotifyAutomationProjectChanged();
        return CurrentSceneRelativePath;
    }

    /// <summary>
    /// 从 content 加载场景文档并替换当前编辑场景图。
    /// </summary>
    public void OpenScene(string sceneRelativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSceneTransitionAllowed("打开场景");
        _editorHost.FlushPendingAuthoringEdits();
        string normalized = Project.ResolveSceneRelativePath(sceneRelativePath);
        EditorSceneModel loaded = LoadSceneModel(Project, normalized);
        SceneModel.ReplaceWith(loaded, markDirty: false);
        UndoStack.Clear();
        CurrentSceneRelativePath = normalized;
        Project.RegisterScene(normalized);
        _app.NotifyAutomationProjectChanged();
    }

    private void EnsureSceneTransitionAllowed(string operation)
    {
        if (_app.IsAutomationTransactionActive)
        {
            throw new InvalidOperationException(
                $"{operation}已被拒绝：外部 automation transaction 正持有 Editor 写租约。");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _editorHost.FlushPendingAuthoringEdits();
        UndoStack.Clear();
        UndoStack.BeforeOperation = null;
        UndoStack.CanModifyScene = null;
        _snapshotStore.Dispose();
        Engine.Dispose();
        _disposed = true;
    }

    private static void AttachContentAndWorld(Engine engine)
    {
        if (engine.HasContentPackage())
        {
            _ = engine.LoadContentPackage();
        }
        else
        {
            RegisterFallbackEditorMaterials(engine);
        }

        if (engine.AttachCurrentSceneWorld(DefaultParticleCapacity) is null && !engine.IsSimulationWorldAttached)
        {
            _ = engine.AttachResidentSimulationWorld(
                DefaultEditorWorldWidth,
                DefaultEditorWorldHeight,
                DefaultParticleCapacity);
        }
    }

    internal static void AttachProjectAudio(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        string audioRoot = Path.Combine(engine.Context.Options.ContentRoot, "audio");
        if (Directory.Exists(audioRoot))
        {
            _ = engine.AttachAudioFromContentAsync().AsTask().GetAwaiter().GetResult();
            return;
        }

        engine.Context.RegisterService<IAudioApi>(EngineServiceRole.AudioService, NullAudioApi.Instance);
    }

    internal static EditorSceneModel LoadSceneModel(EditorProject project, string sceneRelativePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneRelativePath);
        string scenePath = project.ResolveSceneFullPath(sceneRelativePath);
        try
        {
            return EditorSceneModel.FromDocument(EngineSceneDocumentLoader.LoadDocument(scenePath));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileNotFoundException(
                $"无法打开场景 '{sceneRelativePath}'：场景文件不存在。",
                scenePath,
                exception);
        }
        catch (Exception exception) when (
            exception is System.Text.Json.JsonException or NotSupportedException or IOException or UnauthorizedAccessException ||
            IsRecoverableAuthoringSceneValidationFailure(exception))
        {
            throw new InvalidOperationException(
                $"无法打开场景 '{sceneRelativePath}'：{exception.Message}",
                exception);
        }
    }

    private static EditorSceneRuntimeProjection ProjectAuthoringScene(Engine engine, EditorSceneModel sceneModel)
    {
        EngineSceneDocument document = sceneModel.ToDocument();
        _ = EngineSceneCanvasResolver.Resolve(document);
        EditorSceneRuntimeProjection projection = EditorSceneRuntimeProjection.Build(
            sceneModel,
            engine.Context.GetService<ScriptAssemblyRegistry>());
        engine.AttachScriptScene(projection.Scene);
        engine.ApplySceneCanvasDocument(document);
        engine.Context.RegisterService(sceneModel);
        engine.Context.RegisterService(projection);
        return projection;
    }

    private static void RegisterInitialProjectScriptAssembly(EditorProject project, Engine engine, IEditorConsoleSink console)
    {
        RuntimeScriptAssemblyCompileResult result = RuntimeScriptAssemblyCompiler.CompileAndLoadFromDirectory(
            $"{project.Name}.EditorScripts",
            project.ScriptSourcePath);
        if (!result.HasSources)
        {
            return;
        }

        if (!result.Success || result.Assembly is null)
        {
            console.AddScriptDiagnostics(
                "project-scripts",
                result.Error ?? "项目脚本编译失败。",
                result.Diagnostics,
                success: false);
            throw new InvalidOperationException(result.Error ?? "项目脚本编译失败。");
        }

        engine.RegisterScriptAssembly(result.Assembly);
        console.AddScriptDiagnostics(
            "project-scripts",
            $"项目脚本已加载：{project.ScriptSourcePath}",
            result.Diagnostics,
            success: true);
    }

    // 场景图版本变化时重建 ScriptScene 投影，使 Inspector/Game View 与文档一致
    private void RefreshEditProjectionIfNeeded(bool force = false)
    {
        if (Engine.Mode is EngineExecutionMode.Play or EngineExecutionMode.Paused ||
            (!force && SceneModel.Version == _runtimeProjectionVersion))
        {
            return;
        }

        Prefabs.RefreshPrefabInstances(SceneModel);
        int targetVersion = SceneModel.Version;
        if (!TryValidateAuthoringScene(SceneModel, out string diagnostic))
        {
            if (!_authoringProjectionFailureActive)
            {
                _console.AddProjectError(
                    "scene-authoring",
                    $"场景草稿校验失败，继续保留上一份有效预览：{diagnostic}");
            }

            _authoringProjectionFailureActive = true;
            _runtimeProjectionVersion = targetVersion;
            return;
        }

        _authoringProjectionFailureActive = false;
        RuntimeProjection = ProjectAuthoringScene(Engine, SceneModel);
        AuthoringWorldRefreshResult refresh = _authoringWorld.Refresh(
            RuntimeProjection.Scene,
            RuntimeProjection.StableIdToEntityId);
        if (refresh is AuthoringWorldRefreshResult.Rebuilt or AuthoringWorldRefreshResult.Cleared)
        {
            _editorHost.InvalidateAuthoringWorld();
        }

        _runtimeProjectionVersion = SceneModel.Version;
    }

    internal static bool TryValidateAuthoringScene(EditorSceneModel scene, out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(scene);
        try
        {
            _ = EngineSceneCanvasResolver.Resolve(scene.ToDocument());
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsRecoverableAuthoringSceneValidationFailure(exception))
        {
            diagnostic = exception.Message;
            return false;
        }
    }

    internal static bool IsRecoverableAuthoringSceneValidationFailure(Exception exception)
    {
        return exception is InvalidOperationException or
            InvalidDataException or
            ArgumentOutOfRangeException;
    }

    private Hosting.EditorPlaySessionResult RejectPlayForInvalidAuthoringScene(string diagnostic)
    {
        string message = $"无法进入 Play：请先修复场景草稿。{diagnostic}";
        return new Hosting.EditorPlaySessionResult(false, _playSession.Capture(), message);
    }

    private string AllocateCopyScenePath(string sourceRelativePath)
    {
        string directory = Path.GetDirectoryName(sourceRelativePath)?.Replace('\\', '/') ?? string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(sourceRelativePath);
        string extension = Path.GetExtension(sourceRelativePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".scene";
        }

        for (int i = 1; i < 1000; i++)
        {
            string suffix = i == 1 ? "-copy" : $"-copy-{i}";
            string relative = string.IsNullOrEmpty(directory)
                ? $"{fileName}{suffix}{extension}"
                : $"{directory}/{fileName}{suffix}{extension}";
            if (!File.Exists(Project.ResolveSceneFullPath(relative)))
            {
                return relative;
            }
        }

        throw new InvalidOperationException("无法为 Save Scene As 分配可用文件名。");
    }

    private string AllocateNewScenePath()
    {
        string sceneRoot = Path.Combine(Project.ContentRootPath, "scenes");
        _ = Directory.CreateDirectory(sceneRoot);
        for (int i = 1; i < 10_000; i++)
        {
            string fileName = i == 1 ? "new-scene.scene" : $"new-scene-{i}.scene";
            string relative = $"scenes/{fileName}";
            if (!File.Exists(Project.ResolveSceneFullPath(relative)))
            {
                return relative;
            }
        }

        throw new InvalidOperationException("无法分配新的场景文件名。");
    }

    private static void RegisterFallbackEditorMaterials(Engine engine)
    {
        MaterialDef[] definitions =
        [
            new()
            {
                Id = 0,
                Name = "empty",
                Type = CellType.Empty,
                HeatCapacity = 1f,
                TextureId = -1,
                BaseColorBGRA = 0x00000000,
                DisplayName = "Empty",
                LegendCategory = MaterialLegendCategory.Special,
                LegendVisible = false,
            },
            new()
            {
                Id = 1,
                Name = "stone",
                Type = CellType.Solid,
                Density = 200,
                Durability = 40,
                Hardness = 64,
                Integrity = 160,
                HeatCapacity = 1f,
                TextureId = -1,
                BaseColorBGRA = 0xFF808080,
                DisplayName = "Stone",
                LegendCategory = MaterialLegendCategory.Terrain,
                LegendVisible = true,
            },
        ];
        MaterialTable materials = new(definitions);
        ShellMaterialQuery query = new(materials);
        engine.Context.RegisterService(materials);
        engine.Context.RegisterService<IMaterialQuery>(EngineServiceRole.MaterialRegistry, query);
        engine.Context.RegisterService(query);
    }

    internal sealed class ShellMaterialQuery(MaterialTable materials) : IMaterialQuery
    {
        private readonly MaterialTable _materials = materials ?? throw new ArgumentNullException(nameof(materials));

        public MaterialId Resolve(string name)
        {
            return TryResolve(name, out MaterialId id) ? id : MaterialId.Invalid;
        }

        public bool TryResolve(string name, out MaterialId id)
        {
            if (_materials.TryGetId(name, out ushort raw))
            {
                id = new MaterialId(raw);
                return true;
            }

            id = MaterialId.Invalid;
            return false;
        }

        public MaterialInfo GetInfo(MaterialId id)
        {
            ref readonly MaterialDef material = ref _materials.Get(id.Value);
            MaterialProperty flags = material.PropertyFlags;
            bool emissive = (flags & MaterialProperty.Emissive) != 0 || material.RenderStyle == MaterialRenderStyle.Emissive;
            bool destructible = id.Value != 0 &&
                material.Type is CellType.Solid or CellType.Powder &&
                (flags & MaterialProperty.Indestructible) == 0;
            bool blocksCharacter = material.Type is CellType.Solid or CellType.Powder;
            return new MaterialInfo(
                id,
                material.Name,
                material.Density,
                material.Type == CellType.Solid,
                string.IsNullOrWhiteSpace(material.DisplayName) ? material.Name : material.DisplayName,
                LegendCategoryName(material.LegendCategory),
                material.LegendVisible,
                material.BaseColorBGRA,
                material.MineYield,
                material.Type,
                material.LegendCategory,
                emissive,
                material.Hardness != 0 ? material.Hardness : material.Durability,
                material.MaxIntegrity,
                destructible,
                material.Dispersion,
                blocksCharacter,
                material.Flammability,
                material.AutoIgnitionTemp,
                material.FireHp,
                material.TemperatureOfFire,
                material.GeneratesSmoke,
                material.HeatConduct,
                material.HeatCapacity,
                material.RenderStyle,
                flags);
        }

        private static string LegendCategoryName(MaterialLegendCategory category)
        {
            return category switch
            {
                MaterialLegendCategory.Terrain => "Terrain",
                MaterialLegendCategory.Liquid => "Liquid",
                MaterialLegendCategory.Gas => "Gas",
                MaterialLegendCategory.Destructible => "Destructible",
                MaterialLegendCategory.Hazard => "Hazard",
                MaterialLegendCategory.Resource => "Resource",
                MaterialLegendCategory.Special => "Special",
                _ => nameof(MaterialLegendCategory.Special),
            };
        }
    }
}
