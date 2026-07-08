namespace PixelEngine.Rendering;

/// <summary>
/// UI present 目标区域，坐标以默认 framebuffer 左上角为原点。
/// </summary>
/// <param name="X">目标左上角 X。</param>
/// <param name="Y">目标左上角 Y。</param>
/// <param name="Width">目标宽度。</param>
/// <param name="Height">目标高度。</param>
/// <param name="DpiScale">目标 DPI 缩放。</param>
public readonly record struct UiPresentTarget(int X, int Y, int Width, int Height, float DpiScale)
{
    /// <summary>
    /// 由世界呈现区域创建 UI present 目标。
    /// </summary>
    /// <param name="viewport">世界呈现区域。</param>
    /// <returns>UI present 目标。</returns>
    public static UiPresentTarget FromPresentationViewport(in PresentationViewport viewport)
    {
        return new UiPresentTarget(viewport.X, viewport.Y, viewport.Width, viewport.Height, 1f);
    }

    /// <summary>
    /// 目标是否具有正面积和有限 DPI。
    /// </summary>
    public bool IsValid => Width > 0 && Height > 0 && float.IsFinite(DpiScale) && DpiScale > 0f;

    /// <summary>
    /// 与该目标等价的 scissor 矩形。
    /// </summary>
    public UiScissorRect Scissor => new(X, Y, Width, Height);

    /// <summary>
    /// 校验目标区域。
    /// </summary>
    public void Validate()
    {
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "UI present 目标宽高必须大于 0。");
        }

        if (!float.IsFinite(DpiScale) || DpiScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(DpiScale), "UI present 目标 DPI 缩放必须为有限正数。");
        }
    }
}
