using PixelEngine.Core;

namespace PixelEngine.Simulation;

/// <summary>
/// 材质自定义更新委托。委托应通过 <see cref="ChunkWorkContext" /> 写网格，以保证 parity、dirty 与 KeepAlive 一致。
/// </summary>
public delegate void MaterialCustomUpdate(ref CellCursor cell, ref NeighborWindow window, ref ChunkWorkContext context);

/// <summary>
/// 当前被 custom-update 处理的 cell 游标。
/// </summary>
public ref struct CellCursor
{
    internal CellCursor(int x, int y, ushort material, byte flags, byte lifetime)
    {
        X = x;
        Y = y;
        Material = material;
        Flags = flags;
        Lifetime = lifetime;
    }

    /// <summary>
    /// 世界 X 坐标。
    /// </summary>
    public int X { get; }

    /// <summary>
    /// 世界 Y 坐标。
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// 当前材质 id。
    /// </summary>
    public ushort Material { get; internal set; }

    /// <summary>
    /// 当前 flags。
    /// </summary>
    public byte Flags { get; internal set; }

    /// <summary>
    /// 当前 lifetime。
    /// </summary>
    public byte Lifetime { get; internal set; }
}

/// <summary>
/// custom-update 的 chunk 工作上下文。
/// </summary>
public readonly ref struct ChunkWorkContext
{
    private readonly IChunkSource _chunks;
    private readonly int _originX;
    private readonly int _originY;

    internal ChunkWorkContext(
        IChunkSource chunks,
        int originX,
        int originY,
        byte parityBit)
    {
        _chunks = chunks;
        _originX = originX;
        _originY = originY;
        ParityBit = parityBit;
    }

    /// <summary>
    /// 当前帧 parity 位。
    /// </summary>
    public byte ParityBit { get; }

    /// <summary>
    /// 读取材质。
    /// </summary>
    public readonly ushort GetMaterial(ref NeighborWindow window, int wx, int wy)
    {
        ValidateHalo(wx, wy);
        return window.GetMaterial(wx, wy);
    }

    /// <summary>
    /// 读取 flags。
    /// </summary>
    public readonly byte GetFlags(ref NeighborWindow window, int wx, int wy)
    {
        ValidateHalo(wx, wy);
        return window.GetFlags(wx, wy);
    }

    /// <summary>
    /// 写入一个 cell，并自动打当前帧 parity、标记 working dirty / 跨界 KeepAlive。
    /// </summary>
    public readonly void SetCell(ref NeighborWindow window, int wx, int wy, ushort material, byte persistentFlags, byte lifetime)
    {
        ValidateHalo(wx, wy);
        window.SetMaterial(wx, wy, material);
        window.SetFlags(wx, wy, CellFlags.SetParity(persistentFlags, ParityBit));
        window.SetLifetime(wx, wy, lifetime);
        DirtyRegionMarker.MarkCell(_chunks, wx, wy, DirtyPhaseTarget.Working, includeBoundaryNeighbors: true);
    }

    /// <summary>
    /// 标记指定 cell 在下一 CA tick 继续处理。
    /// </summary>
    public readonly void MarkDirty(int wx, int wy)
    {
        ValidateHalo(wx, wy);
        DirtyRegionMarker.MarkCell(_chunks, wx, wy, DirtyPhaseTarget.Working, includeBoundaryNeighbors: true);
    }

    private readonly void ValidateHalo(int wx, int wy)
    {
        if (Math.Abs(wx - _originX) > EngineConstants.MoveCap ||
            Math.Abs(wy - _originY) > EngineConstants.MoveCap)
        {
            throw new InvalidOperationException("custom-update 写入超出 32px halo。");
        }
    }
}
