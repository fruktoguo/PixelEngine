using BenchmarkDotNet.Attributes;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 自由粒子积分与沉积的基准。
/// </summary>
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3)]
public class ParticleHandshakeBenchmarks
{
    private const ushort Sand = 2;
    private readonly Chunk[] _chunks = new Chunk[9];
    private readonly TestChunkSource _source;
    private readonly MaterialPropsTable _materials;
    private readonly CellGrid _grid;
    private readonly SimulationKernel _kernel;
    private readonly ParticleSystem _particles;

    /// <summary>
    /// 创建粒子 handshake benchmark fixture。
    /// </summary>
    public ParticleHandshakeBenchmarks()
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
        _materials = new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder],
            [0, 255, 120],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0]);
        _grid = new CellGrid(_source, _materials);
        _kernel = new SimulationKernel(_source, _materials);
        _particles = new ParticleSystem(capacity: 4096);
    }

    /// <summary>
    /// 活跃粒子数量。
    /// </summary>
    [Params(1024)]
    public int Count { get; set; }

    /// <summary>
    /// 填充飞行粒子初态。
    /// </summary>
    [IterationSetup(Target = nameof(IntegrateFlyingParticles))]
    public void SetupFlying()
    {
        ResetWorldAndParticles();
        for (int i = 0; i < Count; i++)
        {
            _ = _particles.TrySpawn(new ParticleSpawn(1 + (i & 31), 1 + ((i >> 5) & 31), 0.25f, 0.1f, Sand, (byte)i, 120));
        }
    }

    /// <summary>
    /// 填充会立即沉积的静止粒子初态。
    /// </summary>
    [IterationSetup(Target = nameof(IntegrateAndResolveDeposits))]
    public void SetupDeposits()
    {
        ResetWorldAndParticles();
        for (int i = 0; i < Count; i++)
        {
            _ = _particles.TrySpawn(new ParticleSpawn(1 + (i & 31), 1 + ((i >> 5) & 31), 0, 0, Sand, (byte)i, 120));
        }
    }

    /// <summary>
    /// 单线程弹道积分基准。
    /// </summary>
    [Benchmark]
    public void IntegrateFlyingParticles()
    {
        _particles.IntegrateAndAdvance(_grid);
    }

    /// <summary>
    /// 单线程积分 + 沉积基准。
    /// </summary>
    [Benchmark]
    public void IntegrateAndResolveDeposits()
    {
        _particles.IntegrateAndAdvance(_grid);
        _particles.ResolveDeposits(_kernel, _grid);
    }

    private void ResetWorldAndParticles()
    {
        for (int i = 0; i < _chunks.Length; i++)
        {
            Chunk chunk = _chunks[i];
            chunk.Reset(chunk.Coord);
        }

        _particles.Clear();
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
