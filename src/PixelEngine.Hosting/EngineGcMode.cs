using System.Runtime;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 可配置的托管 GC 延迟模式。
/// </summary>
public enum EngineGcMode
{
    /// <summary>
    /// 使用 .NET 当前默认延迟模式。
    /// </summary>
    Default,

    /// <summary>
    /// 使用 SustainedLowLatency，降低交互帧中的长暂停风险。
    /// </summary>
    SustainedLowLatency,
}

/// <summary>
/// EngineGcMode 与运行时 GC 设置之间的转换。
/// </summary>
public static class EngineGcModeExtensions
{
    /// <summary>
    /// 转换为 <see cref="GCLatencyMode" />。
    /// </summary>
    public static GCLatencyMode ToLatencyMode(this EngineGcMode mode)
    {
        return mode switch
        {
            EngineGcMode.Default => GCSettings.LatencyMode,
            EngineGcMode.SustainedLowLatency => GCLatencyMode.SustainedLowLatency,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知 GC 模式。"),
        };
    }
}
