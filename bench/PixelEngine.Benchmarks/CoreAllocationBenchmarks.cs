using BenchmarkDotNet.Attributes;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Memory;
using PixelEngine.Core.Threading;

namespace PixelEngine.Benchmarks;

/// <summary>
/// Core 基础设施零分配基准。
/// </summary>
[MemoryDiagnoser]
public class CoreAllocationBenchmarks
{
    private static readonly ChunkJob<int> SumJob = Sum;

    private static int _sink;

    private readonly FrameProfiler _profiler = new();
    private readonly JobSystem _jobs = new(workerCount: 1);
    private readonly int[] _items = [1, 2, 3, 4];
    private readonly Pool<object> _pool = new(static () => new object(), preallocate: 1);

    /// <summary>
    /// 对象池稳态租还。
    /// </summary>
    [Benchmark]
    public void PoolRentReturn()
    {
        object item = _pool.Rent();
        _pool.Return(item);
    }

    /// <summary>
    /// ArrayPool 租借包装稳态租还。
    /// </summary>
    [Benchmark]
    public void RentedArrayRentDispose()
    {
        using RentedArray<int> rented = RentedArray<int>.Rent(16);
        rented.Span[0] = 1;
    }

    /// <summary>
    /// FrameProfiler using-scope 计时。
    /// </summary>
    [Benchmark]
    public void FrameProfilerMeasure()
    {
        using FrameProfiler.ProfilerScope scope = _profiler.Measure(FramePhase.FrameClock);
    }

    /// <summary>
    /// JobSystem ParallelFor 稳态派发。
    /// </summary>
    [Benchmark]
    public int JobSystemParallelFor()
    {
        _jobs.ParallelFor(_items, SumJob);
        return _sink;
    }

    private static void Sum(in int item, int workerIndex)
    {
        _sink += item + workerIndex;
    }
}
