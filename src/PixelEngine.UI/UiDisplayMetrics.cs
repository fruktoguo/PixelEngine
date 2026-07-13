namespace PixelEngine.UI;

/// <summary>
/// 当前 presentation 与显示器度量。framebuffer scale 与 raw physical DPI 明确分离。
/// </summary>
/// <param name="PresentationWidth">Game presentation surface 像素宽度。</param>
/// <param name="PresentationHeight">Game presentation surface 像素高度。</param>
/// <param name="FramebufferScaleX">平台逻辑坐标到 framebuffer 的 X 缩放。</param>
/// <param name="FramebufferScaleY">平台逻辑坐标到 framebuffer 的 Y 缩放。</param>
/// <param name="ActualPhysicalDpi">平台报告的 raw physical DPI；未知时为 null。</param>
/// <param name="MonitorId">当前显示器的不透明平台标识。</param>
/// <param name="MetricsRevision">在帧边界提交的显示度量版本。</param>
public readonly record struct UiDisplayMetrics(
    int PresentationWidth,
    int PresentationHeight,
    float FramebufferScaleX,
    float FramebufferScaleY,
    float? ActualPhysicalDpi,
    nint MonitorId,
    long MetricsRevision)
{
    /// <summary>
    /// 从旧 UI 视口构造兼容度量；DPI scale 只作为 framebuffer scale，不会伪装成 physical DPI。
    /// </summary>
    /// <param name="viewport">旧 UI 视口。</param>
    /// <param name="actualPhysicalDpi">可选 raw physical DPI。</param>
    /// <param name="monitorId">显示器标识。</param>
    /// <param name="metricsRevision">度量版本。</param>
    /// <returns>分层后的显示度量。</returns>
    public static UiDisplayMetrics FromViewport(
        in UiViewport viewport,
        float? actualPhysicalDpi = null,
        nint monitorId = 0,
        long metricsRevision = 0)
    {
        viewport.Validate();
        return new UiDisplayMetrics(
            viewport.Width,
            viewport.Height,
            viewport.DpiScale,
            viewport.DpiScale,
            actualPhysicalDpi,
            monitorId,
            metricsRevision);
    }

    /// <summary>
    /// 将 Rendering-owned monitor snapshot 与当前 game presentation surface 合并为 UI 度量。
    /// </summary>
    /// <param name="presentationWidth">game presentation 像素宽度。</param>
    /// <param name="presentationHeight">game presentation 像素高度。</param>
    /// <param name="displayMetrics">Rendering 在帧边界提交的 monitor snapshot。</param>
    /// <returns>CanvasScaler 可消费的分层 UI 度量。</returns>
    public static UiDisplayMetrics FromRendering(
        int presentationWidth,
        int presentationHeight,
        in Rendering.DisplayMetricsSnapshot displayMetrics)
    {
        displayMetrics.Validate();
        return new UiDisplayMetrics(
            presentationWidth,
            presentationHeight,
            displayMetrics.FramebufferScaleX,
            displayMetrics.FramebufferScaleY,
            displayMetrics.ActualPhysicalDpi,
            displayMetrics.MonitorId,
            displayMetrics.Revision);
    }

    /// <summary>
    /// 校验 presentation、framebuffer scale、raw physical DPI 与 revision。
    /// </summary>
    public void Validate()
    {
        if (PresentationWidth <= 0 || PresentationHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PresentationWidth), "UI presentation 宽高必须为正数。");
        }

        ValidateFinitePositive(FramebufferScaleX, nameof(FramebufferScaleX));
        ValidateFinitePositive(FramebufferScaleY, nameof(FramebufferScaleY));
        if (ActualPhysicalDpi is float physicalDpi)
        {
            ValidateFinitePositive(physicalDpi, nameof(ActualPhysicalDpi));
        }

        if (MetricsRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MetricsRevision), "显示度量 revision 不能为负数。");
        }
    }

    private static void ValidateFinitePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, "显示度量必须为有限正数。");
        }
    }
}
