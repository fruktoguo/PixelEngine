using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Events;
using PixelEngine.Core.Threading;
using PixelEngine.Core.Time;

namespace PixelEngine.Hosting;

/// <summary>
/// 引擎运行上下文，集中持有 Core 服务、诊断与上层服务定位表。
/// </summary>
public sealed class EngineContext
{
    private readonly Dictionary<Type, object> _services = [];

    /// <summary>
    /// 创建引擎上下文。
    /// </summary>
    public EngineContext(
        EngineOptions options,
        JobSystem jobs,
        FrameClock clock,
        EventBus events,
        EngineCounters counters)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(counters);

        Options = options;
        Jobs = jobs;
        Clock = clock;
        Events = events;
        Counters = counters;
        QualityTier = EngineQualityTier.Full;
    }

    /// <summary>
    /// 当前引擎运行配置。
    /// </summary>
    public EngineOptions Options { get; }

    /// <summary>
    /// 全局持久 worker 线程池。
    /// </summary>
    public JobSystem Jobs { get; }

    /// <summary>
    /// 固定步长帧时钟。
    /// </summary>
    public FrameClock Clock { get; }

    /// <summary>
    /// 引擎事件总线。
    /// </summary>
    public EventBus Events { get; }

    /// <summary>
    /// 引擎诊断计数器。
    /// </summary>
    public EngineCounters Counters { get; }

    /// <summary>
    /// 当前质量档位，由 Hosting 过载降级逻辑下发。
    /// </summary>
    public EngineQualityTier QualityTier { get; private set; }

    /// <summary>
    /// 设置当前质量档位。
    /// </summary>
    public void SetQualityTier(EngineQualityTier qualityTier)
    {
        QualityTier = qualityTier;
    }

    /// <summary>
    /// 注册脚本、Demo 或 Editor 可消费的服务实现。
    /// </summary>
    public void RegisterService<TService>(TService service)
        where TService : notnull
    {
        _services[typeof(TService)] = service;
    }

    /// <summary>
    /// 尝试获取指定服务。
    /// </summary>
    public bool TryGetService<TService>(out TService service)
        where TService : notnull
    {
        if (_services.TryGetValue(typeof(TService), out object? value))
        {
            service = (TService)value;
            return true;
        }

        service = default!;
        return false;
    }

    /// <summary>
    /// 获取指定服务；未注册时抛出明确异常。
    /// </summary>
    public TService GetService<TService>()
        where TService : notnull
    {
        return TryGetService(out TService service)
            ? service
            : throw new InvalidOperationException($"服务 {typeof(TService).FullName} 尚未注册。");
    }
}
