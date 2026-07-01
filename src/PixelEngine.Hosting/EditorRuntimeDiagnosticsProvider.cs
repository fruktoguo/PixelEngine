using PixelEngine.Editor;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Hosting 运行态映射为 Editor 可只读展示的诊断快照。
/// </summary>
public static class EditorRuntimeDiagnosticsProvider
{
    /// <summary>
    /// 从引擎上下文创建 Editor runtime diagnostics。
    /// </summary>
    public static EditorRuntimeDiagnostics Create(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EngineOverloadController overload = context.GetService<EngineOverloadController>();
        EngineQualityTier tier = context.QualityTier;
        return new EditorRuntimeDiagnostics(
            context.Clock.TimeScale,
            (int)tier,
            FormatQualityTier(tier),
            overload.ConsecutiveOverBudgetFrames);
    }

    private static string FormatQualityTier(EngineQualityTier tier)
    {
        return tier switch
        {
            EngineQualityTier.Full => "Full",
            EngineQualityTier.ReducedThermal => "ReducedThermal",
            EngineQualityTier.ReducedLighting => "ReducedLighting",
            EngineQualityTier.DistantChunkThrottle => "DistantChunkThrottle",
            EngineQualityTier.Sim30Hz => "Sim30Hz",
            EngineQualityTier.SlowMotion => "SlowMotion",
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "未知质量档位。"),
        };
    }
}
