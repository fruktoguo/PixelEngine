using System.Numerics;
using System.Runtime.InteropServices;

namespace PixelEngine.Core.Events;

/// <summary>
/// 表示多生产者单消费者的无锁有界 ring buffer。
/// </summary>
/// <typeparam name="T">非托管事件元素类型。</typeparam>
public sealed class MpscRingBuffer<T>
    where T : unmanaged
{
    private readonly Slot[] _slots;
    private readonly int _mask;
    private long _head;
    private long _tail;

    /// <summary>
    /// 创建指定容量的 MPSC ring buffer。
    /// </summary>
    /// <param name="capacityPow2">容量，必须是正的 2 的幂。</param>
    public MpscRingBuffer(int capacityPow2)
    {
        if (capacityPow2 <= 0 || !BitOperations.IsPow2(capacityPow2))
        {
            throw new ArgumentOutOfRangeException(nameof(capacityPow2), capacityPow2, "容量必须是正的 2 的幂。");
        }

        _slots = new Slot[capacityPow2];
        _mask = capacityPow2 - 1;
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new()
            {
                Sequence = i,
            };
        }
    }

    /// <summary>
    /// 获取当前元素数量的近似快照。
    /// </summary>
    public int Count
    {
        get
        {
            long tail = Volatile.Read(ref _tail);
            long head = Volatile.Read(ref _head);
            long count = tail - head;
            return count <= 0 ? 0 : (int)Math.Min(count, _slots.Length);
        }
    }

    /// <summary>
    /// 尝试入队一个元素。
    /// </summary>
    /// <param name="item">待入队元素。</param>
    /// <returns>若入队成功则为 <see langword="true"/>；队列满时为 <see langword="false"/>。</returns>
    // 基于 per-slot sequence 的 MPSC 入队：CAS 抢占 tail 后写入元素并发布 sequence。
    public bool TryEnqueue(in T item)
    {
        while (true)
        {
            long tail = Volatile.Read(ref _tail);
            Slot slot = _slots[tail & _mask];
            long sequence = Volatile.Read(ref slot.Sequence);
            long difference = sequence - tail;

            if (difference == 0)
            {
                if (Interlocked.CompareExchange(ref _tail, tail + 1, tail) == tail)
                {
                    slot.Item = item;
                    Volatile.Write(ref slot.Sequence, tail + 1);
                    return true;
                }
            }
            else if (difference < 0)
            {
                return false;
            }
            else
            {
                _ = Thread.Yield();
            }
        }
    }

    /// <summary>
    /// 将可用元素按顺序排入目标缓冲。
    /// </summary>
    /// <param name="destination">目标缓冲。</param>
    /// <returns>实际写入元素数量。</returns>
    public int DrainTo(Span<T> destination)
    {
        int drained = 0;
        while (drained < destination.Length && TryDequeue(out T item))
        {
            destination[drained++] = item;
        }

        return drained;
    }

    private bool TryDequeue(out T item)
    {
        long head = _head;
        Slot slot = _slots[head & _mask];
        long sequence = Volatile.Read(ref slot.Sequence);
        long difference = sequence - (head + 1);

        if (difference == 0)
        {
            item = slot.Item;
            slot.Item = default;
            Volatile.Write(ref _head, head + 1);
            Volatile.Write(ref slot.Sequence, head + _slots.Length);
            return true;
        }

        item = default;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class Slot
    {
        public long Sequence;
        public T Item;
    }
}
