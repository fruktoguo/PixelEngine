using System.Diagnostics;
using System.Reflection;
using PixelEngine.Audio;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Time;
using PixelEngine.Editor;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.Serialization;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.World;

namespace PixelEngine.Hosting;

/// <summary>
/// PixelEngine 运行时门面，拥有 EngineContext 并控制生命周期。
/// </summary>
public sealed class Engine : IDisposable
{
    private readonly EngineLifecycle _lifecycle;
    private readonly List<IDisposable> _ownedRuntimeResources = [];
    private IScriptRuntime? _attachedScriptRuntime;
    private bool _shutdownRequested;
    private bool _disposed;

    internal Engine(EngineContext context, EnginePhasePipeline phases, EngineLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(phases);
        ArgumentNullException.ThrowIfNull(lifecycle);
        Context = context;
        Phases = phases;
        _lifecycle = lifecycle;
        State = EngineRunState.Created;
        Mode = EngineExecutionMode.Play;
        RequestedSimHz = context.Options.SimHz;
    }

    /// <summary>
    /// 当前运行上下文。
    /// </summary>
    public EngineContext Context { get; }

    /// <summary>
    /// 12 相位同步调度管线。
    /// </summary>
    public EnginePhasePipeline Phases { get; }

    /// <summary>
    /// 当前生命周期状态。
    /// </summary>
    public EngineRunState State { get; private set; }

    /// <summary>
    /// 当前 Play/Edit/Step 执行模式。
    /// </summary>
    public EngineExecutionMode Mode { get; private set; }

    /// <summary>
    /// 当前由用户或工具请求的基础 sim 频率；自动降级可临时覆盖到更低档。
    /// </summary>
    public double RequestedSimHz { get; private set; }

    /// <summary>
    /// 切换到运行模式，后续 tick 会推进 sim/physics。
    /// </summary>
    public void EnterPlayMode()
    {
        ThrowIfShutdown();
        Mode = EngineExecutionMode.Play;
    }

    /// <summary>
    /// 切换到编辑模式，后续普通 tick 只推进渲染与后台流式相位。
    /// </summary>
    public void EnterEditMode()
    {
        ThrowIfShutdown();
        Mode = EngineExecutionMode.Edit;
    }

    /// <summary>
    /// 设置基础 sim 频率；后续普通 tick 由 FrameClock 使用该频率，自动过载降级仍可临时降到 30Hz。
    /// </summary>
    /// <param name="simHz">目标 sim 频率，目前支持 60Hz 与 30Hz。</param>
    public void SetRequestedSimHz(double simHz)
    {
        ThrowIfShutdown();
        Context.Clock.SimHz = simHz;
        Context.Counters.SimHz = simHz;
        RequestedSimHz = simHz;
    }

    /// <summary>
    /// 在编辑模式下执行恰好一个 sim tick，随后回到编辑模式。
    /// </summary>
    public FrameTiming StepOnce(double realDeltaSeconds = 0)
    {
        ThrowIfShutdown();
        if (Mode != EngineExecutionMode.Edit)
        {
            throw new InvalidOperationException("StepOnce 只能从编辑模式触发。");
        }

        Mode = EngineExecutionMode.Step;
        try
        {
            return RunOneTick(realDeltaSeconds);
        }
        finally
        {
            Mode = EngineExecutionMode.Edit;
        }
    }

    /// <summary>
    /// 注册包含 Demo Behaviour 的脚本程序集，供脚本宿主在装配期发现类型。
    /// </summary>
    /// <param name="assembly">脚本程序集。</param>
    public void RegisterScriptAssembly(Assembly assembly)
    {
        ThrowIfShutdown();
        Context.GetService<ScriptAssemblyRegistry>().Register(assembly);
        MaterializeCurrentSceneScriptsIfPossible();
    }

    /// <summary>
    /// 从当前 ContentRoot 加载材质与反应内容包，并注册材质/反应运行时服务。
    /// </summary>
    /// <returns>加载后的内容包。</returns>
    public EngineContentPackage LoadContentPackage()
    {
        ThrowIfShutdown();
        EngineContentPackage package = EngineContentLoader.LoadMaterialPackage(Context.Options.ContentRoot);
        Context.RegisterService(package);
        Context.RegisterService<IMaterialQuery>(EngineServiceRole.MaterialRegistry, package.MaterialRegistry);
        Context.RegisterService(package.MaterialRegistry);
        Context.RegisterService(package.MaterialTable);
        Context.RegisterService(package.ReactionTable);
        return package;
    }

    /// <summary>
    /// 判断当前 ContentRoot 是否存在可加载的材质/反应内容包。
    /// </summary>
    /// <returns>materials.json 与 reactions.json 都存在时返回 true。</returns>
    public bool HasContentPackage()
    {
        ThrowIfShutdown();
        return EngineContentLoader.HasMaterialPackage(Context.Options.ContentRoot);
    }

    /// <summary>
    /// 初始化音频系统并加载 ContentRoot/audio 下的 WAV clip，供脚本音频 API 使用。
    /// </summary>
    /// <param name="backend">可选音频后端；为 null 时优先 OpenAL，失败自动降级无声后端。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已加载 clip 数。</returns>
    public async ValueTask<int> AttachAudioFromContentAsync(
        IAudioBackend? backend = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfShutdown();
        string audioRoot = Path.Combine(Context.Options.ContentRoot, "audio");
        if (!Directory.Exists(audioRoot))
        {
            throw new DirectoryNotFoundException($"音频内容目录不存在：{audioRoot}");
        }

        AudioSystem audio = ResolveAudioSystem(backend);
        AudioClipCache cache = ResolveAudioClipCache(audio, audioRoot);
        IReadOnlyDictionary<int, string> cueMap = AudioCueManifest.Load(audioRoot);
        string[] wavFiles = Directory.GetFiles(audioRoot, "*.wav", SearchOption.AllDirectories);
        Array.Sort(wavFiles, StringComparer.Ordinal);
        for (int i = 0; i < wavFiles.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string assetId = Path.GetRelativePath(audioRoot, wavFiles[i]).Replace('\\', '/');
            _ = await cache.LoadAsync(assetId, cancellationToken).ConfigureAwait(false);
        }

        RegisterScriptAudioApi(audio, cache);
        EnsureAudioPhaseDriver(audio, cache, cueMap);
        return cache.LoadedCount;
    }

    /// <summary>
    /// 将已创建的渲染窗口输入接入脚本输入 API；窗口态每帧在相位 0 更新输入快照。
    /// </summary>
    /// <param name="window">渲染窗口。</param>
    /// <param name="routeProvider">可选输入门控；Editor/ImGui 可用它屏蔽脚本通道。</param>
    /// <returns>脚本输入 API 实例。</returns>
    public ScriptInputApi AttachWindowInput(RenderWindow window, Func<EngineTickContext, ScriptInputRoute>? routeProvider = null)
    {
        ThrowIfShutdown();
        ArgumentNullException.ThrowIfNull(window);
        if (Context.TryGetService(out SilkInputPhaseDriver existing))
        {
            return Context.GetService<ScriptInputApi>();
        }

        ScriptInputApi input = ResolveConcreteInputApi();
        Context.RegisterService(EngineServiceRole.Input, input);
        Context.RegisterService<IInputApi>(input);
        SilkInputPhaseDriver driver = new(window, input, routeProvider);
        Context.RegisterService(driver.GetType(), driver);
        driver.RegisterPhases(Phases);
        return input;
    }

    /// <summary>
    /// 创建并接入窗口、输入与真实 Rendering 管线，用于非 headless Demo/runtime。
    /// </summary>
    /// <returns>已创建的渲染窗口。</returns>
    public RenderWindow AttachWindowRuntime()
    {
        ThrowIfShutdown();
        if (Context.Options.Headless)
        {
            throw new InvalidOperationException("headless Engine 不能接入窗口运行时。");
        }

        if (Context.TryGetService(out RenderWindow existing))
        {
            return existing;
        }

        RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine Demo",
            Width = Context.Options.WindowWidth,
            Height = Context.Options.WindowHeight,
        });
        _ownedRuntimeResources.Add(window);
        Context.RegisterService(window);
        _ = AttachWindowInput(window, _ => ResolveGuiInputRoute());
        _ = AttachCameraSynchronization(window);
        _ = AttachRendering(window);
        Phases.Register(EnginePhase.InputAndTime, _ =>
        {
            if (window.IsClosing)
            {
                _shutdownRequested = true;
            }
        });
        return window;
    }

    /// <summary>
    /// 接入脚本相机同步，使 Rendering/World 能消费脚本相机快照。
    /// </summary>
    /// <param name="window">可选渲染窗口；提供时每帧把窗口尺寸回写脚本相机视口。</param>
    /// <returns>脚本相机同步器。</returns>
    public ScriptCameraSynchronizer AttachCameraSynchronization(RenderWindow? window = null)
    {
        ThrowIfShutdown();
        if (Context.TryGetService(out ScriptCameraSynchronizer existing))
        {
            if (window is not null && Context.TryGetService(out ScriptCameraSyncPhaseDriver existingDriver))
            {
                existingDriver.AttachWindow(window);
                _ = existing.Sync(window.Width, window.Height);
            }

            return existing;
        }

        ScriptCameraApi camera = ResolveConcreteCameraApi();
        WorldManager? world = Context.TryGetService(out WorldManager registeredWorld)
            ? registeredWorld
            : null;
        ScriptCameraSynchronizer synchronizer = new(camera, world);
        ScriptCameraSyncPhaseDriver driver = new(synchronizer, window);
        Context.RegisterService(synchronizer);
        Context.RegisterService(driver.GetType(), driver);
        driver.RegisterPhases(Phases);
        _ = synchronizer.Sync(window?.Width ?? 0, window?.Height ?? 0);
        return synchronizer;
    }

    /// <summary>
    /// 接入真实 Rendering 管线，将 Simulation render buffer、脚本相机与光照同步结果提交到窗口。
    /// </summary>
    /// <param name="window">渲染窗口。</param>
    /// <returns>已接入的 Rendering 相位驱动。</returns>
    public RenderPhaseDriver AttachRendering(RenderWindow window)
    {
        ThrowIfShutdown();
        ArgumentNullException.ThrowIfNull(window);
        if (Context.TryGetService(out RenderPhaseDriver existing))
        {
            return existing;
        }

        SimulationPhaseDriver simulation = Context.GetService<SimulationPhaseDriver>();
        ScriptCameraSynchronizer camera = AttachCameraSynchronization(window);
        ScriptLightingSynchronizer lighting = AttachLightingSynchronization();
        RenderPipeline pipeline = new(window, Math.Max(1, window.Width), Math.Max(1, window.Height));
        RenderPipelineFrameSink sink = new(pipeline);
        RenderPhaseDriver driver = new(
            Context.GetService<IChunkSource>(),
            simulation.Materials,
            simulation.Temperature,
            simulation.Particles,
            camera,
            lighting,
            sink,
            Context.Jobs);
        Context.RegisterService(pipeline);
        Context.RegisterService<IGpuComputeQualityDegrader>(pipeline);
        Context.RegisterService<IRenderFrameSink>(sink);
        Context.RegisterService(sink);
        Context.RegisterService(driver.GetType(), driver);
        driver.RegisterPhases(Phases);
        _ownedRuntimeResources.Add(pipeline);
        AttachGuiRuntime(window, pipeline);
        return driver;
    }

    private void AttachGuiRuntime(RenderWindow window, RenderPipeline pipeline)
    {
        if (Context.TryGetService(out EditorRenderBridge _))
        {
            return;
        }

        bool hasScriptGui = Context.TryGetService(out IScriptRuntime scriptRuntime);
        bool wantsEditor = Context.Options.EnableEditor;
        if (!hasScriptGui && !wantsEditor)
        {
            return;
        }

        EditorApp editor = ResolveEditorApp(wantsEditor);
        EditorInputConnector input = new(window, editor.Input);
        EditorRenderBridge? bridge = EditorRenderBridge.AttachIfEnabled(
            pipeline,
            editor,
            Context.Counters,
            Context.Profiler,
            () => EditorRuntimeDiagnosticsProvider.Create(Context),
            hasScriptGui ? scriptRuntime : null);
        if (bridge is null)
        {
            input.Dispose();
            return;
        }

        Context.RegisterService(bridge);
        _ownedRuntimeResources.Add(input);
        _ownedRuntimeResources.Add(bridge);
    }

    private EditorApp ResolveEditorApp(bool enableDockSpace)
    {
        if (Context.TryGetService(out EditorApp existing))
        {
            return existing;
        }

        EditorApp created = new(
            new HexaImGuiBackend(),
            new EditorAppOptions
            {
                Enabled = true,
                EnableDockSpace = enableDockSpace,
                LayoutPath = Path.Combine(Context.Options.ContentRoot, "imgui.ini"),
            });
        Context.RegisterService(created);
        _ownedRuntimeResources.Add(created);
        return created;
    }

    private ScriptInputRoute ResolveGuiInputRoute()
    {
        if (!Context.TryGetService(out EditorApp editor))
        {
            return ScriptInputRoute.Allowed;
        }

        EditorInputSnapshot capture = editor.Input.Capture;
        return new ScriptInputRoute(capture.AllowWorldKeyboard, capture.AllowWorldMouse);
    }

    /// <summary>
    /// 接入脚本光照同步，使 Rendering 能消费点光源与 fog-of-war 请求。
    /// </summary>
    /// <returns>脚本光照同步器。</returns>
    public ScriptLightingSynchronizer AttachLightingSynchronization()
    {
        ThrowIfShutdown();
        if (Context.TryGetService(out ScriptLightingSynchronizer existing))
        {
            return existing;
        }

        ScriptLightingApi lighting = ResolveConcreteLightingApi();
        ScriptCameraSynchronizer camera = AttachCameraSynchronization();
        ScriptLightingSynchronizer synchronizer = new(lighting, camera);
        ScriptLightingSyncPhaseDriver driver = new(synchronizer);
        Context.RegisterService(synchronizer);
        Context.RegisterService(driver.GetType(), driver);
        driver.RegisterPhases(Phases);
        synchronizer.Sync();
        return synchronizer;
    }

    /// <summary>
    /// 基于已加载材质表装配一个固定尺寸 resident world，并把真实 Simulation 相位接入主循环。
    /// </summary>
    /// <param name="worldWidthCells">可玩世界宽度，单位 cell。</param>
    /// <param name="worldHeightCells">可玩世界高度，单位 cell。</param>
    /// <param name="particleCapacity">自由粒子池容量。</param>
    /// <returns>已注册的 Simulation 相位驱动。</returns>
    public SimulationPhaseDriver AttachResidentSimulationWorld(
        int worldWidthCells,
        int worldHeightCells,
        int particleCapacity = 32768)
    {
        ThrowIfShutdown();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worldWidthCells);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worldHeightCells);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(particleCapacity);
        if (Context.TryGetService(out SimulationPhaseDriver existing))
        {
            return existing;
        }

        MaterialTable materials = Context.GetService<MaterialTable>();
        ResidentChunkMap chunks = new();
        AddResidentChunks(chunks, worldWidthCells, worldHeightCells);
        ParticleSystem particles = new(particleCapacity, Context.Events);
        TemperatureField temperature = new();
        return AttachSimulationWorld(chunks, materials, particles, temperature);
    }

    /// <summary>
    /// 将真实 PhysicsSystem 接入相位 8，并注册脚本/Demo 可见的 PhysicsService。
    /// </summary>
    /// <returns>已注册的 Physics 相位驱动。</returns>
    public PhysicsPhaseDriver AttachPhysics()
    {
        ThrowIfShutdown();
        if (Context.TryGetService(out PhysicsPhaseDriver existing))
        {
            return existing;
        }

        CellGrid grid = Context.GetService<CellGrid>();
        IChunkSource chunks = Context.GetService<IChunkSource>();
        RigidDamageQueue damageQueue = Context.TryGetService(out RigidDamageQueue existingDamageQueue)
            ? existingDamageQueue
            : new RigidDamageQueue();
        ParticleSystem? particles = Context.TryGetService(out ParticleSystem registeredParticles)
            ? registeredParticles
            : null;
        RigidBodyDestruction destruction = new(fragmentPixelThreshold: 4, particles);
        PhysicsSystem physics = PhysicsSystem.Initialize(
            grid,
            Context.Jobs,
            damageQueue: damageQueue,
            destruction: destruction,
            profiler: Context.Profiler,
            eventBus: Context.Events);
        PhysicsPhaseDriver driver = new(physics, chunks);
        Context.RegisterService(damageQueue);
        Context.RegisterService(physics);
        Context.RegisterService(EngineServiceRole.PhysicsService, physics);
        Context.RegisterService(driver.GetType(), driver);
        driver.RegisterPhases(Phases);
        if (Context.TryGetService(out RuntimeWorldStateBridge stateBridge))
        {
            stateBridge.AttachPhysics(physics);
        }

        _ownedRuntimeResources.Add(physics);
        return driver;
    }

    /// <summary>
    /// 从 plan/07 save directory 读取整世界存档，并接入 Simulation/World/Streaming 后端。
    /// </summary>
    /// <param name="savePath">包含 manifest.bin 与 regions/ 的世界存档目录。</param>
    /// <param name="particleCapacity">自由粒子池容量，必须能容纳存档中的在飞粒子。</param>
    /// <param name="fallbackMaterialId">存档材质名在当前材质表中缺失时使用的 fallback 材质 id。</param>
    /// <param name="streamingConfig">可选世界流式配置。</param>
    /// <returns>读档结果。</returns>
    public WorldLoadResult AttachWorldFromSaveDirectory(
        string savePath,
        int particleCapacity = 32768,
        ushort fallbackMaterialId = 0,
        WorldStreamingConfig? streamingConfig = null)
    {
        ThrowIfShutdown();
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(particleCapacity);
        if (Context.TryGetService(out SimulationPhaseDriver _))
        {
            throw new InvalidOperationException("当前 Engine 已接入 Simulation world，不能重复从存档目录装配世界。");
        }

        string resolvedPath = Path.GetFullPath(savePath);
        MaterialTable materials = Context.GetService<MaterialTable>();
        _ = materials.GetName(fallbackMaterialId);
        TemperatureField temperature = new();
        WorldManager world = new(
            new WorldCamera(0, 0, Context.Options.InternalWidth, Context.Options.InternalHeight),
            temperature,
            materials,
            resolvedPath,
            fallbackMaterialId,
            streamingConfig);
        ParticleSystem particles = new(particleCapacity, Context.Events);
        RuntimeWorldStateBridge stateBridge = new(particles);
        WorldLoadResult result = new WorldSaveService().LoadAll(
            resolvedPath,
            new WorldLoadContext(
                world.Chunks,
                world.Residency,
                temperature,
                materials,
                fallbackMaterialId,
                currentParityBit: 0),
            stateBridge);

        _ = AttachSimulationWorld(
            world.Chunks,
            materials,
            particles,
            temperature,
            result.WorldSeed,
            checked((uint)result.GameTimeTicks));
        Context.Clock.RestoreCounters(result.GameTimeTicks, result.GameTimeTicks);
        _ = AttachWorldManager(world);
        Context.RegisterService<IWorldStateSnapshotSource>(stateBridge);
        Context.RegisterService<IWorldStateSnapshotSink>(stateBridge);
        Context.RegisterService(stateBridge);
        if (stateBridge.RigidBodyCount > 0)
        {
            _ = AttachPhysics();
        }

        return result;
    }

    /// <summary>
    /// 按当前场景来源显式装配初始世界；SaveDirectory 直接读档，SceneFile 读取 InitialSaveDirectory。
    /// </summary>
    /// <param name="particleCapacity">自由粒子池容量，必须能容纳存档中的在飞粒子。</param>
    /// <param name="fallbackMaterialId">存档材质名在当前材质表中缺失时使用的 fallback 材质 id。</param>
    /// <param name="streamingConfig">可选世界流式配置。</param>
    /// <returns>装配了存档世界时返回读档结果；当前场景没有存档来源时返回 null。</returns>
    public WorldLoadResult? AttachCurrentSceneWorld(
        int particleCapacity = 32768,
        ushort fallbackMaterialId = 0,
        WorldStreamingConfig? streamingConfig = null)
    {
        ThrowIfShutdown();
        Scene scene = Context.GetService<ISceneService>().Current ??
            throw new InvalidOperationException("当前没有已加载场景，不能装配场景世界。");
        return scene.Descriptor.SourceKind switch
        {
            SceneSourceKind.SaveDirectory => AttachWorldFromSaveDirectory(
                scene.ResolvedSource ?? throw new InvalidOperationException("SaveDirectory 场景缺少解析后的存档路径。"),
                particleCapacity,
                fallbackMaterialId,
                streamingConfig),
            SceneSourceKind.SceneFile => AttachSceneFileInitialWorld(
                scene.ResolvedSource ?? throw new InvalidOperationException(".scene 场景缺少解析后的文件路径。"),
                particleCapacity,
                fallbackMaterialId,
                streamingConfig),
            SceneSourceKind.Empty or SceneSourceKind.Procedural => null,
            _ => throw new ArgumentOutOfRangeException(nameof(scene), scene.Descriptor.SourceKind, "未知场景来源类型。"),
        };
    }

    /// <summary>
    /// 切换到已注册场景描述；实际世界构建由对应场景后端在后续装配中完成。
    /// </summary>
    /// <param name="name">场景稳定名称。</param>
    /// <returns>当前场景实例。</returns>
    public Scene LoadScene(string name)
    {
        ThrowIfShutdown();
        Scene scene = Context.GetService<ISceneService>().SwitchTo(name);
        MaterializeSceneScripts(scene);
        return scene;
    }

    /// <summary>
    /// 将脚本上下文接入 Hosting 相位 1，并注册脚本运行时服务。
    /// </summary>
    /// <param name="scriptContext">脚本访问引擎能力的统一上下文。</param>
    /// <param name="runtime">可选脚本运行时；为 null 时创建默认 <see cref="ScriptRuntime" />。</param>
    public void AttachScripting(IScriptContext scriptContext, IScriptRuntime? runtime = null)
    {
        ThrowIfShutdown();
        ArgumentNullException.ThrowIfNull(scriptContext);
        if (_attachedScriptRuntime is not null)
        {
            throw new InvalidOperationException("脚本运行时已经接入当前 Engine。");
        }

        runtime ??= new ScriptRuntime();
        ScriptingPhaseDriver driver = new(runtime, scriptContext);
        driver.RegisterPhases(Phases);
        _attachedScriptRuntime = runtime;
        Context.RegisterService(EngineServiceRole.Scripting, runtime);
        Context.RegisterService(scriptContext);
    }

    /// <summary>
    /// 从 Hosting 已注册的真实 Simulation/Physics/Audio/Input/Camera 服务创建脚本上下文并接入相位管线。
    /// </summary>
    /// <param name="runtime">可选脚本运行时；为 null 时创建默认 <see cref="ScriptRuntime" />。</param>
    /// <returns>已接入的真实 Simulation 脚本上下文。</returns>
    public ScriptSimulationContext AttachScriptingFromServices(IScriptRuntime? runtime = null)
    {
        ThrowIfShutdown();
        if (_attachedScriptRuntime is not null)
        {
            throw new InvalidOperationException("脚本运行时已经接入当前 Engine。");
        }

        PixelEngine.Scripting.Scene scriptScene = ResolveCurrentScriptScene();
        SimulationPhaseDriver? simulationDriver = Context.TryGetService(out SimulationPhaseDriver driver)
            ? driver
            : null;
        CellGrid grid = ResolveCellGrid(simulationDriver);
        SimulationKernel kernel = ResolveSimulationKernel(simulationDriver);
        ParticleSystem particles = ResolveParticleSystem(simulationDriver);
        MaterialTable materials = ResolveMaterialTable(simulationDriver);
        ScriptEventBus events = ResolveScriptEventBus();
        ScriptFrameTime time = ResolveScriptFrameTime();
        ICameraApi camera = ResolveCameraApi();
        IInputApi input = ResolveInputApi();
        ILightingApi lighting = ResolveLightingApi();
        IAudioApi? audio = ResolveAudioApiOrNull();
        PhysicsSystem? physics = Context.TryGetService(out PhysicsSystem registeredPhysics)
            ? registeredPhysics
            : null;

        RegisterSimulationRolesIfMissing(grid, particles, materials, physics);
        ScriptSimulationContext scriptContext = new(
            scriptScene,
            grid,
            kernel,
            particles,
            materials,
            events,
            time,
            audio,
            physics,
            camera,
            input,
            lighting);
        Context.RegisterService(scriptContext);
        simulationDriver?.AttachScriptContext(scriptContext);
        AttachScripting(scriptContext, runtime);
        if (camera is ScriptCameraApi)
        {
            _ = AttachCameraSynchronization();
        }

        if (lighting is ScriptLightingApi)
        {
            _ = AttachLightingSynchronization();
        }

        return scriptContext;
    }

    private void MaterializeCurrentSceneScriptsIfPossible()
    {
        ISceneService scenes = Context.GetService<ISceneService>();
        if (scenes.Current is not null)
        {
            MaterializeSceneScripts(scenes.Current);
        }
    }

    private void MaterializeSceneScripts(Scene scene)
    {
        if (scene.ScriptScene is not null)
        {
            Context.RegisterService(scene.ScriptScene);
            return;
        }

        if (scene.Descriptor.SourceKind == SceneSourceKind.SceneFile && scene.ResolvedSource is not null)
        {
            PixelEngine.Scripting.Scene scriptScene = EngineSceneDocumentLoader.Load(
                scene.ResolvedSource,
                Context.GetService<ScriptAssemblyRegistry>());
            scene.AttachScriptScene(scriptScene);
            Context.RegisterService(scriptScene);
            return;
        }

        if (scene.Descriptor.SourceKind == SceneSourceKind.Procedural && scene.ResolvedSource is not null)
        {
            PixelEngine.Scripting.Scene scriptScene = BuildProceduralScriptScene(
                scene.ResolvedSource,
                Context.GetService<ScriptAssemblyRegistry>());
            scene.AttachScriptScene(scriptScene);
            Context.RegisterService(scriptScene);
        }
    }

    private PixelEngine.Scripting.Scene ResolveCurrentScriptScene()
    {
        MaterializeCurrentSceneScriptsIfPossible();
        if (Context.TryGetService(out PixelEngine.Scripting.Scene scriptScene))
        {
            return scriptScene;
        }

        ISceneService scenes = Context.GetService<ISceneService>();
        Scene current = scenes.Current ??
            throw new InvalidOperationException("无法自动接入脚本：当前没有已加载或已配置的场景。");
        MaterializeSceneScripts(current);
        return current.ScriptScene ??
            throw new InvalidOperationException("无法自动接入脚本：当前场景没有可物化的脚本 Scene。");
    }

    private CellGrid ResolveCellGrid(SimulationPhaseDriver? simulationDriver)
    {
        if (Context.TryGetService(out CellGrid grid))
        {
            return grid;
        }

        if (simulationDriver is not null)
        {
            Context.RegisterService(simulationDriver.Grid);
            return simulationDriver.Grid;
        }

        throw new InvalidOperationException("无法自动接入脚本：缺少 CellGrid 或 SimulationPhaseDriver。");
    }

    private SimulationKernel ResolveSimulationKernel(SimulationPhaseDriver? simulationDriver)
    {
        if (Context.TryGetService(out SimulationKernel kernel))
        {
            return kernel;
        }

        if (simulationDriver is not null)
        {
            Context.RegisterService(simulationDriver.Kernel);
            return simulationDriver.Kernel;
        }

        throw new InvalidOperationException("无法自动接入脚本：缺少 SimulationKernel 或 SimulationPhaseDriver。");
    }

    private ParticleSystem ResolveParticleSystem(SimulationPhaseDriver? simulationDriver)
    {
        if (Context.TryGetService(out ParticleSystem particles))
        {
            return particles;
        }

        if (simulationDriver is not null)
        {
            Context.RegisterService(simulationDriver.Particles);
            return simulationDriver.Particles;
        }

        throw new InvalidOperationException("无法自动接入脚本：缺少 ParticleSystem 或 SimulationPhaseDriver。");
    }

    private MaterialTable ResolveMaterialTable(SimulationPhaseDriver? simulationDriver)
    {
        if (Context.TryGetService(out MaterialTable materials))
        {
            return materials;
        }

        if (simulationDriver is not null)
        {
            Context.RegisterService(simulationDriver.Materials);
            return simulationDriver.Materials;
        }

        throw new InvalidOperationException("无法自动接入脚本：缺少 MaterialTable 或 SimulationPhaseDriver。");
    }

    private ScriptEventBus ResolveScriptEventBus()
    {
        if (Context.TryGetService(out ScriptEventBus events))
        {
            return events;
        }

        ScriptEventBus created = new(Context.Events);
        Context.RegisterService(created);
        Context.RegisterService<IEventBus>(created);
        return created;
    }

    private ScriptFrameTime ResolveScriptFrameTime()
    {
        if (Context.TryGetService(out ScriptFrameTime time))
        {
            return time;
        }

        ScriptFrameTime created = new(Context.Clock);
        Context.RegisterService(created);
        Context.RegisterService<IGameTime>(created);
        return created;
    }

    private ICameraApi ResolveCameraApi()
    {
        return Context.TryGetService(out ICameraApi camera) ? camera : ResolveConcreteCameraApi();
    }

    private ScriptCameraApi ResolveConcreteCameraApi()
    {
        if (Context.TryGetService(out ScriptCameraApi camera))
        {
            return camera;
        }

        ScriptCameraApi created = new(
            Context.Options.InternalWidth,
            Context.Options.InternalHeight,
            Context.Options.InternalWidth * 0.5f,
            Context.Options.InternalHeight * 0.5f);
        Context.RegisterService<ICameraApi>(EngineServiceRole.Camera, created);
        Context.RegisterService(created);
        return created;
    }

    private IInputApi ResolveInputApi()
    {
        return Context.TryGetService(out IInputApi input) ? input : ResolveConcreteInputApi();
    }

    private ILightingApi ResolveLightingApi()
    {
        return Context.TryGetService(out ILightingApi lighting) ? lighting : ResolveConcreteLightingApi();
    }

    private ScriptLightingApi ResolveConcreteLightingApi()
    {
        if (Context.TryGetService(out ScriptLightingApi lighting))
        {
            return lighting;
        }

        ScriptLightingApi created = new();
        Context.RegisterService<ILightingApi>(created);
        Context.RegisterService(created);
        return created;
    }

    private ScriptInputApi ResolveConcreteInputApi()
    {
        if (Context.TryGetService(out ScriptInputApi input))
        {
            return input;
        }

        ScriptInputApi created = new();
        Context.RegisterService<IInputApi>(EngineServiceRole.Input, created);
        Context.RegisterService(created);
        return created;
    }

    private IAudioApi? ResolveAudioApiOrNull()
    {
        if (Context.TryGetService(out IAudioApi audio))
        {
            return audio;
        }

        if (!Context.TryGetService(out AudioSystem audioSystem) ||
            !Context.TryGetService(out AudioClipCache clips))
        {
            return null;
        }

        ScriptAudioApi created = new(audioSystem, clips);
        Context.RegisterService<IAudioApi>(EngineServiceRole.AudioService, created);
        Context.RegisterService(created);
        return created;
    }

    private AudioSystem ResolveAudioSystem(IAudioBackend? backend)
    {
        if (Context.TryGetService(out AudioSystem audio))
        {
            return backend is not null
                ? throw new InvalidOperationException("AudioSystem 已存在，不能重新指定音频后端。")
                : audio;
        }

        AudioSystem created = new();
        created.Initialize(new AudioSettings(), backend);
        Context.RegisterService(EngineServiceRole.AudioService, created);
        Context.RegisterService(created);
        return created;
    }

    private AudioClipCache ResolveAudioClipCache(AudioSystem audio, string audioRoot)
    {
        if (Context.TryGetService(out AudioClipCache cache))
        {
            audio.AttachClipCache(cache);
            return cache;
        }

        AudioClipCache created = new(audio.Backend, new DirectoryAudioAssetStore(audioRoot), new WavDecoder());
        audio.AttachClipCache(created);
        Context.RegisterService(created);
        return created;
    }

    private void RegisterScriptAudioApi(AudioSystem audio, AudioClipCache cache)
    {
        if (Context.TryGetService(out IAudioApi _))
        {
            return;
        }

        ScriptAudioApi created = new(audio, cache);
        Context.RegisterService<IAudioApi>(created);
        Context.RegisterService(created);
    }

    private void EnsureAudioPhaseDriver(
        AudioSystem audio,
        AudioClipCache cache,
        IReadOnlyDictionary<int, string> cueMap)
    {
        if (Context.TryGetService(out AudioPhaseDriver _))
        {
            return;
        }

        AudioPhaseDriver driver = CreateAudioPhaseDriver(audio, cache, cueMap);
        Context.RegisterService(driver.GetType(), driver);
        driver.RegisterPhases(Phases);
    }

    private AudioPhaseDriver CreateAudioPhaseDriver(
        AudioSystem audio,
        AudioClipCache cache,
        IReadOnlyDictionary<int, string> cueMap)
    {
        if (!Context.TryGetService(out MaterialTable materials) || cueMap.Count == 0)
        {
            return new AudioPhaseDriver(audio, BuildAudioListenerView);
        }

        AudioSettings settings = audio.Settings;
        MaterialAudioTable table = MaterialAudioTable.FromMaterialTable(materials);
        AudioCueClipResolver resolver = new(cache, cueMap);
        Context.RegisterService(table);
        Context.RegisterService<IAudioCueBufferResolver>(resolver);
        if (audio.AmbientLoops is null)
        {
            audio.AttachAmbientLoopManager(new AmbientLoopManager(audio.Backend, table, resolver, settings));
        }

        AudioDispatcher dispatcher = new(Context.Events.Channel<PixelEngine.Core.Events.AudioEvent>(), audio.Voices, settings);
        MaterialAudioPlayer player = new(table, resolver, settings);
        return new AudioPhaseDriver(audio, BuildAudioListenerView, dispatcher, player);
    }

    private AudioListenerView BuildAudioListenerView(EngineTickContext context)
    {
        if (Context.TryGetService(out ICameraApi camera))
        {
            RectF viewport = camera.Viewport;
            return new AudioListenerView(camera.CenterX, camera.CenterY, camera.Zoom, (int)viewport.Width, (int)viewport.Height);
        }

        return new AudioListenerView(0f, 0f, 1f, Context.Options.InternalWidth, Context.Options.InternalHeight);
    }

    private void RegisterSimulationRolesIfMissing(
        CellGrid grid,
        ParticleSystem particles,
        MaterialTable materials,
        PhysicsSystem? physics)
    {
        if (!Context.IsServiceAvailable(EngineServiceRole.WorldAccess))
        {
            Context.RegisterService(EngineServiceRole.WorldAccess, grid);
        }

        if (!Context.IsServiceAvailable(EngineServiceRole.ParticleService))
        {
            Context.RegisterService(EngineServiceRole.ParticleService, particles);
        }

        if (!Context.IsServiceAvailable(EngineServiceRole.MaterialRegistry))
        {
            Context.RegisterService(EngineServiceRole.MaterialRegistry, materials);
        }

        if (physics is not null && !Context.IsServiceAvailable(EngineServiceRole.PhysicsService))
        {
            Context.RegisterService(EngineServiceRole.PhysicsService, physics);
        }
    }

    private static void AddResidentChunks(ResidentChunkMap chunks, int worldWidthCells, int worldHeightCells)
    {
        int lastPlayableChunkX = (worldWidthCells - 1) / PixelEngine.Core.EngineConstants.ChunkSize;
        int lastPlayableChunkY = (worldHeightCells - 1) / PixelEngine.Core.EngineConstants.ChunkSize;
        for (int cy = -1; cy <= lastPlayableChunkY + 1; cy++)
        {
            for (int cx = -1; cx <= lastPlayableChunkX + 1; cx++)
            {
                chunks.Add(new Chunk(new ChunkCoord(cx, cy)));
            }
        }
    }

    private SimulationPhaseDriver AttachSimulationWorld(
        ResidentChunkMap chunks,
        MaterialTable materials,
        ParticleSystem particles,
        TemperatureField temperature,
        ulong worldSeed = 0,
        uint frameIndex = 0)
    {
        MaterialPropsTable props = new(materials.Hot);
        CellGrid grid = new(chunks, props);
        SimulationKernel kernel = new(chunks, props, worldSeed: worldSeed, profiler: Context.Profiler);
        if (frameIndex != 0)
        {
            kernel.RestoreFrameState(frameIndex, CurrentParityFromGameTime(frameIndex));
        }

        SimulationPhaseDriver driver = new(chunks, grid, kernel, particles, temperature, materials);
        driver.RegisterPhases(Phases);

        Context.RegisterService(driver.GetType(), driver);
        Context.RegisterService(chunks);
        Context.RegisterService<IChunkSource>(chunks);
        Context.RegisterService(grid);
        Context.RegisterService(kernel);
        Context.RegisterService(particles);
        Context.RegisterService(temperature);
        Context.RegisterService(EngineServiceRole.WorldAccess, grid);
        Context.RegisterService(EngineServiceRole.ParticleService, particles);
        Context.RegisterService(EngineServiceRole.MaterialRegistry, materials);
        return driver;
    }

    private static byte CurrentParityFromGameTime(long gameTimeTicks)
    {
        return (gameTimeTicks & 1L) == 0 ? (byte)0 : CellFlags.Parity;
    }

    private WorldPhaseDriver AttachWorldManager(WorldManager world)
    {
        if (Context.TryGetService(out WorldPhaseDriver existing))
        {
            return existing;
        }

        Context.RegisterService(world);
        WorldPhaseDriver driver = new(world);
        Context.RegisterService(driver.GetType(), driver);
        driver.RegisterPhases(Phases);
        return driver;
    }

    private WorldLoadResult? AttachSceneFileInitialWorld(
        string scenePath,
        int particleCapacity,
        ushort fallbackMaterialId,
        WorldStreamingConfig? streamingConfig)
    {
        EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scenePath);
        if (string.IsNullOrWhiteSpace(document.InitialSaveDirectory))
        {
            return null;
        }

        string savePath = ResolveSceneRelativePath(scenePath, document.InitialSaveDirectory);
        return AttachWorldFromSaveDirectory(savePath, particleCapacity, fallbackMaterialId, streamingConfig);
    }

    private static string ResolveSceneRelativePath(string scenePath, string source)
    {
        if (Path.IsPathRooted(source))
        {
            return Path.GetFullPath(source);
        }

        string directory = Path.GetDirectoryName(scenePath) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(directory, source));
    }

    private static PixelEngine.Scripting.Scene BuildProceduralScriptScene(
        string entryBehaviourName,
        ScriptAssemblyRegistry scriptAssemblies)
    {
        Type behaviourType = ResolveBehaviourType(entryBehaviourName, scriptAssemblies);
        PixelEngine.Scripting.Scene scriptScene = new();
        PixelEngine.Scripting.Entity entity = scriptScene.CreateEntity();
        _ = entity.AddComponent(behaviourType);
        return scriptScene;
    }

    private static Type ResolveBehaviourType(string typeName, ScriptAssemblyRegistry scriptAssemblies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        for (int i = 0; i < scriptAssemblies.Assemblies.Count; i++)
        {
            Assembly assembly = scriptAssemblies.Assemblies[i];
            Type? exact = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (exact is not null && IsConcreteBehaviour(exact))
            {
                return exact;
            }

            foreach (Type type in assembly.GetTypes())
            {
                if (type.Name == typeName && IsConcreteBehaviour(type))
                {
                    return type;
                }
            }
        }

        throw new InvalidOperationException($"无法在已注册脚本程序集中找到 procedural Behaviour：{typeName}。");
    }

    private static bool IsConcreteBehaviour(Type? type)
    {
        return type is not null &&
            !type.IsAbstract &&
            typeof(Behaviour).IsAssignableFrom(type) &&
            type.GetConstructor(Type.EmptyTypes) is not null;
    }

    /// <summary>
    /// 持续运行直到收到取消请求或 Engine 被关闭。
    /// </summary>
    public void Run(CancellationToken cancellationToken = default)
    {
        ThrowIfShutdown();
        Stopwatch stopwatch = Stopwatch.StartNew();
        double previousSeconds = stopwatch.Elapsed.TotalSeconds;
        while (!cancellationToken.IsCancellationRequested && State != EngineRunState.Shutdown)
        {
            double now = stopwatch.Elapsed.TotalSeconds;
            _ = RunOneTick(now - previousSeconds);
            previousSeconds = now;
        }
    }

    /// <summary>
    /// 执行一个运行时 tick 并推进固定步长帧时钟。
    /// </summary>
    public FrameTiming RunOneTick(double realDeltaSeconds = 0)
    {
        ThrowIfShutdown();
        State = EngineRunState.Running;
        FrameProfiler profiler = Context.Profiler;
        profiler.BeginFrame();
        FrameTiming timing;
        try
        {
            using (profiler.Measure(FramePhase.InputAndTime))
            {
                ApplyOverloadPolicy(realDeltaSeconds);
                timing = BeginRuntimeFrame(realDeltaSeconds);
            }

            Phases.Execute(this, timing);
            Context.Counters.SimHz = Context.Clock.SimHz;
            if (_shutdownRequested)
            {
                Shutdown();
            }
        }
        finally
        {
            profiler.EndFrame();
        }

        return timing;
    }

    /// <summary>
    /// headless 模式下按固定次数驱动 tick，供测试与基准使用。
    /// </summary>
    public void RunHeadlessTicks(int tickCount, double realDeltaSeconds = 0)
    {
        ThrowIfShutdown();
        ArgumentOutOfRangeException.ThrowIfNegative(tickCount);
        if (!Context.Options.Headless)
        {
            throw new InvalidOperationException("RunHeadlessTicks 只能在 headless 模式下调用。");
        }

        for (int i = 0; i < tickCount; i++)
        {
            _ = RunOneTick(realDeltaSeconds);
        }
    }

    /// <summary>
    /// 关闭引擎并按生命周期顺序释放已装配资源。
    /// </summary>
    public void Shutdown()
    {
        if (State == EngineRunState.Shutdown)
        {
            return;
        }

        Exception? lifecycleFailure = null;
        try
        {
            _shutdownRequested = false;
            _attachedScriptRuntime?.Shutdown();
            DisposeOwnedRuntimeResources();
            _lifecycle.Shutdown();
        }
        catch (Exception exception)
        {
            lifecycleFailure = exception;
        }
        finally
        {
            Context.Jobs.Dispose();
            State = EngineRunState.Shutdown;
        }

        if (lifecycleFailure is not null)
        {
            throw lifecycleFailure;
        }
    }

    private void DisposeOwnedRuntimeResources()
    {
        for (int i = _ownedRuntimeResources.Count - 1; i >= 0; i--)
        {
            _ownedRuntimeResources[i].Dispose();
        }

        _ownedRuntimeResources.Clear();
    }

    /// <summary>
    /// 释放引擎资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Shutdown();
        _disposed = true;
    }

    private void ThrowIfShutdown()
    {
        ObjectDisposedException.ThrowIf(State == EngineRunState.Shutdown, this);
    }

    private void ApplyOverloadPolicy(double realDeltaSeconds)
    {
        EngineOverloadController overload = Context.GetService<EngineOverloadController>();
        EngineQualityTier previousTier = Context.QualityTier;
        EngineQualityTier tier = overload.SubmitFrame(realDeltaSeconds * 1000.0);
        Context.SetQualityTier(tier);
        if (tier != previousTier && tier >= EngineQualityTier.ReducedLighting)
        {
            ApplyGpuComputeDegradation();
        }

        Context.Clock.SimHz = tier >= EngineQualityTier.Sim30Hz
            ? PixelEngine.Core.EngineConstants.SimHzDownscaled
            : RequestedSimHz;
    }

    private void ApplyGpuComputeDegradation()
    {
        if (Context.TryGetService(out IGpuComputeQualityDegrader degrader))
        {
            _ = degrader.DegradeGpuComputeOneStep();
        }
    }

    private FrameTiming BeginRuntimeFrame(double realDeltaSeconds)
    {
        return Mode switch
        {
            EngineExecutionMode.Edit => Context.Clock.BeginRenderOnlyFrame(realDeltaSeconds),
            EngineExecutionMode.Step => Context.Clock.BeginForcedSimFrame(realDeltaSeconds),
            EngineExecutionMode.Play => Context.Clock.BeginFrame(realDeltaSeconds),
            _ => throw new InvalidOperationException($"未知 Engine 执行模式：{Mode}。"),
        };
    }
}
