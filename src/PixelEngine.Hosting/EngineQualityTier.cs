namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 下发给子系统的运行质量档位。
/// </summary>
public enum EngineQualityTier
{
    /// <summary>
    /// 全质量运行。
    /// </summary>
    Full,

    /// <summary>
    /// 降低或关闭高成本温度更新。
    /// </summary>
    ReducedThermal,

    /// <summary>
    /// 降低光照与后处理质量。
    /// </summary>
    ReducedLighting,

    /// <summary>
    /// 远离相机的活跃 chunk 降频。
    /// </summary>
    DistantChunkThrottle,

    /// <summary>
    /// sim 降到 30Hz，render 仍可保持 60Hz 出帧。
    /// </summary>
    Sim30Hz,

    /// <summary>
    /// 接受真实慢放，不使用 accumulator 追帧。
    /// </summary>
    SlowMotion,
}
