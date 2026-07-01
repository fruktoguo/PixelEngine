namespace PixelEngine.Hosting;

/// <summary>
/// 在脚本与相机同步后消费脚本光照请求。
/// </summary>
public sealed class ScriptLightingSyncPhaseDriver(ScriptLightingSynchronizer synchronizer) : IEnginePhaseDriver
{
    private readonly ScriptLightingSynchronizer _synchronizer = synchronizer ?? throw new ArgumentNullException(nameof(synchronizer));

    /// <summary>
    /// 注册光照请求消费相位。
    /// </summary>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.GameLogicAndScripts, SyncLighting);
    }

    private void SyncLighting(EngineTickContext context)
    {
        _ = context;
        _synchronizer.Sync();
    }
}
