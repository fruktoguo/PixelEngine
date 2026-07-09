using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 延迟命令队列测试。
/// 不变式：延迟命令在 flush 前不生效、跨相位提交顺序确定。
/// </summary>
public sealed class EngineCommandQueueTests
{
    /// <summary>
    /// 验证 phase hook 中入队的写命令会在目标相位 hook 前落地。
    /// </summary>
    [Fact]
    public void CommandQueuedByScriptPhaseFlushesAtTargetPhaseBeforeHooks()
    {
        List<string> events = [];
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .OnPhase(EnginePhase.GameLogicAndScripts, context =>
            {
                EngineCommandQueue commands = context.Context.GetService<EngineCommandQueue>();
                commands.Enqueue(
                    EnginePhase.ParticleToCell,
                    static (_, state) => ((List<string>)state!).Add("command"),
                    events);
                events.Add("script");
            })
            .OnPhase(EnginePhase.ParticleToCell, _ => events.Add("phase3"))
            .Build();

        _ = engine.RunOneTick();

        Assert.Equal(["script", "command", "phase3"], events);
    }

    /// <summary>
    /// 验证 flush 期间入队到同一相位的命令会延迟到下一帧同相位，避免重入。
    /// </summary>
    [Fact]
    public void CommandQueuedDuringFlushWaitsForNextMatchingPhase()
    {
        List<string> events = [];
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        EngineCommandQueue commands = engine.Context.GetService<EngineCommandQueue>();
        commands.Enqueue(
            EnginePhase.ResidencyApply,
            static (context, state) =>
            {
                List<string> target = (List<string>)state!;
                target.Add("first");
                context.Context.GetService<EngineCommandQueue>().Enqueue(
                    EnginePhase.ResidencyApply,
                    static (_, nestedState) => ((List<string>)nestedState!).Add("second"),
                    target);
            },
            events);

        _ = engine.RunOneTick();
        Assert.Equal(["first"], events);
        Assert.Equal(1, commands.Count(EnginePhase.ResidencyApply));

        _ = engine.RunOneTick();
        Assert.Equal(["first", "second"], events);
        Assert.Equal(0, commands.Count(EnginePhase.ResidencyApply));
    }
}
