using PixelEngine.Rendering.Compute;
using PixelEngine.Core.Diagnostics;
using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 非权威 air/smoke 扩散 pass。输出只供渲染合成使用，绝不作为 CPU sim 输入。
/// </summary>
public sealed class AirSmokePass : IDisposable
{
    private readonly GpuAirSmokePipeline _pipeline;
    private readonly AirSmokeResources _resources;
    private int _parity;
    private bool _disposed;

    /// <summary>
    /// 创建 air/smoke pass。
    /// </summary>
    public AirSmokePass(GL gl, GpuAirSmokePipeline pipeline, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
        _resources = new AirSmokeResources(gl, width, height);
    }

    /// <summary>
    /// 当前 density texture 句柄。该句柄仅可被后续渲染合成采样，不可回读进 sim。
    /// </summary>
    public uint DensityTexture => _resources.SourceDensity;

    /// <summary>
    /// CPU→GPU 单向播种 density；调用者可与世界纹理上传同帧提交，结果只进入渲染侧扩散链。
    /// </summary>
    /// <param name="density">长度必须等于 <c>width * height</c>。</param>
    /// <param name="width">输入宽度。</param>
    /// <param name="height">输入高度。</param>
    public void UploadSeed(ReadOnlySpan<float> density, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _resources.UploadSeed(density, width, height);
    }

    /// <summary>
    /// 执行一个非权威扩散步；设置关闭时不 dispatch。
    /// </summary>
    public void Step(int width, int height, AirSmokeSettings settings, GpuComputeProfiler? gpuProfiler = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings = settings.Validate();
        _resources.Resize(width, height);
        if (!settings.Enabled)
        {
            return;
        }

        using GpuComputeProfiler.GpuTimerScope _ = gpuProfiler?.Measure("air_smoke", FrameSubPhase.GpuAirSmoke) ?? default;
        _pipeline.DispatchMargolusStep(_resources.SourceDensity, _resources.DestinationDensity, width, height, _parity, settings);
        _resources.Swap();
        _parity ^= 1;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _resources.Dispose();
        _disposed = true;
    }
}
