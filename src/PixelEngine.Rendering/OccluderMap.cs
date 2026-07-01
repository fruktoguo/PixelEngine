namespace PixelEngine.Rendering;

/// <summary>
/// CPU 侧 solidity / occluder 字节图。每个元素对应一个视口采样像素，0 表示透明，255 表示完全遮挡。
/// </summary>
public sealed class OccluderMap
{
    private readonly byte[] _solidity;

    /// <summary>
    /// 创建空的 CPU occluder map。
    /// </summary>
    /// <param name="width">视口采样宽度，单位为 render pixel。</param>
    /// <param name="height">视口采样高度，单位为 render pixel。</param>
    public OccluderMap(int width, int height)
    {
        ValidateSize(width, height);
        Width = width;
        Height = height;
        _solidity = GC.AllocateArray<byte>(checked(width * height), pinned: true);
    }

    /// <summary>
    /// 从已有 solidity 字节图创建 CPU occluder map。
    /// </summary>
    /// <param name="width">视口采样宽度，单位为 render pixel。</param>
    /// <param name="height">视口采样高度，单位为 render pixel。</param>
    /// <param name="solidity">长度必须等于 <paramref name="width"/> × <paramref name="height"/> 的 solidity 数据。</param>
    public OccluderMap(int width, int height, ReadOnlySpan<byte> solidity)
        : this(width, height)
    {
        CopyFrom(solidity, width, height);
    }

    /// <summary>
    /// 从渲染副输出中的 occluder 通道创建 CPU occluder map，不触发 GPU readback。
    /// </summary>
    /// <param name="aux">RenderBufferBuilder 生成的 CPU 副输出。</param>
    public OccluderMap(RenderAuxBuffers aux)
    {
        ArgumentNullException.ThrowIfNull(aux);
        Width = aux.Width;
        Height = aux.Height;
        _solidity = GC.AllocateArray<byte>(checked(Width * Height), pinned: true);
        aux.Occluder.CopyTo(_solidity);
    }

    /// <summary>
    /// 视口采样宽度，单位为 render pixel。
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// 视口采样高度，单位为 render pixel。
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// solidity 元素数量。
    /// </summary>
    public int Length => _solidity.Length;

    /// <summary>
    /// 可写 solidity 数据视图。索引顺序为 row-major：screenY × Width + screenX。
    /// </summary>
    public Span<byte> Solidity => _solidity;

    /// <summary>
    /// 清空 occluder map，所有采样点变为透明。
    /// </summary>
    public void Clear()
    {
        _solidity.AsSpan().Clear();
    }

    /// <summary>
    /// 设置一个视口采样点的遮挡强度。
    /// </summary>
    /// <param name="screenX">视口内 render pixel X 坐标。</param>
    /// <param name="screenY">视口内 render pixel Y 坐标。</param>
    /// <param name="solidity">遮挡强度，0 表示透明，255 表示完全遮挡。</param>
    public void Set(int screenX, int screenY, byte solidity)
    {
        _solidity[IndexOf(screenX, screenY)] = solidity;
    }

    /// <summary>
    /// 读取一个视口采样点的遮挡强度。
    /// </summary>
    /// <param name="screenX">视口内 render pixel X 坐标。</param>
    /// <param name="screenY">视口内 render pixel Y 坐标。</param>
    /// <returns>遮挡强度，0 表示透明，255 表示完全遮挡。</returns>
    public byte Get(int screenX, int screenY)
    {
        return _solidity[IndexOf(screenX, screenY)];
    }

    /// <summary>
    /// 从已有 solidity 字节图更新当前 map。
    /// </summary>
    /// <param name="solidity">长度必须等于 <paramref name="width"/> × <paramref name="height"/> 的 solidity 数据。</param>
    /// <param name="width">输入数据宽度，单位为 render pixel。</param>
    /// <param name="height">输入数据高度，单位为 render pixel。</param>
    public void CopyFrom(ReadOnlySpan<byte> solidity, int width, int height)
    {
        ValidateMatchingSize(width, height);
        if (solidity.Length != _solidity.Length)
        {
            throw new ArgumentException("输入 solidity 数据长度必须与 occluder map 尺寸一致。", nameof(solidity));
        }

        solidity.CopyTo(_solidity);
    }

    /// <summary>
    /// 从渲染副输出中的 occluder 通道更新当前 map，不触发 GPU readback。
    /// </summary>
    /// <param name="aux">RenderBufferBuilder 生成的 CPU 副输出。</param>
    public void CopyFrom(RenderAuxBuffers aux)
    {
        ArgumentNullException.ThrowIfNull(aux);
        CopyFrom(aux.Occluder, aux.Width, aux.Height);
    }

    /// <summary>
    /// 将当前 solidity 数据复制到目标 span。
    /// </summary>
    /// <param name="destination">长度必须不小于 <see cref="Length"/> 的目标缓冲。</param>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < _solidity.Length)
        {
            throw new ArgumentException("目标缓冲长度不足。", nameof(destination));
        }

        _solidity.CopyTo(destination);
    }

    private int IndexOf(int screenX, int screenY)
    {
        return (uint)screenX >= (uint)Width
            ? throw new ArgumentOutOfRangeException(nameof(screenX), "screenX 越过 occluder map 边界。")
            : (uint)screenY >= (uint)Height
            ? throw new ArgumentOutOfRangeException(nameof(screenY), "screenY 越过 occluder map 边界。")
            : (screenY * Width) + screenX;
    }

    private void ValidateMatchingSize(int width, int height)
    {
        ValidateSize(width, height);
        if (width != Width || height != Height)
        {
            throw new ArgumentException("输入 solidity 尺寸必须与 occluder map 尺寸一致。");
        }
    }

    private static void ValidateSize(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "occluder map 宽度必须为正数。");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "occluder map 高度必须为正数。");
        }
    }
}
