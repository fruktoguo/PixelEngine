using System.Numerics;
using Silk.NET.OpenAL;

namespace PixelEngine.Audio;

/// <summary>
/// 基于 Silk.NET.OpenAL 的音频后端。
/// </summary>
public sealed unsafe class OpenAlBackend : IAudioBackend
{
    private readonly AL _al;
    private readonly HashSet<uint> _liveSources = [];
    private readonly HashSet<uint> _liveBuffers = [];
    private bool _disposed;

    internal OpenAlBackend(AL al)
    {
        _al = al ?? throw new ArgumentNullException(nameof(al));
    }

    /// <summary>
    /// 当前仍未删除的 OpenAL source 数，用于 native leak detector 证据采集。
    /// </summary>
    public int LiveSourceCount => _liveSources.Count;

    /// <summary>
    /// 当前仍未删除的 OpenAL buffer 数，用于 native leak detector 证据采集。
    /// </summary>
    public int LiveBufferCount => _liveBuffers.Count;

    /// <summary>
    /// 当前仍未删除的 OpenAL source 与 buffer 总数。
    /// </summary>
    public int LiveObjectCount => LiveSourceCount + LiveBufferCount;

    // --- OpenAL 对象生命周期：source/buffer 创建与删除，并跟踪 live 计数供泄漏检测 ---

    /// <inheritdoc />
    public uint CreateSource()
    {
        ThrowIfDisposed();
        uint source = _al.GenSource();
        _ = _liveSources.Add(source);
        return source;
    }

    /// <inheritdoc />
    public uint CreateBuffer()
    {
        ThrowIfDisposed();
        uint buffer = _al.GenBuffer();
        _ = _liveBuffers.Add(buffer);
        return buffer;
    }

    /// <inheritdoc />
    public void DeleteSource(uint source)
    {
        ThrowIfDisposed();
        _al.DeleteSource(source);
        _ = _liveSources.Remove(source);
    }

    /// <inheritdoc />
    public void DeleteBuffer(uint buffer)
    {
        ThrowIfDisposed();
        _al.DeleteBuffer(buffer);
        _ = _liveBuffers.Remove(buffer);
    }

    // --- PCM 缓冲上传与流式队列：buffer 数据写入与 source 队列管理 ---

    /// <inheritdoc />
    public void UploadBuffer(uint buffer, AudioSampleFormat format, ReadOnlySpan<byte> pcm, int sampleRate)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        BufferFormat openAlFormat = format switch
        {
            AudioSampleFormat.Mono8 => BufferFormat.Mono8,
            AudioSampleFormat.Mono16 => BufferFormat.Mono16,
            AudioSampleFormat.Stereo8 => BufferFormat.Stereo8,
            AudioSampleFormat.Stereo16 => BufferFormat.Stereo16,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "未知 PCM 格式。"),
        };
        fixed (byte* data = pcm)
        {
            _al.BufferData(buffer, openAlFormat, data, pcm.Length, sampleRate);
        }
    }

    /// <inheritdoc />
    public void QueueBuffers(uint source, ReadOnlySpan<uint> buffers)
    {
        ThrowIfDisposed();
        fixed (uint* raw = buffers)
        {
            _al.SourceQueueBuffers(source, buffers.Length, raw);
        }
    }

    /// <inheritdoc />
    public int UnqueueProcessedBuffers(uint source, Span<uint> destination)
    {
        ThrowIfDisposed();
        int processed = Math.Min(GetProcessedBufferCount(source), destination.Length);
        if (processed == 0)
        {
            return 0;
        }

        fixed (uint* raw = destination)
        {
            _al.SourceUnqueueBuffers(source, processed, raw);
        }

        return processed;
    }

    /// <inheritdoc />
    public int GetProcessedBufferCount(uint source)
    {
        ThrowIfDisposed();
        _al.GetSourceProperty(source, GetSourceInteger.BuffersProcessed, out int processed);
        return processed;
    }

    // --- source 播放控制：距离衰减、增益、循环与启停 ---

    /// <inheritdoc />
    public void ConfigureSource(uint source, AudioSettings settings)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);
        AudioSettings validated = settings.Validate();
        _al.SetSourceProperty(source, SourceBoolean.SourceRelative, false);
        _al.SetSourceProperty(source, SourceFloat.Gain, 1f);
        _al.SetSourceProperty(source, SourceFloat.Pitch, 1f);
        _al.SetSourceProperty(source, SourceFloat.ReferenceDistance, validated.ReferenceDistance);
        _al.SetSourceProperty(source, SourceFloat.MaxDistance, validated.MaxDistance);
        _al.SetSourceProperty(source, SourceFloat.RolloffFactor, validated.RolloffFactor);
    }

    /// <inheritdoc />
    public void Play(uint source, uint buffer, in Vector3 position, float gain, float pitch)
    {
        ThrowIfDisposed();
        _al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
        _al.SetSourceProperty(source, SourceVector3.Position, position.X, position.Y, position.Z);
        _al.SetSourceProperty(source, SourceFloat.Gain, gain);
        _al.SetSourceProperty(source, SourceFloat.Pitch, pitch);
        _al.SourcePlay(source);
    }

    /// <inheritdoc />
    public void SetSourceGain(uint source, float gain)
    {
        ThrowIfDisposed();
        _al.SetSourceProperty(source, SourceFloat.Gain, gain);
    }

    /// <inheritdoc />
    public void SetSourceLooping(uint source, bool looping)
    {
        ThrowIfDisposed();
        _al.SetSourceProperty(source, SourceBoolean.Looping, looping);
    }

    /// <inheritdoc />
    public void Stop(uint source)
    {
        ThrowIfDisposed();
        _al.SourceStop(source);
        _al.SetSourceProperty(source, SourceInteger.Buffer, 0);
    }

    /// <inheritdoc />
    public AudioSourceState GetState(uint source)
    {
        ThrowIfDisposed();
        _al.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
        return (SourceState)state switch
        {
            SourceState.Initial => AudioSourceState.Initial,
            SourceState.Playing => AudioSourceState.Playing,
            SourceState.Paused => AudioSourceState.Paused,
            SourceState.Stopped => AudioSourceState.Stopped,
            _ => AudioSourceState.Initial,
        };
    }

    // --- listener 声场：主音量、位置与朝向（at/orient） ---

    /// <inheritdoc />
    public void SetListener(in AudioListenerState listener)
    {
        ThrowIfDisposed();
        _al.SetListenerProperty(ListenerFloat.Gain, listener.Gain);
        _al.SetListenerProperty(ListenerVector3.Position, listener.Position.X, listener.Position.Y, listener.Position.Z);

        float* orientation = stackalloc float[6]
        {
            listener.Forward.X,
            listener.Forward.Y,
            listener.Forward.Z,
            listener.Up.X,
            listener.Up.Y,
            listener.Up.Z,
        };
        _al.SetListenerProperty(ListenerFloatArray.Orientation, orientation);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
