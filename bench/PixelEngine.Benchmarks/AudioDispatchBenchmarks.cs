using BenchmarkDotNet.Attributes;
using PixelEngine.Audio;
using PixelEngine.Core.Events;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 音频帧尾派发预算基准。
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class AudioDispatchBenchmarks
{
    private readonly AudioSettings _settings = new()
    {
        MaxVoices = 64,
        MaxDrainedAudioEventsPerFrame = 256,
        MaxParticleImpactEventsPerFrame = 64,
        CoalesceBucketSize = 1,
        DefaultCooldownTicks = 0,
    };

    private readonly NullAudioBackend _backend;
    private readonly AudioVoicePool _voices;
    private readonly MpscRingBuffer<AudioEvent> _ring;
    private readonly AudioDispatcher _dispatcher;
    private readonly BenchmarkAudioEventPlayer _player = new();
    private readonly AudioListenerState _listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);
    private long _tick;

    /// <summary>
    /// 创建音频派发基准夹具。
    /// </summary>
    public AudioDispatchBenchmarks()
    {
        _backend = new NullAudioBackend();
        _voices = new AudioVoicePool(_backend, _settings);
        _ring = new MpscRingBuffer<AudioEvent>(512);
        _dispatcher = new AudioDispatcher(_ring, _voices, _settings);
    }

    /// <summary>
    /// 派发 64 个不合并的 impact 事件，覆盖主线程预算压力路径。
    /// </summary>
    /// <returns>单帧派发统计。</returns>
    [Benchmark]
    public AudioDispatchStats DispatchSixtyFourImpactEvents()
    {
        for (int i = 0; i < _voices.Capacity; i++)
        {
            _backend.MarkStopped(_voices[i].Source);
        }

        _voices.RefreshFinishedVoices();
        for (int i = 0; i < 64; i++)
        {
            AudioEvent audioEvent = new(AudioEventType.ParticleImpact, i * 2, 0, 1, 1f);
            if (!_ring.TryEnqueue(in audioEvent))
            {
                throw new InvalidOperationException("音频基准 ring 容量不足。");
            }
        }

        return _dispatcher.Dispatch(_listener, ++_tick, _player);
    }

    private sealed class BenchmarkAudioEventPlayer : IAudioEventPlayer
    {
        public bool TryPlay(in CoalescedAudioEvent audioEvent, AudioVoice voice, long tick)
        {
            _ = audioEvent;
            _ = tick;
            voice.Play(buffer: 1, gain: 1f, pitch: 1f);
            return true;
        }
    }
}
