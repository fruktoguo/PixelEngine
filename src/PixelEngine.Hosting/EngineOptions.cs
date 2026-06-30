using PixelEngine.Core;

namespace PixelEngine.Hosting;

/// <summary>
/// EngineBuilder 生成的不可变运行配置。
/// </summary>
public sealed class EngineOptions
{
    /// <summary>
    /// 创建引擎运行配置。
    /// </summary>
    public EngineOptions(
        int windowWidth,
        int windowHeight,
        int internalWidth,
        int internalHeight,
        int workerCount,
        EngineGcMode gcMode,
        bool enableEditor,
        bool headless,
        bool deterministicMode,
        bool enableGpu,
        string contentRoot,
        string? startScene,
        double simHz,
        int eventCapacityPerChannel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(internalWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(internalHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(workerCount);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        if (!double.IsFinite(simHz) || simHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simHz), simHz, "sim 频率必须是正有限数。");
        }

        if (eventCapacityPerChannel <= 0 || !System.Numerics.BitOperations.IsPow2(eventCapacityPerChannel))
        {
            throw new ArgumentOutOfRangeException(nameof(eventCapacityPerChannel), eventCapacityPerChannel, "事件通道容量必须是正的 2 的幂。");
        }

        WindowWidth = windowWidth;
        WindowHeight = windowHeight;
        InternalWidth = internalWidth;
        InternalHeight = internalHeight;
        WorkerCount = workerCount;
        GcMode = gcMode;
        EnableEditor = enableEditor;
        Headless = headless;
        DeterministicMode = deterministicMode;
        EnableGpu = enableGpu;
        ContentRoot = contentRoot;
        StartScene = startScene;
        SimHz = simHz;
        EventCapacityPerChannel = eventCapacityPerChannel;
    }

    /// <summary>
    /// 默认窗口宽度。
    /// </summary>
    public const int DefaultWindowWidth = 1280;

    /// <summary>
    /// 默认窗口高度。
    /// </summary>
    public const int DefaultWindowHeight = 720;

    /// <summary>
    /// 默认内部 sim 宽度。
    /// </summary>
    public const int DefaultInternalWidth = 640;

    /// <summary>
    /// 默认内部 sim 高度。
    /// </summary>
    public const int DefaultInternalHeight = 360;

    /// <summary>
    /// 默认事件通道容量。
    /// </summary>
    public const int DefaultEventCapacityPerChannel = 1024;

    /// <summary>
    /// 窗口宽度。
    /// </summary>
    public int WindowWidth { get; }

    /// <summary>
    /// 窗口高度。
    /// </summary>
    public int WindowHeight { get; }

    /// <summary>
    /// 内部 sim 宽度。
    /// </summary>
    public int InternalWidth { get; }

    /// <summary>
    /// 内部 sim 高度。
    /// </summary>
    public int InternalHeight { get; }

    /// <summary>
    /// JobSystem worker 数；0 表示按 CPU 数自动选择。
    /// </summary>
    public int WorkerCount { get; }

    /// <summary>
    /// 托管 GC 延迟模式。
    /// </summary>
    public EngineGcMode GcMode { get; }

    /// <summary>
    /// 是否启用 Editor。
    /// </summary>
    public bool EnableEditor { get; }

    /// <summary>
    /// 是否启用 headless 模式。
    /// </summary>
    public bool Headless { get; }

    /// <summary>
    /// 是否启用确定性模式。
    /// </summary>
    public bool DeterministicMode { get; }

    /// <summary>
    /// 是否允许 GPU 后端。
    /// </summary>
    public bool EnableGpu { get; }

    /// <summary>
    /// 内容根目录。
    /// </summary>
    public string ContentRoot { get; }

    /// <summary>
    /// 起始场景标识；为空时由 Demo 或宿主后续指定。
    /// </summary>
    public string? StartScene { get; }

    /// <summary>
    /// 固定 sim 频率。
    /// </summary>
    public double SimHz { get; }

    /// <summary>
    /// 每个事件类型通道的容量。
    /// </summary>
    public int EventCapacityPerChannel { get; }

    /// <summary>
    /// 创建默认配置。
    /// </summary>
    public static EngineOptions CreateDefault()
    {
        return new EngineOptions(
            DefaultWindowWidth,
            DefaultWindowHeight,
            DefaultInternalWidth,
            DefaultInternalHeight,
            workerCount: 0,
            gcMode: EngineGcMode.SustainedLowLatency,
            enableEditor: false,
            headless: false,
            deterministicMode: false,
            enableGpu: true,
            contentRoot: "content",
            startScene: null,
            simHz: EngineConstants.DefaultSimHz,
            eventCapacityPerChannel: DefaultEventCapacityPerChannel);
    }
}
