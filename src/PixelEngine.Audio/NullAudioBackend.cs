using System.Numerics;

namespace PixelEngine.Audio;

/// <summary>
/// 无声测试后端，记录 source 状态但不访问真实音频设备。
/// </summary>
public sealed class NullAudioBackend : IAudioBackend
{
    private readonly List<SourceRecord> _sources = [];
    private bool _disposed;

    /// <summary>
    /// listener 更新次数。
    /// </summary>
    public int ListenerUpdates { get; private set; }

    /// <summary>
    /// 播放调用次数。
    /// </summary>
    public int PlayCalls { get; private set; }

    /// <summary>
    /// 停止调用次数。
    /// </summary>
    public int StopCalls { get; private set; }

    /// <summary>
    /// 已创建 source 数。
    /// </summary>
    public int SourceCount => _sources.Count;

    /// <summary>
    /// 最近一次 listener 状态。
    /// </summary>
    public AudioListenerState LastListener { get; private set; }

    /// <inheritdoc />
    public uint CreateSource()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint handle = (uint)(_sources.Count + 1);
        _sources.Add(new SourceRecord(handle));
        return handle;
    }

    /// <inheritdoc />
    public void DeleteSource(uint source)
    {
        SourceRecord record = Get(source);
        record.State = AudioSourceState.Stopped;
        record.Deleted = true;
    }

    /// <inheritdoc />
    public void ConfigureSource(uint source, AudioSettings settings)
    {
        SourceRecord record = Get(source);
        ArgumentNullException.ThrowIfNull(settings);
        _ = record.Handle;
        _ = settings.Validate();
    }

    /// <inheritdoc />
    public void Play(uint source, uint buffer, in Vector3 position, float gain, float pitch)
    {
        SourceRecord record = Get(source);
        record.Buffer = buffer;
        record.Position = position;
        record.Gain = gain;
        record.Pitch = pitch;
        record.State = AudioSourceState.Playing;
        PlayCalls++;
    }

    /// <inheritdoc />
    public void Stop(uint source)
    {
        SourceRecord record = Get(source);
        record.State = AudioSourceState.Stopped;
        StopCalls++;
    }

    /// <inheritdoc />
    public AudioSourceState GetState(uint source)
    {
        return Get(source).State;
    }

    /// <summary>
    /// 测试辅助：模拟后端自然播放完成。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    public void MarkStopped(uint source)
    {
        SourceRecord record = Get(source);
        record.State = AudioSourceState.Stopped;
    }

    /// <inheritdoc />
    public void SetListener(in AudioListenerState listener)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastListener = listener;
        ListenerUpdates++;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }

    private SourceRecord Get(uint source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (source == 0 || source > _sources.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "未知 source 句柄。");
        }

        SourceRecord record = _sources[(int)source - 1];
        return record.Deleted ? throw new ObjectDisposedException(nameof(source)) : record;
    }

    private sealed class SourceRecord(uint handle)
    {
        public readonly uint Handle = handle;
        public AudioSourceState State = AudioSourceState.Initial;
        public uint Buffer;
        public Vector3 Position;
        public float Gain;
        public float Pitch;
        public bool Deleted;
    }
}
