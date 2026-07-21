using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// 固体拓扑变化通知与并行区域归并测试。
/// 不变式：所有 Solid 占用变化可观测，CA worker 并发归并不丢边界且不产生重复区域。
/// </summary>
public sealed class CellTopologyChangeTests
{
    private const ushort Empty = 0;
    private const ushort Fire = 1;
    private const ushort Stone = 2;
    private const ushort Ash = 3;

    /// <summary>
    /// 验证并发 writer 会把坐标与变化方向归并到一个可排空区域。
    /// </summary>
    [Fact]
    public void AccumulatorMergesConcurrentWritersWithoutLosingBounds()
    {
        CellTopologyChangeAccumulator accumulator = new();

        _ = Parallel.For(0, 4096, i =>
        {
            CellTopologyChangeKind kind = (i & 1) == 0
                ? CellTopologyChangeKind.SolidRemoved
                : CellTopologyChangeKind.SolidAdded;
            CellTopologyChangeEvent item = new(i, -i, Stone, Empty, kind);
            accumulator.OnCellTopologyChanged(in item);
        });

        Assert.True(accumulator.TryDrain(out CellTopologyChangeRegion region));
        Assert.Equal(0, region.MinX);
        Assert.Equal(-4095, region.MinY);
        Assert.Equal(4095, region.MaxX);
        Assert.Equal(0, region.MaxY);
        Assert.Equal(
            CellTopologyChangeKind.SolidRemoved | CellTopologyChangeKind.SolidAdded,
            region.Kinds);
        Assert.False(accumulator.TryDrain(out _));
    }

    /// <summary>
    /// 验证启用拓扑通知后的逐 cell 累加热路径保持零托管分配。
    /// </summary>
    [Fact]
    public void AccumulatorWritePathHasZeroManagedAllocation()
    {
        CellTopologyChangeAccumulator accumulator = new();
        CellTopologyChangeEvent item = new(
            12,
            18,
            Stone,
            Empty,
            CellTopologyChangeKind.SolidRemoved);
        for (int i = 0; i < 256; i++)
        {
            accumulator.OnCellTopologyChanged(in item);
        }

        Assert.True(accumulator.TryDrain(out _));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 4096; i++)
        {
            accumulator.OnCellTopologyChanged(in item);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(accumulator.TryDrain(out _));
    }

    /// <summary>
    /// 验证输入安全相位的 Solid 写入与清除都进入同一个拓扑契约。
    /// </summary>
    [Fact]
    public void KernelInputWritesReportSolidAddedAndRemoved()
    {
        DeterministicSimFixture fixture = new();
        CellTopologyChangeAccumulator accumulator = new();
        SimulationKernel kernel = new(
            fixture.Source,
            new MaterialPropsTable(fixture.Materials.Hot),
            cellTopologyChangeSink: accumulator);

        kernel.EditCellAtInputPhase(7, 9, DeterministicSimFixture.Solid, persistentFlags: 0);
        kernel.ClearCellAtInputPhase(7, 9);

        Assert.True(accumulator.TryDrain(out CellTopologyChangeRegion region));
        Assert.Equal(new CellTopologyChangeRegion(
            7,
            9,
            7,
            9,
            CellTopologyChangeKind.SolidRemoved | CellTopologyChangeKind.SolidAdded), region);
    }

    /// <summary>
    /// 验证普通双输出 CA 反应经 NeighborWindow 权威写入口报告 Solid 消失。
    /// </summary>
    [Fact]
    public void CaReactionReportsSolidRemovalThroughAuthoritativeWritePath()
    {
        DeterministicSimFixture.TestChunkSource source =
            DeterministicSimFixture.TestChunkSource.CreateDense(-1, -1, 1, 1);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        MaterialDef[] definitions = CreateReactionMaterials();
        MaterialTable materials = new(definitions);
        Reaction[] reactions =
        [
            new Reaction
            {
                InputA = Fire,
                InputB = Stone,
                OutputA = Fire,
                OutputB = Ash,
                Probability = byte.MaxValue,
            },
        ];
        ReactionEngine reactionEngine = new(materials, new ReactionTable(reactions, definitions));
        CellTopologyChangeAccumulator accumulator = new();
        SimulationKernel kernel = new(
            source,
            new MaterialPropsTable(materials.Hot),
            worldSeed: 17,
            reactionExecutor: reactionEngine,
            cellTopologyChangeSink: accumulator)
        {
            ForceSingleThread = true,
        };
        Set(center, 10, 10, Fire);
        Set(center, 11, 10, Stone);
        center.SetCurrentDirty(new DirtyRect(10, 10, 11, 10));

        kernel.StepCa();

        Assert.Equal(Ash, Get(center, 11, 10));
        Assert.True(accumulator.TryDrain(out CellTopologyChangeRegion region));
        Assert.Equal(new CellTopologyChangeRegion(
            11,
            10,
            11,
            10,
            CellTopologyChangeKind.SolidRemoved), region);
    }

    /// <summary>
    /// 验证温度相变从 Solid 熔化为 Liquid 时报告精确拓扑区域。
    /// </summary>
    [Fact]
    public void TemperatureMeltReportsSolidRemoval()
    {
        DeterministicSimFixture fixture = new();
        Chunk center = fixture.Center;
        Set(center, 14, 18, DeterministicSimFixture.Ice);
        center.SetCurrentDirty(new DirtyRect(14, 18, 14, 18));
        TemperatureField temperature = new();
        temperature.AddHeat(14, 18, 20);
        CellTopologyChangeAccumulator accumulator = new();

        temperature.ApplyPhaseTransitions(
            fixture.Source,
            fixture.Materials,
            CellFlags.Parity,
            topologyChangeSink: accumulator);

        Assert.Equal(DeterministicSimFixture.Water, Get(center, 14, 18));
        Assert.True(accumulator.TryDrain(out CellTopologyChangeRegion region));
        Assert.Equal(new CellTopologyChangeRegion(
            14,
            18,
            14,
            18,
            CellTopologyChangeKind.SolidRemoved), region);
    }

    private static MaterialDef[] CreateReactionMaterials()
    {
        return
        [
            Material(Empty, "empty", CellType.Empty),
            Material(Fire, "fire", CellType.Fire) with
            {
                ReactionStart = 0,
                ReactionCount = 1,
            },
            Material(Stone, "stone", CellType.Solid),
            Material(Ash, "ash", CellType.Powder),
        ];
    }

    private static MaterialDef Material(ushort id, string name, CellType type)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = id == Empty ? (byte)0 : (byte)100,
            HeatCapacity = 1,
            TextureId = -1,
        };
    }

    private static void Set(Chunk chunk, int localX, int localY, ushort material)
    {
        chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(localX, localY)] = material;
    }

    private static ushort Get(Chunk chunk, int localX, int localY)
    {
        return chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(localX, localY)];
    }
}
