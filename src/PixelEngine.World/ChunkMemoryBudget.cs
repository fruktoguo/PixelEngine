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
    private const int MetadataSlackBytes = 3 * 1024;

    /// <summary>
    /// 估算的单 chunk 常驻字节数：Material/Flags/Lifetime + 温度子块 + 元数据余量。
    /// </summary>
    public const int EstimatedResidentChunkBytes = SimStateBytes + TemperatureBytes + MetadataSlackBytes;

    private long _residentBytes;
    private EvictionCandidate[] _candidates = [];
    private ChunkCoord[] _evictions = [];
    private readonly EvictionCandidateComparer _candidateComparer = new();

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
    public ReadOnlySpan<ChunkCoord> SelectEvictions(ResidencyTable table, ChunkRect border, long targetBytes)
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

        int candidateCount = 0;
        EnsureCandidateCapacity(table.Count);
        foreach (KeyValuePair<ChunkCoord, ChunkResidencyInfo> entry in table)
        {
            if (entry.Value.State != ChunkResidencyState.Cached || border.Contains(entry.Key))
            {
                continue;
            }

            _candidates[candidateCount++] = new EvictionCandidate(entry.Key, entry.Value);
        }

        _candidateComparer.Border = border;
        Array.Sort(_candidates, 0, candidateCount, _candidateComparer);

        EnsureEvictionCapacity(candidateCount);
        int evictionCount = 0;
        long freed = 0;
        for (int i = 0; i < candidateCount && freed < bytesToFree; i++)
        {
            _evictions[evictionCount++] = _candidates[i].Coord;
            freed += Math.Max(_candidates[i].Info.ResidentBytes, 0);
        }

        return _evictions.AsSpan(0, evictionCount);
    }

    private void EnsureCandidateCapacity(int required)
    {
        if (_candidates.Length < required)
        {
            Array.Resize(ref _candidates, required);
        }
    }

    private void EnsureEvictionCapacity(int required)
    {
        if (_evictions.Length < required)
        {
            Array.Resize(ref _evictions, required);
        }
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

    private readonly record struct EvictionCandidate(ChunkCoord Coord, ChunkResidencyInfo Info);

    private sealed class EvictionCandidateComparer : IComparer<EvictionCandidate>
    {
        internal ChunkRect Border { get; set; }

        int IComparer<EvictionCandidate>.Compare(EvictionCandidate left, EvictionCandidate right)
        {
            int frameCompare = left.Info.LastTouchedFrame.CompareTo(right.Info.LastTouchedFrame);
            if (frameCompare != 0)
            {
                return frameCompare;
            }

            int leftDistance = DistanceOutsideBorder(left.Coord, Border);
            int rightDistance = DistanceOutsideBorder(right.Coord, Border);
            int distanceCompare = rightDistance.CompareTo(leftDistance);
            if (distanceCompare != 0)
            {
                return distanceCompare;
            }

            int bytesCompare = right.Info.ResidentBytes.CompareTo(left.Info.ResidentBytes);
            return bytesCompare != 0 ? bytesCompare : left.Coord.GetHashCode().CompareTo(right.Coord.GetHashCode());
        }
    }
}
