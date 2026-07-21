namespace PixelEngine.Simulation;

/// <summary>
/// 固体占用发生变化的方向；用于让玩法层有界重建支撑关系、导航或派生刚体。
/// </summary>
[Flags]
public enum CellTopologyChangeKind : byte
{
    /// <summary>没有固体拓扑变化。</summary>
    None = 0,

    /// <summary>原 cell 为静态 Solid，写入后不再是 Solid。</summary>
    SolidRemoved = 1 << 0,

    /// <summary>原 cell 不是静态 Solid，写入后成为 Solid。</summary>
    SolidAdded = 1 << 1,
}

/// <summary>
/// 一个权威 cell 写入导致的固体拓扑变化。
/// </summary>
/// <param name="WorldX">世界 X 坐标。</param>
/// <param name="WorldY">世界 Y 坐标。</param>
/// <param name="SourceMaterial">写入前材质 id。</param>
/// <param name="TargetMaterial">写入后材质 id。</param>
/// <param name="Kind">固体占用变化方向。</param>
public readonly record struct CellTopologyChangeEvent(
    int WorldX,
    int WorldY,
    ushort SourceMaterial,
    ushort TargetMaterial,
    CellTopologyChangeKind Kind);

/// <summary>
/// 权威网格固体占用变化的零分配接收器。
/// </summary>
/// <remarks>
/// CA worker 可并发调用 <see cref="OnCellTopologyChanged"/>；实现不得在 cell 热路径分配或加 cell 级锁。
/// </remarks>
public interface ICellTopologyChangeSink
{
    /// <summary>空实现，表示调用方不需要派生拓扑通知。</summary>
    static ICellTopologyChangeSink Null { get; } = new NullCellTopologyChangeSink();

    /// <summary>通知一个 cell 的静态 Solid 占用发生变化。</summary>
    /// <param name="item">拓扑变化事件。</param>
    void OnCellTopologyChanged(in CellTopologyChangeEvent item);

    private sealed class NullCellTopologyChangeSink : ICellTopologyChangeSink
    {
        public void OnCellTopologyChanged(in CellTopologyChangeEvent item)
        {
        }
    }
}

/// <summary>
/// 将并行 CA worker 产生的逐 cell 拓扑变化合并为单个世界坐标区域。
/// </summary>
/// <param name="MinX">最小 X，包含。</param>
/// <param name="MinY">最小 Y，包含。</param>
/// <param name="MaxX">最大 X，包含。</param>
/// <param name="MaxY">最大 Y，包含。</param>
/// <param name="Kinds">区域内出现过的变化方向。</param>
public readonly record struct CellTopologyChangeRegion(
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    CellTopologyChangeKind Kinds);

internal static class CellTopologyChangeClassifier
{
    public static CellTopologyChangeKind Classify(CellType source, CellType target)
    {
        bool sourceSolid = source == CellType.Solid;
        bool targetSolid = target == CellType.Solid;
        return sourceSolid == targetSolid
            ? CellTopologyChangeKind.None
            : sourceSolid
                ? CellTopologyChangeKind.SolidRemoved
                : CellTopologyChangeKind.SolidAdded;
    }
}

/// <summary>
/// 并发写、相位边界排空的固体拓扑区域累加器。
/// </summary>
/// <remarks>
/// <see cref="TryDrain"/> 必须在 CA job barrier 之后、没有并发 writer 的相位边界调用。
/// </remarks>
public sealed class CellTopologyChangeAccumulator : ICellTopologyChangeSink
{
    private int _minX = int.MaxValue;
    private int _minY = int.MaxValue;
    private int _maxX = int.MinValue;
    private int _maxY = int.MinValue;
    private int _kinds;
    private int _pending;

    /// <inheritdoc />
    public void OnCellTopologyChanged(in CellTopologyChangeEvent item)
    {
        if (item.Kind == CellTopologyChangeKind.None)
        {
            return;
        }

        AtomicMin(ref _minX, item.WorldX);
        AtomicMin(ref _minY, item.WorldY);
        AtomicMax(ref _maxX, item.WorldX);
        AtomicMax(ref _maxY, item.WorldY);
        _ = Interlocked.Or(ref _kinds, (int)item.Kind);
        Volatile.Write(ref _pending, 1);
    }

    /// <summary>
    /// 在 worker barrier 后排空当前合并区域；没有变化时返回 false。
    /// </summary>
    public bool TryDrain(out CellTopologyChangeRegion region)
    {
        if (Interlocked.Exchange(ref _pending, 0) == 0)
        {
            region = default;
            return false;
        }

        int minX = Volatile.Read(ref _minX);
        int minY = Volatile.Read(ref _minY);
        int maxX = Volatile.Read(ref _maxX);
        int maxY = Volatile.Read(ref _maxY);
        CellTopologyChangeKind kinds = (CellTopologyChangeKind)Interlocked.Exchange(ref _kinds, 0);
        Volatile.Write(ref _minX, int.MaxValue);
        Volatile.Write(ref _minY, int.MaxValue);
        Volatile.Write(ref _maxX, int.MinValue);
        Volatile.Write(ref _maxY, int.MinValue);

        if (kinds == CellTopologyChangeKind.None || minX > maxX || minY > maxY)
        {
            region = default;
            return false;
        }

        region = new CellTopologyChangeRegion(minX, minY, maxX, maxY, kinds);
        return true;
    }

    private static void AtomicMin(ref int location, int value)
    {
        int current = Volatile.Read(ref location);
        while (value < current)
        {
            int observed = Interlocked.CompareExchange(ref location, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private static void AtomicMax(ref int location, int value)
    {
        int current = Volatile.Read(ref location);
        while (value > current)
        {
            int observed = Interlocked.CompareExchange(ref location, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
