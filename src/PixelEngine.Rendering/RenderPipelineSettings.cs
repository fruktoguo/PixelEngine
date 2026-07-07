using PixelEngine.Rendering.Compute;

namespace PixelEngine.Rendering;

/// <summary>
/// 相位 10 渲染管线设置。
/// </summary>
public sealed class RenderPipelineSettings
{
    /// <summary>
    /// 光照质量档位，用于过载时按架构 §4.3 第二级降级。
    /// </summary>
    public LightingQualityLevel QualityLevel { get; set; } = LightingQualityLevel.Full;

    /// <summary>
    /// Bloom 设置。
    /// </summary>
    public BloomSettings Bloom { get; set; } = BloomSettings.Default;

    /// <summary>
    /// 是否启用 dither。
    /// </summary>
    public bool EnableDither { get; set; } = true;

    /// <summary>
    /// Dither 强度。
    /// </summary>
    public float DitherStrength { get; set; } = 1f / 255f;

    /// <summary>
    /// Gamma 值。
    /// </summary>
    public float Gamma { get; set; } = 2.2f;

    /// <summary>
    /// 是否启用可选 CRT/scanline pass。
    /// </summary>
    public bool EnableCrt { get; set; }

    /// <summary>
    /// CRT scanline 强度。
    /// </summary>
    public float CrtScanlineStrength { get; set; } = 0.12f;

    /// <summary>
    /// CRT 曲率。
    /// </summary>
    public float CrtCurvature { get; set; } = 0.04f;

    /// <summary>
    /// 当 compute shader 可用时，是否允许 plan/09 的高质量 compute/RC 路径接管。
    /// </summary>
    public bool PreferComputeLighting { get; set; }

    /// <summary>
    /// 创建渲染管线时是否显式优先选择 ComputeSharp/DX12 后端；仍必须满足 G2 资源契约与可执行后端。
    /// </summary>
    public bool PreferComputeSharpBackend { get; set; }

    /// <summary>
    /// Radiance Cascades 可选 GI 设置；默认关闭，归入 §4.3 第二级降级。
    /// </summary>
    public RadianceCascadeSettings RadianceCascades { get; set; } = RadianceCascadeSettings.Default;

    /// <summary>
    /// 自由粒子渲染模式。默认保持 plan/08 CPU stamp 路径；显式切到 GPU 时由管线在 world blit 后绘制 point-sprite。
    /// </summary>
    public ParticleRenderMode ParticleRenderMode { get; set; } = ParticleRenderMode.CpuStamp;

    /// <summary>
    /// 非权威 air/smoke 渲染增强设置；默认关闭，输出只允许进入渲染合成。
    /// </summary>
    public AirSmokeSettings AirSmoke { get; set; } = AirSmokeSettings.Default;

    /// <summary>
    /// 校验当前设置。
    /// </summary>
    public void Validate()
    {
        _ = Bloom.Normalize();
        if (!float.IsFinite(DitherStrength) || DitherStrength < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(DitherStrength), "DitherStrength 必须为非负有限数值。");
        }

        if (!float.IsFinite(Gamma) || Gamma <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Gamma), "Gamma 必须为正有限数值。");
        }

        if (!float.IsFinite(CrtScanlineStrength) || CrtScanlineStrength < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(CrtScanlineStrength), "CrtScanlineStrength 必须为非负有限数值。");
        }

        if (!float.IsFinite(CrtCurvature) || CrtCurvature < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(CrtCurvature), "CrtCurvature 必须为非负有限数值。");
        }

        if (!Enum.IsDefined(ParticleRenderMode))
        {
            throw new ArgumentOutOfRangeException(nameof(ParticleRenderMode), ParticleRenderMode, "未知自由粒子渲染模式。");
        }

        _ = RadianceCascades.Validate();
        _ = AirSmoke.Validate();
    }
}
