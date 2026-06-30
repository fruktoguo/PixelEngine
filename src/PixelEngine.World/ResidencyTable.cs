using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// chunk 的 World 侧驻留状态。
/// </summary>
public enum ChunkResidencyState
{
    /// <summary>
    /// 位于激活区内，会参与模拟。
    /// </summary>
    Active,

    /// <summary>
    /// 位于激活区外的 border ring，常驻但默认 sleep。
    /// </summary>
    Border,

    /// <summary>
    /// 位于 border 外但仍保留在内存中的 sleeping chunk，可由 LRU 驱逐。
    /// </summary>
    Cached,

    /// <summary>
    /// 已从 live map 摘下，后台 I/O 只能操作游离对象。
    /// </summary>
    Detached,
}

/// <summary>
/// World 侧 chunk 驻留元数据。
/// </summary>
public readonly record struct ChunkResidencyInfo(
    ChunkResidencyState State,
    long LastTouchedFrame,
    int ResidentBytes,
    bool DirtySinceLoad);

/// <summary>
/// World 私有的驻留元数据表，与 Simulation 的 live chunk map 同步增删。
/// </summary>
public sealed class ResidencyTable
{
    private readonly Dictionary<ChunkCoord, ChunkResidencyInfo> _entries = [];

    /// <summary>
    /// 当前记录数量。
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// 尝试读取指定 chunk 的驻留元数据。
    /// </summary>
    public bool TryGetInfo(ChunkCoord coord, out ChunkResidencyInfo info)
    {
        return _entries.TryGetValue(coord, out info);
    }

    /// <summary>
    /// 写入或覆盖指定 chunk 的驻留元数据。
    /// </summary>
    public void Set(ChunkCoord coord, ChunkResidencyInfo info)
    {
        _entries[coord] = info;
    }

    /// <summary>
    /// 移除指定 chunk 的驻留元数据。
    /// </summary>
    public bool Remove(ChunkCoord coord)
    {
        return _entries.Remove(coord);
    }

    /// <summary>
    /// 更新指定 chunk 的最近触碰帧。
    /// </summary>
    public bool Touch(ChunkCoord coord, long frame)
    {
        if (!_entries.TryGetValue(coord, out ChunkResidencyInfo info))
        {
            return false;
        }

        _entries[coord] = info with { LastTouchedFrame = frame };
        return true;
    }

    /// <summary>
    /// 标记指定 chunk 自加载以来已变脏。
    /// </summary>
    public bool MarkDirty(ChunkCoord coord, long frame)
    {
        if (!_entries.TryGetValue(coord, out ChunkResidencyInfo info))
        {
            return false;
        }

        _entries[coord] = info with { DirtySinceLoad = true, LastTouchedFrame = frame };
        return true;
    }

    /// <summary>
    /// 枚举当前所有驻留记录。
    /// </summary>
    public IEnumerable<KeyValuePair<ChunkCoord, ChunkResidencyInfo>> Entries()
    {
        return _entries;
    }
}
