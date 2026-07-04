using PixelEngine.Hosting;
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
    private bool _disposed;

    private EditorProjectSession(
        EditorProject project,
        Engine engine,
        EditorShellHostExtension editorHost,
        EditorSceneModel sceneModel,
        EditorUndoStack undoStack,
        EditorSceneRuntimeProjection runtimeProjection)
    {
        Project = project;
        Engine = engine;
        _editorHost = editorHost;
        SceneModel = sceneModel;
        UndoStack = undoStack;
        RuntimeProjection = runtimeProjection;
        _snapshotStore = new EngineWorldSnapshotStore(engine);
        _playSession = new EngineEditorPlaySessionService(engine, _snapshotStore);
        _simulationControl = new EngineSimulationControlService(engine);
    }

    public EditorProject Project { get; }

    public Engine Engine { get; }

    public EditorSceneModel SceneModel { get; }

    public EditorUndoStack UndoStack { get; }

    public EditorSceneRuntimeProjection RuntimeProjection { get; private set; }

    public int PanelCount => _editorHost.PanelCount;

    public long EditorBridgeFrameCount => _editorHost.BridgeFrameCount;

    public static EditorProjectSession Open(EditorProject project, RenderWindow window, EditorShellApp app)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(app);
        EditorShellHostExtension editorHost = new(project, app);
        Engine engine = new EngineBuilder()
            .WithProject(project.ToEngineProject())
            .UseVSync(true)
            .AddEditorHostExtension(editorHost)
            .Build();
        try
        {
            AttachContentAndWorld(engine);
            _ = engine.AttachPhysics();
            EditorSceneModel sceneModel = LoadSceneModel(project);
            EditorUndoStack undoStack = new();
            EditorSceneRuntimeProjection projection = ProjectAuthoringScene(engine, sceneModel);
            editorHost.ConfigureAuthoring(sceneModel, undoStack);
            _ = engine.AttachScriptingFromServices();
            engine.EnterEditMode();
            _ = engine.AttachWindowRuntime(window);
            return new EditorProjectSession(project, engine, editorHost, sceneModel, undoStack, projection);
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
        _ = Engine.RunOneTick(deltaSeconds);
    }

    public void EnterPlayMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = _playSession.EnterPlayTemporary();
    }

    public void EnterEditMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = _playSession.ExitPlay();
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

    private static EditorSceneModel LoadSceneModel(EditorProject project)
    {
        string scenePath = Path.GetFullPath(Path.Combine(project.ContentRootPath, project.StartScene));
        return File.Exists(scenePath)
            ? EditorSceneModel.FromDocument(EngineSceneDocumentLoader.LoadDocument(scenePath))
            : EditorSceneModel.Empty(project.ResolveDisplaySceneName(project.StartScene));
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
