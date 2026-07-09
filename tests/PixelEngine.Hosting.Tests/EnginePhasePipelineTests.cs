using PixelEngine.Core;
using PixelEngine.Core.Diagnostics;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Engine 12 相位调度测试。
/// 不变式：12 相位按文档顺序执行、相位边界不交叉写入共享状态。
/// </summary>
public sealed class EnginePhasePipelineTests
{
    /// <summary>
    /// 验证 60Hz sim 帧会按架构 §3.3 顺序执行全部 12 相位。
    /// </summary>
    [Fact]
    public void RunOneTickExecutesAllPhasesInOrderWhenSimRuns()
    {
        List<EnginePhase> phases = [];
        EngineBuilder builder = new EngineBuilder()
            .WithWorkerCount(1);
        RegisterAllPhases(builder, phases);
        using Engine engine = builder.Build();

        _ = engine.RunOneTick();

        Assert.Equal(
            [
                EnginePhase.InputAndTime,
                EnginePhase.GameLogicAndScripts,
                EnginePhase.ResidencyApply,
                EnginePhase.ParticleToCell,
                EnginePhase.CaSimulation,
                EnginePhase.Temperature,
                EnginePhase.DirtyRectSwap,
                EnginePhase.CellToParticle,
                EnginePhase.PhysicsSync,
                EnginePhase.BuildRenderBuffer,
                EnginePhase.GpuUploadAndRender,
                EnginePhase.WorldStreaming,
            ],
            phases);
    }

    /// <summary>
    /// 验证 sim 降到 30Hz 的跳帧仍执行 render 与 streaming 相位，不 accumulator 追帧。
    /// </summary>
    [Fact]
    public void DownscaledSimFrameSkipsSimPhasesButKeepsRenderAndStreaming()
    {
        List<EnginePhase> phases = [];
        EngineBuilder builder = new EngineBuilder()
            .WithWorkerCount(1)
            .WithSimHz(EngineConstants.SimHzDownscaled);
        RegisterAllPhases(builder, phases);
        using Engine engine = builder.Build();

        _ = engine.RunOneTick();
        phases.Clear();
        _ = engine.RunOneTick();

        Assert.Equal(
            [
                EnginePhase.InputAndTime,
                EnginePhase.GameLogicAndScripts,
                EnginePhase.BuildRenderBuffer,
                EnginePhase.GpuUploadAndRender,
                EnginePhase.WorldStreaming,
            ],
            phases);
    }

    /// <summary>
    /// 验证 phase pipeline 被注册进 EngineContext 服务表。
    /// </summary>
    [Fact]
    public void PhasePipelineIsAvailableFromContext()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .OnPhase(EnginePhase.WorldStreaming, static _ => { })
            .Build();

        EnginePhasePipeline phases = engine.Context.GetService<EnginePhasePipeline>();

        Assert.Same(engine.Phases, phases);
        Assert.Equal(1, phases.Count(EnginePhase.WorldStreaming));
    }

    /// <summary>
    /// 验证 12 相位执行结果会写入 Core 帧诊断计时器。
    /// </summary>
    [Fact]
    public void RunOneTickRecordsProfilerTimesForExecutedPhases()
    {
        EngineBuilder builder = new EngineBuilder()
            .WithWorkerCount(1);
        for (int i = 0; i < 12; i++)
        {
            EnginePhase phase = (EnginePhase)i;
            _ = builder.OnPhase(phase, static _ => Thread.SpinWait(50_000));
        }

        using Engine engine = builder.Build();

        _ = engine.RunOneTick();

        ReadOnlySpan<double> lastFrame = engine.Context.Profiler.LastFrame;
        Assert.Equal(12, lastFrame.Length);
        for (int i = 0; i < lastFrame.Length; i++)
        {
            Assert.True(lastFrame[i] > 0, $"{(FramePhase)i} 应记录大于 0 的相位耗时。");
        }

        Assert.True(engine.Context.Profiler.Average(FramePhase.CaSimulation, 1) > 0);
    }

    private static void RegisterAllPhases(EngineBuilder builder, List<EnginePhase> phases)
    {
        for (int i = 0; i < 12; i++)
        {
            EnginePhase phase = (EnginePhase)i;
            _ = builder.OnPhase(phase, context => phases.Add(context.Phase));
        }
    }
}
