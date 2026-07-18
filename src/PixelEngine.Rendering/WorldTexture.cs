using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 单张视口大小世界纹理。按架构 §9.2 禁止 per-chunk texture 上传路径。
/// </summary>
public sealed class WorldTexture : IDisposable
{
    private readonly GL _gl;
    private GlTexture _texture;
    private bool _disposed;

    /// <summary>
    /// 创建 BGRA8 世界纹理；通道保存相位 9 产出的 display-referred sRGB 材质色。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="width">纹理宽度。</param>
    /// <param name="height">纹理高度。</param>
    public WorldTexture(GL gl, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _texture = CreateTexture(gl, width, height);
    }

    /// <summary>
    /// OpenGL 纹理句柄。
    /// </summary>
    public uint Handle => _texture.Handle;

    /// <summary>
    /// 纹理宽度。
    /// </summary>
    public int Width => _texture.Width;

    /// <summary>
    /// 纹理高度。
    /// </summary>
    public int Height => _texture.Height;

    /// <summary>
    /// 调整世界纹理尺寸。尺寸未变化时不重建。
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
        _texture.Dispose();
        _texture = next;
    }

    /// <summary>
    /// 绑定世界纹理。
    /// </summary>
    /// <param name="unit">texture unit。</param>
    public void Bind(uint unit = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _texture.Bind(unit);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

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
}
