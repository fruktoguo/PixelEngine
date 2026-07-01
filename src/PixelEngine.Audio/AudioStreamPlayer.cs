using System.Collections.Concurrent;

namespace PixelEngine.Audio;

/// <summary>
/// streaming source 队列播放器。后台 worker 负责把已上传 buffer 排入 source，并取回 processed buffers。
/// </summary>
public sealed class AudioStreamPlayer : IDisposable
{
    private readonly IAudioBackend _backend;
    private readonly uint _source;
    private readonly ConcurrentQueue<uint> _pending = new();
    private readonly ConcurrentQueue<uint> _processed = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _worker;
    private volatile bool _running = true;
    private bool _disposed;

    /// <summary>
    /// 创建 streaming 播放器。
    /// </summary>
    /// <param name="backend">音频后端。</param>
    /// <param name="source">streaming source 句柄。</param>
    public AudioStreamPlayer(IAudioBackend backend, uint source)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _source = source;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "PixelEngine.AudioStreamPlayer",
        };
        _worker.Start();
    }

    /// <summary>
    /// 等待排入 source 的 buffer 数量快照。
    /// </summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// 已从 source 回收、等待调用方复用或删除的 buffer 数量快照。
    /// </summary>
    public int ProcessedCount => _processed.Count;

    /// <summary>
    /// 把已上传 buffer 交给 stream worker 排队。
    /// </summary>
    /// <param name="buffer">buffer 句柄。</param>
    public void Enqueue(uint buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pending.Enqueue(buffer);
        _ = _signal.Set();
    }

    /// <summary>
    /// 尝试取回已处理 buffer。
    /// </summary>
    /// <param name="buffer">buffer 句柄。</param>
    /// <returns>是否取到。</returns>
    public bool TryDequeueProcessed(out uint buffer)
    {
        return _processed.TryDequeue(out buffer);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _running = false;
        _ = _signal.Set();
        _worker.Join();
        _signal.Dispose();
    }

    private void WorkerLoop()
    {
        Span<uint> queueScratch = stackalloc uint[8];
        Span<uint> processedScratch = stackalloc uint[8];
        while (_running)
        {
            _ = _signal.WaitOne(10);
            int queued = 0;
            while (queued < queueScratch.Length && _pending.TryDequeue(out uint buffer))
            {
                queueScratch[queued++] = buffer;
            }

            if (queued > 0)
            {
                _backend.QueueBuffers(_source, queueScratch[..queued]);
            }

            int processed = _backend.UnqueueProcessedBuffers(_source, processedScratch);
            for (int i = 0; i < processed; i++)
            {
                _processed.Enqueue(processedScratch[i]);
            }
        }
    }
}
