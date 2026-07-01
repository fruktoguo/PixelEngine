using System.Diagnostics;
using PixelEngine.Audio;
using PixelEngine.Core.Diagnostics;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 帧尾音频驱动，负责 render-only 帧 listener/voice 连续推进与可选事件派发。
/// </summary>
public sealed class AudioPhaseDriver : IEnginePhaseDriver
{
    private readonly AudioSystem _audio;
    private readonly Func<EngineTickContext, AudioListenerView> _listenerProvider;
    private readonly AudioDispatcher? _dispatcher;
    private readonly IAudioEventPlayer? _eventPlayer;

    /// <summary>
    /// 创建音频相位驱动。
    /// </summary>
    public AudioPhaseDriver(
        AudioSystem audio,
        Func<EngineTickContext, AudioListenerView> listenerProvider,
        AudioDispatcher? dispatcher = null,
        IAudioEventPlayer? eventPlayer = null)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _listenerProvider = listenerProvider ?? throw new ArgumentNullException(nameof(listenerProvider));
        _dispatcher = dispatcher;
        _eventPlayer = eventPlayer;
        if ((dispatcher is null) != (eventPlayer is null))
        {
            throw new ArgumentException("dispatcher 与 eventPlayer 必须同时提供或同时省略。");
        }
    }

    /// <summary>
    /// 注册音频帧尾相位。
    /// </summary>
    /// <param name="phases">引擎相位管线。</param>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.BuildRenderBuffer, RunAudio);
    }

    private void RunAudio(EngineTickContext context)
    {
        AudioListenerView view = _listenerProvider(context);
        _audio.Update(in view, context.Timing.FrameIndex, context.Timing.RunSim);

        if (_dispatcher is not null && _eventPlayer is not null)
        {
            long start = Stopwatch.GetTimestamp();
            AudioDispatchStats stats = _dispatcher.Dispatch(_audio.CurrentListener, context.Timing.FrameIndex, _eventPlayer);
            long elapsed = Stopwatch.GetTimestamp() - start;
            double elapsedMs = elapsed * 1000.0 / Stopwatch.Frequency;
            context.Context.Profiler.RecordSub(FrameSubPhase.AudioDispatch, elapsedMs);
            _audio.RecordDispatchStats(stats with { DispatchMilliseconds = elapsedMs });
        }

        _audio.PublishDiagnostics(context.Context.Counters);
    }
}
