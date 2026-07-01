using PixelEngine.Rendering.Compute;
using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// GL compute bloom pass：bright-pass → dual-Kawase downsample/upsample → additive composite。
/// </summary>
public sealed class ComputeBloomPass : IDisposable
{
    private readonly GpuComputeBloomPipeline _pipeline;
    private readonly GL _gl;
    private ColorRenderTarget[] _mips = [];
    private ColorRenderTarget[] _upsampled = [];
    private int _chainWidth;
    private int _chainHeight;
    private int _chainIterations;
    private bool _disposed;

    /// <summary>
    /// 创建 compute bloom pass。本类复用 plan/08 的 GL 上下文，不创建新上下文。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="pipeline">已加载 CP-B1..CP-B5 的 compute pipeline。</param>
    public ComputeBloomPass(GL gl, GpuComputeBloomPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(pipeline);
        _gl = gl;
        _pipeline = pipeline;
    }

    /// <summary>
    /// 执行 compute bloom，并把结果写入目标颜色纹理。
    /// </summary>
    /// <param name="source">输入 scene 颜色。</param>
    /// <param name="destination">输出颜色目标。</param>
    /// <param name="settings">Bloom 设置；仅支持 dual-Kawase，Gaussian 由 fragment 路径处理。</param>
    public void Render(ColorRenderTarget source, ColorRenderTarget destination, BloomSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (source.Width != destination.Width || source.Height != destination.Height)
        {
            throw new ArgumentException("Compute bloom source 与 destination 尺寸必须一致。", nameof(destination));
        }

        BloomSettings normalized = settings.Normalize();
        if (normalized.Mode != BloomMode.DualKawase)
        {
            throw new ArgumentException("Compute bloom 当前只实现 dual-Kawase 模式。", nameof(settings));
        }

        EnsureChain(source.Width, source.Height, normalized.Iterations);
        _pipeline.DispatchBrightPass(source.Handle, _mips[0].Handle, _mips[0].Width, _mips[0].Height, normalized.Threshold);
        for (int i = 1; i < _mips.Length; i++)
        {
            ColorRenderTarget previous = _mips[i - 1];
            ColorRenderTarget current = _mips[i];
            if (i == 1)
            {
                _pipeline.DispatchDownsample(
                    previous.Handle,
                    current.Handle,
                    current.Width,
                    current.Height,
                    1f / previous.Width,
                    1f / previous.Height);
            }
            else
            {
                _pipeline.DispatchDualKawaseDown(
                    previous.Handle,
                    current.Handle,
                    current.Width,
                    current.Height,
                    1f / previous.Width,
                    1f / previous.Height,
                    normalized.KawaseOffset);
            }
        }

        ColorRenderTarget bloom = UpsampleBloom(normalized.KawaseOffset);
        _pipeline.DispatchUpsampleComposite(source.Handle, bloom.Handle, destination.Handle, destination.Width, destination.Height, normalized.Intensity);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeChain();
        _disposed = true;
    }

    private ColorRenderTarget UpsampleBloom(float offset)
    {
        ColorRenderTarget current = _mips[^1];
        for (int i = _mips.Length - 2; i >= 0; i--)
        {
            ColorRenderTarget destination = _upsampled[i];
            _pipeline.DispatchDualKawaseUp(
                current.Handle,
                _mips[i].Handle,
                destination.Handle,
                destination.Width,
                destination.Height,
                1f / current.Width,
                1f / current.Height,
                offset,
                intensity: 1f);
            current = destination;
        }

        return current;
    }

    private void EnsureChain(int width, int height, int iterations)
    {
        if (_chainWidth == width && _chainHeight == height && _chainIterations == iterations)
        {
            return;
        }

        DisposeChain();
        _chainWidth = width;
        _chainHeight = height;
        _chainIterations = iterations;
        _mips = new ColorRenderTarget[iterations];
        _upsampled = new ColorRenderTarget[Math.Max(0, iterations - 1)];
        int mipWidth = width;
        int mipHeight = height;
        for (int i = 0; i < iterations; i++)
        {
            _mips[i] = new ColorRenderTarget(_gl, mipWidth, mipHeight);
            if (i < _upsampled.Length)
            {
                _upsampled[i] = new ColorRenderTarget(_gl, mipWidth, mipHeight);
            }

            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }
    }

    private void DisposeChain()
    {
        for (int i = 0; i < _mips.Length; i++)
        {
            _mips[i].Dispose();
        }

        for (int i = 0; i < _upsampled.Length; i++)
        {
            _upsampled[i].Dispose();
        }

        _mips = [];
        _upsampled = [];
        _chainWidth = 0;
        _chainHeight = 0;
        _chainIterations = 0;
    }
}
