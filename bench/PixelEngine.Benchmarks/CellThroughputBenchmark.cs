using BenchmarkDotNet.Attributes;
using PixelEngine.Core;
using PixelEngine.Core.Threading;
using PixelEngine.Simulation;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 热路径：SimulationKernel 单帧 CA 更新；假设 8×8 活跃区块网格，对比满激活/全休眠/典型脏矩形三档规模。
/// </summary>
[MemoryDiagnoser]
public class CellThroughputBenchmark : IDisposable
{
    private const ushort Water = 1;
    private const ushort Oil = 2;
    private const ushort Sand = 3;
    private const ushort Rock = 4;
    private const int ActiveChunksPerAxis = 8;
    private const int ResidentChunksPerAxis = ActiveChunksPerAxis + 2;

    private readonly Chunk[] _chunks = new Chunk[ResidentChunksPerAxis * ResidentChunksPerAxis];
    private readonly TestChunkSource _source;
    private readonly MaterialPropsTable _materials;
    private readonly SimulationKernel _kernel;
    private readonly JobSystem _jobs = new(workerCount: Math.Min(4, Math.Max(1, Environment.ProcessorCount)));

    /// <summary>
    /// 创建 cell throughput benchmark fixture。
    /// </summary>
    public CellThroughputBenchmark()
    {
        int index = 0;
        for (int cy = -1; cy <= ActiveChunksPerAxis; cy++)
        {
            for (int cx = -1; cx <= ActiveChunksPerAxis; cx++)
            {
                _chunks[index++] = new Chunk(new ChunkCoord(cx, cy));
            }
        }

        _source = new TestChunkSource(_chunks);
        _materials = CreateMaterials();
        _kernel = new SimulationKernel(_source, _materials, worldSeed: 0xBEEFUL);
    }

    /// <summary>
    /// 活跃 profile。
    /// </summary>
    [Params(CellProfile.FullActiveLiquid, CellProfile.FullStaticSleeping, CellProfile.TypicalDirtyRect)]
    public CellProfile Profile { get; set; }

    /// <summary>
    /// 当前 profile 的活跃 cell 数，用于从耗时换算 cells/s。
    /// </summary>
    public int ActiveCells => Profile switch
    {
        CellProfile.FullActiveLiquid => ActiveChunksPerAxis * ActiveChunksPerAxis * EngineConstants.ChunkArea,
        CellProfile.FullStaticSleeping => 0,
        CellProfile.TypicalDirtyRect => 16 * 16,
        _ => throw new InvalidOperationException($"未知 cell throughput profile：{Profile}。"),
    };

    /// <summary>
    /// 每次迭代前重建可重复初态。
    /// </summary>
    [IterationSetup]
    public void SetupIteration()
    {
        ResetChunks();
        // Chunk.Reset 只重置网格与 dirty 元数据；内核的 frame/parity 也必须回到
        // 同一初态，否则相邻测量会以不同 parity 进入 CA，失去可比性。
        _kernel.RestoreFrameState(frameIndex: 0, currentParity: 0);
        if (Profile == CellProfile.FullActiveLiquid)
        {
            FillFullActiveLiquid();
        }
        else if (Profile == CellProfile.FullStaticSleeping)
        {
            FillFullStaticSleeping();
        }
        else
        {
            FillTypicalDirtyRect();
        }
    }

    /// <summary>
    /// 基准对照：单线程 StepCa（Baseline），用于对比 JobSystem 并行路径。
    /// </summary>
    [Benchmark(Baseline = true)]
    public void StepSingleThread()
    {
        _kernel.StepCa();
        _kernel.SwapDirtyRects();
    }

    /// <summary>
    /// 基准热路径：JobSystem 驱动的 SimulationKernel.StepCa 单帧步进。
    /// </summary>
    [Benchmark]
    public void StepJobSystem()
    {
        _kernel.StepCa(_jobs);
        _kernel.SwapDirtyRects();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _jobs.Dispose();
    }

    private void FillFullActiveLiquid()
    {
        for (int cy = 0; cy < ActiveChunksPerAxis; cy++)
        {
            for (int cx = 0; cx < ActiveChunksPerAxis; cx++)
            {
                Chunk chunk = _source.GetRequired(new ChunkCoord(cx, cy));
                for (int i = 0; i < chunk.Material.Length; i++)
                {
                    chunk.Material[i] = (i & 1) == 0 ? Water : Oil;
                }

                chunk.SetCurrentDirty(DirtyRect.Full);
            }
        }
    }

    private void FillTypicalDirtyRect()
    {
        Chunk chunk = _source.GetRequired(new ChunkCoord(ActiveChunksPerAxis / 2, ActiveChunksPerAxis / 2));
        for (int y = 24; y < 40; y++)
        {
            for (int x = 24; x < 40; x++)
            {
                chunk.Material[CellAddressing.LocalIndexFromLocal(x, y)] = ((x + y) & 1) == 0 ? Sand : Water;
            }
        }

        chunk.SetCurrentDirty(new DirtyRect(24, 24, 39, 39));
    }

    private void FillFullStaticSleeping()
    {
        for (int cy = 0; cy < ActiveChunksPerAxis; cy++)
        {
            for (int cx = 0; cx < ActiveChunksPerAxis; cx++)
            {
                Chunk chunk = _source.GetRequired(new ChunkCoord(cx, cy));
                Array.Fill(chunk.Material, Rock);
            }
        }
    }

    private void ResetChunks()
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
            [CellType.Empty, CellType.Liquid, CellType.Liquid, CellType.Powder, CellType.Solid],
            [0, 120, 80, 180, 255],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0]);
    }

    /// <summary>
    /// cell throughput profile。
    /// </summary>
    public enum CellProfile
    {
        /// <summary>64 个活跃 chunk，液体交错。</summary>
        FullActiveLiquid,

        /// <summary>64 个常驻非空 chunk，但 dirty 为空并保持 sleeping。</summary>
        FullStaticSleeping,

        /// <summary>单 chunk 16x16 dirty rect。</summary>
        TypicalDirtyRect,
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
