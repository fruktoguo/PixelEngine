using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Time;
using PixelEngine.Editor;
using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Hosting 诊断计数器适配为脚本只读诊断 API。
/// </summary>
/// <param name="counters">引擎计数器。</param>
/// <param name="clock">帧时钟。</param>
/// <param name="overlays">共享调试叠层设置。</param>
public sealed class EngineScriptDiagnosticsApi(
    EngineCounters counters,
    FrameClock clock,
    DebugOverlaySettings overlays) : IDiagnosticsApi
{
    private readonly EngineCounters _counters = counters ?? throw new ArgumentNullException(nameof(counters));
    private readonly FrameClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly DebugOverlaySettings _overlays = overlays ?? throw new ArgumentNullException(nameof(overlays));

    /// <summary>
    /// 捕获当前帧号、FPS、sim 频率与核心运行计数器快照。
    /// </summary>
    /// <returns>脚本可读诊断快照。</returns>
    public EngineDiagnosticsSnapshot Capture()
    {
        double dt = _clock.Dt * _clock.TimeScale;
        float fps = dt > 0 ? (float)(1.0 / dt) : 0f;
        double simHz = _counters.SimHz > 0 ? _counters.SimHz : _clock.SimHz;
        return new EngineDiagnosticsSnapshot(
            _clock.FrameIndex,
            fps,
            (float)simHz,
            _counters.ActiveChunks,
            _counters.ResidentChunks,
            _counters.FreeParticles,
            _counters.RigidBodies);
    }

    /// <summary>
    /// 判断指定调试叠层是否启用。
    /// </summary>
    /// <param name="overlay">脚本调试叠层类型。</param>
    /// <returns>启用时返回 true。</returns>
    public bool IsOverlayEnabled(DebugOverlayKind overlay)
    {
        return _overlays.IsEnabled(MapOverlay(overlay));
    }

    /// <summary>
    /// 设置指定调试叠层开关。
    /// </summary>
    /// <param name="overlay">脚本调试叠层类型。</param>
    /// <param name="enabled">是否启用。</param>
    public void SetOverlay(DebugOverlayKind overlay, bool enabled)
    {
        _overlays.Set(MapOverlay(overlay), enabled);
    }

    /// <summary>
    /// 切换指定调试叠层开关。
    /// </summary>
    /// <param name="overlay">脚本调试叠层类型。</param>
    /// <returns>切换后的启用状态。</returns>
    public bool ToggleOverlay(DebugOverlayKind overlay)
    {
        bool enabled = !IsOverlayEnabled(overlay);
        SetOverlay(overlay, enabled);
        return enabled;
    }

    private static DebugOverlayFlags MapOverlay(DebugOverlayKind overlay)
    {
        return overlay switch
        {
            DebugOverlayKind.DirtyRects => DebugOverlayFlags.DirtyRects,
            DebugOverlayKind.CaIterationRects => DebugOverlayFlags.CaIterationRects,
            DebugOverlayKind.ChunkGridParity => DebugOverlayFlags.ChunkGridParity,
            DebugOverlayKind.KeepAliveHotspots => DebugOverlayFlags.KeepAliveHotspots,
            DebugOverlayKind.CellParity => DebugOverlayFlags.CellParity,
            DebugOverlayKind.TemperatureHeatmap => DebugOverlayFlags.TemperatureHeatmap,
            DebugOverlayKind.OwnedByBody => DebugOverlayFlags.OwnedByBody,
            DebugOverlayKind.ParticleTrails => DebugOverlayFlags.ParticleTrails,
            DebugOverlayKind.ConnectedComponents => DebugOverlayFlags.ConnectedComponents,
            _ => throw new ArgumentOutOfRangeException(nameof(overlay), overlay, "未知调试叠层类型。"),
        };
    }
}
