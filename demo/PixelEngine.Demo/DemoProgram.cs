using System.Diagnostics;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 启动入口实现。
/// </summary>
public static class DemoProgram
{
    private const int DemoWorldWidthCells = 640;
    private const int DemoWorldHeightCells = 360;

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
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayerController))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlayerHealth))]
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
        bool contentLoaded = false;
        if (engine.HasContentPackage())
        {
            EngineContentPackage package = engine.LoadContentPackage();
            object? worldLoad = engine.AttachCurrentSceneWorld();
            if (worldLoad is not null)
            {
                Console.WriteLine("世界存档已加载。");
            }
            else
            {
                _ = engine.AttachResidentSimulationWorld(DemoWorldWidthCells, DemoWorldHeightCells);
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

        PixelEngine.Rendering.RenderWindow window = engine.AttachWindowRuntime();
        DemoWindowScriptedInput? scriptedInput = null;
        DemoWindowScriptedProbe? scriptedProbe = null;
        if (options.ScriptedWindowDemo)
        {
            scriptedInput = new DemoWindowScriptedInput(
                engine.Context.GetService<ScriptInputApi>(),
                engine.Context.GetService<ScriptCameraApi>());
            scriptedInput.RegisterPhases(engine.Phases);
            scriptedProbe = new DemoWindowScriptedProbe(
                engine.Context.GetService<PhysicsSystem>(),
                engine.Context.GetService<ParticleSystem>(),
                engine.Context.GetService<ScriptLightingSynchronizer>());
            scriptedProbe.RegisterPhases(engine.Phases);
            Console.WriteLine("脚本化窗口输入已启用。");
        }

        Console.WriteLine("窗口运行时已接入 Rendering/Input 后端。");
        if (options.WindowTicks > 0)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int executed = 0;
            for (; executed < options.WindowTicks &&
                engine.State != EngineRunState.Shutdown &&
                !engine.IsShutdownRequested &&
                !window.IsClosing; executed++)
            {
                _ = engine.RunOneTick();
            }

            Console.WriteLine($"窗口短跑完成：frames={engine.Context.Clock.FrameIndex}, requested={options.WindowTicks}。");
            Console.WriteLine(
                $"窗口短跑耗时：elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:0.00}, " +
                $"avg_tick_ms={(executed == 0 ? 0 : stopwatch.Elapsed.TotalMilliseconds / executed):0.00}, " +
                $"last_profile_ms={Sum(engine.Context.Profiler.LastFrame):0.00}。");
            if (scriptedInput is not null)
            {
                WriteScriptedWindowSummary(engine, scriptedInput, scriptedProbe);
            }

            return;
        }

        engine.Run();
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

    private static void WriteScriptedWindowSummary(
        Engine engine,
        DemoWindowScriptedInput scriptedInput,
        DemoWindowScriptedProbe? scriptedProbe)
    {
        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        MaterialBrush? brush = FindBehaviour<MaterialBrush>(scene);
        ExplosiveTool? explosive = FindBehaviour<ExplosiveTool>(scene);
        CellGrid grid = engine.Context.GetService<CellGrid>();
        ParticleSystem particles = engine.Context.GetService<ParticleSystem>();
        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        ScriptLightingSynchronizer lighting = engine.Context.GetService<ScriptLightingSynchronizer>();
        ushort paintedMaterial = grid.MaterialAt(
            (int)MathF.Round(scriptedInput.BrushTargetWorld.X),
            (int)MathF.Round(scriptedInput.BrushTargetWorld.Y));
        string brushMaterial = brush?.SelectedMaterialName ?? "<missing>";

        Console.WriteLine(
            $"脚本化窗口输入摘要：frames={scriptedInput.FramesInjected}, " +
            $"brush_material={brushMaterial}, " +
            $"brush_radius={brush?.Radius ?? 0}, " +
            $"painted_material={paintedMaterial}, " +
            $"explosions={explosive?.ExplosionCount ?? 0}, " +
            $"last_explosion=({explosive?.LastExplosionX ?? 0:0.00},{explosive?.LastExplosionY ?? 0:0.00}), " +
            $"particles={particles.ActiveCount}, " +
            $"max_particles={scriptedProbe?.MaxParticles ?? particles.ActiveCount}, " +
            $"lights={lighting.PointLights.Length}, " +
            $"max_lights={scriptedProbe?.MaxLights ?? lighting.PointLights.Length}, " +
            $"physics_destroyed={physics.LastDestructionResult.DestroyedBodies}, " +
            $"physics_created={physics.LastDestructionResult.CreatedBodies}, " +
            $"max_physics_destroyed={scriptedProbe?.MaxDestroyedBodies ?? physics.LastDestructionResult.DestroyedBodies}, " +
            $"max_physics_created={scriptedProbe?.MaxCreatedBodies ?? physics.LastDestructionResult.CreatedBodies}。");
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
    /// 构造 Demo 项目模型。若场景文件不存在，则回退到程序化 LevelDirector 来源。
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
                : new SceneDescriptor(
                    DemoStartupOptions.DefaultSceneName,
                    SceneSourceKind.Procedural,
                    DemoStartupOptions.DefaultProceduralSceneKey);
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
