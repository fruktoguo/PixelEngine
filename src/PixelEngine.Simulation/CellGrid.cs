using PixelEngine.Core;

namespace PixelEngine.Simulation;

/// <summary>
/// 按世界坐标访问权威 cell SoA 的冷路径门面。
/// </summary>
/// <remarks>
/// 创建 cell grid 门面。
/// </remarks>
public sealed class CellGrid(
    IChunkSource chunks,
    MaterialPropsTable materialProps,
    IRigidDamageSink? rigidDamageSink = null)
{
    private readonly IChunkSource _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    private readonly IRigidDamageSink _rigidDamageSink = rigidDamageSink ?? IRigidDamageSink.Null;

    /// <summary>
    /// 材质属性只读视图。
    /// </summary>
    public MaterialPropsTable MaterialProps { get; } = materialProps ?? throw new ArgumentNullException(nameof(materialProps));

    /// <summary>
    /// 尝试读取世界坐标处的材质 id。
    /// </summary>
    public bool TryGetMaterial(int wx, int wy, out ushort material)
    {
        if (!_chunks.TryGetChunk(CellAddressing.WorldToChunk(wx, wy), out Chunk chunk))
        {
            material = 0;
            return false;
        }

        material = chunk.Material[CellAddressing.LocalIndex(wx, wy)];
        return true;
    }

    /// <summary>
    /// 读取世界坐标处的材质 id。目标 chunk 不驻留时抛出异常。
    /// </summary>
    public ushort GetMaterial(int wx, int wy)
    {
        return MaterialAt(wx, wy);
    }

    /// <summary>
    /// 读取世界坐标处的 cell 类型。目标 chunk 不驻留时抛出异常。
    /// </summary>
    public CellType GetCellType(int wx, int wy)
    {
        return MaterialProps.TypeOf(GetMaterial(wx, wy));
    }

    /// <summary>
    /// 写入世界坐标处的材质 id，并标记 dirty。
    /// </summary>
    public void SetMaterial(int wx, int wy, ushort material)
    {
        ref ushort target = ref MaterialAt(wx, wy);
        ref byte flags = ref FlagsAt(wx, wy);
        NotifyRigidDamageIfNeeded(wx, wy, flags);
        target = material;
        MarkDirty(wx, wy);
    }

    /// <summary>
    /// 尝试写入世界坐标处的材质 id。
    /// </summary>
    public bool TryWriteCell(int wx, int wy, ushort material)
    {
        if (!_chunks.TryGetChunk(CellAddressing.WorldToChunk(wx, wy), out Chunk chunk))
        {
            return false;
        }

        int local = CellAddressing.LocalIndex(wx, wy);
        NotifyRigidDamageIfNeeded(wx, wy, chunk.Flags[local]);
        chunk.Material[local] = material;
        MarkDirty(chunk, wx, wy);
        return true;
    }

    /// <summary>
    /// 返回世界坐标处材质 id 的可写引用。
    /// </summary>
    public ref ushort MaterialAt(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        return ref chunk.Material[CellAddressing.LocalIndex(wx, wy)];
    }

    /// <summary>
    /// 返回世界坐标处 flag 的可写引用。
    /// </summary>
    public ref byte FlagsAt(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        return ref chunk.Flags[CellAddressing.LocalIndex(wx, wy)];
    }

    /// <summary>
    /// 返回世界坐标处 lifetime 的可写引用。
    /// </summary>
    public ref byte LifetimeAt(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        return ref chunk.Lifetime[CellAddressing.LocalIndex(wx, wy)];
    }

    /// <summary>
    /// 标记世界坐标所在 cell 为 dirty。
    /// </summary>
    public void MarkDirty(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        MarkDirty(chunk, wx, wy);
    }

    private Chunk RequireChunk(int wx, int wy)
    {
        ChunkCoord coord = CellAddressing.WorldToChunk(wx, wy);
        return !_chunks.TryGetChunk(coord, out Chunk chunk) ? throw new InvalidOperationException($"目标 chunk 未驻留：{coord}。") : chunk;
    }

    private static void MarkDirty(Chunk chunk, int wx, int wy)
    {
        chunk.MarkWorkingDirty(
            CellAddressing.LocalCoord(wx),
            CellAddressing.LocalCoord(wy),
            EngineConstants.DirtyRectPadding);
    }

    private void NotifyRigidDamageIfNeeded(int wx, int wy, byte flags)
    {
        if (CellFlags.Has(flags, CellFlags.RigidOwned))
        {
            _rigidDamageSink.OnOwnedCellDamaged(wx, wy);
        }
    }
}
