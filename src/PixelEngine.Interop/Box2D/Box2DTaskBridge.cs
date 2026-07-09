using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using PixelEngine.Core.Threading;

namespace PixelEngine.Interop.Box2D;

/// <summary>
/// 将 Box2D v3 task callback 同步派发到 PixelEngine 的持久 JobSystem。
/// </summary>
public sealed unsafe class Box2DTaskBridge : IDisposable
{
    private readonly GCHandle _jobsHandle;
    private readonly GCHandle _faultStateHandle;
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
        _faultStateHandle = GCHandle.Alloc(new BridgeFaultState(), GCHandleType.Normal);
        _context = (BridgeContext*)NativeMemory.AllocZeroed((nuint)sizeof(BridgeContext));
        _context->JobSystemHandle = GCHandle.ToIntPtr(_jobsHandle);
        _context->FaultStateHandle = GCHandle.ToIntPtr(_faultStateHandle);
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
    /// 获取当前 physics tick 是否捕获过 callback 异常。
    /// </summary>
    public bool HasFaulted
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetFaultState().HasFaulted;
        }
    }

    /// <summary>
    /// 开始一个新的 physics tick，并清除上一个已处理 tick 的首异常状态。
    /// </summary>
    public void BeginTick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GetFaultState().Reset();
    }

    /// <summary>
    /// 将当前 tick 的首个 callback 异常重新抛回托管 physics 编排器。
    /// </summary>
    /// <exception cref="Exception">当前 tick 捕获的首个 callback 异常。</exception>
    public void ThrowIfFaulted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GetFaultState().ThrowIfFaulted();
    }

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
        _faultStateHandle.Free();
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
        if (context is null || task is null || itemCount <= 0)
        {
            return null;
        }

        try
        {
            int safeMinRange = Math.Max(1, minRange);
            // 单线程或任务过小则同步执行；否则经 JobSystem.ParallelRangeRaw fork-join。
            if (context is null || context->ForceSingleThread != 0 || context->WorkerCount <= 1 || itemCount <= safeMinRange)
            {
                task(0, itemCount, 0, taskContext);
                return userContext;
            }

            JobSystem jobs = GetJobSystem(context);
            TaskInvocation invocation = new(task, taskContext, context);
            jobs.ParallelRangeRaw(itemCount, safeMinRange, &InvokeTask, &invocation);
            return userContext;
        }
        catch (Exception exception)
        {
            RecordCallbackException(context, exception);
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
        try
        {
            invocation->Task(start, end, (uint)workerIndex, invocation->TaskContext);
        }
        catch (Exception exception)
        {
            // 异常绝不能跨过 unmanaged callback 边界；先记录，待 b2World_Step 返回后由 PhysicsSystem rethrow。
            RecordCallbackException(invocation->Bridge, exception);
        }
    }

    private static void RecordCallbackException(BridgeContext* context, Exception exception)
    {
        if (context is null)
        {
            return;
        }

        BridgeFaultState state = GetFaultState(context);
        state.CaptureFirst(exception);
        _ = Interlocked.Increment(ref context->FaultedCallbackCount);
    }

    private BridgeFaultState GetFaultState()
    {
        return GetFaultState(_context);
    }

    private static BridgeFaultState GetFaultState(BridgeContext* context)
    {
        GCHandle handle = GCHandle.FromIntPtr(context->FaultStateHandle);
        return handle.Target as BridgeFaultState
            ?? throw new InvalidOperationException("Box2D task bridge 缺少 fault state。");
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

        /// <summary>托管首异常状态的 GCHandle。</summary>
        public IntPtr FaultStateHandle;

        /// <summary>Box2D 可用 worker 数。</summary>
        public int WorkerCount;

        /// <summary>非 0 表示强制串行。</summary>
        public int ForceSingleThread;

        /// <summary>native 回调兜底捕获的异常数。</summary>
        public int FaultedCallbackCount;
    }

    private readonly struct TaskInvocation(
        delegate* unmanaged[Cdecl]<int, int, uint, void*, void> task,
        void* taskContext,
        BridgeContext* bridge)
    {
        public readonly delegate* unmanaged[Cdecl]<int, int, uint, void*, void> Task = task;
        public readonly void* TaskContext = taskContext;
        public readonly BridgeContext* Bridge = bridge;
    }

    private sealed class BridgeFaultState
    {
        private ExceptionDispatchInfo? _firstException;
        private int _faulted;

        public bool HasFaulted => Volatile.Read(ref _faulted) != 0;

        public void Reset()
        {
            _ = Interlocked.Exchange(ref _firstException, null);
            Volatile.Write(ref _faulted, 0);
        }

        public void CaptureFirst(Exception exception)
        {
            _ = Interlocked.CompareExchange(ref _firstException, ExceptionDispatchInfo.Capture(exception), null);
            Volatile.Write(ref _faulted, 1);
        }

        public void ThrowIfFaulted()
        {
            if (Volatile.Read(ref _faulted) == 0)
            {
                return;
            }

            (Volatile.Read(ref _firstException) ??
                ExceptionDispatchInfo.Capture(new InvalidOperationException("Box2D task callback 失败，但未记录原始异常。"))).Throw();
        }
    }
}
