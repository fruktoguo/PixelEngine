using BenchmarkDotNet.Attributes;
using PixelEngine.Simulation;

namespace PixelEngine.Benchmarks;

/// <summary>
/// Simulation 内核稳态零分配基准。
/// </summary>
[MemoryDiagnoser]
public class SimulationAllocationBenchmarks
{
    private const ushort Sand = 2;
    private readonly Chunk[] _chunks = new Chunk[9];
    private readonly TestChunkSource _source;
    private readonly SimulationKernel _kernel;

    /// <summary>
    /// 创建 Simulation allocation benchmark fixture。
    /// </summary>
    public SimulationAllocationBenchmarks()
    {
        int index = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                _chunks[index++] = new Chunk(new ChunkCoord(dx, dy));
            }
        }

        _source = new TestChunkSource(_chunks);
        _kernel = new SimulationKernel(_source, CreateMaterials());
    }

    /// <summary>
    /// 单粒 powder 的 StepCa + dirty swap 稳态分配基准。
    /// </summary>
    [Benchmark]
    public void StepCaAndSwapSinglePowder()
    {
        ResetWorld();
        Chunk center = _source.GetRequired(new ChunkCoord(0, 0));
        center.Material[CellAddressing.LocalIndexFromLocal(10, 10)] = Sand;
        center.SetCurrentDirty(new DirtyRect(10, 10, 10, 10));

        _kernel.StepCa();
        _kernel.SwapDirtyRects();
    }

    private void ResetWorld()
    {
        for (int i = 0; i < _chunks.Length; i++)
        {
            Chunk chunk = _chunks[i];
            chunk.Reset(chunk.Coord);
        }
    }

    private static MaterialPropsTable CreateMaterials()
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder],
            [0, 255, 120],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0]);
    }

    private sealed class TestChunkSource : IChunkSource
    {
        private readonly Dictionary<ChunkCoord, Chunk> _byCoord;
        private readonly Chunk[] _resident;

        public TestChunkSource(params Chunk[] chunks)
        {
            _resident = chunks;
            _byCoord = new Dictionary<ChunkCoord, Chunk>(chunks.Length);
            foreach (Chunk chunk in chunks)
            {
                _byCoord.Add(chunk.Coord, chunk);
            }
        }

        public ReadOnlySpan<Chunk> ResidentChunks => _resident;

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
}
