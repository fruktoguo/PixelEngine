using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// World 拥有的 live chunk 驻留表，作为 Simulation 的 <see cref="IChunkSource" /> 实现。
/// </summary>
public sealed class ResidentChunkMap : IChunkSource
{
    private const int InitialSnapshotCapacity = 4;
    private readonly Dictionary<ChunkCoord, Chunk> _chunks = [];
    private readonly HashSet<ChunkCoord> _batchCoordinates = [];
    private Chunk[] _residentSnapshot = [];
    private int _residentSnapshotCount;

    /// <summary>
    /// 当前驻留 chunk 数量。
    /// </summary>
    public int Count => _chunks.Count;

    /// <summary>
    /// 当前驻留 chunk 的只读快照，供相位 4 worker 枚举。
    /// </summary>
    public ReadOnlySpan<Chunk> ResidentChunks => _residentSnapshot.AsSpan(0, _residentSnapshotCount);

    /// <summary>
    /// 供 World 性能测试观察快照重建次数；不属于运行时业务状态。
    /// </summary>
    internal int SnapshotRebuildCount { get; private set; }

    /// <summary>
    /// 添加已准备好的 chunk；结构性变更只能在相位 2 单线程执行。
    /// </summary>
    public void Add(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (_chunks.ContainsKey(chunk.Coord))
        {
            throw new ArgumentException($"chunk {chunk.Coord} 已驻留。", nameof(chunk));
        }

        _ = _chunks.EnsureCapacity(checked(_chunks.Count + 1));
        _chunks.Add(chunk.Coord, chunk);
        RebuildSnapshot();
    }

    /// <summary>
    /// 批量添加已准备好的 chunk，并只重建一次驻留快照。
    /// </summary>
    /// <param name="chunks">待添加的唯一 chunk 集合。</param>
    public void AddRange(ReadOnlySpan<Chunk> chunks)
    {
        if (chunks.IsEmpty)
        {
            return;
        }

        if (chunks.Length == 1)
        {
            Add(chunks[0]);
            return;
        }

        _batchCoordinates.Clear();
        _ = _batchCoordinates.EnsureCapacity(chunks.Length);
        try
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                Chunk chunk = chunks[i] ?? throw new ArgumentNullException(nameof(chunks), "批量 chunk 不能为 null。");
                if (_chunks.ContainsKey(chunk.Coord) || !_batchCoordinates.Add(chunk.Coord))
                {
                    throw new ArgumentException($"chunk {chunk.Coord} 已驻留或在批次中重复。", nameof(chunks));
                }
            }

            _ = _chunks.EnsureCapacity(checked(_chunks.Count + chunks.Length));
            for (int i = 0; i < chunks.Length; i++)
            {
                Chunk chunk = chunks[i]!;
                _chunks.Add(chunk.Coord, chunk);
            }

            RebuildSnapshot();
        }
        finally
        {
            _batchCoordinates.Clear();
        }
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
    /// 批量移除 chunk，并只重建一次驻留快照。
    /// </summary>
    /// <param name="coords">待移除的 chunk 坐标。</param>
    /// <param name="removed">接收实际移除对象的输出缓冲。</param>
    /// <returns>实际移除的 chunk 数量。</returns>
    public int RemoveRange(ReadOnlySpan<ChunkCoord> coords, Span<Chunk> removed)
    {
        if (removed.Length < coords.Length)
        {
            throw new ArgumentException("removed 缓冲不足。", nameof(removed));
        }

        int removedCount = 0;
        for (int i = 0; i < coords.Length; i++)
        {
            if (_chunks.Remove(coords[i], out Chunk? chunk))
            {
                removed[removedCount++] = chunk;
            }
        }

        if (removedCount > 0)
        {
            RebuildSnapshot();
        }

        return removedCount;
    }

    /// <summary>
    /// 清空全部驻留 chunk，并只重建一次空快照。
    /// </summary>
    public void Clear()
    {
        if (_chunks.Count == 0)
        {
            return;
        }

        _chunks.Clear();
        RebuildSnapshot();
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
        EnsureSnapshotCapacity(_chunks.Count);
        int write = 0;
        foreach (Chunk chunk in _chunks.Values)
        {
            _residentSnapshot[write++] = chunk;
        }

        if (_residentSnapshotCount > write)
        {
            _residentSnapshot.AsSpan(write, _residentSnapshotCount - write).Clear();
        }

        _residentSnapshotCount = write;
        SnapshotRebuildCount++;
    }

    private void EnsureSnapshotCapacity(int required)
    {
        if (_residentSnapshot.Length >= required)
        {
            return;
        }

        int capacity = _residentSnapshot.Length == 0 ? InitialSnapshotCapacity : _residentSnapshot.Length;
        while (capacity < required)
        {
            capacity = checked(capacity * 2);
        }

        Array.Resize(ref _residentSnapshot, capacity);
    }
}
