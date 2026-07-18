using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PixelEngine.Core;

namespace PixelEngine.Simulation;

/// <summary>
/// 64x64 cell 的 SoA 数据块，保存权威 sim 状态与 dirty rectangle 元数据。
/// </summary>
public sealed class Chunk
{
    // 每列拆成上下两个 32-row word：checkerboard 同 pass 的南/北 32px halo 最多分别写入
    // 同一中间 chunk 的低/高半区，避免两个 worker 对同一聚合 word 做冲突 RMW（docs §5.7）。
    private const int ColumnHalfCount = 2;
    private const int IncomingSlotCount = 8;
    private readonly ushort[] _materialBuffer;
    private readonly uint[] _columnOccupancy;
    private readonly PaddedDirtyRectSlot[] _incoming;
    private bool _columnOccupancyValid;

    /// <summary>
    /// 创建 chunk 并分配固定长度 POH SoA 数组。
    /// </summary>
    public Chunk(ChunkCoord coord)
    {
        _materialBuffer = GC.AllocateArray<ushort>(EngineConstants.ChunkArea, pinned: true);
        _columnOccupancy = GC.AllocateArray<uint>(EngineConstants.ChunkSize * ColumnHalfCount, pinned: true);
        FlagsBuffer = GC.AllocateArray<byte>(EngineConstants.ChunkArea, pinned: true);
        LifetimeBuffer = GC.AllocateArray<byte>(EngineConstants.ChunkArea, pinned: true);
        DamageBuffer = GC.AllocateArray<byte>(EngineConstants.ChunkArea, pinned: true);
        _incoming = GC.AllocateArray<PaddedDirtyRectSlot>(IncomingSlotCount, pinned: true);
        Reset(coord);
    }

    /// <summary>
    /// chunk 坐标。
    /// </summary>
    public ChunkCoord Coord { get; private set; }

    /// <summary>
    /// 每 cell 的运行时材质 id，0 表示 Empty。
    /// </summary>
    public ReadOnlySpan<ushort> Material => _materialBuffer;

    /// <summary>
    /// 每 cell 的运行时 flag。
    /// </summary>
    public ReadOnlySpan<byte> Flags => FlagsBuffer;

    /// <summary>
    /// 每 cell 的 lifetime 计数器。
    /// </summary>
    public ReadOnlySpan<byte> Lifetime => LifetimeBuffer;

    /// <summary>
    /// 每 cell 的累计结构破坏度；仅允许存活 Solid cell 非零。
    /// </summary>
    public ReadOnlySpan<byte> Damage => DamageBuffer;

    // 可写数组仅是实现 seam；公开调用者必须使用 CellGrid 或按相位划分的 edit API，
    // 以保持 dirty/parity/KeepAlive 与刚体 ownership 的耦合。
    internal ushort[] MaterialBuffer
    {
        get
        {
            // 该数组只为反序列化与受信任测试保留；取得可写别名后无法追踪逐格写入，
            // 因此下一次 CA 使用前必须从权威 Material SoA 重建派生列索引。
            _columnOccupancyValid = false;
            return _materialBuffer;
        }
    }
    internal byte[] FlagsBuffer { get; }
    internal byte[] LifetimeBuffer { get; }
    internal byte[] DamageBuffer { get; }

    /// <summary>
    /// 本帧迭代用 dirty rectangle。
    /// </summary>
    public DirtyRect CurrentDirty { get; private set; }

    /// <summary>
    /// 本帧累积给下一帧使用的 dirty rectangle。
    /// </summary>
    public DirtyRect WorkingDirty { get; private set; }

    /// <summary>
    /// 来自 8 个邻居的 KeepAlive 入站槽。
    /// </summary>
    public int IncomingDirtySlotCount => IncomingSlotCount;

    /// <summary>
    /// chunk 当前调度状态。
    /// </summary>
    public ChunkState State { get; private set; }

    /// <summary>
    /// 最近一次处理该 chunk 的帧 parity，仅用于调试与测试。
    /// </summary>
    public byte Parity { get; set; }

    /// <summary>
    /// 重置 chunk 元数据并清空 SoA 数组，供池化复用。
    /// </summary>
    public void Reset(ChunkCoord coord)
    {
        Coord = coord;
        Array.Clear(_materialBuffer);
        Array.Clear(_columnOccupancy);
        Array.Clear(FlagsBuffer);
        Array.Clear(LifetimeBuffer);
        Array.Clear(DamageBuffer);
        ClearDirty();
        Parity = 0;
        State = ChunkState.Sleeping;
        _columnOccupancyValid = true;
    }

    /// <summary>
    /// 获取 Material 数组首元素引用，供热路径 ref 漫游使用。
    /// </summary>
    internal ref ushort GetMaterialBase()
    {
        return ref MemoryMarshal.GetArrayDataReference(_materialBuffer);
    }

    /// <summary>
    /// 读取单格 Material，不创建可写数组 alias。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ushort GetMaterialAt(int localIndex)
    {
        return _materialBuffer[localIndex];
    }

    /// <summary>
    /// 写入单格 Material，并在派生索引有效时增量维护对应占用位。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetMaterialAt(int localIndex, ushort material)
    {
        ushort previous = _materialBuffer[localIndex];
        _materialBuffer[localIndex] = material;
        bool previousOccupied = previous != 0;
        bool occupied = material != 0;
        if (_columnOccupancyValid && previousOccupied != occupied)
        {
            SetColumnOccupancy(localIndex, occupied);
        }
    }

    /// <summary>
    /// 使垂直 movement 使用的派生列占用索引与权威 Material SoA 一致。
    /// </summary>
    internal void EnsureColumnOccupancy()
    {
        if (_columnOccupancyValid)
        {
            return;
        }

        RebuildColumnOccupancy();
    }

    /// <summary>
    /// 从权威 Material SoA 重建 64 列、每列上下两个 32-bit 半区的占用位图。
    /// </summary>
    internal void RebuildColumnOccupancy()
    {
        Array.Clear(_columnOccupancy);
        ref ushort materialBase = ref MemoryMarshal.GetArrayDataReference(_materialBuffer);
        for (int localY = 0; localY < EngineConstants.ChunkSize; localY++)
        {
            int local = localY << EngineConstants.ChunkSizeLog2;
            int half = localY >> 5;
            uint bit = 1U << (localY & 31);
            for (int localX = 0; localX < EngineConstants.ChunkSize; localX++)
            {
                if (Unsafe.Add(ref materialBase, local + localX) != 0)
                {
                    _columnOccupancy[(localX * ColumnHalfCount) + half] |= bit;
                }
            }
        }

        _columnOccupancyValid = true;
    }

    /// <summary>
    /// 增量更新单格的派生列占用位；调用方必须已经提交对应 Material 写入。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetColumnOccupancy(int localIndex, bool occupied)
    {
        Debug.Assert(_columnOccupancyValid);
        int localX = localIndex & (EngineConstants.ChunkSize - 1);
        int localY = localIndex >> EngineConstants.ChunkSizeLog2;
        int wordIndex = (localX * ColumnHalfCount) + (localY >> 5);
        uint mask = 1U << (localY & 31);
        ref uint word = ref _columnOccupancy[wordIndex];
        word = occupied ? word | mask : word & ~mask;
    }

    /// <summary>
    /// 返回指定本地列和闭区间内第一个非空 cell 的 Y；没有占用时返回 -1。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int FindFirstOccupiedInColumn(int localX, int minLocalY, int maxLocalY)
    {
        Debug.Assert((uint)localX < EngineConstants.ChunkSize);
        Debug.Assert((uint)minLocalY < EngineConstants.ChunkSize);
        Debug.Assert((uint)maxLocalY < EngineConstants.ChunkSize);
        Debug.Assert(minLocalY <= maxLocalY);
        Debug.Assert(_columnOccupancyValid);

        int firstHalf = minLocalY >> 5;
        int lastHalf = maxLocalY >> 5;
        for (int half = firstHalf; half <= lastHalf; half++)
        {
            int minBit = half == firstHalf ? minLocalY & 31 : 0;
            int maxBit = half == lastHalf ? maxLocalY & 31 : 31;
            uint bits = _columnOccupancy[(localX * ColumnHalfCount) + half] & (uint.MaxValue << minBit);
            if (maxBit != 31)
            {
                bits &= (1U << (maxBit + 1)) - 1U;
            }

            if (bits != 0)
            {
                return (half << 5) + BitOperations.TrailingZeroCount(bits);
            }
        }

        return -1;
    }

    /// <summary>
    /// 获取 Flags 数组首元素引用，供热路径 ref 漫游使用。
    /// </summary>
    internal ref byte GetFlagsBase()
    {
        return ref MemoryMarshal.GetArrayDataReference(FlagsBuffer);
    }

    /// <summary>
    /// 获取 Lifetime 数组首元素引用，供热路径 ref 漫游使用。
    /// </summary>
    internal ref byte GetLifetimeBase()
    {
        return ref MemoryMarshal.GetArrayDataReference(LifetimeBuffer);
    }

    /// <summary>
    /// 获取 Damage 数组首元素引用，供热路径 ref 漫游使用。
    /// </summary>
    internal ref byte GetDamageBase()
    {
        return ref MemoryMarshal.GetArrayDataReference(DamageBuffer);
    }

    /// <summary>
    /// 将一个本地 cell 合并到 working dirty rect，并唤醒 chunk。
    /// </summary>
    public void MarkWorkingDirty(int lx, int ly, int padding)
    {
        if (WorkingDirty == DirtyRect.Full)
        {
            return;
        }

        WorkingDirty = WorkingDirty.Union(lx, ly, padding);
        State = ChunkState.Awake;
    }

    internal void MarkCurrentDirty(DirtyRect rect)
    {
        if (rect.IsEmpty)
        {
            return;
        }

        CurrentDirty = CurrentDirty.Union(rect);
        State = ChunkState.Awake;
    }

    internal void MarkWorkingDirty(DirtyRect rect)
    {
        if (rect.IsEmpty || WorkingDirty == DirtyRect.Full)
        {
            return;
        }

        WorkingDirty = WorkingDirty.Union(rect);
        State = ChunkState.Awake;
    }

    /// <summary>
    /// 远区 chunk 本帧被降频跳过时，将 current dirty 保留到下一帧，避免 phase 6 swap 丢工作。
    /// </summary>
    internal void DeferCurrentDirty()
    {
        if (CurrentDirty.IsEmpty)
        {
            return;
        }

        WorkingDirty = WorkingDirty.Union(CurrentDirty);
        State = ChunkState.Awake;
    }

    /// <summary>
    /// 隔帧运行前将 dirty rect 内已有 cell 的 parity 调成“未处理本帧”。
    /// </summary>
    internal void PrepareCurrentDirtyForParity(byte parityBit)
    {
        DirtyRect rect = CurrentDirty;
        if (rect.IsEmpty)
        {
            return;
        }

        // 隔帧 chunk 把 dirty 区内 cell 标为“上帧已处理”，避免 parity 跳变漏更新。
        byte staleParity = (byte)((parityBit ^ CellFlags.Parity) & CellFlags.Parity);
        for (int ly = rect.MinY; ly <= rect.MaxY; ly++)
        {
            int localStart = (ly * EngineConstants.ChunkSize) + rect.MinX;
            int run = rect.MaxX - rect.MinX + 1;
            CellSpanOps.SetParityForOccupiedCells(
                _materialBuffer.AsSpan(localStart, run),
                FlagsBuffer.AsSpan(localStart, run),
                staleParity);
        }
    }

    /// <summary>
    /// 直接设置当前 dirty rect，供测试、加载与后续帧边界 swap 使用。
    /// </summary>
    public void SetCurrentDirty(DirtyRect rect)
    {
        EnsureColumnOccupancy();
        CurrentDirty = rect;
        State = rect.IsEmpty && WorkingDirty.IsEmpty ? ChunkState.Sleeping : ChunkState.Awake;
    }

    /// <summary>
    /// 直接设置 working dirty rect，供测试、编辑器与后续相位入口使用。
    /// </summary>
    public void SetWorkingDirty(DirtyRect rect)
    {
        EnsureColumnOccupancy();
        WorkingDirty = rect;
        State = rect.IsEmpty && CurrentDirty.IsEmpty ? ChunkState.Sleeping : ChunkState.Awake;
    }

    /// <summary>
    /// 合并一个 KeepAlive 入站槽。
    /// </summary>
    public void MarkIncomingDirty(int slot, DirtyRect rect)
    {
        if ((uint)slot >= IncomingSlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        _incoming[slot].Rect = _incoming[slot].Rect.Union(rect);
        if (!rect.IsEmpty)
        {
            State = ChunkState.Awake;
        }
    }

    /// <summary>
    /// 在帧边界交换 dirty rectangle：current 接收 working 与 incoming 合并结果，working/incoming 清空。
    /// </summary>
    public void SwapDirtyRects()
    {
        // 帧边界双缓冲：working + 8 路 KeepAlive 合并为下帧 current。
        DirtyRect next = WorkingDirty;
        for (int i = 0; i < _incoming.Length; i++)
        {
            next = next.Union(_incoming[i].Rect);
        }

        CurrentDirty = next;
        WorkingDirty = DirtyRect.Empty;
        ClearIncomingDirty();
        State = CurrentDirty.IsEmpty ? ChunkState.Sleeping : ChunkState.Awake;
    }

    /// <summary>
    /// 读取指定 KeepAlive 入站槽的 dirty rectangle。
    /// </summary>
    public DirtyRect GetIncomingDirty(int slot)
    {
        return (uint)slot >= IncomingSlotCount ? throw new ArgumentOutOfRangeException(nameof(slot)) : _incoming[slot].Rect;
    }

    /// <summary>
    /// 清空所有 dirty rectangle 元数据。
    /// </summary>
    public void ClearDirty()
    {
        CurrentDirty = DirtyRect.Empty;
        WorkingDirty = DirtyRect.Empty;
        ClearIncomingDirty();
        State = ChunkState.Sleeping;
    }

    private void ClearIncomingDirty()
    {
        for (int i = 0; i < _incoming.Length; i++)
        {
            _incoming[i].Rect = DirtyRect.Empty;
        }
    }
}
