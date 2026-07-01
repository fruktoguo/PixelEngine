using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.World;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

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
    /// 验证 RenderPhaseDriver 会在相位 9 构建 CPU render buffer，并把脚本相机、粒子与 fog-of-war 结果提交到渲染 sink。
    /// </summary>
    [Fact]
    public void RenderPhaseDriverBuildsBufferAndSubmitsFrame()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunk.Material[0] = 1;
        TestChunkSource chunks = new(chunk);
        ParticleSystem particles = new(capacity: 16);
        Assert.True(particles.TrySpawn(new ParticleSpawn(1, 0, 0, 0, Material: 1, ColorVariant: 0, Life: 10)));
        TemperatureField temperature = new();
        ScriptCameraApi camera = new(viewportWidth: 32, viewportHeight: 16, centerX: 16, centerY: 8, zoom: 1);
        ScriptCameraSynchronizer cameraSync = new(camera);
        _ = cameraSync.Sync();
        ScriptLightingApi lighting = new();
        lighting.RevealAround(16, 8, radius: 4, alpha: 192);
        ScriptLightingSynchronizer lightingSync = new(lighting, cameraSync);
        lightingSync.Sync();
        RecordingRenderFrameSink sink = new();
        RenderPhaseDriver driver = new(
            chunks,
            materials,
            temperature,
            particles,
            cameraSync,
            lightingSync,
            sink);

        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddPhaseDriver(driver)
            .Build();

        _ = engine.RunOneTick();

        Assert.Equal(1, sink.FrameCount);
        Assert.Equal(32, sink.Width);
        Assert.Equal(16, sink.Height);
        Assert.Equal(32, sink.AuxWidth);
        Assert.Equal(16, sink.AuxHeight);
        Assert.Equal(1, engine.Phases.Count(EnginePhase.BuildRenderBuffer));
        Assert.Equal(1, sink.ParticleCount);
        Assert.Equal(1, sink.DirtyRectCount);
        Assert.Equal(new PixelUploadRect(0, 0, 32, 16), sink.FirstDirtyRect);
        Assert.NotNull(sink.FogOfWar);
        Assert.Equal(192, sink.FogOfWar.RevealAlpha(16, 8));
        Assert.Equal(0xFF_80_80_80u, sink.FirstPixel);
        Assert.Equal(0xFF_80_80_80u, sink.ParticlePixel);
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

    /// <summary>
    /// 验证脚本相位在 sim 降频时仍逐帧 Update，但 FixedSimTick 只随 sim tick 调用。
    /// </summary>
    [Fact]
    public void ScriptingPhaseDriverUpdatesEveryFrameButFixedOnlyOnSimFrames()
    {
        RecordingScriptRuntime runtime = new();
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithSimHz(PixelEngine.Core.EngineConstants.SimHzDownscaled)
            .AddPhaseDriver(new ScriptingPhaseDriver(runtime, new FakeScriptContext(new ScriptScene())))
            .Build();

        _ = engine.RunOneTick();
        _ = engine.RunOneTick();

        Assert.Equal(1, runtime.InitializeCount);
        Assert.Equal(2, runtime.BeginCount);
        Assert.Equal(2, runtime.UpdateCount);
        Assert.Equal(1, runtime.FixedCount);
        Assert.Equal(2, runtime.EndCount);
    }

    /// <summary>
    /// 验证真实 ScriptRuntime 通过 Hosting 相位 1 派发 Behaviour 生命周期。
    /// </summary>
    [Fact]
    public void ScriptingPhaseDriverDispatchesBehaviourLifecycleThroughRuntime()
    {
        ScriptScene scene = new();
        Entity entity = scene.CreateEntity();
        HostingLifecycleScript script = entity.AddComponent<HostingLifecycleScript>();
        List<string> events = [];
        script.Events = events;
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithSimHz(PixelEngine.Core.EngineConstants.SimHzDownscaled)
            .AddPhaseDriver(new ScriptingPhaseDriver(new ScriptRuntime(), new FakeScriptContext(scene)))
            .Build();

        _ = engine.RunOneTick();
        _ = engine.RunOneTick();
        entity.Destroy();
        _ = engine.RunOneTick();

        Assert.Equal(["start", "update", "fixed", "update", "update", "fixed", "destroy"], events);
    }

    /// <summary>
    /// 验证 Engine.AttachScripting 会把脚本运行时接入相位 1 并注册服务。
    /// </summary>
    [Fact]
    public void AttachScriptingRegistersRuntimeAndRunsPhaseOne()
    {
        RecordingScriptRuntime runtime = new();
        FakeScriptContext context = new(new ScriptScene());
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithSimHz(PixelEngine.Core.EngineConstants.SimHzDownscaled)
            .Build();

        engine.AttachScripting(context, runtime);
        _ = engine.RunOneTick();
        _ = engine.RunOneTick();

        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.Scripting));
        Assert.Same(runtime, engine.Context.GetService<IScriptRuntime>());
        Assert.Same(context, engine.Context.GetService<IScriptContext>());
        Assert.Equal(1, engine.Phases.Count(EnginePhase.GameLogicAndScripts));
        Assert.Equal(1, runtime.InitializeCount);
        Assert.Equal(2, runtime.UpdateCount);
        Assert.Equal(1, runtime.FixedCount);
        _ = Assert.Throws<InvalidOperationException>(() => engine.AttachScripting(context, new RecordingScriptRuntime()));
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
                BaseColorBGRA = i == 0 ? 0 : 0xFF_80_80_80u,
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

    private sealed class RecordingRenderFrameSink : IRenderFrameSink
    {
        public int FrameCount { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public int AuxWidth { get; private set; }

        public int AuxHeight { get; private set; }

        public int ParticleCount { get; private set; }

        public int DirtyRectCount { get; private set; }

        public PixelUploadRect FirstDirtyRect { get; private set; }

        public FogOfWarBuffer? FogOfWar { get; private set; }

        public uint FirstPixel { get; private set; }

        public uint ParticlePixel { get; private set; }

        public void Render(
            RenderBuffer renderBuffer,
            RenderAuxBuffers aux,
            CameraState camera,
            ReadOnlySpan<PixelUploadRect> dirtyRects,
            ReadOnlySpan<OverlayCommand> overlays,
            ReadOnlySpan<Particle> particles,
            MaterialTable materials,
            FogOfWarBuffer? fogOfWar,
            PixelEngine.Core.Diagnostics.FrameProfiler? profiler)
        {
            _ = camera;
            _ = dirtyRects;
            _ = overlays;
            _ = materials;
            _ = profiler;
            FrameCount++;
            Width = renderBuffer.Width;
            Height = renderBuffer.Height;
            AuxWidth = aux.Width;
            AuxHeight = aux.Height;
            ParticleCount = particles.Length;
            DirtyRectCount = dirtyRects.Length;
            FirstDirtyRect = dirtyRects.Length == 0 ? default : dirtyRects[0];
            FogOfWar = fogOfWar;
            FirstPixel = renderBuffer.Pixels[0];
            ParticlePixel = renderBuffer.Pixels[1];
        }
    }

    private sealed class RecordingScriptRuntime : IScriptRuntime
    {
        public int InitializeCount { get; private set; }

        public int BeginCount { get; private set; }

        public int UpdateCount { get; private set; }

        public int FixedCount { get; private set; }

        public int EndCount { get; private set; }

        public void Initialize(IScriptContext context)
        {
            InitializeCount++;
        }

        public void BeginFrame()
        {
            BeginCount++;
        }

        public void Update(float dt)
        {
            UpdateCount++;
        }

        public void FixedSimTick()
        {
            FixedCount++;
        }

        public void EndFrame()
        {
            EndCount++;
        }

        public void Shutdown()
        {
        }
    }

    private sealed class HostingLifecycleScript : Behaviour
    {
        public List<string> Events { get; set; } = [];

        protected override void OnStart()
        {
            Events.Add("start");
        }

        protected override void OnUpdate(float dt)
        {
            Events.Add("update");
        }

        protected override void OnFixedSimTick()
        {
            Events.Add("fixed");
        }

        protected override void OnDestroy()
        {
            Events.Add("destroy");
        }
    }

    private sealed class FakeScriptContext(ScriptScene scene) : IScriptContext
    {
        public IWorldCellAccess Cells => throw new NotSupportedException();

        public IMaterialQuery Materials => throw new NotSupportedException();

        public IParticleSpawner Particles => throw new NotSupportedException();

        public ISolidSampler Solids => throw new NotSupportedException();

        public IRigidBodyApi Bodies => throw new NotSupportedException();

        public ICharacterController Character => throw new NotSupportedException();

        public ICameraApi Camera => throw new NotSupportedException();

        public IInputApi Input => throw new NotSupportedException();

        public ILightingApi Lighting => throw new NotSupportedException();

        public IEventBus Events => NoopEventBus.Instance;

        public IAudioApi Audio => throw new NotSupportedException();

        public IGameTime Time => throw new NotSupportedException();

        public ScriptScene Scene { get; } = scene;
    }

    private sealed class NoopEventBus : IEventBus
    {
        public static NoopEventBus Instance { get; } = new();

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
            where TEvent : unmanaged
        {
            return NoopSubscription.Instance;
        }
    }

    private sealed class NoopSubscription : IDisposable
    {
        public static NoopSubscription Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
