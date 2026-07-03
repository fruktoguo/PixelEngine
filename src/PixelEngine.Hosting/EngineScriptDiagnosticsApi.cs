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
        double renderFps = _counters.RenderFramesPerSecond;
        if (renderFps <= 0 || !double.IsFinite(renderFps))
        {
            double dt = _clock.Dt * _clock.TimeScale;
            renderFps = dt > 0 ? 1.0 / dt : 0.0;
        }

        double simHz = _counters.SimHz > 0 ? _counters.SimHz : _clock.SimHz;
        double frameMs = _counters.RenderFrameMilliseconds;
        if (frameMs <= 0 || !double.IsFinite(frameMs))
        {
            frameMs = renderFps > 0 ? 1000.0 / renderFps : 0.0;
        }

        double p99Ms = _counters.RenderFrameP99Milliseconds;
        if (p99Ms <= 0 || !double.IsFinite(p99Ms))
        {
            p99Ms = frameMs;
        }

        double low1PercentFps = _counters.RenderFrameLow1PercentFps;
        if (low1PercentFps <= 0 || !double.IsFinite(low1PercentFps))
        {
            low1PercentFps = p99Ms > 0 ? 1000.0 / p99Ms : 0.0;
        }

        return new EngineDiagnosticsSnapshot(
            _clock.FrameIndex,
            (float)renderFps,
            (float)frameMs,
            (float)_counters.RenderFrameLastMilliseconds,
            (float)p99Ms,
            (float)low1PercentFps,
            (float)_counters.RenderFrameJitterMilliseconds,
            _counters.RenderFrameSampleCount,
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
