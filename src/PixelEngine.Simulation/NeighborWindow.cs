using System.Diagnostics;
using System.Runtime.CompilerServices;
using PixelEngine.Core;

namespace PixelEngine.Simulation;

/// <summary>
/// 单个中心 chunk 更新时使用的 3x3 邻域热路径访问窗口。
/// </summary>
public ref struct NeighborWindow
{
    private readonly Chunk _chunk0;
    private readonly Chunk _chunk1;
    private readonly Chunk _chunk2;
    private readonly Chunk _chunk3;
    private readonly Chunk _chunk4;
    private readonly Chunk _chunk5;
    private readonly Chunk _chunk6;
    private readonly Chunk _chunk7;
    private readonly Chunk _chunk8;
    private readonly MaterialPropsTable? _materialProps;
    private readonly ICellTopologyChangeSink? _topologyChangeSink;

    private ref ushort _matBase0;
    private ref ushort _matBase1;
    private ref ushort _matBase2;
    private ref ushort _matBase3;
    private ref ushort _matBase4;
    private ref ushort _matBase5;
    private ref ushort _matBase6;
    private ref ushort _matBase7;
    private ref ushort _matBase8;

    private ref byte _flagsBase0;
    private ref byte _flagsBase1;
    private ref byte _flagsBase2;
    private ref byte _flagsBase3;
    private ref byte _flagsBase4;
    private ref byte _flagsBase5;
    private ref byte _flagsBase6;
    private ref byte _flagsBase7;
    private ref byte _flagsBase8;

    private ref byte _lifeBase0;
    private ref byte _lifeBase1;
    private ref byte _lifeBase2;
    private ref byte _lifeBase3;
    private ref byte _lifeBase4;
    private ref byte _lifeBase5;
    private ref byte _lifeBase6;
    private ref byte _lifeBase7;
    private ref byte _lifeBase8;

    private ref byte _damageBase0;
    private ref byte _damageBase1;
    private ref byte _damageBase2;
    private ref byte _damageBase3;
    private ref byte _damageBase4;
    private ref byte _damageBase5;
    private ref byte _damageBase6;
    private ref byte _damageBase7;
    private ref byte _damageBase8;

    /// <summary>
    /// 从驻留 chunk 源构造 3x3 邻域窗口。
    /// </summary>
    public NeighborWindow(IChunkSource chunks, ChunkCoord center)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (!chunks.ResolveNeighborhood(center, out ChunkNeighborhood neighborhood))
        {
            Debug.Assert(false, $"中心 chunk {center} 的 3x3 邻域未完整驻留。");
            throw new InvalidOperationException($"中心 chunk {center} 的 3x3 邻域未完整驻留。");
        }

        BaseChunkX = center.X;
        BaseChunkY = center.Y;

        _chunk0 = neighborhood.Slot0;
        _chunk1 = neighborhood.Slot1;
        _chunk2 = neighborhood.Slot2;
        _chunk3 = neighborhood.Slot3;
        _chunk4 = neighborhood.Slot4;
        _chunk5 = neighborhood.Slot5;
        _chunk6 = neighborhood.Slot6;
        _chunk7 = neighborhood.Slot7;
        _chunk8 = neighborhood.Slot8;
        _materialProps = null;
        _topologyChangeSink = null;

        _matBase0 = ref neighborhood.Slot0.GetMaterialBase();
        _matBase1 = ref neighborhood.Slot1.GetMaterialBase();
        _matBase2 = ref neighborhood.Slot2.GetMaterialBase();
        _matBase3 = ref neighborhood.Slot3.GetMaterialBase();
        _matBase4 = ref neighborhood.Slot4.GetMaterialBase();
        _matBase5 = ref neighborhood.Slot5.GetMaterialBase();
        _matBase6 = ref neighborhood.Slot6.GetMaterialBase();
        _matBase7 = ref neighborhood.Slot7.GetMaterialBase();
        _matBase8 = ref neighborhood.Slot8.GetMaterialBase();

        _flagsBase0 = ref neighborhood.Slot0.GetFlagsBase();
        _flagsBase1 = ref neighborhood.Slot1.GetFlagsBase();
        _flagsBase2 = ref neighborhood.Slot2.GetFlagsBase();
        _flagsBase3 = ref neighborhood.Slot3.GetFlagsBase();
        _flagsBase4 = ref neighborhood.Slot4.GetFlagsBase();
        _flagsBase5 = ref neighborhood.Slot5.GetFlagsBase();
        _flagsBase6 = ref neighborhood.Slot6.GetFlagsBase();
        _flagsBase7 = ref neighborhood.Slot7.GetFlagsBase();
        _flagsBase8 = ref neighborhood.Slot8.GetFlagsBase();

        _lifeBase0 = ref neighborhood.Slot0.GetLifetimeBase();
        _lifeBase1 = ref neighborhood.Slot1.GetLifetimeBase();
        _lifeBase2 = ref neighborhood.Slot2.GetLifetimeBase();
        _lifeBase3 = ref neighborhood.Slot3.GetLifetimeBase();
        _lifeBase4 = ref neighborhood.Slot4.GetLifetimeBase();
        _lifeBase5 = ref neighborhood.Slot5.GetLifetimeBase();
        _lifeBase6 = ref neighborhood.Slot6.GetLifetimeBase();
        _lifeBase7 = ref neighborhood.Slot7.GetLifetimeBase();
        _lifeBase8 = ref neighborhood.Slot8.GetLifetimeBase();

        _damageBase0 = ref neighborhood.Slot0.GetDamageBase();
        _damageBase1 = ref neighborhood.Slot1.GetDamageBase();
        _damageBase2 = ref neighborhood.Slot2.GetDamageBase();
        _damageBase3 = ref neighborhood.Slot3.GetDamageBase();
        _damageBase4 = ref neighborhood.Slot4.GetDamageBase();
        _damageBase5 = ref neighborhood.Slot5.GetDamageBase();
        _damageBase6 = ref neighborhood.Slot6.GetDamageBase();
        _damageBase7 = ref neighborhood.Slot7.GetDamageBase();
        _damageBase8 = ref neighborhood.Slot8.GetDamageBase();
    }

    /// <summary>
    /// 从调度阶段已解析的 3x3 邻域构造窗口。
    /// </summary>
    /// <param name="center">中心 chunk 坐标。</param>
    /// <param name="neighborhood">完整驻留的 3x3 邻域。</param>
    /// <param name="materialProps">可选材质热属性；与 topology sink 同时提供时跟踪 Solid 占用变化。</param>
    /// <param name="topologyChangeSink">可选固体拓扑变化 sink。</param>
    public NeighborWindow(
        ChunkCoord center,
        in ChunkNeighborhood neighborhood,
        MaterialPropsTable? materialProps = null,
        ICellTopologyChangeSink? topologyChangeSink = null)
    {
        BaseChunkX = center.X;
        BaseChunkY = center.Y;

        _chunk0 = neighborhood.Slot0;
        _chunk1 = neighborhood.Slot1;
        _chunk2 = neighborhood.Slot2;
        _chunk3 = neighborhood.Slot3;
        _chunk4 = neighborhood.Slot4;
        _chunk5 = neighborhood.Slot5;
        _chunk6 = neighborhood.Slot6;
        _chunk7 = neighborhood.Slot7;
        _chunk8 = neighborhood.Slot8;
        _materialProps = materialProps;
        _topologyChangeSink = topologyChangeSink;

        _matBase0 = ref neighborhood.Slot0.GetMaterialBase();
        _matBase1 = ref neighborhood.Slot1.GetMaterialBase();
        _matBase2 = ref neighborhood.Slot2.GetMaterialBase();
        _matBase3 = ref neighborhood.Slot3.GetMaterialBase();
        _matBase4 = ref neighborhood.Slot4.GetMaterialBase();
        _matBase5 = ref neighborhood.Slot5.GetMaterialBase();
        _matBase6 = ref neighborhood.Slot6.GetMaterialBase();
        _matBase7 = ref neighborhood.Slot7.GetMaterialBase();
        _matBase8 = ref neighborhood.Slot8.GetMaterialBase();

        _flagsBase0 = ref neighborhood.Slot0.GetFlagsBase();
        _flagsBase1 = ref neighborhood.Slot1.GetFlagsBase();
        _flagsBase2 = ref neighborhood.Slot2.GetFlagsBase();
        _flagsBase3 = ref neighborhood.Slot3.GetFlagsBase();
        _flagsBase4 = ref neighborhood.Slot4.GetFlagsBase();
        _flagsBase5 = ref neighborhood.Slot5.GetFlagsBase();
        _flagsBase6 = ref neighborhood.Slot6.GetFlagsBase();
        _flagsBase7 = ref neighborhood.Slot7.GetFlagsBase();
        _flagsBase8 = ref neighborhood.Slot8.GetFlagsBase();

        _lifeBase0 = ref neighborhood.Slot0.GetLifetimeBase();
        _lifeBase1 = ref neighborhood.Slot1.GetLifetimeBase();
        _lifeBase2 = ref neighborhood.Slot2.GetLifetimeBase();
        _lifeBase3 = ref neighborhood.Slot3.GetLifetimeBase();
        _lifeBase4 = ref neighborhood.Slot4.GetLifetimeBase();
        _lifeBase5 = ref neighborhood.Slot5.GetLifetimeBase();
        _lifeBase6 = ref neighborhood.Slot6.GetLifetimeBase();
        _lifeBase7 = ref neighborhood.Slot7.GetLifetimeBase();
        _lifeBase8 = ref neighborhood.Slot8.GetLifetimeBase();

        _damageBase0 = ref neighborhood.Slot0.GetDamageBase();
        _damageBase1 = ref neighborhood.Slot1.GetDamageBase();
        _damageBase2 = ref neighborhood.Slot2.GetDamageBase();
        _damageBase3 = ref neighborhood.Slot3.GetDamageBase();
        _damageBase4 = ref neighborhood.Slot4.GetDamageBase();
        _damageBase5 = ref neighborhood.Slot5.GetDamageBase();
        _damageBase6 = ref neighborhood.Slot6.GetDamageBase();
        _damageBase7 = ref neighborhood.Slot7.GetDamageBase();
        _damageBase8 = ref neighborhood.Slot8.GetDamageBase();
    }

    /// <summary>
    /// 中心 chunk 的 X 坐标。
    /// </summary>
    public readonly int BaseChunkX { get; }

    /// <summary>
    /// 中心 chunk 的 Y 坐标。
    /// </summary>
    public readonly int BaseChunkY { get; }

    /// <summary>
    /// 按 3x3 slot 读取调度阶段已经解析的驻留 chunk。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Chunk GetChunk(int slot)
    {
        return slot switch
        {
            0 => _chunk0,
            1 => _chunk1,
            2 => _chunk2,
            3 => _chunk3,
            4 => _chunk4,
            5 => _chunk5,
            6 => _chunk6,
            7 => _chunk7,
            8 => _chunk8,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }

    /// <summary>
    /// 计算世界坐标落入 3x3 邻域的 slot。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int SlotOf(int wx, int wy)
    {
        // 3x3 slot 布局：行优先，中心 chunk 恒为 slot 4。
        int dcx = CellAddressing.ChunkOf(wx) - BaseChunkX;
        int dcy = CellAddressing.ChunkOf(wy) - BaseChunkY;
        return ((dcy + 1) * 3) + dcx + 1;
    }

    /// <summary>
    /// 读取材质 id。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort GetMaterial(int wx, int wy)
    {
        return MaterialAt(wx, wy);
    }

    /// <summary>
    /// 写入材质 id。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetMaterial(int wx, int wy, ushort value)
    {
        int slot = SlotOf(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        ref ushort material = ref Unsafe.Add(ref SelectMaterialBase(slot), local);
        ushort previous = material;
        bool wasOccupied = material != 0;
        material = value;
        bool isOccupied = value != 0;
        if (wasOccupied != isOccupied)
        {
            GetChunk(slot).SetColumnOccupancy(local, isOccupied);
        }

        Unsafe.Add(ref SelectDamageBase(slot), local) = 0;
        NotifyTopologyChange(wx, wy, previous, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void NotifyTopologyChange(int wx, int wy, ushort sourceMaterial, ushort targetMaterial)
    {
        if (sourceMaterial == targetMaterial || _materialProps is null || _topologyChangeSink is null)
        {
            return;
        }

        CellTopologyChangeKind kind = CellTopologyChangeClassifier.Classify(
            _materialProps.TypeOf(sourceMaterial),
            _materialProps.TypeOf(targetMaterial));
        if (kind == CellTopologyChangeKind.None)
        {
            return;
        }

        CellTopologyChangeEvent item = new(wx, wy, sourceMaterial, targetMaterial, kind);
        _topologyChangeSink.OnCellTopologyChanged(in item);
    }

    /// <summary>
    /// 读取 flag。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetFlags(int wx, int wy)
    {
        return FlagsAt(wx, wy);
    }

    /// <summary>
    /// 写入 flag。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetFlags(int wx, int wy, byte value)
    {
        FlagsAt(wx, wy) = value;
    }

    /// <summary>
    /// 读取 lifetime。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetLifetime(int wx, int wy)
    {
        return LifetimeAt(wx, wy);
    }

    /// <summary>
    /// 写入 lifetime。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetLifetime(int wx, int wy, byte value)
    {
        LifetimeAt(wx, wy) = value;
    }

    /// <summary>
    /// 读取累计结构破坏度。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetDamage(int wx, int wy)
    {
        return DamageAt(wx, wy);
    }

    /// <summary>
    /// 写入累计结构破坏度。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetDamage(int wx, int wy, byte value)
    {
        DamageAt(wx, wy) = value;
    }

    /// <summary>
    /// 返回材质 id 的可写引用。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref ushort MaterialAt(int wx, int wy)
    {
        int slot = SlotOf(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        return ref Unsafe.Add(ref SelectMaterialBase(slot), local);
    }

    /// <summary>
    /// 返回 flag 的可写引用。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref byte FlagsAt(int wx, int wy)
    {
        int slot = SlotOf(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        return ref Unsafe.Add(ref SelectFlagsBase(slot), local);
    }

    /// <summary>
    /// 返回 lifetime 的可写引用。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref byte LifetimeAt(int wx, int wy)
    {
        int slot = SlotOf(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        return ref Unsafe.Add(ref SelectLifetimeBase(slot), local);
    }

    /// <summary>
    /// 返回累计结构破坏度的可写引用。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref byte DamageAt(int wx, int wy)
    {
        int slot = SlotOf(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        return ref Unsafe.Add(ref SelectDamageBase(slot), local);
    }

    /// <summary>
    /// 交换两个 cell 的 Material、Flags 与 Lifetime，返回是否跨 chunk slot。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Swap(int wx1, int wy1, int wx2, int wy2)
    {
        int slot1 = SlotOf(wx1, wy1);
        int slot2 = SlotOf(wx2, wy2);
        int local1 = CellAddressing.LocalIndex(wx1, wy1);
        int local2 = CellAddressing.LocalIndex(wx2, wy2);

        // 交换 Material/Flags/Lifetime 三元组；Damage 清零避免破坏度随 swap 漂移。
        ref ushort material1 = ref Unsafe.Add(ref SelectMaterialBase(slot1), local1);
        ref ushort material2 = ref Unsafe.Add(ref SelectMaterialBase(slot2), local2);
        bool occupied1 = material1 != 0;
        bool occupied2 = material2 != 0;
        (material1, material2) = (material2, material1);
        if (occupied1 != occupied2)
        {
            GetChunk(slot1).SetColumnOccupancy(local1, occupied2);
            GetChunk(slot2).SetColumnOccupancy(local2, occupied1);
        }

        ref byte flags1 = ref Unsafe.Add(ref SelectFlagsBase(slot1), local1);
        ref byte flags2 = ref Unsafe.Add(ref SelectFlagsBase(slot2), local2);
        (flags1, flags2) = (flags2, flags1);

        ref byte life1 = ref Unsafe.Add(ref SelectLifetimeBase(slot1), local1);
        ref byte life2 = ref Unsafe.Add(ref SelectLifetimeBase(slot2), local2);
        (life1, life2) = (life2, life1);

        Unsafe.Add(ref SelectDamageBase(slot1), local1) = 0;
        Unsafe.Add(ref SelectDamageBase(slot2), local2) = 0;

        return slot1 != slot2;
    }

    /// <summary>
    /// 在 movement 扫描阶段一次 slot/local 解析读取非空目标的 material / flags。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadNonEmptyMoveTarget(
        int targetX,
        int targetY,
        out ushort targetMaterial,
        out byte targetFlags)
    {
        int targetSlot = SlotOf(targetX, targetY);
        int targetLocal = CellAddressing.LocalIndex(targetX, targetY);
        targetMaterial = Unsafe.Add(ref SelectMaterialBase(targetSlot), targetLocal);
        if (targetMaterial == 0)
        {
            targetFlags = 0;
            return false;
        }

        targetFlags = Unsafe.Add(ref SelectFlagsBase(targetSlot), targetLocal);
        return true;
    }

    /// <summary>
    /// 从中心 chunk 的已知本地坐标读取 movement 目标，供中心 chunk 内的垂直扫描复用 slot 4 基址。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReadCenterNonEmptyMoveTarget(
        int targetLocal,
        out ushort targetMaterial,
        out byte targetFlags)
    {
        targetMaterial = Unsafe.Add(ref _matBase4, targetLocal);
        if (targetMaterial == 0)
        {
            targetFlags = 0;
            return false;
        }

        targetFlags = Unsafe.Add(ref _flagsBase4, targetLocal);
        return true;
    }

    /// <summary>
    /// 在已解析 slot/local 上读取非空 movement 目标，供列占用索引命中的唯一 cell 取值。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReadKnownNonEmptyMoveTarget(
        int targetSlot,
        int targetLocal,
        out ushort targetMaterial,
        out byte targetFlags)
    {
        targetMaterial = Unsafe.Add(ref SelectMaterialBase(targetSlot), targetLocal);
        if (targetMaterial == 0)
        {
            targetFlags = 0;
            return false;
        }

        targetFlags = Unsafe.Add(ref SelectFlagsBase(targetSlot), targetLocal);
        return true;
    }

    /// <summary>
    /// 使用中心与南邻 chunk 的两级列位图，定位 MoveCap 内第一个非空 cell。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly bool TryFindFirstOccupiedBelow(
        int sourceLocalX,
        int sourceLocalY,
        out int targetY,
        out int targetSlot,
        out int targetLocal)
    {
        int firstLocalY = sourceLocalY + 2;
        int lastLocalY = sourceLocalY + EngineConstants.MoveCap;
        if (firstLocalY < EngineConstants.ChunkSize)
        {
            int foundLocalY = _chunk4.FindFirstOccupiedInColumn(
                sourceLocalX,
                firstLocalY,
                Math.Min(lastLocalY, EngineConstants.ChunkSize - 1));
            if (foundLocalY >= 0)
            {
                targetY = (BaseChunkY << EngineConstants.ChunkSizeLog2) + foundLocalY;
                targetSlot = 4;
                targetLocal = CellAddressing.LocalIndexFromLocal(sourceLocalX, foundLocalY);
                return true;
            }
        }

        if (lastLocalY >= EngineConstants.ChunkSize)
        {
            int foundLocalY = _chunk7.FindFirstOccupiedInColumn(
                sourceLocalX,
                Math.Max(0, firstLocalY - EngineConstants.ChunkSize),
                lastLocalY - EngineConstants.ChunkSize);
            if (foundLocalY >= 0)
            {
                targetY = ((BaseChunkY + 1) << EngineConstants.ChunkSizeLog2) + foundLocalY;
                targetSlot = 7;
                targetLocal = CellAddressing.LocalIndexFromLocal(sourceLocalX, foundLocalY);
                return true;
            }
        }

        targetY = 0;
        targetSlot = 0;
        targetLocal = 0;
        return false;
    }

    /// <summary>
    /// 在 movement 扫描阶段一次 slot/local 解析判断目标是否可被源 cell 置换。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanDisplaceForMove(
        int targetX,
        int targetY,
        MaterialPropsTable materials,
        byte sourceDensity,
        byte parityBit)
    {
        int targetSlot = SlotOf(targetX, targetY);
        int targetLocal = CellAddressing.LocalIndex(targetX, targetY);
        ushort targetMaterial = Unsafe.Add(ref SelectMaterialBase(targetSlot), targetLocal);
        if (targetMaterial == 0)
        {
            return true;
        }

        byte targetFlags = Unsafe.Add(ref SelectFlagsBase(targetSlot), targetLocal);
        return !CellFlags.MatchesFrame(targetFlags, parityBit) &&
            materials.DensityOf(targetMaterial) < sourceDensity;
    }

    /// <summary>
    /// 在 movement 热路径中一次性完成目标可置换判断、刚体占用通知、cell swap 与 parity 标记。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryMoveCell(
        int sourceX,
        int sourceY,
        int targetX,
        int targetY,
        MaterialPropsTable materials,
        byte sourceDensity,
        byte parityBit,
        IRigidDamageSink rigidDamageSink,
        out int targetSlot)
    {
        int sourceSlot = SlotOf(sourceX, sourceY);
        targetSlot = SlotOf(targetX, targetY);
        int sourceLocal = CellAddressing.LocalIndex(sourceX, sourceY);
        int targetLocal = CellAddressing.LocalIndex(targetX, targetY);

        ref ushort targetMaterial = ref Unsafe.Add(ref SelectMaterialBase(targetSlot), targetLocal);
        ref byte targetFlags = ref Unsafe.Add(ref SelectFlagsBase(targetSlot), targetLocal);
        // 目标非空时须未更新且密度更低才可置换；否则 movement 失败。
        if (targetMaterial != 0 &&
            (CellFlags.MatchesFrame(targetFlags, parityBit) ||
            materials.DensityOf(targetMaterial) >= sourceDensity))
        {
            return false;
        }

        if (CellFlags.Has(targetFlags, CellFlags.RigidOwned))
        {
            rigidDamageSink.OnOwnedCellDamaged(targetX, targetY, targetMaterial);
            targetFlags = CellFlags.Clear(targetFlags, CellFlags.RigidOwned);
        }

        ref ushort sourceMaterial = ref Unsafe.Add(ref SelectMaterialBase(sourceSlot), sourceLocal);
        bool sourceOccupied = sourceMaterial != 0;
        bool targetOccupied = targetMaterial != 0;
        (sourceMaterial, targetMaterial) = (targetMaterial, sourceMaterial);
        if (sourceOccupied != targetOccupied)
        {
            GetChunk(sourceSlot).SetColumnOccupancy(sourceLocal, targetOccupied);
            GetChunk(targetSlot).SetColumnOccupancy(targetLocal, sourceOccupied);
        }

        ref byte sourceFlags = ref Unsafe.Add(ref SelectFlagsBase(sourceSlot), sourceLocal);
        (sourceFlags, targetFlags) = (targetFlags, sourceFlags);
        // 交换后双方立即标记本帧 parity，满足 checkerboard 单写约束。
        sourceFlags = CellFlags.SetParity(sourceFlags, parityBit);
        targetFlags = CellFlags.SetParity(targetFlags, parityBit);

        ref byte sourceLifetime = ref Unsafe.Add(ref SelectLifetimeBase(sourceSlot), sourceLocal);
        ref byte targetLifetime = ref Unsafe.Add(ref SelectLifetimeBase(targetSlot), targetLocal);
        (sourceLifetime, targetLifetime) = (targetLifetime, sourceLifetime);

        Unsafe.Add(ref SelectDamageBase(sourceSlot), sourceLocal) = 0;
        Unsafe.Add(ref SelectDamageBase(targetSlot), targetLocal) = 0;
        return true;
    }

    /// <summary>
    /// 使用已知的中心 chunk source local index 完成 movement，避免重复解析 source slot/local。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryMoveCellFromCenter(
        int sourceLocalIndex,
        int targetX,
        int targetY,
        MaterialPropsTable materials,
        byte sourceDensity,
        byte parityBit,
        IRigidDamageSink rigidDamageSink,
        out int targetSlot)
    {
        targetSlot = SlotOf(targetX, targetY);
        int targetLocal = CellAddressing.LocalIndex(targetX, targetY);

        return TryMoveCellFromCenterKnownTarget(
            sourceLocalIndex,
            targetX,
            targetY,
            targetSlot,
            targetLocal,
            materials,
            sourceDensity,
            parityBit,
            rigidDamageSink);
    }

    /// <summary>
    /// 使用已解析的 target slot/local 完成中心 source movement，避免探测后的二次寻址。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryMoveCellFromCenterKnownTarget(
        int sourceLocalIndex,
        int targetX,
        int targetY,
        int targetSlot,
        int targetLocal,
        MaterialPropsTable materials,
        byte sourceDensity,
        byte parityBit,
        IRigidDamageSink rigidDamageSink)
    {
        ref ushort targetMaterial = ref Unsafe.Add(ref SelectMaterialBase(targetSlot), targetLocal);
        ref byte targetFlags = ref Unsafe.Add(ref SelectFlagsBase(targetSlot), targetLocal);
        // 目标非空时须未更新且密度更低才可置换；否则 movement 失败。
        if (targetMaterial != 0 &&
            (CellFlags.MatchesFrame(targetFlags, parityBit) ||
            materials.DensityOf(targetMaterial) >= sourceDensity))
        {
            return false;
        }

        MoveCellFromCenterKnownEligibleTarget(
            sourceLocalIndex,
            targetX,
            targetY,
            parityBit,
            rigidDamageSink,
            GetChunk(targetSlot),
            targetLocal,
            ref targetMaterial,
            ref targetFlags,
            ref Unsafe.Add(ref SelectLifetimeBase(targetSlot), targetLocal),
            ref Unsafe.Add(ref SelectDamageBase(targetSlot), targetLocal));
        return true;
    }

    /// <summary>
    /// 对已知位于中心 chunk 的 target 完成 movement，直接复用 slot 4 SoA 基址。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryMoveCellFromCenterKnownCenterTarget(
        int sourceLocalIndex,
        int targetX,
        int targetY,
        int targetLocal,
        MaterialPropsTable materials,
        byte sourceDensity,
        byte parityBit,
        IRigidDamageSink rigidDamageSink)
    {
        ref ushort targetMaterial = ref Unsafe.Add(ref _matBase4, targetLocal);
        ref byte targetFlags = ref Unsafe.Add(ref _flagsBase4, targetLocal);
        if (targetMaterial != 0 &&
            (CellFlags.MatchesFrame(targetFlags, parityBit) ||
            materials.DensityOf(targetMaterial) >= sourceDensity))
        {
            return false;
        }

        MoveCellFromCenterKnownEligibleTarget(
            sourceLocalIndex,
            targetX,
            targetY,
            parityBit,
            rigidDamageSink,
            _chunk4,
            targetLocal,
            ref targetMaterial,
            ref targetFlags,
            ref Unsafe.Add(ref _lifeBase4, targetLocal),
            ref Unsafe.Add(ref _damageBase4, targetLocal));
        return true;
    }

    /// <summary>
    /// 对已由垂直扫描证明可置换的目标执行中心 cell movement，避免重复读取目标属性。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void MoveCellFromCenterKnownEligibleTarget(
        int sourceLocalIndex,
        int targetX,
        int targetY,
        int targetSlot,
        int targetLocal,
        byte parityBit,
        IRigidDamageSink rigidDamageSink)
    {
        ref ushort targetMaterial = ref Unsafe.Add(ref SelectMaterialBase(targetSlot), targetLocal);
        ref byte targetFlags = ref Unsafe.Add(ref SelectFlagsBase(targetSlot), targetLocal);
        MoveCellFromCenterKnownEligibleTarget(
            sourceLocalIndex,
            targetX,
            targetY,
            parityBit,
            rigidDamageSink,
            GetChunk(targetSlot),
            targetLocal,
            ref targetMaterial,
            ref targetFlags,
            ref Unsafe.Add(ref SelectLifetimeBase(targetSlot), targetLocal),
            ref Unsafe.Add(ref SelectDamageBase(targetSlot), targetLocal));
    }

    /// <summary>
    /// 对已证明可置换且位于中心 chunk 的 target 执行 movement，直接复用 slot 4 SoA 基址。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void MoveCellFromCenterKnownEligibleCenterTarget(
        int sourceLocalIndex,
        int targetX,
        int targetY,
        int targetLocal,
        byte parityBit,
        IRigidDamageSink rigidDamageSink)
    {
        ref ushort targetMaterial = ref Unsafe.Add(ref _matBase4, targetLocal);
        ref byte targetFlags = ref Unsafe.Add(ref _flagsBase4, targetLocal);
        MoveCellFromCenterKnownEligibleTarget(
            sourceLocalIndex,
            targetX,
            targetY,
            parityBit,
            rigidDamageSink,
            _chunk4,
            targetLocal,
            ref targetMaterial,
            ref targetFlags,
            ref Unsafe.Add(ref _lifeBase4, targetLocal),
            ref Unsafe.Add(ref _damageBase4, targetLocal));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MoveCellFromCenterKnownEligibleTarget(
        int sourceLocalIndex,
        int targetX,
        int targetY,
        byte parityBit,
        IRigidDamageSink rigidDamageSink,
        Chunk targetChunk,
        int targetLocal,
        ref ushort targetMaterial,
        ref byte targetFlags,
        ref byte targetLifetime,
        ref byte targetDamage)
    {
        if (CellFlags.Has(targetFlags, CellFlags.RigidOwned))
        {
            rigidDamageSink.OnOwnedCellDamaged(targetX, targetY, targetMaterial);
            targetFlags = CellFlags.Clear(targetFlags, CellFlags.RigidOwned);
        }

        ref ushort sourceMaterial = ref Unsafe.Add(ref _matBase4, sourceLocalIndex);
        bool targetWasEmpty = targetMaterial == 0;
        (sourceMaterial, targetMaterial) = (targetMaterial, sourceMaterial);
        if (targetWasEmpty)
        {
            _chunk4.SetColumnOccupancy(sourceLocalIndex, occupied: false);
            targetChunk.SetColumnOccupancy(targetLocal, occupied: true);
        }

        ref byte sourceFlags = ref Unsafe.Add(ref _flagsBase4, sourceLocalIndex);
        (sourceFlags, targetFlags) = (targetFlags, sourceFlags);
        // 交换后双方立即标记本帧 parity，满足 checkerboard 单写约束。
        sourceFlags = CellFlags.SetParity(sourceFlags, parityBit);
        targetFlags = CellFlags.SetParity(targetFlags, parityBit);

        ref byte sourceLifetime = ref Unsafe.Add(ref _lifeBase4, sourceLocalIndex);
        (sourceLifetime, targetLifetime) = (targetLifetime, sourceLifetime);

        Unsafe.Add(ref _damageBase4, sourceLocalIndex) = 0;
        targetDamage = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref ushort SelectMaterialBase(int slot)
    {
        switch (slot)
        {
            case 0:
                return ref _matBase0;
            case 1:
                return ref _matBase1;
            case 2:
                return ref _matBase2;
            case 3:
                return ref _matBase3;
            case 4:
                return ref _matBase4;
            case 5:
                return ref _matBase5;
            case 6:
                return ref _matBase6;
            case 7:
                return ref _matBase7;
            case 8:
                return ref _matBase8;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref byte SelectFlagsBase(int slot)
    {
        switch (slot)
        {
            case 0:
                return ref _flagsBase0;
            case 1:
                return ref _flagsBase1;
            case 2:
                return ref _flagsBase2;
            case 3:
                return ref _flagsBase3;
            case 4:
                return ref _flagsBase4;
            case 5:
                return ref _flagsBase5;
            case 6:
                return ref _flagsBase6;
            case 7:
                return ref _flagsBase7;
            case 8:
                return ref _flagsBase8;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref byte SelectLifetimeBase(int slot)
    {
        switch (slot)
        {
            case 0:
                return ref _lifeBase0;
            case 1:
                return ref _lifeBase1;
            case 2:
                return ref _lifeBase2;
            case 3:
                return ref _lifeBase3;
            case 4:
                return ref _lifeBase4;
            case 5:
                return ref _lifeBase5;
            case 6:
                return ref _lifeBase6;
            case 7:
                return ref _lifeBase7;
            case 8:
                return ref _lifeBase8;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref byte SelectDamageBase(int slot)
    {
        switch (slot)
        {
            case 0:
                return ref _damageBase0;
            case 1:
                return ref _damageBase1;
            case 2:
                return ref _damageBase2;
            case 3:
                return ref _damageBase3;
            case 4:
                return ref _damageBase4;
            case 5:
                return ref _damageBase5;
            case 6:
                return ref _damageBase6;
            case 7:
                return ref _damageBase7;
            case 8:
                return ref _damageBase8;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }
}
