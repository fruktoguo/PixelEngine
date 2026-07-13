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
    private const double PlayableOverloadFrameBudgetMs = 1000.0 / 30.0;
    private const int PlayableOverloadSustainWindow = 30;

    /// <summary>
    /// 执行 Demo 主入口。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码。</returns>
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CameraFollow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DemoHud))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ExplosionFlashEffect))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ExplosiveTool))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ExtractionTrigger))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(GoalTrigger))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(GrenadeProjectile))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(GameUiDemoController))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LevelDirector))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MaterialBrush))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MaterialEmitter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MissionDirector))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ObjectiveCrystal))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PauseMenu))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayableHud))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayableProjectileTool))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayableWorldDirector))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayerController))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayerHealth))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayerVisual))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RisingHazardDirector))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SparkEmitter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WeaponController))]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "NativeAOT win-x64 publish is smoke-tested from this entry point; remaining Enum.GetValues(Type) analysis comes from the Silk.NET/System.Text.Json dependency closure rather than Demo startup code.")]
    public static int Execute(string[] args)
    {
        // 打包发布时把 app/ 原生依赖目录加入 PATH
        ConfigurePackagedNativeSearchPath();
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

    private static void ConfigurePackagedNativeSearchPath()
    {
        string dependencyDirectory = Path.Combine(AppContext.BaseDirectory, "app");
        if (!Directory.Exists(dependencyDirectory) || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        string current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (current.Split(Path.PathSeparator).Any(path =>
                string.Equals(
                    Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? "." : path),
                    dependencyDirectory,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Environment.SetEnvironmentVariable("PATH", dependencyDirectory + Path.PathSeparator + current);
    }

    /// <summary>
    /// Demo 运行时主流程：构造 Engine → 挂载内容/脚本/窗口 → Headless 或窗口循环。
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

        // Scene 物化会立即解析 Behaviour，因此程序集必须先于任何 world/scene 挂载完成注册。
        RegisterPackagedScriptAssemblies(engine, options, RuntimeFeature.IsDynamicCodeSupported);
        engine.RegisterScriptAssembly(assembly);
        Console.WriteLine(options.HotReloadEnabled
            ? "脚本程序集已注册；热重载等待脚本宿主装配。"
            : "脚本程序集已注册；热重载已由参数关闭。");

        // 内容包就绪时挂载 Simulation/Physics/Audio；否则走最小冒烟 world
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
            AttachMinimalSmokeWorld(engine);
        }

        EngineProbeApi probe = engine.Probe;

        // world/scene 已挂载后，接入真实 Simulation 脚本上下文与热重载。
        ScriptHotReloadRuntimeOptions? hotReload = CreateHotReloadOptions(
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
        if (contentLoaded)
        {
            new DemoLoadCountersPhaseDriver(probe).RegisterPhases(engine.Phases);
        }

        // Headless 模式只跑逻辑 tick，不创建窗口
        if (options.Headless)
        {
            engine.RunHeadlessTicks(options.HeadlessTicks);
            Hosting.Scene? current = engine.CurrentScene;
            Console.WriteLine($"Engine frame: {engine.Context.Clock.FrameIndex}, scene: {current?.Name}");
            return;
        }

        DemoParticleFrameTimeProbe? particleFrameProbe = null;
        if (options.ParticleFrameProbe)
        {
            particleFrameProbe = new DemoParticleFrameTimeProbe(
                probe,
                options.ParticleProbeCount,
                options.ParticleProbeWarmupFrames,
                options.ParticleProbeRunId,
                DemoWorldWidthCells,
                DemoWorldHeightCells);
            particleFrameProbe.RegisterPhases(engine.Phases);
        }

        // 窗口运行时：标题诊断、帧截图、粒子渲染模式与脚本化探针
        RenderWindow window = engine.AttachWindowRuntime();
        RegisterWindowTitleDiagnostics(probe, window, engine.Phases);
        FrameCaptureState? frameCapture = RegisterFrameCapture(probe, window, options);
        ParticleRenderProbeResult? particleRenderProbe = ApplyParticleRenderMode(probe, options);
        DemoWindowScriptedInput? scriptedInput = null;
        DemoWindowScriptedProbe? scriptedProbe = null;
        DemoReactionTemperatureProbe? reactionProbe = null;
        DemoAudioProbe? audioProbe = null;
        DemoParticleLightProbe? particleLightProbe = null;
        if (options.ScriptedWindowDemo)
        {
            scriptedInput = new DemoWindowScriptedInput(probe, options.ScriptedWindowRoute);
            scriptedInput.RegisterPhases(engine.Phases);
            scriptedProbe = new DemoWindowScriptedProbe(probe);
            scriptedProbe.RegisterPhases(engine.Phases);
            if (IsReactionProbeScene(options.Scene))
            {
                reactionProbe = new DemoReactionTemperatureProbe(probe);
                reactionProbe.RegisterPhases(engine.Phases);
            }

            if (IsAudioProbeScene(options.Scene))
            {
                audioProbe = new DemoAudioProbe(probe);
                audioProbe.RegisterPhases(engine.Phases);
            }

            if (IsParticleLightProbeScene(options.Scene))
            {
                particleLightProbe = new DemoParticleLightProbe(probe);
                particleLightProbe.RegisterPhases(engine.Phases);
            }

            Console.WriteLine("脚本化窗口输入已启用。");
        }

        Console.WriteLine("窗口运行时已接入 Rendering/Input 后端。");
        if (!options.ScriptedWindowDemo)
        {
            Console.WriteLine("可玩 Demo 控制：A/D 或方向键移动，Space/W/Up 跳跃，鼠标左键射击破坏地形，Esc 暂停。");
        }

        // 有限帧数短跑：采集 tick 耗时与相位 profile 后退出
        if (options.WindowTicks > 0)
        {
            DemoWindowFrameTimeProbe windowFrameProbe = new(
                Math.Min(120, Math.Max(0, options.WindowTicks / 3)),
                WindowProbeScenario(options));
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
                long threadAllocatedBytesBeforeTick = GC.GetAllocatedBytesForCurrentThread();
                _ = engine.RunOneTick(now - previousSeconds);
                previousSeconds = now;
                double tickMs = (Stopwatch.GetTimestamp() - tickStart) * 1000.0 / Stopwatch.Frequency;
                long threadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - threadAllocatedBytesBeforeTick;
                windowFrameProbe.RecordFrame(
                    tickMs,
                    engine.Context.Profiler.LastFrame,
                    engine.Context.Profiler.LastSubFrame,
                    engine.Context.Counters,
                    threadAllocatedBytes);
                particleFrameProbe?.RecordFrame(tickMs, engine.Context.Profiler.LastSubFrame, engine.Context.Counters);
            }

            Console.WriteLine($"窗口短跑完成：frames={engine.Context.Clock.FrameIndex}, requested={options.WindowTicks}。");
            Console.WriteLine(
                $"窗口短跑耗时：elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:0.00}, " +
                $"avg_tick_ms={(executed == 0 ? 0 : stopwatch.Elapsed.TotalMilliseconds / executed):0.00}, " +
                $"last_profile_ms={Sum(engine.Context.Profiler.LastFrame):0.00}。");
            Console.WriteLine(
                $"窗口短跑最慢相位：main_top={SlowestMainPhase(engine.Context.Profiler.LastFrame)}, " +
                $"sub_top={SlowestSubPhase(engine.Context.Profiler.LastSubFrame)}。");
            Console.WriteLine(
                $"窗口性能拆分：cpu_work_ms={engine.Context.Counters.FrameCpuWorkMilliseconds:0.00}, " +
                $"gpu_frame_ms={(engine.Context.Counters.FrameGpuTimerAvailable ? engine.Context.Counters.FrameGpuWorkMilliseconds.ToString("0.00") : "N/A")}, " +
                $"present_wait_ms={engine.Context.Counters.FramePresentWaitMilliseconds:0.00}, " +
                $"effective_fps={engine.Context.Counters.EffectiveFramesPerSecond:0.0}, " +
                $"vsync={(engine.Context.Counters.VSyncEnabled ? "on" : "off")}。");
            Console.WriteLine(windowFrameProbe.BuildSummary(
                engine.Context.Counters.FrameGpuTimerAvailable,
                engine.Context.Counters.VSyncEnabled));
            WriteGameUiProbeSummary(probe);
            if (scriptedInput is not null)
            {
                WriteScriptedWindowSummary(engine, probe, scriptedInput, scriptedProbe, reactionProbe, audioProbe, particleLightProbe);
            }

            if (particleFrameProbe is not null)
            {
                ParticleRenderProbeResult renderResult = particleRenderProbe ??
                    new ParticleRenderProbeResult(
                        ParticleRenderMode.CpuStamp,
                        ParticleRenderMode.CpuStamp,
                        GpuAvailable: false);
                Console.WriteLine(particleFrameProbe.BuildSummary(
                    renderResult.Effective,
                    renderResult.GpuAvailable));
            }

            CaptureFrameIfRequested(window, options, frameCapture);
            return;
        }

        engine.Run();
    }

    private static string WindowProbeScenario(DemoStartupOptions options)
    {
        return options.ParticleFrameProbe
            ? $"particle_{options.ParticleRenderMode?.ToString() ?? "auto"}"
            : options.ScriptedWindowRoute
                ? "scripted_route"
                : options.ScriptedWindowDemo ? "scripted_demo" : "static";
    }

    private static ParticleRenderProbeResult? ApplyParticleRenderMode(EngineProbeApi probe, DemoStartupOptions options)
    {
        if (!options.ParticleRenderMode.HasValue)
        {
            return null;
        }

        ParticleRenderProbeResult result = probe.SetParticleRenderMode(options.ParticleRenderMode.Value);
        Console.WriteLine(
            $"粒子渲染模式：requested={ParticleRenderModeName(options.ParticleRenderMode.Value)}, " +
            $"effective={ParticleRenderModeName(result.Effective)}, gpu_available={result.GpuAvailable}。");
        if (options.ParticleRenderMode.Value == ParticleRenderMode.GpuPointSprite && !result.GpuAvailable)
        {
            Console.WriteLine("GPU 粒子模式不可用：需要 GL compute 能力门控与 GpuParticlesEnabled 同时可用，本次不会静默当作 GPU 样本。");
        }

        return result;
    }

    private static void RegisterWindowTitleDiagnostics(EngineProbeApi probe, RenderWindow window, EnginePhasePipeline phases)
    {
        phases.Register(EnginePhase.GpuUploadAndRender, context =>
        {
            if ((context.Context.Clock.FrameIndex & 15) != 0)
            {
                return;
            }

            EngineDiagnosticsSnapshot diagnostics = probe.CaptureDiagnostics();
            window.SetTitle(
                $"PixelEngine Demo | Render FPS {diagnostics.FramesPerSecond:0.0} | Sim {diagnostics.SimHz:0}Hz | Bodies {diagnostics.RigidBodies}");
        });
    }

    private static FrameCaptureState? RegisterFrameCapture(EngineProbeApi probe, RenderWindow window, DemoStartupOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CaptureFramePath) || options.WindowTicks <= 0)
        {
            return null;
        }

        FrameCaptureState state = new(Path.GetFullPath(options.CaptureFramePath));
        _ = probe.RegisterBeforeSwapBuffers(() =>
        {
            if (state.Captured || probe.FrameCount < options.WindowTicks)
            {
                return;
            }

            CaptureFramebuffer(window, state.Path);
            state.Captured = true;
            Console.WriteLine($"窗口 framebuffer 截图已写入：{state.Path}");
        });
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
        window.BindPresentationFramebuffer();
        window.Gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Bgra, PixelType.UnsignedByte, bgra);
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

    private static void WriteGameUiProbeSummary(EngineProbeApi probe)
    {
        GameUiProbeSnapshot snapshot = probe.CaptureGameUi();
        Console.WriteLine(
            $"game_ui_probe attached={snapshot.IsAttached}, canvases={snapshot.CanvasCount}, " +
            $"requested={snapshot.RequestedBackend}, active={snapshot.ActiveBackend}, " +
            $"fallback={snapshot.UsedFallback}, " +
            $"fallback_reason={NormalizeProbeValue(snapshot.FallbackReason)}, " +
            $"native_profile={NormalizeProbeValue(snapshot.ActiveNativeProfile)}");
    }

    private static string NormalizeProbeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "<none>"
            : value.Replace('\r', ' ').Replace('\n', ' ').Replace(',', ';');
    }

    private static void WriteScriptedWindowSummary(
        Engine engine,
        EngineProbeApi probe,
        DemoWindowScriptedInput scriptedInput,
        DemoWindowScriptedProbe? scriptedProbe,
        DemoReactionTemperatureProbe? reactionProbe,
        DemoAudioProbe? audioProbe,
        DemoParticleLightProbe? particleLightProbe)
    {
        ScriptScene scene = probe.ScriptScene;
        DemoHud? hud = FindBehaviour<DemoHud>(scene);
        PauseMenu? pause = FindBehaviour<PauseMenu>(scene);
        GoalTrigger? goal = FindBehaviour<GoalTrigger>(scene);
        ExtractionTrigger? extraction = FindBehaviour<ExtractionTrigger>(scene);
        MaterialBrush? brush = FindBehaviour<MaterialBrush>(scene);
        ExplosiveTool? explosive = FindBehaviour<ExplosiveTool>(scene);
        PlayerHealth? health = FindBehaviour<PlayerHealth>(scene);
        PlayerController? player = FindBehaviour<PlayerController>(scene);
        PlayerVisual? playerVisual = FindBehaviour<PlayerVisual>(scene);
        GameUiDemoController? gameUi = FindBehaviour<GameUiDemoController>(scene);
        PlayableProjectileTool? projectile = FindBehaviour<PlayableProjectileTool>(scene);
        WeaponController? weapons = FindBehaviour<WeaponController>(scene);
        LevelDirector? director = FindBehaviour<LevelDirector>(scene);
        PhysicsSystemStats physics = probe.PhysicsStats;
        ScriptCameraApi camera = probe.Camera;
        CameraState renderCamera = probe.CameraSynchronizer.Current;
        EngineDiagnosticsSnapshot diagnostics = probe.CaptureDiagnostics();
        ushort paintedMaterial = probe.MaterialAt(
            (int)MathF.Round(scriptedInput.BrushTargetWorld.X),
            (int)MathF.Round(scriptedInput.BrushTargetWorld.Y));
        string brushMaterial = brush?.SelectedMaterialName ?? "<missing>";
        string hudBlocked = string.IsNullOrEmpty(hud?.BlockedReason) ? "none" : hud.BlockedReason;
        string pauseOpen = pause?.IsOpen.ToString() ?? "<missing>";
        string goalReached = goal?.Reached.ToString() ?? extraction?.Reached.ToString() ?? "<missing>";
        CharacterState playerState = player?.State ?? default;
        int playerCenterX = (int)MathF.Round(player?.CenterX ?? 0f);
        int playerCenterY = (int)MathF.Round(player?.CenterY ?? 0f);
        ushort playerCenterMaterial = probe.MaterialAt(playerCenterX, playerCenterY);
        engine.Context.Counters.CustomMetric.Read(out string customMetricName, out long customMetricValue);
        GameUiProbeSnapshot gameUiProbe = probe.CaptureGameUi();
        int runtimeCanvasCount = gameUiProbe.CanvasCount;
        int serviceCanvasCount = gameUi?.CanvasCount ?? 0;
        string requestedUiBackend = gameUiProbe.IsAttached
            ? gameUiProbe.RequestedBackend.ToString()
            : "<missing>";
        string activeUiBackend = gameUiProbe.IsAttached
            ? gameUiProbe.ActiveBackend.ToString()
            : "<missing>";
        string usedUiFallback = gameUiProbe.IsAttached
            ? gameUiProbe.UsedFallback.ToString()
            : "<missing>";

        PixelEngine.Hosting.Scene? hostingScene = engine.CurrentScene;
        int resolvedSceneCanvasCount = hostingScene?.Descriptor.SourceKind == SceneSourceKind.SceneFile &&
            hostingScene.ResolvedSource is string resolvedScenePath
                ? EngineSceneCanvasResolver.Resolve(EngineSceneDocumentLoader.LoadDocument(resolvedScenePath)).Count
                : -1;

        Console.WriteLine(
            $"脚本化窗口输入摘要：frames={scriptedInput.FramesInjected}, " +
            $"transient_bursts={TransientParticleBurst.ActiveCount(scene)}, " +
            $"max_transient_bursts={scriptedProbe?.MaxTransientBursts ?? TransientParticleBurst.ActiveCount(scene)}, " +
            $"brush_material={brushMaterial}, " +
            $"brush_radius={brush?.Radius ?? 0}, " +
            $"painted_material={paintedMaterial}, " +
            $"explosions={explosive?.ExplosionCount ?? 0}, " +
            $"last_explosion=({explosive?.LastExplosionX ?? 0:0.00},{explosive?.LastExplosionY ?? 0:0.00}), " +
            $"playable_shots={projectile?.ShotsFired ?? 0}, " +
            $"weapon_id={weapons?.SelectedWeaponId ?? "<missing>"}, " +
            $"weapon_primary={weapons?.PrimaryFireCount ?? 0}, " +
            $"weapon_secondary={weapons?.SecondaryFireCount ?? 0}, " +
            $"weapon_last_kind={weapons?.LastDispatchedKind.ToString() ?? "<missing>"}, " +
            $"weapon_ammo={weapons?.CurrentAmmo ?? 0}, " +
            $"playable_last_hit=({projectile?.LastHitX ?? 0:0.00},{projectile?.LastHitY ?? 0:0.00}), " +
            $"playable_collapsed_islands={projectile?.CollapsedFloatingIslands ?? 0}, " +
            $"playable_last_collapse=({projectile?.LastCollapsedRegion.X ?? 0},{projectile?.LastCollapsedRegion.Y ?? 0},{projectile?.LastCollapsedRegion.Width ?? 0},{projectile?.LastCollapsedRegion.Height ?? 0}), " +
            $"playable_collapse_status={projectile?.CollapseStatus ?? "<missing>"}, " +
            $"playable_collapse_skip={projectile?.LastCollapseSkipReason ?? "<missing>"}, " +
            $"playable_collapse_candidates={projectile?.LastCollapseSolidCandidates ?? 0}, " +
            $"destruction_events={engine.Context.Counters.CellDestructionEventsThisTick + engine.Context.Counters.RigidBodiesDestroyedThisTick + engine.Context.Counters.RigidBodiesCreatedThisTick}, " +
            $"custom_metric_name={customMetricName}, " +
            $"custom_metric={customMetricValue}, " +
            $"particles={probe.ActiveParticles}, " +
            $"max_particles={scriptedProbe?.MaxParticles ?? probe.ActiveParticles}, " +
            $"lights={probe.PointLights.Length}, " +
            $"max_lights={scriptedProbe?.MaxLights ?? probe.PointLights.Length}, " +
            $"physics_destroyed={physics.LastDestructionResult.DestroyedBodies}, " +
            $"physics_created={physics.LastDestructionResult.CreatedBodies}, " +
            $"max_physics_destroyed={scriptedProbe?.MaxDestroyedBodies ?? physics.LastDestructionResult.DestroyedBodies}, " +
            $"max_physics_created={scriptedProbe?.MaxCreatedBodies ?? physics.LastDestructionResult.CreatedBodies}, " +
            $"active_bodies={physics.ActiveBodyCount}, " +
            $"max_active_bodies={scriptedProbe?.MaxActiveBodies ?? physics.ActiveBodyCount}, " +
            $"audio_played={engine.Context.Counters.AudioPlayed}, " +
            $"audio_drained={engine.Context.Counters.AudioDrained}, " +
            $"max_audio_played={scriptedProbe?.MaxAudioPlayed ?? engine.Context.Counters.AudioPlayed}, " +
            $"max_audio_drained={scriptedProbe?.MaxAudioDrained ?? engine.Context.Counters.AudioDrained}, " +
            $"audio_loaded={engine.Context.Counters.AudioLoadedClips}, " +
            $"ui_runtime_canvases={runtimeCanvasCount}, " +
            $"ui_service_canvases={serviceCanvasCount}, " +
            $"ui_resolved_scene_canvases={resolvedSceneCanvasCount}, " +
            $"ui_backend_requested={requestedUiBackend}, " +
            $"ui_backend_active={activeUiBackend}, " +
            $"ui_backend_fallback={usedUiFallback}, " +
            $"hosting_scene_kind={hostingScene?.Descriptor.SourceKind.ToString() ?? "<missing>"}, " +
            $"ui_canvases={gameUi?.CanvasCount ?? 0}, " +
            $"ui_pixel_canvas={gameUi?.PixelOverlayCanvas.Value ?? 0}, " +
            $"ui_physical_canvas={gameUi?.PhysicalOverlayCanvas.Value ?? 0}, " +
            $"hud_blocked={hudBlocked}, " +
            $"fps={diagnostics.FramesPerSecond:0.0}, " +
            $"frame_ms={diagnostics.FrameMilliseconds:0.0}, " +
            $"frame_last_ms={diagnostics.FrameLastMilliseconds:0.0}, " +
            $"frame_p99_ms={diagnostics.FrameP99Milliseconds:0.0}, " +
            $"frame_low1_fps={diagnostics.FrameLow1PercentFps:0.0}, " +
            $"frame_jitter_ms={diagnostics.FrameJitterMilliseconds:0.0}, " +
            $"frame_samples={diagnostics.FrameSampleCount}, " +
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
            $"render_overlays={probe.RenderOverlayCount}, " +
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
            $"particle_light_probe_tail_clear_frames={probe.TailClearFrames}, " +
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
        return scene.TryGetFirstComponent(out TBehaviour? behaviour) ? behaviour : null;
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

    /// <summary>
    /// 注册玩家包随 content/scripts 分发的脚本程序集。
    /// </summary>
    /// <param name="engine">目标引擎实例。</param>
    /// <param name="options">玩家启动选项。</param>
    /// <param name="dynamicCodeSupported">当前运行时是否支持动态脚本编译。</param>
    public static void RegisterPackagedScriptAssemblies(
        Engine engine,
        DemoStartupOptions options,
        bool dynamicCodeSupported)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);
        string scriptDirectory = Path.Combine(Path.GetFullPath(options.ContentRoot), "scripts");
        if (!Directory.Exists(scriptDirectory))
        {
            return;
        }

        if (!dynamicCodeSupported || !RuntimeScriptAssemblyCompiler.IsSupported)
        {
            throw new InvalidOperationException($"玩家包包含脚本源码但当前运行时不支持动态脚本编译：{scriptDirectory}");
        }

        RuntimeScriptAssemblyCompileResult result = RuntimeScriptAssemblyCompiler.CompileAndLoadFromDirectory(
            "PixelEngine.PackagedScripts",
            scriptDirectory,
            includeSubdirectories: true);
        if (!result.Success)
        {
            string diagnostics = string.Join(Environment.NewLine, result.Diagnostics);
            throw new InvalidOperationException($"{result.Error}{Environment.NewLine}{diagnostics}");
        }

        if (result.Assembly is not null)
        {
            engine.RegisterScriptAssembly(result.Assembly);
            Console.WriteLine($"随包脚本程序集已注册：{scriptDirectory}");
        }
    }

    /// <summary>
    /// 为不含材质内容包的空工程玩家包接入可渲染的最小 Simulation world。
    /// </summary>
    public static void AttachMinimalSmokeWorld(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _ = engine.EnsureMinimalContentPackage();
        _ = engine.AttachResidentSimulationWorld(DemoWorldWidthCells, DemoWorldHeightCells, DemoParticleCapacityDefault);
    }

    private static ScriptHotReloadRuntimeOptions? CreateHotReloadOptions(
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
            : new ScriptHotReloadRuntimeOptions(
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
        return EngineProject.FromContentRoot(
            contentRoot,
            options.Scene,
            DemoStartupOptions.DefaultProceduralSceneKey);
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
            .WithWindow(options.WindowWidth, options.WindowHeight)
            .WithWindowMode(options.WindowMode)
            .WithWindowTitle(options.WindowTitle)
            .WithInternalResolution(PlayableInternalWidth, PlayableInternalHeight)
            .WithOverloadPolicy(PlayableOverloadFrameBudgetMs, PlayableOverloadSustainWindow)
            .UseVSync(options.VSync)
            .EnableGameUi()
            .UseUiBackend(options.RuntimeUiBackend)
            .UseDeterministicMode();
        if (options.Headless)
        {
            _ = builder.UseHeadless();
        }

        return builder.Build();
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
