using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Hosting 渲染相位输出提交到真实 RenderPipeline。
/// </summary>
public sealed class RenderPipelineFrameSink(RenderPipeline pipeline) : IRenderFrameSink
{
    private readonly RenderPipeline _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

    /// <summary>
    /// 把 Hosting 构建出的 CPU render buffer、辅助 buffer、相机、dirty rect、overlay、粒子与 fog-of-war 数据提交给真实 Rendering 管线。
    /// </summary>
    /// <param name="renderBuffer">本帧 BGRA8 CPU render buffer。</param>
    /// <param name="aux">本帧 emissive/occluder 辅助 buffer。</param>
    /// <param name="camera">本帧相机快照。</param>
    /// <param name="dirtyRects">需要上传到 GPU 世界纹理的矩形集合。</param>
    /// <param name="overlays">调试或编辑器 overlay 命令集合。</param>
    /// <param name="particles">活跃自由粒子快照。</param>
    /// <param name="materials">材质表。</param>
    /// <param name="fogOfWar">可选 fog-of-war 可见性 buffer。</param>
    /// <param name="profiler">可选帧诊断计时器。</param>
    public void Render(
        RenderBuffer renderBuffer,
        RenderAuxBuffers aux,
        CameraState camera,
        ReadOnlySpan<PixelUploadRect> dirtyRects,
        ReadOnlySpan<OverlayCommand> overlays,
        ReadOnlySpan<Particle> particles,
        MaterialTable materials,
        FogOfWarBuffer? fogOfWar,
        PixelEngine.Core.Diagnostics.FrameProfiler? profiler)
    {
        _pipeline.RenderFrame(renderBuffer, aux, camera, dirtyRects, overlays, particles, materials, fogOfWar, profiler);
    }
}
