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
    private readonly StreamingRequestQueue _requests;
    private readonly CompletedChunkQueue _completed;

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
        _requests = requests ?? new StreamingRequestQueue();
        _completed = completed ?? new CompletedChunkQueue();
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
        StreamingRequest[] requests = DrainRequests();
        if (requests.Length == 0)
        {
            return 0;
        }

        PreparedStreamingOperation[] prepared = new PreparedStreamingOperation[requests.Length];
        if (jobs is not null && requests.Length > 1)
        {
            PreparationBatch batch = new(this, requests, prepared);
            jobs.ParallelRange(requests.Length, 1, PrepareRange, batch);
        }
        else
        {
            for (int i = 0; i < requests.Length; i++)
            {
                prepared[i] = Prepare(requests[i]);
            }
        }

        for (int i = 0; i < prepared.Length; i++)
        {
            Publish(prepared[i]);
            processed++;
        }

        return processed;
    }

    private void ApplyLoaded(CompletedStreamingOperation operation, long frame)
    {
        Chunk chunk = operation.Chunk ?? throw new InvalidOperationException("装载完成事件缺少 chunk。");
        Half[] temperature = operation.Temperature ?? throw new InvalidOperationException("装载完成事件缺少温度子块。");
        if (_chunks.Contains(chunk.Coord))
        {
            return;
        }

        _temperature.ImportBlock(chunk.Coord, temperature);
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

    private void ApplyUnloaded(ChunkCoord coord)
    {
        _ = _residency.Remove(coord);
        _budget.Remove(ChunkMemoryBudget.EstimatedResidentChunkBytes);
    }

    private void ApplyStateChanges(IReadOnlyList<ResidencyStateChange> stateChanges)
    {
        for (int i = 0; i < stateChanges.Count; i++)
        {
            ResidencyStateChange change = stateChanges[i];
            if (!_residency.TryGetInfo(change.Coord, out ChunkResidencyInfo info))
            {
                continue;
            }

            _residency.Set(change.Coord, info with { State = change.State });
        }
    }

    private void SubmitUnloads(IReadOnlyList<ChunkCoord> unloadCoords)
    {
        Half[] temperature = new Half[TemperatureField.BlockArea];
        for (int i = 0; i < unloadCoords.Count; i++)
        {
            ChunkCoord coord = unloadCoords[i];
            if (!_chunks.TryRemove(coord, out Chunk chunk))
            {
                continue;
            }

            _temperature.ExportBlock(coord, temperature);
            _requests.Enqueue(StreamingRequest.Unload(chunk, temperature));
            if (_residency.TryGetInfo(coord, out ChunkResidencyInfo info))
            {
                _residency.Set(coord, info with { State = ChunkResidencyState.Detached });
            }
        }
    }

    private void SubmitLoads(IReadOnlyList<ChunkCoord> loadCoords)
    {
        for (int i = 0; i < loadCoords.Count; i++)
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

    private StreamingRequest[] DrainRequests()
    {
        List<StreamingRequest> drained = [];
        while (_requests.TryDequeue(out StreamingRequest request))
        {
            drained.Add(request);
        }

        return [.. drained];
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

        _chunkStore.Write(prepared.Coord, prepared.Blob!);
        _completed.Enqueue(CompletedStreamingOperation.Unloaded(prepared.Coord));
    }

    private PreparedStreamingOperation PrepareLoad(ChunkCoord coord)
    {
        Chunk chunk = new(coord);
        Half[] temperature = new Half[TemperatureField.BlockArea];
        using PooledByteBufferWriter buffer = new();
        if (_chunkStore.TryRead(coord, buffer))
        {
            _chunkCodec.Decode(
                buffer.WrittenSpan,
                new ChunkSnapshot(coord, chunk.Material, chunk.Flags, chunk.Lifetime, temperature),
                CurrentParityBit);
            _materialRemap.RemapInPlace(chunk.Material);
        }

        chunk.SetCurrentDirty(DirtyRect.Full);
        return PreparedStreamingOperation.Loaded(chunk, temperature);
    }

    private PreparedStreamingOperation PrepareUnload(StreamingRequest request)
    {
        Chunk chunk = request.DetachedChunk ?? throw new InvalidOperationException("卸载请求缺少 chunk。");
        Half[] temperature = request.Temperature ?? throw new InvalidOperationException("卸载请求缺少温度子块。");
        using PooledByteBufferWriter buffer = new();
        _chunkCodec.Encode(
            new ChunkSnapshot(chunk.Coord, chunk.Material, chunk.Flags, chunk.Lifetime, temperature),
            buffer);
        return PreparedStreamingOperation.Unloaded(chunk.Coord, buffer.WrittenSpan);
    }

    private sealed class PreparationBatch(
        WorldStreamer streamer,
        StreamingRequest[] requests,
        PreparedStreamingOperation[] prepared)
    {
        public WorldStreamer Streamer { get; } = streamer;

        public StreamingRequest[] Requests { get; } = requests;

        public PreparedStreamingOperation[] Prepared { get; } = prepared;
    }

    private sealed class PreparedStreamingOperation
    {
        private PreparedStreamingOperation(
            CompletedStreamingKind kind,
            ChunkCoord coord,
            Chunk? chunk,
            Half[]? temperature,
            byte[]? blob)
        {
            Kind = kind;
            Coord = coord;
            Chunk = chunk;
            Temperature = temperature;
            Blob = blob;
        }

        public CompletedStreamingKind Kind { get; }

        public ChunkCoord Coord { get; }

        public Chunk? Chunk { get; }

        public Half[]? Temperature { get; }

        public byte[]? Blob { get; }

        public static PreparedStreamingOperation Loaded(Chunk chunk, ReadOnlySpan<Half> temperature)
        {
            return new PreparedStreamingOperation(CompletedStreamingKind.Loaded, chunk.Coord, chunk, temperature.ToArray(), null);
        }

        public static PreparedStreamingOperation Unloaded(ChunkCoord coord, ReadOnlySpan<byte> blob)
        {
            return new PreparedStreamingOperation(CompletedStreamingKind.Unloaded, coord, null, null, blob.ToArray());
        }
    }
}
