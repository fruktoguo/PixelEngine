namespace PixelEngine.Simulation;

/// <summary>
/// Simulation 消费的 chunk 驻留集合 seam，由 World/Streaming 层实现。
/// </summary>
public interface IChunkSource
{
    /// <summary>
    /// 当前帧驻留 chunk 的只读快照。
    /// </summary>
    ReadOnlySpan<Chunk> ResidentChunks { get; }

    /// <summary>
    /// 尝试获取指定坐标的驻留 chunk。
    /// </summary>
    bool TryGetChunk(ChunkCoord coord, out Chunk chunk);

    /// <summary>
    /// 解析以 center 为中心的 3x3 邻域，slot=(dy+1)*3+(dx+1)。
    /// </summary>
    bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood);
}
