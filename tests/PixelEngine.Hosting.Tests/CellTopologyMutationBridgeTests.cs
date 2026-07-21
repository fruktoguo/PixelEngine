using PixelEngine.Core.Events;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Simulation 固体拓扑变化到脚本世界事件的 Hosting 桥测试。
/// 不变式：一个 sim tick 最多发布一个合并区域，事件只经公开脚本总线派发。
/// </summary>
public sealed class CellTopologyMutationBridgeTests
{
    /// <summary>
    /// 验证多个权威 cell 写入会在 Temperature 相位边界合并为单个 SolidTopology 事件。
    /// </summary>
    [Fact]
    public void SimulationPhasePublishesOneMergedSolidTopologyMutationPerTick()
    {
        MaterialTable materials = new(
        [
            Material(0, "empty", CellType.Empty),
            Material(1, "stone", CellType.Solid),
        ]);
        Chunk chunk = new(new ChunkCoord(0, 0));
        TestChunkSource chunks = new(chunk);
        MaterialPropsTable props = new(materials.Hot);
        CellGrid grid = new(chunks, props);
        CellTopologyChangeAccumulator topologyChanges = new();
        SimulationKernel kernel = new(
            chunks,
            props,
            cellTopologyChangeSink: topologyChanges)
        {
            ForceSingleThread = true,
        };
        ParticleSystem particles = new(capacity: 16);
        TemperatureField temperature = new();
        SimulationPhaseDriver driver = new(
            chunks,
            grid,
            kernel,
            particles,
            temperature,
            materials,
            topologyChanges: topologyChanges);
        EventBus coreEvents = new(capacityPerChannel: 8);
        using ScriptEventBus scriptEvents = new(coreEvents);
        using ScriptSimulationContext scripts = new(
            new PixelEngine.Scripting.Scene(),
            grid,
            kernel,
            particles,
            materials,
            temperature,
            scriptEvents);
        kernel.EditCellAtInputPhase(30, 31, material: 1, persistentFlags: 0);
        driver.AttachScriptContext(scripts);
        List<WorldMutationEvent> received = [];
        using IDisposable subscription = scriptEvents.Subscribe<WorldMutationEvent>(received.Add);
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddPhaseDriver(driver)
            .Build();

        kernel.EditCellAtInputPhase(4, 5, material: 1, persistentFlags: 0);
        kernel.ClearCellAtInputPhase(4, 5);
        kernel.EditCellAtInputPhase(20, 25, material: 1, persistentFlags: 0);

        _ = engine.RunOneTick();
        scriptEvents.DrainEvents();

        WorldMutationEvent mutation = Assert.Single(received);
        Assert.Equal(new WorldMutationEvent(
            MinX: 4,
            MinY: 5,
            MaxXExclusive: 21,
            MaxYExclusive: 26,
            WorldMutationKind.SolidTopology), mutation);
        Assert.False(topologyChanges.TryDrain(out _));
    }

    private static MaterialDef Material(ushort id, string name, CellType type)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = id == 0 ? (byte)0 : (byte)100,
            HeatCapacity = 1,
            TextureId = -1,
        };
    }

    private sealed class TestChunkSource(Chunk chunk) : IChunkSource
    {
        private readonly Chunk[] _chunks = [chunk];

        public ReadOnlySpan<Chunk> ResidentChunks => _chunks;

        public bool TryGetChunk(ChunkCoord coord, out Chunk result)
        {
            if (coord == chunk.Coord)
            {
                result = chunk;
                return true;
            }

            result = null!;
            return false;
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            if (center != chunk.Coord)
            {
                neighborhood = default;
                return false;
            }

            neighborhood = new ChunkNeighborhood(
                chunk,
                chunk,
                chunk,
                chunk,
                chunk,
                chunk,
                chunk,
                chunk,
                chunk);
            return true;
        }
    }
}
