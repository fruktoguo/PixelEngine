using PixelEngine.Rendering;
using PixelEngine.Scripting;
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
    /// 尝试把 ContentRoot 内的稳定 Texture 引用解析为当前 GL context 的 overlay sprite。
    /// 不支持纹理的测试/headless sink 默认返回 false。
    /// </summary>
    /// <param name="asset">稳定 Texture 资产引用。</param>
    /// <param name="sprite">解析成功的纹理精灵。</param>
    /// <returns>当前 sink 可绘制该资产时返回 true。</returns>
    bool TryResolveWorldVisualSprite(ScriptAssetReference asset, out OverlaySprite sprite)
    {
        _ = asset;
        sprite = default;
        return false;
    }

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
