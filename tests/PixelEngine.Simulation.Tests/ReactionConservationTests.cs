using PixelEngine.Core.Threading;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// 双输出反应的输入 / 输出计数账本与中间概率守恒测试。
/// </summary>
public sealed class ReactionConservationTests
{
    private const ushort Empty = 0;
    private const ushort Lava = 1;
    private const ushort Water = 2;
    private const ushort Rock = 3;
    private const ushort Steam = 4;
    private const int MaterialCount = 5;

    /// <summary>
    /// 验证 datamined 的 lava+water->rock+steam @80 在 0&lt;p&lt;255 中间概率下按 byte 概率裁决，且总账本严格守恒。
    /// </summary>
    [Fact]
    public void IntermediateProbabilityCensusKeepsDataminedLavaWaterLedgerBalanced()
    {
        byte probability = ReactionExpansionRules.RateToProbabilityByte(80);
        ReactionEngine engine = CreateReactionSetup(probability).Engine;
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        NeighborWindow window = new(source, center.Coord);
        int[] ledger = new int[MaterialCount];

        for (int randomByte = 0; randomByte <= byte.MaxValue; randomByte++)
        {
            ResetPair(center, 10, 10, Lava, 11, 10, Water);

            _ = engine.TryReact(
                ref window,
                10,
                10,
                Lava,
                11,
                10,
                Water,
                CellFlags.Parity,
                checked((byte)randomByte));

            ledger[Get(center, 10, 10)]++;
            ledger[Get(center, 11, 10)]++;
        }

        int expectedSuccesses = probability;
        int expectedMisses = 256 - probability;
        Assert.Equal(expectedMisses, ledger[Lava]);
        Assert.Equal(expectedMisses, ledger[Water]);
        Assert.Equal(expectedSuccesses, ledger[Rock]);
        Assert.Equal(expectedSuccesses, ledger[Steam]);
        Assert.Equal(512, ledger[Lava] + ledger[Water] + ledger[Rock] + ledger[Steam]);
    }

    /// <summary>
    /// 验证 lava+water->rock+steam 的计数账本在同 chunk、水平 / 垂直边界和 2x2 交汇处都不翻倍、不丢失。
    /// </summary>
    [Fact]
    public void DataminedLavaWaterLedgerConservesCountsAcrossBoundaryMatrix()
    {
        TestChunkSource source = CreateDenseSource(-2, -2, 3, 3);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        Chunk east = source.GetRequired(new ChunkCoord(1, 0));
        Chunk south = source.GetRequired(new ChunkCoord(0, 1));
        Chunk southeast = source.GetRequired(new ChunkCoord(1, 1));
        PlaceDataminedBoundaryMatrix(center, east, south, southeast);
        MarkNonEmptyChunksDirty(source);
        ReactionSetup setup = CreateReactionSetup(byte.MaxValue);
        SimulationKernel kernel = new(source, new MaterialPropsTable(setup.Materials.Hot), worldSeed: 0xBEEFUL, reactionExecutor: setup.Engine);

        int[] before = CountMaterials(source);
        Assert.Equal(5, before[Lava]);
        Assert.Equal(5, before[Water]);
        Assert.Equal(0, before[Rock]);
        Assert.Equal(0, before[Steam]);
        Assert.Equal(10, before[Lava] + before[Water] + before[Rock] + before[Steam]);

        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        kernel.StepCa(jobs);

        int[] after = CountMaterials(source);
        Assert.Equal(0, after[Lava]);
        Assert.Equal(0, after[Water]);
        Assert.Equal(5, after[Rock]);
        Assert.Equal(5, after[Steam]);
        Assert.Equal(10, after[Lava] + after[Water] + after[Rock] + after[Steam]);
        Assert.Equal(5, kernel.Diagnostics.ReactionSuccessCount);
        Assert.Equal(4, kernel.Diagnostics.BoundaryReactionCount);
    }

    private static void PlaceDataminedBoundaryMatrix(Chunk center, Chunk east, Chunk south, Chunk southeast)
    {
        Set(center, 10, 10, Lava);
        Set(center, 11, 10, Water);

        Set(center, 63, 20, Lava);
        Set(east, 0, 20, Water);

        Set(center, 20, 63, Lava);
        Set(south, 20, 0, Water);

        Set(center, 63, 63, Lava);
        Set(south, 63, 0, Water);

        Set(east, 0, 63, Lava);
        Set(southeast, 0, 0, Water);
    }

    private static ReactionSetup CreateReactionSetup(byte probability)
    {
        Reaction[] packed =
        [
            Reaction(Lava, Water, Rock, Steam, probability),
            Reaction(Water, Lava, Steam, Rock, probability),
        ];
        MaterialDef[] definitions =
        [
            Material(Empty, "empty", CellType.Empty, reactionStart: 0, reactionCount: 0),
            Material(Lava, "lava", CellType.Solid, reactionStart: 0, reactionCount: 1),
            Material(Water, "water", CellType.Solid, reactionStart: 1, reactionCount: 1),
            Material(Rock, "rock", CellType.Solid, reactionStart: 0, reactionCount: 0),
            Material(Steam, "steam", CellType.Solid, reactionStart: 0, reactionCount: 0),
        ];
        MaterialTable materials = new(definitions);
        ReactionTable reactions = new(packed, definitions);
        return new ReactionSetup(materials, new ReactionEngine(materials, reactions));
    }

    private static MaterialDef Material(ushort id, string name, CellType type, int reactionStart, int reactionCount)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = id == Empty ? (byte)0 : (byte)200,
            HeatCapacity = 1f,
            ReactionStart = reactionStart,
            ReactionCount = checked((byte)reactionCount),
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }

    private static Reaction Reaction(ushort inputA, ushort inputB, ushort outputA, ushort outputB, byte probability)
    {
        return new Reaction
        {
            InputA = inputA,
            InputB = inputB,
            OutputA = outputA,
            OutputB = outputB,
            Probability = probability,
            Flags = ReactionFlags.None,
        };
    }

    private static TestChunkSource CreateNeighborhood(ChunkCoord centerCoord, out Chunk center)
    {
        Chunk[] chunks = new Chunk[9];
        int index = 0;
        center = null!;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Chunk chunk = new(new ChunkCoord(centerCoord.X + dx, centerCoord.Y + dy));
                chunks[index++] = chunk;
                if (dx == 0 && dy == 0)
                {
                    center = chunk;
                }
            }
        }

        return new TestChunkSource(chunks);
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

    private static void MarkNonEmptyChunksDirty(TestChunkSource source)
    {
        foreach (Chunk chunk in source.ResidentChunks)
        {
            if (ContainsNonEmptyCell(chunk))
            {
                chunk.SetCurrentDirty(DirtyRect.Full);
            }
        }
    }

    private static bool ContainsNonEmptyCell(Chunk chunk)
    {
        foreach (ushort material in chunk.Material)
        {
            if (material != Empty)
            {
                return true;
            }
        }

        return false;
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

    private static void ResetPair(Chunk chunk, int ax, int ay, ushort materialA, int bx, int by, ushort materialB)
    {
        ClearCell(chunk, ax, ay);
        ClearCell(chunk, bx, by);
        Set(chunk, ax, ay, materialA);
        Set(chunk, bx, by, materialB);
    }

    private static void ClearCell(Chunk chunk, int lx, int ly)
    {
        int local = CellAddressing.LocalIndexFromLocal(lx, ly);
        chunk.Material[local] = Empty;
        chunk.Flags[local] = 0;
        chunk.Lifetime[local] = 0;
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        int local = CellAddressing.LocalIndexFromLocal(lx, ly);
        chunk.Material[local] = material;
        chunk.Flags[local] = 0;
        chunk.Lifetime[local] = 0;
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private readonly record struct ReactionSetup(MaterialTable Materials, ReactionEngine Engine);

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
