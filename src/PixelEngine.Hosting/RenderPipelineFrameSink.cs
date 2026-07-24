using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Hosting 渲染相位输出提交到真实 RenderPipeline。
/// </summary>
public sealed class RenderPipelineFrameSink : IRenderFrameSink, IDisposable
{
    private readonly RenderPipeline _pipeline;
    private readonly GamePresentationCoordinator? _presentation;
    private readonly WorldVisualTextureCache? _worldVisualTextures;
    private bool _disposed;

#pragma warning disable IDE0290 // 普通构造器保留独立 XML 文档，供公开 API 文档纪律测试识别。
    /// <summary>
    /// 创建真实渲染管线帧提交器。
    /// </summary>
    /// <param name="pipeline">真实 Rendering 管线。</param>
    public RenderPipelineFrameSink(RenderPipeline pipeline)
        : this(pipeline, presentation: null, worldVisualTextures: null)
    {
    }

    /// <summary>
    /// 创建带帧边界 presentation 协调器的真实渲染管线帧提交器。
    /// </summary>
    /// <param name="pipeline">真实 Rendering 管线。</param>
    /// <param name="presentation">Hosting 三层分辨率协调器。</param>
    public RenderPipelineFrameSink(RenderPipeline pipeline, GamePresentationCoordinator? presentation)
        : this(pipeline, presentation, worldVisualTextures: null)
    {
    }

    /// <summary>
    /// 创建带 presentation 协调器与 ContentRoot 世界视觉纹理缓存的真实帧提交器。
    /// </summary>
    /// <param name="pipeline">真实 Rendering 管线。</param>
    /// <param name="presentation">Hosting 三层分辨率协调器。</param>
    /// <param name="window">持有共享 GL context 的渲染窗口。</param>
    /// <param name="contentRoot">世界视觉资产所在 ContentRoot。</param>
    public RenderPipelineFrameSink(
        RenderPipeline pipeline,
        GamePresentationCoordinator? presentation,
        RenderWindow window,
        string contentRoot)
        : this(
            pipeline,
            presentation,
            new WorldVisualTextureCache(
                (window ?? throw new ArgumentNullException(nameof(window))).Gl,
                contentRoot))
    {
    }

    private RenderPipelineFrameSink(
        RenderPipeline pipeline,
        GamePresentationCoordinator? presentation,
        WorldVisualTextureCache? worldVisualTextures)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _presentation = presentation;
        _worldVisualTextures = worldVisualTextures;
    }
#pragma warning restore IDE0290

    /// <summary>
    /// 当前真实渲染管线实际使用的自由粒子渲染模式。
    /// </summary>
    public ParticleRenderMode ParticleRenderMode => _pipeline.CanRenderParticlesOnGpu
        ? ParticleRenderMode.GpuPointSprite
        : ParticleRenderMode.CpuStamp;

    /// <summary>
    /// 尝试把 ContentRoot 内的稳定 Texture 引用解析为当前 GL context 的 overlay sprite。
    /// </summary>
    /// <param name="asset">稳定 Texture 资产引用。</param>
    /// <param name="sprite">解析成功的纹理精灵。</param>
    /// <returns>当前帧提交器可绘制该资产时返回 true。</returns>
    public bool TryResolveWorldVisualSprite(ScriptAssetReference asset, out OverlaySprite sprite)
    {
        if (_worldVisualTextures is null)
        {
            sprite = default;
            return false;
        }

        sprite = _worldVisualTextures.Resolve(asset);
        return true;
    }

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_presentation is not null)
        {
            GamePresentationDescriptor descriptor = _presentation.CommitFrameBoundary();
            RenderPresentationDescriptor renderDescriptor = descriptor.ToRenderDescriptor();
            _pipeline.CommitPresentation(in renderDescriptor);
        }

        _pipeline.RenderFrame(renderBuffer, aux, camera, dirtyRects, overlays, pointLights, particles, materials, fogOfWar, profiler);
    }

    /// <summary>
    /// 释放 ContentRoot 世界视觉纹理缓存；底层 RenderPipeline 仍由创建方持有。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _worldVisualTextures?.Dispose();
        _disposed = true;
    }
}
