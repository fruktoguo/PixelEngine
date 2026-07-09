using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.World.Tests;

/// <summary>
/// World live chunk map 与流式队列测试。
/// 不变式：live map 与流式队列操作原子、无重复装载。
/// </summary>
public sealed class ResidentChunkMapAndQueueTests
{
    /// <summary>
    /// 验证 ResidentChunkMap 提供稳定驻留快照与 3x3 邻域解析。
    /// </summary>
    [Fact]
    public void ResidentChunkMapAddsRemovesAndResolvesNeighborhood()
    {
        ResidentChunkMap map = new();
        Chunk center = null!;
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                Chunk chunk = new(new ChunkCoord(x, y));
                map.Add(chunk);
                if (x == 0 && y == 0)
                {
                    center = chunk;
                }
            }
        }

        Assert.Equal(9, map.Count);
        Assert.Equal(9, map.ResidentChunks.Length);
        Assert.True(map.TryGetChunk(new ChunkCoord(0, 0), out Chunk loaded));
        Assert.Same(center, loaded);
        Assert.True(map.ResolveNeighborhood(new ChunkCoord(0, 0), out ChunkNeighborhood neighborhood));
        Assert.Same(center, neighborhood.Slot4);

        Assert.True(map.TryRemove(new ChunkCoord(1, 1), out Chunk removed));
        Assert.Equal(new ChunkCoord(1, 1), removed.Coord);
        Assert.Equal(8, map.ResidentChunks.Length);
        Assert.False(map.ResolveNeighborhood(new ChunkCoord(0, 0), out _));
    }

    /// <summary>
    /// 验证重复添加会失败，避免 live map 出现双对象同坐标。
    /// </summary>
    [Fact]
    public void ResidentChunkMapRejectsDuplicateChunkCoord()
    {
        ResidentChunkMap map = new();
        Chunk first = new(new ChunkCoord(2, 3));
        map.Add(first);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => map.Add(new Chunk(new ChunkCoord(2, 3))));

        Assert.Contains("已驻留", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证流式请求队列保持 FIFO 并校验 load/unload payload。
    /// </summary>
    [Fact]
    public void StreamingRequestQueuePreservesFifoAndValidatesPayload()
    {
        StreamingRequestQueue queue = new();
        Chunk chunk = new(new ChunkCoord(4, 5));
        Half[] temperature = new Half[TemperatureField.BlockArea];
        queue.Enqueue(StreamingRequest.Load(new ChunkCoord(1, 2)));
        queue.Enqueue(StreamingRequest.Unload(chunk, temperature));

        Assert.Equal(2, queue.Count);
        Assert.True(queue.TryDequeue(out StreamingRequest load));
        Assert.Equal(StreamingRequestKind.Load, load.Kind);
        Assert.Equal(new ChunkCoord(1, 2), load.Coord);
        Assert.True(queue.TryDequeue(out StreamingRequest unload));
        Assert.Equal(StreamingRequestKind.Unload, unload.Kind);
        Assert.Same(chunk, unload.DetachedChunk);
        Assert.Equal(TemperatureField.BlockArea, unload.Temperature!.Length);
        Assert.False(queue.TryDequeue(out _));

        _ = Assert.Throws<ArgumentException>(() =>
            queue.Enqueue(new StreamingRequest(StreamingRequestKind.Unload, new ChunkCoord(0, 0), null, null)));
    }

    /// <summary>
    /// 验证完成队列保持 FIFO 并校验 loaded/unloaded payload。
    /// </summary>
    [Fact]
    public void CompletedChunkQueuePreservesFifoAndValidatesPayload()
    {
        CompletedChunkQueue queue = new();
        Chunk chunk = new(new ChunkCoord(-2, 7));
        Half[] temperature = new Half[TemperatureField.BlockArea];
        queue.Enqueue(CompletedStreamingOperation.Loaded(chunk, temperature));
        queue.Enqueue(CompletedStreamingOperation.Unloaded(new ChunkCoord(9, 9)));

        Assert.Equal(2, queue.Count);
        Assert.True(queue.TryDequeue(out CompletedStreamingOperation loaded));
        Assert.Equal(CompletedStreamingKind.Loaded, loaded.Kind);
        Assert.Same(chunk, loaded.Chunk);
        Assert.Equal(TemperatureField.BlockArea, loaded.Temperature!.Length);
        Assert.True(queue.TryDequeue(out CompletedStreamingOperation unloaded));
        Assert.Equal(CompletedStreamingKind.Unloaded, unloaded.Kind);
        Assert.Equal(new ChunkCoord(9, 9), unloaded.Coord);
        Assert.False(queue.TryDequeue(out _));

        _ = Assert.Throws<ArgumentException>(() =>
            queue.Enqueue(new CompletedStreamingOperation(CompletedStreamingKind.Loaded, new ChunkCoord(0, 0), null, null)));
    }
}
