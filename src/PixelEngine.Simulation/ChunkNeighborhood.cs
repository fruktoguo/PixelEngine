namespace PixelEngine.Simulation;

/// <summary>
/// 以中心 chunk 为基准的 3x3 驻留邻域快照，slot=(dy+1)*3+(dx+1)。
/// </summary>
/// <remarks>
/// 创建 3x3 chunk 邻域。
/// </remarks>
public readonly struct ChunkNeighborhood(
    Chunk slot0,
    Chunk slot1,
    Chunk slot2,
    Chunk slot3,
    Chunk slot4,
    Chunk slot5,
    Chunk slot6,
    Chunk slot7,
    Chunk slot8)
{
    /// <summary>
    /// 左上邻居。
    /// </summary>
    public Chunk Slot0 { get; } = slot0;

    /// <summary>
    /// 上方邻居。
    /// </summary>
    public Chunk Slot1 { get; } = slot1;

    /// <summary>
    /// 右上邻居。
    /// </summary>
    public Chunk Slot2 { get; } = slot2;

    /// <summary>
    /// 左侧邻居。
    /// </summary>
    public Chunk Slot3 { get; } = slot3;

    /// <summary>
    /// 中心 chunk。
    /// </summary>
    public Chunk Slot4 { get; } = slot4;

    /// <summary>
    /// 右侧邻居。
    /// </summary>
    public Chunk Slot5 { get; } = slot5;

    /// <summary>
    /// 左下邻居。
    /// </summary>
    public Chunk Slot6 { get; } = slot6;

    /// <summary>
    /// 下方邻居。
    /// </summary>
    public Chunk Slot7 { get; } = slot7;

    /// <summary>
    /// 右下邻居。
    /// </summary>
    public Chunk Slot8 { get; } = slot8;

    /// <summary>
    /// 按 slot 读取 chunk。
    /// </summary>
    public Chunk GetSlot(int slot)
    {
        return slot switch
        {
            0 => Slot0,
            1 => Slot1,
            2 => Slot2,
            3 => Slot3,
            4 => Slot4,
            5 => Slot5,
            6 => Slot6,
            7 => Slot7,
            8 => Slot8,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }
}
