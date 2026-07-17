using PixelEngine.Core;
using PixelEngine.Hosting;
using PixelEngine.Rendering;
using PixelEngine.UI;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 启动参数。
/// </summary>
public sealed class DemoStartupOptions
{
    /// <summary>
    /// 默认关卡名称。
    /// </summary>
    public const string DefaultSceneName = "playable-world";

    /// <summary>
    /// 默认程序化关卡生成器键。
    /// </summary>
    public const string DefaultProceduralSceneKey = "PixelEngine.Demo.PlayableWorldDirector";

    /// <summary>
    /// 默认玩家窗口宽度。
    /// </summary>
    public const int DefaultWindowWidth = 1080;

    /// <summary>
    /// 默认玩家窗口高度。
    /// </summary>
    public const int DefaultWindowHeight = 720;

    /// <summary>
    /// 是否启用脚本热重载。
    /// </summary>
    public bool HotReloadEnabled { get; init; } = true;

    /// <summary>
    /// 窗口模式是否启用垂直同步。
    /// </summary>
    public bool VSync { get; init; } = true;

    /// <summary>
    /// 窗口标题。
    /// </summary>
    public string WindowTitle { get; init; } = EngineOptions.DefaultWindowTitle;

    /// <summary>
    /// 窗口宽度。
    /// </summary>
    public int WindowWidth { get; init; } = DefaultWindowWidth;

    /// <summary>
    /// 窗口高度。
    /// </summary>
    public int WindowHeight { get; init; } = DefaultWindowHeight;

    /// <summary>
    /// 独立 Player 在首帧前应用的平台窗口模式。
    /// </summary>
    public PlayerWindowMode WindowMode { get; init; } = PlayerWindowMode.Windowed;

    /// <summary>
    /// 游戏 UI 后端。
    /// </summary>
    public UiBackendKind RuntimeUiBackend { get; init; } = UiBackendKind.ManagedFallback;

    /// <summary>
    /// 玩家发行通道。
    /// </summary>
    public PlayerReleaseChannel ReleaseChannel { get; init; } = PlayerReleaseChannel.Development;

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
    /// 是否启用完整路线脚本化窗口探针；该模式会在默认关卡中持续输入移动，而不是打开暂停菜单。
    /// </summary>
    public bool ScriptedWindowRoute { get; init; }

    /// <summary>
    /// 显式请求的自由粒子渲染模式；为空表示使用渲染管线默认值。
    /// </summary>
    public ParticleRenderMode? ParticleRenderMode { get; init; }

    /// <summary>
    /// 是否启用高密度粒子帧时间探针。
    /// </summary>
    public bool ParticleFrameProbe { get; init; }

    /// <summary>
    /// 粒子帧时间探针请求的活跃粒子数。
    /// </summary>
    public int ParticleProbeCount { get; init; } = 100_000;

    /// <summary>
    /// 粒子帧时间探针跳过统计的预热帧数。
    /// </summary>
    public int ParticleProbeWarmupFrames { get; init; } = 5;

    /// <summary>
    /// 粒子帧时间探针本次采样 run 的外部可审计标识。
    /// </summary>
    public string ParticleProbeRunId { get; init; } = "local";

    /// <summary>
    /// 内容根目录。
    /// </summary>
    public string ContentRoot { get; init; } = ResolveDefaultContentRoot(AppContext.BaseDirectory);

    /// <summary>
    /// 要加载的场景名或场景路径。
    /// </summary>
    public string Scene { get; init; } = Path.Combine("scenes", DefaultSceneName + ".scene");

    /// <summary>
    /// 异常日志目录。
    /// </summary>
    public string LogDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>
    /// 窗口有限短跑结束时从默认 framebuffer 读回的 BMP 截图路径；空字符串表示不截图。
    /// </summary>
    public string CaptureFramePath { get; init; } = string.Empty;

    /// <summary>
    /// 是否在真实窗口相位累计物理 Game UI 输入、捕获、事件与屏栈诊断。
    /// </summary>
    public bool PhysicalUiInputProbe { get; init; }

    /// <summary>
    /// 物理 UI 输入探针确认产品 UI 已挂载后原子创建的 ready 文件；空字符串表示不发布握手。
    /// </summary>
    public string PhysicalUiInputReadyFile { get; init; } = string.Empty;

    /// <summary>
    /// 从命令行解析启动参数。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>启动参数。</returns>
    public static DemoStartupOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        bool hotReload = true;
        string contentRoot = ResolveDefaultContentRoot(AppContext.BaseDirectory);
        EngineProjectStartupSettings startupSettings = ResolveStartupSettings(contentRoot);
        bool vSync = startupSettings.VSync;
        bool vSyncExplicitlySet = false;
        string windowTitle = startupSettings.WindowTitle;
        int windowWidth = startupSettings.WindowWidth;
        int windowHeight = startupSettings.WindowHeight;
        PlayerWindowMode windowMode = startupSettings.WindowMode;
        bool windowModeExplicitlySet = false;
        UiBackendKind runtimeUiBackend = startupSettings.RuntimeUiBackend;
        PlayerReleaseChannel releaseChannel = startupSettings.ReleaseChannel;
        bool headless = false;
        int ticks = 1;
        int windowTicks = 0;
        bool scriptedWindowDemo = false;
        bool scriptedWindowRoute = false;
        ParticleRenderMode? particleRenderMode = null;
        bool particleFrameProbe = false;
        int particleProbeCount = 100_000;
        int particleProbeWarmupFrames = 5;
        string particleProbeRunId = "local";
        string scene = startupSettings.StartScene;
        bool sceneExplicitlySet = false;
        string logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        string captureFramePath = string.Empty;
        bool physicalUiInputProbe = false;
        string physicalUiInputReadyFile = string.Empty;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
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
                case "--vsync":
                    vSync = true;
                    vSyncExplicitlySet = true;
                    break;
                case "--no-vsync":
                    vSync = false;
                    vSyncExplicitlySet = true;
                    break;
                case "--scene":
                    scene = ReadValue(args, ref i, "--scene");
                    sceneExplicitlySet = true;
                    break;
                case "--content":
                    contentRoot = ReadValue(args, ref i, "--content");
                    startupSettings = ResolveStartupSettings(contentRoot);
                    windowTitle = startupSettings.WindowTitle;
                    windowWidth = startupSettings.WindowWidth;
                    windowHeight = startupSettings.WindowHeight;
                    if (!windowModeExplicitlySet)
                    {
                        windowMode = startupSettings.WindowMode;
                    }

                    runtimeUiBackend = startupSettings.RuntimeUiBackend;
                    releaseChannel = startupSettings.ReleaseChannel;
                    if (!vSyncExplicitlySet)
                    {
                        vSync = startupSettings.VSync;
                    }

                    if (!sceneExplicitlySet)
                    {
                        scene = startupSettings.StartScene;
                    }

                    break;
                case "--window-mode":
                    windowMode = ParseWindowMode(ReadValue(args, ref i, "--window-mode"));
                    windowModeExplicitlySet = true;
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
                case "--scripted-window-route":
                    scriptedWindowDemo = true;
                    scriptedWindowRoute = true;
                    break;
                case "--particle-render-mode":
                    particleRenderMode = ParseParticleRenderMode(ReadValue(args, ref i, "--particle-render-mode"));
                    break;
                case "--particle-frame-probe":
                    particleFrameProbe = true;
                    break;
                case "--particle-count":
                    {
                        string value = ReadValue(args, ref i, "--particle-count");
                        if (!int.TryParse(value, out particleProbeCount) || particleProbeCount <= 0)
                        {
                            throw new ArgumentOutOfRangeException(nameof(args), value, "--particle-count 必须是正整数。");
                        }

                        break;
                    }
                case "--particle-probe-warmup":
                    {
                        string value = ReadValue(args, ref i, "--particle-probe-warmup");
                        if (!int.TryParse(value, out particleProbeWarmupFrames) || particleProbeWarmupFrames < 0)
                        {
                            throw new ArgumentOutOfRangeException(nameof(args), value, "--particle-probe-warmup 必须是非负整数。");
                        }

                        break;
                    }
                case "--particle-probe-run-id":
                    particleProbeRunId = ReadValue(args, ref i, "--particle-probe-run-id");
                    break;
                case "--log-dir":
                    logDirectory = ReadValue(args, ref i, "--log-dir");
                    break;
                case "--capture-frame":
                    captureFramePath = ReadValue(args, ref i, "--capture-frame");
                    break;
                case "--physical-ui-input-probe":
                    physicalUiInputProbe = true;
                    break;
                case "--physical-ui-input-ready-file":
                    physicalUiInputReadyFile = Path.GetFullPath(
                        ReadValue(args, ref i, "--physical-ui-input-ready-file"));
                    break;
                default:
                    throw new ArgumentException($"未知 Demo 参数：{args[i]}", nameof(args));
            }
        }

        ValidateWindowTicks(headless, windowTicks);
        ValidateScriptedWindowDemo(headless, windowTicks, scriptedWindowDemo);
        ValidateParticleRenderMode(headless, particleRenderMode);
        ValidateParticleFrameProbe(headless, windowTicks, particleFrameProbe, particleProbeCount, particleProbeRunId);
        ValidatePhysicalUiInputProbe(
            headless,
            windowTicks,
            physicalUiInputProbe,
            physicalUiInputReadyFile);
        return new DemoStartupOptions
        {
            HotReloadEnabled = hotReload,
            VSync = vSync,
            WindowTitle = windowTitle,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
            WindowMode = windowMode,
            RuntimeUiBackend = runtimeUiBackend,
            ReleaseChannel = releaseChannel,
            Headless = headless,
            HeadlessTicks = ticks,
            WindowTicks = windowTicks,
            ScriptedWindowDemo = scriptedWindowDemo,
            ScriptedWindowRoute = scriptedWindowRoute,
            ParticleRenderMode = particleRenderMode,
            ParticleFrameProbe = particleFrameProbe,
            ParticleProbeCount = particleProbeCount,
            ParticleProbeWarmupFrames = particleProbeWarmupFrames,
            ParticleProbeRunId = particleProbeRunId,
            ContentRoot = contentRoot,
            Scene = scene,
            LogDirectory = logDirectory,
            CaptureFramePath = captureFramePath,
            PhysicalUiInputProbe = physicalUiInputProbe,
            PhysicalUiInputReadyFile = physicalUiInputReadyFile,
        };
    }

    private static EngineProjectStartupSettings ResolveStartupSettings(string contentRoot)
    {
        EngineProjectStartupSettings fallback = EngineProjectStartupSettings.CreateDefault() with
        {
            StartScene = Path.Combine("scenes", DefaultSceneName + ".scene"),
            WindowTitle = EngineOptions.DefaultWindowTitle,
            WindowWidth = DefaultWindowWidth,
            WindowHeight = DefaultWindowHeight,
            WindowMode = PlayerWindowMode.Windowed,
            VSync = true,
            RuntimeUiBackend = UiBackendKind.ManagedFallback,
            ReleaseChannel = PlayerReleaseChannel.Development,
        };

        try
        {
            return EngineProjectSettingsStore.LoadStartupSettings(contentRoot, fallback);
        }
        catch (System.Text.Json.JsonException)
        {
            return fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
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

    private static void ValidateParticleFrameProbe(bool headless, int windowTicks, bool particleFrameProbe, int particleProbeCount, string particleProbeRunId)
    {
        _ = particleFrameProbe && (headless || windowTicks <= 0)
            ? throw new ArgumentException("--particle-frame-probe 只能与窗口有限短跑 --window-ticks 一起使用。", nameof(particleFrameProbe))
            : true;
        _ = particleProbeCount > EngineConstants.ParticleCapacityDefault
            ? throw new ArgumentOutOfRangeException(nameof(particleProbeCount), particleProbeCount, $"--particle-count 不能超过固定粒子容量 {EngineConstants.ParticleCapacityDefault}。")
            : true;
        _ = particleFrameProbe && string.IsNullOrWhiteSpace(particleProbeRunId)
            ? throw new ArgumentException("--particle-probe-run-id 必须是非空字符串。", nameof(particleProbeRunId))
            : true;
    }

    private static void ValidatePhysicalUiInputProbe(
        bool headless,
        int windowTicks,
        bool enabled,
        string readyFile)
    {
        _ = enabled && (headless || windowTicks <= 0)
            ? throw new ArgumentException("--physical-ui-input-probe 只能与窗口有限短跑 --window-ticks 一起使用。", nameof(enabled))
            : true;
        _ = !string.IsNullOrEmpty(readyFile) && !enabled
            ? throw new ArgumentException(
                "--physical-ui-input-ready-file 只能与 --physical-ui-input-probe 一起使用。",
                nameof(readyFile))
            : true;
    }

    private static ParticleRenderMode ParseParticleRenderMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "cpu" or "cpu-stamp" => Rendering.ParticleRenderMode.CpuStamp,
            "gpu" or "gpu-point-sprite" => Rendering.ParticleRenderMode.GpuPointSprite,
            _ => throw new ArgumentException("--particle-render-mode 仅支持 cpu 或 gpu。", nameof(value)),
        };
    }

    private static PlayerWindowMode ParseWindowMode(string value)
    {
        return Enum.TryParse(value, ignoreCase: true, out PlayerWindowMode mode) && Enum.IsDefined(mode)
            ? mode
            : throw new ArgumentException($"未知 --window-mode：{value}。", nameof(value));
    }

    /// <summary>
    /// 解析默认 content 目录；发行包允许真实程序集位于 <c>app/</c>，内容目录位于包根。
    /// </summary>
    /// <param name="baseDirectory">运行时基准目录，通常为 <see cref="AppContext.BaseDirectory"/>。</param>
    /// <returns>默认 content 目录。</returns>
    public static string ResolveDefaultContentRoot(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        string fullBase = Path.GetFullPath(baseDirectory);
        string localContent = Path.Combine(fullBase, "content");
        DirectoryInfo? directory = new(fullBase);
        if (string.Equals(directory.Name, "app", StringComparison.OrdinalIgnoreCase) &&
            directory.Parent is not null)
        {
            string packageRootContent = Path.Combine(directory.Parent.FullName, "content");
            if (Directory.Exists(packageRootContent))
            {
                return packageRootContent;
            }
        }

        return localContent;
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        return index + 1 >= args.Length
            ? throw new ArgumentException($"{option} 缺少参数值。", nameof(args))
            : args[++index];
    }
}
