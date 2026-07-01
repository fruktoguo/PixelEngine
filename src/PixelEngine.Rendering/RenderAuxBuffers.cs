namespace PixelEngine.Rendering;

/// <summary>
/// render buffer 构建阶段生成的副输出，供光照和 bloom 管线消费。
/// </summary>
public sealed class RenderAuxBuffers
{
    private uint[] _emissive;
    private byte[] _occluder;

    /// <summary>
    /// 创建副输出 buffer。
    /// </summary>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    public RenderAuxBuffers(int width, int height)
    {
        ValidateSize(width, height);
        Width = width;
        Height = height;
        _emissive = GC.AllocateArray<uint>(checked(width * height), pinned: true);
        _occluder = GC.AllocateArray<byte>(checked(width * height), pinned: true);
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
    /// BGRA8 emissive 副输出。
    /// </summary>
    public Span<uint> Emissive => _emissive;

    /// <summary>
    /// solidity / occluder map，0 表示透明，255 表示遮挡。
    /// </summary>
    public Span<byte> Occluder => _occluder;

    /// <summary>
    /// 调整副输出尺寸。
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
        _emissive = GC.AllocateArray<uint>(checked(width * height), pinned: true);
        _occluder = GC.AllocateArray<byte>(checked(width * height), pinned: true);
    }

    /// <summary>
    /// 清空副输出。
    /// </summary>
    public void Clear()
    {
        _emissive.AsSpan().Clear();
        _occluder.AsSpan().Clear();
    }

    private static void ValidateSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "副输出 buffer 尺寸必须为正数。");
        }
    }
}
