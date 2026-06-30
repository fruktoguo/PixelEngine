namespace PixelEngine.Simulation;

/// <summary>
/// chunk 池化复用器，供世界驻留层在装卸 chunk 时避免稳态重复分配。
/// </summary>
public sealed class ChunkPool
{
    private readonly Stack<Chunk> _free = new();

    /// <summary>
    /// 当前可复用 chunk 数量。
    /// </summary>
    public int Count => _free.Count;

    /// <summary>
    /// 租用一个已重置到指定坐标的 chunk。
    /// </summary>
    public Chunk Rent(ChunkCoord coord)
    {
        if (_free.TryPop(out Chunk? chunk))
        {
            chunk.Reset(coord);
            return chunk;
        }

        return new Chunk(coord);
    }

    /// <summary>
    /// 归还 chunk。该类型不提供线程安全保证，调用方应在帧边界单线程执行。
    /// </summary>
    public void Return(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        chunk.Reset(default);
        _free.Push(chunk);
    }
}
