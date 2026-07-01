using PixelEngine.Core;

namespace PixelEngine.Rendering.Compute;

/// <summary>
/// Radiance Cascades 可选 GI 的质量档设置。
/// </summary>
/// <param name="Enabled">是否启用 RC；默认关闭，由 G4 开关控制。</param>
/// <param name="CascadeCount">cascade 层数。</param>
/// <param name="BaseRayCount">第 0 层角度射线数量。</param>
/// <param name="BaseStepPixels">第 0 层空间步进像素数。</param>
/// <param name="MaxRaySteps">单条射线最大步数。</param>
public readonly record struct RadianceCascadeSettings(
    bool Enabled,
    int CascadeCount,
    int BaseRayCount,
    int BaseStepPixels,
    int MaxRaySteps)
{
    /// <summary>
    /// 默认设置。模式默认关闭，满足 plan/09 §4.4 “默认关、G4 控制”。
    /// </summary>
    public static RadianceCascadeSettings Default { get; } = new(
        Enabled: false,
        CascadeCount: EngineConstants.RadianceCascadeCount,
        BaseRayCount: EngineConstants.RadianceCascadeBaseRayCount,
        BaseStepPixels: EngineConstants.RadianceCascadeBaseStepPixels,
        MaxRaySteps: EngineConstants.RadianceCascadeMaxRaySteps);

    /// <summary>
    /// 校验并返回当前设置。
    /// </summary>
    /// <returns>已校验设置。</returns>
    public RadianceCascadeSettings Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CascadeCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BaseRayCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BaseStepPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxRaySteps);
        return (BaseRayCount & (BaseRayCount - 1)) != 0
            ? throw new ArgumentOutOfRangeException(nameof(BaseRayCount), "Radiance Cascades ray count 必须为 2 的幂，便于 shader 层索引。")
            : this;
    }
}
