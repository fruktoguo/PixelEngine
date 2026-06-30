using System.Buffers;
using PixelEngine.Core.Threading;
using PixelEngine.Serialization;
using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// 负责相位 2 与后台 I/O 之间的 chunk 流式装卸屏障。
/// </summary>
public sealed class WorldStreamer
{
    private readonly ResidentChunkMap _chunks;
    private readonly ResidencyTable _residency;
    private readonly ChunkMemoryBudget _budget;
    private readonly TemperatureField _temperature;
    private readonly IChunkStore _chunkStore;
    private readonly MaterialRemap _materialRemap;
    private readonly ChunkCodec _chunkCodec;
    private readonly ChunkPool _chunkPool;
    private readonly StreamingRequestQueue _requests;
    private readonly CompletedChunkQueue _completed;
    private readonly PreparationBatch _preparationBatch;
    private StreamingRequest[] _requestBatch = [];
    private PreparedStreamingOperation[] _preparedBatch = [];
    private static readonly ThreadLocal<PooledByteBufferWriter> IoWriters = new(() => new PooledByteBufferWriter());
    private static readonly ThreadLocal<PooledByteBufferWriter> PayloadWriters = new(() => new PooledByteBufferWriter());

    /// <summary>
    /// 创建世界流式装卸器。
    /// </summary>
    public WorldStreamer(
        ResidentChunkMap chunks,
        ResidencyTable residency,
        ChunkMemoryBudget budget,
        TemperatureField temperature,
        IChunkStore chunkStore,
        MaterialRemap materialRemap,
        ChunkCodec? chunkCodec = null,
        ChunkPool? chunkPool = null,
        StreamingRequestQueue? requests = null,
        CompletedChunkQueue? completed = null)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(residency);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(temperature);
        ArgumentNullException.ThrowIfNull(chunkStore);
        ArgumentNullException.ThrowIfNull(materialRemap);

        _chunks = chunks;
        _residency = residency;
        _budget = budget;
        _temperature = temperature;
        _chunkStore = chunkStore;
        _materialRemap = materialRemap;
        _chunkCodec = chunkCodec ?? new ChunkCodec();
        _chunkPool = chunkPool ?? new ChunkPool();
        _requests = requests ?? new StreamingRequestQueue();
        _completed = completed ?? new CompletedChunkQueue();
        _preparationBatch = new PreparationBatch(this);
    }

    /// <summary>
    /// 当前帧 parity bit，后台解码 chunk 时用于重置瞬时位。
    /// </summary>
    public byte CurrentParityBit { get; set; }

    /// <summary>
    /// 待处理流式请求数量。
    /// </summary>
    public int PendingRequestCount => _requests.Count;

    /// <summary>
    /// 待应用完成事件数量。
    /// </summary>
    public int PendingCompletedCount => _completed.Count;

    /// <summary>
    /// 相位 2：把后台完成的装载 / 卸载结果应用回 live world。
    /// </summary>
    public int ApplyPrepared(long frame)
    {
        int applied = 0;
        while (_completed.TryDequeue(out CompletedStreamingOperation operation))
        {
            if (operation.Kind == CompletedStreamingKind.Loaded)
            {
                ApplyLoaded(operation, frame);
            }
            else
            {
                ApplyUnloaded(operation.Coord);
            }

            applied++;
        }

        return applied;
    }

    /// <summary>
    /// 相位 2：提交驻留计划。卸载 chunk 会在此从 live map 摘下，磁盘 I/O 留给后台。
    /// </summary>
    public void SubmitPlan(ResidencyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ApplyStateChanges(plan.StateChanges);
        SubmitUnloads(plan.UnloadCoords);
        SubmitLoads(plan.LoadCoords);
    }

    /// <summary>
    /// 相位 11：持续消费流式请求，直到 cancellationToken 取消。
    /// </summary>
    public void ProcessIo(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (ProcessIoOnce() == 0)
            {
                Thread.Sleep(1);
            }
        }
    }

    /// <summary>
    /// 相位 11：处理当前队列中的所有请求，便于测试和外部调度器按批驱动。
    /// </summary>
    public int ProcessIoOnce()
    {
        return ProcessIoOnce(jobs: null);
    }

    /// <summary>
    /// 相位 11：处理当前队列中的所有请求；提供 jobs 时并行准备多 chunk 编解码，磁盘写仍在调用线程顺序提交。
    /// </summary>
    public int ProcessIoOnce(JobSystem? jobs)
    {
        int processed = 0;
        int requestCount = DrainRequests();
        if (requestCount == 0)
        {
            return 0;
        }

        EnsurePreparedCapacity(requestCount);
        if (jobs is not null && requestCount > 1)
        {
            _preparationBatch.Requests = _requestBatch;
            _preparationBatch.Prepared = _preparedBatch;
            jobs.ParallelRange(requestCount, 1, PrepareRange, _preparationBatch);
        }
        else
        {
            for (int i = 0; i < requestCount; i++)
            {
                _preparedBatch[i] = Prepare(_requestBatch[i]);
            }
        }

        for (int i = 0; i < requestCount; i++)
        {
            Publish(_preparedBatch[i]);
            _preparedBatch[i] = default;
            processed++;
        }

        return processed;
    }

    private void ApplyLoaded(CompletedStreamingOperation operation, long frame)
    {
        Chunk chunk = operation.Chunk ?? throw new InvalidOperationException("装载完成事件缺少 chunk。");
        Half[] temperature = operation.Temperature ?? throw new InvalidOperationException("装载完成事件缺少温度子块。");
        try
        {
            if (_chunks.Contains(chunk.Coord))
            {
                _chunkPool.Return(chunk);
                return;
            }

            _temperature.ImportBlock(chunk.Coord, temperature.AsSpan(0, TemperatureField.BlockArea));
            chunk.SetCurrentDirty(DirtyRect.Full);
            _chunks.Add(chunk);
            _residency.Set(
                chunk.Coord,
                new ChunkResidencyInfo(
                    ChunkResidencyState.Cached,
                    frame,
                    ChunkMemoryBudget.EstimatedResidentChunkBytes,
                    DirtySinceLoad: false));
            _budget.Add(ChunkMemoryBudget.EstimatedResidentChunkBytes);
        }
        finally
        {
            ArrayPool<Half>.Shared.Return(temperature);
        }
    }

    private void ApplyUnloaded(ChunkCoord coord)
    {
        _ = _residency.Remove(coord);
        _budget.Remove(ChunkMemoryBudget.EstimatedResidentChunkBytes);
    }

    private void ApplyStateChanges(ReadOnlySpan<ResidencyStateChange> stateChanges)
    {
        for (int i = 0; i < stateChanges.Length; i++)
        {
            ResidencyStateChange change = stateChanges[i];
            if (!_residency.TryGetInfo(change.Coord, out ChunkResidencyInfo info))
            {
                continue;
            }

            _residency.Set(change.Coord, info with { State = change.State });
        }
    }

    private void SubmitUnloads(ReadOnlySpan<ChunkCoord> unloadCoords)
    {
        for (int i = 0; i < unloadCoords.Length; i++)
        {
            ChunkCoord coord = unloadCoords[i];
            if (!_chunks.TryRemove(coord, out Chunk chunk))
            {
                continue;
            }

            Half[] temperature = ArrayPool<Half>.Shared.Rent(TemperatureField.BlockArea);
            bool queued = false;
            try
            {
                _temperature.ExportBlock(coord, temperature.AsSpan(0, TemperatureField.BlockArea));
                _requests.Enqueue(StreamingRequest.Unload(chunk, temperature));
                queued = true;
            }
            finally
            {
                if (!queued)
                {
                    ArrayPool<Half>.Shared.Return(temperature);
                }
            }

            if (_residency.TryGetInfo(coord, out ChunkResidencyInfo info))
            {
                _residency.Set(coord, info with { State = ChunkResidencyState.Detached });
            }
        }
    }

    private void SubmitLoads(ReadOnlySpan<ChunkCoord> loadCoords)
    {
        for (int i = 0; i < loadCoords.Length; i++)
        {
            ChunkCoord coord = loadCoords[i];
            if (_chunks.Contains(coord))
            {
                continue;
            }

            _residency.Set(
                coord,
                new ChunkResidencyInfo(
                    ChunkResidencyState.Detached,
                    LastTouchedFrame: 0,
                    ResidentBytes: ChunkMemoryBudget.EstimatedResidentChunkBytes,
                    DirtySinceLoad: false));
            _requests.Enqueue(StreamingRequest.Load(coord));
        }
    }

    private int DrainRequests()
    {
        int count = 0;
        while (_requests.TryDequeue(out StreamingRequest request))
        {
            EnsureRequestCapacity(count + 1);
            _requestBatch[count++] = request;
        }

        return count;
    }

    private static void PrepareRange(int start, int end, int workerIndex, object? context)
    {
        PreparationBatch batch = (PreparationBatch)context!;
        for (int i = start; i < end; i++)
        {
            batch.Prepared[i] = batch.Streamer.Prepare(batch.Requests[i]);
        }
    }

    private PreparedStreamingOperation Prepare(StreamingRequest request)
    {
        return request.Kind == StreamingRequestKind.Load
            ? PrepareLoad(request.Coord)
            : PrepareUnload(request);
    }

    private void Publish(PreparedStreamingOperation prepared)
    {
        if (prepared.Kind == CompletedStreamingKind.Loaded)
        {
            _completed.Enqueue(CompletedStreamingOperation.Loaded(prepared.Chunk!, prepared.Temperature!));
            return;
        }

        byte[] blob = prepared.Blob ?? throw new InvalidOperationException("卸载准备结果缺少 blob。");
        try
        {
            _chunkStore.Write(prepared.Coord, blob.AsSpan(0, prepared.BlobLength));
            _completed.Enqueue(CompletedStreamingOperation.Unloaded(prepared.Coord));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(blob);
        }
    }

    private PreparedStreamingOperation PrepareLoad(ChunkCoord coord)
    {
        Chunk chunk = _chunkPool.Rent(coord);
        Half[] temperature = ArrayPool<Half>.Shared.Rent(TemperatureField.BlockArea);
        PooledByteBufferWriter buffer = RentIoWriter();
        buffer.Clear();
        try
        {
            if (_chunkStore.TryRead(coord, buffer))
            {
                _chunkCodec.Decode(
                    buffer.WrittenSpan,
                    new ChunkSnapshot(coord, chunk.Material, chunk.Flags, chunk.Lifetime, temperature.AsSpan(0, TemperatureField.BlockArea)),
                    CurrentParityBit);
                _materialRemap.RemapInPlace(chunk.Material);
            }

            chunk.SetCurrentDirty(DirtyRect.Full);
            return PreparedStreamingOperation.Loaded(chunk, temperature);
        }
        catch
        {
            _chunkPool.Return(chunk);
            ArrayPool<Half>.Shared.Return(temperature);
            throw;
        }
    }

    private PreparedStreamingOperation PrepareUnload(StreamingRequest request)
    {
        Chunk chunk = request.DetachedChunk ?? throw new InvalidOperationException("卸载请求缺少 chunk。");
        Half[] temperature = request.Temperature ?? throw new InvalidOperationException("卸载请求缺少温度子块。");
        try
        {
            PooledByteBufferWriter buffer = RentIoWriter();
            PooledByteBufferWriter payload = RentPayloadWriter();
            buffer.Clear();
            _chunkCodec.Encode(
                new ChunkSnapshot(chunk.Coord, chunk.Material, chunk.Flags, chunk.Lifetime, temperature.AsSpan(0, TemperatureField.BlockArea)),
                buffer,
                payload);
            ChunkCoord coord = chunk.Coord;
            byte[] blob = buffer.DetachWrittenBuffer(out int blobLength);
            _chunkPool.Return(chunk);
            return PreparedStreamingOperation.Unloaded(coord, blob, blobLength);
        }
        finally
        {
            ArrayPool<Half>.Shared.Return(temperature);
        }
    }

    private void EnsureRequestCapacity(int required)
    {
        if (_requestBatch.Length < required)
        {
            Array.Resize(ref _requestBatch, required);
        }
    }

    private void EnsurePreparedCapacity(int required)
    {
        if (_preparedBatch.Length < required)
        {
            Array.Resize(ref _preparedBatch, required);
        }
    }

    private static PooledByteBufferWriter RentIoWriter()
    {
        return IoWriters.Value ?? throw new InvalidOperationException("线程本地 I/O writer 未初始化。");
    }

    private static PooledByteBufferWriter RentPayloadWriter()
    {
        return PayloadWriters.Value ?? throw new InvalidOperationException("线程本地 payload writer 未初始化。");
    }

    private sealed class PreparationBatch(WorldStreamer streamer)
    {
        internal WorldStreamer Streamer { get; } = streamer;

        internal StreamingRequest[] Requests { get; set; } = [];

        internal PreparedStreamingOperation[] Prepared { get; set; } = [];
    }

    private readonly record struct PreparedStreamingOperation(
        CompletedStreamingKind Kind,
        ChunkCoord Coord,
        Chunk? Chunk,
        Half[]? Temperature,
        byte[]? Blob,
        int BlobLength)
    {
        internal static PreparedStreamingOperation Loaded(Chunk chunk, Half[] temperature)
        {
            return new PreparedStreamingOperation(CompletedStreamingKind.Loaded, chunk.Coord, chunk, temperature, null, 0);
        }

        internal static PreparedStreamingOperation Unloaded(ChunkCoord coord, byte[] blob, int blobLength)
        {
            return new PreparedStreamingOperation(CompletedStreamingKind.Unloaded, coord, null, null, blob, blobLength);
        }
    }
}
