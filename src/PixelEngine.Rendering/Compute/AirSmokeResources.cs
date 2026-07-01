using Silk.NET.OpenGL;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// 非权威 air/smoke density GPU 资源。资源独立于 CPU 权威网格，仅用于渲染相位 10 的视觉增强。
/// </summary>
public sealed unsafe class AirSmokeResources : IDisposable
{
    private readonly GL _gl;
    private GlTexture _densityA;
    private GlTexture _densityB;
    private bool _useAAsSource = true;
    private bool _disposed;

    /// <summary>
    /// 创建 R16F ping-pong density texture。
    /// </summary>
    public AirSmokeResources(GL gl, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _densityA = CreateDensityTexture(gl, width, height);
        _densityB = CreateDensityTexture(gl, width, height);
    }

    /// <summary>宽度。</summary>
    public int Width => _densityA.Width;

    /// <summary>高度。</summary>
    public int Height => _densityA.Height;

    /// <summary>当前输入 density texture 句柄。</summary>
    public uint SourceDensity => _useAAsSource ? _densityA.Handle : _densityB.Handle;

    /// <summary>当前输出 density texture 句柄。</summary>
    public uint DestinationDensity => _useAAsSource ? _densityB.Handle : _densityA.Handle;

    /// <summary>
    /// 调整资源尺寸。尺寸变化会丢弃旧视觉 density，绝不影响 CPU 权威模拟数据。
    /// </summary>
    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width == Width && height == Height)
        {
            return;
        }

        GlTexture nextA = CreateDensityTexture(_gl, width, height);
        GlTexture nextB = CreateDensityTexture(_gl, width, height);
        DisposeTextures();
        _densityA = nextA;
        _densityB = nextB;
        _useAAsSource = true;
    }

    /// <summary>
    /// CPU→GPU 单向播种 density。该数据只进入渲染侧 texture，不会回写 CPU 权威网格。
    /// </summary>
    /// <param name="density">长度必须等于 <c>width * height</c>。</param>
    /// <param name="width">输入宽度。</param>
    /// <param name="height">输入高度。</param>
    public void UploadSeed(ReadOnlySpan<float> density, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int expectedLength = checked(width * height);
        if (density.Length != expectedLength)
        {
            throw new ArgumentException("air/smoke density 数据长度必须等于纹理宽高乘积。", nameof(density));
        }

        Resize(width, height);
        _gl.BindTexture(TextureTarget.Texture2D, SourceDensity);
        fixed (float* data = density)
        {
            _gl.TexSubImage2D(
                TextureTarget.Texture2D,
                0,
                0,
                0,
                (uint)width,
                (uint)height,
                PixelFormat.Red,
                PixelType.Float,
                data);
        }
    }

    /// <summary>
    /// 交换 ping-pong 输入输出。
    /// </summary>
    public void Swap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _useAAsSource = !_useAAsSource;
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

    private static GlTexture CreateDensityTexture(GL gl, int width, int height)
    {
        return new GlTexture(gl, width, height, InternalFormat.R16f, PixelFormat.Red, PixelType.Float);
    }

    private void DisposeTextures()
    {
        _densityA.Dispose();
        _densityB.Dispose();
    }
}
