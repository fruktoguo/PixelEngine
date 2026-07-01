namespace PixelEngine.Rendering;

/// <summary>
/// BGRA8 CPU render buffer 后备存储。颜色在渲染相位生成，不写入 sim cell，守护架构 §7.1 / 不变式 #7。
/// </summary>
public sealed class RenderBuffer
{
    private uint[] _pixels;

    /// <summary>
    /// 创建 pinned BGRA8 render buffer。
    /// </summary>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    public RenderBuffer(int width, int height)
    {
        ValidateSize(width, height);
        Width = width;
        Height = height;
        _pixels = GC.AllocateArray<uint>(checked(width * height), pinned: true);
    }

    /// <summary>
    /// 宽度。
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// 高度。
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// buffer 字节数。
    /// </summary>
    public int ByteLength => checked(_pixels.Length * sizeof(uint));

    /// <summary>
    /// BGRA8 像素视图。
    /// </summary>
    public Span<uint> Pixels => _pixels;

    /// <summary>
    /// 调整 buffer 尺寸。尺寸未变化时不重新分配。
    /// </summary>
    /// <param name="width">新宽度。</param>
    /// <param name="height">新高度。</param>
    public void Resize(int width, int height)
    {
        ValidateSize(width, height);
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        _pixels = GC.AllocateArray<uint>(checked(width * height), pinned: true);
    }

    /// <summary>
    /// 验证上传矩形在 buffer 范围内。
    /// </summary>
    /// <param name="rect">上传矩形。</param>
    /// <exception cref="ArgumentOutOfRangeException">矩形越界时抛出。</exception>
    public void ValidateRect(PixelUploadRect rect)
    {
        if (rect.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), "上传矩形必须非空。");
        }

        if (rect.X < 0 || rect.Y < 0 ||
            rect.Width > Width - rect.X ||
            rect.Height > Height - rect.Y)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), "上传矩形越过 render buffer 边界。");
        }
    }

    private static void ValidateSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "render buffer 尺寸必须为正数。");
        }
    }
}
