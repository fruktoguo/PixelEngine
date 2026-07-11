using PixelEngine.Core;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Events;
using PixelEngine.Core.Threading;
using PixelEngine.Core.Time;
using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// Engine fluent 构建器，负责生成不可变配置并装配 Core 运行时服务。
/// </summary>
public sealed class EngineBuilder
{
    private int _windowWidth = EngineOptions.DefaultWindowWidth;
    private int _windowHeight = EngineOptions.DefaultWindowHeight;
    private string _windowTitle = EngineOptions.DefaultWindowTitle;
    private int _internalWidth = EngineOptions.DefaultInternalWidth;
    private int _internalHeight = EngineOptions.DefaultInternalHeight;
    private int _workerCount;
    private EngineGcMode _gcMode = EngineGcMode.SustainedLowLatency;
    private bool _enableEditor;
    private bool _headless;
    private bool _deterministicMode;
    private bool _enableGpu = true;
    private bool _preferComputeSharpBackend;
    private bool _enableGuiRuntime = true;
    private bool _enableGameUi;
    private UiBackendKind _gameUiBackend = UiBackendKind.ManagedFallback;
    private bool _vSync = true;
    private string _contentRoot = "content";
    private string? _startScene;
    private double _simHz = EngineConstants.DefaultSimHz;
    private int _eventCapacityPerChannel = EngineOptions.DefaultEventCapacityPerChannel;
    private long _noGcRegionBudgetBytes;
    private EngineOverloadOptions _overload = EngineOverloadOptions.CreateDefault();
    private readonly List<(EnginePhase Phase, EnginePhaseAction Action)> _phaseActions = [];
    private readonly List<IEnginePhaseDriver> _phaseDrivers = [];
    private readonly List<IEditorHostExtension> _editorHostExtensions = [];
    private readonly List<SceneDescriptor> _scenes = [];
    private readonly List<IEngineSubsystem> _subsystems = [];

    /// <summary>
    /// 配置窗口尺寸。
    /// </summary>
    public EngineBuilder WithWindow(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _windowWidth = width;
        _windowHeight = height;
        return this;
    }

    /// <summary>
    /// 配置窗口标题。
    /// </summary>
    public EngineBuilder WithWindowTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _windowTitle = title.Trim();
        return this;
    }

    /// <summary>
    /// 配置内部 sim 分辨率。
    /// </summary>
    public EngineBuilder WithInternalResolution(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _internalWidth = width;
        _internalHeight = height;
        return this;
    }

    /// <summary>
    /// 配置 JobSystem worker 数；0 表示自动。
    /// </summary>
    public EngineBuilder WithWorkerCount(int workerCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workerCount);
        _workerCount = workerCount;
        return this;
    }

    /// <summary>
    /// 配置托管 GC 延迟模式。
    /// </summary>
    public EngineBuilder WithGcMode(EngineGcMode gcMode)
    {
        _gcMode = gcMode;
        return this;
    }

    /// <summary>
    /// 配置是否启用 Editor。
    /// </summary>
    public EngineBuilder EnableEditor(bool enabled = true)
    {
        _enableEditor = enabled;
        return this;
    }

    /// <summary>
    /// 配置 headless 模式。
    /// </summary>
    public EngineBuilder UseHeadless(bool enabled = true)
    {
        _headless = enabled;
        if (enabled)
        {
            _enableEditor = false;
            _enableGpu = false;
            _preferComputeSharpBackend = false;
            _enableGuiRuntime = false;
            _enableGameUi = false;
        }

        return this;
    }

    /// <summary>
    /// 配置确定性模式。
    /// </summary>
    public EngineBuilder UseDeterministicMode(bool enabled = true)
    {
        _deterministicMode = enabled;
        if (enabled && _workerCount == 0)
        {
            _workerCount = 1;
        }

        return this;
    }

    /// <summary>
    /// 配置是否允许 GPU 后端。
    /// </summary>
    public EngineBuilder EnableGpu(bool enabled = true)
    {
        _enableGpu = enabled && !_headless;
        if (!_enableGpu)
        {
            _preferComputeSharpBackend = false;
        }

        return this;
    }

    /// <summary>
    /// 配置是否显式优先选择 ComputeSharp/DX12 后端；实际启用仍受 plan/09 G2 资源契约与可执行后端门控约束。
    /// </summary>
    public EngineBuilder PreferComputeSharpBackend(bool enabled = true)
    {
        _preferComputeSharpBackend = enabled && _enableGpu && !_headless;
        return this;
    }

    /// <summary>
    /// 配置是否允许 Hosting 自建脚本 GUI runtime；外部编辑器宿主可关闭以避免接管窗口上下文资源。
    /// </summary>
    public EngineBuilder UseGuiRuntime(bool enabled = true)
    {
        _enableGuiRuntime = enabled && !_headless;
        if (!_enableGuiRuntime)
        {
            _enableGameUi = false;
        }

        return this;
    }

    /// <summary>
    /// 配置是否启用游戏大 UI；headless 或 GUI runtime 禁用时保持关闭。
    /// </summary>
    public EngineBuilder EnableGameUi(bool enabled = true)
    {
        _enableGameUi = enabled && !_headless && _enableGuiRuntime;
        return this;
    }

    /// <summary>
    /// 配置游戏大 UI 后端。
    /// </summary>
    public EngineBuilder UseUiBackend(UiBackendKind backend)
    {
        _gameUiBackend = backend;
        return this;
    }

    /// <summary>
    /// 配置窗口 VSync；headless 模式下保留配置但不会创建窗口。
    /// </summary>
    public EngineBuilder UseVSync(bool enabled = true)
    {
        _vSync = enabled;
        return this;
    }

    /// <summary>
    /// 配置内容根目录。
    /// </summary>
    public EngineBuilder WithContentRoot(string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        _contentRoot = contentRoot;
        return this;
    }

    /// <summary>
    /// 配置起始场景标识。
    /// </summary>
    public EngineBuilder WithStartScene(string? startScene)
    {
        _startScene = string.IsNullOrWhiteSpace(startScene) ? null : startScene;
        return this;
    }

    /// <summary>
    /// 加载项目模型中的内容根、起始场景与场景列表。
    /// </summary>
    public EngineBuilder WithProject(EngineProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _contentRoot = project.ContentRoot;
        _startScene = project.StartScene;
        _scenes.Clear();
        foreach (SceneDescriptor scene in project.Scenes)
        {
            _scenes.Add(scene);
        }

        return this;
    }

    /// <summary>
    /// 注册项目中的一个场景。
    /// </summary>
    public EngineBuilder AddScene(SceneDescriptor scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scenes.Add(scene);
        return this;
    }

    /// <summary>
    /// 配置固定 sim 频率，目前支持 60Hz 与 30Hz。
    /// </summary>
    public EngineBuilder WithSimHz(double simHz)
    {
        _simHz = simHz;
        return this;
    }

    /// <summary>
    /// 配置每个事件类型通道的容量。
    /// </summary>
    public EngineBuilder WithEventCapacityPerChannel(int capacity)
    {
        _eventCapacityPerChannel = capacity;
        return this;
    }

    /// <summary>
    /// 配置每帧关键段 no-GC region 预算；0 表示关闭。
    /// </summary>
    public EngineBuilder WithNoGcRegionBudget(long budgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetBytes);
        _noGcRegionBudgetBytes = budgetBytes;
        return this;
    }

    /// <summary>
    /// 配置过载降级策略。
    /// </summary>
    public EngineBuilder WithOverloadPolicy(double frameBudgetMs, int sustainWindow)
    {
        _overload = new EngineOverloadOptions(frameBudgetMs, sustainWindow);
        return this;
    }

    /// <summary>
    /// 注册指定运行时相位的同步 hook。
    /// </summary>
    public EngineBuilder OnPhase(EnginePhase phase, EnginePhaseAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _phaseActions.Add((phase, action));
        return this;
    }

    /// <summary>
    /// 注册一个由 Hosting 管理初始化与关闭顺序的子系统。
    /// </summary>
    public EngineBuilder AddSubsystem(IEngineSubsystem subsystem)
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        _subsystems.Add(subsystem);
        return this;
    }

    /// <summary>
    /// 注册一个真实子系统相位驱动，由 Build 阶段绑定到 12 相位管线。
    /// </summary>
    public EngineBuilder AddPhaseDriver(IEnginePhaseDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _phaseDrivers.Add(driver);
        return this;
    }

    /// <summary>
    /// 注册独立编辑器壳注入的中性 GUI/相位 [10] 扩展。Hosting 只保存接口，不引用 Editor 程序集。
    /// </summary>
    public EngineBuilder AddEditorHostExtension(IEditorHostExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        _editorHostExtensions.Add(extension);
        return this;
    }

    /// <summary>
    /// 构建 Engine 并完成 Core 服务装配。
    /// </summary>
    public Engine Build()
    {
        // 1) 物化不可变 EngineOptions，并应用 GC 延迟模式。
        EngineOptions options = new(
            _windowWidth,
            _windowHeight,
            _windowTitle,
            _internalWidth,
            _internalHeight,
            _workerCount,
            _gcMode,
            _enableEditor,
            _headless,
            _deterministicMode,
            _enableGpu,
            _preferComputeSharpBackend,
            _enableGuiRuntime,
            _enableGameUi,
            _gameUiBackend,
            _vSync,
            _contentRoot,
            _startScene,
            _simHz,
            _eventCapacityPerChannel,
            _noGcRegionBudgetBytes,
            _overload);
        EngineGcCoordinator.ApplyLatencyMode(options.GcMode.ToLatencyMode());
        // 2) 创建 Core 运行时服务：JobSystem、帧时钟、事件总线、计数器与过载控制器。
        JobSystem jobs = new(options.WorkerCount);
        FrameClock clock = new(options.SimHz);
        EventBus events = new(options.EventCapacityPerChannel);
        EngineCounters counters = new()
        {
            NoGcRegionBudgetBytes = options.NoGcRegionBudgetBytes,
            SimHz = options.SimHz,
        };
        FrameProfiler profiler = new();
        EngineOverloadController overload = new(options.Overload);
        EngineCommandQueue commands = new();
        ScriptAssemblyRegistry scriptAssemblies = new();
        EngineLifecycle lifecycle = BuildLifecycle();
        SceneService scenes = BuildSceneService(options);
        // 3) 装配 EngineContext 并注册所有 Core/Hosting 基础服务。
        EngineContext context = new(options, jobs, clock, events, counters, profiler);
        context.RegisterService(context);
        context.RegisterService(options);
        context.RegisterService(jobs);
        context.RegisterService(clock);
        context.RegisterService(EngineServiceRole.EventBus, events);
        context.RegisterService(EngineServiceRole.Diagnostics, counters);
        context.RegisterService(profiler);
        context.RegisterService(overload);
        context.RegisterService(commands);
        context.RegisterService(scriptAssemblies);
        context.RegisterService(lifecycle);
        context.RegisterService<ISceneService>(EngineServiceRole.SceneService, scenes);
        context.RegisterService(scenes);
        IReadOnlyList<IEditorHostExtension> editorHostExtensions = [.. _editorHostExtensions];
        context.RegisterService(editorHostExtensions);
        bool gameUiInputSourceRegistered = false;
        bool gameplayViewportMapperRegistered = false;
        for (int i = 0; i < editorHostExtensions.Count; i++)
        {
            IEditorHostExtension extension = editorHostExtensions[i];
            if (!gameUiInputSourceRegistered && extension is IGameUiInputSourceFactory gameUiInputSourceFactory)
            {
                context.RegisterService(gameUiInputSourceFactory);
                gameUiInputSourceRegistered = true;
            }

            if (!gameplayViewportMapperRegistered && extension is IGameplayViewportInputMapper gameplayViewportMapper)
            {
                context.RegisterService(gameplayViewportMapper);
                gameplayViewportMapperRegistered = true;
            }
        }

        // 4) 构建 12 相位管线：先绑定相位驱动，再注册自定义 OnPhase hook。
        EnginePhasePipeline phases = new(commands);
        for (int i = 0; i < _phaseDrivers.Count; i++)
        {
            context.RegisterService(_phaseDrivers[i].GetType(), _phaseDrivers[i]);
            _phaseDrivers[i].RegisterPhases(phases);
        }

        for (int i = 0; i < _phaseActions.Count; i++)
        {
            phases.Register(_phaseActions[i].Phase, _phaseActions[i].Action);
        }

        context.RegisterService(phases);
        try
        {
            // 5) 按注册顺序初始化子系统，再返回 Engine 门面。
            lifecycle.Initialize(context);
            return new Engine(context, phases, lifecycle);
        }
        catch
        {
            jobs.Dispose();
            throw;
        }
    }

    private EngineLifecycle BuildLifecycle()
    {
        EngineLifecycle lifecycle = new();
        for (int i = 0; i < _subsystems.Count; i++)
        {
            lifecycle.Register(_subsystems[i]);
        }

        return lifecycle;
    }

    private SceneService BuildSceneService(EngineOptions options)
    {
        SceneService service = new(options.ContentRoot);
        for (int i = 0; i < _scenes.Count; i++)
        {
            service.Register(_scenes[i]);
        }

        if (options.StartScene is not null && !service.TryGet(options.StartScene, out _))
        {
            service.Register(new SceneDescriptor(options.StartScene));
        }

        if (options.StartScene is not null)
        {
            _ = service.SwitchTo(options.StartScene);
        }

        return service;
    }
}
