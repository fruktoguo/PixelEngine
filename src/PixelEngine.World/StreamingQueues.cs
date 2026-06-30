using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// 后台流式请求类型。
/// </summary>
public enum StreamingRequestKind
{
    /// <summary>
    /// 装载指定 chunk。
    /// </summary>
    Load,

    /// <summary>
    /// 卸载并持久化指定游离 chunk。
    /// </summary>
    Unload,
}

/// <summary>
/// 相位 2 提交给后台 I/O 的流式请求。
/// </summary>
public readonly record struct StreamingRequest(
    StreamingRequestKind Kind,
    ChunkCoord Coord,
    Chunk? DetachedChunk,
    Half[]? Temperature)
{
    /// <summary>
    /// 创建装载请求。
    /// </summary>
    public static StreamingRequest Load(ChunkCoord coord)
    {
        return new StreamingRequest(StreamingRequestKind.Load, coord, null, null);
    }

    /// <summary>
    /// 创建卸载请求。
    /// </summary>
    public static StreamingRequest Unload(Chunk chunk, ReadOnlySpan<Half> temperature)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        bool validTemperatureLength = temperature.Length == TemperatureField.BlockArea;
        return validTemperatureLength
            ? new StreamingRequest(StreamingRequestKind.Unload, chunk.Coord, chunk, temperature.ToArray())
            : throw new ArgumentException("卸载请求温度子块长度必须等于 BlockArea。", nameof(temperature));
    }
}

/// <summary>
/// 后台流式完成事件类型。
/// </summary>
public enum CompletedStreamingKind
{
    /// <summary>
    /// chunk 已装载到游离对象。
    /// </summary>
    Loaded,

    /// <summary>
    /// 游离 chunk 已持久化并可释放。
    /// </summary>
    Unloaded,
}

/// <summary>
/// 后台 I/O 提交回相位 2 的完成事件。
/// </summary>
public readonly record struct CompletedStreamingOperation(
    CompletedStreamingKind Kind,
    ChunkCoord Coord,
    Chunk? Chunk,
    Half[]? Temperature)
{
    /// <summary>
    /// 创建装载完成事件。
    /// </summary>
    public static CompletedStreamingOperation Loaded(Chunk chunk, ReadOnlySpan<Half> temperature)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        bool validTemperatureLength = temperature.Length == TemperatureField.BlockArea;
        return validTemperatureLength
            ? new CompletedStreamingOperation(CompletedStreamingKind.Loaded, chunk.Coord, chunk, temperature.ToArray())
            : throw new ArgumentException("装载完成事件温度子块长度必须等于 BlockArea。", nameof(temperature));
    }

    /// <summary>
    /// 创建卸载完成事件。
    /// </summary>
    public static CompletedStreamingOperation Unloaded(ChunkCoord coord)
    {
        return new CompletedStreamingOperation(CompletedStreamingKind.Unloaded, coord, null, null);
    }
}

/// <summary>
/// 相位 2 到后台 I/O 的流式请求队列。
/// </summary>
public sealed class StreamingRequestQueue
{
    private readonly Queue<StreamingRequest> _queue = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// 当前队列长度。
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>
    /// 入队请求。
    /// </summary>
    public void Enqueue(StreamingRequest request)
    {
        ValidateRequest(request);
        lock (_gate)
        {
            _queue.Enqueue(request);
        }
    }

    /// <summary>
    /// 尝试出队请求。
    /// </summary>
    public bool TryDequeue(out StreamingRequest request)
    {
        lock (_gate)
        {
            if (_queue.Count == 0)
            {
                request = default;
                return false;
            }

            request = _queue.Dequeue();
            return true;
        }
    }

    private static void ValidateRequest(StreamingRequest request)
    {
        if (request.Kind == StreamingRequestKind.Unload && (request.DetachedChunk is null || request.Temperature is null))
        {
            throw new ArgumentException("卸载请求必须携带游离 chunk 与温度子块。", nameof(request));
        }

        if (request.Kind == StreamingRequestKind.Load && (request.DetachedChunk is not null || request.Temperature is not null))
        {
            throw new ArgumentException("装载请求不能携带 chunk 或温度子块。", nameof(request));
        }
    }
}

/// <summary>
/// 后台 I/O 到相位 2 的流式完成队列。
/// </summary>
public sealed class CompletedChunkQueue
{
    private readonly Queue<CompletedStreamingOperation> _queue = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// 当前队列长度。
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>
    /// 入队完成事件。
    /// </summary>
    public void Enqueue(CompletedStreamingOperation operation)
    {
        ValidateOperation(operation);
        lock (_gate)
        {
            _queue.Enqueue(operation);
        }
    }

    /// <summary>
    /// 尝试出队完成事件。
    /// </summary>
    public bool TryDequeue(out CompletedStreamingOperation operation)
    {
        lock (_gate)
        {
            if (_queue.Count == 0)
            {
                operation = default;
                return false;
            }

            operation = _queue.Dequeue();
            return true;
        }
    }

    private static void ValidateOperation(CompletedStreamingOperation operation)
    {
        if (operation.Kind == CompletedStreamingKind.Loaded && (operation.Chunk is null || operation.Temperature is null))
        {
            throw new ArgumentException("装载完成事件必须携带游离 chunk 与温度子块。", nameof(operation));
        }

        if (operation.Kind == CompletedStreamingKind.Unloaded && (operation.Chunk is not null || operation.Temperature is not null))
        {
            throw new ArgumentException("卸载完成事件不能携带 chunk 或温度子块。", nameof(operation));
        }
    }
}
