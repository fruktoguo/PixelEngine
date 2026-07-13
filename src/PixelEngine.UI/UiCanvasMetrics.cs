namespace PixelEngine.UI;

/// <summary>
/// Canvas layout、raster、hit-test、IME 与预览共享的解析后度量。
/// </summary>
public readonly record struct UiCanvasMetrics
{
    internal UiCanvasMetrics(
        int presentationWidth,
        int presentationHeight,
        float logicalWidth,
        float logicalHeight,
        float scaleFactor,
        float resolvedReferencePixelsPerUnit,
        float framebufferScaleX,
        float framebufferScaleY,
        float effectivePhysicalDpi,
        bool usedFallbackPhysicalDpi,
        long displayMetricsRevision)
    {
        PresentationWidth = presentationWidth;
        PresentationHeight = presentationHeight;
        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
        ScaleFactor = scaleFactor;
        ResolvedReferencePixelsPerUnit = resolvedReferencePixelsPerUnit;
        FramebufferScaleX = framebufferScaleX;
        FramebufferScaleY = framebufferScaleY;
        EffectivePhysicalDpi = effectivePhysicalDpi;
        UsedFallbackPhysicalDpi = usedFallbackPhysicalDpi;
        DisplayMetricsRevision = displayMetricsRevision;
    }

    /// <summary>presentation/render surface 像素宽度。</summary>
    public int PresentationWidth { get; }

    /// <summary>presentation/render surface 像素高度。</summary>
    public int PresentationHeight { get; }

    /// <summary>Canvas 逻辑宽度。</summary>
    public float LogicalWidth { get; }

    /// <summary>Canvas 逻辑高度。</summary>
    public float LogicalHeight { get; }

    /// <summary>Canvas logical unit 到 presentation pixel 的缩放。</summary>
    public float ScaleFactor { get; }

    /// <summary>解析后的 reference pixels-per-unit。</summary>
    public float ResolvedReferencePixelsPerUnit { get; }

    /// <summary>平台逻辑坐标到 framebuffer 的 X 缩放；不等同于 Canvas scale。</summary>
    public float FramebufferScaleX { get; }

    /// <summary>平台逻辑坐标到 framebuffer 的 Y 缩放；不等同于 Canvas scale。</summary>
    public float FramebufferScaleY { get; }

    /// <summary>解析时使用的 physical DPI；非物理模式下为 0。</summary>
    public float EffectivePhysicalDpi { get; }

    /// <summary>Constant Physical Size 是否使用了入盘 fallback DPI。</summary>
    public bool UsedFallbackPhysicalDpi { get; }

    /// <summary>产生本度量的 display-metrics revision。</summary>
    public long DisplayMetricsRevision { get; }

    /// <summary>后端整数 layout viewport 宽度。</summary>
    public int LayoutWidth => Math.Max(1, (int)MathF.Round(LogicalWidth));

    /// <summary>后端整数 layout viewport 高度。</summary>
    public int LayoutHeight => Math.Max(1, (int)MathF.Round(LogicalHeight));

    /// <summary>兼容旧后端的物理 presentation 视口。</summary>
    public UiViewport PresentationViewport => new(
        0,
        0,
        PresentationWidth,
        PresentationHeight,
        FramebufferScaleX);

    /// <summary>
    /// 把 presentation point 映射到 Canvas logical point，并拒绝 surface 外坐标。
    /// </summary>
    /// <param name="presentationX">presentation X。</param>
    /// <param name="presentationY">presentation Y。</param>
    /// <param name="logicalX">Canvas logical X。</param>
    /// <param name="logicalY">Canvas logical Y。</param>
    /// <returns>输入有限且位于 presentation surface 内时返回 true。</returns>
    public bool TryMapPresentationToLogical(
        float presentationX,
        float presentationY,
        out float logicalX,
        out float logicalY)
    {
        if (!float.IsFinite(presentationX) ||
            !float.IsFinite(presentationY) ||
            presentationX < 0f ||
            presentationY < 0f ||
            presentationX >= PresentationWidth ||
            presentationY >= PresentationHeight)
        {
            logicalX = 0f;
            logicalY = 0f;
            return false;
        }

        logicalX = presentationX / ScaleFactor;
        logicalY = presentationY / ScaleFactor;
        return true;
    }

    /// <summary>
    /// 把 Canvas logical point 映射到 presentation point。
    /// </summary>
    /// <param name="logicalX">Canvas logical X。</param>
    /// <param name="logicalY">Canvas logical Y。</param>
    /// <returns>presentation point。</returns>
    public (float X, float Y) MapLogicalToPresentation(float logicalX, float logicalY)
    {
        return !float.IsFinite(logicalX)
            ? throw new ArgumentOutOfRangeException(nameof(logicalX), "Canvas logical X 必须是有限数值。")
            : !float.IsFinite(logicalY)
                ? throw new ArgumentOutOfRangeException(nameof(logicalY), "Canvas logical Y 必须是有限数值。")
                : (logicalX * ScaleFactor, logicalY * ScaleFactor);
    }

    /// <summary>
    /// 把 presentation delta 映射为 Canvas logical delta。
    /// </summary>
    /// <param name="presentationDelta">presentation 像素差值。</param>
    /// <returns>Canvas logical 差值。</returns>
    public float MapPresentationDeltaToLogical(float presentationDelta)
    {
        return float.IsFinite(presentationDelta) ? presentationDelta / ScaleFactor : 0f;
    }

    /// <summary>
    /// 把后端发布的 logical IME 几何映射到 presentation 坐标。
    /// </summary>
    /// <param name="geometry">Canvas logical IME 几何。</param>
    /// <returns>presentation 坐标中的 IME 几何。</returns>
    public UiImeGeometry MapLogicalImeGeometryToPresentation(in UiImeGeometry geometry)
    {
        return geometry.Transform(0f, 0f, ScaleFactor, ScaleFactor);
    }
}
