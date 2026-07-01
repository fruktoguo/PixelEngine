using PixelEngine.Core;
using PixelEngine.Core.Threading;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Simulation 多线程 checkerboard 与单线程 oracle 的统计性质测试。
/// </summary>
public sealed class MultithreadOracleTests
{
    private const ushort Solid = 1;
    private const ushort Sand = 2;
    private const ushort Water = 3;
    private const ushort Gas = 4;
    private const int MaterialCount = 5;

    /// <summary>
    /// 验证 4-pass JobSystem 路径与单线程 oracle 的材质守恒量和边界带统计一致。
    /// </summary>
    [Fact]
    public void StepCaWithJobSystemMatchesSingleThreadOracleStatistics()
    {
        TestChunkSource singleSource = CreateSeededSource();
        TestChunkSource multiSource = CreateSeededSource();
        SimulationKernel single = new(singleSource, CreateMaterials(), worldSeed: 42)
        {
            ForceSingleThread = true,
        };
        SimulationKernel multi = new(multiSource, CreateMaterials(), worldSeed: 42);
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };

        for (int i = 0; i < 3; i++)
        {
            single.StepCa();
            single.SwapDirtyRects();
            multi.StepCa(jobs);
            multi.SwapDirtyRects();
        }

        Assert.Equal(CountMaterials(singleSource), CountMaterials(multiSource));
        Assert.Equal(CountBoundaryBandMaterials(singleSource), CountBoundaryBandMaterials(multiSource));
    }

    /// <summary>
    /// 验证多线程路径相对单线程 oracle 的宏观堆高、液面和气体分布在统计容差内一致。
    /// </summary>
    [Fact]
    public void StepCaWithJobSystemMatchesSingleThreadOracleMacroProfiles()
    {
        TestChunkSource singleSource = CreateSeededSource();
        TestChunkSource multiSource = CreateSeededSource();
        SimulationKernel single = new(singleSource, CreateMaterials(), worldSeed: 77)
        {
            ForceSingleThread = true,
        };
        SimulationKernel multi = new(multiSource, CreateMaterials(), worldSeed: 77);
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };

        for (int i = 0; i < 4; i++)
        {
            single.StepCa();
            single.SwapDirtyRects();
            multi.StepCa(jobs);
            multi.SwapDirtyRects();
        }

        Assert.Equal(CountMaterials(singleSource), CountMaterials(multiSource));
        AssertProfileWithinTolerance(HeightProfile(singleSource, Sand), HeightProfile(multiSource, Sand), maxTotalDelta: 0, maxSingleDelta: 0);
        AssertProfileWithinTolerance(HeightProfile(singleSource, Water), HeightProfile(multiSource, Water), maxTotalDelta: 0, maxSingleDelta: 0);
        Assert.Equal(QuadrantCounts(singleSource, Gas), QuadrantCounts(multiSource, Gas));
    }

    private static TestChunkSource CreateSeededSource()
    {
        TestChunkSource source = CreateDenseSource(-1, -1, 4, 3);
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
            Set(chunk, 8, 10, Sand);
            Set(chunk, 9, 11, Solid);
            Set(chunk, 10, 11, Solid);
            Set(chunk, 11, 11, Solid);
            Set(chunk, 20, 12, Water);
            Set(chunk, 30, 30, Gas);
            chunk.SetCurrentDirty(DirtyRect.Full);
        }

        Chunk boundary = source.GetRequired(new ChunkCoord(1, 0));
        Set(boundary, 12, 63, Sand);
        return source;
    }

    private static int[] CountMaterials(TestChunkSource source)
    {
        int[] counts = new int[MaterialCount];
        foreach (Chunk chunk in source.ResidentChunks)
        {
            foreach (ushort material in chunk.Material)
            {
                if (material < counts.Length)
                {
                    counts[material]++;
                }
            }
        }

        return counts;
    }

    private static int[] CountBoundaryBandMaterials(TestChunkSource source)
    {
        int[] counts = new int[MaterialCount];
        foreach (Chunk chunk in source.ResidentChunks)
        {
            for (int y = 0; y < EngineConstants.ChunkSize; y++)
            {
                CountAt(chunk, 0, y, counts);
                CountAt(chunk, EngineConstants.ChunkSize - 1, y, counts);
            }

            for (int x = 1; x < EngineConstants.ChunkSize - 1; x++)
            {
                CountAt(chunk, x, 0, counts);
                CountAt(chunk, x, EngineConstants.ChunkSize - 1, counts);
            }
        }

        return counts;
    }

    private static int[] HeightProfile(TestChunkSource source, ushort material)
    {
        int minX = int.MaxValue;
        int maxX = int.MinValue;
        foreach (Chunk chunk in source.ResidentChunks)
        {
            int baseX = chunk.Coord.X << EngineConstants.ChunkSizeLog2;
            minX = Math.Min(minX, baseX);
            maxX = Math.Max(maxX, baseX + EngineConstants.ChunkSize - 1);
        }

        int[] heights = new int[maxX - minX + 1];
        Array.Fill(heights, -1);
        foreach (Chunk chunk in source.ResidentChunks)
        {
            int baseX = chunk.Coord.X << EngineConstants.ChunkSizeLog2;
            int baseY = chunk.Coord.Y << EngineConstants.ChunkSizeLog2;
            for (int ly = 0; ly < EngineConstants.ChunkSize; ly++)
            {
                for (int lx = 0; lx < EngineConstants.ChunkSize; lx++)
                {
                    if (chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] == material)
                    {
                        int profileIndex = baseX + lx - minX;
                        heights[profileIndex] = Math.Max(heights[profileIndex], baseY + ly);
                    }
                }
            }
        }

        return heights;
    }

    private static int[] QuadrantCounts(TestChunkSource source, ushort material)
    {
        int[] counts = new int[4];
        foreach (Chunk chunk in source.ResidentChunks)
        {
            for (int ly = 0; ly < EngineConstants.ChunkSize; ly++)
            {
                for (int lx = 0; lx < EngineConstants.ChunkSize; lx++)
                {
                    if (chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] != material)
                    {
                        continue;
                    }

                    int index = (lx >= EngineConstants.ChunkSize / 2 ? 1 : 0) + (ly >= EngineConstants.ChunkSize / 2 ? 2 : 0);
                    counts[index]++;
                }
            }
        }

        return counts;
    }

    private static void AssertProfileWithinTolerance(int[] expected, int[] actual, int maxTotalDelta, int maxSingleDelta)
    {
        Assert.Equal(expected.Length, actual.Length);
        int totalDelta = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            int delta = Math.Abs(expected[i] - actual[i]);
            Assert.True(delta <= maxSingleDelta, $"profile[{i}] delta {delta} > {maxSingleDelta}");
            totalDelta += delta;
        }

        Assert.True(totalDelta <= maxTotalDelta, $"profile total delta {totalDelta} > {maxTotalDelta}");
    }

    private static void CountAt(Chunk chunk, int lx, int ly, int[] counts)
    {
        ushort material = chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)];
        if (material < counts.Length)
        {
            counts[material]++;
        }
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
        chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
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
