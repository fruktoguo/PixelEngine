using System.Runtime.CompilerServices;

namespace PixelEngine.Simulation;

/// <summary>
/// 单个中心 chunk 更新时使用的 3x3 邻域热路径访问窗口。
/// </summary>
public ref struct NeighborWindow
{
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

    /// <summary>
    /// 从驻留 chunk 源构造 3x3 邻域窗口。
    /// </summary>
    public NeighborWindow(IChunkSource chunks, ChunkCoord center)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (!chunks.ResolveNeighborhood(center, out ChunkNeighborhood neighborhood))
        {
            throw new InvalidOperationException($"中心 chunk {center} 的 3x3 邻域未完整驻留。");
        }

        BaseChunkX = center.X;
        BaseChunkY = center.Y;

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
    /// 计算世界坐标落入 3x3 邻域的 slot。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int SlotOf(int wx, int wy)
    {
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
    /// 交换两个 cell 的 Material、Flags 与 Lifetime，返回是否跨 chunk slot。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Swap(int wx1, int wy1, int wx2, int wy2)
    {
        int slot1 = SlotOf(wx1, wy1);
        int slot2 = SlotOf(wx2, wy2);
        int local1 = CellAddressing.LocalIndex(wx1, wy1);
        int local2 = CellAddressing.LocalIndex(wx2, wy2);

        ref ushort material1 = ref Unsafe.Add(ref SelectMaterialBase(slot1), local1);
        ref ushort material2 = ref Unsafe.Add(ref SelectMaterialBase(slot2), local2);
        (material1, material2) = (material2, material1);

        ref byte flags1 = ref Unsafe.Add(ref SelectFlagsBase(slot1), local1);
        ref byte flags2 = ref Unsafe.Add(ref SelectFlagsBase(slot2), local2);
        (flags1, flags2) = (flags2, flags1);

        ref byte life1 = ref Unsafe.Add(ref SelectLifetimeBase(slot1), local1);
        ref byte life2 = ref Unsafe.Add(ref SelectLifetimeBase(slot2), local2);
        (life1, life2) = (life2, life1);

        return slot1 != slot2;
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
}
