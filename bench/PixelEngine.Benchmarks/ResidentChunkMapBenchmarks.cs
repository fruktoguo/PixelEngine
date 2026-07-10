using BenchmarkDotNet.Attributes;
using PixelEngine.Simulation;
using PixelEngine.World;

namespace PixelEngine.Benchmarks;

/// <summary>
/// ResidentChunkMap 批量驻留快照重建与指数扩容基准。
/// </summary>
[MemoryDiagnoser]
public class ResidentChunkMapBenchmarks
{
    /// <summary>
    /// 本批次加入的 chunk 数量，覆盖 PERF-006 验收档位。
    /// </summary>
    [Params(1, 16, 64, 256)]
    public int ChunkCount { get; set; }

    private ResidentChunkMap _map = null!;
    private Chunk[] _chunks = [];

    /// <summary>
    /// 在每轮 measured iteration 外准备新 map 与 chunk 批次，避免 fixture 初始化污染被测调用。
    /// </summary>
    [IterationSetup]
    public void Setup()
    {
        _map = new ResidentChunkMap();
        _chunks = new Chunk[ChunkCount];
        for (int i = 0; i < ChunkCount; i++)
        {
            _chunks[i] = new Chunk(new ChunkCoord(i, 0));
        }
    }

    /// <summary>
    /// 基线：逐个添加，每次添加都重建驻留快照。
    /// </summary>
    [Benchmark(Baseline = true)]
    [InvocationCount(1)]
    public int AddIndividually()
    {
        for (int i = 0; i < _chunks.Length; i++)
        {
            _map.Add(_chunks[i]);
        }

        return _map.SnapshotRebuildCount;
    }

    /// <summary>
    /// 目标：批量添加只重建一次驻留快照，并按指数扩容快照数组。
    /// </summary>
    [Benchmark]
    [InvocationCount(1)]
    public int AddRange()
    {
        _map.AddRange(_chunks);
        return _map.SnapshotRebuildCount;
    }
}
