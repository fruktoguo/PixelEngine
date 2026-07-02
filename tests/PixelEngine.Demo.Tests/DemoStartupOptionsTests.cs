using PixelEngine.Rendering;
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
        DemoStartupOptions options = DemoStartupOptions.Parse(["--no-hot-reload", "--window-ticks", "60"]);

        Assert.False(options.Headless);
        Assert.False(options.HotReloadEnabled);
        Assert.Equal(60, options.WindowTicks);
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
