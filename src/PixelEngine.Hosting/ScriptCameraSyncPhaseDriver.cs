using PixelEngine.Rendering;

namespace PixelEngine.Hosting;

/// <summary>
/// 在脚本更新后同步脚本相机到 Rendering/World 快照。
/// </summary>
public sealed class ScriptCameraSyncPhaseDriver(
    ScriptCameraSynchronizer synchronizer,
    RenderWindow? window = null,
    int fixedViewportWidth = 0,
    int fixedViewportHeight = 0) : IEnginePhaseDriver
{
    private readonly ScriptCameraSynchronizer _synchronizer = synchronizer ?? throw new ArgumentNullException(nameof(synchronizer));
    private int _fixedViewportWidth = fixedViewportWidth;
    private int _fixedViewportHeight = fixedViewportHeight;
    private RenderWindow? _window = window;

    /// <summary>
    /// 绑定或替换窗口尺寸来源。
    /// </summary>
    public void AttachWindow(RenderWindow window, int fixedViewportWidth = 0, int fixedViewportHeight = 0)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _fixedViewportWidth = fixedViewportWidth;
        _fixedViewportHeight = fixedViewportHeight;
    }

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

        int viewportWidth = _fixedViewportWidth > 0 ? _fixedViewportWidth : _window.Width;
        int viewportHeight = _fixedViewportHeight > 0 ? _fixedViewportHeight : _window.Height;
        _ = _synchronizer.Sync(viewportWidth, viewportHeight);
    }
}
