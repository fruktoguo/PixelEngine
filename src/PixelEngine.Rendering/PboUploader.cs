using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 2-PBO ping-pong 上传器。每帧 orphan + unsynchronized map，避免 CPU render buffer 到世界纹理的同步停顿。
/// </summary>
public sealed unsafe class PboUploader : IDisposable
{
    private readonly GL _gl;
    private readonly GlBuffer[] _pbos;
    private int _index;
    private bool _disposed;

    /// <summary>
    /// 创建 PBO 上传器。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="initialCapacityBytes">初始 PBO 容量。</param>
    public PboUploader(GL gl, int initialCapacityBytes)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (initialCapacityBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacityBytes));
        }

        _gl = gl;
        _pbos =
        [
            new GlBuffer(gl, BufferTargetARB.PixelUnpackBuffer),
            new GlBuffer(gl, BufferTargetARB.PixelUnpackBuffer),
        ];
        EnsureCapacity(initialCapacityBytes);
    }

    /// <summary>
    /// 当前 PBO 容量。
    /// </summary>
    public int CapacityBytes { get; private set; }

    /// <summary>
    /// 上传整张 render buffer 到世界纹理。
    /// </summary>
    /// <param name="texture">目标世界纹理。</param>
    /// <param name="buffer">源 render buffer。</param>
    public void UploadFull(WorldTexture texture, RenderBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSameSize(texture, buffer);

        GlBuffer pbo = CopyBufferToNextPbo(buffer);
        pbo.Bind();
        texture.Bind();
        _gl.TexSubImage2D(
            TextureTarget.Texture2D,
            0,
            0,
            0,
            (uint)buffer.Width,
            (uint)buffer.Height,
            PixelFormat.Bgra,
            PixelType.UnsignedInt8888Rev,
            null);
        _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
    }

    /// <summary>
    /// 上传 dirty rect 子区。实现使用单张视口纹理与 PBO，不创建 per-chunk texture。
    /// </summary>
    /// <param name="texture">目标世界纹理。</param>
    /// <param name="buffer">源 render buffer。</param>
    /// <param name="rects">待上传矩形。</param>
    public void UploadDirtyRects(WorldTexture texture, RenderBuffer buffer, ReadOnlySpan<PixelUploadRect> rects)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSameSize(texture, buffer);
        if (rects.IsEmpty)
        {
            return;
        }

        foreach (PixelUploadRect rect in rects)
        {
            buffer.ValidateRect(rect);
        }

        GlBuffer pbo = CopyBufferToNextPbo(buffer);
        pbo.Bind();
        texture.Bind();
        _gl.PixelStore(GLEnum.UnpackRowLength, buffer.Width);

        foreach (PixelUploadRect rect in rects)
        {
            _gl.PixelStore(GLEnum.UnpackSkipPixels, rect.X);
            _gl.PixelStore(GLEnum.UnpackSkipRows, rect.Y);
            _gl.TexSubImage2D(
                TextureTarget.Texture2D,
                0,
                rect.X,
                rect.Y,
                (uint)rect.Width,
                (uint)rect.Height,
                PixelFormat.Bgra,
                PixelType.UnsignedInt8888Rev,
                null);
        }

        _gl.PixelStore(GLEnum.UnpackRowLength, 0);
        _gl.PixelStore(GLEnum.UnpackSkipPixels, 0);
        _gl.PixelStore(GLEnum.UnpackSkipRows, 0);
        _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
    }

    /// <summary>
    /// 确保 PBO 容量不小于指定字节数。
    /// </summary>
    /// <param name="requiredBytes">需要的最小容量。</param>
    public void EnsureCapacity(int requiredBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (requiredBytes <= CapacityBytes)
        {
            return;
        }

        foreach (GlBuffer pbo in _pbos)
        {
            pbo.Bind();
            pbo.Allocate((nuint)requiredBytes, BufferUsageARB.StreamDraw);
        }

        CapacityBytes = requiredBytes;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (GlBuffer pbo in _pbos)
        {
            pbo.Dispose();
        }

        _disposed = true;
    }

    private GlBuffer CopyBufferToNextPbo(RenderBuffer buffer)
    {
        EnsureCapacity(buffer.ByteLength);
        GlBuffer pbo = _pbos[_index];
        _index = (_index + 1) & 1;
        pbo.Bind();
        pbo.Allocate((nuint)buffer.ByteLength, BufferUsageARB.StreamDraw);
        void* destination = pbo.Map(
            0,
            (nuint)buffer.ByteLength,
            MapBufferAccessMask.WriteBit |
            MapBufferAccessMask.InvalidateBufferBit |
            MapBufferAccessMask.UnsynchronizedBit);
        fixed (uint* source = buffer.Pixels)
        {
            System.Buffer.MemoryCopy(source, destination, buffer.ByteLength, buffer.ByteLength);
        }

        _ = pbo.Unmap();
        return pbo;
    }

    private static void ValidateSameSize(WorldTexture texture, RenderBuffer buffer)
    {
        if (texture.Width != buffer.Width || texture.Height != buffer.Height)
        {
            throw new ArgumentException("世界纹理与 render buffer 尺寸必须一致。", nameof(buffer));
        }
    }
}
