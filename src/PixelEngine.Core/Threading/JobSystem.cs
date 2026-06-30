using System.Runtime.ExceptionServices;

namespace PixelEngine.Core.Threading;

/// <summary>
/// 表示区间并行任务委托。
/// </summary>
/// <param name="start">起始索引，包含。</param>
/// <param name="end">结束索引，不包含。</param>
/// <param name="workerIndex">稳定 worker 索引。</param>
/// <param name="context">调用方上下文。</param>
public delegate void RangeJob(int start, int end, int workerIndex, object? context);

/// <summary>
/// 表示 chunk 或其它值类型任务委托。
/// </summary>
/// <typeparam name="TState">任务状态类型。</typeparam>
/// <param name="item">任务状态。</param>
/// <param name="workerIndex">稳定 worker 索引。</param>
public delegate void ChunkJob<TState>(in TState item, int workerIndex)
    where TState : struct;

/// <summary>
/// 表示一次已派发并行任务的等待句柄。
/// </summary>
public readonly struct JobHandle
{
    internal JobHandle(JobSystem.WorkBatch? batch)
    {
        Batch = batch;
    }

    internal JobSystem.WorkBatch? Batch { get; }

    /// <summary>
    /// 获取任务是否已经完成。
    /// </summary>
    public bool IsCompleted => Batch is null || Batch.IsCompleted;
}

/// <summary>
/// 提供持久 worker 线程池与 fork-join barrier 调度。
/// </summary>
/// <remarks>
/// 架构 §5.7/§12.7 要求 CA checkerboard 与 Box2D task bridge 共用持久 worker，
/// 避免 60fps 细粒度任务反复创建 <c>Parallel.For</c> 分区和委托；每个 fork-join 调用即一次 barrier。
/// workerIndex 在 worker 生命周期内稳定，对架构 §14.2 的 Box2D 回调桥和 per-worker false-sharing 填充槽位都是硬约束。
/// </remarks>
public sealed unsafe partial class JobSystem : IDisposable
{
    private readonly object _gate = new();
    private readonly Thread[] _workers;
    private WorkBatch? _currentBatch;
    private long _batchVersion;
    private int _activeDispatch;
    private bool _disposed;

    /// <summary>
    /// 创建持久 worker 线程池。
    /// </summary>
    /// <param name="workerCount">worker 数；0 表示使用 <see cref="Environment.ProcessorCount"/>。</param>
    public JobSystem(int workerCount = 0)
    {
        if (workerCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCount), workerCount, "worker 数不能为负。");
        }

        WorkerCount = workerCount == 0 ? Math.Max(1, Environment.ProcessorCount) : workerCount;
        SingleThreadThreshold = 1;
        _workers = new Thread[WorkerCount];

        for (int i = 0; i < _workers.Length; i++)
        {
            int workerIndex = i;
            Thread worker = new(() => WorkerLoop(workerIndex))
            {
                IsBackground = true,
                Name = $"PixelEngine Job Worker {workerIndex}",
            };

            _workers[i] = worker;
            worker.Start();
        }
    }

    /// <summary>
    /// 获取持久 worker 数量。
    /// </summary>
    public int WorkerCount { get; }

    /// <summary>
    /// 获取或设置小任务单线程回退阈值。
    /// </summary>
    public int SingleThreadThreshold { get; set; }

    /// <summary>
    /// 将区间任务派发到持久 worker，并阻塞到全部完成。
    /// </summary>
    /// <param name="itemCount">总元素数量。</param>
    /// <param name="minRange">每个子区间的最小元素数量。</param>
    /// <param name="body">区间任务。</param>
    /// <param name="context">调用方上下文。</param>
    public void ParallelRange(int itemCount, int minRange, RangeJob body, object? context = null)
    {
        JobHandle handle = Schedule(itemCount, minRange, body, context);
        Wait(in handle);
    }

    /// <summary>
    /// 将值类型任务列表派发到持久 worker，并阻塞到全部完成。
    /// </summary>
    /// <typeparam name="TState">任务状态类型。</typeparam>
    /// <param name="items">任务状态列表。</param>
    /// <param name="body">任务委托。</param>
    public void ParallelFor<TState>(ReadOnlySpan<TState> items, ChunkJob<TState> body)
        where TState : unmanaged
    {
        ArgumentNullException.ThrowIfNull(body);
        ThrowIfDisposed();

        if (items.IsEmpty)
        {
            return;
        }

        if (ShouldRunSingleThread(items.Length, 1))
        {
            for (int i = 0; i < items.Length; i++)
            {
                body(in items[i], 0);
            }

            return;
        }

        // ReadOnlySpan<T> 不能存到堆对象。这里仅允许 unmanaged TState，
        // 在 fixed 作用域内把稳定指针交给本次同步 fork-join；方法返回前所有 worker 已完成。
        fixed (TState* pointer = items)
        {
            SpanBatch<TState> batch = new(pointer, items.Length, body, WorkerCount);
            JobHandle handle = ScheduleBatch(batch);
            Wait(in handle);
        }
    }

    /// <summary>
    /// 派发区间任务并返回等待句柄。
    /// </summary>
    /// <param name="itemCount">总元素数量。</param>
    /// <param name="minRange">每个子区间的最小元素数量。</param>
    /// <param name="body">区间任务。</param>
    /// <param name="context">调用方上下文。</param>
    /// <returns>等待句柄。</returns>
    public JobHandle Schedule(int itemCount, int minRange, RangeJob body, object? context = null)
    {
        ValidateRangeArguments(itemCount, minRange);
        ArgumentNullException.ThrowIfNull(body);
        ThrowIfDisposed();

        if (itemCount == 0)
        {
            return default;
        }

        if (ShouldRunSingleThread(itemCount, minRange))
        {
            body(0, itemCount, 0, context);
            return default;
        }

        return ScheduleBatch(new RangeBatch(itemCount, minRange, body, context, WorkerCount));
    }

    /// <summary>
    /// 等待已派发任务完成，并转发 worker 中捕获的异常。
    /// </summary>
    /// <param name="handle">任务句柄。</param>
    public void Wait(in JobHandle handle)
    {
        WorkBatch? batch = handle.Batch;
        if (batch is null)
        {
            return;
        }

        try
        {
            batch.Wait();
            batch.ThrowIfFaulted();
        }
        finally
        {
            batch.Dispose();
            if (ReferenceEquals(_currentBatch, batch))
            {
                _currentBatch = null;
            }

            _ = Interlocked.Exchange(ref _activeDispatch, 0);
        }
    }

    /// <summary>
    /// 释放持久 worker 线程。
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _batchVersion++;
            Monitor.PulseAll(_gate);
        }

        foreach (Thread worker in _workers)
        {
            worker.Join();
        }
    }

    private JobHandle ScheduleBatch(WorkBatch batch)
    {
        ThrowIfDisposed();

        if (Interlocked.CompareExchange(ref _activeDispatch, 1, 0) != 0)
        {
            batch.Dispose();
            throw new InvalidOperationException("JobSystem 当前已有未 Wait 的任务；不支持并发派发或重入派发。");
        }

        try
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _currentBatch = batch;
                _batchVersion++;
                Monitor.PulseAll(_gate);
            }
        }
        catch
        {
            batch.Dispose();
            _ = Interlocked.Exchange(ref _activeDispatch, 0);
            throw;
        }

        return new JobHandle(batch);
    }

    private bool ShouldRunSingleThread(int itemCount, int minRange)
    {
        return WorkerCount <= 1
            || itemCount <= Math.Max(1, SingleThreadThreshold)
            || itemCount <= minRange;
    }

    private static void ValidateRangeArguments(int itemCount, int minRange)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minRange);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void WorkerLoop(int workerIndex)
    {
        long seenVersion = 0;

        while (true)
        {
            WorkBatch? batch;
            lock (_gate)
            {
                while (!_disposed && _batchVersion == seenVersion)
                {
                    _ = Monitor.Wait(_gate);
                }

                if (_disposed)
                {
                    return;
                }

                seenVersion = _batchVersion;
                batch = _currentBatch;
            }

            batch?.Execute(workerIndex);
        }
    }

    internal abstract class WorkBatch(int itemCount, int minRange, int workerCount) : IDisposable
    {
        private readonly ManualResetEventSlim _completed = new(false);
        private readonly int _rangeSize = Math.Max(1, minRange);
        private readonly int _itemCount = itemCount;
        private int _nextStart;
        private int _remainingWorkers = workerCount;
        private int _completedFlag;
        private int _faulted;
        private ExceptionDispatchInfo? _exception;

        public bool IsCompleted => Volatile.Read(ref _completedFlag) != 0;

        public void Execute(int workerIndex)
        {
            try
            {
                while (Volatile.Read(ref _faulted) == 0)
                {
                    int start = Interlocked.Add(ref _nextStart, _rangeSize) - _rangeSize;
                    if (start >= _itemCount)
                    {
                        break;
                    }

                    int end = Math.Min(start + _rangeSize, _itemCount);
                    ExecuteRange(start, end, workerIndex);
                }
            }
            catch (Exception ex)
            {
                CaptureException(ex);
            }
            finally
            {
                if (Interlocked.Decrement(ref _remainingWorkers) == 0)
                {
                    Volatile.Write(ref _completedFlag, 1);
                    _completed.Set();
                }
            }
        }

        public void Wait()
        {
            _completed.Wait();
        }

        public void ThrowIfFaulted()
        {
            _exception?.Throw();
        }

        public void Dispose()
        {
            _completed.Dispose();
        }

        protected abstract void ExecuteRange(int start, int end, int workerIndex);

        private void CaptureException(Exception exception)
        {
            _ = Interlocked.Exchange(ref _faulted, 1);
            ExceptionDispatchInfo info = ExceptionDispatchInfo.Capture(exception);
            _ = Interlocked.CompareExchange(ref _exception, info, null);
        }
    }

    private sealed class RangeBatch(int itemCount, int minRange, RangeJob body, object? context, int workerCount) : WorkBatch(itemCount, minRange, workerCount)
    {
        private readonly RangeJob _body = body;
        private readonly object? _context = context;

        protected override void ExecuteRange(int start, int end, int workerIndex)
        {
            _body(start, end, workerIndex, _context);
        }
    }

    private sealed class SpanBatch<TState>(TState* items, int itemCount, ChunkJob<TState> body, int workerCount) : WorkBatch(itemCount, 1, workerCount)
        where TState : unmanaged
    {
        private readonly TState* _items = items;
        private readonly ChunkJob<TState> _body = body;

        protected override void ExecuteRange(int start, int end, int workerIndex)
        {
            for (int i = start; i < end; i++)
            {
                _body(in _items[i], workerIndex);
            }
        }
    }
}
