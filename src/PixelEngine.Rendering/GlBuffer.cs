using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// OpenGL buffer object 句柄封装，可用于 VBO、PBO 与 UBO。
/// </summary>
public sealed unsafe class GlBuffer : IDisposable
{
    private readonly GL _gl;
    private bool _disposed;

    /// <summary>
    /// 创建 buffer object。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="target">buffer 绑定目标。</param>
    public GlBuffer(GL gl, BufferTargetARB target)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        Target = target;
        Handle = gl.GenBuffer();
        GlResourceTracker.TrackCreated(GlResourceKind.Buffer, Handle);
    }

    /// <summary>
    /// OpenGL buffer 句柄。
    /// </summary>
    public uint Handle { get; }

    /// <summary>
    /// buffer 绑定目标。
    /// </summary>
    public BufferTargetARB Target { get; }

    /// <summary>
    /// 绑定该 buffer。
    /// </summary>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.BindBuffer(Target, Handle);
    }

    /// <summary>
    /// 分配或替换 buffer 存储。
    /// </summary>
    /// <param name="sizeBytes">大小，单位为字节。</param>
    /// <param name="usage">使用模式。</param>
    public void Allocate(nuint sizeBytes, BufferUsageARB usage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Bind();
        _gl.BufferData(Target, sizeBytes, null, usage);
    }

    /// <summary>
    /// 分配 immutable buffer storage，用于 persistent mapping 等显式能力路径。
    /// </summary>
    /// <param name="sizeBytes">大小，单位为字节。</param>
    /// <param name="flags">storage 访问标志。</param>
    public void AllocateImmutable(nuint sizeBytes, BufferStorageMask flags)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Bind();
        _gl.BufferStorage((BufferStorageTarget)Target, sizeBytes, null, flags);
    }

    /// <summary>
    /// 映射 buffer 子区间。
    /// </summary>
    /// <param name="offset">起始偏移。</param>
    /// <param name="length">映射长度。</param>
    /// <param name="access">映射访问标志。</param>
    /// <returns>映射后的原生指针。</returns>
    public void* Map(nint offset, nuint length, MapBufferAccessMask access)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Bind();
        return _gl.MapBufferRange(Target, offset, length, access);
    }

    /// <summary>
    /// 解除 buffer 映射。
    /// </summary>
    /// <returns>OpenGL 返回的映射状态。</returns>
    public bool Unmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Bind();
        return _gl.UnmapBuffer(Target);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gl.DeleteBuffer(Handle);
        GlResourceTracker.TrackDeleted(GlResourceKind.Buffer, Handle);
        _disposed = true;
    }
}
