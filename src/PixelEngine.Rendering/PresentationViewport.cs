namespace PixelEngine.Rendering;

/// <summary>
/// 内部渲染画布在窗口 framebuffer 中的等比呈现区域。
/// </summary>
public readonly record struct PresentationViewport(
    int X,
    int Y,
    int Width,
    int Height,
    int SourceWidth,
    int SourceHeight,
    int TargetWidth,
    int TargetHeight)
{
    /// <summary>
    /// 根据内部画布与 framebuffer 尺寸计算居中等比缩放区域。
    /// </summary>
    public static PresentationViewport Fit(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);

        double scale = Math.Min(targetWidth / (double)sourceWidth, targetHeight / (double)sourceHeight);
        int width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        int height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        width = Math.Min(width, targetWidth);
        height = Math.Min(height, targetHeight);
        int x = (targetWidth - width) / 2;
        int y = (targetHeight - height) / 2;
        return new PresentationViewport(x, y, width, height, sourceWidth, sourceHeight, targetWidth, targetHeight);
    }

    /// <summary>
    /// 把 framebuffer 像素坐标映射到内部画布坐标。输入与输出都使用左上角原点。
    /// </summary>
    public (float X, float Y) MapFramebufferToSource(float framebufferX, float framebufferY)
    {
        float sourceX = (framebufferX - X) * SourceWidth / Width;
        float topY = TargetHeight - Y - Height;
        float sourceY = (framebufferY - topY) * SourceHeight / Height;
        return (sourceX, sourceY);
    }
}
