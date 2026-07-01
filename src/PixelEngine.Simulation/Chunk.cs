using System.Runtime.InteropServices;
using PixelEngine.Core;

namespace PixelEngine.Simulation;

/// <summary>
/// 64x64 cell 的 SoA 数据块，保存权威 sim 状态与 dirty rectangle 元数据。
/// </summary>
public sealed class Chunk
{
    private const int IncomingSlotCount = 8;
    private readonly PaddedDirtyRectSlot[] _incoming;

    /// <summary>
    /// 创建 chunk 并分配固定长度 POH SoA 数组。
    /// </summary>
    public Chunk(ChunkCoord coord)
    {
        Material = GC.AllocateArray<ushort>(EngineConstants.ChunkArea, pinned: true);
        Flags = GC.AllocateArray<byte>(EngineConstants.ChunkArea, pinned: true);
        Lifetime = GC.AllocateArray<byte>(EngineConstants.ChunkArea, pinned: true);
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
    public ushort[] Material { get; }

    /// <summary>
    /// 每 cell 的运行时 flag。
    /// </summary>
    public byte[] Flags { get; }

    /// <summary>
    /// 每 cell 的 lifetime 计数器。
    /// </summary>
    public byte[] Lifetime { get; }

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
        Array.Clear(Material);
        Array.Clear(Flags);
        Array.Clear(Lifetime);
        ClearDirty();
        Parity = 0;
        State = ChunkState.Sleeping;
    }

    /// <summary>
    /// 获取 Material 数组首元素引用，供热路径 ref 漫游使用。
    /// </summary>
    public ref ushort GetMaterialBase()
    {
        return ref MemoryMarshal.GetArrayDataReference(Material);
    }

    /// <summary>
    /// 获取 Flags 数组首元素引用，供热路径 ref 漫游使用。
    /// </summary>
    public ref byte GetFlagsBase()
    {
        return ref MemoryMarshal.GetArrayDataReference(Flags);
    }

    /// <summary>
    /// 获取 Lifetime 数组首元素引用，供热路径 ref 漫游使用。
    /// </summary>
    public ref byte GetLifetimeBase()
    {
        return ref MemoryMarshal.GetArrayDataReference(Lifetime);
    }

    /// <summary>
    /// 将一个本地 cell 合并到 working dirty rect，并唤醒 chunk。
    /// </summary>
    public void MarkWorkingDirty(int lx, int ly, int padding)
    {
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
        if (rect.IsEmpty)
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

        byte staleParity = (byte)((parityBit ^ CellFlags.Parity) & CellFlags.Parity);
        for (int ly = rect.MinY; ly <= rect.MaxY; ly++)
        {
            int row = ly * EngineConstants.ChunkSize;
            for (int lx = rect.MinX; lx <= rect.MaxX; lx++)
            {
                int local = row + lx;
                if (Material[local] == 0 && Flags[local] == 0)
                {
                    continue;
                }

                Flags[local] = CellFlags.SetParity(Flags[local], staleParity);
            }
        }
    }

    /// <summary>
    /// 直接设置当前 dirty rect，供测试、加载与后续帧边界 swap 使用。
    /// </summary>
    public void SetCurrentDirty(DirtyRect rect)
    {
        CurrentDirty = rect;
        State = rect.IsEmpty && WorkingDirty.IsEmpty ? ChunkState.Sleeping : ChunkState.Awake;
    }

    /// <summary>
    /// 直接设置 working dirty rect，供测试、编辑器与后续相位入口使用。
    /// </summary>
    public void SetWorkingDirty(DirtyRect rect)
    {
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
