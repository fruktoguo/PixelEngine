using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PixelEngine.Core.Threading;

namespace PixelEngine.Interop.Box2D;

/// <summary>
/// 将 Box2D v3 task callback 同步派发到 PixelEngine 的持久 JobSystem。
/// </summary>
public sealed unsafe class Box2DTaskBridge : IDisposable
{
    private readonly GCHandle _jobsHandle;
    private readonly BridgeContext* _context;
    private bool _disposed;

    /// <summary>
    /// 创建 Box2D task 桥。
    /// </summary>
    /// <param name="jobs">持久 worker 线程池。</param>
    /// <param name="forceSingleThread">是否强制确定性串行路径。</param>
    public Box2DTaskBridge(JobSystem jobs, bool forceSingleThread = false)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        _jobsHandle = GCHandle.Alloc(jobs, GCHandleType.Normal);
        _context = (BridgeContext*)NativeMemory.AllocZeroed((nuint)sizeof(BridgeContext));
        _context->JobSystemHandle = GCHandle.ToIntPtr(_jobsHandle);
        _context->WorkerCount = forceSingleThread ? 1 : jobs.WorkerCount;
        _context->ForceSingleThread = forceSingleThread ? 1 : 0;
    }

    /// <summary>
    /// 获取传给 Box2D 的 userTaskContext 指针。
    /// </summary>
    public void* UserTaskContext
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _context;
        }
    }

    /// <summary>
    /// 获取注入 Box2D 的 worker 数。
    /// </summary>
    public int WorkerCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _context->WorkerCount;
        }
    }

    /// <summary>
    /// 获取 native 回调中被兜底捕获的异常次数。
    /// </summary>
    public int FaultedCallbackCount => _disposed ? 0 : Volatile.Read(ref _context->FaultedCallbackCount);

    /// <summary>
    /// 将 task callbacks 和 worker 数写入 Box2D world 定义。
    /// </summary>
    /// <param name="worldDef">world 定义。</param>
    public void ConfigureWorldDef(ref B2WorldDef worldDef)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        worldDef.WorkerCount = _context->WorkerCount;
        worldDef.EnqueueTask = &EnqueueTask;
        worldDef.FinishTask = &FinishTask;
        worldDef.UserTaskContext = _context;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeMemory.Free(_context);
        _jobsHandle.Free();
    }

    /// <summary>
    /// Box2D enqueueTask 回调。同步 fork-join 完成后返回哑句柄。
    /// </summary>
    /// <param name="task">Box2D task 函数指针。</param>
    /// <param name="itemCount">任务元素数量。</param>
    /// <param name="minRange">建议最小区间长度。</param>
    /// <param name="taskContext">Box2D task 上下文。</param>
    /// <param name="userContext">桥上下文。</param>
    /// <returns>同步完成后的哑句柄；异常或空任务返回 null。</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static void* EnqueueTask(
        delegate* unmanaged[Cdecl]<int, int, uint, void*, void> task,
        int itemCount,
        int minRange,
        void* taskContext,
        void* userContext)
    {
        BridgeContext* context = (BridgeContext*)userContext;
        if (task is null || itemCount <= 0)
        {
            return null;
        }

        try
        {
            int safeMinRange = Math.Max(1, minRange);
            if (context is null || context->ForceSingleThread != 0 || context->WorkerCount <= 1 || itemCount <= safeMinRange)
            {
                task(0, itemCount, 0, taskContext);
                return userContext;
            }

            JobSystem jobs = GetJobSystem(context);
            TaskInvocation invocation = new(task, taskContext);
            jobs.ParallelRangeRaw(itemCount, safeMinRange, &InvokeTask, &invocation);
            return userContext;
        }
        catch
        {
            if (context is not null)
            {
                _ = Interlocked.Increment(ref context->FaultedCallbackCount);
            }

            return null;
        }
    }

    /// <summary>
    /// Box2D finishTask 回调。同步 fork-join 路径下无需额外等待。
    /// </summary>
    /// <param name="userTask">enqueueTask 返回的哑句柄。</param>
    /// <param name="userContext">桥上下文。</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static void FinishTask(void* userTask, void* userContext)
    {
        _ = userTask;
        _ = userContext;
    }

    [UnmanagedCallersOnly]
    private static void InvokeTask(int start, int end, int workerIndex, void* context)
    {
        TaskInvocation* invocation = (TaskInvocation*)context;
        invocation->Task(start, end, (uint)workerIndex, invocation->TaskContext);
    }

    private static JobSystem GetJobSystem(BridgeContext* context)
    {
        GCHandle handle = GCHandle.FromIntPtr(context->JobSystemHandle);
        return handle.Target as JobSystem
            ?? throw new ObjectDisposedException(nameof(JobSystem), "Box2D task bridge 的 JobSystem 已不可用。");
    }

    /// <summary>
    /// 传给 Box2D 的 native 上下文。只含 unmanaged 字段，可由 native 长期持有。
    /// </summary>
    public struct BridgeContext
    {
        /// <summary>JobSystem 的 GCHandle。</summary>
        public IntPtr JobSystemHandle;

        /// <summary>Box2D 可用 worker 数。</summary>
        public int WorkerCount;

        /// <summary>非 0 表示强制串行。</summary>
        public int ForceSingleThread;

        /// <summary>native 回调兜底捕获的异常数。</summary>
        public int FaultedCallbackCount;
    }

    private readonly struct TaskInvocation(
        delegate* unmanaged[Cdecl]<int, int, uint, void*, void> task,
        void* taskContext)
    {
        public readonly delegate* unmanaged[Cdecl]<int, int, uint, void*, void> Task = task;
        public readonly void* TaskContext = taskContext;
    }
}
