using PixelEngine.Rendering;

namespace PixelEngine.Hosting;

/// <summary>
/// 在脚本更新后同步脚本相机到 Rendering/World 快照。
/// </summary>
public sealed class ScriptCameraSyncPhaseDriver(
    ScriptCameraSynchronizer synchronizer,
    RenderWindow? window = null) : IEnginePhaseDriver
{
    private readonly ScriptCameraSynchronizer _synchronizer = synchronizer ?? throw new ArgumentNullException(nameof(synchronizer));
    private readonly RenderWindow? _window = window;

    /// <summary>
    /// 注册脚本相机同步相位。
    /// </summary>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.GameLogicAndScripts, SyncCamera);
    }

    private void SyncCamera(EngineTickContext context)
    {
        _ = context;
        if (_window is null)
        {
            _ = _synchronizer.Sync();
            return;
        }

        _ = _synchronizer.Sync(_window.Width, _window.Height);
    }
}
