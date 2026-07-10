using BenchmarkDotNet.Attributes;
using PixelEngine.Rendering;
using PixelEngine.Simulation;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 1280x720 静态世界 render-buffer 全量重建与稳定帧复用对照。
/// </summary>
[MemoryDiagnoser]
public class RenderBufferViewportBenchmarks
{
    private const int Width = 1280;
    private const int Height = 720;
    private const ushort Stone = 1;

    private readonly ViewportChunkSource _chunks;
    private readonly MaterialTable _materials;
    private readonly TemperatureField _temperature = new();
    private readonly RenderBuffer _stableTarget = new(Width, Height);
    private readonly RenderAuxBuffers _stableAux = new(Width, Height);
    private readonly RenderBuffer _forcedTarget = new(Width, Height);
    private readonly RenderAuxBuffers _forcedAux = new(Width, Height);
    private readonly RenderBufferBuilder _stableBuilder = new();
    private readonly RenderBufferBuilder _forcedBuilder = new();
    private readonly RenderFrameContext _stableContext;
    private readonly RenderFrameContext _forcedContext;

    /// <summary>
    /// 创建覆盖 20x12 个 64x64 chunk 的 720p 静态材质场景。
    /// </summary>
    public RenderBufferViewportBenchmarks()
    {
        _chunks = new ViewportChunkSource(20, 12);
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
                Id = Stone,
                Name = "stone",
                Type = CellType.Solid,
                BaseColorBGRA = 0xFF304050u,
                TextureId = -1,
                HeatCapacity = 1f,
                Integrity = 100,
                RenderStyle = MaterialRenderStyle.Destructible,
                EdgeColorBGRA = 0xFF101820u,
            },
        ]);
        _stableContext = new RenderFrameContext(
            _chunks,
            _materials,
            _temperature,
            CameraState.OneToOne(0, 0, Width, Height),
            simStepped: true);
        _forcedContext = new RenderFrameContext(
            _chunks,
            _materials,
            _temperature,
            CameraState.OneToOne(0, 0, Width, Height),
            simStepped: true,
            forceRebuild: true);

        _stableBuilder.Build(_stableContext, _stableTarget, _stableAux);
    }

    /// <summary>
    /// 强制 1280x720 全量重建，作为未命中稳定帧复用时的基线。
    /// </summary>
    [Benchmark(Baseline = true)]
    public void BuildStaticInvalidatedFrame()
    {
        _forcedBuilder.Build(_forcedContext, _forcedTarget, _forcedAux);
    }

    /// <summary>
    /// 无 dirty rect 的稳定帧复用路径。
    /// </summary>
    [Benchmark]
    public void ReuseStaticFrame()
    {
        _stableBuilder.Build(_stableContext, _stableTarget, _stableAux);
    }

    private sealed class ViewportChunkSource : IChunkSource
    {
        private readonly Dictionary<ChunkCoord, Chunk> _byCoord = [];
        private readonly Chunk[] _resident;

        public ViewportChunkSource(int width, int height)
        {
            _resident = new Chunk[checked(width * height)];
            int write = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Chunk chunk = new(new ChunkCoord(x, y));
                    chunk.MaterialBuffer.AsSpan().Fill(Stone);
                    _resident[write++] = chunk;
                    _byCoord.Add(chunk.Coord, chunk);
                }
            }
        }

        public ReadOnlySpan<Chunk> ResidentChunks => _resident;

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            return _byCoord.TryGetValue(coord, out chunk!);
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            neighborhood = default;
            return false;
        }
    }
}
