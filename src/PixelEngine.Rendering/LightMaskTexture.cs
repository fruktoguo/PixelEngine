using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// R8 光照遮罩纹理，用于上传 occluder、fog-of-war reveal 或 1D shadow map。
/// </summary>
public sealed unsafe class LightMaskTexture : IDisposable
{
    private readonly GL _gl;
    private bool _disposed;

    /// <summary>
    /// 创建 R8 遮罩纹理。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    public LightMaskTexture(GL gl, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        Texture = CreateTexture(gl, width, height);
    }

    /// <summary>
    /// OpenGL 纹理句柄。
    /// </summary>
    public uint Handle => Texture.Handle;

    /// <summary>
    /// 内部 2D 纹理对象，供同程序集 framebuffer attachment 使用。
    /// </summary>
    internal GlTexture Texture { get; private set; }

    /// <summary>
    /// 宽度。
    /// </summary>
    public int Width => Texture.Width;

    /// <summary>
    /// 高度。
    /// </summary>
    public int Height => Texture.Height;

    /// <summary>
    /// 绑定到指定 texture unit。
    /// </summary>
    /// <param name="unit">texture unit 索引。</param>
    public void Bind(uint unit = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Texture.Bind(unit);
    }

    /// <summary>
    /// 调整纹理尺寸，尺寸不变时保留原句柄。
    /// </summary>
    /// <param name="width">新宽度。</param>
    /// <param name="height">新高度。</param>
    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width == Width && height == Height)
        {
            return;
        }

        GlTexture next = CreateTexture(_gl, width, height);
        Texture.Dispose();
        Texture = next;
    }

    /// <summary>
    /// 上传完整 R8 遮罩数据。
    /// </summary>
    /// <param name="mask">长度必须等于 <c>Width * Height</c>。</param>
    public void Upload(ReadOnlySpan<byte> mask)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int expectedLength = checked(Width * Height);
        if (mask.Length != expectedLength)
        {
            throw new ArgumentException("遮罩数据长度必须等于纹理宽高乘积。", nameof(mask));
        }

        Bind();
        fixed (byte* data = mask)
        {
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            _gl.TexSubImage2D(
                TextureTarget.Texture2D,
                0,
                0,
                0,
                (uint)Width,
                (uint)Height,
                PixelFormat.Red,
                PixelType.UnsignedByte,
                data);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Texture.Dispose();
        _disposed = true;
    }

    private static GlTexture CreateTexture(GL gl, int width, int height)
    {
        return new GlTexture(
            gl,
            width,
            height,
            InternalFormat.R8,
            PixelFormat.Red,
            PixelType.UnsignedByte);
    }
}
