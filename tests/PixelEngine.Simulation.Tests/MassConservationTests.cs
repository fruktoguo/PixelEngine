using PixelEngine.Core;
using PixelEngine.Core.Threading;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// 无反应 CA movement 的材质质量守恒性质测试。
/// 不变式：无反应 movement 不创造/销毁材质计数。
/// </summary>
public sealed class MassConservationTests
{
    private const ushort Solid = 1;
    private const ushort Sand = 2;
    private const ushort Water = 3;
    private const ushort Gas = 4;
    private const int MaterialCount = 5;

    /// <summary>
    /// 验证单线程路径在跨 chunk 边界与四角交汇场景中逐帧保持材质计数不变。
    /// </summary>
    [Fact]
    public void SingleThreadStepCaPreservesMaterialCountsAcrossChunkBoundaries()
    {
        TestChunkSource source = CreateBoundaryStressSource();
        SimulationKernel kernel = new(source, CreateMaterials(), worldSeed: 123)
        {
            ForceSingleThread = true,
        };

        AssertConservedForTicks(source, kernel, jobs: null);
    }

    /// <summary>
    /// 验证 4-pass JobSystem 路径在跨 chunk 边界与四角交汇场景中逐帧保持材质计数不变。
    /// </summary>
    [Fact]
    public void JobSystemStepCaPreservesMaterialCountsAcrossChunkBoundaries()
    {
        TestChunkSource source = CreateBoundaryStressSource();
        SimulationKernel kernel = new(source, CreateMaterials(), worldSeed: 123);
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };

        AssertConservedForTicks(source, kernel, jobs);
    }

    private static void AssertConservedForTicks(TestChunkSource source, SimulationKernel kernel, JobSystem? jobs)
    {
        int[] expected = CountMaterials(source);
        for (int tick = 0; tick < 6; tick++)
        {
            if (jobs is null)
            {
                kernel.StepCa();
            }
            else
            {
                kernel.StepCa(jobs);
            }

            Assert.Equal(expected, CountMaterials(source));
            kernel.SwapDirtyRects();
        }
    }

    private static TestChunkSource CreateBoundaryStressSource()
    {
        TestChunkSource source = CreateDenseSource(-1, -1, 2, 2);
        Chunk northwest = source.GetRequired(new ChunkCoord(0, 0));
        Chunk northeast = source.GetRequired(new ChunkCoord(1, 0));
        Chunk southwest = source.GetRequired(new ChunkCoord(0, 1));
        Chunk southeast = source.GetRequired(new ChunkCoord(1, 1));

        Set(northwest, 62, 62, Sand);
        Set(northwest, 63, 62, Water);
        Set(northwest, 62, 63, Gas);
        Set(northwest, 63, 63, Sand);

        Set(northeast, 0, 62, Sand);
        Set(northeast, 1, 63, Water);
        Set(northeast, 0, 63, Gas);

        Set(southwest, 62, 0, Water);
        Set(southwest, 63, 0, Sand);
        Set(southwest, 63, 1, Gas);

        Set(southeast, 0, 0, Sand);
        Set(southeast, 1, 0, Water);
        Set(southeast, 0, 1, Gas);

        for (int x = 0; x < EngineConstants.ChunkSize; x++)
        {
            Set(southwest, x, 20, Solid);
            Set(southeast, x, 20, Solid);
        }

        northwest.SetCurrentDirty(DirtyRect.Full);
        northeast.SetCurrentDirty(DirtyRect.Full);
        southwest.SetCurrentDirty(DirtyRect.Full);
        southeast.SetCurrentDirty(DirtyRect.Full);
        return source;
    }

    private static int[] CountMaterials(TestChunkSource source)
    {
        int[] counts = new int[MaterialCount];
        foreach (Chunk chunk in source.ResidentChunks)
        {
            foreach (ushort material in chunk.MaterialBuffer)
            {
                if (material < counts.Length)
                {
                    counts[material]++;
                }
            }
        }

        return counts;
    }

    private static MaterialPropsTable CreateMaterials()
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder, CellType.Liquid, CellType.Gas],
            [0, 255, 120, 60, 1],
            [0, 0, 0, 3, 2],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0]);
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
        chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
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
