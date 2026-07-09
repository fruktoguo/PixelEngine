using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Simulation 节点 4 的 dirty rectangle 生命周期测试。
/// 不变式：dirty rect 合并/清空与 CA 步进相位一致。
/// </summary>
public sealed class DirtyRectLifecycleTests
{
    /// <summary>
    /// 验证 SwapDirtyRects 会合并 working 与 incoming，并清空下一帧累积槽。
    /// </summary>
    [Fact]
    public void ChunkSwapDirtyRectsMergesWorkingAndIncomingThenClearsAccumulation()
    {
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunk.SetWorkingDirty(new DirtyRect(10, 10, 12, 12));
        chunk.MarkIncomingDirty(0, new DirtyRect(1, 2, 3, 4));
        chunk.MarkIncomingDirty(7, new DirtyRect(50, 51, 52, 53));

        chunk.SwapDirtyRects();

        Assert.Equal(new DirtyRect(1, 2, 52, 53), chunk.CurrentDirty);
        Assert.Equal(DirtyRect.Empty, chunk.WorkingDirty);
        Assert.Equal(ChunkState.Awake, chunk.State);
        for (int i = 0; i < chunk.IncomingDirtySlotCount; i++)
        {
            Assert.Equal(DirtyRect.Empty, chunk.GetIncomingDirty(i));
        }
    }

    /// <summary>
    /// 验证没有任何 dirty 累积时，帧边界 swap 会让 chunk 进入 Sleeping。
    /// </summary>
    [Fact]
    public void ChunkSwapDirtyRectsSleepsWhenNextCurrentIsEmpty()
    {
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunk.SetCurrentDirty(DirtyRect.Full);
        chunk.SetWorkingDirty(DirtyRect.Empty);

        chunk.SwapDirtyRects();

        Assert.Equal(DirtyRect.Empty, chunk.CurrentDirty);
        Assert.Equal(DirtyRect.Empty, chunk.WorkingDirty);
        Assert.Equal(ChunkState.Sleeping, chunk.State);
    }

    /// <summary>
    /// 验证 SimulationKernel 帧边界 swap 后，sleeping chunk 不再被下一次 StepCa 处理。
    /// </summary>
    [Fact]
    public void KernelSwapDirtyRectsFeedsNextStepAndSleepsWhenNoNewDirtyExists()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetWorkingDirty(new DirtyRect(10, 10, 10, 10));
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.SwapDirtyRects();

        Assert.Equal(ChunkState.Awake, center.State);
        Assert.Equal(new DirtyRect(10, 10, 10, 10), center.CurrentDirty);
        Assert.Equal(DirtyRect.Empty, center.WorkingDirty);

        kernel.StepCa();
        kernel.SwapDirtyRects();

        Assert.Equal(ChunkState.Sleeping, center.State);
        Assert.Equal(DirtyRect.Empty, center.CurrentDirty);
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
