namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 过载降级策略配置。
/// </summary>
public sealed class EngineOverloadOptions
{
    /// <summary>
    /// 创建过载降级配置。
    /// </summary>
    public EngineOverloadOptions(double frameBudgetMs, int sustainWindow)
    {
        if (!double.IsFinite(frameBudgetMs) || frameBudgetMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameBudgetMs), frameBudgetMs, "帧预算必须是正有限毫秒数。");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sustainWindow);
        FrameBudgetMs = frameBudgetMs;
        SustainWindow = sustainWindow;
    }

    /// <summary>
    /// 默认 60fps 帧预算，单位毫秒。
    /// </summary>
    public const double DefaultFrameBudgetMs = 1000.0 / 60.0;

    /// <summary>
    /// 默认连续超预算窗口。
    /// </summary>
    public const int DefaultSustainWindow = 3;

    /// <summary>
    /// 单帧预算，单位毫秒。
    /// </summary>
    public double FrameBudgetMs { get; }

    /// <summary>
    /// 连续超预算多少帧后升级一级质量档位。
    /// </summary>
    public int SustainWindow { get; }

    /// <summary>
    /// 创建默认过载降级配置。
    /// </summary>
    public static EngineOverloadOptions CreateDefault()
    {
        return new EngineOverloadOptions(DefaultFrameBudgetMs, DefaultSustainWindow);
    }
}
