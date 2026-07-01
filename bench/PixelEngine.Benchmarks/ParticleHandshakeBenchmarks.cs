using BenchmarkDotNet.Attributes;
using PixelEngine.Core;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 自由粒子积分与沉积的基准。
/// </summary>
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3)]
public class ParticleIntegrationBenchmark
{
    private const ushort Sand = 2;
    private const int SpawnChunksPerAxis = 8;
    private const int ResidentChunksPerAxis = SpawnChunksPerAxis + 2;
    private const int SpawnStride = (SpawnChunksPerAxis * EngineConstants.ChunkSize) - 2;
    private readonly Chunk[] _chunks = new Chunk[ResidentChunksPerAxis * ResidentChunksPerAxis];
    private readonly TestChunkSource _source;
    private readonly MaterialPropsTable _materials;
    private readonly CellGrid _grid;
    private readonly SimulationKernel _kernel;
    private readonly ParticleSystem _particles;

    /// <summary>
    /// 创建粒子 handshake benchmark fixture。
    /// </summary>
    public ParticleIntegrationBenchmark()
    {
        int index = 0;
        for (int cy = 0; cy < ResidentChunksPerAxis; cy++)
        {
            for (int cx = 0; cx < ResidentChunksPerAxis; cx++)
            {
                _chunks[index++] = new Chunk(new ChunkCoord(cx - 1, cy - 1));
            }
        }

        _source = new TestChunkSource(_chunks);
        _materials = new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder],
            [0, 255, 120],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 120]);
        _grid = new CellGrid(_source, _materials);
        _kernel = new SimulationKernel(_source, _materials);
        _particles = new ParticleSystem();
    }

    /// <summary>
    /// 活跃粒子数量。
    /// </summary>
    [Params(50_000, 100_000, 200_000)]
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
            _ = _particles.TrySpawn(new ParticleSpawn(SpawnX(i), SpawnY(i), 0.25f, 0.1f, Sand, (byte)i, 120));
        }
    }

    /// <summary>
    /// 填充混合初态：大多数粒子飞行，少数粒子本 tick 落定沉积。
    /// </summary>
    [IterationSetup(Target = nameof(IntegrateAndResolveDeposits))]
    public void SetupDeposits()
    {
        ResetWorldAndParticles();
        for (int i = 0; i < Count; i++)
        {
            bool shouldDeposit = (i & 15) == 0;
            float vx = shouldDeposit ? 0 : 0.25f;
            float vy = shouldDeposit ? 0 : 0.1f;
            _ = _particles.TrySpawn(new ParticleSpawn(SpawnX(i), SpawnY(i), vx, vy, Sand, (byte)i, 120));
        }
    }

    /// <summary>
    /// 填充只读活跃前缀迭代初态。
    /// </summary>
    [IterationSetup(Target = nameof(ReadActivePrefix))]
    public void SetupReadback()
    {
        SetupFlying();
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

    /// <summary>
    /// 只读遍历活跃前缀，验证 ReadOnlySpan 迭代形态。
    /// </summary>
    [Benchmark]
    public float ReadActivePrefix()
    {
        float sum = 0;
        ReadOnlySpan<Particle> particles = _particles.ActiveReadOnly;
        ref readonly Particle first = ref MemoryMarshal.GetReference(particles);
        for (int i = 0; i < particles.Length; i++)
        {
            sum += Unsafe.Add(ref Unsafe.AsRef(in first), i).X;
        }

        return sum;
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

    private static int SpawnX(int index)
    {
        return 1 + (index % SpawnStride);
    }

    private static int SpawnY(int index)
    {
        return 1 + (index / SpawnStride);
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

/// <summary>
/// 旧入口保留给已有 benchmark filter/脚本；实际实现见 <see cref="ParticleIntegrationBenchmark"/>。
/// </summary>
public class ParticleHandshakeBenchmarks : ParticleIntegrationBenchmark
{
}
