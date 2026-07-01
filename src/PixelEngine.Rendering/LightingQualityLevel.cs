namespace PixelEngine.Rendering;

/// <summary>
/// 光照与 bloom 的降级档位。高质量 Radiance Cascades 由 plan/09 通过 capability hook 接入。
/// </summary>
public enum LightingQualityLevel
{
    /// <summary>
    /// 完整 fragment 路径：emissive/visibility composite + bloom + post。
    /// </summary>
    Full,

    /// <summary>
    /// 关闭 bloom，保留 emissive/visibility composite 与最终 post。
    /// </summary>
    BloomDisabled,

    /// <summary>
    /// 保留 fog-of-war/emissive 基础合成，不执行 bloom 与 CRT。
    /// </summary>
    FogOfWarEmissiveOnly,
}
