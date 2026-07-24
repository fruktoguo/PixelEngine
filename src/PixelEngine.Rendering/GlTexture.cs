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
        GlResourceTracker.TrackCreated(GlResourceKind.Texture, Handle);
        gl.GetInteger(GLEnum.TextureBinding2D, out int previousTexture);
        gl.GetInteger(GLEnum.PixelUnpackBufferBinding, out int previousUnpackBuffer);
        try
        {
            // TexImage2D 的 null 在绑定 PBO 时表示 buffer offset 0，而不是“不上传数据”。
            // render target 尺寸大于残留 PBO 容量时会让纹理存储分配失败并产生 incomplete FBO。
            gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            gl.BindTexture(TextureTarget.Texture2D, Handle);
            gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, (uint)width, (uint)height, 0, pixelFormat, pixelType, null);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        }
        finally
        {
            gl.BindTexture(TextureTarget.Texture2D, (uint)previousTexture);
            gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, (uint)previousUnpackBuffer);
        }
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

    internal void EnableMipmappedMinification()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.GetInteger(GLEnum.TextureBinding2D, out int previousTexture);
        try
        {
            _gl.BindTexture(TextureTarget.Texture2D, Handle);
            _gl.GenerateMipmap(TextureTarget.Texture2D);
            _gl.TexParameter(
                TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter,
                (int)GLEnum.LinearMipmapLinear);
        }
        finally
        {
            _gl.BindTexture(TextureTarget.Texture2D, (uint)previousTexture);
        }
    }

    internal void RegenerateMipmaps()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.GetInteger(GLEnum.TextureBinding2D, out int previousTexture);
        try
        {
            _gl.BindTexture(TextureTarget.Texture2D, Handle);
            _gl.GenerateMipmap(TextureTarget.Texture2D);
        }
        finally
        {
            _gl.BindTexture(TextureTarget.Texture2D, (uint)previousTexture);
        }
    }

    /// <summary>
    /// 上传覆盖整张纹理的 32-bit 像素。调用前后的 Texture2D、PBO 与 unpack alignment 状态保持不变。
    /// </summary>
    /// <param name="pixels">行优先像素；长度必须等于纹理宽乘高。</param>
    /// <param name="pixelFormat">输入像素通道格式。</param>
    /// <param name="pixelType">输入像素元素类型。</param>
    public void UploadPixels(
        ReadOnlySpan<uint> pixels,
        PixelFormat pixelFormat = PixelFormat.Rgba,
        PixelType pixelType = PixelType.UnsignedByte)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pixels.Length != checked(Width * Height))
        {
            throw new ArgumentException("上传像素数量必须与纹理尺寸一致。", nameof(pixels));
        }

        _gl.GetInteger(GLEnum.TextureBinding2D, out int previousTexture);
        _gl.GetInteger(GLEnum.PixelUnpackBufferBinding, out int previousUnpackBuffer);
        _gl.GetInteger(GLEnum.UnpackAlignment, out int previousAlignment);
        try
        {
            _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            _gl.BindTexture(TextureTarget.Texture2D, Handle);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            fixed (uint* data = pixels)
            {
                _gl.TexSubImage2D(
                    TextureTarget.Texture2D,
                    level: 0,
                    xoffset: 0,
                    yoffset: 0,
                    (uint)Width,
                    (uint)Height,
                    pixelFormat,
                    pixelType,
                    data);
            }
        }
        finally
        {
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, previousAlignment);
            _gl.BindTexture(TextureTarget.Texture2D, (uint)previousTexture);
            _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, (uint)previousUnpackBuffer);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gl.DeleteTexture(Handle);
        GlResourceTracker.TrackDeleted(GlResourceKind.Texture, Handle);
        _disposed = true;
    }
}
