using PixelEngine.Rendering;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Demo 启动参数解析测试。
/// </summary>
public sealed class DemoStartupOptionsTests
{
    /// <summary>
    /// 验证默认启动进入窗口模式，保留裸 dotnet run 的可玩入口语义。
    /// </summary>
    [Fact]
    public void DefaultOptionsSelectWindowedRuntime()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([]);

        Assert.False(options.Headless);
        Assert.True(options.HotReloadEnabled);
        Assert.Contains(DemoStartupOptions.DefaultSceneName, options.Scene);
        Assert.Equal("playable-world", DemoStartupOptions.DefaultSceneName);
    }

    /// <summary>
    /// 验证默认项目模型进入可玩程序化场景，而不是 lava-mine 验收场景。
    /// </summary>
    [Fact]
    public void DefaultProjectUsesPlayableProceduralScene()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([]);

        PixelEngine.Hosting.EngineProject project = DemoProgram.BuildProject(options);
        PixelEngine.Hosting.SceneDescriptor scene = project.Scenes[0];

        Assert.Equal("playable-world", project.StartScene);
        Assert.Equal(PixelEngine.Hosting.SceneSourceKind.Procedural, scene.SourceKind);
        Assert.Equal(DemoStartupOptions.DefaultProceduralSceneKey, scene.Source);
    }

    /// <summary>
    /// 验证默认可玩程序化场景从 AI 材质地图导入 cell，而不是只走旧的数学地形填充。
    /// </summary>
    [Fact]
    public void DefaultPlayableWorldImportsAiMaterialMap()
    {
        string contentRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
        DemoStartupOptions options = DemoStartupOptions.Parse([
            "--headless",
            "--no-hot-reload",
            "--content",
            contentRoot,
        ]);
        PixelEngine.Hosting.EngineProject project = DemoProgram.BuildProject(options);
        using PixelEngine.Hosting.Engine engine = DemoProgram.BuildEngine(options, project);
        PlayableCavernWorldGenerator generator = new(Path.Combine(contentRoot, PlayableCavernWorldGenerator.DefaultMaterialMapRelativePath));
        engine.RegisterProceduralWorldGenerator(
            PlayableCavernWorldGenerator.Key,
            generator);
        PixelEngine.Hosting.EngineContentPackage package = engine.LoadContentPackage();
        Assert.True(package.MaterialCount > 0);
        PixelEngine.World.WorldLoadResult? worldLoad = engine.AttachCurrentSceneWorld();
        Assert.Null(worldLoad);

        MaterialTable materials = engine.Context.GetService<MaterialTable>();
        CellGrid grid = engine.Context.GetService<CellGrid>();
        PixelEngine.Hosting.ProceduralWorldDescriptor descriptor = generator.Describe(default);
        Assert.True(materials.TryGetId("acid", out ushort acid));
        Assert.True(materials.TryGetId("metal", out ushort metal));
        Assert.True(materials.TryGetId("wood", out ushort wood));
        Assert.True(materials.TryGetId("water", out ushort water));
        Assert.True(materials.TryGetId("lava", out ushort lava));

        int acidCells = 0;
        int metalCells = 0;
        int waterCells = 0;
        int lavaCells = 0;
        for (int y = 0; y < descriptor.HeightCells; y++)
        {
            for (int x = 0; x < descriptor.WidthCells; x++)
            {
                ushort material = grid.MaterialAt(x, y);
                acidCells += material == acid ? 1 : 0;
                metalCells += material == metal ? 1 : 0;
                waterCells += material == water ? 1 : 0;
                lavaCells += material == lava ? 1 : 0;
            }
        }

        Assert.True(acidCells > 100, $"AI 图里的酸液区域应进入世界，actual={acidCells}");
        Assert.True(metalCells > 100, $"AI 图里的矿脉应进入世界，actual={metalCells}");
        Assert.True(waterCells > 100, $"AI 图里的水池应进入世界，actual={waterCells}");
        Assert.True(lavaCells > 100, $"AI 图里的熔岩池应进入世界，actual={lavaCells}");
        Assert.Equal(wood, grid.MaterialAt(72, 188));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }

    /// <summary>
    /// 验证默认 Demo 使用玩家友好的窗口尺寸，并把内部渲染画布固定为 720x480。
    /// </summary>
    [Fact]
    public void DefaultEngineUsesPlayableWindowSize()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(["--no-hot-reload"]);
        PixelEngine.Hosting.EngineProject project = DemoProgram.BuildProject(options);
        using PixelEngine.Hosting.Engine engine = DemoProgram.BuildEngine(options, project);

        Assert.Equal(1080, engine.Context.Options.WindowWidth);
        Assert.Equal(720, engine.Context.Options.WindowHeight);
        Assert.Equal(720, engine.Context.Options.InternalWidth);
        Assert.Equal(480, engine.Context.Options.InternalHeight);
    }

    /// <summary>
    /// 验证发行冒烟参数显式进入 headless、禁用 hot reload，并只执行一个 tick。
    /// </summary>
    [Fact]
    public void SmokeOptionSelectsHeadlessSingleTickWithoutHotReload()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(["--smoke"]);

        Assert.True(options.Headless);
        Assert.False(options.HotReloadEnabled);
        Assert.Equal(1, options.HeadlessTicks);
    }

    /// <summary>
    /// 验证窗口短跑参数保持真实窗口模式，但允许测试/发行脚本在固定 tick 后退出。
    /// </summary>
    [Fact]
    public void WindowTicksSelectsFiniteWindowedRuntime()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(["--no-hot-reload", "--window-ticks", "60", "--capture-frame", "artifacts/demo.bmp"]);

        Assert.False(options.Headless);
        Assert.False(options.HotReloadEnabled);
        Assert.Equal(60, options.WindowTicks);
        Assert.Equal("artifacts/demo.bmp", options.CaptureFramePath);
    }

    /// <summary>
    /// 验证脚本化窗口 Demo 只能绑定有限窗口短跑，避免伪装成 headless 验收。
    /// </summary>
    [Fact]
    public void ScriptedWindowDemoRequiresFiniteWindowTicks()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(["--no-hot-reload", "--window-ticks", "60", "--scripted-window-demo"]);

        Assert.True(options.ScriptedWindowDemo);
        Assert.False(options.ScriptedWindowRoute);
        Assert.Equal(60, options.WindowTicks);
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--scripted-window-demo"]));
    }

    /// <summary>
    /// 验证完整路线窗口探针复用真实窗口短跑约束，并显式进入 route 输入脚本。
    /// </summary>
    [Fact]
    public void ScriptedWindowRouteEnablesRouteInputOnlyForFiniteWindowRuns()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(["--no-hot-reload", "--window-ticks", "600", "--scripted-window-route"]);

        Assert.True(options.ScriptedWindowDemo);
        Assert.True(options.ScriptedWindowRoute);
        Assert.Equal(600, options.WindowTicks);
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--scripted-window-route"]));
    }

    /// <summary>
    /// 验证窗口态可显式请求 CPU/GPU 粒子渲染模式，作为真实窗口粒子帧时间 probe 的前置开关。
    /// </summary>
    [Fact]
    public void ParticleRenderModeOptionIsWindowOnlyAndParsesCpuGpu()
    {
        DemoStartupOptions cpu = DemoStartupOptions.Parse(["--window-ticks", "1", "--particle-render-mode", "cpu"]);
        DemoStartupOptions gpu = DemoStartupOptions.Parse(["--window-ticks", "1", "--particle-render-mode", "gpu"]);

        Assert.Equal(ParticleRenderMode.CpuStamp, cpu.ParticleRenderMode);
        Assert.Equal(ParticleRenderMode.GpuPointSprite, gpu.ParticleRenderMode);
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--headless", "--particle-render-mode", "gpu"]));
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--window-ticks", "1", "--particle-render-mode", "bad"]));
    }

    /// <summary>
    /// 验证高密度粒子帧时间探针只能绑定有限窗口短跑，并解析粒子数与预热帧。
    /// </summary>
    [Fact]
    public void ParticleFrameProbeRequiresFiniteWindowTicksAndParsesParameters()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([
            "--window-ticks", "12",
            "--particle-frame-probe",
            "--particle-count", "100000",
            "--particle-probe-warmup", "3",
        ]);

        Assert.True(options.ParticleFrameProbe);
        Assert.Equal(100_000, options.ParticleProbeCount);
        Assert.Equal(3, options.ParticleProbeWarmupFrames);
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--particle-frame-probe"]));
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--headless", "--particle-frame-probe"]));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => DemoStartupOptions.Parse(["--window-ticks", "1", "--particle-frame-probe", "--particle-count", "262145"]));
    }

    /// <summary>
    /// 验证窗口短跑不能和 headless 冒烟混用，避免调用方误以为覆盖了窗口路径。
    /// </summary>
    [Fact]
    public void WindowTicksRejectsHeadlessRuntime()
    {
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--headless", "--window-ticks", "1"]));
    }

    /// <summary>
    /// 验证 NativeAOT 等不支持动态代码的运行时会显式禁用脚本热重载，而不是尝试走 Roslyn/ALC 路径。
    /// </summary>
    [Fact]
    public void HotReloadRequiresDynamicCodeSupport()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([]);

        Assert.True(DemoProgram.CanEnableHotReload(options, dynamicCodeSupported: true));
        Assert.False(DemoProgram.CanEnableHotReload(options, dynamicCodeSupported: false));
        Assert.False(DemoProgram.CanEnableHotReload(DemoStartupOptions.Parse(["--no-hot-reload"]), dynamicCodeSupported: true));
    }
}
