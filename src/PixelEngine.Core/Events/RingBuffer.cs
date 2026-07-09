using System.Numerics;

namespace PixelEngine.Core.Events;

/// <summary>
/// 表示单生产者单消费者的无锁有界 ring buffer。
/// </summary>
/// <typeparam name="T">非托管事件元素类型。</typeparam>
public sealed class RingBuffer<T>
    where T : unmanaged
{
    private readonly T[] _items;
    private readonly int _mask;
    private int _head;
    private int _tail;

    /// <summary>
    /// 创建指定容量的 SPSC ring buffer。
    /// </summary>
    /// <param name="capacityPow2">容量，必须是正的 2 的幂。</param>
    public RingBuffer(int capacityPow2)
    {
        if (capacityPow2 <= 0 || !BitOperations.IsPow2(capacityPow2))
        {
            throw new ArgumentOutOfRangeException(nameof(capacityPow2), capacityPow2, "容量必须是正的 2 的幂。");
        }

        _items = new T[capacityPow2];
        _mask = capacityPow2 - 1;
    }

    /// <summary>
    /// 获取当前元素数量。
    /// </summary>
    public int Count => Volatile.Read(ref _tail) - Volatile.Read(ref _head);

    /// <summary>
    /// 尝试入队一个元素。
    /// </summary>
    /// <param name="item">待入队元素。</param>
    /// <returns>若入队成功则为 <see langword="true"/>；队列满时为 <see langword="false"/>。</returns>
    // SPSC 无锁入队：单生产者写 tail，消费者通过 Volatile.Read(head) 判断容量。
    public bool TryEnqueue(in T item)
    {
        int tail = _tail;
        int head = Volatile.Read(ref _head);
        if (tail - head >= _items.Length)
        {
            return false;
        }

        _items[tail & _mask] = item;
        Volatile.Write(ref _tail, tail + 1);
        return true;
    }

    /// <summary>
    /// 尝试出队一个元素。
    /// </summary>
    /// <param name="item">出队元素。</param>
    /// <returns>若出队成功则为 <see langword="true"/>；队列空时为 <see langword="false"/>。</returns>
    public bool TryDequeue(out T item)
    {
        int head = _head;
        int tail = Volatile.Read(ref _tail);
        if (head == tail)
        {
            item = default;
            return false;
        }

        int index = head & _mask;
        item = _items[index];
        _items[index] = default;
        Volatile.Write(ref _head, head + 1);
        return true;
    }

    /// <summary>
    /// 将可用元素按顺序排入目标缓冲。
    /// </summary>
    /// <param name="destination">目标缓冲。</param>
    /// <returns>实际写入元素数量。</returns>
    public int DrainTo(Span<T> destination)
    {
        int count = 0;
        while (count < destination.Length && TryDequeue(out T item))
        {
            destination[count++] = item;
        }

        return count;
    }
}
