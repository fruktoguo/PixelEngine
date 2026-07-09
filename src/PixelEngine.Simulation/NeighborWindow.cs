using System.Diagnostics;
using System.Runtime.CompilerServices;

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
    public NeighborWindow(ChunkCoord center, in ChunkNeighborhood neighborhood)
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
        MaterialAt(wx, wy) = value;
        DamageAt(wx, wy) = 0;
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
    public ref ushort MaterialAt(int wx, int wy)
    {
        int slot = SlotOf(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        return ref Unsafe.Add(ref SelectMaterialBase(slot), local);
    }

    /// <summary>
    /// 返回 flag 的可写引用。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte FlagsAt(int wx, int wy)
    {
        int slot = SlotOf(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        return ref Unsafe.Add(ref SelectFlagsBase(slot), local);
    }

    /// <summary>
    /// 返回 lifetime 的可写引用。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte LifetimeAt(int wx, int wy)
    {
        int slot = SlotOf(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        return ref Unsafe.Add(ref SelectLifetimeBase(slot), local);
    }

    /// <summary>
    /// 返回累计结构破坏度的可写引用。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte DamageAt(int wx, int wy)
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
        (material1, material2) = (material2, material1);

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
        (sourceMaterial, targetMaterial) = (targetMaterial, sourceMaterial);

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
