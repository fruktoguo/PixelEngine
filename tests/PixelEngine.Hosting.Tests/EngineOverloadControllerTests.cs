using PixelEngine.Core;
using PixelEngine.Core.Time;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Hosting 过载降级编排测试。
/// </summary>
public sealed class EngineOverloadControllerTests
{
    /// <summary>
    /// 验证连续超预算会按架构 §4.3 的五级顺序升级质量档位。
    /// </summary>
    [Fact]
    public void OverloadControllerEscalatesQualityTierInOrder()
    {
        EngineOverloadController controller = new(new EngineOverloadOptions(frameBudgetMs: 10, sustainWindow: 2));

        Assert.Equal(EngineQualityTier.Full, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.ReducedThermal, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.ReducedThermal, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.ReducedLighting, controller.SubmitFrame(11));
        Assert.Equal(EngineQualityTier.ReducedLighting, controller.SubmitFrame(0));
        controller.ResetToFullQuality();

        Assert.Equal(EngineQualityTier.Full, controller.QualityTier);
    }

    /// <summary>
    /// 验证 Engine 在降级到 Sim30Hz 后通过 FrameClock 下发 sim 降频。
    /// </summary>
    [Fact]
    public void EngineAppliesSim30HzQualityTierToFrameClock()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithOverloadPolicy(frameBudgetMs: 10, sustainWindow: 1)
            .Build();

        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);

        Assert.Equal(EngineQualityTier.Sim30Hz, engine.Context.QualityTier);
        Assert.Equal(EngineConstants.SimHzDownscaled, engine.Context.Clock.SimHz);
        Assert.Equal(EngineConstants.SimHzDownscaled, engine.Context.Counters.SimHz);
    }

    /// <summary>
    /// 验证人工过载降到 30Hz 后，render/streaming 相位仍逐帧执行且 sim 不追帧。
    /// </summary>
    [Fact]
    public void OverloadedSim30HzKeepsRenderFramesWithoutCatchUp()
    {
        List<EnginePhase> phases = [];
        EngineBuilder builder = new EngineBuilder()
            .WithWorkerCount(1)
            .WithOverloadPolicy(frameBudgetMs: 10, sustainWindow: 1);
        RegisterAllPhases(builder, phases);
        using Engine engine = builder.Build();

        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        _ = engine.RunOneTick(realDeltaSeconds: 0.011);
        Assert.Equal(EngineQualityTier.Sim30Hz, engine.Context.QualityTier);

        phases.Clear();
        FrameTiming simFrame = engine.RunOneTick(realDeltaSeconds: 0);
        long simTicksAfterSimFrame = engine.Context.Clock.SimTickIndex;
        phases.Clear();
        FrameTiming renderOnlyFrame = engine.RunOneTick(realDeltaSeconds: 0);

        Assert.True(simFrame.RunSim);
        Assert.False(renderOnlyFrame.RunSim);
        Assert.Equal(simTicksAfterSimFrame, engine.Context.Clock.SimTickIndex);
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
    /// 验证过载控制器通过 EngineContext 服务表暴露给脚本服务后端与 Editor。
    /// </summary>
    [Fact]
    public void OverloadControllerIsRegisteredAsRuntimeService()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();

        EngineOverloadController overload = engine.Context.GetService<EngineOverloadController>();

        Assert.Same(overload, engine.Context.GetService<EngineOverloadController>());
        Assert.Equal(EngineQualityTier.Full, overload.QualityTier);
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
