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

        JobHandle handle = ScheduleRawRangeBatch(itemCount, minRange, body, context);
        Wait(in handle);
    }

    private JobHandle ScheduleRawRangeBatch(
        int itemCount,
        int minRange,
        delegate* unmanaged<int, int, int, void*, void> body,
        void* context)
    {
        EnterDispatch();
        try
        {
            _rawRangeBatch.Configure(itemCount, minRange, body, context);
            return PublishBatch(_rawRangeBatch);
        }
        catch
        {
            _ = Interlocked.Exchange(ref _activeDispatch, 0);
            throw;
        }
    }

    private sealed class RawRangeBatch(int workerCount) : WorkBatch(workerCount, disposeAfterWait: false)
    {
        private delegate* unmanaged<int, int, int, void*, void> _body;
        private void* _context;

        public void Configure(
            int itemCount,
            int minRange,
            delegate* unmanaged<int, int, int, void*, void> body,
            void* context)
        {
            _body = body;
            _context = context;
            Reset(itemCount, minRange);
        }

        protected override void ExecuteRange(int start, int end, int workerIndex)
        {
            _body(start, end, workerIndex, _context);
        }
    }
}
