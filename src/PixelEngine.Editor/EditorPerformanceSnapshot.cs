using PixelEngine.Core.Diagnostics;
using PixelEngine.Rendering;

namespace PixelEngine.Editor;

/// <summary>
/// 性能 HUD 的只读数据入口，聚合 plan/02 计数器、计时器与 Hosting 运行态。
/// </summary>
/// <param name="Counters">引擎计数器。</param>
/// <param name="Profiler">帧计时器；未接入时 HUD 显示计数项并把耗时视为 0。</param>
/// <param name="Runtime">运行节奏与降级状态。</param>
/// <param name="PresentationControl">可选窗口 present 控制器，用于 HUD 运行时切换 VSync。</param>
public readonly record struct EditorPerformanceSnapshot(
    EngineCounters? Counters,
    FrameProfiler? Profiler,
    EditorRuntimeDiagnostics Runtime,
    IRenderPresentationControl? PresentationControl = null)
{
    /// <summary>
    /// 从计数器创建性能快照。
    /// </summary>
    public static EditorPerformanceSnapshot FromCounters(EngineCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        return new EditorPerformanceSnapshot(counters, null, EditorRuntimeDiagnostics.FullQuality);
    }

    /// <summary>
    /// 从计数器、profiler 与运行态创建性能快照。
    /// </summary>
    public static EditorPerformanceSnapshot Create(
        EngineCounters counters,
        FrameProfiler? profiler,
        EditorRuntimeDiagnostics runtime)
    {
        return Create(counters, profiler, runtime, null);
    }

    /// <summary>
    /// 从计数器、profiler、运行态与 present 控制器创建性能快照。
    /// </summary>
    public static EditorPerformanceSnapshot Create(
        EngineCounters counters,
        FrameProfiler? profiler,
        EditorRuntimeDiagnostics runtime,
        IRenderPresentationControl? presentationControl)
    {
        ArgumentNullException.ThrowIfNull(counters);
        return new EditorPerformanceSnapshot(counters, profiler, runtime, presentationControl);
    }

    /// <summary>
    /// 上一帧主相位耗时，单位毫秒。
    /// </summary>
    public ReadOnlySpan<double> LastFrame => Profiler is null ? [] : Profiler.LastFrame;

    /// <summary>
    /// 上一帧细分相位耗时，单位毫秒。
    /// </summary>
    public ReadOnlySpan<double> LastSubFrame => Profiler is null ? [] : Profiler.LastSubFrame;

    /// <summary>
    /// 上一帧真实墙钟耗时，单位毫秒。
    /// </summary>
    public double LastWallMilliseconds => Profiler?.LastWallMilliseconds ?? 0.0;

    /// <summary>
    /// 当前 sim 频率。
    /// </summary>
    public double SimHz => Counters?.SimHz ?? 0.0;
}
