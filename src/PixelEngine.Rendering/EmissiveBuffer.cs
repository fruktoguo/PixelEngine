using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 相位 10 使用的 emissive additive 离屏 buffer。
/// </summary>
public sealed unsafe class EmissiveBuffer : IDisposable
{
    private readonly GL _gl;
    private GlTexture _texture;
    private Framebuffer _framebuffer;
    private bool _disposed;

    /// <summary>
    /// 创建 BGRA8/RGBA8 emissive buffer；CPU/GPU 写入均保持 authored sRGB，采样方在线性光照前解码。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    public EmissiveBuffer(GL gl, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _texture = CreateTexture(gl, width, height);
        _framebuffer = CreateFramebuffer(gl, _texture);
    }

    /// <summary>
    /// OpenGL 纹理句柄。
    /// </summary>
    public uint Handle => _texture.Handle;

    /// <summary>
    /// 宽度。
    /// </summary>
    public int Width => _texture.Width;

    /// <summary>
    /// 高度。
    /// </summary>
    public int Height => _texture.Height;

    /// <summary>
    /// 绑定 emissive 纹理供采样。
    /// </summary>
    /// <param name="unit">texture unit 索引。</param>
    public void BindTexture(uint unit = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _texture.Bind(unit);
    }

    /// <summary>
    /// 绑定 emissive framebuffer 供渲染写入。
    /// </summary>
    public void BindFramebuffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _framebuffer.Bind();
    }

    /// <summary>
    /// 调整 emissive buffer 尺寸。
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

        GlTexture nextTexture = CreateTexture(_gl, width, height);
        Framebuffer nextFramebuffer = CreateFramebuffer(_gl, nextTexture);
        _framebuffer.Dispose();
        _texture.Dispose();
        _texture = nextTexture;
        _framebuffer = nextFramebuffer;
    }

    /// <summary>
    /// 从 CPU 相位 9 副输出上传完整 BGRA8 emissive 数据。
    /// </summary>
    /// <param name="bgra">长度必须等于 <c>Width * Height</c>。</param>
    public void Upload(ReadOnlySpan<uint> bgra)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int expectedLength = checked(Width * Height);
        if (bgra.Length != expectedLength)
        {
            throw new ArgumentException("emissive 数据长度必须等于纹理宽高乘积。", nameof(bgra));
        }

        BindTexture();
        fixed (uint* data = bgra)
        {
            _gl.TexSubImage2D(
                TextureTarget.Texture2D,
                0,
                0,
                0,
                (uint)Width,
                (uint)Height,
                PixelFormat.Bgra,
                PixelType.UnsignedInt8888Rev,
                data);
        }
    }

    /// <summary>
    /// 清空离屏 emissive buffer。
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BindFramebuffer();
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _framebuffer.Dispose();
        _texture.Dispose();
        _disposed = true;
    }

    private static GlTexture CreateTexture(GL gl, int width, int height)
    {
        return new GlTexture(
            gl,
            width,
            height,
            InternalFormat.Rgba8,
            PixelFormat.Bgra,
            PixelType.UnsignedInt8888Rev);
    }

    private static Framebuffer CreateFramebuffer(GL gl, GlTexture texture)
    {
        Framebuffer framebuffer = new(gl);
        try
        {
            framebuffer.AttachColorTexture(texture);
            return framebuffer;
        }
        catch
        {
            framebuffer.Dispose();
            throw;
        }
    }
}
