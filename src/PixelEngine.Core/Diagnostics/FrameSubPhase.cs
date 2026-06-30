namespace PixelEngine.Core.Diagnostics;

/// <summary>
/// 表示诊断 HUD 可选显示的细分相位。
/// </summary>
public enum FrameSubPhase
{
    /// <summary>
    /// CA pass A。
    /// </summary>
    CheckerboardA,

    /// <summary>
    /// CA pass B。
    /// </summary>
    CheckerboardB,

    /// <summary>
    /// CA pass C。
    /// </summary>
    CheckerboardC,

    /// <summary>
    /// CA pass D。
    /// </summary>
    CheckerboardD,

    /// <summary>
    /// Box2D task bridge。
    /// </summary>
    Box2DTasks,

    /// <summary>
    /// 光照计算。
    /// </summary>
    Lighting,

    /// <summary>
    /// 后处理。
    /// </summary>
    PostProcess,
}
