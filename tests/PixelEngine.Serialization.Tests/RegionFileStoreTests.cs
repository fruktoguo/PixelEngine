using System.Buffers;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Serialization.Tests;

/// <summary>
/// RegionFileStore 的单 chunk 随机读写测试。
/// </summary>
public sealed class RegionFileStoreTests
{
    /// <summary>
    /// 验证 chunk blob 能写入 region 文件并按坐标读回。
    /// </summary>
    [Fact]
    public void RegionFileStoreRoundTripsChunkBlob()
    {
        using TempWorldDirectory world = TempWorldDirectory.Create();
        RegionFileStore store = new(world.Path);
        ChunkCoord coord = new(3, 4);
        byte[] blob = [1, 2, 3, 4, 5];

        store.Write(coord, blob);

        ArrayBufferWriter<byte> destination = new();
        Assert.True(store.Exists(coord));
        Assert.True(store.TryRead(coord, destination));
        Assert.Equal(blob, destination.WrittenSpan.ToArray());
    }

    /// <summary>
    /// 验证覆盖写入只暴露最新 blob。
    /// </summary>
    [Fact]
    public void RegionFileStoreOverwritesExistingChunk()
    {
        using TempWorldDirectory world = TempWorldDirectory.Create();
        RegionFileStore store = new(world.Path);
        ChunkCoord coord = new(7, 8);
        store.Write(coord, [1, 1, 1]);

        store.Write(coord, [9, 8, 7, 6]);

        ArrayBufferWriter<byte> destination = new();
        Assert.True(store.TryRead(coord, destination));
        Assert.Equal([9, 8, 7, 6], destination.WrittenSpan.ToArray());
    }

    /// <summary>
    /// 验证删除只清除目标 chunk 的索引。
    /// </summary>
    [Fact]
    public void RegionFileStoreDeletesChunkBlob()
    {
        using TempWorldDirectory world = TempWorldDirectory.Create();
        RegionFileStore store = new(world.Path);
        ChunkCoord removed = new(1, 2);
        ChunkCoord kept = new(2, 2);
        store.Write(removed, [1, 2, 3]);
        store.Write(kept, [4, 5, 6]);

        store.Delete(removed);

        ArrayBufferWriter<byte> removedDestination = new();
        ArrayBufferWriter<byte> keptDestination = new();
        Assert.False(store.Exists(removed));
        Assert.False(store.TryRead(removed, removedDestination));
        Assert.True(store.Exists(kept));
        Assert.True(store.TryRead(kept, keptDestination));
        Assert.Equal([4, 5, 6], keptDestination.WrittenSpan.ToArray());
    }

    /// <summary>
    /// 验证负坐标使用 floor-div 映射到正确 region 与本地索引。
    /// </summary>
    [Fact]
    public void RegionFileStoreMapsNegativeChunkCoordsWithFloorDivision()
    {
        using TempWorldDirectory world = TempWorldDirectory.Create();
        RegionFileStore store = new(world.Path);
        ChunkCoord rightEdge = new(-1, 0);
        ChunkCoord leftEdge = new(-32, 0);
        store.Write(rightEdge, [31]);
        store.Write(leftEdge, [0]);

        string regionPath = System.IO.Path.Combine(world.Path, "regions", "r.-1.0.rgn");
        Assert.True(File.Exists(regionPath));

        ArrayBufferWriter<byte> rightDestination = new();
        ArrayBufferWriter<byte> leftDestination = new();
        Assert.True(store.TryRead(rightEdge, rightDestination));
        Assert.True(store.TryRead(leftEdge, leftDestination));
        Assert.Equal([31], rightDestination.WrittenSpan.ToArray());
        Assert.Equal([0], leftDestination.WrittenSpan.ToArray());
    }

    /// <summary>
    /// 验证缺失 region 或缺失 chunk 返回 false。
    /// </summary>
    [Fact]
    public void RegionFileStoreMissingChunkReturnsFalse()
    {
        using TempWorldDirectory world = TempWorldDirectory.Create();
        RegionFileStore store = new(world.Path);
        ArrayBufferWriter<byte> destination = new();

        Assert.False(store.Exists(new ChunkCoord(100, 100)));
        Assert.False(store.TryRead(new ChunkCoord(100, 100), destination));

        store.Write(new ChunkCoord(0, 0), [1]);
        Assert.False(store.Exists(new ChunkCoord(1, 0)));
        Assert.False(store.TryRead(new ChunkCoord(1, 0), destination));
        Assert.Empty(destination.WrittenSpan.ToArray());
    }

    private sealed class TempWorldDirectory : IDisposable
    {
        private TempWorldDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempWorldDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PixelEngine.RegionFileStoreTests",
                Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(path);
            return new TempWorldDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
