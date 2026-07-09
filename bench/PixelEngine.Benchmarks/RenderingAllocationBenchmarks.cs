using BenchmarkDotNet.Attributes;
using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 热路径：RenderBufferBuilder.Build 的托管分配。
/// </summary>
[MemoryDiagnoser]
public class RenderingAllocationBenchmarks
{
    private const ushort Sand = 1;
    private readonly Chunk _chunk = new(new ChunkCoord(0, 0));
    private readonly TestChunkSource _chunks;
    private readonly MaterialTable _materials;
    private readonly TemperatureField _temperature = new();
    private readonly RenderBuffer _target = new(64, 64);
    private readonly RenderAuxBuffers _aux = new(64, 64);
    private readonly RenderBufferBuilder _builder = new();
    private readonly ParticleCompositor _particles = new();
    private readonly Particle[] _particleBuffer = new Particle[128];
    private readonly RenderFrameContext _context;

    /// <summary>
    /// 创建 rendering allocation benchmark fixture。
    /// </summary>
    public RenderingAllocationBenchmarks()
    {
        _chunks = new TestChunkSource(_chunk);
        _materials = new MaterialTable(
        [
            new MaterialDef
            {
                Id = 0,
                Name = "empty",
                Type = CellType.Empty,
                BaseColorBGRA = 0,
                TextureId = -1,
                HeatCapacity = 1f,
            },
            new MaterialDef
            {
                Id = Sand,
                Name = "sand",
                Type = CellType.Powder,
                BaseColorBGRA = 0xFF204060u,
                TextureId = -1,
                HeatCapacity = 1f,
            },
        ]);
        _context = new RenderFrameContext(
            _chunks,
            _materials,
            _temperature,
            CameraState.OneToOne(0, 0, 64, 64),
            simStepped: true);

        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                _chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(x, y)] = Sand;
            }
        }

        for (int i = 0; i < _particleBuffer.Length; i++)
        {
            _particleBuffer[i] = new Particle
            {
                X = i & 63,
                Y = i >> 1,
                Material = Sand,
                ColorVariant = (byte)i,
            };
        }
    }

    /// <summary>
    /// 验证Build Render Buffer行为符合预期。
    /// </summary>
    [Benchmark]
    public void BuildRenderBuffer()
    {
        _builder.Build(_context, _target, _aux);
    }

    /// <summary>
    /// 验证Stamp Particles。
    /// </summary>
    [Benchmark]
    public void StampParticles()
    {
        _particles.Stamp(_particleBuffer, _materials, CameraState.OneToOne(0, 0, 64, 64), _target, _aux);
    }

    private sealed class TestChunkSource(Chunk chunk) : IChunkSource
    {
        private readonly Chunk[] _resident = [chunk];

        public ReadOnlySpan<Chunk> ResidentChunks => _resident;

        public bool TryGetChunk(ChunkCoord coord, out Chunk result)
        {
            if (coord == chunk.Coord)
            {
                result = chunk;
                return true;
            }

            result = null!;
            return false;
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            neighborhood = default;
            return false;
        }
    }
}
