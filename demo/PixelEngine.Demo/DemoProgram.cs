using System.Diagnostics;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using Silk.NET.OpenGL;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 启动入口实现。
/// </summary>
public static class DemoProgram
{
    private const int DemoWorldWidthCells = 640;
    private const int DemoWorldHeightCells = 360;
    private const int DemoParticleCapacityDefault = 32_768;
    private const int PlayableInternalWidth = 720;
    private const int PlayableInternalHeight = 480;
    private const int PlayableWindowWidth = 1080;
    private const int PlayableWindowHeight = 720;

    /// <summary>
    /// 执行 Demo 主入口。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码。</returns>
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CameraFollow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DemoHud))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ExplosiveTool))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(GoalTrigger))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LevelDirector))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MaterialBrush))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MaterialEmitter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PauseMenu))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayableHud))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayableProjectileTool))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayableWorldDirector))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayerController))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayerHealth))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayerVisual))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SparkEmitter))]
    public static int Execute(string[] args)
    {
        DemoStartupOptions? options = null;
        try
        {
            options = DemoStartupOptions.Parse(args);
            Run(options);
            return 0;
        }
        catch (Exception exception)
        {
            string path = WriteCrashLog(exception, options?.LogDirectory);
            Console.Error.WriteLine($"Demo 启动失败，异常已写入：{path}");
            return 1;
        }
    }

    /// <summary>
    /// 按给定参数构造并运行 Demo。
    /// </summary>
    /// <param name="options">启动参数。</param>
    public static void Run(DemoStartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Assembly assembly = typeof(DemoProgram).Assembly;
        AssemblyName name = assembly.GetName();
        Console.WriteLine($"{name.Name} {name.Version?.ToString() ?? "0.0.0.0"}");
        Console.WriteLine($"RID: {RuntimeInformation.RuntimeIdentifier}");

        EngineProject project = BuildProject(options);
        using Engine engine = BuildEngine(options, project);
        engine.RegisterProceduralWorldGenerator(PlayableCavernWorldGenerator.Key, new PlayableCavernWorldGenerator());
        bool contentLoaded = false;
        if (engine.HasContentPackage())
        {
            EngineContentPackage package = engine.LoadContentPackage();
            int particleCapacity = options.ParticleFrameProbe
                ? Math.Max(DemoParticleCapacityDefault, options.ParticleProbeCount)
                : DemoParticleCapacityDefault;
            object? worldLoad = engine.AttachCurrentSceneWorld(particleCapacity);
            if (worldLoad is not null)
            {
                Console.WriteLine("世界存档已加载。");
            }
            else
            {
                _ = engine.AttachResidentSimulationWorld(DemoWorldWidthCells, DemoWorldHeightCells, particleCapacity);
            }

            _ = engine.AttachPhysics();
            int audioClips = engine.AttachAudioFromContentAsync().AsTask().GetAwaiter().GetResult();
            contentLoaded = true;
            Console.WriteLine($"内容包已加载：{package.MaterialCount} 个材质，{package.ReactionCount} 条反应，{audioClips} 个音频 clip，Physics 已接入。");
        }
        else
        {
            Console.WriteLine("内容包尚未就绪：缺少 materials.json 或 reactions.json，本次仅执行宿主启动冒烟。");
        }

        engine.RegisterScriptAssembly(assembly);
        Console.WriteLine(options.HotReloadEnabled
            ? "脚本程序集已注册；热重载等待脚本宿主装配。"
            : "脚本程序集已注册；热重载已由参数关闭。");
        if (contentLoaded)
        {
            PixelEngine.Scripting.ScriptHotReloadRuntimeOptions? hotReload = CreateHotReloadOptions(
                options,
                RuntimeFeature.IsDynamicCodeSupported);
            _ = engine.AttachScriptingFromServices(hotReload: hotReload);
            if (options.HotReloadEnabled && !RuntimeFeature.IsDynamicCodeSupported)
            {
                Console.WriteLine("脚本热重载未启用：当前运行时不支持动态代码（NativeAOT 通道显式降级）。");
            }
            else if (options.HotReloadEnabled && hotReload is null)
            {
                Console.WriteLine("脚本热重载未启用：未找到 Demo 源码目录。");
            }

            Console.WriteLine("脚本运行时已接入 Hosting/Simulation 后端。");
        }

        if (options.Headless)
        {
            engine.RunHeadlessTicks(options.HeadlessTicks);
            PixelEngine.Hosting.Scene? current = engine.Context.GetService<ISceneService>().Current;
            Console.WriteLine($"Engine frame: {engine.Context.Clock.FrameIndex}, scene: {current?.Name}");
            return;
        }

        DemoParticleFrameTimeProbe? particleFrameProbe = null;
        if (options.ParticleFrameProbe)
        {
            particleFrameProbe = new DemoParticleFrameTimeProbe(
                engine.Context.GetService<EngineProbeApi>(),
                options.ParticleProbeCount,
                options.ParticleProbeWarmupFrames,
                DemoWorldWidthCells,
                DemoWorldHeightCells);
            particleFrameProbe.RegisterPhases(engine.Phases);
        }

        PixelEngine.Rendering.RenderWindow window = engine.AttachWindowRuntime();
        RegisterWindowTitleDiagnostics(engine, window);
        FrameCaptureState? frameCapture = RegisterFrameCapture(engine, window, options);
        ApplyParticleRenderMode(engine, options);
        DemoWindowScriptedInput? scriptedInput = null;
        DemoWindowScriptedProbe? scriptedProbe = null;
        DemoReactionTemperatureProbe? reactionProbe = null;
        DemoAudioProbe? audioProbe = null;
        DemoParticleLightProbe? particleLightProbe = null;
        if (options.ScriptedWindowDemo)
        {
            scriptedInput = new DemoWindowScriptedInput(
                engine.Context.GetService<ScriptInputApi>(),
                engine.Context.GetService<ScriptCameraApi>(),
                options.ScriptedWindowRoute);
            scriptedInput.RegisterPhases(engine.Phases);
            scriptedProbe = new DemoWindowScriptedProbe(
                engine.Context.GetService<PhysicsSystem>(),
                engine.Context.GetService<EngineProbeApi>(),
                engine.Context.GetService<ScriptLightingSynchronizer>(),
                engine.Context.GetService<ScriptScene>(),
                engine.Context.GetService<ScriptCameraApi>(),
                engine.Context.GetService<ScriptCameraSynchronizer>());
            scriptedProbe.RegisterPhases(engine.Phases);
            if (IsReactionProbeScene(options.Scene))
            {
                reactionProbe = new DemoReactionTemperatureProbe(
                    engine.Context.GetService<EngineProbeApi>());
                reactionProbe.RegisterPhases(engine.Phases);
            }

            if (IsAudioProbeScene(options.Scene))
            {
                audioProbe = new DemoAudioProbe(engine.Context.GetService<EngineProbeApi>());
                audioProbe.RegisterPhases(engine.Phases);
            }

            if (IsParticleLightProbeScene(options.Scene))
            {
                particleLightProbe = new DemoParticleLightProbe(
                    engine.Context.GetService<EngineProbeApi>(),
                    engine.Context.GetService<ScriptLightingApi>(),
                    engine.Context.GetService<ScriptLightingSynchronizer>(),
                    engine.Context.GetService<ScriptCameraSynchronizer>());
                particleLightProbe.RegisterPhases(engine.Phases);
            }

            Console.WriteLine("脚本化窗口输入已启用。");
        }

        Console.WriteLine("窗口运行时已接入 Rendering/Input 后端。");
        if (!options.ScriptedWindowDemo)
        {
            Console.WriteLine("可玩 Demo 控制：A/D 或方向键移动，Space/W/Up 跳跃，鼠标左键射击破坏地形，Esc 暂停。");
        }

        if (options.WindowTicks > 0)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            double previousSeconds = stopwatch.Elapsed.TotalSeconds;
            int executed = 0;
            for (; executed < options.WindowTicks &&
                engine.State != EngineRunState.Shutdown &&
                !engine.IsShutdownRequested &&
                !window.IsClosing; executed++)
            {
                long tickStart = Stopwatch.GetTimestamp();
                double now = stopwatch.Elapsed.TotalSeconds;
                _ = engine.RunOneTick(now - previousSeconds);
                previousSeconds = now;
                double tickMs = (Stopwatch.GetTimestamp() - tickStart) * 1000.0 / Stopwatch.Frequency;
                particleFrameProbe?.RecordFrame(tickMs, engine.Context.Profiler.LastSubFrame);
            }

            Console.WriteLine($"窗口短跑完成：frames={engine.Context.Clock.FrameIndex}, requested={options.WindowTicks}。");
            Console.WriteLine(
                $"窗口短跑耗时：elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:0.00}, " +
                $"avg_tick_ms={(executed == 0 ? 0 : stopwatch.Elapsed.TotalMilliseconds / executed):0.00}, " +
                $"last_profile_ms={Sum(engine.Context.Profiler.LastFrame):0.00}。");
            Console.WriteLine(
                $"窗口短跑最慢相位：main_top={SlowestMainPhase(engine.Context.Profiler.LastFrame)}, " +
                $"sub_top={SlowestSubPhase(engine.Context.Profiler.LastSubFrame)}。");
            if (scriptedInput is not null)
            {
                WriteScriptedWindowSummary(engine, scriptedInput, scriptedProbe, reactionProbe, audioProbe, particleLightProbe);
            }

            if (particleFrameProbe is not null)
            {
                RenderPipeline pipeline = engine.Context.GetService<RenderPipeline>();
                ParticleRenderMode effective = pipeline.CanRenderParticlesOnGpu
                    ? ParticleRenderMode.GpuPointSprite
                    : ParticleRenderMode.CpuStamp;
                Console.WriteLine(particleFrameProbe.BuildSummary(effective, pipeline.CanRenderParticlesOnGpu));
            }

            CaptureFrameIfRequested(window, options, frameCapture);
            return;
        }

        engine.Run();
    }

    private static void ApplyParticleRenderMode(Engine engine, DemoStartupOptions options)
    {
        if (!options.ParticleRenderMode.HasValue)
        {
            return;
        }

        RenderPipeline pipeline = engine.Context.GetService<RenderPipeline>();
        pipeline.Settings.ParticleRenderMode = options.ParticleRenderMode.Value;
        bool gpuAvailable = pipeline.CanRenderParticlesOnGpu;
        ParticleRenderMode effective = gpuAvailable
            ? ParticleRenderMode.GpuPointSprite
            : ParticleRenderMode.CpuStamp;
        Console.WriteLine(
            $"粒子渲染模式：requested={ParticleRenderModeName(options.ParticleRenderMode.Value)}, " +
            $"effective={ParticleRenderModeName(effective)}, gpu_available={gpuAvailable}。");
        if (options.ParticleRenderMode.Value == ParticleRenderMode.GpuPointSprite && !gpuAvailable)
        {
            Console.WriteLine("GPU 粒子模式不可用：需要 GL compute 能力门控与 GpuParticlesEnabled 同时可用，本次不会静默当作 GPU 样本。");
        }
    }

    private static void RegisterWindowTitleDiagnostics(Engine engine, RenderWindow window)
    {
        engine.Phases.Register(EnginePhase.GpuUploadAndRender, context =>
        {
            if ((context.Context.Clock.FrameIndex & 15) != 0)
            {
                return;
            }

            EngineDiagnosticsSnapshot diagnostics = engine.Context.GetService<IDiagnosticsApi>().Capture();
            window.SetTitle(
                $"PixelEngine Demo | Render FPS {diagnostics.FramesPerSecond:0.0} | Sim {diagnostics.SimHz:0}Hz | Bodies {diagnostics.RigidBodies}");
        });
    }

    private static FrameCaptureState? RegisterFrameCapture(Engine engine, RenderWindow window, DemoStartupOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CaptureFramePath) || options.WindowTicks <= 0)
        {
            return null;
        }

        RenderPipeline pipeline = engine.Context.GetService<RenderPipeline>();
        FrameCaptureState state = new(Path.GetFullPath(options.CaptureFramePath));
        pipeline.BeforeSwapBuffers += _ =>
        {
            if (state.Captured || engine.Context.Clock.FrameIndex < options.WindowTicks)
            {
                return;
            }

            CaptureFramebuffer(window, state.Path);
            state.Captured = true;
            Console.WriteLine($"窗口 framebuffer 截图已写入：{state.Path}");
        };
        return state;
    }

    private static void CaptureFrameIfRequested(RenderWindow window, DemoStartupOptions options, FrameCaptureState? state)
    {
        if (string.IsNullOrWhiteSpace(options.CaptureFramePath))
        {
            return;
        }

        if (state?.Captured == true)
        {
            return;
        }

        string path = state?.Path ?? Path.GetFullPath(options.CaptureFramePath);
        CaptureFramebuffer(window, path);
        if (state is { } captureState)
        {
            captureState.Captured = true;
        }

        Console.WriteLine($"窗口 framebuffer 截图已写入：{path}");
    }

    private static void CaptureFramebuffer(RenderWindow window, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        int width = window.Width;
        int height = window.Height;
        byte[] bgra = new byte[checked(width * height * 4)];
        window.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        window.Gl.ReadPixels<byte>(0, 0, (uint)width, (uint)height, PixelFormat.Bgra, PixelType.UnsignedByte, bgra);
        WriteBgraBottomUpBmp(path, width, height, bgra);
    }

    private static void WriteBgraBottomUpBmp(string path, int width, int height, ReadOnlySpan<byte> bgra)
    {
        int pixelBytes = checked(width * height * 4);
        if (bgra.Length != pixelBytes)
        {
            throw new ArgumentException("BMP 像素数据尺寸与宽高不一致。", nameof(bgra));
        }

        const int fileHeaderBytes = 14;
        const int infoHeaderBytes = 40;
        int pixelOffset = fileHeaderBytes + infoHeaderBytes;
        int fileSize = checked(pixelOffset + pixelBytes);
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(pixelOffset);
        writer.Write(infoHeaderBytes);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2_835);
        writer.Write(2_835);
        writer.Write(0);
        writer.Write(0);
        writer.Write(bgra);
    }

    private sealed class FrameCaptureState(string path)
    {
        public string Path { get; } = path;

        public bool Captured { get; set; }
    }

    private static string ParticleRenderModeName(ParticleRenderMode mode)
    {
        return mode switch
        {
            ParticleRenderMode.CpuStamp => "cpu",
            ParticleRenderMode.GpuPointSprite => "gpu",
            _ => mode.ToString(),
        };
    }

    private static double Sum(ReadOnlySpan<double> values)
    {
        double total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            total += values[i];
        }

        return total;
    }

    private static string SlowestMainPhase(ReadOnlySpan<double> values)
    {
        int index = SlowestIndex(values);
        return index < 0 ? "none=0.00" : $"{(FramePhase)index}={values[index]:0.00}";
    }

    private static string SlowestSubPhase(ReadOnlySpan<double> values)
    {
        int index = SlowestIndex(values);
        return index < 0 ? "none=0.00" : $"{(FrameSubPhase)index}={values[index]:0.00}";
    }

    private static int SlowestIndex(ReadOnlySpan<double> values)
    {
        int bestIndex = -1;
        double best = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] <= best)
            {
                continue;
            }

            best = values[i];
            bestIndex = i;
        }

        return bestIndex;
    }

    private static void WriteScriptedWindowSummary(
        Engine engine,
        DemoWindowScriptedInput scriptedInput,
        DemoWindowScriptedProbe? scriptedProbe,
        DemoReactionTemperatureProbe? reactionProbe,
        DemoAudioProbe? audioProbe,
        DemoParticleLightProbe? particleLightProbe)
    {
        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        DemoHud? hud = FindBehaviour<DemoHud>(scene);
        PauseMenu? pause = FindBehaviour<PauseMenu>(scene);
        GoalTrigger? goal = FindBehaviour<GoalTrigger>(scene);
        MaterialBrush? brush = FindBehaviour<MaterialBrush>(scene);
        ExplosiveTool? explosive = FindBehaviour<ExplosiveTool>(scene);
        PlayerHealth? health = FindBehaviour<PlayerHealth>(scene);
        PlayerController? player = FindBehaviour<PlayerController>(scene);
        PlayerVisual? playerVisual = FindBehaviour<PlayerVisual>(scene);
        PlayableProjectileTool? projectile = FindBehaviour<PlayableProjectileTool>(scene);
        LevelDirector? director = FindBehaviour<LevelDirector>(scene);
        EngineProbeApi probe = engine.Context.GetService<EngineProbeApi>();
        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        ScriptCameraApi camera = engine.Context.GetService<ScriptCameraApi>();
        ScriptCameraSynchronizer cameraSync = engine.Context.GetService<ScriptCameraSynchronizer>();
        ScriptLightingSynchronizer lighting = engine.Context.GetService<ScriptLightingSynchronizer>();
        EngineDiagnosticsSnapshot diagnostics = engine.Context.GetService<IDiagnosticsApi>().Capture();
        RenderPhaseDriver? renderDriver = engine.Context.TryGetService(out RenderPhaseDriver registeredRenderDriver)
            ? registeredRenderDriver
            : null;
        ushort paintedMaterial = probe.MaterialAt(
            (int)MathF.Round(scriptedInput.BrushTargetWorld.X),
            (int)MathF.Round(scriptedInput.BrushTargetWorld.Y));
        string brushMaterial = brush?.SelectedMaterialName ?? "<missing>";
        string hudBlocked = string.IsNullOrEmpty(hud?.BlockedReason) ? "none" : hud.BlockedReason;
        string pauseOpen = pause?.IsOpen.ToString() ?? "<missing>";
        string goalReached = goal?.Reached.ToString() ?? "<missing>";
        CharacterState playerState = player?.State ?? default;
        int playerCenterX = (int)MathF.Round(player?.CenterX ?? 0f);
        int playerCenterY = (int)MathF.Round(player?.CenterY ?? 0f);
        ushort playerCenterMaterial = probe.MaterialAt(playerCenterX, playerCenterY);
        CameraState renderCamera = cameraSync.Current;

        Console.WriteLine(
            $"脚本化窗口输入摘要：frames={scriptedInput.FramesInjected}, " +
            $"brush_material={brushMaterial}, " +
            $"brush_radius={brush?.Radius ?? 0}, " +
            $"painted_material={paintedMaterial}, " +
            $"explosions={explosive?.ExplosionCount ?? 0}, " +
            $"last_explosion=({explosive?.LastExplosionX ?? 0:0.00},{explosive?.LastExplosionY ?? 0:0.00}), " +
            $"playable_shots={projectile?.ShotsFired ?? 0}, " +
            $"playable_last_hit=({projectile?.LastHitX ?? 0:0.00},{projectile?.LastHitY ?? 0:0.00}), " +
            $"playable_collapsed_islands={projectile?.CollapsedFloatingIslands ?? 0}, " +
            $"playable_last_collapse=({projectile?.LastCollapsedRegion.X ?? 0},{projectile?.LastCollapsedRegion.Y ?? 0},{projectile?.LastCollapsedRegion.Width ?? 0},{projectile?.LastCollapsedRegion.Height ?? 0}), " +
            $"playable_collapse_status={projectile?.CollapseStatus ?? "<missing>"}, " +
            $"playable_collapse_skip={projectile?.LastCollapseSkipReason ?? "<missing>"}, " +
            $"playable_collapse_candidates={projectile?.LastCollapseSolidCandidates ?? 0}, " +
            $"particles={probe.ActiveParticles}, " +
            $"max_particles={scriptedProbe?.MaxParticles ?? probe.ActiveParticles}, " +
            $"lights={lighting.PointLights.Length}, " +
            $"max_lights={scriptedProbe?.MaxLights ?? lighting.PointLights.Length}, " +
            $"physics_destroyed={physics.LastDestructionResult.DestroyedBodies}, " +
            $"physics_created={physics.LastDestructionResult.CreatedBodies}, " +
            $"max_physics_destroyed={scriptedProbe?.MaxDestroyedBodies ?? physics.LastDestructionResult.DestroyedBodies}, " +
            $"max_physics_created={scriptedProbe?.MaxCreatedBodies ?? physics.LastDestructionResult.CreatedBodies}, " +
            $"active_bodies={physics.PhysicsWorld.ActiveBodyCount}, " +
            $"max_active_bodies={scriptedProbe?.MaxActiveBodies ?? physics.PhysicsWorld.ActiveBodyCount}, " +
            $"audio_played={engine.Context.Counters.AudioPlayed}, " +
            $"audio_drained={engine.Context.Counters.AudioDrained}, " +
            $"max_audio_played={scriptedProbe?.MaxAudioPlayed ?? engine.Context.Counters.AudioPlayed}, " +
            $"max_audio_drained={scriptedProbe?.MaxAudioDrained ?? engine.Context.Counters.AudioDrained}, " +
            $"audio_loaded={engine.Context.Counters.AudioLoadedClips}, " +
            $"hud_blocked={hudBlocked}, " +
            $"fps={diagnostics.FramesPerSecond:0.0}, " +
            $"sim_hz={diagnostics.SimHz:0.0}, " +
            $"diagnostic_frame={diagnostics.FrameCount}, " +
            $"pause_open={pauseOpen}, " +
            $"goal_reached={goalReached}, " +
            $"player_health={health?.Health ?? 0:0.00}, " +
            $"damage_events={health?.DamageEventCount ?? 0}, " +
            $"respawns={health?.RespawnCount ?? 0}, " +
            $"spawn_probe={director?.BuildSpawnHazardProbe.ToString() ?? "<missing>"}, " +
            $"player=({playerState.X:0.00},{playerState.Y:0.00},{playerState.Width:0.00},{playerState.Height:0.00}), " +
            $"player_center=({player?.CenterX ?? 0f:0.00},{player?.CenterY ?? 0f:0.00}), " +
            $"player_visual={(playerVisual is not null ? "present" : "missing")}, " +
            $"player_visual_overlays={playerVisual?.LastOverlayCommandsSubmitted ?? 0}, " +
            $"render_overlays={renderDriver?.LastOverlayCount ?? -1}, " +
            $"camera_center=({camera.CenterX:0.00},{camera.CenterY:0.00}), " +
            $"camera_zoom={camera.Zoom:0.00}, " +
            $"camera_samples={scriptedProbe?.CameraSamples ?? 0}, " +
            $"camera_followed={scriptedProbe?.CameraFollowed.ToString() ?? "<missing>"}, " +
            $"render_camera_synced={scriptedProbe?.RenderCameraSynced.ToString() ?? "<missing>"}, " +
            $"player_x_range=({scriptedProbe?.PlayerMinX ?? player?.CenterX ?? 0f:0.00},{scriptedProbe?.PlayerMaxX ?? player?.CenterX ?? 0f:0.00}), " +
            $"player_y_range={scriptedProbe?.PlayerYRange ?? 0f:0.00}, " +
            $"player_ground_samples={scriptedProbe?.PlayerGroundedSamples ?? 0}, " +
            $"player_air_samples={scriptedProbe?.PlayerAirborneSamples ?? 0}, " +
            $"player_left_ground={scriptedProbe?.PlayerLeftGround.ToString() ?? "<missing>"}, " +
            $"player_air_x_range={scriptedProbe?.PlayerAirXRange ?? 0f:0.00}, " +
            $"player_air_control={scriptedProbe?.PlayerAirControl.ToString() ?? "<missing>"}, " +
            $"camera_x_range=({scriptedProbe?.CameraMinX ?? camera.CenterX:0.00},{scriptedProbe?.CameraMaxX ?? camera.CenterX:0.00}), " +
            $"render_origin_x_range=({scriptedProbe?.RenderOriginMinX ?? renderCamera.OriginWorldX:0.00},{scriptedProbe?.RenderOriginMaxX ?? renderCamera.OriginWorldX:0.00}), " +
            $"render_camera=({renderCamera.OriginWorldX:0.00},{renderCamera.OriginWorldY:0.00},{renderCamera.CellsPerPixel:0.000},{renderCamera.ViewportWidth}x{renderCamera.ViewportHeight}), " +
            ReactionProbeSummary(reactionProbe) +
            AudioProbeSummary(audioProbe) +
            ParticleLightProbeSummary(particleLightProbe) +
            $"player_center_material={playerCenterMaterial}。");
    }

    private static string ReactionProbeSummary(DemoReactionTemperatureProbe? probe)
    {
        return probe is null
            ? string.Empty
            :
            $"reaction_probe_initialized={probe.Initialized}, " +
            $"reactions_observed={probe.ReactionsObserved}, " +
            $"phase_transitions_observed={probe.PhaseTransitionsObserved}, " +
            $"reaction_cases=(lava_water={probe.LavaWater};molten_water={probe.MoltenWater};water_fire={probe.WaterFire};fire_wood={probe.FireWood};fire_oil={probe.FireOil};acid={probe.AcidCorrosion};steam_condense={probe.SteamCondense}), " +
            $"phase_cases=(ice_melted={probe.IceMelted};water_boiled={probe.WaterBoiled};water_froze={probe.WaterFroze};lava_cooled={probe.LavaCooled};metal_melted={probe.MetalMelted};sand_glassed={probe.SandGlassed}), " +
            $"probe_counts=({probe.CountSummary}), ";
    }

    private static string AudioProbeSummary(DemoAudioProbe? probe)
    {
        return probe is null
            ? string.Empty
            :
            $"audio_probe_initialized={probe.Initialized}, " +
            $"audio_probe_enqueued={probe.Enqueued}, " +
            $"audio_probe_stress_enqueued={probe.StressEnqueued}, " +
            $"audio_probe_one_shot_played={probe.OneShotPlayed}, " +
            $"audio_probe_ambient_activated={probe.AmbientActivated}, " +
            $"audio_probe_limited={probe.Limited}, " +
            $"audio_probe_max_drained={probe.MaxDrained}, " +
            $"audio_probe_max_coalesced={probe.MaxCoalesced}, " +
            $"audio_probe_max_dropped={probe.MaxDropped}, " +
            $"audio_probe_max_played={probe.MaxPlayed}, " +
            $"audio_probe_max_active_voices={probe.MaxActiveVoices}, " +
            $"audio_probe_max_active_ambient={probe.MaxActiveAmbientVoices}, ";
    }

    private static string ParticleLightProbeSummary(DemoParticleLightProbe? probe)
    {
        return probe is null
            ? string.Empty
            :
            $"particle_light_probe_initialized={probe.Initialized}, " +
            $"particle_light_probe_spawned={probe.Spawned}, " +
            $"particle_light_probe_max_active={probe.MaxActive}, " +
            $"particle_light_probe_tail_max={probe.TailMaxActive}, " +
            $"particle_light_probe_last_active={probe.LastActive}, " +
            $"particle_light_probe_lifetime_kill={probe.LifetimeKillObserved}, " +
            $"particle_light_probe_depleted={probe.Depleted}, " +
            $"particle_light_probe_light_observed={probe.LightObserved}, " +
            $"particle_light_probe_fog_alpha={probe.MaxFogAlpha}, " +
            $"particle_light_probe_lighting_synced={probe.LightingSynced}, ";
    }

    private static bool IsReactionProbeScene(string scene)
    {
        return string.Equals(
            Path.GetFileName(scene),
            "lava-mine-reaction-probe.scene",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAudioProbeScene(string scene)
    {
        return string.Equals(
            Path.GetFileName(scene),
            "lava-mine-audio-probe.scene",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsParticleLightProbeScene(string scene)
    {
        return string.Equals(
            Path.GetFileName(scene),
            "lava-mine-particle-light-probe.scene",
            StringComparison.OrdinalIgnoreCase);
    }

    private static TBehaviour? FindBehaviour<TBehaviour>(ScriptScene scene)
        where TBehaviour : Behaviour
    {
        foreach (ScriptEntityInspection entity in scene.CaptureInspectionSnapshot())
        {
            foreach (ScriptComponentInspection component in entity.Components)
            {
                if (component.Behaviour is TBehaviour behaviour)
                {
                    return behaviour;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 判断当前启动参数和运行时能力是否允许启用脚本热重载。
    /// </summary>
    /// <param name="options">启动参数。</param>
    /// <param name="dynamicCodeSupported">当前运行时是否支持动态代码生成与加载；NativeAOT 通道通常为 false。</param>
    /// <returns>允许启用热重载时返回 true。</returns>
    public static bool CanEnableHotReload(DemoStartupOptions options, bool dynamicCodeSupported)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.HotReloadEnabled && dynamicCodeSupported;
    }

    private static PixelEngine.Scripting.ScriptHotReloadRuntimeOptions? CreateHotReloadOptions(
        DemoStartupOptions options,
        bool dynamicCodeSupported)
    {
        if (!CanEnableHotReload(options, dynamicCodeSupported))
        {
            return null;
        }

        string contentRoot = Path.GetFullPath(options.ContentRoot);
        DirectoryInfo? contentDirectory = new(contentRoot);
        string? sourceDirectory = contentDirectory.Parent?.FullName;
        return string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory)
            ? null
            : new PixelEngine.Scripting.ScriptHotReloadRuntimeOptions(
                "PixelEngine.Demo.HotReload",
                sourceDirectory,
                PreserveState: true,
                SearchPattern: "*.cs",
                IncludeSubdirectories: false);
    }

    /// <summary>
    /// 构造 Demo 项目模型。若场景文件不存在，则回退到程序化可玩场景来源。
    /// </summary>
    /// <param name="options">启动参数。</param>
    /// <returns>EngineProject。</returns>
    public static EngineProject BuildProject(DemoStartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string contentRoot = Path.GetFullPath(options.ContentRoot);
        SceneDescriptor scene = BuildSceneDescriptor(contentRoot, options.Scene);
        return new EngineProject(contentRoot, scene.Name, [scene]);
    }

    /// <summary>
    /// 构造 Demo Engine。
    /// </summary>
    /// <param name="options">启动参数。</param>
    /// <param name="project">项目模型。</param>
    /// <returns>Engine 实例。</returns>
    public static Engine BuildEngine(DemoStartupOptions options, EngineProject project)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(project);
        EngineBuilder builder = new EngineBuilder()
            .WithProject(project)
            .WithWindow(PlayableWindowWidth, PlayableWindowHeight)
            .WithInternalResolution(PlayableInternalWidth, PlayableInternalHeight)
            .UseDeterministicMode();
        if (options.Headless)
        {
            _ = builder.UseHeadless();
        }

        if (options.EnableEditor)
        {
            _ = builder.EnableEditor();
        }

        return builder.Build();
    }

    private static SceneDescriptor BuildSceneDescriptor(string contentRoot, string scene)
    {
        string source = scene;
        if (!Path.IsPathRooted(source))
        {
            source = Path.Combine(contentRoot, source);
        }

        string sceneName = Path.GetFileNameWithoutExtension(source);
        return Directory.Exists(source)
            ? new SceneDescriptor(sceneName, SceneSourceKind.SaveDirectory, source)
            : File.Exists(source)
                ? new SceneDescriptor(sceneName, SceneSourceKind.SceneFile, source)
                : new SceneDescriptor(sceneName, SceneSourceKind.Procedural, DemoStartupOptions.DefaultProceduralSceneKey);
    }

    private static string WriteCrashLog(Exception exception, string? logDirectory)
    {
        string directory = string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : logDirectory;
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"demo-crash-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
        File.WriteAllText(path, exception.ToString());
        return path;
    }
}
