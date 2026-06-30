namespace PixelEngine.World;

/// <summary>
/// 世界流式与驻留策略配置。
/// </summary>
public sealed record WorldStreamingConfig
{
    /// <summary>
    /// 可见区外额外模拟的 chunk 边距。
    /// </summary>
    public int ActivationMarginChunks { get; init; } = 2;

    /// <summary>
    /// 激活区外常驻 border ring 宽度，单位 chunk。
    /// </summary>
    public int BorderRingWidth { get; init; } = 1;

    /// <summary>
    /// 常驻 chunk 内存上限，默认 512MB。
    /// </summary>
    public long ResidentMemoryCapBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>
    /// 驱逐后的目标水位，默认 448MB。
    /// </summary>
    public long EvictionTargetBytes { get; init; } = 448L * 1024 * 1024;

    /// <summary>
    /// 单个 region 的 chunk 边长。
    /// </summary>
    public int RegionSizeChunks { get; init; } = 32;

    /// <summary>
    /// 单帧最多提交的流式请求数量。
    /// </summary>
    public int MaxStreamOpsPerFrame { get; init; } = 64;

    /// <summary>
    /// 校验配置并返回自身。
    /// </summary>
    public WorldStreamingConfig Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ActivationMarginChunks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BorderRingWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ResidentMemoryCapBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(EvictionTargetBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(EvictionTargetBytes, ResidentMemoryCapBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RegionSizeChunks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxStreamOpsPerFrame);
        return this;
    }
}
