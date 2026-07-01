using System.Reflection;
using System.Runtime.InteropServices;
using PixelEngine.Hosting;

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
            Scene? currentScene = engine.Context.GetService<ISceneService>().Current;
            if (currentScene?.Descriptor.SourceKind == SceneSourceKind.SaveDirectory)
            {
                _ = engine.AttachWorldFromSaveDirectory(currentScene.ResolvedSource!);
                Console.WriteLine($"世界存档已加载：{currentScene.ResolvedSource}");
            }
            else
            {
                _ = engine.AttachResidentSimulationWorld(DemoWorldWidthCells, DemoWorldHeightCells);
            }

            int audioClips = engine.AttachAudioFromContentAsync().AsTask().GetAwaiter().GetResult();
            contentLoaded = true;
            Console.WriteLine($"内容包已加载：{package.MaterialCount} 个材质，{package.ReactionCount} 条反应，{audioClips} 个音频 clip。");
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
            _ = engine.AttachScriptingFromServices();
            Console.WriteLine("脚本运行时已接入 Hosting/Simulation 后端。");
        }

        if (options.Headless)
        {
            engine.RunHeadlessTicks(options.HeadlessTicks);
            Scene? current = engine.Context.GetService<ISceneService>().Current;
            Console.WriteLine($"Engine frame: {engine.Context.Clock.FrameIndex}, scene: {current?.Name}");
            return;
        }

        _ = engine.AttachWindowRuntime();
        Console.WriteLine("窗口运行时已接入 Rendering/Input 后端。");
        engine.Run();
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
