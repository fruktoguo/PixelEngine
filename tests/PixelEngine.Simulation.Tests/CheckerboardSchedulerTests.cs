using PixelEngine.Core.Threading;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Simulation 节点 6 的 checkerboard 调度测试。
/// </summary>
public sealed class CheckerboardSchedulerTests
{
    private const ushort Sand = 2;

    /// <summary>
    /// 验证低活跃 chunk 数时 StepCa(JobSystem) 不触碰 JobSystem 派发路径，直接单线程回退。
    /// </summary>
    [Fact]
    public void StepCaWithJobSystemFallsBackToSingleThreadForFewAwakeChunks()
    {
        TestChunkSource source = CreateDenseSource(-1, -1, 1, 1);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        Set(center, 10, 10, Sand);
        center.SetCurrentDirty(DirtyRect.Full);
        SimulationKernel kernel = new(source, CreateMaterials());
        using JobSystem jobs = new(workerCount: 2);
        jobs.Dispose();

        kernel.StepCa(jobs);

        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Sand, Get(center, 10, 11));
    }

    /// <summary>
    /// 验证 4-pass checkerboard 会按 bucket 处理多个 awake chunk，并经 JobSystem 路径完成更新。
    /// </summary>
    [Fact]
    public void StepCaWithJobSystemProcessesAllCheckerboardBuckets()
    {
        TestChunkSource source = CreateDenseSource(-1, -1, 4, 2);
        ChunkCoord[] activeCoords =
        [
            new(0, 0),
            new(1, 0),
            new(2, 0),
            new(3, 0),
            new(0, 1),
            new(1, 1),
            new(2, 1),
            new(3, 1),
        ];

        foreach (ChunkCoord coord in activeCoords)
        {
            Chunk chunk = source.GetRequired(coord);
            Set(chunk, 10, 10, Sand);
            chunk.SetCurrentDirty(DirtyRect.Full);
        }

        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa(jobs);

        foreach (ChunkCoord coord in activeCoords)
        {
            Chunk chunk = source.GetRequired(coord);
            Assert.Equal(0, Get(chunk, 10, 10));
            Assert.Equal(Sand, Get(chunk, 10, 11));
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

    private static TestChunkSource CreateDenseSource(int minX, int minY, int maxX, int maxY)
    {
        List<Chunk> chunks = [];
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                chunks.Add(new Chunk(new ChunkCoord(x, y)));
            }
        }

        return new TestChunkSource([.. chunks]);
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)];
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
