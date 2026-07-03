namespace PixelEngine.Editor;

/// <summary>
/// 性能 HUD 滚动窗口统计结果。
/// </summary>
public readonly record struct PerformanceHudStatistics(
    int SampleCount,
    double AverageMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    bool IsSteady,
    bool IsSpike)
{
    /// <summary>
    /// 没有足够稳态样本时的空统计。
    /// </summary>
    public static PerformanceHudStatistics Empty { get; } = new();
}
