using PixelEngine.Core;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Time;
using PixelEngine.Editor;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.World;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Hosting 过载降级编排测试。
/// </summary>
public sealed class EngineOverloadControllerTests
{
    /// <summary>
    /// 验证连续超预算会按架构 §4.3 的五级顺序升级质量档位。
    /// </summary>
    [Fact]
    public void OverloadControllerEscalatesQualityTierInOrder()
    {
        EngineOverloadController controller = new(new EngineOverloadOptions(frameBudgetMs: 10, sustainWindow: 2));

        Assert.Equal(EngineQualityTier.Full, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.ReducedThermal, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.ReducedThermal, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.ReducedLighting, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.ReducedLighting, controller.SubmitFrame(0));
        controller.ResetToFullQuality();

        Assert.Equal(EngineQualityTier.Full, controller.QualityTier);
    }

    /// <summary>
    /// 验证完整五级降级链最终封顶在 SlowMotion，不继续追帧或产生额外档位。
    /// </summary>
    [Fact]
    public void OverloadControllerEscalatesThroughAllFiveTiersAndCapsAtSlowMotion()
    {
        EngineOverloadController controller = new(new EngineOverloadOptions(frameBudgetMs: 10, sustainWindow: 1));

        Assert.Equal(EngineQualityTier.ReducedThermal, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.ReducedLighting, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.DistantChunkThrottle, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.Sim30Hz, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.SlowMotion, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.SlowMotion, controller.SubmitFrame(11));
    }

    /// <summary>
    /// 验证 Engine 在降级到 Sim30Hz 后通过 FrameClock 下发 sim 降频。
    /// </summary>
    [Fact]
    public void EngineAppliesSim30HzQualityTierToFrameClock()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithOverloadPolicy(frameBudgetMs: 10, sustainWindow: 1)
            .Build();

        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);

        Assert.Equal(EngineQualityTier.Sim30Hz, engine.Context.QualityTier);
        Assert.Equal(EngineConstants.SimHzDownscaled, engine.Context.Clock.SimHz);
        Assert.Equal(EngineConstants.SimHzDownscaled, engine.Context.Counters.SimHz);
    }

    /// <summary>
    /// 验证一级过载降级会真实下发温度场降频，并在恢复全质量时复位。
    /// </summary>
    [Fact]
    public void EngineAppliesReducedThermalQualityTierToTemperatureField()
    {
        TemperatureField temperature = new();
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithOverloadPolicy(frameBudgetMs: 10, sustainWindow: 1)
            .Build();
        engine.Context.RegisterService(temperature);

        _ = engine.RunOneTick(realDeltaSeconds: 0.011);

        Assert.Equal(EngineQualityTier.ReducedThermal, engine.Context.QualityTier);
        Assert.Equal(4, temperature.StepInterval);
        Assert.True(temperature.ShouldRun(0));
        Assert.False(temperature.ShouldRun(1));
        Assert.True(temperature.ShouldRun(4));

        engine.Context.GetService<EngineOverloadController>().ResetToFullQuality();
        _ = engine.RunOneTick(realDeltaSeconds: 0);

        Assert.Equal(EngineQualityTier.Full, engine.Context.QualityTier);
        Assert.Equal(1, temperature.StepInterval);
        Assert.True(temperature.ShouldRun(1));
    }

    /// <summary>
    /// 验证人工过载降到 30Hz 后，render/streaming 相位仍逐帧执行且 sim 不追帧。
    /// </summary>
    [Fact]
    public void OverloadedSim30HzKeepsRenderFramesWithoutCatchUp()
    {
        List<EnginePhase> phases = [];
        EngineBuilder builder = new EngineBuilder()
            .WithWorkerCount(1)
            .WithOverloadPolicy(frameBudgetMs: 10, sustainWindow: 1);
        RegisterAllPhases(builder, phases);
        using Engine engine = builder.Build();

        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        Assert.Equal(EngineQualityTier.Sim30Hz, engine.Context.QualityTier);

        phases.Clear();
        FrameTiming simFrame = engine.RunOneTick(realDeltaSeconds: 0);
        long simTicksAfterSimFrame = engine.Context.Clock.SimTickIndex;
        phases.Clear();
        FrameTiming renderOnlyFrame = engine.RunOneTick(realDeltaSeconds: 0);

        Assert.True(simFrame.RunSim);
        Assert.False(renderOnlyFrame.RunSim);
        Assert.Equal(simTicksAfterSimFrame, engine.Context.Clock.SimTickIndex);
        Assert.Equal(
            [
                EnginePhase.InputAndTime,
                EnginePhase.GameLogicAndScripts,
                EnginePhase.BuildRenderBuffer,
                EnginePhase.GpuUploadAndRender,
                EnginePhase.WorldStreaming,
            ],
            phases);
    }

    /// <summary>
    /// 验证 Hosting 在进入光照降级层级后会调用已注册的 GPU compute 降级后端。
    /// </summary>
    [Fact]
    public void EngineAppliesGpuComputeDegradationWhenReducedLightingTierIsReached()
    {
        RecordingGpuComputeQualityDegrader degrader = new();
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithOverloadPolicy(frameBudgetMs: 10, sustainWindow: 1)
            .Build();
        engine.Context.RegisterService<IGpuComputeQualityDegrader>(degrader);

        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        Assert.Equal(0, degrader.CallCount);

        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        Assert.Equal(1, degrader.CallCount);
        Assert.Equal(EngineQualityTier.ReducedLighting, engine.Context.QualityTier);

        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        Assert.Equal(2, degrader.CallCount);
        Assert.Equal(EngineQualityTier.DistantChunkThrottle, engine.Context.QualityTier);
    }

    /// <summary>
    /// 验证三级过载降级会从 World 相机下发远区 chunk 隔帧降频策略。
    /// </summary>
    [Fact]
    public void EngineAppliesDistantChunkThrottleFromWorldCamera()
    {
        string worldPath = Path.Combine(Path.GetTempPath(), $"pixelengine-throttle-{Guid.NewGuid():N}");
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
            TemperatureField temperature = new();
            WorldManager world = new(
                new WorldCamera(32, 32, 64, 64),
                temperature,
                materials,
                worldPath,
                fallbackMaterialId: 0,
                new WorldStreamingConfig { ActivationMarginChunks = 2, BorderRingWidth = 1 });
            AddDenseChunks(world.Chunks, -1, -1, 3, 2);
            Chunk near = RequireChunk(world.Chunks, new ChunkCoord(0, 0));
            Chunk far = RequireChunk(world.Chunks, new ChunkCoord(2, 0));
            MaterialPropsTable props = new(materials.Hot);
            CellGrid grid = new(world.Chunks, props);
            SimulationKernel kernel = new(world.Chunks, props);
            ParticleSystem particles = new(capacity: 16);

            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .WithOverloadPolicy(frameBudgetMs: 10, sustainWindow: 1)
                .AddPhaseDriver(new SimulationPhaseDriver(world.Chunks, grid, kernel, particles, temperature, materials))
                .OnPhase(EnginePhase.GameLogicAndScripts, _ =>
                {
                    Set(near, 10, 0, 1);
                    near.SetCurrentDirty(DirtyRect.Full);
                    Set(far, 10, 0, 1);
                    far.SetCurrentDirty(DirtyRect.Full);
                })
                .Build();
            engine.Context.RegisterService(world);

            _ = engine.RunOneTick(realDeltaSeconds: 0.020);
            _ = engine.RunOneTick(realDeltaSeconds: 0.020);
            _ = engine.RunOneTick(realDeltaSeconds: 0.020);

            Assert.Equal(EngineQualityTier.DistantChunkThrottle, engine.Context.QualityTier);
            CaIterationSnapshot[] iterations = new CaIterationSnapshot[16];
            int count = kernel.CopyCaIterationSnapshots(iterations);
            Assert.True(ContainsIteration(iterations, count, near.Coord));
            Assert.False(ContainsIteration(iterations, count, far.Coord));
            Assert.Equal(1, far.Material[CellAddressing.LocalIndexFromLocal(10, 0)]);
            Assert.Equal(DirtyRect.Full, far.CurrentDirty);
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
    /// 验证过载控制器通过 EngineContext 服务表暴露给脚本服务后端与 Editor。
    /// </summary>
    [Fact]
    public void OverloadControllerIsRegisteredAsRuntimeService()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();

        EngineOverloadController overload = engine.Context.GetService<EngineOverloadController>();

        Assert.Same(overload, engine.Context.GetService<EngineOverloadController>());
        Assert.Equal(EngineQualityTier.Full, overload.QualityTier);
    }

    /// <summary>
    /// 验证 Editor runtime diagnostics 从 Hosting 上下文读取真实时间膨胀与降级状态。
    /// </summary>
    [Fact]
    public void EditorRuntimeDiagnosticsProviderMapsClockAndQualityTier()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithOverloadPolicy(frameBudgetMs: 10, sustainWindow: 1)
            .Build();

        _ = engine.RunOneTick(realDeltaSeconds: 0.020);

        EditorRuntimeDiagnostics diagnostics = EditorRuntimeDiagnosticsProvider.Create(engine.Context);

        Assert.True(diagnostics.TimeScale < 1.0);
        Assert.Equal((int)EngineQualityTier.ReducedThermal, diagnostics.DegradationLevel);
        Assert.Equal("ReducedThermal", diagnostics.DegradationName);
        Assert.Equal(1, diagnostics.ConsecutiveOverBudgetFrames);
    }

    /// <summary>
    /// 验证脚本诊断 API 从 Hosting 计数器导出 HUD 需要的核心指标。
    /// </summary>
    [Fact]
    public void ScriptDiagnosticsApiCapturesHudCounters()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        engine.Context.Counters.ActiveChunks = 3;
        engine.Context.Counters.ResidentChunks = 5;
        engine.Context.Counters.FreeParticles = 7;
        engine.Context.Counters.RigidBodies = 2;
        engine.Context.Counters.SimHz = 60;
        _ = engine.RunOneTick(realDeltaSeconds: 0.02);

        EngineScriptDiagnosticsApi api = new(engine.Context.Counters, engine.Context.Clock, new DebugOverlaySettings());
        EngineDiagnosticsSnapshot snapshot = api.Capture();

        Assert.Equal(engine.Context.Clock.FrameIndex, snapshot.FrameCount);
        Assert.Equal(50f, snapshot.FramesPerSecond, precision: 2);
        Assert.Equal(20f, snapshot.FrameMilliseconds, precision: 2);
        Assert.Equal(20f, snapshot.FrameP99Milliseconds, precision: 2);
        Assert.Equal(50f, snapshot.FrameLow1PercentFps, precision: 2);
        Assert.Equal(1, snapshot.FrameSampleCount);
        Assert.Equal(60f, snapshot.SimHz);
        Assert.Equal(3, snapshot.ActiveChunks);
        Assert.Equal(5, snapshot.ResidentChunks);
        Assert.Equal(7, snapshot.FreeParticles);
        Assert.Equal(2, snapshot.RigidBodies);
        Assert.False(api.IsOverlayEnabled(DebugOverlayKind.DirtyRects));
        Assert.True(api.ToggleOverlay(DebugOverlayKind.DirtyRects));
        Assert.True(api.IsOverlayEnabled(DebugOverlayKind.DirtyRects));
        api.SetOverlay(DebugOverlayKind.DirtyRects, enabled: false);
        Assert.False(api.IsOverlayEnabled(DebugOverlayKind.DirtyRects));
        Assert.False(api.IsOverlayEnabled(DebugOverlayKind.CaIterationRects));
        Assert.True(api.ToggleOverlay(DebugOverlayKind.CaIterationRects));
        Assert.True(api.IsOverlayEnabled(DebugOverlayKind.CaIterationRects));
    }

    /// <summary>
    /// 验证渲染帧率诊断使用多帧窗口统计平均值、p99 与 1% low，而非只按最后一帧反推。
    /// </summary>
    [Fact]
    public void ScriptDiagnosticsApiCapturesWindowedFrameRateStatistics()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();

        _ = engine.RunOneTick(realDeltaSeconds: 0.010);
        _ = engine.RunOneTick(realDeltaSeconds: 0.020);
        _ = engine.RunOneTick(realDeltaSeconds: 0.030);

        EngineScriptDiagnosticsApi api = new(engine.Context.Counters, engine.Context.Clock, new DebugOverlaySettings());
        EngineDiagnosticsSnapshot snapshot = api.Capture();

        Assert.Equal(3, snapshot.FrameSampleCount);
        Assert.Equal(50f, snapshot.FramesPerSecond, precision: 2);
        Assert.Equal(20f, snapshot.FrameMilliseconds, precision: 2);
        Assert.Equal(30f, snapshot.FrameLastMilliseconds, precision: 2);
        Assert.Equal(30f, snapshot.FrameP99Milliseconds, precision: 2);
        Assert.Equal(33.333f, snapshot.FrameLow1PercentFps, precision: 2);
        Assert.True(snapshot.FrameJitterMilliseconds > 8f);
    }

    /// <summary>
    /// 验证脚本运行时控制 API 真实驱动 Engine Play/Edit 模式与关闭请求。
    /// </summary>
    [Fact]
    public void ScriptRuntimeControlApiControlsEngineModeAndShutdownRequest()
    {
        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .WithWorkerCount(1)
            .Build();
        EngineScriptRuntimeControlApi api = new(engine);

        RuntimeControlSnapshot initial = api.Capture();
        Assert.True(initial.IsPlaying);
        Assert.False(initial.IsShutdownRequested);

        api.PauseSimulation();
        Assert.Equal(EngineExecutionMode.Edit, engine.Mode);
        Assert.False(api.Capture().IsPlaying);

        api.ResumeSimulation();
        Assert.Equal(EngineExecutionMode.Play, engine.Mode);
        Assert.True(api.Capture().IsPlaying);

        RuntimeControlResult quit = api.RequestShutdown();
        Assert.True(quit.Success);
        Assert.True(engine.IsShutdownRequested);
        Assert.True(api.Capture().IsShutdownRequested);

        RuntimeControlResult editor = api.OpenEditor();
        Assert.False(editor.Success);
        Assert.Contains("headless", editor.Message, StringComparison.Ordinal);

        RuntimeControlResult restart = api.RequestRestartCurrentScene();
        Assert.False(restart.Success);
        Assert.Contains("重开关卡", restart.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证脚本请求打开 Editor 时不会在未接入窗口渲染桥的进程里伪造成功。
    /// </summary>
    [Fact]
    public void ScriptRuntimeControlApiReportsEditorUnavailableBeforeWindowRuntime()
    {
        using Engine engine = new EngineBuilder()
            .EnableEditor()
            .WithWorkerCount(1)
            .Build();
        EngineScriptRuntimeControlApi api = new(engine);

        RuntimeControlResult result = api.OpenEditor();

        Assert.False(result.Success);
        Assert.Contains("DockSpace", result.Message, StringComparison.Ordinal);
    }

    private static void RegisterAllPhases(EngineBuilder builder, List<EnginePhase> phases)
    {
        for (int i = 0; i < 12; i++)
        {
            EnginePhase phase = (EnginePhase)i;
            _ = builder.OnPhase(phase, context => phases.Add(context.Phase));
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
                Density = i == 0 ? (byte)0 : (byte)120,
                HeatCapacity = 1,
                HeatConduct = 0,
                TextureId = -1,
                BaseColorBGRA = i == 0 ? 0 : 0xFF_80_80_80u,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            };
        }

        return new MaterialTable(materials);
    }

    private static void AddDenseChunks(ResidentChunkMap chunks, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                chunks.Add(new Chunk(new ChunkCoord(x, y)));
            }
        }
    }

    private static Chunk RequireChunk(ResidentChunkMap chunks, ChunkCoord coord)
    {
        return chunks.TryGetChunk(coord, out Chunk chunk)
            ? chunk
            : throw new InvalidOperationException($"测试缺少 chunk {coord}。");
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static bool ContainsIteration(ReadOnlySpan<CaIterationSnapshot> iterations, int count, ChunkCoord coord)
    {
        for (int i = 0; i < count; i++)
        {
            if (iterations[i].Coord == coord)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class RecordingGpuComputeQualityDegrader : IGpuComputeQualityDegrader
    {
        public int CallCount { get; private set; }

        public bool DegradeGpuComputeOneStep()
        {
            CallCount++;
            return true;
        }
    }
}
