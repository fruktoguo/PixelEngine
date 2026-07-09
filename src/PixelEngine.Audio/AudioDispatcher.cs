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
    private AudioEventCoalescer _coalescer;
    private CooldownTracker _cooldowns;
    private AudioEvent[] _eventScratch;
    private CoalescedAudioEvent[] _coalescedScratch;
    private AudioSpace _space;
    private int _cooldownTicks;

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
        _coalescedScratch = new CoalescedAudioEvent[CoalescedCapacity(validated)];
        _space = new AudioSpace(validated.PixelsPerMeter);
        _cooldownTicks = validated.DefaultCooldownTicks;
    }

    /// <summary>
    /// 最近一次派发统计。
    /// </summary>
    public AudioDispatchStats LastStats { get; private set; }

    /// <summary>
    /// 应用新的运行时设置，重建限频 / 合并 scratch 结构。
    /// </summary>
    /// <param name="settings">新的音频设置。</param>
    public void ApplySettings(AudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AudioSettings validated = settings.Validate();
        _coalescer = new AudioEventCoalescer(validated);
        _cooldowns = new CooldownTracker(validated.CooldownTableCapacity);
        _eventScratch = new AudioEvent[validated.MaxDrainedAudioEventsPerFrame];
        _coalescedScratch = new CoalescedAudioEvent[CoalescedCapacity(validated)];
        _space = new AudioSpace(validated.PixelsPerMeter);
        _cooldownTicks = validated.DefaultCooldownTicks;
    }

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
        // --- 阶段 1：从 Core MPSC ring 排空本帧事件并合并限频 ---
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
        // AmbientRegion 由专用 loop 管理器处理，不占用 one-shot voice 槽位。
        ambientLoops?.Update(_coalescedScratch.AsSpan(0, coalescedEventCount));

        // --- 阶段 2：冷却去重 → voice 池申请 → 材质音效解析播放 ---
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

        // --- 阶段 3：回收已停止 voice 并汇总派发统计 ---
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

    private static int CoalescedCapacity(AudioSettings settings)
    {
        return
            settings.MaxParticleImpactEventsPerFrame +
            settings.MaxFireCrackleEventsPerFrame +
            settings.MaxLiquidSplashEventsPerFrame +
            settings.MaxExplosionEventsPerFrame +
            settings.MaxRigidbodyShatterEventsPerFrame +
            settings.MaxAmbientRegionEventsPerFrame;
    }
}
