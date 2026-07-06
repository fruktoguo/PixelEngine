using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// UI overlay BGRA8 纹理。用于 HTML/RmlUi/自定义 CPU 位图后端按脏矩形上传到当前 GL context。
/// </summary>
public sealed unsafe class UiOverlayTexture : IDisposable
{
    private readonly GL _gl;
    private GlTexture _texture;
    private bool _disposed;

    /// <summary>
    /// 创建 UI overlay 纹理。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="width">纹理宽度。</param>
    /// <param name="height">纹理高度。</param>
    public UiOverlayTexture(GL gl, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _texture = CreateTexture(gl, width, height);
    }

    /// <summary>
    /// OpenGL Texture2D 句柄，可直接传给 <see cref="UiDrawState.Textured(uint)" />。
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
    /// 调整纹理尺寸。尺寸未变化时保留原句柄。
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
    /// 上传整张 BGRA8 位图。源数据长度必须等于当前纹理宽高乘积。
    /// </summary>
    /// <param name="pixelsBgra">按行连续的 BGRA8 像素。</param>
    public void Upload(ReadOnlySpan<uint> pixelsBgra)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int expectedLength = checked(Width * Height);
        if (pixelsBgra.Length != expectedLength)
        {
            throw new ArgumentException("UI overlay 源像素长度必须等于纹理宽高乘积。", nameof(pixelsBgra));
        }

        PixelUploadRect full = new(0, 0, Width, Height);
        UploadDirtyRects(pixelsBgra, Width, Height, new ReadOnlySpan<PixelUploadRect>(in full));
    }

    /// <summary>
    /// 上传 BGRA8 位图中的脏矩形区域。矩形坐标以源位图左上角为原点，并直接映射到同坐标纹理区域。
    /// </summary>
    /// <param name="pixelsBgra">按行连续的 BGRA8 源像素。</param>
    /// <param name="sourceWidth">源位图宽度。</param>
    /// <param name="sourceHeight">源位图高度。</param>
    /// <param name="dirtyRects">待上传脏矩形。</param>
    public void UploadDirtyRects(
        ReadOnlySpan<uint> pixelsBgra,
        int sourceWidth,
        int sourceHeight,
        ReadOnlySpan<PixelUploadRect> dirtyRects)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSource(pixelsBgra, sourceWidth, sourceHeight);
        if (dirtyRects.IsEmpty)
        {
            return;
        }

        foreach (PixelUploadRect rect in dirtyRects)
        {
            ValidateRect(rect, sourceWidth, sourceHeight);
        }

        SaveUnpackState(out int activeTexture, out int texture0Binding, out int unpackBuffer, out int unpackAlignment, out int unpackRowLength, out int unpackSkipPixels, out int unpackSkipRows);
        try
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, Handle);
            _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            _gl.PixelStore(GLEnum.UnpackRowLength, sourceWidth);
            _gl.PixelStore(GLEnum.UnpackSkipPixels, 0);
            _gl.PixelStore(GLEnum.UnpackSkipRows, 0);

            fixed (uint* basePtr = pixelsBgra)
            {
                foreach (PixelUploadRect rect in dirtyRects)
                {
                    uint* rectPtr = basePtr + ((rect.Y * sourceWidth) + rect.X);
                    _gl.TexSubImage2D(
                        TextureTarget.Texture2D,
                        0,
                        rect.X,
                        rect.Y,
                        (uint)rect.Width,
                        (uint)rect.Height,
                        PixelFormat.Bgra,
                        PixelType.UnsignedInt8888Rev,
                        rectPtr);
                }
            }
        }
        finally
        {
            RestoreUnpackState(activeTexture, texture0Binding, unpackBuffer, unpackAlignment, unpackRowLength, unpackSkipPixels, unpackSkipRows);
        }
    }

    /// <summary>
    /// 绑定到指定 texture unit。
    /// </summary>
    /// <param name="unit">texture unit 索引。</param>
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

    private void SaveUnpackState(
        out int activeTexture,
        out int texture0Binding,
        out int unpackBuffer,
        out int unpackAlignment,
        out int unpackRowLength,
        out int unpackSkipPixels,
        out int unpackSkipRows)
    {
        _gl.GetInteger(GLEnum.ActiveTexture, out activeTexture);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.GetInteger(GLEnum.TextureBinding2D, out texture0Binding);
        _gl.ActiveTexture((TextureUnit)activeTexture);
        _gl.GetInteger(GLEnum.PixelUnpackBufferBinding, out unpackBuffer);
        _gl.GetInteger(GLEnum.UnpackAlignment, out unpackAlignment);
        _gl.GetInteger(GLEnum.UnpackRowLength, out unpackRowLength);
        _gl.GetInteger(GLEnum.UnpackSkipPixels, out unpackSkipPixels);
        _gl.GetInteger(GLEnum.UnpackSkipRows, out unpackSkipRows);
    }

    private void RestoreUnpackState(
        int activeTexture,
        int texture0Binding,
        int unpackBuffer,
        int unpackAlignment,
        int unpackRowLength,
        int unpackSkipPixels,
        int unpackSkipRows)
    {
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, (uint)texture0Binding);
        _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, (uint)unpackBuffer);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, unpackAlignment);
        _gl.PixelStore(GLEnum.UnpackRowLength, unpackRowLength);
        _gl.PixelStore(GLEnum.UnpackSkipPixels, unpackSkipPixels);
        _gl.PixelStore(GLEnum.UnpackSkipRows, unpackSkipRows);
        _gl.ActiveTexture((TextureUnit)activeTexture);
    }

    private void ValidateRect(PixelUploadRect rect, int sourceWidth, int sourceHeight)
    {
        if (rect.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), rect, "UI overlay 脏矩形尺寸必须为正数。");
        }

        if (rect.X < 0 || rect.Y < 0 || rect.X > sourceWidth - rect.Width || rect.Y > sourceHeight - rect.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), rect, "UI overlay 脏矩形必须位于源位图范围内。");
        }

        if (rect.X > Width - rect.Width || rect.Y > Height - rect.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), rect, "UI overlay 脏矩形必须位于目标纹理范围内。");
        }
    }

    private static void ValidateSource(ReadOnlySpan<uint> pixelsBgra, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), sourceWidth, "源位图宽度必须为正数。");
        }

        if (sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight), sourceHeight, "源位图高度必须为正数。");
        }

        int requiredLength = checked(sourceWidth * sourceHeight);
        if (pixelsBgra.Length < requiredLength)
        {
            throw new ArgumentException("UI overlay 源像素长度小于源位图宽高乘积。", nameof(pixelsBgra));
        }
    }
}
