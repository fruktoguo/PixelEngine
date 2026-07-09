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

    // 相位 2：应用本帧 chunk 驻留增删，在 CA 模拟前确定权威 chunk 集合。
    private void ApplyResidency(EngineTickContext context)
    {
        World.ApplyResidency(context.Timing.FrameIndex);
    }

    // 相位 11：后台流式 I/O 单步推进；无 sim 的渲染帧也会运行以保持 chunk 预取。
    private void ProcessStreaming(EngineTickContext context)
    {
        _ = World.Streamer.ProcessIoOnce(context.Context.Jobs);
    }
}
