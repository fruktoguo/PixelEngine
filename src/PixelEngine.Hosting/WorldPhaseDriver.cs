using PixelEngine.World;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 WorldManager 的驻留应用与流式 I/O 入口绑定到 Hosting 相位管线。
/// </summary>
/// <param name="world">真实 WorldManager 实例。</param>
public sealed class WorldPhaseDriver(WorldManager world) : IEnginePhaseDriver
{
    /// <summary>
    /// 被驱动的世界管理器。
    /// </summary>
    public WorldManager World { get; } = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>
    /// 注册相位 2 驻留应用与相位 11 流式 I/O 批处理。
    /// </summary>
    /// <param name="phases">目标 12 相位管线。</param>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.ResidencyApply, ApplyResidency);
        phases.Register(EnginePhase.WorldStreaming, ProcessStreaming);
    }

    private void ApplyResidency(EngineTickContext context)
    {
        World.ApplyResidency(context.Timing.FrameIndex);
    }

    private void ProcessStreaming(EngineTickContext context)
    {
        _ = World.Streamer.ProcessIoOnce(context.Context.Jobs);
    }
}
