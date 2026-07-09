using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Hosting 渲染相位输出提交到真实 RenderPipeline。
/// </summary>
public sealed class RenderPipelineFrameSink : IRenderFrameSink
{
    private readonly RenderPipeline _pipeline;

#pragma warning disable IDE0290 // 普通构造器保留独立 XML 文档，供公开 API 文档纪律测试识别。
    /// <summary>
    /// 创建真实渲染管线帧提交器。
    /// </summary>
    /// <param name="pipeline">真实 Rendering 管线。</param>
    public RenderPipelineFrameSink(RenderPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }
#pragma warning restore IDE0290

    /// <summary>
    /// 当前真实渲染管线实际使用的自由粒子渲染模式。
    /// </summary>
    public ParticleRenderMode ParticleRenderMode => _pipeline.CanRenderParticlesOnGpu
        ? ParticleRenderMode.GpuPointSprite
        : ParticleRenderMode.CpuStamp;

    /// <summary>
    /// 把 Hosting 构建出的 CPU render buffer、辅助 buffer、相机、dirty rect、overlay、粒子与 fog-of-war 数据提交给真实 Rendering 管线。
    /// </summary>
    /// <param name="renderBuffer">本帧 BGRA8 CPU render buffer。</param>
    /// <param name="aux">本帧 emissive/occluder 辅助 buffer。</param>
    /// <param name="camera">本帧相机快照。</param>
    /// <param name="dirtyRects">需要上传到 GPU 世界纹理的矩形集合。</param>
    /// <param name="overlays">调试或编辑器 overlay 命令集合。</param>
    /// <param name="pointLights">脚本同步后的点光源快照。</param>
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
        ReadOnlySpan<LightSource> pointLights,
        ReadOnlySpan<Particle> particles,
        MaterialTable materials,
        FogOfWarBuffer? fogOfWar,
        Core.Diagnostics.FrameProfiler? profiler)
    {
        _pipeline.RenderFrame(renderBuffer, aux, camera, dirtyRects, overlays, pointLights, particles, materials, fogOfWar, profiler);
    }
}
