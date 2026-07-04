namespace PixelEngine.Rendering;

/// <summary>
/// UI scissor 矩形，坐标以 framebuffer 左上角为原点。
/// </summary>
/// <param name="X">左上角 X。</param>
/// <param name="Y">左上角 Y。</param>
/// <param name="Width">宽度。</param>
/// <param name="Height">高度。</param>
public readonly record struct UiScissorRect(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// 校验矩形尺寸。
    /// </summary>
    public void Validate()
    {
        if (Width < 0 || Height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "UI scissor 尺寸不能为负。");
        }
    }
}
