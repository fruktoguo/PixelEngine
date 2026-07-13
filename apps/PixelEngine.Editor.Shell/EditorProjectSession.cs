using PixelEngine.Hosting;
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
    private readonly EngineWorldSnapshotStore _snapshotStore;
    private readonly EngineEditorPlaySessionService _playSession;
    private readonly EngineSimulationControlService _simulationControl;
    private readonly AuthoringWorldPreviewRuntime _authoringWorld;
    private readonly EditorScriptAssetOpenService _scriptAssetOpenService;
    private readonly EditorCodeWorkspaceOpenService _codeWorkspaceOpenService;
    private int _runtimeProjectionVersion;
    private bool _disposed;

    private EditorProjectSession(
        EditorProject project,
        Engine engine,
        EditorShellHostExtension editorHost,
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
        Engine = engine;
        _editorHost = editorHost;
        SceneModel = sceneModel;
        UndoStack = undoStack;
        RuntimeProjection = runtimeProjection;
        _authoringWorld = authoringWorld ?? throw new ArgumentNullException(nameof(authoringWorld));
        Prefabs = prefabs;
        _scriptAssetOpenService = scriptAssetOpenService ?? throw new ArgumentNullException(nameof(scriptAssetOpenService));
        _codeWorkspaceOpenService = codeWorkspaceOpenService ?? throw new ArgumentNullException(nameof(codeWorkspaceOpenService));
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
        Engine engine = new EngineBuilder()
            .WithProject(project.ToEngineProject(sceneRelativePath))
            .ApplyRuntimeDefaults(playerSettings, applyStartupScene: false)
            .UseGuiRuntime()
            .EnableGameUi()
            .AddEditorHostExtension(editorHost)
            .Build();
        try
        {
            AttachContentAndWorld(engine);
            AttachProjectAudio(engine);
            _ = engine.AttachPhysics();
            EditorSceneModel sceneModel = LoadSceneModel(project, sceneRelativePath);
            EditorUndoStack undoStack = new();
            EditorAssetManifestStore assets = new(project);
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

            return new EditorProjectSession(project, engine, editorHost, sceneModel, undoStack, projection, authoringWorld, prefabs, scriptAssetOpenService, codeWorkspaceOpenService, sceneRelativePath);
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
        RefreshEditProjectionIfNeeded();
        return _playSession.EnterPlayCurrent();
    }

    public Hosting.EditorPlaySessionResult EnterPlayTemporary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RefreshEditProjectionIfNeeded();
        return _playSession.EnterPlayTemporary();
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
        return result;
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
        _editorHost.RequestGameViewFocus();
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

        UndoStack.Execute(SceneModel, new InstantiatePrefabCommand(Prefabs, logicalPath, SceneModel.SelectedStableId));
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
        string normalized = Project.ResolveSceneRelativePath(sceneRelativePath);
        Prefabs.RefreshPrefabInstances(SceneModel);
        Engine.SaveSceneDocument(SceneModel.ToDocument(), Project.ResolveSceneFullPath(normalized));
        Project.UpsertScene(normalized, makeStartScene);
        CurrentSceneRelativePath = normalized;
        SceneModel.Name = Project.ResolveDisplaySceneName(normalized);
        SceneModel.MarkSaved();
    }

    public string NewSceneAuto()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string relative = AllocateNewScenePath();
        EditorSceneModel empty = EditorSceneModel.Empty(Path.GetFileNameWithoutExtension(relative) ?? "scene");
        string normalized = Project.ResolveSceneRelativePath(relative);
        Engine.SaveSceneDocument(empty.ToDocument(), Project.ResolveSceneFullPath(normalized));
        Project.RegisterScene(normalized);
        SceneModel.ReplaceWith(empty, markDirty: false);
        UndoStack.Clear();
        CurrentSceneRelativePath = normalized;
        return CurrentSceneRelativePath;
    }

    /// <summary>
    /// 从 content 加载场景文档并替换当前编辑场景图。
    /// </summary>
    public void OpenScene(string sceneRelativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string normalized = Project.ResolveSceneRelativePath(sceneRelativePath);
        EditorSceneModel loaded = LoadSceneModel(Project, normalized);
        SceneModel.ReplaceWith(loaded, markDirty: false);
        UndoStack.Clear();
        CurrentSceneRelativePath = normalized;
        Project.RegisterScene(normalized);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

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

        if (engine.AttachCurrentSceneWorld(DefaultParticleCapacity) is null)
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
        catch (Exception exception) when (exception is
            System.Text.Json.JsonException or
            NotSupportedException or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"无法打开场景 '{sceneRelativePath}'：{exception.Message}",
                exception);
        }
    }

    private static EditorSceneRuntimeProjection ProjectAuthoringScene(Engine engine, EditorSceneModel sceneModel)
    {
        EditorSceneRuntimeProjection projection = EditorSceneRuntimeProjection.Build(
            sceneModel,
            engine.Context.GetService<ScriptAssemblyRegistry>());
        engine.AttachScriptScene(projection.Scene);
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
