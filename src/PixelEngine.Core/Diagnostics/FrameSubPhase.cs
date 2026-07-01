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
    /// render buffer 构建。
    /// </summary>
    RenderBufferBuild,

    /// <summary>
    /// 自由粒子 stamp。
    /// </summary>
    ParticleStamp,

    /// <summary>
    /// 光照合成。
    /// </summary>
    Lighting,

    /// <summary>
    /// Bloom 后处理。
    /// </summary>
    Bloom,

    /// <summary>
    /// Dither、gamma 与 CRT 后处理。
    /// </summary>
    PostProcess,

    /// <summary>
    /// Present / SwapBuffers。
    /// </summary>
    Present,

    /// <summary>
    /// 音频派发。
    /// </summary>
    AudioDispatch,
}
