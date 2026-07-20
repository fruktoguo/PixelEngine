using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using PixelEngine.Audio;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Time;
using PixelEngine.Gui;
using PixelEngine.Interop.Box2D;
using PixelEngine.UI;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Rendering.Compute;
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
    private const int FullThermalStepInterval = 1;
    private const int ReducedThermalStepInterval = 4;
    private const int RenderFrameSampleCapacity = 240;

    private readonly EngineLifecycle _lifecycle;
    private readonly List<IDisposable> _ownedRuntimeResources = [];
    private readonly double[] _renderFrameSamplesMs = new double[RenderFrameSampleCapacity];
    private readonly double[] _renderFrameSortScratchMs = new double[RenderFrameSampleCapacity];
    private IScriptRuntime? _attachedScriptRuntime;
    private EngineWorldSnapshotStore? _restartSnapshotStore;
    private EngineSceneCanvasSet? _sceneCanvasSet;
    private bool _editorHostExtensionsAttached;
    private bool _windowRuntimeAttached;
    private int _renderFrameSampleIndex;
    private int _renderFrameSampleCount;
    private double _renderFrameSampleSumMs;
    private bool _restartSnapshotCaptured;
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
    /// 当前 Engine 是否已经装配 Simulation world。
    /// </summary>
    public bool IsSimulationWorldAttached
    {
        get
        {
            ThrowIfShutdown();
            return Context.TryGetService(out SimulationPhaseDriver _);
        }
    }

    /// <summary>
    /// Hosting 为 Demo/benchmark probe 提供的稳定运行时门面。
    /// </summary>
    /// <remarks>
    /// 该门面只暴露统计快照、脚本公开 API 和受控 probe 操作，不把 PhysicsSystem 或 RenderPipeline 本体交给玩家入口。
    /// </remarks>
    public EngineProbeApi Probe
    {
        get
        {
            EngineProbeApi probe = Context.GetService<EngineProbeApi>();
            probe.AttachRegisteredScriptBindings(
                Context.TryGetService(out IScriptContext scriptContext) ? scriptContext : null,
                Context.TryGetService(out ScriptInputApi input) ? input : null,
                Context.TryGetService(out ScriptCameraApi camera) ? camera : null,
                Context.TryGetService(out ScriptLightingApi lighting) ? lighting : null,
                Context.TryGetService(out ScriptCameraSynchronizer cameraSynchronizer) ? cameraSynchronizer : null,
                Context.TryGetService(out ScriptLightingSynchronizer lightingSynchronizer) ? lightingSynchronizer : null);
            if (Context.TryGetService(out GameUiCanvasRegistry gameUiRegistry) &&
                Context.TryGetService(out GameUiBackendSelection gameUiSelection) &&
                Context.TryGetService(out UiInputRouter gameUiInputRouter) &&
                Context.TryGetService(out GameUiPhaseDriver gameUiDriver))
            {
                probe.AttachGameUi(gameUiRegistry, in gameUiSelection, gameUiInputRouter, gameUiDriver);
            }

            if (Context.TryGetService(out GuiApp gui))
            {
                probe.AttachGui(gui);
            }

            return probe;
        }
    }

    /// <summary>
    /// 当前 Hosting 场景；没有注册场景时返回 <see langword="null" />。
    /// </summary>
    public Scene? CurrentScene => Context.TryGetService(out ISceneService scenes) ? scenes.Current : null;

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
    /// 是否已请求在当前 tick 结束后关闭。
    /// </summary>
    public bool IsShutdownRequested { get; private set; }

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
    /// 暂停当前 Play session；运行时世界与临时快照保持活动。
    /// </summary>
    public void EnterPauseMode()
    {
        ThrowIfShutdown();
        if (Mode is not (EngineExecutionMode.Play or EngineExecutionMode.Paused))
        {
            throw new InvalidOperationException("只有活动的 Play session 可以暂停。");
        }

        Mode = EngineExecutionMode.Paused;
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
    /// 在编辑模式或暂停的 Play session 中执行恰好一个 sim tick，随后回到原模式。
    /// </summary>
    public FrameTiming StepOnce(double realDeltaSeconds = 0)
    {
        ThrowIfShutdown();
        if (Mode is not (EngineExecutionMode.Edit or EngineExecutionMode.Paused))
        {
            throw new InvalidOperationException("StepOnce 只能从编辑模式或暂停的 Play session 触发。");
        }

        EngineExecutionMode returnMode = Mode;
        Mode = EngineExecutionMode.Step;
        try
        {
            return RunOneTick(realDeltaSeconds);
        }
        finally
        {
            Mode = returnMode;
        }
    }

    /// <summary>
    /// 请求宿主在当前 tick 结束后关闭；若未处于 RunOneTick，可由下一次 tick 消费该请求。
    /// </summary>
    public void RequestShutdown()
    {
        ThrowIfShutdown();
        IsShutdownRequested = true;
    }

    /// <summary>
    /// 请求显示已启用的内嵌 Editor dockspace；未以 Editor 模式启动时返回失败而不伪造 UI。
    /// </summary>
    /// <returns>运行时控制结果。</returns>
    public RuntimeControlResult OpenEditor()
    {
        ThrowIfShutdown();
        return Context.Options.Headless
            ? new RuntimeControlResult(false, "headless 模式没有窗口，不能打开 Editor。")
            : new RuntimeControlResult(false, "内嵌 Demo Editor 已迁移到独立编辑器壳；请启动 PixelEngine。");
    }

    /// <summary>
    /// 将当前关卡恢复到首次脚本 tick 后捕获的运行基线，并回到 Play 模式。
    /// </summary>
    /// <returns>重开请求结果。</returns>
    public RuntimeControlResult RestartCurrentScene()
    {
        ThrowIfShutdown();
        if (!_restartSnapshotCaptured || _restartSnapshotStore is null)
        {
            return new RuntimeControlResult(false, "重开关卡快照尚未捕获。");
        }

        EndScriptPlaySession();
        SaveLoadOperationResult restore = _restartSnapshotStore.RestoreTemporarySnapshot();
        if (!restore.Success)
        {
            return new RuntimeControlResult(false, restore.Message);
        }

        EnterPlayMode();
        WorldLoadResult? load = restore.LoadResult;
        return new RuntimeControlResult(
            true,
            load.HasValue
                ? $"已重开当前关卡：tick={load.Value.GameTimeTicks}, chunks={load.Value.LoadedChunkCount}。"
                : "已重开当前关卡。");
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
    /// 注册程序化世界生成器，供 <see cref="SceneSourceKind.Procedural" /> 场景构建起始世界。
    /// </summary>
    /// <param name="key">场景描述中的生成器键。</param>
    /// <param name="generator">程序化世界生成器。</param>
    public void RegisterProceduralWorldGenerator(string key, IProceduralWorldGenerator generator)
    {
        ThrowIfShutdown();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(generator);
        ProceduralWorldGeneratorRegistry registry = Context.TryGetService(out ProceduralWorldGeneratorRegistry existing)
            ? existing
            : new ProceduralWorldGeneratorRegistry();
        registry.Register(key, generator);
        Context.RegisterService(registry);
    }

    /// <summary>
    /// 注册流式程序化世界生成器，供 <see cref="SceneSourceKind.Procedural" /> 场景按相机动态生成缺失 chunk。
    /// </summary>
    /// <param name="key">场景描述中的生成器键。</param>
    /// <param name="generator">流式程序化世界生成器。</param>
    public void RegisterStreamingProceduralWorldGenerator(string key, IStreamingProceduralWorldGenerator generator)
    {
        ThrowIfShutdown();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(generator);
        ProceduralWorldGeneratorRegistry registry = Context.TryGetService(out ProceduralWorldGeneratorRegistry existing)
            ? existing
            : new ProceduralWorldGeneratorRegistry();
        registry.Register(key, generator);
        Context.RegisterService(registry);
    }

    /// <summary>
    /// 从当前 ContentRoot 加载材质与反应内容包，并注册材质/反应运行时服务。
    /// </summary>
    /// <returns>加载后的内容包。</returns>
    public EngineContentPackage LoadContentPackage()
    {
        ThrowIfShutdown();
        EngineContentPackage package = EngineContentLoader.LoadMaterialPackage(Context.Options.ContentRoot);
        RegisterContentPackage(package);
        return package;
    }

    /// <summary>
    /// 确保当前 Engine 至少具备一个只含 empty 材质的内容包，供内容缺失的窗口探针完成最小可渲染世界初始化。
    /// </summary>
    /// <returns>已有或新建的最小内容包。</returns>
    public EngineContentPackage EnsureMinimalContentPackage()
    {
        ThrowIfShutdown();
        if (Context.TryGetService(out EngineContentPackage existing))
        {
            return existing;
        }

        if (Context.TryGetService(out MaterialTable materials) &&
            Context.TryGetService(out ReactionTable reactions))
        {
            EngineContentPackage package = new(Context.Options.ContentRoot, materials, reactions);
            RegisterContentPackage(package);
            return package;
        }

        MaterialDef[] definitions =
        [
            new MaterialDef
            {
                Id = 0,
                Name = "empty",
                Type = CellType.Empty,
                HeatCapacity = 1f,
                TextureId = -1,
                BaseColorBGRA = 0x00000000,
                Opacity = 0,
                DisplayName = "Empty",
                LegendVisible = false,
            },
        ];
        EngineContentPackage minimal = new(
            Context.Options.ContentRoot,
            new MaterialTable(definitions),
            new ReactionTable([], definitions));
        RegisterContentPackage(minimal);
        return minimal;
    }

    private void RegisterContentPackage(EngineContentPackage package)
    {
        Context.RegisterService(package);
        Context.RegisterService<IMaterialQuery>(EngineServiceRole.MaterialRegistry, package.MaterialRegistry);
        Context.RegisterService(package.MaterialRegistry);
        Context.RegisterService(package.MaterialTable);
        Context.RegisterService(package.ReactionTable);
    }

    /// <summary>
    /// 以稳定顺序保存 .scene 文档，供独立编辑器壳写回场景文件。
    /// </summary>
    /// <param name="document">待保存的场景文档。</param>
    /// <param name="path">目标 .scene 文件路径。</param>
    public void SaveSceneDocument(EngineSceneDocument document, string path)
    {
        ThrowIfShutdown();
        EngineSceneDocumentLoader.SaveDocument(document, path);
    }

    /// <summary>
    /// 将编辑态或工具链中的 .scene 文档 Canvas authoring 投影原子应用到当前运行时。
    /// 窗口尚未装配时会保存解析结果，并在 Game UI 创建时一次物化。
    /// </summary>
    /// <param name="document">包含内建 WebCanvas/CanvasScaler 的完整场景文档。</param>
    public void ApplySceneCanvasDocument(EngineSceneDocument document)
    {
        ThrowIfShutdown();
        ArgumentNullException.ThrowIfNull(document);
        EngineSceneCanvasSet canvasSet = EngineSceneCanvasResolver.Resolve(document);
        if (Context.TryGetService(out GameUiCanvasRegistry registry))
        {
            registry.Configure(canvasSet);
            SynchronizeLegacyGameUiHostService(registry);
        }

        _sceneCanvasSet = canvasSet;
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
    /// 从当前 ContentRoot 加载一个玩法配置 JSON；调用方只提供目标类型，不直接依赖底层 JSON 解析器。
    /// </summary>
    /// <typeparam name="TConfig">配置文档类型。</typeparam>
    /// <param name="relativePath">相对 ContentRoot 的配置路径。</param>
    /// <param name="typeInfo">由调用方提供的 source-generated JSON 类型元数据。</param>
    /// <returns>解析后的配置文档。</returns>
    public TConfig LoadConfig<TConfig>(string relativePath, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TConfig> typeInfo)
        where TConfig : class
    {
        ThrowIfShutdown();
        return EngineContentLoader.LoadConfig(Context.Options.ContentRoot, relativePath, typeInfo);
    }

    /// <summary>
    /// 从当前 ContentRoot 读取配置文本，供玩法层执行显式 AOT-safe 解析。
    /// </summary>
    /// <param name="relativePath">相对 ContentRoot 的配置路径。</param>
    /// <returns>配置文件 UTF-8 文本。</returns>
    public string ReadConfigText(string relativePath)
    {
        ThrowIfShutdown();
        return EngineContentLoader.ReadConfigText(Context.Options.ContentRoot, relativePath);
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
        SilkInputPhaseDriver driver = new(
            window,
            input,
            routeProvider,
            Context.Options.InternalWidth,
            Context.Options.InternalHeight,
            Context.TryGetService(out IGameplayViewportInputMapper gameplayViewportMapper)
                ? gameplayViewportMapper
                : null);
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
            Title = Context.Options.WindowTitle,
            Width = Context.Options.WindowWidth,
            Height = Context.Options.WindowHeight,
            WindowMode = Context.Options.WindowMode,
            VSync = Context.Options.VSync,
        });
        return AttachWindowRuntime(window, takeOwnership: true);
    }

    /// <summary>
    /// 接入由外部宿主拥有的窗口、输入与真实 Rendering 管线；Engine 关闭时不会销毁该窗口。
    /// </summary>
    /// <param name="window">外部宿主已创建并持有所有权的渲染窗口。</param>
    /// <returns>接入的渲染窗口。</returns>
    public RenderWindow AttachWindowRuntime(RenderWindow window)
    {
        return AttachWindowRuntime(window, takeOwnership: false);
    }

    // 窗口运行时接入：注册窗口服务、串联输入/相机/渲染，并在相位 0 监听关闭请求。
    private RenderWindow AttachWindowRuntime(RenderWindow window, bool takeOwnership)
    {
        ThrowIfShutdown();
        ArgumentNullException.ThrowIfNull(window);
        if (Context.Options.Headless)
        {
            throw new InvalidOperationException("headless Engine 不能接入窗口运行时。");
        }

        if (Context.TryGetService(out RenderWindow existing))
        {
            return ReferenceEquals(existing, window)
                ? existing
                : throw new InvalidOperationException("Engine 已接入另一个渲染窗口。");
        }

        if (takeOwnership)
        {
            _ownedRuntimeResources.Add(window);
        }

        Context.RegisterService(window);
        Context.Counters.VSyncEnabled = window.VSyncEnabled;
        _ = AttachCameraSynchronization(window);
        _ = AttachRendering(window);
        _ = AttachWindowInput(window, _ => ResolveGuiInputRoute());
        if (!_windowRuntimeAttached)
        {
            Phases.Register(EnginePhase.InputAndTime, _ =>
            {
                if (window.IsClosing)
                {
                    IsShutdownRequested = true;
                }
            });
            _windowRuntimeAttached = true;
        }

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
                existingDriver.AttachWindow(window, Context.Options.InternalWidth, Context.Options.InternalHeight);
                _ = existing.Sync(Context.Options.InternalWidth, Context.Options.InternalHeight);
            }

            return existing;
        }

        ScriptCameraApi camera = ResolveConcreteCameraApi();
        WorldManager? world = Context.TryGetService(out WorldManager registeredWorld)
            ? registeredWorld
            : null;
        ScriptCameraSynchronizer synchronizer = new(camera, world);
        ScriptCameraSyncPhaseDriver driver = new(
            synchronizer,
            window,
            window is null ? 0 : Context.Options.InternalWidth,
            window is null ? 0 : Context.Options.InternalHeight);
        Context.RegisterService(synchronizer);
        Context.RegisterService(driver.GetType(), driver);
        if (Context.TryGetService(out EngineProbeApi probe))
        {
            probe.AttachCameraSynchronizer(synchronizer);
        }
        driver.RegisterPhases(Phases);
        _ = synchronizer.Sync(
            window is null ? 0 : Context.Options.InternalWidth,
            window is null ? 0 : Context.Options.InternalHeight);
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
        ComputeFeatureSwitches computeFeatures = ComputeFeatureSwitches.Default with
        {
            GpuParticlesEnabled = Context.Options.EnableGpu,
        };
        RenderPipelineSettings renderSettings = new()
        {
            PreferComputeSharpBackend = Context.Options.PreferComputeSharpBackend,
        };
        RenderPipeline pipeline = new(
            window,
            Math.Max(1, Context.Options.InternalWidth),
            Math.Max(1, Context.Options.InternalHeight),
            computeFeatures,
            renderSettings);
        IDisplayMetricsSource displayMetricsSource = ResolveDisplayMetricsSource(window);
        GamePresentationCoordinator presentation = new(
            Context.Options.InternalWidth,
            Context.Options.InternalHeight,
            Context.Options.WindowWidth,
            Context.Options.WindowHeight,
            pipeline.MaximumTextureSize,
            displayMetricsSource,
            Context.TryGetService(out IGamePresentationOverride presentationOverride)
                ? presentationOverride
                : null);
        if (!Context.TryGetService(out IGameplayViewportInputMapper _))
        {
            Context.RegisterService<IGameplayViewportInputMapper>(
                new GamePresentationViewportInputMapper(window, presentation));
        }

        Context.Counters.FrameGpuTimerAvailable = pipeline.GpuFrameTimerAvailable;
        RenderPipelineFrameSink sink = new(pipeline, presentation);
        RenderPhaseDriver driver = new(
            Context.GetService<IChunkSource>(),
            simulation.Materials,
            simulation.Temperature,
            simulation.Particles,
            camera,
            lighting,
            sink,
            Context.Jobs,
            kernel: simulation.Kernel,
            physics: Context.TryGetService(out PhysicsSystem physics) ? physics : null,
            scriptOverlays: Context.TryGetService(out ScriptOverlayApi overlays) ? overlays : null,
            debugOverlays: ResolveDebugOverlayController());
        Context.RegisterService(pipeline);
        Context.RegisterService(presentation);
        Context.RegisterService<IGpuComputeQualityDegrader>(pipeline);
        Context.RegisterService<IRenderPresentationControl>(pipeline);
        Context.RegisterService(driver.RenderStyleQuality);
        Context.RegisterService<IRenderFrameSink>(sink);
        Context.RegisterService(sink);
        Context.RegisterService(driver.GetType(), driver);
        if (Context.TryGetService(out EngineProbeApi probe))
        {
            probe.AttachRendering(pipeline, driver);
        }
        driver.RegisterPhases(Phases);
        _ownedRuntimeResources.Add(pipeline);
        AttachGuiRuntime(window, pipeline);
        return driver;
    }

    // GUI 叠层装配：按脚本 GUI / GameUi / Editor 扩展需求惰性创建 UiLayerCompositor 与 GuiRenderBridge。
    private void AttachGuiRuntime(RenderWindow window, RenderPipeline pipeline)
    {
        IScriptRuntime? scriptRuntime = null;
        bool hasScriptGui = false;
        if (Context.Options.EnableGuiRuntime &&
            Context.TryGetService(out IScriptRuntime resolvedScriptRuntime))
        {
            scriptRuntime = resolvedScriptRuntime;
            hasScriptGui = true;
        }
        bool hasGameUi = Context.Options.EnableGameUi;
        GameUiCanvasRegistry? gameUi = null;
        if (hasGameUi)
        {
            gameUi = ResolveGameUiCanvasRegistry(window);
        }

        // Game UI 始终使用 Rendering-owned display metrics，所有 Canvas 在同一帧边界消费同一来源。
        IDisplayMetricsSource? displayMetricsSource = hasGameUi
            ? ResolveDisplayMetricsSource(window)
            : null;

        bool hasGuiBridge = Context.TryGetService(out GuiRenderBridge _);
        bool hasUiLayerCompositor = Context.TryGetService(out UiLayerCompositor _);
        IReadOnlyList<IEditorHostExtension>? extensions = null;
        IGameUiCompositionPolicy? gameUiCompositionPolicy =
            Context.TryGetService(out IGameUiCompositionPolicy registeredCompositionPolicy)
                ? registeredCompositionPolicy
                : null;
        bool hasEditorHostExtensions = false;
        if (!_editorHostExtensionsAttached &&
            Context.TryGetService(out IReadOnlyList<IEditorHostExtension> registeredExtensions) &&
            registeredExtensions.Count > 0)
        {
            extensions = registeredExtensions;
            hasEditorHostExtensions = true;
        }

        // 注册表可能同时包含 native Canvas 与 ManagedFallback Canvas，也可能在后续场景切换时改变组合；
        // 因此 direct present 与共享 ImGui bridge 都保持稳定挂载，由注册表按 backend kind 精确分流。
        bool gameUiNeedsPresentation = gameUi is not null;
        bool needsGuiBridge = hasScriptGui || gameUiNeedsPresentation;
        bool needsUiLayerCompositor = gameUiNeedsPresentation;

        if ((!needsGuiBridge || hasGuiBridge) &&
            (!needsUiLayerCompositor || hasUiLayerCompositor) &&
            !hasEditorHostExtensions)
        {
            return;
        }

        if (needsUiLayerCompositor && !hasUiLayerCompositor)
        {
            UiLayerCompositor compositor = UiLayerCompositor.Attach(
                pipeline,
                UiPresentSurface.RuntimeViewport,
                gameUi!,
                targetProvider: null,
                displayMetricsSource!,
                () => gameUiCompositionPolicy?.AllowsGameUiComposition ?? true);
            Context.RegisterService(compositor);
            _ownedRuntimeResources.Add(compositor);
        }

        GuiWindowInputConnector? input = null;
        bool guiInputOwned = false;
        if (needsGuiBridge && !hasGuiBridge)
        {
            GuiApp gui = ResolveGuiApp(window);
            IGuiViewportInputRoute? viewportInputRoute = gameUi is not null &&
                Context.TryGetService(out IGameUiPresentationInputMapper gameUiInputMapper) &&
                Context.TryGetService(out UiInputRouter gameUiInputRouter)
                ? new GameUiAwareGuiInputRoute(
                    gameUiInputMapper,
                    gameUi,
                    gameUiInputRouter)
                : Context.TryGetService(out IGameplayViewportInputMapper gameplayViewportMapper)
                    ? new GameplayViewportGuiInputRoute(gameplayViewportMapper)
                    : null;
            input = new GuiWindowInputConnector(window, gui.Input, viewportInputRoute);
            Action<IGuiDrawContext>? managedGui = null;
            if (gameUiNeedsPresentation)
            {
                managedGui = gui =>
                {
                    if (gameUiCompositionPolicy?.AllowsGameUiComposition ?? true)
                    {
                        gameUi!.DrawGui(gui);
                    }
                };
            }

            Action<UiPresentTarget>? gameUiTargetFrame = gameUiNeedsPresentation
                ? target => ResizeGameUiAtFrameBoundary(gameUi!, target, displayMetricsSource!)
                : null;
            GuiRenderBridge? bridge = GuiRenderBridge.AttachIfEnabled(
                pipeline,
                UiPresentSurface.RuntimeViewport,
                gui,
                scriptRuntime,
                managedGui,
                presentTargetChanged: null,
                gameUiTargetFrame);
            if (bridge is not null)
            {
                Context.RegisterService(bridge);
                _ownedRuntimeResources.Add(bridge);
                guiInputOwned = true;
            }
        }

        if (hasEditorHostExtensions)
        {
            Debug.Assert(extensions is not null);
            AttachEditorHostExtensions(extensions, window, pipeline);
        }

        if (input is null)
        {
            return;
        }

        if (guiInputOwned)
        {
            _ownedRuntimeResources.Add(input);
        }
        else
        {
            input.Dispose();
        }
    }

    private void AttachEditorHostExtensions(
        IReadOnlyList<IEditorHostExtension> extensions,
        RenderWindow window,
        RenderPipeline pipeline)
    {
        for (int i = 0; i < extensions.Count; i++)
        {
            IDisposable? attached = extensions[i].Attach(this, window, pipeline);
            if (attached is not null)
            {
                _ownedRuntimeResources.Add(attached);
            }
        }

        _editorHostExtensionsAttached = true;
    }

    private GuiApp ResolveGuiApp(RenderWindow window)
    {
        if (Context.TryGetService(out GuiApp existing))
        {
            if (Context.TryGetService(out EngineProbeApi existingProbe))
            {
                existingProbe.AttachGui(existing);
            }

            return existing;
        }

        GuiApp created = new(
            new HexaImGuiBackend(window),
            new GuiAppOptions
            {
                Enabled = true,
                LayoutPath = Context.Options.GuiLayoutPath,
            });

        Context.RegisterService(created);
        if (Context.TryGetService(out EngineProbeApi probe))
        {
            probe.AttachGui(created);
        }

        _ownedRuntimeResources.Add(created);
        return created;
    }

    // GameUi Canvas 注册表解析：每个场景 Canvas 创建独立后端/文档栈，RmlUi 初始化失败逐 Canvas 回退。
    private GameUiCanvasRegistry ResolveGameUiCanvasRegistry(RenderWindow window)
    {
        if (Context.TryGetService(out GameUiCanvasRegistry existing))
        {
            return existing;
        }

        FontEngine fontEngine = new(new FontEngineOptions(Path.Combine(Context.Options.ContentRoot, "ui")));
        UiFontSelection fontSelection = fontEngine.Resolve();
        UiBackendKind requestedBackend = Context.Options.GameUiBackend;
        UiBackendKind activeBackend = requestedBackend;
        string? selectionFallbackReason = null;
        string? selectionNativeProfile = null;
        bool selectionCaptured = false;
        UiStringPool strings = new();
        IDisplayMetricsSource displayMetricsSource = ResolveDisplayMetricsSource(window);

        GameUiHost CreateCanvasHost(EngineSceneCanvasDefinition definition)
        {
            IGameUiBackend backend = CreateGameUiBackend(
                window,
                requestedBackend,
                strings,
                out string? fallbackReason,
                out string? activeNativeProfile);
            GameUiHost host = new(backend);
            try
            {
                InitializeGameUiHost(
                    host,
                    backend,
                    window,
                    fontSelection,
                    displayMetricsSource.Current,
                    definition.ScalerSettings);
            }
            // native 库缺失、入口点不匹配或 GL 初始化失败时只降级当前 Canvas。
            catch (Exception ex) when (backend.Kind == UiBackendKind.RmlUi && IsRmlUiFallbackException(ex))
            {
                fallbackReason = $"RmlUi 初始化失败，回退 ManagedFallback：{ex.GetType().Name}: {ex.Message}";
                activeNativeProfile = null;
                host.Dispose();
                backend = CreateManagedFallbackGameUiBackend(window);
                host = new GameUiHost(backend);
                InitializeGameUiHost(
                    host,
                    backend,
                    window,
                    fontSelection,
                    displayMetricsSource.Current,
                    definition.ScalerSettings);
            }

            if (!selectionCaptured || definition.IsPrimary)
            {
                activeBackend = backend.Kind;
                selectionFallbackReason = fallbackReason;
                selectionNativeProfile = activeNativeProfile;
                selectionCaptured = true;
            }

            return host;
        }

        Func<string, string?>? manifestAssetResolver = null;
        if (Context.TryGetService(out IGameUiManifestAssetResolver registeredManifestResolver))
        {
            manifestAssetResolver = assetId => registeredManifestResolver.TryResolveManifest(assetId, out string path)
                ? path
                : null;
        }

        GameUiCanvasRegistry registry = new(
            Context.Options.ContentRoot,
            CreateCanvasHost,
            strings,
            manifestAssetResolver);
        EngineSceneCanvasSet canvasSet = _sceneCanvasSet ?? ResolveCurrentSceneCanvasSet();
        registry.Configure(canvasSet);
        _sceneCanvasSet = canvasSet;

        Context.RegisterService(fontEngine);
        GameUiBackendSelection backendSelection = new(
            requestedBackend,
            activeBackend,
            selectionFallbackReason,
            selectionNativeProfile);
        Context.RegisterService(backendSelection);
        Context.RegisterService(registry);
        SynchronizeLegacyGameUiHostService(registry);

        RenderWindowUiInputSource platformInputSource = new(window);
        IUiInputSource inputSource = platformInputSource;
        if (Context.TryGetService(out IGameUiInputSourceFactory inputSourceFactory))
        {
            inputSource = inputSourceFactory.CreateGameUiInputSource(window, inputSource);
        }
        else if (Context.TryGetService(out GamePresentationCoordinator presentation))
        {
            inputSource = new GamePresentationUiInputSource(inputSource, window, presentation);
        }

        UiInputRouter inputRouter = new(registry, inputSource);
        inputRouter.TextCompositionCapabilities.Validate();
        if (inputSource is IGameUiPresentationInputMapper gameUiInputMapper &&
            !Context.TryGetService(out IGameUiPresentationInputMapper _))
        {
            Context.RegisterService<IGameUiPresentationInputMapper>(gameUiInputMapper);
        }

        Context.RegisterService(inputRouter.TextCompositionCapabilities);
        Context.RegisterService(inputRouter);
        GameUiServiceBridge service = new(registry);
        Context.RegisterService(service);
        Context.RegisterService<IGameUiService>(service);
        // Editor 会先接入脚本运行时以便 GuiRenderBridge 同帧绘制 OnGui，再创建窗口 Game UI。
        // 把晚到的真实服务回填进既有脚本上下文，避免 Behaviour 永久抓住 NoopGameUiService。
        if (Context.TryGetService(out ScriptSimulationContext scriptContext))
        {
            scriptContext.AttachGameUiService(service);
            if (Context.TryGetService(out ScriptEventBus scriptEvents))
            {
                service.AttachScriptEventBus(scriptEvents);
            }
        }

        GameUiPhaseDriver driver = new(
            registry,
            eventSink: service,
            modelPusher: service,
            runtimePolicy: Context.TryGetService(out IGameUiCompositionPolicy runtimePolicy)
                ? runtimePolicy
                : null);
        if (Context.TryGetService(out EngineProbeApi probe))
        {
            probe.AttachGameUi(registry, in backendSelection, inputRouter, driver, platformInputSource);
        }

        Context.RegisterService(driver.GetType(), driver);
        driver.RegisterPhases(Phases);
        _ownedRuntimeResources.Add(registry);
        _ownedRuntimeResources.Add(platformInputSource);
        return registry;
    }

    // 按请求后端类型选择实现；RmlUi 需通过 native profile gate 与 DLL 探针双重校验。
    private IGameUiBackend CreateGameUiBackend(
        RenderWindow window,
        UiBackendKind requestedBackend,
        UiStringPool strings,
        out string? fallbackReason,
        out string? activeNativeProfile)
    {
        fallbackReason = null;
        activeNativeProfile = null;
        if (requestedBackend == UiBackendKind.ManagedFallback)
        {
            return CreateManagedFallbackGameUiBackend(window);
        }

        if (requestedBackend == UiBackendKind.RmlUi)
        {
            RmlUiNativeProfileDecision decision = RmlUiNativeProfileGate.Evaluate(window.Backend, window.Capabilities);
            if (!decision.CanUseNativeRenderer)
            {
                return CreateManagedFallbackGameUiBackend(
                    window,
                    out fallbackReason,
                    decision.FallbackReason ?? "RmlUi native profile gate 拒绝当前上下文，回退 ManagedFallback。");
            }

            if (!RmlUiNativeInfo.TryQuery(out RmlUiNativeProbe probe))
            {
                return CreateManagedFallbackGameUiBackend(
                    window,
                    out fallbackReason,
                    $"RmlUi native 不可用：{probe.Error ?? "unknown"}。");
            }

            activeNativeProfile =
                $"{decision.NativeRendererSymbol}; {decision.ShaderVersionDirective}; profileId={RmlUiNativeProfileGate.ToNativeProfileId(decision.RequestedProfile)}";
            return new RmlUiBackend(window, stringResolver: strings);
        }

        return requestedBackend == UiBackendKind.Ultralight
            ? CreateManagedFallbackGameUiBackend(
                window,
                out fallbackReason,
                UltralightOptionalProfileGate.InactiveReason)
            : throw new ArgumentOutOfRangeException(nameof(requestedBackend), requestedBackend, "未知游戏 UI 后端。");
    }

    private ManagedFallbackBackend CreateManagedFallbackGameUiBackend(RenderWindow window)
    {
        return new ManagedFallbackBackend(new GuiAppManagedFallbackHost(ResolveGuiApp(window), window));
    }

    private ManagedFallbackBackend CreateManagedFallbackGameUiBackend(RenderWindow window, out string? fallbackReason, string reason)
    {
        fallbackReason = reason;
        return CreateManagedFallbackGameUiBackend(window);
    }

    private static void InitializeGameUiHost(
        GameUiHost host,
        IGameUiBackend backend,
        RenderWindow window,
        UiFontSelection fontSelection,
        DisplayMetricsSnapshot displayMetrics,
        UiCanvasScalerSettings canvasScalerSettings)
    {
        UiDisplayMetrics uiDisplayMetrics = UiDisplayMetrics.FromRendering(
            Math.Max(1, window.Width),
            Math.Max(1, window.Height),
            in displayMetrics);
        host.Initialize(new UiBackendInitializeInfo(
            uiDisplayMetrics,
            canvasScalerSettings,
            backend.Kind,
            fontSelection));
    }

    private IDisplayMetricsSource ResolveDisplayMetricsSource(RenderWindow window)
    {
        if (Context.TryGetService(out IDisplayMetricsSource existing))
        {
            return existing;
        }

        IDisplayMetricsSource source = new RenderWindowDisplayMetricsSource(window);
        Context.RegisterService<IDisplayMetricsSource>(source);
        return source;
    }

    private static void ResizeGameUiAtFrameBoundary(
        GameUiCanvasRegistry registry,
        UiPresentTarget target,
        IDisplayMetricsSource displayMetricsSource)
    {
        DisplayMetricsSnapshot snapshot = displayMetricsSource.CommitFrameBoundary();
        UiDisplayMetrics displayMetrics = UiDisplayMetrics.FromRendering(
            target.Width,
            target.Height,
            in snapshot);
        registry.Resize(in displayMetrics);
    }

    private static bool IsRmlUiFallbackException(Exception ex)
    {
        return ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException;
    }

    // 输入仲裁：Editor 门控 → GameUi 当前帧 pump → shared/runtime Gui 捕获 → 世界脚本。
    // ManagedFallback 本身绘制在 shared Gui 中，若先应用 Gui capture 会形成“捕获后禁止自身 pump”的死锁。
    private ScriptInputRoute ResolveGuiInputRoute()
    {
        InputArbitrationState input = ApplyEditorInputCapture(InputArbitrationState.Allowed);
        if (Context.TryGetService(out UiInputRouter router))
        {
            UiInputCapture uiCapture = router.Pump(
                allowPointer: input.AllowWorldMouse,
                allowKeyboard: input.AllowWorldKeyboard);
            input = InputArbitrator.ApplyGameUi(input, uiCapture);
        }

        if (Context.TryGetService(out GuiApp gui))
        {
            input = InputArbitrator.ApplyGui(input, gui.Input.Capture);
        }

        return input.ToScriptInputRoute();
    }

    private InputArbitrationState ApplyEditorInputCapture(InputArbitrationState input)
    {
        return Context.TryGetService(out IEditorInputCaptureSource editorInput) &&
            editorInput.TryGetInputCapture(out EditorHostInputCapture editorCapture)
            ? InputArbitrator.ApplyEditor(input, editorCapture)
            : input;
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
        if (Context.TryGetService(out EngineProbeApi probe))
        {
            probe.AttachLightingSynchronizer(synchronizer);
        }
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

        ResetRestartSnapshot();
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
        RigidDamageQueue damageQueue = ResolveRigidDamageQueue();
        PhysicsStepEventBus physicsEvents = Context.TryGetService(out PhysicsStepEventBus existingPhysicsEvents)
            ? existingPhysicsEvents
            : new PhysicsStepEventBus();
        ParticleSystem? particles = Context.TryGetService(out ParticleSystem registeredParticles)
            ? registeredParticles
            : null;
        RigidBodyDestruction destruction = new(fragmentPixelThreshold: 4, particles);
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 10f };
        PhysicsSystem physics = PhysicsSystem.Initialize(
            grid,
            Context.Jobs,
            damageQueue: damageQueue,
            destruction: destruction,
            profiler: Context.Profiler,
            eventBus: Context.Events,
            worldDef: worldDef);
        PhysicsPhaseDriver driver = new(physics, chunks);
        Context.RegisterService(damageQueue);
        Context.RegisterService<IPhysicsStepEvents>(physicsEvents);
        Context.RegisterService(physicsEvents);
        Context.RegisterService(physics);
        Context.RegisterService(EngineServiceRole.PhysicsService, physics);
        Context.RegisterService(driver.GetType(), driver);
        if (Context.TryGetService(out EngineProbeApi probe))
        {
            probe.AttachPhysics(physics);
        }
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

        ResetRestartSnapshot();
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
    /// 按当前场景来源显式装配初始世界；SaveDirectory 直接读档，SceneFile 读取 InitialSaveDirectory，Procedural 调用已注册生成器。
    /// </summary>
    /// <param name="particleCapacity">自由粒子池容量，必须能容纳存档中的在飞粒子。</param>
    /// <param name="fallbackMaterialId">存档材质名在当前材质表中缺失时使用的 fallback 材质 id。</param>
    /// <param name="streamingConfig">可选世界流式配置。</param>
    /// <param name="proceduralWorldRoot">无限程序化世界持久化根目录；为空时使用 LocalApplicationData。</param>
    /// <returns>装配了存档世界时返回读档结果；当前场景没有存档来源时返回 null。</returns>
    public WorldLoadResult? AttachCurrentSceneWorld(
        int particleCapacity = 32768,
        ushort fallbackMaterialId = 0,
        WorldStreamingConfig? streamingConfig = null,
        string? proceduralWorldRoot = null)
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
                scene,
                scene.ResolvedSource ?? throw new InvalidOperationException(".scene 场景缺少解析后的文件路径。"),
                particleCapacity,
                fallbackMaterialId,
                streamingConfig,
                proceduralWorldRoot),
            SceneSourceKind.Procedural => AttachCurrentProceduralSceneWorld(
                scene,
                particleCapacity,
                fallbackMaterialId,
                streamingConfig,
                proceduralWorldRoot),
            SceneSourceKind.Empty => null,
            _ => throw new ArgumentOutOfRangeException(nameof(scene), scene.Descriptor.SourceKind, "未知场景来源类型。"),
        };
    }

    /// <summary>
    /// 在当前暂停点捕获 resident world 快照；供 Play/Edit 回滚或关卡重开使用。
    /// </summary>
    /// <returns>需要由调用方释放的临时世界快照。</returns>
    public EngineWorldSnapshot CaptureWorldSnapshot()
    {
        ThrowIfShutdown();
        string snapshotPath = Path.Combine(
            Path.GetTempPath(),
            "PixelEngine",
            "world-snapshots",
            Guid.NewGuid().ToString("N"));
        _ = SaveWorldToDirectory(snapshotPath);
        SimulationKernel kernel = Context.GetService<SimulationKernel>();
        return new EngineWorldSnapshot(snapshotPath, Context.Clock.SimTickIndex, kernel.WorldSeed);
    }

    /// <summary>
    /// 将当前 resident world 持久保存到指定目录；目录内容使用 plan/07 world save 格式。
    /// </summary>
    /// <param name="savePath">目标存档目录。</param>
    /// <returns>已原子发布的目录与可选延迟清理 journal。</returns>
    public WorldSaveWriteResult SaveWorldToDirectory(string savePath)
    {
        ThrowIfShutdown();
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);
        WorldSaveService service = new();
        WorldSaveSnapshot snapshot = CaptureWorldSaveSnapshot(service);
        WorldSaveWriteResult result = service.WriteSnapshot(snapshot, Path.GetFullPath(savePath));
        _ = MarkWorldSnapshotPersisted(snapshot);
        return result;
    }

    /// <summary>
    /// 在当前 world 安全点冻结完整 resident world 深快照；快照可交给后台线程编码，
    /// 也可用于失败回滚与 Undo/Redo。
    /// </summary>
    /// <param name="cancellationToken">在 resident chunk 间响应的取消令牌。</param>
    /// <returns>不再引用 Engine 权威可变对象的完整快照。</returns>
    public WorldSaveSnapshot CaptureWorldSaveSnapshot(CancellationToken cancellationToken = default)
    {
        ThrowIfShutdown();
        return CaptureWorldSaveSnapshot(new WorldSaveService(), cancellationToken);
    }

    /// <summary>
    /// 在当前 world 安全点应用完整快照，并同步随机种子、tick 与 parity。
    /// 本方法不执行磁盘 I/O；调用者可用另一份快照实现回滚或 Undo/Redo。
    /// </summary>
    /// <param name="snapshot">已完全冻结或后台解码的世界快照。</param>
    /// <returns>应用后的世界摘要。</returns>
    public WorldLoadResult ApplyWorldSaveSnapshot(WorldSaveSnapshot snapshot)
    {
        ThrowIfShutdown();
        ArgumentNullException.ThrowIfNull(snapshot);
        WorldSaveService service = new();
        WorldLoadResult result = service.ApplySnapshot(
            snapshot,
            CreateWorldLoadContext(fallbackMaterialId: 0),
            ResolveRuntimeWorldStateBridge());
        RestoreWorldTimeline(result, snapshot.CurrentParity);
        return result;
    }

    /// <summary>在存档目录成功发布后清除该快照覆盖 chunk 的流式 dirty 标记。</summary>
    /// <param name="snapshot">已成功持久化的完整 world 快照。</param>
    /// <returns>至少一个 chunk 的 dirty 状态发生变化时返回 <see langword="true" />。</returns>
    public bool MarkWorldSnapshotPersisted(WorldSaveSnapshot snapshot)
    {
        ThrowIfShutdown();
        ArgumentNullException.ThrowIfNull(snapshot);
        ResidentChunkMap chunks = Context.GetService<ResidentChunkMap>();
        return WorldSaveService.MarkSnapshotPersisted(ResolveSnapshotResidency(chunks), snapshot);
    }

    /// <summary>恢复 world 快照捕获时的流式 dirty before-image，供保存失败回滚与 Undo。</summary>
    /// <param name="snapshot">保存前捕获的完整 world 快照。</param>
    /// <returns>至少一个 chunk 的 dirty 状态发生变化时返回 <see langword="true" />。</returns>
    public bool RestoreWorldSnapshotPersistenceState(WorldSaveSnapshot snapshot)
    {
        ThrowIfShutdown();
        ArgumentNullException.ThrowIfNull(snapshot);
        ResidentChunkMap chunks = Context.GetService<ResidentChunkMap>();
        return WorldSaveService.RestoreSnapshotPersistenceState(
            ResolveSnapshotResidency(chunks),
            snapshot);
    }

    private WorldSaveSnapshot CaptureWorldSaveSnapshot(
        WorldSaveService service,
        CancellationToken cancellationToken = default)
    {
        ResidentChunkMap chunks = Context.GetService<ResidentChunkMap>();
        TemperatureField temperature = Context.GetService<TemperatureField>();
        MaterialTable materials = Context.GetService<MaterialTable>();
        SimulationKernel kernel = Context.GetService<SimulationKernel>();
        RuntimeWorldStateBridge stateBridge = ResolveRuntimeWorldStateBridge();
        ResidencyTable residency = ResolveSnapshotResidency(chunks);
        long gameTimeTicks = Context.Clock.SimTickIndex;
        return service.CaptureSnapshot(
            new WorldSaveContext(
                chunks,
                residency,
                temperature,
                materials,
                kernel.WorldSeed,
                gameTimeTicks,
                ReadOnlyMemory<byte>.Empty,
                isFrameBoundary: true),
            stateBridge,
            kernel.CurrentParity,
            cancellationToken);
    }

    /// <summary>
    /// 将当前 resident world 恢复到指定快照状态。
    /// </summary>
    /// <param name="snapshot">由 <see cref="CaptureWorldSnapshot" /> 创建的世界快照。</param>
    /// <param name="fallbackMaterialId">快照材质名在当前材质表中缺失时使用的 fallback 材质 id。</param>
    /// <returns>读档恢复结果。</returns>
    public WorldLoadResult RestoreWorldSnapshot(EngineWorldSnapshot snapshot, ushort fallbackMaterialId = 0)
    {
        ThrowIfShutdown();
        ArgumentNullException.ThrowIfNull(snapshot);
        return LoadWorldFromDirectory(snapshot.DirectoryPath, fallbackMaterialId);
    }

    /// <summary>
    /// 从指定 plan/07 world save 目录覆盖恢复当前 resident world，并同步 kernel 与帧时钟计数。
    /// </summary>
    /// <param name="savePath">包含 manifest.bin 与 regions/ 的世界存档目录。</param>
    /// <param name="fallbackMaterialId">快照材质名在当前材质表中缺失时使用的 fallback 材质 id。</param>
    /// <returns>读档恢复结果。</returns>
    public WorldLoadResult LoadWorldFromDirectory(string savePath, ushort fallbackMaterialId = 0)
    {
        ThrowIfShutdown();
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);
        MaterialTable materials = Context.GetService<MaterialTable>();
        _ = materials.GetName(fallbackMaterialId);
        WorldSaveService service = new();
        WorldSaveSnapshot loaded = service.ReadSnapshot(
            Path.GetFullPath(savePath),
            new MaterialNameTable(materials.BuildIdNameTable()),
            fallbackMaterialId);
        WorldSaveSnapshot before = CaptureWorldSaveSnapshot(service);
        try
        {
            WorldLoadResult result = service.ApplySnapshot(
                loaded,
                CreateWorldLoadContext(fallbackMaterialId),
                ResolveRuntimeWorldStateBridge());
            RestoreWorldTimeline(result, loaded.CurrentParity);
            return result;
        }
        catch (Exception operationException)
        {
            try
            {
                WorldLoadResult restored = service.ApplySnapshot(
                    before,
                    CreateWorldLoadContext(fallbackMaterialId: 0),
                    ResolveRuntimeWorldStateBridge());
                RestoreWorldTimeline(restored, before.CurrentParity);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "World load 应用失败且 before-image 回滚失败。",
                    operationException,
                    rollbackException);
            }

            throw;
        }
    }

    private WorldLoadContext CreateWorldLoadContext(ushort fallbackMaterialId)
    {
        MaterialTable materials = Context.GetService<MaterialTable>();
        _ = materials.GetName(fallbackMaterialId);
        ResidentChunkMap chunks = Context.GetService<ResidentChunkMap>();
        return new WorldLoadContext(
            chunks,
            ResolveSnapshotResidency(chunks),
            Context.GetService<TemperatureField>(),
            materials,
            fallbackMaterialId,
            Context.GetService<SimulationKernel>().CurrentParity);
    }

    private RuntimeWorldStateBridge ResolveRuntimeWorldStateBridge()
    {
        return EnsureRuntimeWorldStateBridge(Context.GetService<ParticleSystem>());
    }

    private void RestoreWorldTimeline(WorldLoadResult result, byte currentParity)
    {
        Context.GetService<SimulationKernel>().RestoreWorldState(
            result.WorldSeed,
            checked((uint)result.GameTimeTicks),
            currentParity);
        Context.Clock.RestoreCounters(result.GameTimeTicks, result.GameTimeTicks);
    }

    /// <summary>
    /// 按当前程序化场景的生成器键装配有限 resident world 或流式无限 world。
    /// </summary>
    /// <param name="particleCapacity">自由粒子池容量。</param>
    /// <param name="fallbackMaterialId">流式存档中材质缺失时使用的 fallback id。</param>
    /// <param name="streamingConfig">无限世界的可选流式配置。</param>
    /// <param name="proceduralWorldRoot">无限世界持久化根目录；为空时使用 LocalApplicationData。</param>
    /// <returns>成功装配程序化世界时返回 true；当前场景不是程序化来源时返回 false。</returns>
    public bool AttachProceduralSceneWorld(
        int particleCapacity = 32768,
        ushort fallbackMaterialId = 0,
        WorldStreamingConfig? streamingConfig = null,
        string? proceduralWorldRoot = null)
    {
        ThrowIfShutdown();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(particleCapacity);
        Scene scene = Context.GetService<ISceneService>().Current ??
            throw new InvalidOperationException("当前没有已加载场景，不能装配程序化世界。");
        return AttachProceduralSceneWorld(
            scene,
            particleCapacity,
            fallbackMaterialId,
            streamingConfig,
            proceduralWorldRoot);
    }

    /// <summary>
    /// 切换到已注册场景描述；实际世界构建由对应场景后端在后续装配中完成。
    /// </summary>
    /// <param name="name">场景稳定名称。</param>
    /// <returns>当前场景实例。</returns>
    public Scene LoadScene(string name)
    {
        ThrowIfShutdown();
        ResetRestartSnapshot();
        Scene scene = Context.GetService<ISceneService>().SwitchTo(name);
        MaterializeSceneScripts(scene);
        MaterializeSceneCanvases(scene);
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
        ResetRestartSnapshot();
        ScriptingPhaseDriver driver = new(runtime, scriptContext);
        driver.RegisterPhases(Phases);
        _attachedScriptRuntime = runtime;
        Context.RegisterService(EngineServiceRole.Scripting, runtime);
        Context.RegisterService(scriptContext);
    }

    /// <summary>
    /// 将外部编辑态物化出的脚本 Scene 接到当前 Hosting 场景，并注册为后续脚本运行时的输入。
    /// 已接入脚本运行时后再次调用会替换编辑态 authoring projection，并同步默认脚本运行时引用。
    /// </summary>
    /// <param name="scriptScene">已由调用方物化好的脚本 Scene。</param>
    public void AttachScriptScene(Scripting.Scene scriptScene)
    {
        ThrowIfShutdown();
        ArgumentNullException.ThrowIfNull(scriptScene);
        Scene current = Context.GetService<ISceneService>().Current ??
            throw new InvalidOperationException("当前没有已加载场景，不能接入脚本 Scene。");
        Scripting.Scene? previous = current.ScriptScene;
        bool replacing = previous is not null && !ReferenceEquals(previous, scriptScene);
        if (replacing && _attachedScriptRuntime is not null && _attachedScriptRuntime is not ScriptRuntime)
        {
            throw new InvalidOperationException("当前脚本运行时不支持替换脚本 Scene。");
        }

        if (replacing)
        {
            _attachedScriptRuntime?.EndPlaySession();
        }

        current.AttachScriptScene(scriptScene);
        Context.RegisterService(scriptScene);
        if (replacing && _attachedScriptRuntime is ScriptRuntime scriptRuntime)
        {
            scriptRuntime.ReplaceScene(scriptScene);
        }
    }

    /// <summary>
    /// 在编辑态显式应用待处理脚本热重载，复用 Hosting-owned runtime 的程序集注册与诊断路径。
    /// </summary>
    /// <returns>热重载应用结果。</returns>
    public ScriptHotReloadApplyResult ApplyPendingScriptHotReload()
    {
        ThrowIfShutdown();
        return _attachedScriptRuntime is ScriptRuntime scriptRuntime
            ? scriptRuntime.ApplyPendingReload()
            : new ScriptHotReloadApplyResult(
                ScriptHotReloadStatus.NoPendingReload,
                [],
                OldContextUnloaded: true,
                LoadedAssembly: null);
    }

    internal void EndScriptPlaySession()
    {
        ThrowIfShutdown();
        _attachedScriptRuntime?.EndPlaySession();
    }

    internal ScriptPlaySessionSnapshot? CaptureScriptPlaySessionSnapshot()
    {
        ThrowIfShutdown();
        return _attachedScriptRuntime?.CapturePlaySessionSnapshot();
    }

    internal void RestoreScriptPlaySessionSnapshot(ScriptPlaySessionSnapshot? snapshot)
    {
        ThrowIfShutdown();
        if (snapshot is not null)
        {
            _attachedScriptRuntime?.RestorePlaySessionSnapshot(snapshot);
        }
    }

    /// <summary>
    /// 从 Hosting 已注册的真实 Simulation/Physics/Audio/Input/Camera 服务创建脚本上下文并接入相位管线。
    /// </summary>
    /// <param name="runtime">可选脚本运行时；为 null 时创建默认 <see cref="ScriptRuntime" />。</param>
    /// <param name="hotReload">可选热重载源目录；提供时由默认运行时在相位 1 应用变更。</param>
    /// <returns>已接入的真实 Simulation 脚本上下文。</returns>
    public ScriptSimulationContext AttachScriptingFromServices(
        IScriptRuntime? runtime = null,
        ScriptHotReloadRuntimeOptions? hotReload = null)
    {
        ThrowIfShutdown();
        if (_attachedScriptRuntime is not null)
        {
            throw new InvalidOperationException("脚本运行时已经接入当前 Engine。");
        }

        if (runtime is not null && hotReload is not null)
        {
            throw new ArgumentException("自定义脚本运行时不能同时由 Hosting 自动接入热重载。", nameof(hotReload));
        }

        // 从已注册服务物化脚本上下文：优先复用 Simulation 相位驱动中的真实世界句柄。
        Scripting.Scene scriptScene = ResolveCurrentScriptScene();
        SimulationPhaseDriver? simulationDriver = Context.TryGetService(out SimulationPhaseDriver driver)
            ? driver
            : null;
        CellGrid grid = ResolveCellGrid(simulationDriver);
        SimulationKernel kernel = ResolveSimulationKernel(simulationDriver);
        TemperatureField? temperature = ResolveTemperatureFieldOrNull(simulationDriver);
        ParticleSystem particles = ResolveParticleSystem(simulationDriver);
        MaterialTable materials = ResolveMaterialTable(simulationDriver);
        ScriptEventBus events = ResolveScriptEventBus();
        ScriptFrameTime time = ResolveScriptFrameTime();
        ICameraApi camera = ResolveCameraApi();
        IInputApi input = ResolveInputApi();
        ILightingApi lighting = ResolveLightingApi();
        IOverlayApi overlay = ResolveOverlayApi();
        IDiagnosticsApi diagnostics = ResolveDiagnosticsApi();
        IRuntimeControlApi runtimeControl = ResolveRuntimeControlApi();
        IAudioApi? audio = ResolveAudioApiOrNull();
        IGameUiService? gameUi = ResolveGameUiServiceOrNull();
        if (gameUi is GameUiServiceBridge gameUiBridge)
        {
            gameUiBridge.AttachScriptEventBus(events);
        }

        IConfigApi config = ResolveConfigApi();
        PhysicsSystem? physics = Context.TryGetService(out PhysicsSystem registeredPhysics)
            ? registeredPhysics
            : null;
        IPhysicsStepEvents physicsEvents = Context.TryGetService(out IPhysicsStepEvents registeredPhysicsEvents)
            ? registeredPhysicsEvents
            : NoopPhysicsStepEvents.Instance;
        ScriptCameraSyncPhaseDriver? existingCameraSyncDriver =
            camera is ScriptCameraApi && Context.TryGetService(out ScriptCameraSyncPhaseDriver registeredCameraSync)
                ? registeredCameraSync
                : null;
        ScriptLightingSyncPhaseDriver? existingLightingSyncDriver =
            lighting is ScriptLightingApi && Context.TryGetService(out ScriptLightingSyncPhaseDriver registeredLightingSync)
                ? registeredLightingSync
                : null;

        RegisterSimulationRolesIfMissing(grid, particles, materials, physics);
        ScriptSimulationContext scriptContext = new(
            scriptScene,
            grid,
            kernel,
            particles,
            materials,
            temperature,
            events,
            time,
            audio,
            physics,
            camera,
            input,
            lighting,
            overlay,
            diagnostics,
            runtimeControl,
            gameUi,
            config,
            physicsEvents);
        Context.RegisterService(scriptContext);
        if (Context.TryGetService(out EngineProbeApi probe))
        {
            probe.AttachScriptContext(scriptContext);
        }
        simulationDriver?.AttachScriptContext(scriptContext);
        runtime ??= CreateScriptRuntime(scriptScene, scriptContext, hotReload);
        AttachScripting(scriptContext, runtime);
        RegisterLateScriptSynchronizers(existingCameraSyncDriver, existingLightingSyncDriver);
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

    private void RegisterLateScriptSynchronizers(
        ScriptCameraSyncPhaseDriver? cameraSyncDriver,
        ScriptLightingSyncPhaseDriver? lightingSyncDriver)
    {
        // 窗口运行时可能在脚本接入前就绑定了相机/光照驱动；此处二次注册以保证它们在脚本 Update 之后同步。
        cameraSyncDriver?.RegisterPhases(Phases);
        lightingSyncDriver?.RegisterPhases(Phases);
    }

    private IScriptRuntime CreateScriptRuntime(
        Scripting.Scene scriptScene,
        IScriptContext scriptContext,
        ScriptHotReloadRuntimeOptions? hotReload)
    {
        if (hotReload is null)
        {
            return new ScriptRuntime();
        }

        IScriptHotReloadDiagnosticSink? diagnosticSink = Context.TryGetService(out IScriptHotReloadDiagnosticSink registeredDiagnosticSink)
            ? registeredDiagnosticSink
            : null;
        ScriptHotReloadController controller = new(scriptScene, scriptContext);
        try
        {
            controller.StartWatching(
                hotReload.AssemblyName,
                hotReload.SourceDirectory,
                hotReload.PreserveState,
                hotReload.SearchPattern,
                hotReload.IncludeSubdirectories,
                hotReload.DebounceInterval);
            diagnosticSink?.Report(new ScriptHotReloadDiagnostic(
                DateTimeOffset.UtcNow,
                ScriptHotReloadDiagnosticKind.WatcherStarted,
                ScriptHotReloadStatus.NoPendingReload,
                $"脚本热重载监听已启动：{hotReload.SourceDirectory}",
                []));
        }
        catch (Exception ex) when (IsNonFatalWatcherStartFailure(ex))
        {
            diagnosticSink?.Report(new ScriptHotReloadDiagnostic(
                DateTimeOffset.UtcNow,
                ScriptHotReloadDiagnosticKind.WatcherStartFailed,
                ScriptHotReloadStatus.NoPendingReload,
                $"脚本热重载监听启动失败（WatcherStartFailed），将继续运行无 watcher 的脚本运行时：{ex.Message}",
                [ex.ToString()]));
        }

        Context.RegisterService(controller);
        ScriptAssemblyRegistry scriptAssemblies = Context.GetService<ScriptAssemblyRegistry>();
        return new ScriptRuntime(controller, diagnosticSink, scriptAssemblies.RegisterOrReplaceByName);
    }

    private static bool IsNonFatalWatcherStartFailure(Exception exception)
    {
        return exception is DirectoryNotFoundException or IOException or UnauthorizedAccessException or NotSupportedException;
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
            Scripting.Scene scriptScene = EngineSceneDocumentLoader.Load(
                scene.ResolvedSource,
                Context.GetService<ScriptAssemblyRegistry>());
            scene.AttachScriptScene(scriptScene);
            Context.RegisterService(scriptScene);
            return;
        }

        if (scene.Descriptor.SourceKind == SceneSourceKind.Procedural && scene.ResolvedSource is not null)
        {
            Scripting.Scene scriptScene = BuildProceduralScriptScene(
                scene.ResolvedSource,
                Context.GetService<ScriptAssemblyRegistry>());
            scene.AttachScriptScene(scriptScene);
            Context.RegisterService(scriptScene);
        }
    }

    private EngineSceneCanvasSet ResolveCurrentSceneCanvasSet()
    {
        ISceneService scenes = Context.GetService<ISceneService>();
        return scenes.Current is Scene current
            ? ResolveSceneCanvasSet(current)
            : CreateImplicitCanvasSet("Runtime");
    }

    private void MaterializeSceneCanvases(Scene scene)
    {
        EngineSceneCanvasSet canvasSet = ResolveSceneCanvasSet(scene);
        if (Context.TryGetService(out GameUiCanvasRegistry registry))
        {
            registry.Configure(canvasSet);
            SynchronizeLegacyGameUiHostService(registry);
        }

        _sceneCanvasSet = canvasSet;
    }

    private static EngineSceneCanvasSet ResolveSceneCanvasSet(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.Descriptor.SourceKind == SceneSourceKind.SceneFile && scene.ResolvedSource is not null)
        {
            EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scene.ResolvedSource);
            return EngineSceneCanvasResolver.Resolve(document);
        }

        return CreateImplicitCanvasSet(scene.Name);
    }

    private static EngineSceneCanvasSet CreateImplicitCanvasSet(string sceneName)
    {
        return EngineSceneCanvasResolver.Resolve(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = sceneName,
            Entities = [],
        });
    }

    private void SynchronizeLegacyGameUiHostService(GameUiCanvasRegistry registry)
    {
        if (registry.TryGetPrimaryHost(out GameUiHost primaryHost))
        {
            Context.RegisterService(primaryHost);
            return;
        }

        Context.RemoveService<GameUiHost>();
    }

    private Scripting.Scene ResolveCurrentScriptScene()
    {
        MaterializeCurrentSceneScriptsIfPossible();
        if (Context.TryGetService(out Scripting.Scene scriptScene))
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

    private TemperatureField? ResolveTemperatureFieldOrNull(SimulationPhaseDriver? simulationDriver)
    {
        if (Context.TryGetService(out TemperatureField temperature))
        {
            return temperature;
        }

        if (simulationDriver is not null)
        {
            Context.RegisterService(simulationDriver.Temperature);
            return simulationDriver.Temperature;
        }

        return null;
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

    private IGameUiService? ResolveGameUiServiceOrNull()
    {
        return Context.TryGetService(out IGameUiService gameUi) ? gameUi : null;
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

    private IOverlayApi ResolveOverlayApi()
    {
        if (Context.TryGetService(out IOverlayApi overlay))
        {
            return overlay;
        }

        ScriptOverlayApi created = new();
        Context.RegisterService<IOverlayApi>(created);
        Context.RegisterService(created);
        return created;
    }

    private IDiagnosticsApi ResolveDiagnosticsApi()
    {
        if (Context.TryGetService(out IDiagnosticsApi diagnostics))
        {
            return diagnostics;
        }

        EngineScriptDiagnosticsApi created = new(
            Context.Counters,
            Context.Clock,
            ResolveDebugOverlaySettings(),
            () => Context.TryGetService(out ScriptLightingSynchronizer lighting) ? lighting.PointLights.Length : 0);
        Context.RegisterService<IDiagnosticsApi>(created);
        Context.RegisterService(created);
        return created;
    }

    private IRuntimeControlApi ResolveRuntimeControlApi()
    {
        if (Context.TryGetService(out IRuntimeControlApi runtime))
        {
            return runtime;
        }

        EngineScriptRuntimeControlApi created = new(this);
        Context.RegisterService<IRuntimeControlApi>(created);
        Context.RegisterService(created);
        return created;
    }

    private DebugOverlaySettings ResolveDebugOverlaySettings()
    {
        if (Context.TryGetService(out DebugOverlaySettings settings))
        {
            return settings;
        }

        DebugOverlaySettings created = new();
        Context.RegisterService(created);
        return created;
    }

    private DebugOverlayController ResolveDebugOverlayController()
    {
        if (Context.TryGetService(out DebugOverlayController controller))
        {
            return controller;
        }

        DebugOverlayController created = new(
            ResolveDebugOverlaySettings(),
            Context.TryGetService(out RigidStampRegistry registry) ? registry : null);
        Context.RegisterService(created);
        return created;
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

    private IConfigApi ResolveConfigApi()
    {
        if (Context.TryGetService(out IConfigApi existing))
        {
            return existing;
        }

        EngineScriptConfigApi created = new(Context.Options.ContentRoot);
        Context.RegisterService<IConfigApi>(created);
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
        _ownedRuntimeResources.Add(created);
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
        audio.AttachClipCache(created, takeOwnership: true);
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

        AudioDispatcher dispatcher = new(Context.Events.Channel<Core.Events.AudioEvent>(), audio.Voices, settings);
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
        const int ResidentBorderChunks = 2;
        int lastPlayableChunkX = (worldWidthCells - 1) / Core.EngineConstants.ChunkSize;
        int lastPlayableChunkY = (worldHeightCells - 1) / Core.EngineConstants.ChunkSize;
        int chunkWidth = checked(lastPlayableChunkX + (ResidentBorderChunks * 2) + 1);
        int chunkHeight = checked(lastPlayableChunkY + (ResidentBorderChunks * 2) + 1);
        Chunk[] residentChunks = new Chunk[checked(chunkWidth * chunkHeight)];
        int write = 0;
        // Resident world 没有 WorldManager 的按帧边界补环，脚本/input-phase 写入会把第一圈 border 标成 current dirty。
        // 预驻留第二圈，保证被唤醒的 border chunk 也能构造 CA 所需的完整 3x3 邻域。
        for (int cy = -ResidentBorderChunks; cy <= lastPlayableChunkY + ResidentBorderChunks; cy++)
        {
            for (int cx = -ResidentBorderChunks; cx <= lastPlayableChunkX + ResidentBorderChunks; cx++)
            {
                residentChunks[write++] = new Chunk(new ChunkCoord(cx, cy));
            }
        }

        chunks.AddRange(residentChunks);
    }

    private RuntimeWorldStateBridge EnsureRuntimeWorldStateBridge(ParticleSystem particles)
    {
        if (Context.TryGetService(out RuntimeWorldStateBridge existing))
        {
            return existing;
        }

        RuntimeWorldStateBridge stateBridge = new(particles);
        if (Context.TryGetService(out PhysicsSystem physics))
        {
            stateBridge.AttachPhysics(physics);
        }

        Context.RegisterService<IWorldStateSnapshotSource>(stateBridge);
        Context.RegisterService<IWorldStateSnapshotSink>(stateBridge);
        Context.RegisterService(stateBridge);
        return stateBridge;
    }

    private ResidencyTable ResolveSnapshotResidency(ResidentChunkMap chunks)
    {
        if (Context.TryGetService(out WorldManager world))
        {
            return world.Residency;
        }

        ResidencyTable residency = new();
        foreach (Chunk chunk in chunks.ResidentChunks)
        {
            residency.Set(
                chunk.Coord,
                new ChunkResidencyInfo(
                    ChunkResidencyState.Cached,
                    Context.Clock.FrameIndex,
                    ChunkMemoryBudget.EstimatedResidentChunkBytes,
                    DirtySinceLoad: false));
        }

        return residency;
    }

    // Simulation 世界装配：构建 CellGrid/Kernel、注册编辑 API 与 EngineServiceRole 别名。
    private SimulationPhaseDriver AttachSimulationWorld(
        ResidentChunkMap chunks,
        MaterialTable materials,
        ParticleSystem particles,
        TemperatureField temperature,
        ulong worldSeed = 0,
        uint frameIndex = 0)
    {
        ConfigureMaterialRuntimeBehaviours(materials, particles, temperature, out IReactionExecutor? reactionExecutor, out ILifetimeSink? lifetimeSink);
        MaterialPropsTable props = new(materials.Hot);
        RigidDamageQueue damageQueue = ResolveRigidDamageQueue();
        CellGrid grid = new(chunks, props, damageQueue);
        SimulationKernel kernel = new(
            chunks,
            props,
            worldSeed: worldSeed,
            rigidDamageSink: damageQueue,
            reactionExecutor: reactionExecutor,
            lifetimeSink: lifetimeSink,
            customUpdateExecutor: materials,
            profiler: Context.Profiler,
            cellDestructionSink: particles);
        if (frameIndex != 0)
        {
            kernel.RestoreFrameState(frameIndex, CurrentParityFromGameTime(frameIndex));
        }

        SimulationPhaseDriver driver = new(chunks, grid, kernel, particles, temperature, materials);
        driver.RegisterPhases(Phases);
        SimulationEditApi editApi = new(
            kernel,
            materials,
            temperature,
            Context.TryGetService(out IRigidCellOwnershipLookup ownership) ? ownership : null);

        Context.RegisterService(driver.GetType(), driver);
        Context.RegisterService(chunks);
        Context.RegisterService<IChunkSource>(chunks);
        Context.RegisterService(grid);
        Context.RegisterService(kernel);
        Context.RegisterService(particles);
        Context.RegisterService(temperature);
        Context.RegisterService(new EngineProbeApi(grid, kernel, temperature, materials, particles));
        Context.RegisterService<ISimulationEditApi>(editApi);
        Context.RegisterService<ISimulationInspectApi>(editApi);
        Context.RegisterService(editApi);
        Context.RegisterService(EngineServiceRole.WorldAccess, grid);
        Context.RegisterService(EngineServiceRole.ParticleService, particles);
        Context.RegisterService(EngineServiceRole.MaterialRegistry, materials);
        return driver;
    }

    private RigidDamageQueue ResolveRigidDamageQueue()
    {
        if (Context.TryGetService(out RigidDamageQueue existingDamageQueue))
        {
            return existingDamageQueue;
        }

        RigidDamageQueue damageQueue = new();
        Context.RegisterService(damageQueue);
        return damageQueue;
    }

    private void ConfigureMaterialRuntimeBehaviours(
        MaterialTable materials,
        ParticleSystem particles,
        TemperatureField temperature,
        out IReactionExecutor? reactionExecutor,
        out ILifetimeSink? lifetimeSink)
    {
        SimulationReactionSideEffects sideEffects = new(temperature, particles, materials);
        Context.RegisterService<IReactionSideEffectSink>(sideEffects);
        Context.RegisterService(sideEffects);

        reactionExecutor = null;
        if (Context.TryGetService(out ReactionTable reactions))
        {
            ReactionEngine engine = new(materials, reactions, sideEffects, particles);
            Context.RegisterService<IReactionExecutor>(engine);
            Context.RegisterService(engine);
            reactionExecutor = engine;
        }

        lifetimeSink = null;
        if (materials.TryGetId("fire", out _) && TryResolveBurnoutMaterial(materials, out ushort burnoutMaterial))
        {
            BurningCellSystem burning = new(materials, burnoutMaterial, sideEffects);
            materials.RegisterCustomUpdate("fire", burning.UpdateBurning);
            Context.RegisterService<ILifetimeSink>(burning);
            Context.RegisterService(burning);
            lifetimeSink = burning;
        }
    }

    private static bool TryResolveBurnoutMaterial(MaterialTable materials, out ushort burnoutMaterial)
    {
        if (materials.TryGetId("ash", out burnoutMaterial))
        {
            return true;
        }

        if (materials.TryGetId("smoke", out burnoutMaterial))
        {
            return true;
        }

        burnoutMaterial = 0;
        return materials.Count != 0;
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
        Scene scene,
        string scenePath,
        int particleCapacity,
        ushort fallbackMaterialId,
        WorldStreamingConfig? streamingConfig,
        string? proceduralWorldRoot)
    {
        EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scenePath);
        if (!string.IsNullOrWhiteSpace(document.InitialSaveDirectory))
        {
            string savePath = ResolveSceneRelativePath(scenePath, document.InitialSaveDirectory);
            return AttachWorldFromSaveDirectory(savePath, particleCapacity, fallbackMaterialId, streamingConfig);
        }

        if (!string.IsNullOrWhiteSpace(document.ProceduralWorldGenerator))
        {
            _ = AttachProceduralSceneWorld(
                scene,
                particleCapacity,
                fallbackMaterialId,
                streamingConfig,
                proceduralWorldRoot,
                document.ProceduralWorldGenerator);
        }

        return null;
    }

    private WorldLoadResult? AttachCurrentProceduralSceneWorld(
        Scene scene,
        int particleCapacity,
        ushort fallbackMaterialId,
        WorldStreamingConfig? streamingConfig,
        string? proceduralWorldRoot)
    {
        _ = AttachProceduralSceneWorld(
            scene,
            particleCapacity,
            fallbackMaterialId,
            streamingConfig,
            proceduralWorldRoot);
        return null;
    }

    private bool AttachProceduralSceneWorld(
        Scene scene,
        int particleCapacity,
        ushort fallbackMaterialId,
        WorldStreamingConfig? streamingConfig,
        string? proceduralWorldRoot,
        string? generatorKeyOverride = null)
    {
        if (scene.Descriptor.SourceKind != SceneSourceKind.Procedural &&
            (scene.Descriptor.SourceKind != SceneSourceKind.SceneFile || string.IsNullOrWhiteSpace(generatorKeyOverride)))
        {
            return false;
        }

        if (Context.TryGetService(out SimulationPhaseDriver _))
        {
            throw new InvalidOperationException("当前 Engine 已接入 Simulation world，不能重复装配程序化世界。");
        }

        ResetRestartSnapshot();
        string key = string.IsNullOrWhiteSpace(generatorKeyOverride)
            ? scene.ResolvedSource ?? throw new InvalidOperationException("Procedural 场景缺少生成器键。")
            : generatorKeyOverride.Trim();
        if (!TryResolveProceduralWorldGenerator(scene, key, out ProceduralWorldGeneratorRegistry.Registration registration))
        {
            throw new InvalidOperationException($"未注册程序化世界生成器：{key}。");
        }

        MaterialTable materials = Context.GetService<MaterialTable>();
        IMaterialQuery materialQuery = ResolveMaterialQuery(materials);
        ProceduralWorldBuildRequest request = new(key, materialQuery);
        if (registration.Streaming is not null)
        {
            ProceduralWorldDescriptor streamingDescriptor = registration.Streaming.Describe(in request).Validate();
            if (streamingDescriptor.Extent != ProceduralWorldExtent.Infinite)
            {
                throw new InvalidOperationException("流式程序化世界生成器必须返回 Infinite descriptor。");
            }

            AttachStreamingProceduralWorld(
                key,
                registration.Streaming,
                streamingDescriptor,
                materialQuery,
                materials,
                particleCapacity,
                fallbackMaterialId,
                streamingConfig,
                proceduralWorldRoot);
            return true;
        }

        IProceduralWorldGenerator generator = registration.Finite ??
            throw new InvalidOperationException($"程序化世界注册项 {key} 没有可用生成器。");
        ProceduralWorldDescriptor descriptor = generator.Describe(in request).Validate();
        if (descriptor.Extent != ProceduralWorldExtent.Finite)
        {
            throw new InvalidOperationException("有限程序化世界生成器必须返回 Finite descriptor。");
        }

        ResidentChunkMap chunks = new();
        AddResidentChunks(chunks, descriptor.WidthCells, descriptor.HeightCells);
        ParticleSystem particles = new(particleCapacity, Context.Events);
        TemperatureField temperature = new();
        _ = AttachSimulationWorld(
            chunks,
            materials,
            particles,
            temperature,
            descriptor.WorldSeed,
            descriptor.FrameIndex);
        Context.Clock.RestoreCounters(descriptor.FrameIndex, descriptor.FrameIndex);
        ProceduralWorldBuildContext context = new(
            key,
            materialQuery,
            Context.GetService<ISimulationEditApi>(),
            descriptor.WidthCells,
            descriptor.HeightCells);
        generator.Populate(in context);
        return true;
    }

    private bool TryResolveProceduralWorldGenerator(
        Scene scene,
        string key,
        out ProceduralWorldGeneratorRegistry.Registration registration)
    {
        ProceduralWorldGeneratorRegistry registry = Context.TryGetService(out ProceduralWorldGeneratorRegistry existing)
            ? existing
            : new ProceduralWorldGeneratorRegistry();
        if (registry.TryGet(key, out registration))
        {
            return true;
        }

        // Editor 动态脚本可让 procedural 入口 Behaviour 同时实现生成接口，避免 Editor 反向引用 Demo 程序集。
        if (scene.ScriptScene is not null)
        {
            ScriptEntityInspection[] entities = scene.ScriptScene.CaptureInspectionSnapshot();
            for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
            {
                ScriptComponentInspection[] components = entities[entityIndex].Components;
                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    if (components[componentIndex].Behaviour is not IStreamingProceduralWorldGenerator generator)
                    {
                        continue;
                    }

                    registry.Register(key, generator);
                    Context.RegisterService(registry);
                    return registry.TryGet(key, out registration);
                }
            }
        }

        registration = default;
        return false;
    }

    private void AttachStreamingProceduralWorld(
        string key,
        IStreamingProceduralWorldGenerator generator,
        ProceduralWorldDescriptor descriptor,
        IMaterialQuery materialQuery,
        MaterialTable materials,
        int particleCapacity,
        ushort fallbackMaterialId,
        WorldStreamingConfig? streamingConfig,
        string? proceduralWorldRoot)
    {
        _ = materials.GetName(fallbackMaterialId);
        TemperatureField temperature = new();
        StreamingProceduralChunkInitializer initializer = new(key, materialQuery, generator);
        WorldManager world = new(
            new WorldCamera(
                descriptor.InitialFocusX,
                descriptor.InitialFocusY,
                Context.Options.InternalWidth,
                Context.Options.InternalHeight),
            temperature,
            materials,
            ResolveProceduralWorldPath(descriptor, proceduralWorldRoot),
            fallbackMaterialId,
            streamingConfig,
            initializer);
        ParticleSystem particles = new(particleCapacity, Context.Events);
        _ = AttachSimulationWorld(
            world.Chunks,
            materials,
            particles,
            temperature,
            descriptor.WorldSeed,
            descriptor.FrameIndex);
        Context.Clock.RestoreCounters(descriptor.FrameIndex, descriptor.FrameIndex);
        _ = AttachWorldManager(world);
        _ = EnsureRuntimeWorldStateBridge(particles);
        PrimeStreamingWorld(world, descriptor.FrameIndex);
    }

    private void PrimeStreamingWorld(WorldManager world, uint frameIndex)
    {
        ChunkRect active = world.ComputeVisibleChunks().Expand(world.Config.ActivationMarginChunks);
        int targetChunkCount = active.Expand(world.Config.BorderRingWidth).Count;
        int batchCount = checked((targetChunkCount + world.Config.MaxStreamOpsPerFrame - 1) /
            world.Config.MaxStreamOpsPerFrame);
        for (int batch = 0; batch < batchCount; batch++)
        {
            world.ApplyResidency(frameIndex);
            _ = world.Streamer.ProcessIoOnce(Context.Jobs);
        }

        world.ApplyResidency(frameIndex);
        if (world.Chunks.Count != targetChunkCount || world.Streamer.PendingRequestCount != 0)
        {
            throw new InvalidOperationException(
                $"无限程序化世界初始驻留不完整：expected={targetChunkCount}, resident={world.Chunks.Count}, pending={world.Streamer.PendingRequestCount}。");
        }
    }

    private static string ResolveProceduralWorldPath(
        ProceduralWorldDescriptor descriptor,
        string? proceduralWorldRoot)
    {
        string root = proceduralWorldRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Path.GetTempPath(), "PixelEngine-user-data");
        }

        if (proceduralWorldRoot is null)
        {
            root = Path.Combine(root, "PixelEngine", "Worlds");
        }

        return Path.GetFullPath(Path.Combine(
            root,
            descriptor.PersistenceKey!,
            $"{descriptor.WorldSeed:X16}"));
    }

    private sealed class StreamingProceduralChunkInitializer(
        string key,
        IMaterialQuery materials,
        IStreamingProceduralWorldGenerator generator) : IWorldChunkInitializer
    {
        public void Initialize(in WorldChunkInitializationContext context)
        {
            ProceduralChunkBuildContext buildContext = new(
                key,
                materials,
                context.ChunkX,
                context.ChunkY,
                context.OriginCellX,
                context.OriginCellY,
                context.SizeCells,
                context.TemperatureSizeCells,
                context.MaterialCells,
                context.TemperatureCells);
            generator.PopulateChunk(in buildContext);
        }
    }

    private IMaterialQuery ResolveMaterialQuery(MaterialTable materials)
    {
        if (Context.TryGetService(out IMaterialQuery existing))
        {
            return existing;
        }

        EngineMaterialRegistry registry = new(materials);
        Context.RegisterService<IMaterialQuery>(registry);
        Context.RegisterService(registry);
        if (!Context.IsServiceAvailable(EngineServiceRole.MaterialRegistry))
        {
            Context.RegisterService(EngineServiceRole.MaterialRegistry, registry);
        }

        return registry;
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

    private static Scripting.Scene BuildProceduralScriptScene(
        string entryBehaviourName,
        ScriptAssemblyRegistry scriptAssemblies)
    {
        Type behaviourType = ResolveBehaviourType(entryBehaviourName, scriptAssemblies);
        Scripting.Scene scriptScene = new();
        Entity entity = scriptScene.CreateEntity();
        _ = entity.AddComponent(behaviourType);
        return scriptScene;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Runtime scripting resolves Behaviour types from assemblies compiled or loaded at runtime; this boundary is outside trimmed engine hot paths.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Runtime script assemblies preserve their Behaviour constructors independently of the trimmed engine closure.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2073",
        Justification = "Behaviour Type values returned from runtime script assemblies are validated at load time; the trimmer cannot statically model those assemblies.")]
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

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Behaviour constructor validation is performed against runtime script types that are outside the trimmed engine closure.")]
    private static bool IsConcreteBehaviour([NotNullWhen(true)] Type? type)
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
        bool noGcRegionStarted = TryBeginNoGcRegion();
        profiler.BeginFrame();
        FrameTiming timing;
        try
        {
            // 相位 0：过载策略、帧率统计与帧时钟推进。
            using (profiler.Measure(FramePhase.InputAndTime))
            {
                ApplyOverloadPolicy(realDeltaSeconds);
                PublishRenderFrameRate(realDeltaSeconds);
                timing = BeginRuntimeFrame(realDeltaSeconds);
                PublishScriptFrameTime(timing);
            }

            // 相位 1-11：按 EnginePhasePipeline 顺序执行各子系统 tick。
            Phases.Execute(this, timing);
            Context.Counters.SimHz = Context.Clock.SimHz;
            // 首次脚本 tick 后捕获重开关卡基线快照。
            TryCaptureRestartSnapshot(timing);
            if (IsShutdownRequested)
            {
                Shutdown();
            }
        }
        finally
        {
            profiler.EndFrame();
            PublishFrameBreakdown(profiler);
            EndNoGcRegionIfStarted(noGcRegionStarted);
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
            IsShutdownRequested = false;
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
        _restartSnapshotStore = null;
        _restartSnapshotCaptured = false;
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

    // 过载降级：根据帧耗时调整质量档位，并联动热力学步进、渲染风格、GameUi 与 GPU compute。
    private void ApplyOverloadPolicy(double realDeltaSeconds)
    {
        EngineOverloadController overload = Context.GetService<EngineOverloadController>();
        EngineQualityTier previousTier = Context.QualityTier;
        bool hasPresentationTiming =
            Context.Counters.FramePresentSubmitMilliseconds > 0 ||
            Context.Counters.FramePresentWaitMilliseconds > 0 ||
            Context.Counters.FrameGpuTimerAvailable;
        double frameMs = hasPresentationTiming && Context.Counters.EffectiveFrameMilliseconds > 0
            ? Context.Counters.EffectiveFrameMilliseconds
            : realDeltaSeconds * 1000.0;
        EngineQualityTier tier = overload.SubmitFrame(frameMs);
        Context.SetQualityTier(tier);
        ApplyThermalDegradation(tier);
        ApplyRenderStyleDegradation(tier);
        ApplyGameUiDegradation(tier);
        if (tier != previousTier && tier >= EngineQualityTier.ReducedLighting)
        {
            ApplyGpuComputeDegradation();
        }

        Context.Clock.SimHz = tier >= EngineQualityTier.Sim30Hz
            ? Core.EngineConstants.SimHzDownscaled
            : RequestedSimHz;
    }

    // GameUi present 降频：过载越高，paint/composite 间隔越大以回收 CPU/GPU 预算。
    private void ApplyGameUiDegradation(EngineQualityTier tier)
    {
        bool hasRegistry = Context.TryGetService(out GameUiCanvasRegistry gameUiRegistry);
        GameUiHost? legacyGameUiHost = null;
        bool hasLegacyHost = false;
        if (!hasRegistry && Context.TryGetService(out GameUiHost resolvedLegacyHost))
        {
            legacyGameUiHost = resolvedLegacyHost;
            hasLegacyHost = true;
        }
        if (!hasRegistry && !hasLegacyHost)
        {
            return;
        }

        int intervalFrames = tier switch
        {
            EngineQualityTier.Full => 1,
            EngineQualityTier.ReducedThermal or EngineQualityTier.ReducedLighting => 2,
            EngineQualityTier.DistantChunkThrottle or EngineQualityTier.Sim30Hz => 3,
            EngineQualityTier.SlowMotion => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "未知引擎质量档位。"),
        };
        if (hasRegistry)
        {
            gameUiRegistry.SetPresentationFrameInterval(intervalFrames);
        }
        else
        {
            legacyGameUiHost!.SetPresentationFrameInterval(intervalFrames);
        }
    }

    private void PublishRenderFrameRate(double realDeltaSeconds)
    {
        if (realDeltaSeconds <= 0 || !double.IsFinite(realDeltaSeconds))
        {
            return;
        }

        double frameMs = realDeltaSeconds * 1000.0;
        if (_renderFrameSampleCount == RenderFrameSampleCapacity)
        {
            _renderFrameSampleSumMs -= _renderFrameSamplesMs[_renderFrameSampleIndex];
        }
        else
        {
            _renderFrameSampleCount++;
        }

        _renderFrameSamplesMs[_renderFrameSampleIndex] = frameMs;
        _renderFrameSampleSumMs += frameMs;
        _renderFrameSampleIndex = (_renderFrameSampleIndex + 1) % RenderFrameSampleCapacity;

        double averageMs = _renderFrameSampleSumMs / _renderFrameSampleCount;
        double varianceSum = 0;
        for (int i = 0; i < _renderFrameSampleCount; i++)
        {
            double delta = _renderFrameSamplesMs[i] - averageMs;
            varianceSum += delta * delta;
            _renderFrameSortScratchMs[i] = _renderFrameSamplesMs[i];
        }

        Array.Sort(_renderFrameSortScratchMs, 0, _renderFrameSampleCount);
        int p99Index = Math.Clamp(
            (int)Math.Ceiling(_renderFrameSampleCount * 0.99) - 1,
            0,
            _renderFrameSampleCount - 1);
        double p99Ms = _renderFrameSortScratchMs[p99Index];

        Context.Counters.RenderFrameLastMilliseconds = frameMs;
        Context.Counters.RenderFrameMilliseconds = averageMs;
        Context.Counters.RenderFramesPerSecond = averageMs > 0 ? 1000.0 / averageMs : 0;
        Context.Counters.RenderFrameP99Milliseconds = p99Ms;
        Context.Counters.RenderFrameLow1PercentFps = p99Ms > 0 ? 1000.0 / p99Ms : 0;
        Context.Counters.RenderFrameJitterMilliseconds = Math.Sqrt(varianceSum / _renderFrameSampleCount);
        Context.Counters.RenderFrameSampleCount = _renderFrameSampleCount;
    }

    private void PublishScriptFrameTime(FrameTiming timing)
    {
        if (Context.TryGetService(out ScriptFrameTime time))
        {
            time.SetRealDeltaTime(timing.RealDeltaSeconds);
        }
    }

    private void PublishFrameBreakdown(FrameProfiler profiler)
    {
        ReadOnlySpan<double> phases = profiler.LastFrame;
        ReadOnlySpan<double> subPhases = profiler.LastSubFrame;
        double wallMs = profiler.LastWallMilliseconds;
        double presentSubmitMs = GetSubPhase(subPhases, FrameSubPhase.Present);
        double presentWaitMs = GetSubPhase(subPhases, FrameSubPhase.PresentWait);
        double uiUpdateMs = GetSubPhase(subPhases, FrameSubPhase.UiUpdate);
        double uiCompositeMs = GetSubPhase(subPhases, FrameSubPhase.UiComposite);
        double uiPaintMs = GetSubPhase(subPhases, FrameSubPhase.UiPaint);
        double uiUploadMs = GetSubPhase(subPhases, FrameSubPhase.UiUpload);
        if (Context.TryGetService(out GameUiCanvasRegistry gameUiRegistry))
        {
            if (uiPaintMs <= 0.0)
            {
                uiPaintMs = gameUiRegistry.LastPaintMilliseconds;
            }

            Context.Counters.UiPresentationIntervalFrames = gameUiRegistry.PresentationIntervalFrames;
            Context.Counters.UiSkippedPresentationFrames = gameUiRegistry.SkippedPresentationFrames;
        }
        else if (Context.TryGetService(out GameUiHost legacyGameUiHost))
        {
            if (uiPaintMs <= 0.0)
            {
                uiPaintMs = legacyGameUiHost.LastPaintMilliseconds;
            }

            Context.Counters.UiPresentationIntervalFrames = legacyGameUiHost.PresentationIntervalFrames;
            Context.Counters.UiSkippedPresentationFrames = legacyGameUiHost.SkippedPresentationFrames;
        }
        else
        {
            Context.Counters.UiPresentationIntervalFrames = 0;
            Context.Counters.UiSkippedPresentationFrames = 0;
        }

        double waitMs = presentWaitMs;
        double profileTotalMs = Sum(phases);
        double cpuWorkMs = Math.Max(0.0, profileTotalMs - waitMs);
        double gpuWorkMs = GetSubPhase(subPhases, FrameSubPhase.GpuFrame);
        double effectiveMs = Math.Max(0.0, wallMs - waitMs);

        Context.Counters.FrameCpuWorkMilliseconds = cpuWorkMs;
        Context.Counters.FrameGpuWorkMilliseconds = gpuWorkMs;
        Context.Counters.FramePresentSubmitMilliseconds = presentSubmitMs;
        Context.Counters.UiUpdateMilliseconds = uiUpdateMs;
        Context.Counters.UiCompositeMilliseconds = uiCompositeMs;
        Context.Counters.UiPaintMilliseconds = uiPaintMs;
        Context.Counters.UiUploadMilliseconds = uiUploadMs;
        Context.Counters.FramePresentWaitMilliseconds = presentWaitMs;
        Context.Counters.FrameWaitMilliseconds = waitMs;
        Context.Counters.EffectiveFrameMilliseconds = effectiveMs;
        Context.Counters.EffectiveFramesPerSecond = effectiveMs > 0.0001 ? 1000.0 / effectiveMs : 0.0;
        if (Context.TryGetService(out RenderWindow window))
        {
            Context.Counters.VSyncEnabled = window.VSyncEnabled;
        }

        if (Context.TryGetService(out RenderPipeline pipeline))
        {
            Context.Counters.FrameGpuTimerAvailable = pipeline.GpuFrameTimerAvailable;
        }
    }

    private static double Sum(ReadOnlySpan<double> values)
    {
        double total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            total += values[i];
        }

        return total;
    }

    private static double GetSubPhase(ReadOnlySpan<double> subPhases, FrameSubPhase phase)
    {
        int index = (int)phase;
        return (uint)index < (uint)subPhases.Length ? subPhases[index] : 0.0;
    }

    // ReducedThermal 起将热力学步进间隔从每 tick 降为每 4 tick，降低温度/相变 CPU 开销。
    private void ApplyThermalDegradation(EngineQualityTier tier)
    {
        if (!Context.TryGetService(out TemperatureField temperature))
        {
            return;
        }

        temperature.SetStepInterval(tier >= EngineQualityTier.ReducedThermal
            ? ReducedThermalStepInterval
            : FullThermalStepInterval);
    }

    // ReducedThermal 起关闭 CPU RenderStyle 差异化着色，减轻 render buffer 构建成本。
    private void ApplyRenderStyleDegradation(EngineQualityTier tier)
    {
        if (!Context.TryGetService(out IRenderStyleQualityController renderStyle))
        {
            return;
        }

        renderStyle.SetRenderStyleLevel(tier >= EngineQualityTier.ReducedThermal
            ? RenderBufferStyleLevel.Off
            : RenderBufferStyleLevel.Full);
    }

    private void ApplyGpuComputeDegradation()
    {
        if (Context.TryGetService(out IGpuComputeQualityDegrader degrader))
        {
            _ = degrader.DegradeGpuComputeOneStep();
        }
    }

    private void TryCaptureRestartSnapshot(FrameTiming timing)
    {
        if (_restartSnapshotCaptured || !timing.RunSim || _attachedScriptRuntime is null)
        {
            return;
        }

        if (!Context.TryGetService(out ResidentChunkMap _) ||
            !Context.TryGetService(out SimulationKernel _) ||
            !Context.TryGetService(out ParticleSystem _) ||
            !Context.TryGetService(out MaterialTable _))
        {
            return;
        }

        EngineWorldSnapshotStore store = EnsureRestartSnapshotStore();
        SaveLoadOperationResult save = store.SaveTemporarySnapshot();
        if (save.Success)
        {
            _restartSnapshotCaptured = true;
        }
    }

    private EngineWorldSnapshotStore EnsureRestartSnapshotStore()
    {
        if (_restartSnapshotStore is not null)
        {
            return _restartSnapshotStore;
        }

        _restartSnapshotStore = new EngineWorldSnapshotStore(
            this,
            consumeOnRestore: false,
            slotId: "__restart_baseline");
        _ownedRuntimeResources.Add(_restartSnapshotStore);
        return _restartSnapshotStore;
    }

    private void ResetRestartSnapshot()
    {
        if (_restartSnapshotStore is not null)
        {
            _restartSnapshotStore.Dispose();
            _ = _ownedRuntimeResources.Remove(_restartSnapshotStore);
            _restartSnapshotStore = null;
        }

        _restartSnapshotCaptured = false;
    }

    private FrameTiming BeginRuntimeFrame(double realDeltaSeconds)
    {
        return Mode switch
        {
            EngineExecutionMode.Edit => Context.Clock.BeginRenderOnlyFrame(realDeltaSeconds),
            EngineExecutionMode.Paused => Context.Clock.BeginRenderOnlyFrame(realDeltaSeconds),
            EngineExecutionMode.Step => Context.Clock.BeginForcedSimFrame(realDeltaSeconds),
            EngineExecutionMode.Play => Context.Clock.BeginFrame(realDeltaSeconds),
            _ => throw new InvalidOperationException($"未知 Engine 执行模式：{Mode}。"),
        };
    }

    private bool TryBeginNoGcRegion()
    {
        long budgetBytes = Context.Options.NoGcRegionBudgetBytes;
        if (budgetBytes == 0)
        {
            return false;
        }

        bool started;
        try
        {
            started = EngineGcCoordinator.TryBeginNoGcRegion(budgetBytes);
        }
        catch (InvalidOperationException)
        {
            started = false;
        }

        Context.Counters.RecordNoGcRegionStartAttempt(started);
        return started;
    }

    private void EndNoGcRegionIfStarted(bool started)
    {
        if (!started)
        {
            return;
        }

        try
        {
            EngineGcCoordinator.EndNoGcRegion();
            Context.Counters.RecordNoGcRegionSuccess();
        }
        catch (InvalidOperationException)
        {
            Context.Counters.RecordNoGcRegionEndFailure();
            throw;
        }
    }
}
