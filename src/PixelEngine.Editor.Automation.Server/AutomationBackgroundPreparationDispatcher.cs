using System.Collections.Concurrent;
using System.Diagnostics;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// 为 automation preparation 提供有界常驻 worker；空闲时阻塞等待，不创建临时并行任务。
/// </summary>
internal sealed class AutomationBackgroundPreparationDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _queue;
    private readonly Thread[] _workers;
    private int _disposed;

    internal AutomationBackgroundPreparationDispatcher(int workerCount, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _queue = new BlockingCollection<Action>(
            new ConcurrentQueue<Action>(),
            capacity);
        _workers = new Thread[workerCount];
        for (int i = 0; i < _workers.Length; i++)
        {
            Thread worker = new(
                static state => ((AutomationBackgroundPreparationDispatcher)state!).RunWorker())
            {
                IsBackground = true,
                Name = $"PixelEngine Automation Preparation {i}",
            };
            _workers[i] = worker;
            worker.Start(this);
        }
    }

    internal bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        try
        {
            return _queue.TryAdd(action);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal void Dispose(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.CompleteAdding();
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < _workers.Length; i++)
        {
            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero || !_workers[i].Join(remaining))
            {
                throw new TimeoutException(
                    $"Automation preparation worker 未能在 {timeout.TotalMilliseconds:F0} ms 内停止。");
            }
        }

        _queue.Dispose();
    }

    public void Dispose()
    {
        Dispose(TimeSpan.FromSeconds(10));
    }

    private void RunWorker()
    {
        foreach (Action action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }
}
