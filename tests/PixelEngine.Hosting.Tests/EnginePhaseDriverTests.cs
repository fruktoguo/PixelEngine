using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.World;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Hosting 真实子系统相位驱动测试。
/// </summary>
public sealed class EnginePhaseDriverTests
{
    /// <summary>
    /// 验证 EngineBuilder 会在自定义 hook 前注册 phase driver 入口。
    /// </summary>
    [Fact]
    public void BuilderRegistersPhaseDriversBeforeManualHooks()
    {
        List<string> events = [];
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddPhaseDriver(new RecordingPhaseDriver(events))
            .OnPhase(EnginePhase.CaSimulation, static context => context.Engine.Context.GetService<List<string>>().Add("manual"))
            .Build();
        engine.Context.RegisterService(events);

        _ = engine.RunOneTick();

        Assert.Equal(["driver", "manual"], events);
    }

    /// <summary>
    /// 验证 SimulationPhaseDriver 会调用现有粒子、CA、温度、dirty swap 与抛射相位入口。
    /// </summary>
    [Fact]
    public void SimulationPhaseDriverRunsExistingSimulationPhases()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty));
        TestChunkSource chunks = new(new Chunk(new ChunkCoord(0, 0)));
        CellGrid grid = new(chunks, new MaterialPropsTable(materials.Hot));
        SimulationKernel kernel = new(chunks, new MaterialPropsTable(materials.Hot), profiler: null);
        ParticleSystem particles = new(capacity: 16);
        TemperatureField temperature = new();
        SimulationPhaseDriver driver = new(chunks, grid, kernel, particles, temperature, materials);
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddPhaseDriver(driver)
            .Build();

        _ = engine.RunOneTick();

        Assert.Equal(1u, kernel.FrameIndex);
        Assert.Equal(0, particles.ActiveCount);
        Assert.Equal(0, engine.Context.Counters.FreeParticles);
        Assert.Equal(1, engine.Phases.Count(EnginePhase.ParticleToCell));
        Assert.Equal(1, engine.Phases.Count(EnginePhase.CaSimulation));
        Assert.Equal(1, engine.Phases.Count(EnginePhase.Temperature));
        Assert.Equal(1, engine.Phases.Count(EnginePhase.DirtyRectSwap));
        Assert.Equal(1, engine.Phases.Count(EnginePhase.CellToParticle));
    }

    /// <summary>
    /// 验证 WorldPhaseDriver 会绑定驻留应用与流式 I/O 批处理入口。
    /// </summary>
    [Fact]
    public void WorldPhaseDriverRunsResidencyAndStreamingBatch()
    {
        string worldPath = Path.Combine(Path.GetTempPath(), $"pixelengine-world-phase-{Guid.NewGuid():N}");
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty));
            WorldManager world = new(
                new WorldCamera(0, 0, 64, 64),
                new TemperatureField(),
                materials,
                worldPath,
                fallbackMaterialId: 0,
                new WorldStreamingConfig
                {
                    ActivationMarginChunks = 0,
                    BorderRingWidth = 1,
                    MaxStreamOpsPerFrame = 4,
                });

            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .AddPhaseDriver(new WorldPhaseDriver(world))
                .Build();

            _ = engine.RunOneTick();

            Assert.Equal(1, engine.Phases.Count(EnginePhase.ResidencyApply));
            Assert.Equal(1, engine.Phases.Count(EnginePhase.WorldStreaming));
            Assert.Equal(0, world.Streamer.PendingRequestCount);
            Assert.True(world.Streamer.PendingCompletedCount > 0);
        }
        finally
        {
            if (Directory.Exists(worldPath))
            {
                Directory.Delete(worldPath, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 headless 固定步数驱动可以运行当前已有的 Simulation 与 World phase driver。
    /// </summary>
    [Fact]
    public void HeadlessTicksDriveAvailableSimulationAndWorldPhaseDrivers()
    {
        string worldPath = Path.Combine(Path.GetTempPath(), $"pixelengine-headless-phase-{Guid.NewGuid():N}");
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty));
            TestChunkSource chunks = new(new Chunk(new ChunkCoord(0, 0)));
            CellGrid grid = new(chunks, new MaterialPropsTable(materials.Hot));
            SimulationKernel kernel = new(chunks, new MaterialPropsTable(materials.Hot));
            ParticleSystem particles = new(capacity: 16);
            TemperatureField temperature = new();
            WorldManager world = new(
                new WorldCamera(0, 0, 64, 64),
                temperature,
                materials,
                worldPath,
                fallbackMaterialId: 0,
                new WorldStreamingConfig
                {
                    ActivationMarginChunks = 0,
                    BorderRingWidth = 1,
                    MaxStreamOpsPerFrame = 4,
                });

            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .UseDeterministicMode()
                .AddPhaseDriver(new SimulationPhaseDriver(chunks, grid, kernel, particles, temperature, materials))
                .AddPhaseDriver(new WorldPhaseDriver(world))
                .Build();

            engine.RunHeadlessTicks(3);

            Assert.Equal(3, engine.Context.Clock.FrameIndex);
            Assert.Equal(3, engine.Context.Clock.SimTickIndex);
            Assert.Equal(3u, kernel.FrameIndex);
            Assert.False(engine.Context.Options.EnableGpu);
            Assert.Equal(0, world.Streamer.PendingRequestCount);
        }
        finally
        {
            if (Directory.Exists(worldPath))
            {
                Directory.Delete(worldPath, recursive: true);
            }
        }
    }

    private static MaterialTable Materials(params (string Name, CellType Type)[] definitions)
    {
        MaterialDef[] materials = new MaterialDef[definitions.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = new MaterialDef
            {
                Id = (ushort)i,
                Name = definitions[i].Name,
                Type = definitions[i].Type,
                Density = i == 0 ? (byte)0 : (byte)100,
                HeatCapacity = 1,
                HeatConduct = 255,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            };
        }

        return new MaterialTable(materials);
    }

    private sealed class RecordingPhaseDriver(List<string> events) : IEnginePhaseDriver
    {
        public void RegisterPhases(EnginePhasePipeline phases)
        {
            phases.Register(EnginePhase.CaSimulation, _ => events.Add("driver"));
        }
    }

    private sealed class TestChunkSource(params Chunk[] chunks) : IChunkSource
    {
        public ReadOnlySpan<Chunk> ResidentChunks => chunks;

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i].Coord == coord)
                {
                    chunk = chunks[i];
                    return true;
                }
            }

            chunk = null!;
            return false;
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            if (!TryGetChunk(center, out Chunk chunk))
            {
                neighborhood = default;
                return false;
            }

            neighborhood = new ChunkNeighborhood(chunk, chunk, chunk, chunk, chunk, chunk, chunk, chunk, chunk);
            return true;
        }
    }
}
