using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 渲染相位的最终帧输出目标；生产实现桥接 RenderPipeline，测试可记录输入快照。
/// </summary>
public interface IRenderFrameSink
{
    /// <summary>
    /// 当前帧输出端实际接管的自由粒子渲染模式。默认由相位 9 CPU stamp 粒子。
    /// </summary>
    ParticleRenderMode ParticleRenderMode => ParticleRenderMode.CpuStamp;

    /// <summary>
    /// 提交一帧渲染。
    /// </summary>
    void Render(
        RenderBuffer renderBuffer,
        RenderAuxBuffers aux,
        CameraState camera,
        ReadOnlySpan<PixelUploadRect> dirtyRects,
        ReadOnlySpan<OverlayCommand> overlays,
        ReadOnlySpan<LightSource> pointLights,
        ReadOnlySpan<Particle> particles,
        MaterialTable materials,
        FogOfWarBuffer? fogOfWar,
        Core.Diagnostics.FrameProfiler? profiler);
}
