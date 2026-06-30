namespace PixelEngine.Simulation;

/// <summary>
/// chunk 池化复用器，供世界驻留层在装卸 chunk 时避免稳态重复分配。
/// </summary>
public sealed class ChunkPool
{
    private readonly Stack<Chunk> _free = new();
    private readonly Lock _gate = new();

    /// <summary>
    /// 当前可复用 chunk 数量。
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _free.Count;
            }
        }
    }

    /// <summary>
    /// 租用一个已重置到指定坐标的 chunk。
    /// </summary>
    public Chunk Rent(ChunkCoord coord)
    {
        lock (_gate)
        {
            if (_free.TryPop(out Chunk? chunk))
            {
                chunk.Reset(coord);
                return chunk;
            }
        }

        return new Chunk(coord);
    }

    /// <summary>
    /// 归还 chunk；归还时会重置 SoA 与 dirty 状态，可由后台流式 worker 调用。
    /// </summary>
    public void Return(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        chunk.Reset(default);
        lock (_gate)
        {
            _free.Push(chunk);
        }
    }
}
