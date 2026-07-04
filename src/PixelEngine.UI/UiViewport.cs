namespace PixelEngine.UI;

/// <summary>
/// UI 视口。
/// </summary>
/// <param name="X">左上角 x。</param>
/// <param name="Y">左上角 y。</param>
/// <param name="Width">宽度。</param>
/// <param name="Height">高度。</param>
/// <param name="DpiScale">DPI 缩放。</param>
public readonly record struct UiViewport(int X, int Y, int Width, int Height, float DpiScale)
{
    /// <summary>
    /// 校验视口。
    /// </summary>
    public void Validate()
    {
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "UI 视口宽高必须大于 0。");
        }

        if (DpiScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DpiScale), "UI DPI 缩放必须大于 0。");
        }
    }
}
