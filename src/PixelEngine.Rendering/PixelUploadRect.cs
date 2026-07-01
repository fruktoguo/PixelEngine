namespace PixelEngine.Rendering;

/// <summary>
/// 像素上传矩形，坐标位于 render buffer / world texture 的像素空间。
/// </summary>
/// <param name="X">左上角 X。</param>
/// <param name="Y">左上角 Y。</param>
/// <param name="Width">宽度。</param>
/// <param name="Height">高度。</param>
public readonly record struct PixelUploadRect(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// 判断矩形是否为空。
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
