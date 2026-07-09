using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// Box2D task callback 桥测试。
/// 不变式：Box2D 任务桥接在 worker 上完成且不阻塞主相位。
/// </summary>
public sealed unsafe class Box2DTaskBridgeTests
{
    /// <summary>
    /// 验证 task bridge 会把 callbacks、workerCount 与 userTaskContext 注入 worldDef。
    /// </summary>
    [Fact]
    public void ConfigureWorldDefInjectsCallbacksAndContext()
    {
        using JobSystem jobs = new(workerCount: 2);
        using Box2DTaskBridge bridge = new(jobs);
        B2WorldDef worldDef = default;

        bridge.ConfigureWorldDef(ref worldDef);

        Assert.Equal(2, worldDef.WorkerCount);
        Assert.Equal((nint)bridge.UserTaskContext, (nint)worldDef.UserTaskContext);
        Assert.NotEqual(0, (nint)worldDef.EnqueueTask);
        Assert.NotEqual(0, (nint)worldDef.FinishTask);
    }

    /// <summary>
    /// 验证 enqueueTask 通过 JobSystem 同步 fork-join 执行完整区间。
    /// </summary>
    [Fact]
    public void EnqueueTaskDispatchesRangeThroughJobSystem()
    {
        using JobSystem jobs = new(workerCount: 2);
        using Box2DTaskBridge bridge = new(jobs);
        TaskStats stats = new() { MaxWorkerIndex = -1 };

        delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<int, int, uint, void*, void>, int, int, void*, void*, void*> enqueue =
            &Box2DTaskBridge.EnqueueTask;
        delegate* unmanaged[Cdecl]<void*, void*, void> finish = &Box2DTaskBridge.FinishTask;

        void* handle = enqueue(&CountTask, 64, 4, &stats, bridge.UserTaskContext);
        finish(handle, bridge.UserTaskContext);

        Assert.Equal((nint)bridge.UserTaskContext, (nint)handle);
        Assert.Equal(64, stats.TotalItems);
        Assert.True(stats.Calls > 0);
        Assert.Equal(0, stats.OutOfRangeWorker);
        Assert.InRange(stats.MaxWorkerIndex, 0, jobs.WorkerCount - 1);
        Assert.Equal(0, bridge.FaultedCallbackCount);
    }

    /// <summary>
    /// 验证确定性串行模式固定使用 workerIndex 0。
    /// </summary>
    [Fact]
    public void EnqueueTaskUsesWorkerZeroWhenForcedSingleThread()
    {
        using JobSystem jobs = new(workerCount: 2);
        using Box2DTaskBridge bridge = new(jobs, forceSingleThread: true);
        TaskStats stats = new() { MaxWorkerIndex = -1 };

        delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<int, int, uint, void*, void>, int, int, void*, void*, void*> enqueue =
            &Box2DTaskBridge.EnqueueTask;

        void* handle = enqueue(&CountTask, 32, 1, &stats, bridge.UserTaskContext);

        Assert.Equal((nint)bridge.UserTaskContext, (nint)handle);
        Assert.Equal(1, bridge.WorkerCount);
        Assert.Equal(32, stats.TotalItems);
        Assert.Equal(1, stats.Calls);
        Assert.Equal(0, stats.MaxWorkerIndex);
        Assert.Equal(0, stats.OutOfRangeWorker);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CountTask(int start, int end, uint workerIndex, void* context)
    {
        TaskStats* stats = (TaskStats*)context;
        _ = Interlocked.Add(ref stats->TotalItems, end - start);
        _ = Interlocked.Increment(ref stats->Calls);
        if (workerIndex >= 8)
        {
            _ = Interlocked.Increment(ref stats->OutOfRangeWorker);
            return;
        }

        _ = Interlocked.Increment(ref stats->WorkerHits[(int)workerIndex]);

        int worker = (int)workerIndex;
        while (true)
        {
            int current = Volatile.Read(ref stats->MaxWorkerIndex);
            if (worker <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref stats->MaxWorkerIndex, worker, current) == current)
            {
                return;
            }
        }
    }

    private unsafe struct TaskStats
    {
        public int TotalItems;
        public int Calls;
        public int MaxWorkerIndex;
        public int OutOfRangeWorker;
        public fixed int WorkerHits[8];
    }
}
