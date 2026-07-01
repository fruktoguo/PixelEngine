namespace PixelEngine.Editor;

/// <summary>
/// Hosting 可传给 Editor 的运行时节奏与降级状态快照；Editor 只读展示。
/// </summary>
/// <param name="TimeScale">时间膨胀系数，正常为 1。</param>
/// <param name="DegradationLevel">架构 §4.3 过载降级级别，0 表示全质量。</param>
/// <param name="DegradationName">降级级别显示名。</param>
/// <param name="ConsecutiveOverBudgetFrames">连续超预算帧数。</param>
public readonly record struct EditorRuntimeDiagnostics(
    double TimeScale,
    int DegradationLevel,
    string DegradationName,
    int ConsecutiveOverBudgetFrames)
{
    /// <summary>
    /// 全质量运行态。
    /// </summary>
    public static readonly EditorRuntimeDiagnostics FullQuality = new(1.0, 0, "Full", 0);

    /// <summary>
    /// 当前是否处于时间膨胀。
    /// </summary>
    public bool IsTimeDilated => TimeScale < 0.999;
}
