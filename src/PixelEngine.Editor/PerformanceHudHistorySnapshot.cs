namespace PixelEngine.Editor;

/// <summary>Performance HUD 环形历史中的一帧及其精确聚合样本。</summary>
public readonly record struct PerformanceHudFrameSample(
    long FrameIndex,
    PerformanceHudSample Sample);

/// <summary>Performance HUD 当前 512 帧历史和全部滚动统计的不可变副本。</summary>
public sealed record PerformanceHudHistorySnapshot(
    int Capacity,
    int CapturedSampleCount,
    PerformanceHudFrameSample[] Samples,
    PerformanceHudStatistics FrameStatistics,
    PerformanceHudStatistics CpuStatistics,
    PerformanceHudStatistics GpuStatistics,
    PerformanceHudStatistics WaitStatistics,
    PerformanceHudStatistics EffectiveStatistics,
    PerformanceHudStatistics VariableWorkStatistics,
    PerformanceHudStatistics FixedOverheadStatistics);
