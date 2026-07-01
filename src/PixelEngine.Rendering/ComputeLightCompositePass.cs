using PixelEngine.Rendering.Compute;

namespace PixelEngine.Rendering;

/// <summary>
/// GL compute 光照合成 pass：scene * visibility + emissive，作为 plan/08 fragment composite 的可选替代路径。
/// </summary>
/// <remarks>
/// 本 pass 仅在渲染相位 10 使用，复用 plan/08 的 GL 上下文与纹理资源，不读取 CPU 权威模拟数据。
/// </remarks>
public sealed class ComputeLightCompositePass
{
    private readonly GpuComputeBloomPipeline _pipeline;

    /// <summary>
    /// 创建 compute 光照合成 pass。
    /// </summary>
    /// <param name="pipeline">已加载 CP-L0 的 compute pipeline。</param>
    public ComputeLightCompositePass(GpuComputeBloomPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
    }

    /// <summary>
    /// 执行 CP-L0，将 scene、visibility 与 emissive 合成到目标颜色纹理。
    /// </summary>
    /// <param name="scene">world blit、GPU 粒子和 overlay 后的 scene 颜色。</param>
    /// <param name="visibility">fog-of-war / visibility R8 遮罩。</param>
    /// <param name="emissive">emissive additive buffer。</param>
    /// <param name="destination">输出颜色目标。</param>
    /// <param name="exposure">光照曝光倍率。</param>
    public void Render(
        ColorRenderTarget scene,
        LightMaskTexture visibility,
        EmissiveBuffer emissive,
        ColorRenderTarget destination,
        float exposure = 1f)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(emissive);
        ArgumentNullException.ThrowIfNull(destination);
        if (scene.Width != destination.Width || scene.Height != destination.Height ||
            scene.Width != visibility.Width || scene.Height != visibility.Height ||
            scene.Width != emissive.Width || scene.Height != emissive.Height)
        {
            throw new ArgumentException("Compute light composite 输入与输出尺寸必须一致。", nameof(destination));
        }

        _pipeline.DispatchLightComposite(
            scene.Handle,
            visibility.Handle,
            emissive.Handle,
            destination.Handle,
            destination.Width,
            destination.Height,
            exposure);
    }
}
