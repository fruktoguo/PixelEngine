using PixelEngine.Core;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Dispersion / FlowRate 与 32px 移动上限不变式测试。
/// 不变式：Dispersion/FlowRate 遵守 32px 移动上限。
/// </summary>
public sealed class DispersionMoveCapTests
{
    private const ushort Empty = 0;
    private const ushort Solid = 1;
    private const ushort Water = 2;

    /// <summary>
    /// 即使运行时表给出超过 MoveCap 的液体 FlowRate，单个 CA tick 也不会水平移动超过 32px。
    /// </summary>
    [Fact]
    public void LiquidSingleStepHorizontalDispersionNeverExceedsMoveCap()
    {
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(-1, -1, 1, 1);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, Water);
        Set(center, 9, 11, Solid);
        Set(center, 10, 11, Solid);
        Set(center, 11, 11, Solid);
        SimulationKernel kernel = new(source, CreateMaterials(waterDispersion: byte.MaxValue))
        {
            ForceSingleThread = true,
        };

        kernel.StepCa();

        Assert.Equal(Empty, Get(center, 10, 10));
        Assert.Equal(Water, Get(center, 10 + EngineConstants.MoveCap, 10));
        Assert.Equal(Empty, Get(center, 10 + EngineConstants.MoveCap + 1, 10));
    }

    /// <summary>
    /// FlowRate 是 Dispersion 的热路径语义别名，测试命名按玩法字段锁定。
    /// </summary>
    [Fact]
    public void FlowRateAliasReadsDispersionValueFromMaterialProps()
    {
        MaterialPropsTable props = CreateMaterials(waterDispersion: 19);

        Assert.Equal(19, props.DispersionOf(Water));
        Assert.Equal(19, props.FlowRateOf(Water));
    }

    private static MaterialPropsTable CreateMaterials(byte waterDispersion)
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Liquid],
            [0, 255, 60],
            [0, 0, waterDispersion],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0]);
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }
}
