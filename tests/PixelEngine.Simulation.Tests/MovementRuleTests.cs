using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// CA movement 规则的精确终态测试。
/// 不变式：各 movement 规则终态与 parity 位符合单步 oracle。
/// </summary>
public sealed class MovementRuleTests
{
    private const ushort Solid = 1;
    private const ushort Sand = 2;
    private const ushort Water = 3;
    private const ushort Gas = 4;
    private const ushort Oil = 5;

    /// <summary>
    /// 验证单个沙 cell 按 bottom-up 规则坍塌到障碍上方。
    /// </summary>
    [Fact]
    public void SingleSandColumnCollapsesOntoObstacle()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, Sand);
        Set(center, 10, 30, Solid);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Sand, Get(center, 10, 29));
        Assert.Equal(Solid, Get(center, 10, 30));
    }

    /// <summary>
    /// 验证水在支撑面上水平铺开为单层，不向下穿过固体支撑。
    /// </summary>
    [Fact]
    public void WaterSpreadsAsSingleLayerOnSupport()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, Water);
        Set(center, 9, 11, Solid);
        Set(center, 10, 11, Solid);
        Set(center, 11, 11, Solid);
        SimulationKernel kernel = new(source, CreateMaterials(waterDispersion: 3));

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Water, Get(center, 13, 10));
        Assert.Equal(0, Get(center, 13, 11));
    }

    /// <summary>
    /// 验证较重水会置换较轻油，形成油在水上的分层。
    /// </summary>
    [Fact]
    public void OilFloatsAboveWaterAfterDensityDisplacement()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, Water);
        Set(center, 10, 11, Oil);
        BlockAroundColumn(center, 10);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(Oil, Get(center, 10, 10));
        Assert.Equal(Water, Get(center, 10, 11));
    }

    /// <summary>
    /// 验证气体上升受阻后会在当前层侧向扩散。
    /// </summary>
    [Fact]
    public void GasSpreadsSidewaysWhenBlockedAbove()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 20, 20, Gas);
        Set(center, 19, 19, Solid);
        Set(center, 20, 19, Solid);
        Set(center, 21, 19, Solid);
        SimulationKernel kernel = new(source, CreateMaterials(), worldSeed: 123);

        kernel.StepCa();

        Assert.Equal(0, Get(center, 20, 20));
        Assert.True(
            Get(center, 18, 20) == Gas ||
            Get(center, 19, 20) == Gas ||
            Get(center, 21, 20) == Gas ||
            Get(center, 22, 20) == Gas);
    }

    /// <summary>
    /// 验证粉末斜下偏置随帧翻转，偶数 tick 后不会固定向同一侧漂移。
    /// </summary>
    [Fact]
    public void PowderDiagonalBiasAlternatesAcrossEvenTicks()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, Sand);
        Set(center, 10, 11, Solid);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();
        // Assert：验证预期结果
        Assert.Equal(Sand, Get(center, 11, 11));

        Set(center, 11, 11, 0);
        SetFlags(center, 10, 10, 0);
        SetFlags(center, 11, 11, 0);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 20, 10, Sand);
        SetFlags(center, 20, 10, CellFlags.Parity);
        Set(center, 20, 11, Solid);
        SetFlags(center, 19, 11, CellFlags.Parity);

        kernel.StepCa();

        Assert.Equal(Sand, Get(center, 19, 11));
    }

    private static void BlockAroundColumn(Chunk chunk, int x)
    {
        Set(chunk, x - 1, 10, Solid);
        Set(chunk, x + 1, 10, Solid);
        Set(chunk, x - 1, 11, Solid);
        Set(chunk, x + 1, 11, Solid);
        Set(chunk, x - 1, 12, Solid);
        Set(chunk, x, 12, Solid);
        Set(chunk, x + 1, 12, Solid);
    }

    private static MaterialPropsTable CreateMaterials(byte waterDispersion = 3)
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder, CellType.Liquid, CellType.Gas, CellType.Liquid],
            [0, 255, 120, 60, 1, 30],
            [0, 0, 0, waterDispersion, 2, waterDispersion],
            [0, 0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0, 0]);
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

    private static void SetFlags(Chunk chunk, int lx, int ly, byte flags)
    {
        chunk.Flags[CellAddressing.LocalIndexFromLocal(lx, ly)] = flags;
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
