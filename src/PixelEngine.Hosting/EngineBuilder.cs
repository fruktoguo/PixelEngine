using System.Runtime;
using PixelEngine.Core;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Events;
using PixelEngine.Core.Threading;
using PixelEngine.Core.Time;

namespace PixelEngine.Hosting;

/// <summary>
/// Engine fluent 构建器，负责生成不可变配置并装配 Core 运行时服务。
/// </summary>
public sealed class EngineBuilder
{
    private int _windowWidth = EngineOptions.DefaultWindowWidth;
    private int _windowHeight = EngineOptions.DefaultWindowHeight;
    private int _internalWidth = EngineOptions.DefaultInternalWidth;
    private int _internalHeight = EngineOptions.DefaultInternalHeight;
    private int _workerCount;
    private EngineGcMode _gcMode = EngineGcMode.SustainedLowLatency;
    private bool _enableEditor;
    private bool _headless;
    private bool _deterministicMode;
    private bool _enableGpu = true;
    private string _contentRoot = "content";
    private string? _startScene;
    private double _simHz = EngineConstants.DefaultSimHz;
    private int _eventCapacityPerChannel = EngineOptions.DefaultEventCapacityPerChannel;

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
    /// 构建 Engine 并完成 Core 服务装配。
    /// </summary>
    public Engine Build()
    {
        EngineOptions options = new(
            _windowWidth,
            _windowHeight,
            _internalWidth,
            _internalHeight,
            _workerCount,
            _gcMode,
            _enableEditor,
            _headless,
            _deterministicMode,
            _enableGpu,
            _contentRoot,
            _startScene,
            _simHz,
            _eventCapacityPerChannel);
        GCSettings.LatencyMode = options.GcMode.ToLatencyMode();
        JobSystem jobs = new(options.WorkerCount);
        FrameClock clock = new(options.SimHz);
        EventBus events = new(options.EventCapacityPerChannel);
        EngineCounters counters = new()
        {
            SimHz = options.SimHz,
        };
        EngineContext context = new(options, jobs, clock, events, counters);
        context.RegisterService(context);
        context.RegisterService(options);
        context.RegisterService(jobs);
        context.RegisterService(clock);
        context.RegisterService(events);
        context.RegisterService(counters);
        return new Engine(context);
    }
}
