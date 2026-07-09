namespace PixelEngine.Rendering;

/// <summary>
/// 非权威 air/smoke 渲染增强设置。该 pass 只影响渲染侧 density texture，绝不回写 CPU 权威网格。
/// </summary>
public readonly record struct AirSmokeSettings(
    bool Enabled,
    float Diffusion)
{
    /// <summary>
    /// 默认关闭；air/smoke 是纯视觉增强，不是 plan/08 基线功能，且当前尚未接入生产 RenderPipeline。
    /// </summary>
    public static AirSmokeSettings Default { get; } = new(
        Enabled: false,
        Diffusion: 0.25f);

    /// <summary>
    /// 校验并返回规范化设置。
    /// </summary>
    public AirSmokeSettings Validate()
    {
        return !float.IsFinite(Diffusion) || Diffusion < 0f || Diffusion > 1f
            ? throw new ArgumentOutOfRangeException(nameof(Diffusion), "Air/smoke diffusion 必须位于 [0,1]。")
            : this;
    }
}
