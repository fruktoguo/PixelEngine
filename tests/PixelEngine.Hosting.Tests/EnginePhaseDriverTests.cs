using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Gui;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Time;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.World;
using Xunit;
using GameUi = PixelEngine.UI;
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
    /// 验证 Engine.AttachPhysics 会注册真实 PhysicsService，并把 PhysicsSystem 接入相位 8。
    /// </summary>
    [Fact]
    public void AttachPhysicsRegistersServiceAndRunsPhaseEight()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);

        PhysicsPhaseDriver driver = engine.AttachPhysics();
        _ = engine.RunOneTick();

        Assert.Same(driver, engine.Context.GetService<PhysicsPhaseDriver>());
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.PhysicsService));
        Assert.Same(engine.Context.GetService<PhysicsSystem>(), engine.Context.GetService<PhysicsSystem>());
        Assert.Equal(1, engine.Phases.Count(EnginePhase.PhysicsSync));
        Assert.Equal(0, engine.Context.GetService<PhysicsSystem>().PhysicsWorld.ActiveBodyCount);
    }

    /// <summary>
    /// 验证 RenderPhaseDriver 会在相位 9 构建 CPU render buffer，并在相位 10 把脚本相机、粒子与 fog-of-war 结果提交到渲染 sink。
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
        lighting.AddPointLight(16, 8, radius: 5, colorBgra: 0xFF_40_80_FF, intensity: 0.75f);
        lighting.RevealAround(16, 8, radius: 4, alpha: 192);
        ScriptLightingSynchronizer lightingSync = new(lighting, cameraSync);
        lightingSync.Sync();
        RecordingRenderFrameSink sink = new();
        DebugOverlayController overlays = new(new DebugOverlaySettings { Enabled = DebugOverlayFlags.ParticleTrails });
        ScriptOverlayApi scriptOverlays = new();
        scriptOverlays.SolidRectangle(2, 3, 4, 5, 0xFF_10_20_30);
        RenderPhaseDriver driver = new(
            chunks,
            materials,
            temperature,
            particles,
            cameraSync,
            lightingSync,
            sink,
            scriptOverlays: scriptOverlays,
            debugOverlays: overlays);

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
        Assert.Equal(1, engine.Phases.Count(EnginePhase.GpuUploadAndRender));
        Assert.Equal(1, sink.ParticleCount);
        Assert.Equal(1, sink.PointLightCount);
        Assert.True(sink.OverlayCount > 0);
        Assert.Contains(sink.Overlays, command => command.ColorBgra == 0xFF_10_20_30);
        Assert.Equal(1, sink.DirtyRectCount);
        Assert.Equal(new PixelUploadRect(0, 0, 32, 16), sink.FirstDirtyRect);
        Assert.NotNull(sink.FogOfWar);
        Assert.Equal(192, sink.FogOfWar.RevealAlpha(16, 8));
        Assert.Equal(0xFF_80_80_80u, sink.FirstPixel);
        Assert.Equal(0xFF_80_80_80u, sink.ParticlePixel);
    }

    /// <summary>
    /// 验证 GPU 粒子输出端接管粒子时，相位 9 不再把同一批粒子 CPU stamp 进 render buffer。
    /// </summary>
    [Fact]
    public void RenderPhaseDriverSkipsCpuParticleStampWhenSinkUsesGpuParticles()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("spark", CellType.Fire));
        Chunk chunk = new(new ChunkCoord(0, 0));
        TestChunkSource chunks = new(chunk);
        ParticleSystem particles = new(capacity: 16);
        Assert.True(particles.TrySpawn(new ParticleSpawn(1, 0, 0, 0, Material: 1, ColorVariant: 0, Life: 10)));
        TemperatureField temperature = new();
        ScriptCameraApi camera = new(viewportWidth: 32, viewportHeight: 16, centerX: 16, centerY: 8, zoom: 1);
        ScriptCameraSynchronizer cameraSync = new(camera);
        _ = cameraSync.Sync();
        ScriptLightingSynchronizer lightingSync = new(new ScriptLightingApi(), cameraSync);
        lightingSync.Sync();
        RecordingRenderFrameSink sink = new()
        {
            ParticleRenderMode = ParticleRenderMode.GpuPointSprite,
        };
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
        Assert.Equal(1, sink.ParticleCount);
        Assert.Equal(0u, sink.ParticlePixel);
    }

    /// <summary>
    /// 验证上一帧 CPU stamp 的粒子在 render-only 帧会触发 render buffer 重建，避免暂停或跳帧时留下粒子残影。
    /// </summary>
    [Fact]
    public void RenderPhaseDriverRefreshesRenderBufferAfterCpuParticleStamp()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("spark", CellType.Fire));
        Chunk chunk = new(new ChunkCoord(0, 0));
        TestChunkSource chunks = new(chunk);
        ParticleSystem particles = new(capacity: 16);
        Assert.True(particles.TrySpawn(new ParticleSpawn(1, 0, 0, 0, Material: 1, ColorVariant: 0, Life: 10)));
        TemperatureField temperature = new();
        ScriptCameraApi camera = new(viewportWidth: 32, viewportHeight: 16, centerX: 16, centerY: 8, zoom: 1);
        ScriptCameraSynchronizer cameraSync = new(camera);
        _ = cameraSync.Sync();
        ScriptLightingSynchronizer lightingSync = new(new ScriptLightingApi(), cameraSync);
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

        FrameTiming first = engine.RunOneTick();
        Assert.True(first.RunSim);
        Assert.Equal(0xFF_80_80_80u, sink.ParticlePixel);

        particles.Clear();
        engine.EnterEditMode();
        FrameTiming second = engine.RunOneTick();

        Assert.False(second.RunSim);
        Assert.Equal(2, sink.FrameCount);
        Assert.Equal(0u, sink.ParticlePixel);
    }

    /// <summary>
    /// 验证 render-only 帧中相机移动会强制重建 render buffer，避免世界画面与 overlay/玩家移动之间产生残影。
    /// </summary>
    [Fact]
    public void RenderPhaseDriverRefreshesRenderBufferWhenCameraMovesWithoutSimStep()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunk.Material[CellAddressing.LocalIndexFromLocal(0, 0)] = 1;
        TestChunkSource chunks = new(chunk);
        ParticleSystem particles = new(capacity: 16);
        TemperatureField temperature = new();
        ScriptCameraApi camera = new(viewportWidth: 2, viewportHeight: 1, centerX: 1, centerY: 0.5f, zoom: 1);
        ScriptCameraSynchronizer cameraSync = new(camera);
        _ = cameraSync.Sync();
        ScriptLightingSynchronizer lightingSync = new(new ScriptLightingApi(), cameraSync);
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

        FrameTiming first = engine.RunOneTick();
        Assert.True(first.RunSim);
        Assert.Equal(0xFF_80_80_80u, sink.FirstPixel);

        engine.EnterEditMode();
        camera.SetCenter(2, 0.5f);
        _ = cameraSync.Sync();
        FrameTiming second = engine.RunOneTick();

        Assert.False(second.RunSim);
        Assert.Equal(2, sink.FrameCount);
        Assert.Equal(0u, sink.FirstPixel);
    }

    /// <summary>
    /// 验证 CA iteration 调试叠层只显示本帧实际迭代区域，resident sleeping chunk 不产生迭代矩形。
    /// </summary>
    [Fact]
    public void RenderDebugOverlayShowsNoCaIterationRectForSleepingChunk()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
        Chunk active = new(new ChunkCoord(0, 0));
        active.Material[CellAddressing.LocalIndexFromLocal(1, 1)] = 1;
        active.SetCurrentDirty(new DirtyRect(1, 1, 1, 1));
        Chunk sleeping = new(new ChunkCoord(2, 0));
        sleeping.Material[CellAddressing.LocalIndexFromLocal(1, 1)] = 1;
        TestChunkSource chunks = new(active, sleeping);
        MaterialPropsTable props = new(materials.Hot);
        CellGrid grid = new(chunks, props);
        SimulationKernel kernel = new(chunks, props);
        ParticleSystem particles = new(capacity: 16);
        TemperatureField temperature = new();
        ScriptCameraApi camera = new(viewportWidth: 192, viewportHeight: 64, centerX: 96, centerY: 32, zoom: 1);
        ScriptCameraSynchronizer cameraSync = new(camera);
        _ = cameraSync.Sync();
        ScriptLightingSynchronizer lightingSync = new(new ScriptLightingApi(), cameraSync);
        lightingSync.Sync();
        RecordingRenderFrameSink sink = new();
        DebugOverlayController overlays = new(new DebugOverlaySettings { Enabled = DebugOverlayFlags.CaIterationRects });
        RenderPhaseDriver render = new(
            chunks,
            materials,
            temperature,
            particles,
            cameraSync,
            lightingSync,
            sink,
            kernel: kernel,
            debugOverlays: overlays);

        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddPhaseDriver(new SimulationPhaseDriver(chunks, grid, kernel, particles, temperature, materials))
            .AddPhaseDriver(render)
            .Build();

        _ = engine.RunOneTick();

        Assert.Equal(ChunkState.Sleeping, sleeping.State);
        Assert.Contains(sink.Overlays, static command =>
            command.PrimitiveType == OverlayPrimitiveType.OutlineRectangle &&
            command.ColorBgra == 0xD000FF40u &&
            command.ViewportX == 1f &&
            command.ViewportY == 1f);
        Assert.DoesNotContain(sink.Overlays, static command =>
            command.PrimitiveType == OverlayPrimitiveType.OutlineRectangle &&
            command.ColorBgra == 0xD000FF40u &&
            command.ViewportX >= 128f);
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
    /// 验证 headless 固定步数驱动会运行显式接入的真实 Physics 相位。
    /// </summary>
    [Fact]
    public void HeadlessTicksDriveAttachedPhysicsPhase()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .WithWorkerCount(1)
            .Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);
        _ = engine.AttachPhysics();

        engine.RunHeadlessTicks(2);

        Assert.Equal(2, engine.Context.Clock.FrameIndex);
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.PhysicsService));
        Assert.Equal(1, engine.Phases.Count(EnginePhase.PhysicsSync));
        Assert.Equal(0, engine.Context.GetService<PhysicsSystem>().PhysicsWorld.ActiveBodyCount);
    }

    /// <summary>
    /// 验证 Hosting 相位 8 会自动 flush 脚本刚体命令，再执行真实 Physics step。
    /// </summary>
    [Fact]
    public void PhysicsPhaseFlushesScriptBodyCommandsBeforeStep()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .WithWorkerCount(1)
            .Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);
        _ = engine.AttachPhysics();

        CellGrid grid = engine.Context.GetService<CellGrid>();
        for (int y = 8; y < 24; y++)
        {
            for (int x = 8; x < 24; x++)
            {
                grid.MaterialAt(x, y) = 1;
                grid.FlagsAt(x, y) = 0;
                grid.LifetimeAt(x, y) = 0;
            }
        }

        ScriptSimulationContext scripts = new(
            new ScriptScene(),
            grid,
            engine.Context.GetService<SimulationKernel>(),
            engine.Context.GetService<ParticleSystem>(),
            materials,
            physics: engine.Context.GetService<PhysicsSystem>());
        engine.Context.RegisterService(scripts);

        BodyHandle handle = scripts.Bodies.CreateFromRegion(8, 8, 16, 16);
        Assert.False(scripts.Bodies.TryGetTransform(handle, out _));
        Assert.Equal(0, engine.Context.GetService<PhysicsSystem>().PhysicsWorld.ActiveBodyCount);

        engine.RunHeadlessTicks(1);

        Assert.Equal(1, engine.Context.GetService<PhysicsSystem>().PhysicsWorld.ActiveBodyCount);
        Assert.True(scripts.Bodies.TryGetTransform(handle, out BodyTransform transform));
        Assert.InRange(transform.X, 15.9f, 16.1f);
        Assert.InRange(transform.Y, 15.9f, 16.1f);
        Assert.Equal(1, engine.Context.Counters.RigidBodies);
        Assert.True(CellFlags.Has(grid.FlagsAt(16, 16), CellFlags.RigidOwned));
    }

    /// <summary>
    /// 验证 Hosting 相位 8 会自动 flush 脚本角色移动命令。
    /// </summary>
    [Fact]
    public void PhysicsPhaseFlushesScriptCharacterMoveCommands()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .WithWorkerCount(1)
            .Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);
        _ = engine.AttachPhysics();

        CellGrid grid = engine.Context.GetService<CellGrid>();
        for (int x = 0; x < 32; x++)
        {
            grid.MaterialAt(x, 10) = 1;
            grid.FlagsAt(x, 10) = 0;
            grid.LifetimeAt(x, 10) = 0;
        }

        ScriptSimulationContext scripts = new(
            new ScriptScene(),
            grid,
            engine.Context.GetService<SimulationKernel>(),
            engine.Context.GetService<ParticleSystem>(),
            materials,
            physics: engine.Context.GetService<PhysicsSystem>());
        engine.Context.RegisterService(scripts);

        CharacterHandle handle = scripts.Character.Create(4, 0, 4, 4);
        CharacterState pending = scripts.Character.Move(handle, 0, 20);
        Assert.Equal(0f, pending.Y);

        engine.RunHeadlessTicks(1);

        CharacterState state = scripts.Character.GetState(handle);
        Assert.True(state.OnGround);
        Assert.Equal(6f, state.Y);
        Assert.Equal(20f, state.RequestedDeltaY);
        Assert.Equal(6f, state.AppliedDeltaY);
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
    /// 验证脚本 Update 使用真实渲染间隔，并在慢帧下限制到一个固定逻辑步，避免控制速度随渲染帧率漂移。
    /// </summary>
    [Fact]
    public void ScriptingPhaseDriverUsesWallClockDeltaClampedToFixedStep()
    {
        RecordingScriptRuntime runtime = new();
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddPhaseDriver(new ScriptingPhaseDriver(runtime, new FakeScriptContext(new ScriptScene())))
            .Build();

        _ = engine.RunOneTick(realDeltaSeconds: 1.0 / 120.0);
        Assert.Equal(1f / 120f, runtime.LastUpdateDt, precision: 4);

        _ = engine.RunOneTick(realDeltaSeconds: 1.0 / 30.0);
        Assert.Equal(1f / 60f, runtime.LastUpdateDt, precision: 4);
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
    /// 验证 Game UI 相位使用渲染帧 dt 逐帧推进，并在 sim 降频帧仍 drain UI 事件。
    /// </summary>
    [Fact]
    public void GameUiPhaseDriverUpdatesEveryRenderFrameAndDrainsEvents()
    {
        RecordingGameUiBackend backend = new();
        using GameUi.GameUiHost host = new(backend);
        host.Initialize(new GameUi.UiBackendInitializeInfo(new GameUi.UiViewport(0, 0, 320, 180, 1f), GameUi.UiBackendKind.ManagedFallback));
        RecordingGameUiEventSink sink = new();
        GameUiPhaseDriver driver = new(host, eventCapacity: 4, sink);
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithSimHz(PixelEngine.Core.EngineConstants.SimHzDownscaled)
            .AddPhaseDriver(driver)
            .Build();

        FrameTiming first = engine.RunOneTick(realDeltaSeconds: 1.0 / 120.0);
        FrameTiming second = engine.RunOneTick(realDeltaSeconds: 1.0 / 30.0);

        Assert.True(first.RunSim);
        Assert.False(second.RunSim);
        Assert.Equal(2, backend.UpdateCount);
        Assert.Equal(1.0f / 30.0f, backend.LastDeltaSeconds, precision: 4);
        Assert.Equal(1.0f / 30.0f, driver.LastDeltaSeconds, precision: 4);
        Assert.Equal(2, driver.TotalDrainedEventCount);
        Assert.Equal(2, sink.TotalEventCount);
        Assert.Equal(new GameUi.UiActionId(9), sink.LastAction);
        Assert.True(engine.Context.Profiler.LastSubFrame[(int)FrameSubPhase.UiUpdate] > 0);
        Assert.Equal(1, engine.Phases.Count(EnginePhase.GameLogicAndScripts));
    }

    /// <summary>
    /// 验证退出 Editor Play Session 会销毁已启动 Behaviour，并允许再次进入 Play 时重新启动。
    /// </summary>
    [Fact]
    public void EditorPlaySessionExitEndsScriptPlayLifecycle()
    {
        ScriptScene scene = new();
        Entity entity = scene.CreateEntity();
        HostingLifecycleScript script = entity.AddComponent<HostingLifecycleScript>();
        List<string> events = [];
        script.Events = events;
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        engine.AttachScripting(new FakeScriptContext(scene), new ScriptRuntime());
        EngineEditorPlaySessionService service = new(engine);

        _ = service.EnterPlayCurrent();
        _ = engine.RunOneTick();
        EditorPlaySessionResult exit = service.ExitPlay();
        _ = service.EnterPlayCurrent();
        _ = engine.RunOneTick();

        Assert.True(exit.Succeeded);
        Assert.Equal(EngineExecutionMode.Play, engine.Mode);
        Assert.Equal(["start", "update", "fixed", "destroy", "start", "update", "fixed"], events);
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

    /// <summary>
    /// 验证 Engine 关闭时会释放已接入的脚本运行时，避免热重载控制器和可回收 ALC 泄漏。
    /// </summary>
    [Fact]
    public void EngineShutdownDisposesAttachedScriptRuntime()
    {
        RecordingScriptRuntime runtime = new();
        FakeScriptContext context = new(new ScriptScene());
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();

        engine.AttachScripting(context, runtime);
        engine.Shutdown();

        Assert.Equal(1, runtime.ShutdownCount);
        Assert.Equal(EngineRunState.Shutdown, engine.State);
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
        public ParticleRenderMode ParticleRenderMode { get; init; } = ParticleRenderMode.CpuStamp;

        public int FrameCount { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public int AuxWidth { get; private set; }

        public int AuxHeight { get; private set; }

        public int ParticleCount { get; private set; }

        public int PointLightCount { get; private set; }

        public int OverlayCount { get; private set; }

        public OverlayCommand[] Overlays { get; private set; } = [];

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
            ReadOnlySpan<LightSource> pointLights,
            ReadOnlySpan<Particle> particles,
            MaterialTable materials,
            FogOfWarBuffer? fogOfWar,
            PixelEngine.Core.Diagnostics.FrameProfiler? profiler)
        {
            _ = camera;
            _ = dirtyRects;
            _ = materials;
            _ = profiler;
            FrameCount++;
            Width = renderBuffer.Width;
            Height = renderBuffer.Height;
            AuxWidth = aux.Width;
            AuxHeight = aux.Height;
            PointLightCount = pointLights.Length;
            OverlayCount = overlays.Length;
            Overlays = overlays.ToArray();
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

        public float LastUpdateDt { get; private set; }

        public int FixedCount { get; private set; }

        public int GuiCount { get; private set; }

        public int EndCount { get; private set; }

        public int EndPlaySessionCount { get; private set; }

        public int CapturePlaySessionSnapshotCount { get; private set; }

        public int RestorePlaySessionSnapshotCount { get; private set; }

        public int ShutdownCount { get; private set; }

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
            LastUpdateDt = dt;
        }

        public void FixedSimTick()
        {
            FixedCount++;
        }

        public void DrawGui(IGuiContext gui)
        {
            ArgumentNullException.ThrowIfNull(gui);
            GuiCount++;
        }

        public void EndFrame()
        {
            EndCount++;
        }

        public void EndPlaySession()
        {
            EndPlaySessionCount++;
        }

        public ScriptPlaySessionSnapshot CapturePlaySessionSnapshot()
        {
            CapturePlaySessionSnapshotCount++;
            ScriptRuntime runtime = new();
            runtime.Initialize(new FakeScriptContext(new ScriptScene()));
            return runtime.CapturePlaySessionSnapshot();
        }

        public void RestorePlaySessionSnapshot(ScriptPlaySessionSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            RestorePlaySessionSnapshotCount++;
        }

        public void Shutdown()
        {
            ShutdownCount++;
        }
    }

    private sealed class RecordingGameUiBackend : GameUi.IGameUiBackend
    {
        private int _nextDocumentHandle = 1;

        public GameUi.UiBackendKind Kind => GameUi.UiBackendKind.ManagedFallback;

        public bool IsDirty => false;

        public bool IsAnimating => false;

        public int UpdateCount { get; private set; }

        public float LastDeltaSeconds { get; private set; }

        public void Initialize(in GameUi.UiBackendInitializeInfo info)
        {
            info.Viewport.Validate();
        }

        public void Resize(in GameUi.UiViewport viewport)
        {
            viewport.Validate();
        }

        public GameUi.UiDocumentHandle LoadDocument(in GameUi.UiDocumentSource source)
        {
            _ = source;
            return new GameUi.UiDocumentHandle(_nextDocumentHandle++);
        }

        public void UnloadDocument(GameUi.UiDocumentHandle document)
        {
            document.Validate();
        }

        public void SetScreenStack(ReadOnlySpan<GameUi.UiScreenStackEntry> stack)
        {
            _ = stack;
        }

        public void Update(float deltaSeconds)
        {
            UpdateCount++;
            LastDeltaSeconds = deltaSeconds;
        }

        public void FeedPointerMove(float x, float y)
        {
            _ = x;
            _ = y;
        }

        public void FeedPointerButton(GameUi.UiPointerButton button, bool isDown)
        {
            _ = button;
            _ = isDown;
        }

        public void FeedScroll(float deltaX, float deltaY)
        {
            _ = deltaX;
            _ = deltaY;
        }

        public void FeedKey(GameUi.UiKey key, bool isDown, GameUi.UiKeyModifiers modifiers)
        {
            _ = key;
            _ = isDown;
            _ = modifiers;
        }

        public void FeedText(ReadOnlySpan<char> text)
        {
            _ = text;
        }

        public GameUi.UiHitResult HitTest(float x, float y)
        {
            _ = x;
            _ = y;
            return GameUi.UiHitResult.None;
        }

        public void SetModelValue(GameUi.UiDocumentHandle document, GameUi.UiPathId path, in GameUi.UiValue value)
        {
            _ = document;
            _ = path;
            _ = value;
        }

        public bool TryGetModelValue(GameUi.UiDocumentHandle document, GameUi.UiPathId path, out GameUi.UiValue value)
        {
            _ = document;
            _ = path;
            value = default;
            return false;
        }

        public int CopyModelPaths(GameUi.UiDocumentHandle document, Span<GameUi.UiPathId> destination)
        {
            _ = document;
            _ = destination;
            return 0;
        }

        public int DrainEvents(Span<GameUi.UiEvent> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            destination[0] = new GameUi.UiEvent(new GameUi.UiDocumentHandle(1), new GameUi.UiElementId(7), new GameUi.UiActionId(9), new GameUi.UiValue(UpdateCount));
            return 1;
        }

        public void Composite(in UiPresentContext context)
        {
            _ = context;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingGameUiEventSink : IGameUiEventSink
    {
        public int TotalEventCount { get; private set; }

        public GameUi.UiActionId LastAction { get; private set; }

        public void OnGameUiEvents(ReadOnlySpan<GameUi.UiEvent> events)
        {
            TotalEventCount += events.Length;
            if (!events.IsEmpty)
            {
                LastAction = events[^1].Action;
            }
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

        public IWorldEffects World => throw new NotSupportedException();

        public IMaterialQuery Materials => throw new NotSupportedException();

        public IParticleSpawner Particles => throw new NotSupportedException();

        public ISolidSampler Solids => throw new NotSupportedException();

        public IRigidBodyApi Bodies => throw new NotSupportedException();

        public ICharacterController Character => throw new NotSupportedException();

        public ICameraApi Camera => throw new NotSupportedException();

        public IInputApi Input => throw new NotSupportedException();

        public ILightingApi Lighting => throw new NotSupportedException();

        public IDiagnosticsApi Diagnostics => throw new NotSupportedException();

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
