namespace PixelEngine.Rendering;

/// <summary>
/// CPU render buffer 构建参数。
/// </summary>
public sealed record RenderBufferBuilderOptions
{
    /// <summary>
    /// RenderStyle 差异化着色质量档；关闭后退回纯 palette / 纹理颜色。
    /// </summary>
    public RenderBufferStyleLevel StyleLevel { get; init; } = RenderBufferStyleLevel.Full;

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

/// <summary>
/// CPU render buffer 的材质样式着色质量档。
/// </summary>
public enum RenderBufferStyleLevel : byte
{
    /// <summary>
    /// 关闭描边、裂纹、流动高光、颗粒和危险脉动，退回基础颜色。
    /// </summary>
    Off,

    /// <summary>
    /// 启用材质定义驱动的完整样式着色。
    /// </summary>
    Full,
}
