using PixelEngine.Rendering.Compute;
using PixelEngine.Core.Diagnostics;
using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// Radiance Cascades 渲染侧高质量 GI pass。默认由 G4 开关控制，不读取 CPU 权威网格也不做 GPU→CPU readback。
/// </summary>
public sealed class RadianceCascadePass : IDisposable
{
    private readonly GpuRadianceCascadePipeline _pipeline;
    private readonly RadianceCascadeResources _resources;
    private bool _disposed;

    /// <summary>
    /// 创建 Radiance Cascades pass。
    /// </summary>
    public RadianceCascadePass(GL gl, GpuRadianceCascadePipeline pipeline, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
        _resources = new RadianceCascadeResources(gl, width, height);
    }

    /// <summary>
    /// 执行完整 CP-R0..CP-R3 链，输出渲染侧 radiance 合成结果。
    /// </summary>
    public void Render(
        LightMaskTexture occluder,
        EmissiveBuffer emissive,
        ColorRenderTarget scene,
        ColorRenderTarget destination,
        RadianceCascadeSettings settings,
        float occluderThreshold = 0.5f,
        float intensity = 1f,
        GpuComputeProfiler? gpuProfiler = null)
    {
        ArgumentNullException.ThrowIfNull(occluder);
        ArgumentNullException.ThrowIfNull(emissive);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings = settings.Validate();
        if (scene.Width != destination.Width || scene.Height != destination.Height ||
            scene.Width != occluder.Width || scene.Height != occluder.Height ||
            scene.Width != emissive.Width || scene.Height != emissive.Height)
        {
            throw new ArgumentException("Radiance Cascades 输入输出尺寸必须一致。", nameof(destination));
        }

        _resources.Resize(scene.Width, scene.Height);
        using GpuComputeProfiler.GpuTimerScope _ = gpuProfiler?.Measure("radiance_cascades", FrameSubPhase.GpuRadianceCascades) ?? default;
        // CP-R0：occluder → Jump Flood SDF
        uint sdf = BuildSdf(occluder, occluderThreshold);
        // CP-R1/R2：自底向上构建并合并 cascade radiance
        uint radiance = BuildRadiance(sdf, emissive, settings);
        // CP-R3：radiance 叠加到 scene 输出
        _pipeline.DispatchApply(scene.Handle, radiance, destination.Handle, destination.Width, destination.Height, intensity);
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

    private uint BuildSdf(LightMaskTexture occluder, float threshold)
    {
        // 种子 pass：实心 occluder 像素写入零距离种子。
        _pipeline.DispatchSdfInitialize(occluder.Handle, _resources.SdfA, _resources.Width, _resources.Height, threshold);
        uint source = _resources.SdfA;
        uint destination = _resources.SdfB;
        // JFA 多步跳跃：步长从 max(w,h)/2 减半至 1，乒乓写入 SdfA/SdfB。
        for (int jump = HighestPowerOfTwoAtLeast(Math.Max(_resources.Width, _resources.Height)) / 2; jump >= 1; jump /= 2)
        {
            _pipeline.DispatchSdfJump(source, destination, _resources.Width, _resources.Height, jump);
            (source, destination) = (destination, source);
        }

        return source;
    }

    // 自最远层向近层迭代：build → merge，乒乓复用 CascadeA/CascadeB 纹理。
    private uint BuildRadiance(uint sdf, EmissiveBuffer emissive, RadianceCascadeSettings settings)
    {
        uint accumulated = 0;
        uint scratch = _resources.CascadeA;
        uint merged = _resources.CascadeB;
        for (int cascade = settings.CascadeCount - 1; cascade >= 0; cascade--)
        {
            _pipeline.DispatchCascadeBuild(sdf, emissive.Handle, scratch, _resources.Width, _resources.Height, settings, cascade);
            if (accumulated == 0)
            {
                accumulated = scratch;
                scratch = scratch == _resources.CascadeA ? _resources.CascadeB : _resources.CascadeA;
                merged = scratch == _resources.CascadeA ? _resources.CascadeB : _resources.CascadeA;
                continue;
            }

            _pipeline.DispatchMerge(scratch, accumulated, merged, _resources.Width, _resources.Height, cascade, mergeFactor: 0.5f);
            accumulated = merged;
            scratch = accumulated == _resources.CascadeA ? _resources.CascadeB : _resources.CascadeA;
            merged = scratch == _resources.CascadeA ? _resources.CascadeB : _resources.CascadeA;
        }

        return accumulated;
    }

    private static int HighestPowerOfTwoAtLeast(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }
}
