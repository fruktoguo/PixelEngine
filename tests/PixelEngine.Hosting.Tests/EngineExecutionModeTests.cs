using PixelEngine.Core.Time;
using PixelEngine.Core;
using PixelEngine.Editor;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Play/Edit/Step 执行模式测试。
/// </summary>
public sealed class EngineExecutionModeTests
{
    /// <summary>
    /// 验证编辑模式普通 tick 只推进渲染帧，不推进 sim 相位。
    /// </summary>
    [Fact]
    public void EditModeTickKeepsRenderPhasesButPausesSim()
    {
        List<EnginePhase> phases = [];
        EngineBuilder builder = new EngineBuilder()
            .WithWorkerCount(1);
        RegisterAllPhases(builder, phases);
        using Engine engine = builder.Build();

        engine.EnterEditMode();
        FrameTiming timing = engine.RunOneTick();

        Assert.Equal(EngineExecutionMode.Edit, engine.Mode);
        Assert.False(timing.RunSim);
        Assert.False(timing.RunPhysics);
        Assert.Equal(1, engine.Context.Clock.FrameIndex);
        Assert.Equal(0, engine.Context.Clock.SimTickIndex);
        Assert.Equal(
            [
                EnginePhase.InputAndTime,
                EnginePhase.BuildRenderBuffer,
                EnginePhase.GpuUploadAndRender,
                EnginePhase.WorldStreaming,
            ],
            phases);
    }

    /// <summary>
    /// 验证 StepOnce 从编辑模式临时执行一个 sim tick，随后回到编辑模式。
    /// </summary>
    [Fact]
    public void StepOnceRunsOneSimTickThenReturnsToEditMode()
    {
        List<EngineExecutionMode> observedModes = [];
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .OnPhase(EnginePhase.GameLogicAndScripts, context => observedModes.Add(context.Engine.Mode))
            .Build();

        engine.EnterEditMode();
        FrameTiming timing = engine.StepOnce();

        Assert.True(timing.RunSim);
        Assert.True(timing.RunPhysics);
        Assert.Equal(1, engine.Context.Clock.FrameIndex);
        Assert.Equal(1, engine.Context.Clock.SimTickIndex);
        Assert.Equal(EngineExecutionMode.Edit, engine.Mode);
        Assert.Equal([EngineExecutionMode.Step], observedModes);
        engine.EnterPlayMode();
        _ = Assert.Throws<InvalidOperationException>(() => engine.StepOnce());
    }

    /// <summary>
    /// 验证 StepOnce 在 30Hz 降频时仍强制执行恰好一个 sim tick。
    /// </summary>
    [Fact]
    public void StepOnceForcesSimTickWhenClockIsDownscaled()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithSimHz(EngineConstants.SimHzDownscaled)
            .Build();

        engine.EnterEditMode();
        _ = engine.RunOneTick();
        FrameTiming timing = engine.StepOnce();

        Assert.True(timing.RunSim);
        Assert.True(timing.RunPhysics);
        Assert.Equal(2, engine.Context.Clock.FrameIndex);
        Assert.Equal(1, engine.Context.Clock.SimTickIndex);
        Assert.Equal(EngineExecutionMode.Edit, engine.Mode);
    }

    /// <summary>
    /// 验证 Editor sim 控制适配器只通过 Engine/FrameClock 控制暂停、单步与 60/30Hz。
    /// </summary>
    [Fact]
    public void EngineSimulationControlServiceDrivesEngineClockAndModes()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        EngineSimulationControlService control = new(engine);

        control.EnterEditMode();
        control.SetSimHz(EngineConstants.SimHzDownscaled);
        control.StepOnce();
        SimulationControlSnapshot stepped = control.Capture();
        control.EnterPlayMode();
        _ = engine.RunOneTick();
        SimulationControlSnapshot playing = control.Capture();

        Assert.False(stepped.IsPlaying);
        Assert.Equal(EngineConstants.SimHzDownscaled, stepped.SimHz);
        Assert.Equal(1, stepped.SimTickIndex);
        Assert.True(playing.IsPlaying);
        Assert.Equal(EngineConstants.SimHzDownscaled, engine.Context.Clock.SimHz);
        Assert.Equal(EngineConstants.SimHzDownscaled, engine.RequestedSimHz);
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
