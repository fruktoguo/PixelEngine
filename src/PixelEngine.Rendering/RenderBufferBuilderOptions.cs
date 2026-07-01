namespace PixelEngine.Rendering;

/// <summary>
/// CPU render buffer 构建参数。
/// </summary>
public sealed record RenderBufferBuilderOptions
{
    /// <summary>
    /// 温度 glow 起始阈值。
    /// </summary>
    public float TemperatureGlowThreshold { get; init; } = 80f;

    /// <summary>
    /// 温度每超过阈值 1 度增加的 glow 强度。
    /// </summary>
    public float TemperatureGlowScale { get; init; } = 0.02f;

    /// <summary>
    /// 每个并行任务至少处理的行数。
    /// </summary>
    public int MinRowsPerJob { get; init; } = 16;
}
