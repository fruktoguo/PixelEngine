using System.Runtime.InteropServices;
using System.Reflection;
using PixelEngine.Core.Threading;
using Xunit;

namespace PixelEngine.Core.Tests;

/// <summary>
/// Core JobSystem 测试。
/// </summary>
public sealed unsafe class JobSystemTests
{
    private static readonly RangeJob CountRangeJob = CountRange;

    /// <summary>
    /// 验证 ParallelRange workerIndex 范围稳定且全部任务执行一次。
    /// </summary>
    [Fact]
    public void ParallelRangeUsesWorkerIndicesWithinConfiguredRange()
    {
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };

        CounterContext context = new();
        jobs.ParallelRange(128, 1, static (start, end, workerIndex, context) =>
        {
            Assert.InRange(workerIndex, 0, 1);
            _ = Interlocked.Add(ref UnsafeContext(context).Total, end - start);
            _ = Interlocked.Or(ref UnsafeContext(context).SeenMask, 1 << workerIndex);
        }, context);

        Assert.Equal(128, context.Total);
        Assert.NotEqual(0, context.SeenMask);

        static CounterContext UnsafeContext(object? context)
        {
            return Assert.IsType<CounterContext>(context);
        }
    }

    /// <summary>
    /// 验证 ParallelFor 可处理 unmanaged state 列表。
    /// </summary>
    [Fact]
    public void ParallelForProcessesAllItems()
    {
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        int[] items = [.. Enumerable.Range(1, 64)];
        int sum = 0;

        jobs.ParallelFor(items, AddItem);

        Assert.Equal(items.Sum(), sum);

        void AddItem(in int item, int workerIndex)
        {
            Assert.InRange(workerIndex, 0, 1);
            _ = Interlocked.Add(ref sum, item);
        }
    }

    /// <summary>
    /// 验证 ParallelRangeRaw 可调用 unmanaged 函数指针。
    /// </summary>
    [Fact]
    public void ParallelRangeRawInvokesUnmanagedCallback()
    {
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        int total = 0;

        jobs.ParallelRangeRaw(64, 1, &RawCallback, &total);

        Assert.Equal(64, total);
    }

    /// <summary>
    /// 验证小任务低于阈值时回退到 workerIndex 0 单线程执行。
    /// </summary>
    [Fact]
    public void ParallelRangeFallsBackToSingleThreadBelowThreshold()
    {
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 10,
        };
        CounterContext context = new();

        jobs.ParallelRange(5, 1, static (start, end, workerIndex, context) =>
        {
            Assert.Equal(0, workerIndex);
            _ = Interlocked.Add(ref Assert.IsType<CounterContext>(context).Total, end - start);
        }, context);

        Assert.Equal(5, context.Total);
    }

    /// <summary>
    /// 验证多 worker ParallelRange 稳态派发不再为 RangeBatch / ManualResetEventSlim 分配托管对象。
    /// </summary>
    [Fact]
    public void ParallelRangeMultiWorkerDispatchDoesNotAllocate()
    {
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        CounterContext context = new();
        jobs.ParallelRange(128, 1, CountRangeJob, context);
        context.Total = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();

        jobs.ParallelRange(128, 1, CountRangeJob, context);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(128, context.Total);
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// 验证 Box2D task bridge 使用的 raw range 稳态派发同样不分配托管对象。
    /// </summary>
    [Fact]
    public void ParallelRangeRawMultiWorkerDispatchDoesNotAllocate()
    {
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        int total = 0;
        jobs.ParallelRangeRaw(128, 1, &RawCallback, &total);
        total = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();

        jobs.ParallelRangeRaw(128, 1, &RawCallback, &total);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(128, total);
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// 验证 WorkerLocal 内部槽位包含 64 字节 cache-line padding。
    /// </summary>
    [Fact]
    public void WorkerLocalSlotDefinesCacheLinePadding()
    {
        Type? slotType = typeof(WorkerLocal<object>).GetNestedType("PaddedSlot", BindingFlags.NonPublic);
        Assert.NotNull(slotType);

        int longPaddingFields = slotType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Count(field => field.FieldType == typeof(long));

        Assert.True(longPaddingFields * sizeof(long) >= 56);
    }

    private sealed class CounterContext
    {
        public int Total;

        public int SeenMask;
    }

    private static void CountRange(int start, int end, int workerIndex, object? context)
    {
        _ = workerIndex;
        _ = Interlocked.Add(ref Assert.IsType<CounterContext>(context).Total, end - start);
    }

    [UnmanagedCallersOnly]
    private static void RawCallback(int start, int end, int workerIndex, void* context)
    {
        ref int total = ref *(int*)context;
        _ = Interlocked.Add(ref total, end - start);
    }
}
