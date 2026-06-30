using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// chunk 坐标闭区间矩形。
/// </summary>
public readonly record struct ChunkRect(int MinCx, int MinCy, int MaxCx, int MaxCy)
{
    /// <summary>
    /// 空矩形。
    /// </summary>
    public static ChunkRect Empty { get; } = new(0, 0, -1, -1);

    /// <summary>
    /// 是否为空。
    /// </summary>
    public bool IsEmpty => MinCx > MaxCx || MinCy > MaxCy;

    /// <summary>
    /// 矩形内 chunk 数量。
    /// </summary>
    public int Count => IsEmpty
        ? 0
        : checked((MaxCx - MinCx + 1) * (MaxCy - MinCy + 1));

    /// <summary>
    /// 判断指定 chunk 坐标是否落在矩形内。
    /// </summary>
    public bool Contains(ChunkCoord coord)
    {
        return !IsEmpty &&
            coord.X >= MinCx &&
            coord.X <= MaxCx &&
            coord.Y >= MinCy &&
            coord.Y <= MaxCy;
    }

    /// <summary>
    /// 向四周扩张指定 chunk 数。
    /// </summary>
    public ChunkRect Expand(int chunks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunks);
        return IsEmpty
            ? Empty
            : new ChunkRect(
                checked(MinCx - chunks),
                checked(MinCy - chunks),
                checked(MaxCx + chunks),
                checked(MaxCy + chunks));
    }

    /// <summary>
    /// 按 y-major 顺序枚举矩形内所有 chunk 坐标。
    /// </summary>
    public IEnumerable<ChunkCoord> Iterate()
    {
        if (IsEmpty)
        {
            yield break;
        }

        for (int y = MinCy; y <= MaxCy; y++)
        {
            for (int x = MinCx; x <= MaxCx; x++)
            {
                yield return new ChunkCoord(x, y);
            }
        }
    }
}
