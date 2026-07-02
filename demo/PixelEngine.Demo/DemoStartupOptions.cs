using PixelEngine.Rendering;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 启动参数。
/// </summary>
public sealed class DemoStartupOptions
{
    /// <summary>
    /// 默认关卡名称。
    /// </summary>
    public const string DefaultSceneName = "lava-mine";

    /// <summary>
    /// 默认程序化关卡生成器键。
    /// </summary>
    public const string DefaultProceduralSceneKey = "LevelDirector";

    /// <summary>
    /// 是否启用内嵌编辑器。
    /// </summary>
    public bool EnableEditor { get; init; }

    /// <summary>
    /// 是否启用脚本热重载。
    /// </summary>
    public bool HotReloadEnabled { get; init; } = true;

    /// <summary>
    /// 是否以 headless 冒烟模式运行。
    /// </summary>
    public bool Headless { get; init; }

    /// <summary>
    /// headless 模式下执行的 tick 数。
    /// </summary>
    public int HeadlessTicks { get; init; } = 1;

    /// <summary>
    /// 窗口模式下执行的有限 tick 数；0 表示进入持续运行主循环。
    /// </summary>
    public int WindowTicks { get; init; }

    /// <summary>
    /// 是否在有限窗口短跑中注入固定的 Demo 键鼠脚本，用于自动化窗口态玩法链路验收。
    /// </summary>
    public bool ScriptedWindowDemo { get; init; }

    /// <summary>
    /// 显式请求的自由粒子渲染模式；为空表示使用渲染管线默认值。
    /// </summary>
    public ParticleRenderMode? ParticleRenderMode { get; init; }

    /// <summary>
    /// 内容根目录。
    /// </summary>
    public string ContentRoot { get; init; } = Path.Combine(AppContext.BaseDirectory, "content");

    /// <summary>
    /// 要加载的场景名或场景路径。
    /// </summary>
    public string Scene { get; init; } = Path.Combine("scenes", DefaultSceneName + ".scene");

    /// <summary>
    /// 异常日志目录。
    /// </summary>
    public string LogDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>
    /// 从命令行解析启动参数。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>启动参数。</returns>
    public static DemoStartupOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        bool enableEditor = false;
        bool hotReload = true;
        bool headless = false;
        int ticks = 1;
        int windowTicks = 0;
        bool scriptedWindowDemo = false;
        ParticleRenderMode? particleRenderMode = null;
        string contentRoot = Path.Combine(AppContext.BaseDirectory, "content");
        string scene = Path.Combine("scenes", DefaultSceneName + ".scene");
        string logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--editor":
                    enableEditor = true;
                    headless = false;
                    break;
                case "--headless":
                    headless = true;
                    break;
                case "--smoke":
                    headless = true;
                    hotReload = false;
                    ticks = 1;
                    break;
                case "--no-hot-reload":
                    hotReload = false;
                    break;
                case "--scene":
                    scene = ReadValue(args, ref i, "--scene");
                    break;
                case "--content":
                    contentRoot = ReadValue(args, ref i, "--content");
                    break;
                case "--ticks":
                    {
                        string value = ReadValue(args, ref i, "--ticks");
                        if (!int.TryParse(value, out ticks) || ticks < 0)
                        {
                            throw new ArgumentOutOfRangeException(nameof(args), value, "--ticks 必须是非负整数。");
                        }

                        break;
                    }
                case "--window-ticks":
                    {
                        string value = ReadValue(args, ref i, "--window-ticks");
                        if (!int.TryParse(value, out windowTicks) || windowTicks < 0)
                        {
                            throw new ArgumentOutOfRangeException(nameof(args), value, "--window-ticks 必须是非负整数。");
                        }

                        break;
                    }
                case "--scripted-window-demo":
                    scriptedWindowDemo = true;
                    break;
                case "--particle-render-mode":
                    particleRenderMode = ParseParticleRenderMode(ReadValue(args, ref i, "--particle-render-mode"));
                    break;
                case "--log-dir":
                    logDirectory = ReadValue(args, ref i, "--log-dir");
                    break;
                default:
                    throw new ArgumentException($"未知 Demo 参数：{args[i]}", nameof(args));
            }
        }

        ValidateWindowTicks(headless, windowTicks);
        ValidateScriptedWindowDemo(headless, windowTicks, scriptedWindowDemo);
        ValidateParticleRenderMode(headless, particleRenderMode);
        return new DemoStartupOptions
        {
            EnableEditor = enableEditor,
            HotReloadEnabled = hotReload,
            Headless = headless,
            HeadlessTicks = ticks,
            WindowTicks = windowTicks,
            ScriptedWindowDemo = scriptedWindowDemo,
            ParticleRenderMode = particleRenderMode,
            ContentRoot = contentRoot,
            Scene = scene,
            LogDirectory = logDirectory,
        };
    }

    private static void ValidateWindowTicks(bool headless, int windowTicks)
    {
        _ = headless && windowTicks > 0
            ? throw new ArgumentException("--window-ticks 只能用于窗口模式，不能与 --headless/--smoke 同时使用。", nameof(windowTicks))
            : true;
    }

    private static void ValidateScriptedWindowDemo(bool headless, int windowTicks, bool scriptedWindowDemo)
    {
        _ = scriptedWindowDemo && (headless || windowTicks <= 0)
            ? throw new ArgumentException("--scripted-window-demo 只能与窗口有限短跑 --window-ticks 一起使用。", nameof(scriptedWindowDemo))
            : true;
    }

    private static void ValidateParticleRenderMode(bool headless, ParticleRenderMode? particleRenderMode)
    {
        _ = headless && particleRenderMode.HasValue
            ? throw new ArgumentException("--particle-render-mode 只能用于窗口模式。", nameof(particleRenderMode))
            : true;
    }

    private static ParticleRenderMode ParseParticleRenderMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "cpu" or "cpu-stamp" => PixelEngine.Rendering.ParticleRenderMode.CpuStamp,
            "gpu" or "gpu-point-sprite" => PixelEngine.Rendering.ParticleRenderMode.GpuPointSprite,
            _ => throw new ArgumentException("--particle-render-mode 仅支持 cpu 或 gpu。", nameof(value)),
        };
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        return index + 1 >= args.Length
            ? throw new ArgumentException($"{option} 缺少参数值。", nameof(args))
            : args[++index];
    }
}
