using System.Numerics;
using PixelEngine.Core.Events;

namespace PixelEngine.Audio;

/// <summary>
/// 材质化 ambient loop 管理器，使用独立 source 池并按聚合区域事件滞回淡变。
/// </summary>
public sealed class AmbientLoopManager : IDisposable
{
    private readonly IAudioBackend _backend;
    private readonly MaterialAudioTable _table;
    private readonly IAudioCueBufferResolver _buffers;
    private AudioSettings _settings;
    private AmbientVoice[] _voices;
    private bool[] _touched;
    private AudioSpace _space;
    private float _enterThreshold;
    private float _exitThreshold;
    private bool _disposed;

    /// <summary>
    /// 创建 ambient loop 管理器。
    /// </summary>
    public AmbientLoopManager(
        IAudioBackend backend,
        MaterialAudioTable table,
        IAudioCueBufferResolver buffers,
        AudioSettings settings)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _buffers = buffers ?? throw new ArgumentNullException(nameof(buffers));
        ArgumentNullException.ThrowIfNull(settings);
        AudioSettings validated = settings.Validate();
        _settings = validated;
        _voices = new AmbientVoice[validated.MaxAmbientVoices];
        _touched = new bool[_voices.Length];
        _space = new AudioSpace(validated.PixelsPerMeter);
        _enterThreshold = validated.AmbientEnterThreshold;
        _exitThreshold = validated.AmbientExitThreshold;
        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new AmbientVoice(_backend, _backend.CreateSource(), i, validated);
        }
    }

    /// <summary>
    /// 活跃 ambient voice 数。
    /// </summary>
    public int ActiveVoiceCount { get; private set; }

    /// <summary>
    /// 获取固定槽位中的 ambient voice。
    /// </summary>
    public AmbientVoice this[int index] => _voices[index];

    /// <summary>
    /// 应用新的运行时设置，必要时扩缩 ambient source 池。
    /// </summary>
    /// <param name="settings">新的音频设置。</param>
    public void ApplySettings(AudioSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        AudioSettings validated = settings.Validate();
        _settings = validated;
        _space = new AudioSpace(validated.PixelsPerMeter);
        _enterThreshold = validated.AmbientEnterThreshold;
        _exitThreshold = validated.AmbientExitThreshold;
        if (validated.MaxAmbientVoices != _voices.Length)
        {
            Resize(validated);
        }

        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i].Configure(i, validated);
        }

        RefreshActiveVoiceCount();
    }

    /// <summary>
    /// 用本帧聚合 ambient 区域事件推进循环声。
    /// </summary>
    public void Update(ReadOnlySpan<CoalescedAudioEvent> events)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Array.Clear(_touched);
        for (int i = 0; i < events.Length; i++)
        {
            CoalescedAudioEvent audioEvent = events[i];
            if (audioEvent.Type != AudioEventType.AmbientRegion || audioEvent.Magnitude < _enterThreshold)
            {
                continue;
            }

            if (!_table.TryResolve(in audioEvent, tick: 0, out MaterialAudioPlayback playback) ||
                !_buffers.TryResolveBuffer(playback.CueHandle, out uint buffer))
            {
                continue;
            }

            if (_voices.Length == 0)
            {
                continue;
            }

            int slot = FindExisting(audioEvent.MaterialId, playback.CueHandle);
            if (slot < 0)
            {
                slot = FindFreeOrQuietest();
                Vector3 position = _space.ToMeters(audioEvent.CellX, audioEvent.CellY);
                _voices[slot].Begin(audioEvent.MaterialId, playback.CueHandle, buffer, in position, audioEvent.Magnitude * _settings.AmbientVolume);
            }
            else
            {
                _voices[slot].SetTarget(audioEvent.Magnitude * _settings.AmbientVolume);
            }

            _touched[slot] = true;
        }

        int active = 0;
        for (int i = 0; i < _voices.Length; i++)
        {
            AmbientVoice voice = _voices[i];
            if (voice.IsActive && !_touched[i] && voice.TargetGain > _exitThreshold)
            {
                voice.SetTarget(_exitThreshold);
            }
            else if (voice.IsActive && !_touched[i])
            {
                voice.FadeOut();
            }

            voice.Step();
            if (voice.IsActive)
            {
                active++;
            }
        }

        ActiveVoiceCount = active;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (int i = 0; i < _voices.Length; i++)
        {
            AmbientVoice voice = _voices[i];
            if (voice.IsActive)
            {
                _backend.Stop(voice.Source);
            }

            _backend.DeleteSource(voice.Source);
        }
    }

    private int FindExisting(ushort materialId, int cueHandle)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            AmbientVoice voice = _voices[i];
            if (voice.IsActive && voice.MaterialId == materialId && voice.CueHandle == cueHandle)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindFreeOrQuietest()
    {
        int quietest = 0;
        float quietestGain = float.MaxValue;
        for (int i = 0; i < _voices.Length; i++)
        {
            AmbientVoice voice = _voices[i];
            if (!voice.IsActive)
            {
                return i;
            }

            if (voice.Gain < quietestGain)
            {
                quietestGain = voice.Gain;
                quietest = i;
            }
        }

        return quietest;
    }

    private void Resize(AudioSettings settings)
    {
        AmbientVoice[] next = new AmbientVoice[settings.MaxAmbientVoices];
        int kept = Math.Min(_voices.Length, next.Length);
        for (int i = 0; i < kept; i++)
        {
            next[i] = _voices[i];
        }

        for (int i = kept; i < _voices.Length; i++)
        {
            AmbientVoice voice = _voices[i];
            if (voice.IsActive)
            {
                _backend.Stop(voice.Source);
            }

            _backend.DeleteSource(voice.Source);
        }

        for (int i = kept; i < next.Length; i++)
        {
            next[i] = new AmbientVoice(_backend, _backend.CreateSource(), i, settings);
        }

        _voices = next;
        _touched = new bool[next.Length];
        RefreshActiveVoiceCount();
    }

    private void RefreshActiveVoiceCount()
    {
        int count = 0;
        for (int i = 0; i < _voices.Length; i++)
        {
            if (_voices[i].IsActive)
            {
                count++;
            }
        }

        ActiveVoiceCount = count;
    }
}
