using PixelEngine.Core.Diagnostics;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 过载降级控制器，按架构 §4.3 的顺序升级质量档位。
/// </summary>
public sealed class EngineOverloadController
{
    private readonly EngineOverloadOptions _options;
    private readonly BudgetMonitor _monitor;

    /// <summary>
    /// 创建过载降级控制器。
    /// </summary>
    public EngineOverloadController(EngineOverloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _monitor = new BudgetMonitor(options.FrameBudgetMs, options.SustainWindow);
        QualityTier = EngineQualityTier.Full;
    }

    /// <summary>
    /// 当前质量档位。
    /// </summary>
    public EngineQualityTier QualityTier { get; private set; }

    /// <summary>
    /// 连续超预算帧数。
    /// </summary>
    public int ConsecutiveOverBudgetFrames => _monitor.ConsecutiveOverBudgetFrames;

    /// <summary>
    /// 提交上一帧耗时，并在达到窗口时按五级降级链升级一档。
    /// </summary>
    public EngineQualityTier SubmitFrame(double frameMs)
    {
        _monitor.Submit(frameMs);
        if (_monitor.IsSustainedOverBudget &&
            _monitor.ConsecutiveOverBudgetFrames % _options.SustainWindow == 0)
        {
            QualityTier = Next(QualityTier);
        }

        return QualityTier;
    }

    /// <summary>
    /// 手动恢复到全质量；用于编辑器覆盖或场景切换后的显式复位。
    /// </summary>
    public void ResetToFullQuality()
    {
        QualityTier = EngineQualityTier.Full;
    }

    private static EngineQualityTier Next(EngineQualityTier current)
    {
        return current switch
        {
            EngineQualityTier.Full => EngineQualityTier.ReducedThermal,
            EngineQualityTier.ReducedThermal => EngineQualityTier.ReducedLighting,
            EngineQualityTier.ReducedLighting => EngineQualityTier.DistantChunkThrottle,
            EngineQualityTier.DistantChunkThrottle => EngineQualityTier.Sim30Hz,
            EngineQualityTier.Sim30Hz => EngineQualityTier.SlowMotion,
            EngineQualityTier.SlowMotion => EngineQualityTier.SlowMotion,
            _ => throw new ArgumentOutOfRangeException(nameof(current), current, "未知质量档位。"),
        };
    }
}
