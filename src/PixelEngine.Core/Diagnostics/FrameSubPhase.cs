namespace PixelEngine.Core.Diagnostics;

/// <summary>
/// 表示诊断 HUD 可选显示的细分相位。
/// </summary>
public enum FrameSubPhase
{
    /// <summary>
    /// CA pass A。
    /// </summary>
    CaPassA,

    /// <summary>
    /// CA pass B。
    /// </summary>
    CaPassB,

    /// <summary>
    /// CA pass C。
    /// </summary>
    CaPassC,

    /// <summary>
    /// CA pass D。
    /// </summary>
    CaPassD,

    /// <summary>
    /// 物理步进。
    /// </summary>
    PhysicsStep,

    /// <summary>
    /// 刚体形状重建。
    /// </summary>
    ShapeRebuild,

    /// <summary>
    /// GPU 上传。
    /// </summary>
    GpuUpload,

    /// <summary>
    /// 音频派发。
    /// </summary>
    AudioDispatch,
}
