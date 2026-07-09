namespace PixelEngine.Simulation;

/// <summary>
/// Chunk 格子数据与 dirty 状态的深拷贝快照，用于调试、回放或跨线程只读访问。
/// </summary>
internal sealed class ChunkSnapshot
{
    private ChunkSnapshot(
        ChunkCoord coord,
        ushort[] material,
        byte[] flags,
        byte[] lifetime,
        byte[] damage,
        DirtyRect currentDirty,
        DirtyRect workingDirty,
        ChunkState state)
    {
        Coord = coord;
        Material = material;
        Flags = flags;
        Lifetime = lifetime;
        Damage = damage;
        CurrentDirty = currentDirty;
        WorkingDirty = workingDirty;
        State = state;
    }

    /// <summary>快照对应的 chunk 坐标。</summary>
    public ChunkCoord Coord { get; }

    /// <summary>材质 ID 数组副本。</summary>
    public ushort[] Material { get; }

    /// <summary>格子标志数组副本。</summary>
    public byte[] Flags { get; }

    /// <summary>寿命数组副本。</summary>
    public byte[] Lifetime { get; }

    /// <summary>损伤数组副本。</summary>
    public byte[] Damage { get; }

    /// <summary>当前相位 dirty 矩形。</summary>
    public DirtyRect CurrentDirty { get; }

    /// <summary>工作相位 dirty 矩形。</summary>
    public DirtyRect WorkingDirty { get; }

    /// <summary>chunk 生命周期状态（活跃/休眠等）。</summary>
    public ChunkState State { get; }

    /// <summary>
    /// 从活 chunk 复制全部格子缓冲与 dirty 元数据。
    /// </summary>
    public static ChunkSnapshot Create(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return new ChunkSnapshot(
            chunk.Coord,
            [.. chunk.Material],
            [.. chunk.Flags],
            [.. chunk.Lifetime],
            [.. chunk.Damage],
            chunk.CurrentDirty,
            chunk.WorkingDirty,
            chunk.State);
    }
}
