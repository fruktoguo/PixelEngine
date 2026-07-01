using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Time;
using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Hosting 诊断计数器适配为脚本只读诊断 API。
/// </summary>
/// <param name="counters">引擎计数器。</param>
/// <param name="clock">帧时钟。</param>
public sealed class EngineScriptDiagnosticsApi(EngineCounters counters, FrameClock clock) : IDiagnosticsApi
{
    private readonly EngineCounters _counters = counters ?? throw new ArgumentNullException(nameof(counters));
    private readonly FrameClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

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
}
