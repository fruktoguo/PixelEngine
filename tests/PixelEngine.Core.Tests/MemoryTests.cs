using PixelEngine.Core.Memory;
using Xunit;

namespace PixelEngine.Core.Tests;

/// <summary>
/// Core 内存基础设施测试。
/// </summary>
public sealed unsafe class MemoryTests
{
    /// <summary>
    /// 验证 pinned 缓冲指针在 GC 压力下保持稳定。
    /// </summary>
    [Fact]
    public void PinnedBufferPointerStaysStableAcrossGc()
    {
        using PinnedBuffer<int> buffer = new(16);
        int* before = buffer.Pointer;
        buffer[0] = 42;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal((nint)before, (nint)buffer.Pointer);
        Assert.Equal(42, buffer[0]);
    }

    /// <summary>
    /// 验证 NativeBuffer 可读写、清零和释放。
    /// </summary>
    [Fact]
    public void NativeBufferCanBeWrittenClearedAndDisposed()
    {
        using NativeBuffer<int> buffer = new(8);

        buffer[3] = 99;
        Assert.Equal(99, buffer.Span[3]);

        buffer.Clear();
        Assert.All(buffer.Span.ToArray(), value => Assert.Equal(0, value));
    }

    /// <summary>
    /// 验证对象池和租借数组稳态循环不产生托管分配。
    /// </summary>
    [Fact]
    public void PoolAndRentedArraySteadyStateDoNotAllocate()
    {
        Pool<object> pool = new(static () => new object(), preallocate: 1);
        object warm = pool.Rent();
        pool.Return(warm);
        using (RentedArray<int> rented = RentedArray<int>.Rent(16))
        {
            Assert.True(rented.Array.Length >= rented.Length);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000_000; i++)
        {
            object item = pool.Rent();
            pool.Return(item);
        }

        for (int i = 0; i < 1_000_000; i++)
        {
            using RentedArray<int> rented = RentedArray<int>.Rent(16);
            rented.Span[0] = i;
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(before, after);
    }

    /// <summary>
    /// 验证 DoubleBuffer 只交换引用。
    /// </summary>
    [Fact]
    public void DoubleBufferSwapOnlyExchangesReferences()
    {
        int created = 0;
        DoubleBuffer<object> buffer = new(() =>
        {
            created++;
            return new object();
        });

        object front = buffer.Front;
        object back = buffer.Back;
        buffer.Swap();

        Assert.Equal(2, created);
        Assert.Same(back, buffer.Front);
        Assert.Same(front, buffer.Back);
    }

    /// <summary>
    /// 验证 SoaBuffer 扩容后所有列同步扩容并保留旧数据。
    /// </summary>
    [Fact]
    public void SoaBufferEnsureCapacityPreservesColumns()
    {
        using TestSoaBuffer buffer = new();
        buffer.EnsureCapacity(2);
        buffer.Ints.Span[0] = 7;
        buffer.Floats.Span[0] = 1.5f;

        buffer.EnsureCapacity(100);

        Assert.True(buffer.Capacity >= 100);
        Assert.Equal(buffer.Ints.Capacity, buffer.Floats.Capacity);
        Assert.Equal(7, buffer.Ints.Span[0]);
        Assert.Equal(1.5f, buffer.Floats.Span[0]);
    }

    private sealed class TestSoaBuffer : SoaBuffer
    {
        public TestSoaBuffer()
        {
            Ints = DefineColumn<int>(MemoryKind.Poh);
            Floats = DefineColumn<float>(MemoryKind.Native);
        }

        public SoaColumn<int> Ints { get; }

        public SoaColumn<float> Floats { get; }
    }
}
