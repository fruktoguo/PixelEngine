using BenchmarkDotNet.Attributes;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Memory;
using PixelEngine.Core.Threading;

namespace PixelEngine.Benchmarks;

/// <summary>
/// Core 基础设施零分配基准。
/// </summary>
[MemoryDiagnoser]
public unsafe class CoreAllocationBenchmarks
{
    private static readonly ChunkJob<int> SumJob = Sum;
    private static readonly RangeJob RangeSumJob = RangeSum;

    private static int _sink;

    private readonly FrameProfiler _profiler = new();
    private readonly JobSystem _jobs = new(workerCount: 1);
    private readonly JobSystem _parallelJobs = new(workerCount: 2)
    {
        SingleThreadThreshold = 0,
    };
    private readonly int[] _items = [1, 2, 3, 4];
    private readonly int[] _rangeItems = [.. Enumerable.Range(1, 256)];
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
    /// 验证Rented Array Rent Dispose。
    /// </summary>
    [Benchmark]
    public void RentedArrayRentDispose()
    {
        using RentedArray<int> rented = RentedArray<int>.Rent(16);
        rented.Span[0] = 1;
    }

    /// <summary>
    /// 验证Frame Profiler Measure。
    /// </summary>
    [Benchmark]
    public void FrameProfilerMeasure()
    {
        using FrameProfiler.ProfilerScope scope = _profiler.Measure(FramePhase.InputAndTime);
    }

    /// <summary>
    /// 验证Job System Parallel For。
    /// </summary>
    [Benchmark]
    public int JobSystemParallelFor()
    {
        _jobs.ParallelFor(_items, SumJob);
        return _sink;
    }

    /// <summary>
    /// 验证Job System Parallel Range Multi Worker。
    /// </summary>
    [Benchmark]
    public int JobSystemParallelRangeMultiWorker()
    {
        _parallelJobs.ParallelRange(_rangeItems.Length, 16, RangeSumJob, _rangeItems);
        return _sink;
    }

    /// <summary>
    /// 验证Job System Parallel Range Raw Multi Worker。
    /// </summary>
    [Benchmark]
    public int JobSystemParallelRangeRawMultiWorker()
    {
        fixed (int* sink = &_sink)
        {
            _parallelJobs.ParallelRangeRaw(_rangeItems.Length, 16, &RawRangeSum, sink);
        }

        return _sink;
    }

    /// <summary>
    /// 释放 benchmark 持有的持久 worker。
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _jobs.Dispose();
        _parallelJobs.Dispose();
    }

    private static void Sum(in int item, int workerIndex)
    {
        _sink += item + workerIndex;
    }

    private static void RangeSum(int start, int end, int workerIndex, object? context)
    {
        int[] items = (int[])context!;
        int sum = workerIndex;
        for (int i = start; i < end; i++)
        {
            sum += items[i];
        }

        _ = Interlocked.Add(ref _sink, sum);
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void RawRangeSum(int start, int end, int workerIndex, void* context)
    {
        int sum = workerIndex;
        for (int i = start; i < end; i++)
        {
            sum += i;
        }

        _ = Interlocked.Add(ref *(int*)context, sum);
    }
}
