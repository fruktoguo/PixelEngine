using PixelEngine.Hosting;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.Simulation;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorProjectSession : IDisposable
{
    private const int DefaultEditorWorldWidth = 720;
    private const int DefaultEditorWorldHeight = 480;
    private const int DefaultParticleCapacity = 32768;
    private readonly EditorShellHostExtension _editorHost;
    private readonly EngineWorldSnapshotStore _snapshotStore;
    private readonly EngineEditorPlaySessionService _playSession;
    private readonly EngineSimulationControlService _simulationControl;
    private int _runtimeProjectionVersion;
    private bool _disposed;

    private EditorProjectSession(
        EditorProject project,
        Engine engine,
        EditorShellHostExtension editorHost,
        EditorSceneModel sceneModel,
        EditorUndoStack undoStack,
        EditorSceneRuntimeProjection runtimeProjection,
        EditorPrefabAssetStore prefabs,
        string currentSceneRelativePath)
    {
        Project = project;
        Engine = engine;
        _editorHost = editorHost;
        SceneModel = sceneModel;
        UndoStack = undoStack;
        RuntimeProjection = runtimeProjection;
        Prefabs = prefabs;
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
        EditorShellHostExtension editorHost = new(project, app);
        string sceneRelativePath = project.ResolveSceneRelativePath(app.SceneOverridePath);
        PlayerSettingsDto playerSettings = new PlayerSettingsStore(project).Load();
        Engine engine = new EngineBuilder()
            .WithProject(project.ToEngineProject(sceneRelativePath))
            .ApplyRuntimeDefaults(playerSettings)
            .UseGuiRuntime(false)
            .AddEditorHostExtension(editorHost)
            .Build();
        try
        {
            AttachContentAndWorld(engine);
            _ = engine.AttachPhysics();
            EditorSceneModel sceneModel = LoadSceneModel(project, sceneRelativePath);
            EditorUndoStack undoStack = new();
            EditorPrefabAssetStore prefabs = new(project.ContentRootPath);
            EditorSceneRuntimeProjection projection = ProjectAuthoringScene(engine, sceneModel);
            editorHost.ConfigureAuthoring(sceneModel, undoStack, prefabs);
            _ = engine.AttachScriptingFromServices();
            engine.EnterEditMode();
            _ = engine.AttachWindowRuntime(window);
            return new EditorProjectSession(project, engine, editorHost, sceneModel, undoStack, projection, prefabs, sceneRelativePath);
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    public void RunOneTick(double deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RefreshEditProjectionIfNeeded();
        _ = Engine.RunOneTick(deltaSeconds);
    }

    public void EnterPlayMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = EnterPlayTemporary();
    }

    public void EnterEditMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ExitEditorPlay();
    }

    public PixelEngine.Hosting.EditorPlaySessionSnapshot CaptureEditorPlaySession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _playSession.Capture();
    }

    public PixelEngine.Hosting.EditorPlaySessionResult EnterPlayCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RefreshEditProjectionIfNeeded();
        return _playSession.EnterPlayCurrent();
    }

    public PixelEngine.Hosting.EditorPlaySessionResult EnterPlayTemporary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RefreshEditProjectionIfNeeded();
        return _playSession.EnterPlayTemporary();
    }

    public PixelEngine.Hosting.EditorPlaySessionResult ExitEditorPlay()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PixelEngine.Hosting.EditorPlaySessionResult result = _playSession.ExitPlay();
        RefreshEditProjectionIfNeeded();
        return result;
    }

    public void StepOnce()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = Engine.StepOnce();
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
        UndoStack.Execute(SceneModel, new InstantiatePrefabCommand(Prefabs, assetPath, SceneModel.SelectedStableId));
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

    public bool ShowPanel(string title)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.TryShowPanel(title);
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

    public PixelEngine.Editor.Shell.Build.ScriptedBuildProbeSnapshot CaptureScriptedBuildProbe()
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

    public PixelEngine.Editor.Shell.Build.ScriptedBuildSettingsProbeSnapshot ApplyScriptedBuildSettingsProbe(string outputDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _editorHost.ApplyScriptedBuildSettingsProbe(outputDirectory);
    }

    public PixelEngine.Editor.Shell.Build.ScriptedBuildSettingsProbeSnapshot CaptureScriptedBuildSettingsProbe()
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

    public PixelEngine.Hosting.SimulationControlSnapshot CaptureSimulationControl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _simulationControl.Capture();
    }

    public string SceneFilePath => Project.ResolveSceneFullPath(CurrentSceneRelativePath);

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
        SceneModel.ReplaceWith(empty, markDirty: false);
        UndoStack.Clear();
        CurrentSceneRelativePath = Project.ResolveSceneRelativePath(relative);
        Project.UpsertScene(CurrentSceneRelativePath, makeStartScene: true);
        SaveScene();
        return CurrentSceneRelativePath;
    }

    public void OpenScene(string sceneRelativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string normalized = Project.ResolveSceneRelativePath(sceneRelativePath);
        EditorSceneModel loaded = LoadSceneModel(Project, normalized);
        SceneModel.ReplaceWith(loaded, markDirty: false);
        UndoStack.Clear();
        CurrentSceneRelativePath = normalized;
        Project.UpsertScene(normalized, makeStartScene: true);
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

    private static EditorSceneModel LoadSceneModel(EditorProject project, string sceneRelativePath)
    {
        string scenePath = project.ResolveSceneFullPath(sceneRelativePath);
        return File.Exists(scenePath)
            ? EditorSceneModel.FromDocument(EngineSceneDocumentLoader.LoadDocument(scenePath))
            : EditorSceneModel.Empty(project.ResolveDisplaySceneName(sceneRelativePath));
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

    private void RefreshEditProjectionIfNeeded(bool force = false)
    {
        if (Engine.Mode == EngineExecutionMode.Play || (!force && SceneModel.Version == _runtimeProjectionVersion))
        {
            return;
        }

        Prefabs.RefreshPrefabInstances(SceneModel);
        RuntimeProjection = ProjectAuthoringScene(Engine, SceneModel);
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
            },
            new()
            {
                Id = 1,
                Name = "stone",
                Type = CellType.Solid,
                Density = 200,
                HeatCapacity = 1f,
                TextureId = -1,
                BaseColorBGRA = 0xFF808080,
            },
        ];
        MaterialTable materials = new(definitions);
        ShellMaterialQuery query = new(materials);
        engine.Context.RegisterService(materials);
        engine.Context.RegisterService<IMaterialQuery>(EngineServiceRole.MaterialRegistry, query);
        engine.Context.RegisterService(query);
    }

    private sealed class ShellMaterialQuery(MaterialTable materials) : IMaterialQuery
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
            return new MaterialInfo(id, material.Name, material.Density, material.Type == CellType.Solid);
        }
    }
}
