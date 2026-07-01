namespace PixelEngine.Simulation;

/// <summary>
/// CA chunk 降频策略。Simulation 层只接收 chunk 坐标边界，不依赖 World/Camera 类型。
/// </summary>
/// <param name="Enabled">是否启用远区 chunk 降频。</param>
/// <param name="FullRateMinCx">全速区域最小 chunk X。</param>
/// <param name="FullRateMinCy">全速区域最小 chunk Y。</param>
/// <param name="FullRateMaxCx">全速区域最大 chunk X。</param>
/// <param name="FullRateMaxCy">全速区域最大 chunk Y。</param>
/// <param name="FrameIndex">当前 CA frame index，用于远区 cohort 交错。</param>
public readonly record struct CaChunkThrottlePolicy(
    bool Enabled,
    int FullRateMinCx,
    int FullRateMinCy,
    int FullRateMaxCx,
    int FullRateMaxCy,
    uint FrameIndex)
{
    /// <summary>
    /// 未启用降频的默认策略。
    /// </summary>
    public static CaChunkThrottlePolicy Disabled => default;

    /// <summary>
    /// 返回绑定到指定 CA frame 的同一策略。
    /// </summary>
    public CaChunkThrottlePolicy ForFrame(uint frameIndex)
    {
        return Enabled ? this with { FrameIndex = frameIndex } : this;
    }

    /// <summary>
    /// 判断指定 chunk 是否处于全速区域。
    /// </summary>
    public bool IsFullRate(ChunkCoord coord)
    {
        return !Enabled ||
            (coord.X >= FullRateMinCx &&
            coord.X <= FullRateMaxCx &&
            coord.Y >= FullRateMinCy &&
            coord.Y <= FullRateMaxCy);
    }

    /// <summary>
    /// 判断远区 chunk 本帧是否应运行。远区按坐标 hash 分成两个 cohort，隔帧交错更新。
    /// </summary>
    public bool ShouldRunDistantThisFrame(ChunkCoord coord)
    {
        if (IsFullRate(coord))
        {
            return true;
        }

        int cohort = ((coord.X * 73856093) ^ (coord.Y * 19349663)) & 1;
        return (((int)FrameIndex + cohort) & 1) == 0;
    }
}
