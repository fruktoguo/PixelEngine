using System.Numerics;

namespace PixelEngine.Audio;

/// <summary>
/// 无声测试后端，记录 source 状态但不访问真实音频设备。
/// </summary>
public sealed class NullAudioBackend : IAudioBackend
{
    private readonly List<SourceRecord> _sources = [];
    private readonly List<BufferRecord> _buffers = [];
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
    /// source 增益更新次数。
    /// </summary>
    public int GainUpdates { get; private set; }

    /// <summary>
    /// 已创建 source 数。
    /// </summary>
    public int SourceCount => _sources.Count;

    /// <summary>
    /// 当前仍未删除的 source 数。
    /// </summary>
    public int LiveSourceCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _sources.Count; i++)
            {
                if (!_sources[i].Deleted)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// 已创建 buffer 数。
    /// </summary>
    public int BufferCount => _buffers.Count;

    /// <summary>
    /// 当前仍未删除的 buffer 数。
    /// </summary>
    public int LiveBufferCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _buffers.Count; i++)
            {
                if (!_buffers[i].Deleted)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// 当前仍未删除的 source 与 buffer 总数。
    /// </summary>
    public int LiveObjectCount => LiveSourceCount + LiveBufferCount;

    /// <summary>
    /// 已删除 buffer 数。
    /// </summary>
    public int DeletedBufferCount { get; private set; }

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
    public uint CreateBuffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint handle = (uint)(_buffers.Count + 1);
        _buffers.Add(new BufferRecord(handle));
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
    public void DeleteBuffer(uint buffer)
    {
        BufferRecord record = GetBuffer(buffer);
        record.Deleted = true;
        DeletedBufferCount++;
    }

    /// <inheritdoc />
    public void UploadBuffer(uint buffer, AudioSampleFormat format, ReadOnlySpan<byte> pcm, int sampleRate)
    {
        BufferRecord record = GetBuffer(buffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        record.Format = format;
        record.SampleRate = sampleRate;
        record.ByteLength = pcm.Length;
    }

    /// <inheritdoc />
    public void QueueBuffers(uint source, ReadOnlySpan<uint> buffers)
    {
        SourceRecord record = Get(source);
        for (int i = 0; i < buffers.Length; i++)
        {
            _ = GetBuffer(buffers[i]);
            record.QueuedBufferHandles.Enqueue(buffers[i]);
            record.QueuedBuffers++;
        }
    }

    /// <inheritdoc />
    public int UnqueueProcessedBuffers(uint source, Span<uint> destination)
    {
        SourceRecord record = Get(source);
        int count = Math.Min(record.ProcessedBuffers, destination.Length);
        for (int i = 0; i < count; i++)
        {
            destination[i] = record.QueuedBufferHandles.TryDequeue(out uint buffer) ? buffer : 0;
        }

        record.ProcessedBuffers -= count;
        record.QueuedBuffers -= count;
        return count;
    }

    /// <inheritdoc />
    public int GetProcessedBufferCount(uint source)
    {
        return Get(source).ProcessedBuffers;
    }

    /// <inheritdoc />
    public void ConfigureSource(uint source, AudioSettings settings)
    {
        SourceRecord record = Get(source);
        ArgumentNullException.ThrowIfNull(settings);
        AudioSettings validated = settings.Validate();
        record.ReferenceDistance = validated.ReferenceDistance;
        record.MaxDistance = validated.MaxDistance;
        record.RolloffFactor = validated.RolloffFactor;
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
    public void SetSourceGain(uint source, float gain)
    {
        SourceRecord record = Get(source);
        record.Gain = gain;
        GainUpdates++;
    }

    /// <inheritdoc />
    public void SetSourceLooping(uint source, bool looping)
    {
        SourceRecord record = Get(source);
        record.Looping = looping;
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
    /// 测试辅助：读取 source 最近播放位置。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <returns>位置。</returns>
    public Vector3 GetSourcePosition(uint source)
    {
        return Get(source).Position;
    }

    /// <summary>
    /// 测试辅助：读取 source 最近一次播放 / 更新后的增益。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <returns>线性增益。</returns>
    public float GetSourceGain(uint source)
    {
        return Get(source).Gain;
    }

    /// <summary>
    /// 测试辅助：读取 source 最近配置的 reference distance。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <returns>reference distance。</returns>
    public float GetSourceReferenceDistance(uint source)
    {
        return Get(source).ReferenceDistance;
    }

    /// <summary>
    /// 测试辅助：读取 source 最近配置的 max distance。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <returns>max distance。</returns>
    public float GetSourceMaxDistance(uint source)
    {
        return Get(source).MaxDistance;
    }

    /// <summary>
    /// 测试辅助：读取 source 最近配置的 rolloff factor。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <returns>rolloff factor。</returns>
    public float GetSourceRolloffFactor(uint source)
    {
        return Get(source).RolloffFactor;
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

    /// <summary>
    /// 测试辅助：模拟 source 上有已处理 streaming buffer。
    /// </summary>
    /// <param name="source">source 句柄。</param>
    /// <param name="count">已处理 buffer 数。</param>
    public void MarkProcessedBuffers(uint source, int count)
    {
        SourceRecord record = Get(source);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        record.ProcessedBuffers = count;
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

    private BufferRecord GetBuffer(uint buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer == 0 || buffer > _buffers.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer), "未知 buffer 句柄。");
        }

        BufferRecord record = _buffers[(int)buffer - 1];
        return record.Deleted ? throw new ObjectDisposedException(nameof(buffer)) : record;
    }

    private sealed class SourceRecord(uint handle)
    {
        public readonly uint Handle = handle;
        public AudioSourceState State = AudioSourceState.Initial;
        public uint Buffer;
        public Vector3 Position;
        public float Gain;
        public float Pitch;
        public float ReferenceDistance;
        public float MaxDistance;
        public float RolloffFactor;
        public bool Deleted;
        public bool Looping;
        public int QueuedBuffers;
        public int ProcessedBuffers;
        public Queue<uint> QueuedBufferHandles { get; } = [];
    }

    private sealed class BufferRecord(uint handle)
    {
        public readonly uint Handle = handle;
        public AudioSampleFormat Format;
        public int SampleRate;
        public int ByteLength;
        public bool Deleted;
    }
}
