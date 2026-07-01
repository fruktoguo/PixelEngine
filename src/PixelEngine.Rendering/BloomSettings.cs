namespace PixelEngine.Rendering;

/// <summary>
/// Bloom pass 设置。
/// </summary>
/// <param name="Mode">Bloom 模式。</param>
/// <param name="Threshold">bright-pass 亮度阈值。</param>
/// <param name="Intensity">最终叠加强度。</param>
/// <param name="Iterations">dual-Kawase mip 层数或 Gaussian 迭代次数。</param>
/// <param name="KawaseOffset">dual-Kawase 采样偏移。</param>
public readonly record struct BloomSettings(
    BloomMode Mode,
    float Threshold,
    float Intensity,
    int Iterations,
    float KawaseOffset)
{
    /// <summary>
    /// 默认 bloom 设置。
    /// </summary>
    public static BloomSettings Default => new(BloomMode.DualKawase, 0.75f, 0.8f, 4, 1.5f);

    /// <summary>
    /// 校验设置并返回裁剪后的安全值。
    /// </summary>
    /// <returns>规范化后的设置。</returns>
    public BloomSettings Normalize()
    {
        if (!float.IsFinite(Threshold))
        {
            throw new ArgumentOutOfRangeException(nameof(Threshold), "Bloom threshold 必须为有限数值。");
        }

        if (!float.IsFinite(Intensity) || Intensity < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Intensity), "Bloom intensity 必须为非负有限数值。");
        }

        if (Iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Iterations), "Bloom iterations 必须为正数。");
        }

        if (!float.IsFinite(KawaseOffset))
        {
            throw new ArgumentOutOfRangeException(nameof(KawaseOffset), "Bloom Kawase offset 必须为正有限数值。");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(KawaseOffset);

        return this with
        {
            Threshold = Math.Clamp(Threshold, 0f, 8f),
            Iterations = Math.Clamp(Iterations, 1, 8),
            KawaseOffset = Math.Clamp(KawaseOffset, 0.25f, 8f),
        };
    }
}
