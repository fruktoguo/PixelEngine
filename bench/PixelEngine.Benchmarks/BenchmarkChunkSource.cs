using PixelEngine.Simulation;

namespace PixelEngine.Benchmarks;

/// <summary>
/// CA benchmark 共用的确定性驻留 chunk source。
/// </summary>
internal sealed class BenchmarkChunkSource(params Chunk[] chunks) : IChunkSource
{
    private readonly Dictionary<ChunkCoord, Chunk> _byCoord = chunks.ToDictionary(static chunk => chunk.Coord);

    public ReadOnlySpan<Chunk> ResidentChunks => chunks;

    public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
    {
        return _byCoord.TryGetValue(coord, out chunk!);
    }

    public Chunk GetRequired(ChunkCoord coord)
    {
        return _byCoord[coord];
    }

    public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
    {
        if (!TryGetChunk(new ChunkCoord(center.X - 1, center.Y - 1), out Chunk slot0) ||
            !TryGetChunk(new ChunkCoord(center.X, center.Y - 1), out Chunk slot1) ||
            !TryGetChunk(new ChunkCoord(center.X + 1, center.Y - 1), out Chunk slot2) ||
            !TryGetChunk(new ChunkCoord(center.X - 1, center.Y), out Chunk slot3) ||
            !TryGetChunk(center, out Chunk slot4) ||
            !TryGetChunk(new ChunkCoord(center.X + 1, center.Y), out Chunk slot5) ||
            !TryGetChunk(new ChunkCoord(center.X - 1, center.Y + 1), out Chunk slot6) ||
            !TryGetChunk(new ChunkCoord(center.X, center.Y + 1), out Chunk slot7) ||
            !TryGetChunk(new ChunkCoord(center.X + 1, center.Y + 1), out Chunk slot8))
        {
            neighborhood = default;
            return false;
        }

        neighborhood = new ChunkNeighborhood(slot0, slot1, slot2, slot3, slot4, slot5, slot6, slot7, slot8);
        return true;
    }
}
