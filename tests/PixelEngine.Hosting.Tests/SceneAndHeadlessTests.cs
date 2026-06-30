using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 场景、项目模型与 headless 驱动测试。
/// </summary>
public sealed class SceneAndHeadlessTests
{
    /// <summary>
    /// 验证 EngineBuilder 能从项目模型注册场景并切到起始场景。
    /// </summary>
    [Fact]
    public void BuildLoadsProjectScenesAndStartScene()
    {
        EngineProject project = new(
            "game-content",
            "start",
            [
                new SceneDescriptor("start", SceneSourceKind.Procedural, "terrain-a"),
                new SceneDescriptor("menu"),
            ]);

        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithProject(project)
            .Build();

        ISceneService scenes = engine.Context.GetService<ISceneService>();

        Assert.Equal("game-content", engine.Context.Options.ContentRoot);
        Assert.Equal("start", engine.Context.Options.StartScene);
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.SceneService));
        Assert.NotNull(scenes.Current);
        Assert.Equal("start", scenes.Current.Name);
        Assert.Equal("terrain-a", scenes.Current.ResolvedSource);
        Assert.True(scenes.Current.WorldConstructionPending);
        Assert.True(scenes.TryGet("menu", out SceneDescriptor menu));
        Assert.Equal(SceneSourceKind.Empty, menu.SourceKind);
    }

    /// <summary>
    /// 验证手动场景切换和卸载。
    /// </summary>
    [Fact]
    public void SceneServiceSwitchesAndUnloadsCurrentScene()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("a"))
            .AddScene(new SceneDescriptor("b", SceneSourceKind.SaveDirectory, "saves/b"))
            .Build();
        ISceneService scenes = engine.Context.GetService<ISceneService>();

        Scene loaded = scenes.SwitchTo("b");

        Assert.Same(loaded, scenes.Current);
        Assert.Equal(SceneSourceKind.SaveDirectory, loaded.Descriptor.SourceKind);
        Assert.Equal("saves/b", loaded.Descriptor.Source);
        Assert.Equal(Path.GetFullPath(Path.Combine(engine.Context.Options.ContentRoot, "saves/b")), loaded.ResolvedSource);
        Assert.True(loaded.WorldConstructionPending);

        scenes.UnloadCurrent();
        Assert.Null(scenes.Current);
        _ = Assert.Throws<InvalidOperationException>(() => scenes.SwitchTo("missing"));
    }

    /// <summary>
    /// 验证场景来源配置会快速拒绝无效组合。
    /// </summary>
    [Fact]
    public void SceneDescriptorRejectsInvalidSourceConfiguration()
    {
        _ = Assert.Throws<ArgumentException>(() => new SceneDescriptor("empty", SceneSourceKind.Empty, "unexpected"));
        _ = Assert.Throws<ArgumentException>(() => new SceneDescriptor("save", SceneSourceKind.SaveDirectory));
        _ = Assert.Throws<ArgumentException>(() => new SceneDescriptor("proc", SceneSourceKind.Procedural));
    }

    /// <summary>
    /// 验证 headless 模式可以按固定步数驱动，且非 headless 禁止使用该入口。
    /// </summary>
    [Fact]
    public void RunHeadlessTicksAdvancesFixedNumberOfFrames()
    {
        using Engine headless = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .Build();

        headless.RunHeadlessTicks(5);

        Assert.Equal(5, headless.Context.Clock.FrameIndex);
        Assert.Equal(5, headless.Context.Clock.SimTickIndex);
        Assert.False(headless.Context.Options.EnableGpu);

        using Engine windowed = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        _ = Assert.Throws<InvalidOperationException>(() => windowed.RunHeadlessTicks(1));
    }
}
