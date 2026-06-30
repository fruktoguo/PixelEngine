namespace PixelEngine.Core.Diagnostics;

/// <summary>
/// 记录连续超预算帧数，为 Hosting 降级策略提供数据源。
/// </summary>
public sealed class BudgetMonitor
{
    private readonly double _budgetMs;
    private readonly int _sustainWindow;

    /// <summary>
    /// 创建预算监测器。
    /// </summary>
    /// <param name="budgetMs">单帧预算毫秒数。</param>
    /// <param name="sustainWindow">需要连续超预算的帧数。</param>
    public BudgetMonitor(double budgetMs, int sustainWindow)
    {
        if (!double.IsFinite(budgetMs) || budgetMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetMs), budgetMs, "预算必须是正有限毫秒数。");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sustainWindow);
        _budgetMs = budgetMs;
        _sustainWindow = sustainWindow;
    }

    /// <summary>
    /// 获取连续超预算帧数。
    /// </summary>
    public int ConsecutiveOverBudgetFrames { get; private set; }

    /// <summary>
    /// 获取是否已连续超预算达到窗口长度。
    /// </summary>
    public bool IsSustainedOverBudget => ConsecutiveOverBudgetFrames >= _sustainWindow;

    /// <summary>
    /// 提交一帧耗时。
    /// </summary>
    /// <param name="frameMs">帧耗时毫秒数。</param>
    public void Submit(double frameMs)
    {
        if (!double.IsFinite(frameMs) || frameMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameMs), frameMs, "帧耗时必须是非负有限毫秒数。");
        }

        ConsecutiveOverBudgetFrames = frameMs > _budgetMs
            ? ConsecutiveOverBudgetFrames + 1
            : 0;
    }
}
