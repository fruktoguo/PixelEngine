namespace PixelEngine.Simulation;

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

    public ChunkCoord Coord { get; }

    public ushort[] Material { get; }

    public byte[] Flags { get; }

    public byte[] Lifetime { get; }

    public byte[] Damage { get; }

    public DirtyRect CurrentDirty { get; }

    public DirtyRect WorkingDirty { get; }

    public ChunkState State { get; }

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
