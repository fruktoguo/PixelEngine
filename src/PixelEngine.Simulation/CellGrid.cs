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
    /// 尝试读取世界坐标处的 cell flags。
    /// </summary>
    public bool TryGetFlags(int wx, int wy, out byte flags)
    {
        if (!_chunks.TryGetChunk(CellAddressing.WorldToChunk(wx, wy), out Chunk chunk))
        {
            flags = 0;
            return false;
        }

        flags = chunk.Flags[CellAddressing.LocalIndex(wx, wy)];
        return true;
    }

    /// <summary>
    /// 读取世界坐标处的材质 id。目标 chunk 不驻留时抛出异常。
    /// </summary>
    public ushort GetMaterial(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        return chunk.Material[CellAddressing.LocalIndex(wx, wy)];
    }

    /// <summary>
    /// 读取世界坐标处的 cell flags。目标 chunk 不驻留时抛出异常。
    /// </summary>
    public byte GetFlags(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        return chunk.Flags[CellAddressing.LocalIndex(wx, wy)];
    }

    /// <summary>
    /// 读取世界坐标处的 lifetime。目标 chunk 不驻留时抛出异常。
    /// </summary>
    public byte GetLifetime(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        return chunk.Lifetime[CellAddressing.LocalIndex(wx, wy)];
    }

    /// <summary>
    /// 读取世界坐标处的累计结构破坏度。目标 chunk 不驻留时抛出异常。
    /// </summary>
    public byte GetDamage(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        return chunk.Damage[CellAddressing.LocalIndex(wx, wy)];
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
        Chunk chunk = RequireChunk(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        ref byte flags = ref chunk.FlagsBuffer[local];
        ushort target = chunk.GetMaterialAt(local);
        // 覆盖 RigidOwned cell 前先通知物理层，避免 stamp 与材质写入不一致。
        bool wasRigidOwned = NotifyRigidDamageIfNeeded(wx, wy, flags, target);
        chunk.SetMaterialAt(local, material);
        if (wasRigidOwned)
        {
            flags = 0;
            chunk.LifetimeBuffer[local] = 0;
        }

        chunk.DamageBuffer[local] = 0;
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
        bool wasRigidOwned = NotifyRigidDamageIfNeeded(wx, wy, chunk.FlagsBuffer[local], chunk.GetMaterialAt(local));
        chunk.SetMaterialAt(local, material);
        if (wasRigidOwned)
        {
            chunk.FlagsBuffer[local] = 0;
            chunk.LifetimeBuffer[local] = 0;
        }

        chunk.DamageBuffer[local] = 0;
        MarkDirty(wx, wy);
        return true;
    }

    /// <summary>
    /// 返回世界坐标处材质 id 的内部可写引用。
    /// </summary>
    /// <remarks>
    /// 仅供 Simulation 热路径与受信任测试夹具使用；跨程序集生产代码必须使用本类的受控写 API。
    /// </remarks>
    internal ref ushort MaterialAt(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        return ref chunk.MaterialBuffer[CellAddressing.LocalIndex(wx, wy)];
    }

    /// <summary>
    /// 返回世界坐标处 flag 的内部可写引用。
    /// </summary>
    internal ref byte FlagsAt(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        return ref chunk.FlagsBuffer[CellAddressing.LocalIndex(wx, wy)];
    }

    /// <summary>
    /// 返回世界坐标处 lifetime 的内部可写引用。
    /// </summary>
    internal ref byte LifetimeAt(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        return ref chunk.LifetimeBuffer[CellAddressing.LocalIndex(wx, wy)];
    }

    /// <summary>
    /// 返回世界坐标处累计结构破坏度的内部可写引用。
    /// </summary>
    internal ref byte DamageAt(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        return ref chunk.DamageBuffer[CellAddressing.LocalIndex(wx, wy)];
    }

    /// <summary>
    /// 标记世界坐标所在 cell 为 dirty。
    /// </summary>
    public void MarkDirty(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        MarkDirty(chunk, wx, wy);
    }

    /// <summary>
    /// 清空一个非刚体 cell，并标记 dirty。刚体 cell 会先通知 damage sink 并保留原状。
    /// </summary>
    public bool TryClearCell(int wx, int wy)
    {
        if (!_chunks.TryGetChunk(CellAddressing.WorldToChunk(wx, wy), out Chunk chunk))
        {
            return false;
        }

        int local = CellAddressing.LocalIndex(wx, wy);
        byte flags = chunk.FlagsBuffer[local];
        ushort material = chunk.GetMaterialAt(local);
        if (CellFlags.Has(flags, CellFlags.RigidOwned))
        {
            _ = NotifyRigidDamageIfNeeded(wx, wy, flags, material);
            return false;
        }

        chunk.SetMaterialAt(local, 0);
        chunk.FlagsBuffer[local] = 0;
        chunk.LifetimeBuffer[local] = 0;
        chunk.DamageBuffer[local] = 0;
        MarkDirty(wx, wy);
        return true;
    }

    /// <summary>
    /// 相位 8：仅当目标仍是刚体占用 cell 时清空它，不触发刚体 damage 回调。
    /// </summary>
    public bool TryClearRigidOwnedCell(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        if (!CellFlags.Has(chunk.FlagsBuffer[local], CellFlags.RigidOwned))
        {
            return false;
        }

        chunk.SetMaterialAt(local, 0);
        chunk.FlagsBuffer[local] = 0;
        chunk.LifetimeBuffer[local] = 0;
        chunk.DamageBuffer[local] = 0;
        MarkRigidDirty(wx, wy);
        return true;
    }

    /// <summary>
    /// 相位 8：写回刚体像素并设置 <see cref="CellFlags.RigidOwned"/>，不触发刚体 damage 回调。
    /// </summary>
    public void StampRigidOwnedCell(int wx, int wy, ushort material)
    {
        if (material == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(material), material, "刚体 stamp 材质不能是 Empty。");
        }

        Chunk chunk = RequireChunk(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        chunk.SetMaterialAt(local, material);
        chunk.FlagsBuffer[local] = CellFlags.RigidOwned;
        chunk.LifetimeBuffer[local] = 0;
        chunk.DamageBuffer[local] = 0;
        MarkRigidDirty(wx, wy);
    }

    /// <summary>
    /// 相位 8：尝试写回刚体像素。目标 cell 或 dirty padding 所需邻居未驻留时返回 false。
    /// </summary>
    public bool TryStampRigidOwnedCell(int wx, int wy, ushort material)
    {
        if (material == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(material), material, "刚体 stamp 材质不能是 Empty。");
        }

        ChunkCoord coord = CellAddressing.WorldToChunk(wx, wy);
        // stamp 须邻域完整驻留，否则 working dirty 无法正确唤醒边界 chunk。
        if (!_chunks.TryGetChunk(coord, out Chunk chunk) ||
            !DirtyRegionMarker.TryMarkCell(_chunks, wx, wy, DirtyPhaseTarget.Working, includeBoundaryNeighbors: true))
        {
            return false;
        }

        int local = CellAddressing.LocalIndex(wx, wy);
        chunk.SetMaterialAt(local, material);
        chunk.FlagsBuffer[local] = CellFlags.RigidOwned;
        chunk.LifetimeBuffer[local] = 0;
        chunk.DamageBuffer[local] = 0;
        return true;
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

    // 刚体 erase/stamp 走 working dirty 并唤醒邻接，供下一帧 CA 重检交界。
    private void MarkRigidDirty(int wx, int wy)
    {
        DirtyRegionMarker.MarkCell(_chunks, wx, wy, DirtyPhaseTarget.Working, includeBoundaryNeighbors: true);
    }

    private bool NotifyRigidDamageIfNeeded(int wx, int wy, byte flags, ushort material)
    {
        if (CellFlags.Has(flags, CellFlags.RigidOwned))
        {
            _rigidDamageSink.OnOwnedCellDamaged(wx, wy, material);
            return true;
        }

        return false;
    }
}
