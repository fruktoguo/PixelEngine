namespace PixelEngine.Rendering;

/// <summary>
/// Rendering 在帧边界发布的显示器度量；不包含游戏 presentation 分辨率。
/// </summary>
/// <param name="MonitorId">当前显示器的不透明平台标识。</param>
/// <param name="FramebufferScaleX">平台逻辑坐标到 framebuffer 的 X 缩放。</param>
/// <param name="FramebufferScaleY">平台逻辑坐标到 framebuffer 的 Y 缩放。</param>
/// <param name="ActualPhysicalDpi">平台 raw monitor DPI；不可用时为 null。</param>
/// <param name="Revision">只在帧边界递增的度量版本。</param>
public readonly record struct DisplayMetricsSnapshot(
    nint MonitorId,
    float FramebufferScaleX,
    float FramebufferScaleY,
    float? ActualPhysicalDpi,
    long Revision)
{
    /// <summary>
    /// 校验 framebuffer scale、可选 raw physical DPI 与 revision。
    /// </summary>
    public void Validate()
    {
        ValidateFinitePositive(FramebufferScaleX, nameof(FramebufferScaleX));
        ValidateFinitePositive(FramebufferScaleY, nameof(FramebufferScaleY));
        if (ActualPhysicalDpi is float physicalDpi)
        {
            ValidateFinitePositive(physicalDpi, nameof(ActualPhysicalDpi));
        }

        if (Revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Revision), "Display metrics revision 不能为负数。");
        }
    }

    private static void ValidateFinitePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Display metrics 数值必须为有限正数。");
        }
    }
}
