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
/// 将并行 CA worker 产生的同方向逐 cell 拓扑变化合并为世界坐标区域。
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
    private int _removedMinX = int.MaxValue;
    private int _removedMinY = int.MaxValue;
    private int _removedMaxX = int.MinValue;
    private int _removedMaxY = int.MinValue;
    private int _addedMinX = int.MaxValue;
    private int _addedMinY = int.MaxValue;
    private int _addedMaxX = int.MinValue;
    private int _addedMaxY = int.MinValue;
    private int _pendingKinds;

    /// <inheritdoc />
    public void OnCellTopologyChanged(in CellTopologyChangeEvent item)
    {
        if (item.Kind == CellTopologyChangeKind.None)
        {
            return;
        }

        if ((item.Kind & CellTopologyChangeKind.SolidRemoved) != 0)
        {
            Include(
                ref _removedMinX,
                ref _removedMinY,
                ref _removedMaxX,
                ref _removedMaxY,
                item.WorldX,
                item.WorldY);
        }

        if ((item.Kind & CellTopologyChangeKind.SolidAdded) != 0)
        {
            Include(
                ref _addedMinX,
                ref _addedMinY,
                ref _addedMaxX,
                ref _addedMaxY,
                item.WorldX,
                item.WorldY);
        }

        _ = Interlocked.Or(ref _pendingKinds, (int)item.Kind);
    }

    /// <summary>
    /// 在 worker barrier 后排空当前合并区域；没有变化时返回 false。
    /// </summary>
    public bool TryDrain(out CellTopologyChangeRegion region)
    {
        CellTopologyChangeKind kinds = (CellTopologyChangeKind)Interlocked.Exchange(ref _pendingKinds, 0);
        if (kinds == CellTopologyChangeKind.None)
        {
            region = default;
            return false;
        }

        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        if ((kinds & CellTopologyChangeKind.SolidRemoved) != 0)
        {
            MergeDrainedBounds(
                ref _removedMinX,
                ref _removedMinY,
                ref _removedMaxX,
                ref _removedMaxY,
                ref minX,
                ref minY,
                ref maxX,
                ref maxY);
        }

        if ((kinds & CellTopologyChangeKind.SolidAdded) != 0)
        {
            MergeDrainedBounds(
                ref _addedMinX,
                ref _addedMinY,
                ref _addedMaxX,
                ref _addedMaxY,
                ref minX,
                ref minY,
                ref maxX,
                ref maxY);
        }

        if (minX > maxX || minY > maxY)
        {
            region = default;
            return false;
        }

        region = new CellTopologyChangeRegion(minX, minY, maxX, maxY, kinds);
        return true;
    }

    /// <summary>
    /// 在 worker barrier 后只排空指定方向的合并区域，避免另一方向的远距变化扩大边界。
    /// </summary>
    /// <param name="kind">必须是单个 <see cref="CellTopologyChangeKind.SolidRemoved"/> 或 <see cref="CellTopologyChangeKind.SolidAdded"/>。</param>
    /// <param name="region">指定方向的精确合并区域。</param>
    /// <returns>该方向存在待处理变化时返回 true。</returns>
    public bool TryDrain(CellTopologyChangeKind kind, out CellTopologyChangeRegion region)
    {
        if (kind is not CellTopologyChangeKind.SolidRemoved and not CellTopologyChangeKind.SolidAdded)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "必须按单个固体拓扑变化方向排空。");
        }

        int previous = Interlocked.And(ref _pendingKinds, ~(int)kind);
        if ((previous & (int)kind) == 0)
        {
            region = default;
            return false;
        }

        int minX;
        int minY;
        int maxX;
        int maxY;
        if (kind == CellTopologyChangeKind.SolidRemoved)
        {
            DrainBounds(
                ref _removedMinX,
                ref _removedMinY,
                ref _removedMaxX,
                ref _removedMaxY,
                out minX,
                out minY,
                out maxX,
                out maxY);
        }
        else
        {
            DrainBounds(
                ref _addedMinX,
                ref _addedMinY,
                ref _addedMaxX,
                ref _addedMaxY,
                out minX,
                out minY,
                out maxX,
                out maxY);
        }

        if (minX > maxX || minY > maxY)
        {
            region = default;
            return false;
        }

        region = new CellTopologyChangeRegion(minX, minY, maxX, maxY, kind);
        return true;
    }

    private static void Include(
        ref int minX,
        ref int minY,
        ref int maxX,
        ref int maxY,
        int worldX,
        int worldY)
    {
        AtomicMin(ref minX, worldX);
        AtomicMin(ref minY, worldY);
        AtomicMax(ref maxX, worldX);
        AtomicMax(ref maxY, worldY);
    }

    private static void MergeDrainedBounds(
        ref int sourceMinX,
        ref int sourceMinY,
        ref int sourceMaxX,
        ref int sourceMaxY,
        ref int minX,
        ref int minY,
        ref int maxX,
        ref int maxY)
    {
        DrainBounds(
            ref sourceMinX,
            ref sourceMinY,
            ref sourceMaxX,
            ref sourceMaxY,
            out int drainedMinX,
            out int drainedMinY,
            out int drainedMaxX,
            out int drainedMaxY);
        minX = Math.Min(minX, drainedMinX);
        minY = Math.Min(minY, drainedMinY);
        maxX = Math.Max(maxX, drainedMaxX);
        maxY = Math.Max(maxY, drainedMaxY);
    }

    private static void DrainBounds(
        ref int sourceMinX,
        ref int sourceMinY,
        ref int sourceMaxX,
        ref int sourceMaxY,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        minX = Volatile.Read(ref sourceMinX);
        minY = Volatile.Read(ref sourceMinY);
        maxX = Volatile.Read(ref sourceMaxX);
        maxY = Volatile.Read(ref sourceMaxY);
        Volatile.Write(ref sourceMinX, int.MaxValue);
        Volatile.Write(ref sourceMinY, int.MaxValue);
        Volatile.Write(ref sourceMaxX, int.MinValue);
        Volatile.Write(ref sourceMaxY, int.MinValue);
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
