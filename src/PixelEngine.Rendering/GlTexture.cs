using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// OpenGL 2D 纹理句柄封装。
/// </summary>
public sealed unsafe class GlTexture : IDisposable
{
    private readonly GL _gl;
    private bool _disposed;

    /// <summary>
    /// 创建 2D 纹理。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="width">纹理宽度。</param>
    /// <param name="height">纹理高度。</param>
    /// <param name="internalFormat">内部格式。</param>
    /// <param name="pixelFormat">上传像素格式。</param>
    /// <param name="pixelType">上传像素类型。</param>
    public GlTexture(
        GL gl,
        int width,
        int height,
        InternalFormat internalFormat = InternalFormat.Rgba8,
        PixelFormat pixelFormat = PixelFormat.Bgra,
        PixelType pixelType = PixelType.UnsignedInt8888Rev)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "纹理尺寸必须为正数。");
        }

        _gl = gl;
        Width = width;
        Height = height;
        Handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Handle);
        gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, (uint)width, (uint)height, 0, pixelFormat, pixelType, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    /// <summary>
    /// OpenGL 纹理句柄。
    /// </summary>
    public uint Handle { get; }

    /// <summary>
    /// 纹理宽度。
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// 纹理高度。
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// 绑定到指定 texture unit。
    /// </summary>
    /// <param name="unit">texture unit 索引。</param>
    public void Bind(uint unit = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.ActiveTexture((TextureUnit)((uint)TextureUnit.Texture0 + unit));
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gl.DeleteTexture(Handle);
        _disposed = true;
    }
}
