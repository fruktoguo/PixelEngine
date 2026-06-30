namespace PixelEngine.Core.Threading;

public sealed unsafe partial class JobSystem
{
    /// <summary>
    /// 将原生函数指针区间任务派发到持久 worker，并阻塞到全部完成。
    /// </summary>
    /// <param name="itemCount">总元素数量。</param>
    /// <param name="minRange">每个子区间的最小元素数量。</param>
    /// <param name="body">原生函数指针任务。</param>
    /// <param name="context">原生上下文指针。</param>
    public void ParallelRangeRaw(
        int itemCount,
        int minRange,
        delegate* unmanaged<int, int, int, void*, void> body,
        void* context)
    {
        ValidateRangeArguments(itemCount, minRange);
        if (body is null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        ThrowIfDisposed();

        if (itemCount == 0)
        {
            return;
        }

        if (ShouldRunSingleThread(itemCount, minRange))
        {
            body(0, itemCount, 0, context);
            return;
        }

        JobHandle handle = ScheduleBatch(new RawRangeBatch(itemCount, minRange, body, context, WorkerCount));
        Wait(in handle);
    }

    private sealed class RawRangeBatch(
        int itemCount,
        int minRange,
        delegate* unmanaged<int, int, int, void*, void> body,
        void* context,
        int workerCount) : WorkBatch(itemCount, minRange, workerCount)
    {
        private readonly delegate* unmanaged<int, int, int, void*, void> _body = body;
        private readonly void* _context = context;

        protected override void ExecuteRange(int start, int end, int workerIndex)
        {
            _body(start, end, workerIndex, _context);
        }
    }
}
