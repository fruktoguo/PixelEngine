using System.Diagnostics;
using System.Numerics;
using PixelEngine.Core.Events;

namespace PixelEngine.Audio;

/// <summary>
/// 主线程帧尾的音频事件消费器：排空 Core ring、合并限频、冷却去重、申请 voice 并交给播放解析器。
/// </summary>
public sealed class AudioDispatcher
{
    private readonly MpscRingBuffer<AudioEvent> _events;
    private readonly AudioVoicePool _voices;
    private readonly AudioEventCoalescer _coalescer;
    private readonly CooldownTracker _cooldowns;
    private readonly AudioEvent[] _eventScratch;
    private readonly CoalescedAudioEvent[] _coalescedScratch;
    private readonly AudioSpace _space;
    private readonly int _cooldownTicks;

    /// <summary>
    /// 创建音频事件派发器。
    /// </summary>
    /// <param name="events">Core 音频事件 ring。</param>
    /// <param name="voices">positional voice 池。</param>
    /// <param name="settings">音频设置。</param>
    public AudioDispatcher(MpscRingBuffer<AudioEvent> events, AudioVoicePool voices, AudioSettings settings)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _voices = voices ?? throw new ArgumentNullException(nameof(voices));
        ArgumentNullException.ThrowIfNull(settings);
        AudioSettings validated = settings.Validate();
        _coalescer = new AudioEventCoalescer(validated);
        _cooldowns = new CooldownTracker(validated.CooldownTableCapacity);
        _eventScratch = new AudioEvent[validated.MaxDrainedAudioEventsPerFrame];
        int coalescedCapacity =
            validated.MaxParticleImpactEventsPerFrame +
            validated.MaxFireCrackleEventsPerFrame +
            validated.MaxLiquidSplashEventsPerFrame +
            validated.MaxExplosionEventsPerFrame +
            validated.MaxRigidbodyShatterEventsPerFrame +
            validated.MaxAmbientRegionEventsPerFrame;
        _coalescedScratch = new CoalescedAudioEvent[coalescedCapacity];
        _space = new AudioSpace(validated.PixelsPerMeter);
        _cooldownTicks = validated.DefaultCooldownTicks;
    }

    /// <summary>
    /// 最近一次派发统计。
    /// </summary>
    public AudioDispatchStats LastStats { get; private set; }

    /// <summary>
    /// 执行一帧音频事件派发。
    /// </summary>
    /// <param name="listener">当前 listener 状态，用于 voice stealing 距离评分。</param>
    /// <param name="tick">当前模拟 tick。</param>
    /// <param name="player">材质音效播放解析器。</param>
    /// <param name="ambientLoops">可选 ambient loop 管理器；提供后 `AmbientRegion` 不占用 one-shot voice。</param>
    /// <returns>本帧派发统计。</returns>
    public AudioDispatchStats Dispatch(
        in AudioListenerState listener,
        long tick,
        IAudioEventPlayer player,
        AmbientLoopManager? ambientLoops = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        long startTimestamp = Stopwatch.GetTimestamp();
        _coalescer.BeginFrame();
        int drained = _events.DrainTo(_eventScratch);
        for (int i = 0; i < drained; i++)
        {
            AudioEvent audioEvent = _eventScratch[i];
            _coalescer.Add(in audioEvent);
        }

        int coalescedEventCount = _coalescer.Flush(_coalescedScratch);
        int dropped = _coalescer.DroppedCount;
        int dispatched = 0;
        int played = 0;
        ambientLoops?.Update(_coalescedScratch.AsSpan(0, coalescedEventCount));

        for (int i = 0; i < coalescedEventCount; i++)
        {
            CoalescedAudioEvent audioEvent = _coalescedScratch[i];
            if (ambientLoops is not null && audioEvent.Type == AudioEventType.AmbientRegion)
            {
                continue;
            }

            if (!_cooldowns.ShouldPlay(audioEvent.MaterialId, audioEvent.Type, tick, _cooldownTicks))
            {
                dropped++;
                continue;
            }

            Vector3 position = _space.ToMeters(audioEvent.CellX, audioEvent.CellY);
            byte priority = AudioEventTypeTraits.GetPriority(audioEvent.Type);
            AudioVoice? voice = _voices.Acquire(priority, audioEvent.Type, position, listener.Position, tick);
            if (voice is null)
            {
                dropped++;
                continue;
            }

            dispatched++;
            if (player.TryPlay(in audioEvent, voice, tick))
            {
                played++;
            }
            else
            {
                voice.Stop();
                dropped++;
            }
        }

        _voices.RefreshFinishedVoices();
        long elapsedTimestamp = Stopwatch.GetTimestamp() - startTimestamp;
        double elapsedMilliseconds = elapsedTimestamp * 1000.0 / Stopwatch.Frequency;
        LastStats = new AudioDispatchStats(
            drained,
            _coalescer.CoalescedCount,
            dropped,
            dispatched,
            played,
            _voices.ActiveVoiceCount,
            _voices.StealCount,
            elapsedMilliseconds);
        return LastStats;
    }
}
