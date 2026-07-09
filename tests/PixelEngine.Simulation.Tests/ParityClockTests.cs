using PixelEngine.Core;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// CA 单缓冲 parity 时钟位测试。
/// 不变式：parity 位每帧翻转、读写窗口与单缓冲模型一致。
/// </summary>
public sealed class ParityClockTests
{
    private const ushort Solid = 1;
    private const ushort Sand = 2;
    private const ushort Water = 3;

    /// <summary>
    /// 验证 parity 每个 CA tick 翻转含义，cell flag 不需要全局清零也能被下一帧重新处理。
    /// </summary>
    [Fact]
    public void ParityBitTogglesAcrossTicksWithoutClearingCellFlags()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Chunk south = source.GetRequired(new ChunkCoord(0, 1));
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, Sand);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();
        // Assert：验证预期结果
        Assert.Equal(CellFlags.Parity, kernel.CurrentParity);
        Assert.Equal(Sand, Get(center, 10, 10 + EngineConstants.MoveCap));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 10, 10 + EngineConstants.MoveCap), kernel.CurrentParity));

        kernel.SwapDirtyRects();
        kernel.StepCa();

        Assert.Equal(0, kernel.CurrentParity);
        Assert.Equal(Sand, Get(south, 10, 10 + (EngineConstants.MoveCap * 2) - EngineConstants.ChunkSize));
        Assert.True(CellFlags.MatchesFrame(
            GetFlags(south, 10, 10 + (EngineConstants.MoveCap * 2) - EngineConstants.ChunkSize),
            kernel.CurrentParity));
    }

    /// <summary>
    /// 验证本帧已经带当前 parity 的源 cell 会被跳过，不会再次移动。
    /// </summary>
    [Fact]
    public void SourceCellAlreadyMarkedWithCurrentParityIsSkipped()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, Sand);
        SetFlags(center, 10, 10, CellFlags.Parity);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(Sand, Get(center, 10, 10));
        Assert.Equal(0, Get(center, 10, 11));
    }

    /// <summary>
    /// 验证同一帧内刚移动过的液体不会被同一行后续扫描再次移动。
    /// </summary>
    [Fact]
    public void MovedLiquidCellIsProcessedAtMostOncePerFrame()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, Water);
        Set(center, 9, 11, Solid);
        Set(center, 10, 11, Solid);
        Set(center, 11, 11, Solid);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Water, Get(center, 13, 10));
        Assert.Equal(0, Get(center, 16, 10));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 13, 10), kernel.CurrentParity));
    }

    private static MaterialPropsTable CreateMaterials()
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder, CellType.Liquid],
            [0, 255, 120, 60],
            [0, 0, 0, 3],
            [0, 0, 0, 0],
            [0, 0, 0, 0],
            [0, 0, 0, 0]);
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

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private static void SetFlags(Chunk chunk, int lx, int ly, byte flags)
    {
        chunk.Flags[CellAddressing.LocalIndexFromLocal(lx, ly)] = flags;
    }

    private static byte GetFlags(Chunk chunk, int lx, int ly)
    {
        return chunk.Flags[CellAddressing.LocalIndexFromLocal(lx, ly)];
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
