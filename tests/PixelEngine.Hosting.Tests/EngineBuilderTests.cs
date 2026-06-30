using PixelEngine.Core;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Events;
using PixelEngine.Core.Threading;
using PixelEngine.Core.Time;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// EngineBuilder、EngineContext 与 Engine 生命周期测试。
/// </summary>
public sealed class EngineBuilderTests
{
    /// <summary>
    /// 验证 builder 能装配 Core 服务并把配置写入 EngineContext。
    /// </summary>
    [Fact]
    public void BuildCreatesEngineContextWithCoreServices()
    {
        using Engine engine = new EngineBuilder()
            .WithWindow(1920, 1080)
            .WithInternalResolution(960, 540)
            .WithWorkerCount(1)
            .WithGcMode(EngineGcMode.SustainedLowLatency)
            .WithContentRoot("content-test")
            .WithStartScene("scenes/start.scene")
            .WithEventCapacityPerChannel(64)
            .Build();

        EngineContext context = engine.Context;
        Assert.Equal(EngineRunState.Created, engine.State);
        Assert.Equal(1920, context.Options.WindowWidth);
        Assert.Equal(1080, context.Options.WindowHeight);
        Assert.Equal(960, context.Options.InternalWidth);
        Assert.Equal(540, context.Options.InternalHeight);
        Assert.Equal(EngineGcMode.SustainedLowLatency, context.Options.GcMode);
        Assert.Equal("content-test", context.Options.ContentRoot);
        Assert.Equal("scenes/start.scene", context.Options.StartScene);
        Assert.Equal(64, context.Events.CapacityPerChannel);
        Assert.Same(context, context.GetService<EngineContext>());
        Assert.Same(context.Jobs, context.GetService<JobSystem>());
        Assert.Same(context.Clock, context.GetService<FrameClock>());
        Assert.Same(context.Events, context.GetService<EventBus>());
        Assert.Same(context.Counters, context.GetService<EngineCounters>());
    }

    /// <summary>
    /// 验证 headless 与确定性模式会关闭 GPU/Editor 并固定单 worker。
    /// </summary>
    [Fact]
    public void HeadlessDeterministicBuildAppliesRuntimeFlags()
    {
        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .Build();

        Assert.True(engine.Context.Options.Headless);
        Assert.True(engine.Context.Options.DeterministicMode);
        Assert.False(engine.Context.Options.EnableEditor);
        Assert.False(engine.Context.Options.EnableGpu);
        Assert.Equal(1, engine.Context.Options.WorkerCount);
        Assert.Equal(1, engine.Context.Jobs.WorkerCount);
    }

    /// <summary>
    /// 验证 RunOneTick 只推进一次固定步长时钟，不追补多步。
    /// </summary>
    [Fact]
    public void RunOneTickAdvancesFrameClockWithoutCatchUp()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithSimHz(EngineConstants.SimHzDownscaled)
            .Build();

        FrameTiming first = engine.RunOneTick(realDeltaSeconds: 1.0);
        FrameTiming second = engine.RunOneTick(realDeltaSeconds: 1.0);

        Assert.Equal(EngineRunState.Running, engine.State);
        Assert.True(first.RunSim);
        Assert.False(second.RunSim);
        Assert.Equal(1, first.SimTickIndex);
        Assert.Equal(1, second.SimTickIndex);
        Assert.Equal(2, second.FrameIndex);
        Assert.Equal(EngineConstants.SimHzDownscaled, engine.Context.Counters.SimHz);
    }

    /// <summary>
    /// 验证 Shutdown 释放 JobSystem，关闭后禁止继续 tick。
    /// </summary>
    [Fact]
    public void ShutdownDisposesCoreRuntime()
    {
        Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        JobSystem jobs = engine.Context.Jobs;

        engine.Shutdown();

        Assert.Equal(EngineRunState.Shutdown, engine.State);
        _ = Assert.Throws<ObjectDisposedException>(() => engine.RunOneTick());
        _ = Assert.Throws<ObjectDisposedException>(() => jobs.ParallelRange(1, 1, static (_, _, _, _) => { }));
        engine.Dispose();
    }

    /// <summary>
    /// 验证 builder 对非法配置快速失败。
    /// </summary>
    [Fact]
    public void BuilderRejectsInvalidConfiguration()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new EngineBuilder().WithWorkerCount(-1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new EngineBuilder().WithWindow(0, 720));
        _ = Assert.Throws<ArgumentException>(() => new EngineBuilder().WithContentRoot(""));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new EngineBuilder().WithEventCapacityPerChannel(63).Build());
    }
}
