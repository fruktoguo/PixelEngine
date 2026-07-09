namespace PixelEngine.UI;

/// <summary>
/// 解码后的 UI 图片位图，以 BGRA 打包的 <c>uint</c> 数组存储像素。
/// </summary>
internal readonly struct UiImageBitmap
{
    /// <summary>
    /// 创建已验证尺寸的 UI 位图。
    /// </summary>
    /// <param name="width">图片宽度（像素）。</param>
    /// <param name="height">图片高度（像素）。</param>
    /// <param name="rgba">BGRA 打包像素数组，长度必须为 width × height。</param>
    internal UiImageBitmap(int width, int height, uint[] rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length != checked(width * height))
        {
            throw new ArgumentException("图片像素数量与宽高不一致。", nameof(rgba));
        }

        Width = width;
        Height = height;
        Rgba = rgba;
    }

    /// <summary>
    /// 图片宽度（像素）。
    /// </summary>
    internal int Width { get; }

    /// <summary>
    /// 图片高度（像素）。
    /// </summary>
    internal int Height { get; }

    /// <summary>
    /// BGRA 打包像素数据，行优先排列。
    /// </summary>
    internal uint[] Rgba { get; }
}
