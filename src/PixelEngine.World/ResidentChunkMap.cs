using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// World 拥有的 live chunk 驻留表，作为 Simulation 的 <see cref="IChunkSource" /> 实现。
/// </summary>
public sealed class ResidentChunkMap : IChunkSource
{
    private readonly Dictionary<ChunkCoord, Chunk> _chunks = [];
    private Chunk[] _residentSnapshot = [];

    /// <summary>
    /// 当前驻留 chunk 数量。
    /// </summary>
    public int Count => _chunks.Count;

    /// <summary>
    /// 当前驻留 chunk 的只读快照，供相位 4 worker 枚举。
    /// </summary>
    public ReadOnlySpan<Chunk> ResidentChunks => _residentSnapshot;

    /// <summary>
    /// 添加已准备好的 chunk；结构性变更只能在相位 2 单线程执行。
    /// </summary>
    public void Add(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (!_chunks.TryAdd(chunk.Coord, chunk))
        {
            throw new ArgumentException($"chunk {chunk.Coord} 已驻留。", nameof(chunk));
        }

        RebuildSnapshot();
    }

    /// <summary>
    /// 移除指定 chunk 并返回游离对象，供后台 I/O 处理。
    /// </summary>
    public bool TryRemove(ChunkCoord coord, out Chunk chunk)
    {
        if (!_chunks.Remove(coord, out chunk!))
        {
            return false;
        }

        RebuildSnapshot();
        return true;
    }

    /// <summary>
    /// 按 chunk 坐标查找当前驻留 chunk。
    /// </summary>
    public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
    {
        return _chunks.TryGetValue(coord, out chunk!);
    }

    /// <summary>
    /// 判断指定 chunk 是否驻留。
    /// </summary>
    public bool Contains(ChunkCoord coord)
    {
        return _chunks.ContainsKey(coord);
    }

    /// <summary>
    /// 解析中心 chunk 的 3x3 邻域；任一邻居缺失时返回 <see langword="false" />。
    /// </summary>
    public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
    {
        if (!TryGetChunk(new ChunkCoord(center.X - 1, center.Y - 1), out Chunk slot0) ||
            !TryGetChunk(new ChunkCoord(center.X, center.Y - 1), out Chunk slot1) ||
            !TryGetChunk(new ChunkCoord(center.X + 1, center.Y - 1), out Chunk slot2) ||
            !TryGetChunk(new ChunkCoord(center.X - 1, center.Y), out Chunk slot3) ||
            !TryGetChunk(center, out Chunk slot4) ||
            !TryGetChunk(new ChunkCoord(center.X + 1, center.Y), out Chunk slot5) ||
            !TryGetChunk(new ChunkCoord(center.X - 1, center.Y + 1), out Chunk slot6) ||
            !TryGetChunk(new ChunkCoord(center.X, center.Y + 1), out Chunk slot7) ||
            !TryGetChunk(new ChunkCoord(center.X + 1, center.Y + 1), out Chunk slot8))
        {
            neighborhood = default;
            return false;
        }

        neighborhood = new ChunkNeighborhood(slot0, slot1, slot2, slot3, slot4, slot5, slot6, slot7, slot8);
        return true;
    }

    private void RebuildSnapshot()
    {
        _residentSnapshot = new Chunk[_chunks.Count];
        int write = 0;
        foreach (Chunk chunk in _chunks.Values)
        {
            _residentSnapshot[write++] = chunk;
        }
    }
}
