using System.Buffers;
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
        int processed = 0;
        while (_requests.TryDequeue(out StreamingRequest request))
        {
            if (request.Kind == StreamingRequestKind.Load)
            {
                ProcessLoad(request.Coord);
            }
            else
            {
                ProcessUnload(request);
            }

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

    private void ProcessLoad(ChunkCoord coord)
    {
        Chunk chunk = new(coord);
        Half[] temperature = new Half[TemperatureField.BlockArea];
        ArrayBufferWriter<byte> buffer = new();
        if (_chunkStore.TryRead(coord, buffer))
        {
            _chunkCodec.Decode(
                buffer.WrittenSpan,
                new ChunkSnapshot(coord, chunk.Material, chunk.Flags, chunk.Lifetime, temperature),
                CurrentParityBit);
            _materialRemap.RemapInPlace(chunk.Material);
        }

        chunk.SetCurrentDirty(DirtyRect.Full);
        _completed.Enqueue(CompletedStreamingOperation.Loaded(chunk, temperature));
    }

    private void ProcessUnload(StreamingRequest request)
    {
        Chunk chunk = request.DetachedChunk ?? throw new InvalidOperationException("卸载请求缺少 chunk。");
        Half[] temperature = request.Temperature ?? throw new InvalidOperationException("卸载请求缺少温度子块。");
        ArrayBufferWriter<byte> buffer = new();
        _chunkCodec.Encode(
            new ChunkSnapshot(chunk.Coord, chunk.Material, chunk.Flags, chunk.Lifetime, temperature),
            buffer);
        _chunkStore.Write(chunk.Coord, buffer.WrittenSpan);
        _completed.Enqueue(CompletedStreamingOperation.Unloaded(chunk.Coord));
    }
}
