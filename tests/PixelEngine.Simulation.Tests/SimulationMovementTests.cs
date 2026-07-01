using PixelEngine.Core;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Simulation 节点 3 的单线程 movement 与 parity 测试。
/// </summary>
public sealed class SimulationMovementTests
{
    private const ushort Solid = 1;
    private const ushort Sand = 2;
    private const ushort Water = 3;
    private const ushort Gas = 4;
    private const ushort Fire = 5;
    private const ushort Oil = 6;

    /// <summary>
    /// 验证 powder 按 bottom-up 扫描下落一格，并给源/目标 cell 标记当前 parity 与 working dirty。
    /// </summary>
    [Fact]
    public void StepCaMovesPowderDownAndMarksParityAndDirty()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 10, Sand);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(CellFlags.Parity, kernel.CurrentParity);
        Assert.Equal(1U, kernel.FrameIndex);
        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Sand, Get(center, 10, 11));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 10, 10), kernel.CurrentParity));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 10, 11), kernel.CurrentParity));
        Assert.Equal(new DirtyRect(8, 8, 12, 13), center.WorkingDirty);
    }

    /// <summary>
    /// 验证 powder 在正下与斜下都被阻挡时不会水平自流，保留休止角。
    /// </summary>
    [Fact]
    public void StepCaDoesNotMovePowderHorizontallyOnFlatSupport()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 10, Sand);
        Set(center, 9, 11, Solid);
        Set(center, 10, 11, Solid);
        Set(center, 11, 11, Solid);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(Sand, Get(center, 10, 10));
        Assert.Equal(0, Get(center, 9, 10));
        Assert.Equal(0, Get(center, 11, 10));
    }

    /// <summary>
    /// 验证 powder 左右斜下偏置随帧切换，避免稳定单侧漂移。
    /// </summary>
    [Fact]
    public void StepCaAlternatesPowderDiagonalBiasAcrossFrames()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 10, Sand);
        Set(center, 10, 11, Solid);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(Sand, Get(center, 11, 11));

        Set(center, 11, 11, 0);
        SetFlags(center, 10, 10, 0);
        SetFlags(center, 11, 11, 0);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 20, 10, Sand);
        SetFlags(center, 20, 10, CellFlags.Parity);
        Set(center, 20, 11, Solid);
        SetFlags(center, 19, 11, CellFlags.Parity);

        kernel.StepCa();

        Assert.Equal(Sand, Get(center, 19, 11));
    }

    /// <summary>
    /// 验证液体水平铺开后，因 parity 标记不会在同一行后续扫描中再次移动。
    /// </summary>
    [Fact]
    public void StepCaUsesParityToPreventMovedLiquidFromMovingTwice()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 10, Water);
        Set(center, 9, 11, Solid);
        Set(center, 10, 11, Solid);
        Set(center, 11, 11, Solid);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Water, Get(center, 13, 10));
        Assert.Equal(0, Get(center, 16, 10));
        Assert.Equal(0, Get(center, 13, 11));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 13, 10), kernel.CurrentParity));
    }

    /// <summary>
    /// 验证 gas 按上下翻转规则优先上升。
    /// </summary>
    [Fact]
    public void StepCaMovesGasUp()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 20, 20, Gas);
        SimulationKernel kernel = new(source, CreateMaterials(), worldSeed: 123);

        kernel.StepCa();

        Assert.Equal(0, Get(center, 20, 20));
        Assert.Equal(Gas, Get(center, 20, 19));
    }

    /// <summary>
    /// 验证 gas 上升受阻后会侧向扩散，而不是保持干净柱状。
    /// </summary>
    [Fact]
    public void StepCaMovesGasSidewaysWhenUpwardCellsAreBlocked()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
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
    /// 验证 Fire 与 Solid 不参与 swap movement；Fire 仍会打 parity 表示本帧已处理。
    /// </summary>
    [Fact]
    public void StepCaDoesNotMoveFireOrSolid()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 30, 30, Fire);
        Set(center, 31, 30, Solid);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(Fire, Get(center, 30, 30));
        Assert.Equal(Solid, Get(center, 31, 30));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 30, 30), kernel.CurrentParity));
        Assert.False(CellFlags.MatchesFrame(GetFlags(center, 31, 30), kernel.CurrentParity));
    }

    /// <summary>
    /// 验证已经带当前帧 parity 的源 cell 会被扫描跳过。
    /// </summary>
    [Fact]
    public void StepCaSkipsSourceCellAlreadyMarkedWithCurrentParity()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 10, Sand);
        SetFlags(center, 10, 10, CellFlags.Parity);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(Sand, Get(center, 10, 10));
        Assert.Equal(0, Get(center, 10, 11));
    }

    /// <summary>
    /// 验证更轻目标若已带当前帧 parity，不会被后续 cell 再次置换。
    /// </summary>
    [Fact]
    public void StepCaDoesNotDisplaceTargetAlreadyMarkedWithCurrentParity()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 9, Sand);
        Set(center, 9, 10, Solid);
        Set(center, 10, 10, Water);
        Set(center, 11, 10, Solid);
        SetFlags(center, 10, 10, CellFlags.Parity);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(Sand, Get(center, 10, 9));
        Assert.Equal(Water, Get(center, 10, 10));
    }

    /// <summary>
    /// 验证 movement 覆盖 RigidOwned 目标时通知 damage sink，并消费 owned 标记。
    /// </summary>
    [Fact]
    public void StepCaReportsRigidOwnedTargetDamageAndConsumesOwnedFlag()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 10, Sand);
        Set(center, 10, 11, Water);
        SetFlags(center, 10, 11, CellFlags.RigidOwned);
        CountingRigidDamageSink damageSink = new();
        SimulationKernel kernel = new(source, CreateMaterials(), rigidDamageSink: damageSink);

        kernel.StepCa();

        Assert.Equal(1, damageSink.Count);
        Assert.Equal((10, 11), damageSink.Last);
        Assert.Equal(Sand, Get(center, 10, 11));
        Assert.False(CellFlags.Has(GetFlags(center, 10, 10), CellFlags.RigidOwned));
        Assert.False(CellFlags.Has(GetFlags(center, 10, 11), CellFlags.RigidOwned));
    }

    /// <summary>
    /// 验证跨 chunk movement 会写目标 chunk working dirty 与朝向中心的 KeepAlive incoming 槽。
    /// </summary>
    [Fact]
    public void StepCaMarksKeepAliveIncomingSlotWhenMovingAcrossChunkBoundary()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Chunk south = source.GetRequired(new ChunkCoord(0, 1));
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 63, Sand);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(0, Get(center, 10, 63));
        Assert.Equal(Sand, Get(south, 10, 0));
        Assert.Equal(DirtyRect.Empty, south.WorkingDirty);
        Assert.Equal(new DirtyRect(8, 0, 12, 2), south.GetIncomingDirty(1));
    }

    /// <summary>
    /// 验证液体水平 dispersion 被 MoveCap 钳制。
    /// </summary>
    [Fact]
    public void StepCaClampsLiquidDispersionToMoveCap()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 10, Water);
        Set(center, 9, 11, Solid);
        Set(center, 10, 11, Solid);
        Set(center, 11, 11, Solid);
        SimulationKernel kernel = new(source, CreateMaterials(waterDispersion: 40));

        kernel.StepCa();

        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Water, Get(center, 10 + EngineConstants.MoveCap, 10));
        Assert.Equal(0, Get(center, 10 + EngineConstants.MoveCap + 1, 10));
    }

    /// <summary>
    /// 验证较重水液会置换未移动的较轻油液，使油浮在水上。
    /// </summary>
    [Fact]
    public void StepCaDisplacesUnmovedLighterOilSoOilFloatsOnWater()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 10, Water);
        Set(center, 10, 11, Oil);
        Set(center, 9, 10, Solid);
        Set(center, 11, 10, Solid);
        Set(center, 9, 11, Solid);
        Set(center, 11, 11, Solid);
        Set(center, 9, 12, Solid);
        Set(center, 10, 12, Solid);
        Set(center, 11, 12, Solid);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(Oil, Get(center, 10, 10));
        Assert.Equal(Water, Get(center, 10, 11));
    }

    /// <summary>
    /// 验证较轻油液位于水上时不会下沉置换较重水液。
    /// </summary>
    [Fact]
    public void StepCaKeepsLighterOilAboveWater()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 10, Oil);
        Set(center, 10, 11, Water);
        Set(center, 9, 10, Solid);
        Set(center, 11, 10, Solid);
        Set(center, 9, 11, Solid);
        Set(center, 11, 11, Solid);
        Set(center, 9, 12, Solid);
        Set(center, 10, 12, Solid);
        Set(center, 11, 12, Solid);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        Assert.Equal(Oil, Get(center, 10, 10));
        Assert.Equal(Water, Get(center, 10, 11));
    }

    private static MaterialPropsTable CreateMaterials(byte waterDispersion = 3)
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder, CellType.Liquid, CellType.Gas, CellType.Fire, CellType.Liquid],
            [0, 255, 120, 60, 1, 1, 30],
            [0, 0, 0, waterDispersion, 2, 0, waterDispersion],
            [0, 0, 0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0, 0, 0]);
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

    private static void SetCurrentDirty(Chunk chunk, DirtyRect rect)
    {
        chunk.SetCurrentDirty(rect);
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

    private sealed class CountingRigidDamageSink : IRigidDamageSink
    {
        public int Count { get; private set; }

        public (int X, int Y) Last { get; private set; }

        public void OnOwnedCellDamaged(int wx, int wy)
        {
            Count++;
            Last = (wx, wy);
        }
    }
}
