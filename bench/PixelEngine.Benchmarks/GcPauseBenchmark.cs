using BenchmarkDotNet.Attributes;
using PixelEngine.Core.Memory;
using PixelEngine.Simulation;
using System.Runtime;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 稳态帧循环零分配与 GC 模式观测基准。
/// </summary>
[MemoryDiagnoser]
public class GcPauseBenchmark
{
    private const ushort Sand = 2;
    private readonly Chunk[] _chunks = new Chunk[9];
    private readonly TestChunkSource _source;
    private readonly SimulationKernel _kernel;
    private readonly Pool<object> _pool = new(static () => new object(), preallocate: 4);
    private GCLatencyMode _originalLatencyMode;

    /// <summary>
    /// 创建 GC pause benchmark fixture。
    /// </summary>
    public GcPauseBenchmark()
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
        PrepareSingleDirtyCell();
    }

    /// <summary>
    /// 当前运行时是否启用 Server GC；用于报告中区分 Workstation / Server GC。
    /// </summary>
    public bool IsServerGc => System.Runtime.GCSettings.IsServerGC;

    /// <summary>
    /// 当前 GC 延迟模式；Workstation/Server 对比时保持同一低延迟配置。
    /// </summary>
    public GCLatencyMode LatencyMode => System.Runtime.GCSettings.LatencyMode;

    /// <summary>
    /// 基准进程内强制使用 SustainedLowLatency，与 Hosting 默认运行态一致。
    /// </summary>
    [GlobalSetup]
    public void SetLatencyMode()
    {
        _originalLatencyMode = System.Runtime.GCSettings.LatencyMode;
        System.Runtime.GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
    }

    /// <summary>
    /// 恢复基准进程原始 GC latency mode。
    /// </summary>
    [GlobalCleanup]
    public void RestoreLatencyMode()
    {
        System.Runtime.GCSettings.LatencyMode = _originalLatencyMode;
    }

    /// <summary>
    /// 稳态 sim tick：MemoryDiagnoser 应报告 0 B/op。
    /// </summary>
    [Benchmark]
    public void SteadySimulationTick()
    {
        _kernel.StepCa();
        _kernel.SwapDirtyRects();
        PrepareSingleDirtyCell();
    }

    /// <summary>
    /// 稳态 pool 租还：验证基础设施无分配。
    /// </summary>
    [Benchmark]
    public void SteadyPoolRentReturn()
    {
        object item = _pool.Rent();
        _pool.Return(item);
    }

    private void PrepareSingleDirtyCell()
    {
        for (int i = 0; i < _chunks.Length; i++)
        {
            Chunk chunk = _chunks[i];
            chunk.Reset(chunk.Coord);
        }

        Chunk center = _source.GetRequired(new ChunkCoord(0, 0));
        center.Material[CellAddressing.LocalIndexFromLocal(10, 10)] = Sand;
        center.SetCurrentDirty(new DirtyRect(10, 10, 10, 10));
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

    private sealed class TestChunkSource(params Chunk[] chunks) : IChunkSource
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
}
