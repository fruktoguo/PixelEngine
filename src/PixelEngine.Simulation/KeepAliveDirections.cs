namespace PixelEngine.Simulation;

/// <summary>
/// chunk 3×3 邻域方向槽编号与入站槽映射；用于跨 chunk dirty 边界唤醒。
/// </summary>
internal static class KeepAliveDirections
{
    /// <summary>西北邻域槽。</summary>
    public const int SlotNorthWest = 0;
    /// <summary>正北邻域槽。</summary>
    public const int SlotNorth = 1;
    /// <summary>东北邻域槽。</summary>
    public const int SlotNorthEast = 2;
    /// <summary>正西邻域槽。</summary>
    public const int SlotWest = 3;
    /// <summary>正东邻域槽。</summary>
    public const int SlotEast = 4;
    /// <summary>西南邻域槽。</summary>
    public const int SlotSouthWest = 5;
    /// <summary>正南邻域槽。</summary>
    public const int SlotSouth = 6;
    /// <summary>东南邻域槽。</summary>
    public const int SlotSouthEast = 7;

    /// <summary>
    /// 将「被触碰的邻居槽」映射为目标 chunk 上的「入站唤醒槽」；中心槽（4）不参与映射。
    /// </summary>
    /// <param name="neighborSlot">3×3 邻域中的非中心槽索引（0–8，不含 4）。</param>
    public static int IncomingSlotForTouchedNeighborSlot(int neighborSlot)
    {
        return neighborSlot switch
        {
            0 => SlotSouthEast,
            1 => SlotSouth,
            2 => SlotSouthWest,
            3 => SlotEast,
            5 => SlotWest,
            6 => SlotNorthEast,
            7 => SlotNorth,
            8 => SlotNorthWest,
            _ => throw new ArgumentOutOfRangeException(nameof(neighborSlot), neighborSlot, "slot 必须是 3x3 邻域中的非中心邻居。"),
        };
    }
}
