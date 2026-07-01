using Silk.NET.OpenGL;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// Radiance Cascades 渲染侧 compute 资源。只持有 GPU texture，不创建 GL 上下文。
/// </summary>
public sealed class RadianceCascadeResources : IDisposable
{
    private readonly GL _gl;
    private GlTexture _sdfA;
    private GlTexture _sdfB;
    private GlTexture _cascadeA;
    private GlTexture _cascadeB;
    private bool _disposed;

    /// <summary>
    /// 创建 Radiance Cascades 资源。
    /// </summary>
    public RadianceCascadeResources(GL gl, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _sdfA = CreateFloatTexture(gl, width, height, InternalFormat.Rgba32f);
        _sdfB = CreateFloatTexture(gl, width, height, InternalFormat.Rgba32f);
        _cascadeA = CreateFloatTexture(gl, width, height, InternalFormat.Rgba16f);
        _cascadeB = CreateFloatTexture(gl, width, height, InternalFormat.Rgba16f);
    }

    /// <summary>宽度。</summary>
    public int Width => _sdfA.Width;

    /// <summary>高度。</summary>
    public int Height => _sdfA.Height;

    /// <summary>SDF ping texture。</summary>
    public uint SdfA => _sdfA.Handle;

    /// <summary>SDF pong texture。</summary>
    public uint SdfB => _sdfB.Handle;

    /// <summary>Cascade ping texture。</summary>
    public uint CascadeA => _cascadeA.Handle;

    /// <summary>Cascade pong texture。</summary>
    public uint CascadeB => _cascadeB.Handle;

    /// <summary>
    /// 调整资源尺寸。
    /// </summary>
    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width == Width && height == Height)
        {
            return;
        }

        GlTexture sdfA = CreateFloatTexture(_gl, width, height, InternalFormat.Rgba32f);
        GlTexture sdfB = CreateFloatTexture(_gl, width, height, InternalFormat.Rgba32f);
        GlTexture cascadeA = CreateFloatTexture(_gl, width, height, InternalFormat.Rgba16f);
        GlTexture cascadeB = CreateFloatTexture(_gl, width, height, InternalFormat.Rgba16f);
        DisposeTextures();
        _sdfA = sdfA;
        _sdfB = sdfB;
        _cascadeA = cascadeA;
        _cascadeB = cascadeB;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeTextures();
        _disposed = true;
    }

    private static GlTexture CreateFloatTexture(GL gl, int width, int height, InternalFormat internalFormat)
    {
        return new GlTexture(gl, width, height, internalFormat, PixelFormat.Rgba, PixelType.Float);
    }

    private void DisposeTextures()
    {
        _sdfA.Dispose();
        _sdfB.Dispose();
        _cascadeA.Dispose();
        _cascadeB.Dispose();
    }
}
