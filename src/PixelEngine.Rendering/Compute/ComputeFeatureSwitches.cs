namespace PixelEngine.Rendering.Compute;

/// <summary>
/// plan/09 的逐特性 G4 开关。
/// </summary>
/// <param name="BloomComputeEnabled">是否允许 compute bloom。</param>
/// <param name="RadianceCascadesEnabled">是否允许 Radiance Cascades。</param>
/// <param name="GpuParticlesEnabled">是否允许 GPU 粒子批绘。</param>
/// <param name="NonAuthoritativeAirEnabled">是否允许非权威 air/smoke 渲染增强 pass；ARCH-005 在生产接线完成前强制关闭。</param>
public readonly record struct ComputeFeatureSwitches(
    bool BloomComputeEnabled,
    bool RadianceCascadesEnabled,
    bool GpuParticlesEnabled,
    bool NonAuthoritativeAirEnabled)
{
    /// <summary>
    /// 默认开关：只启用可替换 plan/08 fragment 路径的 compute bloom，其余高风险增强默认关闭。
    /// </summary>
    public static ComputeFeatureSwitches Default { get; } = new(
        BloomComputeEnabled: true,
        RadianceCascadesEnabled: false,
        GpuParticlesEnabled: false,
        NonAuthoritativeAirEnabled: false);

    /// <summary>
    /// 全部关闭，用于强制基线回退测试。
    /// </summary>
    public static ComputeFeatureSwitches Disabled { get; } = new(
        BloomComputeEnabled: false,
        RadianceCascadesEnabled: false,
        GpuParticlesEnabled: false,
        NonAuthoritativeAirEnabled: false);
}
