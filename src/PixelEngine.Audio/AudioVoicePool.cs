using System.Numerics;
using PixelEngine.Core.Events;

namespace PixelEngine.Audio;

/// <summary>
/// 固定大小 positional voice 池，初始化期预创建 OpenAL source，运行时只做抢占选择。
/// </summary>
public sealed class AudioVoicePool : IDisposable
{
    private readonly IAudioBackend _backend;
    private AudioVoice[] _voices;
    private bool _disposed;

    /// <summary>
    /// 创建 voice 池。
    /// </summary>
    /// <param name="backend">音频后端。</param>
    /// <param name="settings">音频设置。</param>
    public AudioVoicePool(IAudioBackend backend, AudioSettings settings)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        ArgumentNullException.ThrowIfNull(settings);
        AudioSettings validated = settings.Validate();
        _voices = new AudioVoice[validated.MaxVoices];
        for (int i = 0; i < _voices.Length; i++)
        {
            uint source = _backend.CreateSource();
            _voices[i] = new AudioVoice(_backend, source, i, validated);
        }
    }

    /// <summary>
    /// 池容量。
    /// </summary>
    public int Capacity => _voices.Length;

    /// <summary>
    /// 当前活跃 voice 数。
    /// </summary>
    public int ActiveVoiceCount { get; private set; }

    /// <summary>
    /// 池满且不可抢占导致丢弃的次数。
    /// </summary>
    public long DroppedVoiceCount { get; private set; }

    /// <summary>
    /// 抢占次数。
    /// </summary>
    public long StealCount { get; private set; }

    /// <summary>
    /// 获取固定槽位中的 voice。
    /// </summary>
    /// <param name="index">槽位索引。</param>
    /// <returns>voice。</returns>
    public AudioVoice this[int index] => _voices[index];

    /// <summary>
    /// 应用新的运行时设置，必要时扩缩 source 池并重新配置距离衰减。
    /// </summary>
    /// <param name="settings">新的音频设置。</param>
    public void ApplySettings(AudioSettings settings)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);
        AudioSettings validated = settings.Validate();
        if (validated.MaxVoices != _voices.Length)
        {
            Resize(validated);
        }

        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i].Configure(validated);
        }

        RefreshFinishedVoices();
    }

    /// <summary>
    /// 申请一个 voice。优先复用已完成槽位，池满时按优先级 / 距离 / 年龄抢占。
    /// </summary>
    /// <param name="priority">新事件优先级。</param>
    /// <param name="eventType">新事件类型。</param>
    /// <param name="position">新 source 位置，单位为米。</param>
    /// <param name="listenerPosition">listener 位置，单位为米。</param>
    /// <param name="tick">当前逻辑 tick。</param>
    /// <returns>可用 voice；若不可抢占则返回 <see langword="null"/>。</returns>
    public AudioVoice? Acquire(
        byte priority,
        AudioEventType eventType,
        Vector3 position,
        Vector3 listenerPosition,
        long tick)
    {
        ThrowIfDisposed();
        RefreshFinishedVoices();

        // 优先复用已停止的空闲槽位，避免无谓抢占。
        for (int i = 0; i < _voices.Length; i++)
        {
            AudioVoice voice = _voices[i];
            if (!voice.IsAllocated)
            {
                voice.Reserve(priority, eventType, position, tick);
                ActiveVoiceCount++;
                return voice;
            }
        }

        // 池满时按优先级 / 距离 / 年龄评分抢占最低价值 voice。
        int stealIndex = -1;
        float bestScore = 0f;
        for (int i = 0; i < _voices.Length; i++)
        {
            float score = _voices[i].StealScore(priority, listenerPosition, tick);
            if (score > bestScore)
            {
                bestScore = score;
                stealIndex = i;
            }
        }

        if (stealIndex < 0)
        {
            DroppedVoiceCount++;
            return null;
        }

        AudioVoice stolen = _voices[stealIndex];
        stolen.Reserve(priority, eventType, position, tick);
        StealCount++;
        return stolen;
    }

    /// <summary>
    /// 刷新完成状态并释放已停止 voice。
    /// </summary>
    public void RefreshFinishedVoices()
    {
        ThrowIfDisposed();
        int activeCount = 0;
        for (int i = 0; i < _voices.Length; i++)
        {
            AudioVoice voice = _voices[i];
            _ = voice.ReleaseIfFinished();
            if (voice.IsAllocated)
            {
                activeCount++;
            }
        }

        ActiveVoiceCount = activeCount;
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
            AudioVoice voice = _voices[i];
            voice.Stop();
            _backend.DeleteSource(voice.Source);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void Resize(AudioSettings settings)
    {
        AudioVoice[] next = new AudioVoice[settings.MaxVoices];
        int kept = Math.Min(_voices.Length, next.Length);
        for (int i = 0; i < kept; i++)
        {
            _voices[i].ReassignSlot(i);
            next[i] = _voices[i];
        }

        for (int i = kept; i < _voices.Length; i++)
        {
            _voices[i].Stop();
            _backend.DeleteSource(_voices[i].Source);
        }

        for (int i = kept; i < next.Length; i++)
        {
            uint source = _backend.CreateSource();
            next[i] = new AudioVoice(_backend, source, i, settings);
        }

        _voices = next;
    }
}
