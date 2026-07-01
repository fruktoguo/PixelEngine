using PixelEngine.Core;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// CA KeepAlive 边界唤醒与 32px halo 专项测试。
/// </summary>
public sealed class KeepAliveBoundaryTests
{
    private const ushort Solid = 1;
    private const ushort Sand = 2;

    /// <summary>
    /// 验证雪崩跨 chunk 边界后写入邻居 incoming dirty，并在下一帧继续传播。
    /// </summary>
    [Fact]
    public void BoundaryAvalancheMarksIncomingDirtyAndContinuesAfterSwap()
    {
        TestChunkSource source = CreateGrid(-1, 1, -1, 2);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        Chunk south = source.GetRequired(new ChunkCoord(0, 1));
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, EngineConstants.ChunkSize - 1, Sand);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(0, Get(center, 10, EngineConstants.ChunkSize - 1));
        Assert.Equal(Sand, Get(south, 10, EngineConstants.MoveCap - 1));
        Assert.Equal(DirtyRect.Empty, south.WorkingDirty);
        Assert.Equal(new DirtyRect(8, 29, 12, 33), south.GetIncomingDirty(KeepAliveDirections.SlotNorth));
        Assert.Equal(1, kernel.Diagnostics.BoundaryWakeCount);
        BoundaryWakeRecord record = kernel.Diagnostics.BoundaryWakeRecords[0];
        Assert.Equal(new ChunkCoord(0, 1), record.TargetCoord);
        Assert.Equal(KeepAliveDirections.SlotNorth, record.IncomingSlot);
        Assert.Equal(new DirtyRect(8, 29, 12, 33), record.Rect);

        kernel.SwapDirtyRects();
        Assert.Equal(new DirtyRect(8, 29, 12, 33), south.CurrentDirty);
        Assert.Equal(ChunkState.Awake, south.State);

        kernel.StepCa();

        Assert.Equal(0, Get(south, 10, EngineConstants.MoveCap - 1));
        Assert.Equal(Sand, Get(south, 10, EngineConstants.ChunkSize - 1));
        Assert.Equal(new DirtyRect(8, 29, 12, 63), south.WorkingDirty);
    }

    /// <summary>
    /// 验证边界附近完全沉降且没有新 dirty 时，帧边界会收回 dirty 并让 chunk sleep。
    /// </summary>
    [Fact]
    public void SettledBoundaryChunkShrinksDirtyAndSleeps()
    {
        TestChunkSource source = CreateGrid(-1, 1, -1, 1);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        center.SetCurrentDirty(new DirtyRect(8, 60, 12, 63));
        Set(center, 10, 62, Sand);
        Set(center, 9, 63, Solid);
        Set(center, 10, 63, Solid);
        Set(center, 11, 63, Solid);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();
        kernel.SwapDirtyRects();

        Assert.Equal(Sand, Get(center, 10, 62));
        Assert.Equal(DirtyRect.Empty, center.CurrentDirty);
        Assert.Equal(DirtyRect.Empty, center.WorkingDirty);
        Assert.Equal(ChunkState.Sleeping, center.State);
    }

    /// <summary>
    /// 验证跨界写入受 32px MoveCap/halo 约束，不会越过相邻 chunk 的 halo 深度。
    /// </summary>
    [Fact]
    public void BoundaryWriteStaysWithinThirtyTwoPixelHalo()
    {
        TestChunkSource source = CreateGrid(-1, 1, -1, 2);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        Chunk south = source.GetRequired(new ChunkCoord(0, 1));
        Chunk farSouth = source.GetRequired(new ChunkCoord(0, 2));
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, EngineConstants.ChunkSize - 1, Sand);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(Sand, Get(south, 10, EngineConstants.MoveCap - 1));
        AssertCellsEmpty(south, 10, EngineConstants.MoveCap, EngineConstants.ChunkSize - 1);
        AssertChunkEmpty(farSouth);
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

    private static TestChunkSource CreateGrid(int minX, int maxX, int minY, int maxY)
    {
        Chunk[] chunks = new Chunk[(maxX - minX + 1) * (maxY - minY + 1)];
        int index = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                chunks[index++] = new Chunk(new ChunkCoord(x, y));
            }
        }

        return new TestChunkSource(chunks);
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private static void AssertCellsEmpty(Chunk chunk, int lx, int minY, int maxY)
    {
        for (int ly = minY; ly <= maxY; ly++)
        {
            Assert.Equal(0, Get(chunk, lx, ly));
        }
    }

    private static void AssertChunkEmpty(Chunk chunk)
    {
        for (int i = 0; i < chunk.Material.Length; i++)
        {
            Assert.Equal(0, chunk.Material[i]);
        }
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
