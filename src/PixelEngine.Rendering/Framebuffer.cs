using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// OpenGL framebuffer object 句柄封装。
/// </summary>
public sealed class Framebuffer : IDisposable
{
    private readonly GL _gl;
    private bool _disposed;

    /// <summary>
    /// 创建 framebuffer。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    public Framebuffer(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        Handle = gl.GenFramebuffer();
        GlResourceTracker.TrackCreated(GlResourceKind.Framebuffer, Handle);
    }

    /// <summary>
    /// OpenGL framebuffer 句柄。
    /// </summary>
    public uint Handle { get; }

    /// <summary>
    /// 绑定 framebuffer。
    /// </summary>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);
    }

    /// <summary>
    /// 将 2D 纹理接到颜色附件。
    /// </summary>
    /// <param name="texture">颜色附件纹理。</param>
    /// <param name="attachment">颜色附件枚举。</param>
    public void AttachColorTexture(GlTexture texture, FramebufferAttachment attachment = FramebufferAttachment.ColorAttachment0)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Bind();
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            attachment,
            TextureTarget.Texture2D,
            texture.Handle,
            0);
        EnsureComplete();
    }

    /// <summary>
    /// 检查 framebuffer 完整性，不完整时抛出明确异常。
    /// </summary>
    public void EnsureComplete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Bind();
        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"Framebuffer 不完整: {status}。");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gl.DeleteFramebuffer(Handle);
        GlResourceTracker.TrackDeleted(GlResourceKind.Framebuffer, Handle);
        _disposed = true;
    }
}
