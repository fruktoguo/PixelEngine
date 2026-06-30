using PixelEngine.Core;
using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// 常驻 chunk 内存预算与 LRU 驱逐选择器。
/// </summary>
public sealed class ChunkMemoryBudget
{
    private const int HalfBytes = 2;
    private const int TempBlockSize = EngineConstants.ChunkSize / EngineConstants.TempFieldDownscale;
    private const int SimStateBytes = EngineConstants.ChunkArea * (sizeof(ushort) + sizeof(byte) + sizeof(byte));
    private const int TemperatureBytes = TempBlockSize * TempBlockSize * HalfBytes;
    private const int MetadataSlackBytes = 4 * 1024;

    /// <summary>
    /// 估算的单 chunk 常驻字节数：Material/Flags/Lifetime + 温度子块 + 元数据余量。
    /// </summary>
    public const int EstimatedResidentChunkBytes = SimStateBytes + TemperatureBytes + MetadataSlackBytes;

    private long _residentBytes;

    /// <summary>
    /// 创建常驻内存预算器。
    /// </summary>
    public ChunkMemoryBudget(long capBytes, long evictionTargetBytes)
    {
        if (capBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capBytes), capBytes, "内存上限必须为正。");
        }

        if (evictionTargetBytes <= 0 || evictionTargetBytes > capBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(evictionTargetBytes), evictionTargetBytes, "驱逐目标水位必须为正且不大于上限。");
        }

        CapBytes = capBytes;
        EvictionTargetBytes = evictionTargetBytes;
    }

    /// <summary>
    /// 常驻字节数。
    /// </summary>
    public long ResidentBytes => Volatile.Read(ref _residentBytes);

    /// <summary>
    /// 常驻内存上限。
    /// </summary>
    public long CapBytes { get; }

    /// <summary>
    /// 驱逐目标水位。
    /// </summary>
    public long EvictionTargetBytes { get; }

    /// <summary>
    /// 是否超过常驻内存上限。
    /// </summary>
    public bool OverCap => ResidentBytes > CapBytes;

    /// <summary>
    /// 增加常驻字节数。
    /// </summary>
    public void Add(int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        _ = Interlocked.Add(ref _residentBytes, bytes);
    }

    /// <summary>
    /// 减少常驻字节数。
    /// </summary>
    public void Remove(int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        long after = Interlocked.Add(ref _residentBytes, -bytes);
        if (after < 0)
        {
            Volatile.Write(ref _residentBytes, 0);
            throw new InvalidOperationException("常驻字节记账出现负数。");
        }
    }

    /// <summary>
    /// 选择 border 外 cached chunk 进行 LRU 驱逐，直到预计字节数降到目标水位。
    /// </summary>
    public IReadOnlyList<ChunkCoord> SelectEvictions(ResidencyTable table, ChunkRect border, long targetBytes)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (targetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetBytes));
        }

        long bytesToFree = ResidentBytes - targetBytes;
        if (bytesToFree <= 0)
        {
            return [];
        }

        List<(ChunkCoord Coord, ChunkResidencyInfo Info)> candidates = [];
        foreach (KeyValuePair<ChunkCoord, ChunkResidencyInfo> entry in table.Entries())
        {
            if (entry.Value.State != ChunkResidencyState.Cached || border.Contains(entry.Key))
            {
                continue;
            }

            candidates.Add((entry.Key, entry.Value));
        }

        candidates.Sort((left, right) =>
        {
            int frameCompare = left.Info.LastTouchedFrame.CompareTo(right.Info.LastTouchedFrame);
            if (frameCompare != 0)
            {
                return frameCompare;
            }

            int leftDistance = DistanceOutsideBorder(left.Coord, border);
            int rightDistance = DistanceOutsideBorder(right.Coord, border);
            int distanceCompare = rightDistance.CompareTo(leftDistance);
            if (distanceCompare != 0)
            {
                return distanceCompare;
            }

            int bytesCompare = right.Info.ResidentBytes.CompareTo(left.Info.ResidentBytes);
            return bytesCompare != 0 ? bytesCompare : left.Coord.GetHashCode().CompareTo(right.Coord.GetHashCode());
        });

        List<ChunkCoord> evictions = [];
        long freed = 0;
        for (int i = 0; i < candidates.Count && freed < bytesToFree; i++)
        {
            evictions.Add(candidates[i].Coord);
            freed += Math.Max(candidates[i].Info.ResidentBytes, 0);
        }

        return evictions;
    }

    private static int DistanceOutsideBorder(ChunkCoord coord, ChunkRect border)
    {
        int dx = coord.X < border.MinCx
            ? border.MinCx - coord.X
            : coord.X > border.MaxCx
                ? coord.X - border.MaxCx
                : 0;
        int dy = coord.Y < border.MinCy
            ? border.MinCy - coord.Y
            : coord.Y > border.MaxCy
                ? coord.Y - border.MaxCy
                : 0;
        return Math.Max(dx, dy);
    }
}
