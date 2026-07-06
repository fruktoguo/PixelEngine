namespace PixelEngine.UI;

internal readonly struct UiImageBitmap
{
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

    internal int Width { get; }

    internal int Height { get; }

    internal uint[] Rgba { get; }
}
