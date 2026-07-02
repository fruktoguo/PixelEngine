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
