using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// RGBA8 颜色渲染目标，封装一张 2D 纹理与对应 framebuffer。
/// </summary>
public sealed class ColorRenderTarget : IDisposable
{
    private readonly GL _gl;
    private GlTexture _texture;
    private Framebuffer _framebuffer;
    private bool _disposed;

    /// <summary>
    /// 创建颜色渲染目标。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    public ColorRenderTarget(GL gl, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _texture = CreateTexture(gl, width, height);
        _framebuffer = CreateFramebuffer(gl, _texture);
    }

    /// <summary>
    /// 颜色纹理句柄。
    /// </summary>
    public uint Handle => _texture.Handle;

    /// <summary>当前颜色纹理所属 framebuffer 句柄；仅供同一 GL context 的宿主安全阶段读取。</summary>
    public uint FramebufferHandle => _framebuffer.Handle;

    /// <summary>
    /// 宽度。
    /// </summary>
    public int Width => _texture.Width;

    /// <summary>
    /// 高度。
    /// </summary>
    public int Height => _texture.Height;

    /// <summary>
    /// 绑定 framebuffer 供渲染写入。
    /// </summary>
    public void BindFramebuffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _framebuffer.Bind();
    }

    /// <summary>
    /// 绑定颜色纹理供采样。
    /// </summary>
    /// <param name="unit">texture unit 索引。</param>
    public void BindTexture(uint unit = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _texture.Bind(unit);
    }

    /// <summary>
    /// 调整渲染目标尺寸。
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
    /// 清空颜色目标。
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BindFramebuffer();
        _gl.Disable(EnableCap.ScissorTest);
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
