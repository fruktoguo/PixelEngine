using BenchmarkDotNet.Attributes;
using PixelEngine.Core.Threading;

namespace PixelEngine.Benchmarks;

/// <summary>
/// Core JobSystem worker 数扩展性与单线程回退阈值基准。
/// </summary>
[MemoryDiagnoser]
public sealed class CoreScalingBenchmark : IDisposable
{
    private static readonly RangeJob SumRangeJob = SumRange;

    private JobSystem? _jobs;
    private int[] _values = [];
    private long[] _partialSums = [];

    /// <summary>
    /// worker 数。0 表示按 JobSystem 默认处理器数。
    /// </summary>
    [Params(1, 2, 4, 0)]
    public int WorkerCount { get; set; }

    /// <summary>
    /// 区间元素数。
    /// </summary>
    [Params(65_536, 1_048_576)]
    public int ItemCount { get; set; }

    /// <summary>
    /// 创建 worker 与输入数据。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _jobs = new JobSystem(WorkerCount)
        {
            SingleThreadThreshold = 1024,
        };
        _values = GC.AllocateArray<int>(ItemCount, pinned: true);
        _partialSums = GC.AllocateArray<long>(_jobs.WorkerCount, pinned: true);
        for (int i = 0; i < _values.Length; i++)
        {
            _values[i] = i & 255;
        }
    }

    /// <summary>
    /// 释放 worker。
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    /// <summary>
    /// worker 1→N 扩展性主基准。
    /// </summary>
    [Benchmark]
    public long ParallelRangeSum()
    {
        Array.Clear(_partialSums);
        _jobs!.ParallelRange(_values.Length, 1024, SumRangeJob, this);
        long sum = 0;
        for (int i = 0; i < _partialSums.Length; i++)
        {
            sum += _partialSums[i];
        }

        return sum;
    }

    /// <summary>
    /// 小任务触发 SingleThreadThreshold 的回退路径。
    /// </summary>
    [Benchmark]
    public long SingleThreadThresholdFallback()
    {
        Array.Clear(_partialSums);
        _jobs!.ParallelRange(256, 1024, SumRangeJob, this);
        return _partialSums[0];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _jobs?.Dispose();
        _jobs = null;
    }

    private static void SumRange(int start, int end, int workerIndex, object? context)
    {
        CoreScalingBenchmark benchmark = (CoreScalingBenchmark)context!;
        long sum = 0;
        int[] values = benchmark._values;
        for (int i = start; i < end; i++)
        {
            sum += values[i];
        }

        benchmark._partialSums[workerIndex] += sum;
    }
}
