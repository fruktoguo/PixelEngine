using System.Collections;
using System.Runtime.InteropServices;

namespace PixelEngine.Core.Threading;

/// <summary>
/// 为 <see cref="JobSystem"/> 的每个 worker 提供一个独立对象槽位。
/// </summary>
/// <typeparam name="T">每个 worker 持有的引用类型。</typeparam>
/// <remarks>
/// 本类型用于 worker 私有累加器、队列或对象池等场景，调用方应使用稳定的 workerIndex 访问对应槽位。
/// </remarks>
public sealed class WorkerLocal<T>
    where T : class
{
    private readonly PaddedSlot[] _slots;
    private readonly SlotList _slotList;

    /// <summary>
    /// 创建每 worker 独立对象槽位。
    /// </summary>
    /// <param name="jobs">提供 worker 数量的作业系统。</param>
    /// <param name="factory">按 workerIndex 创建槽位对象的工厂函数。</param>
    /// <exception cref="ArgumentNullException"><paramref name="jobs"/> 或 <paramref name="factory"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="jobs"/> 的 worker 数量不是正数。</exception>
    /// <exception cref="InvalidOperationException"><paramref name="factory"/> 返回 <see langword="null"/>。</exception>
    public WorkerLocal(JobSystem jobs, Func<int, T> factory)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(factory);

        int workerCount = jobs.WorkerCount;
        if (workerCount <= 0)
        {
            throw new ArgumentException("JobSystem.WorkerCount 必须大于 0。", nameof(jobs));
        }

        _slots = new PaddedSlot[workerCount];
        for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            T value = factory(workerIndex)
                ?? throw new InvalidOperationException($"WorkerLocal 工厂函数为 workerIndex {workerIndex} 返回了 null。");
            _slots[workerIndex].Value = value;
        }

        _slotList = new SlotList(this);
    }

    /// <summary>
    /// 获取指定 worker 的本地对象。
    /// </summary>
    /// <param name="workerIndex">worker 的稳定索引，必须位于 <c>[0, Slots.Count)</c>。</param>
    /// <returns>指定 worker 的本地对象。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="workerIndex"/> 不在有效范围内。</exception>
    public T this[int workerIndex] => (uint)workerIndex >= (uint)_slots.Length
                ? throw new ArgumentOutOfRangeException(
                    nameof(workerIndex),
                    workerIndex,
                    $"workerIndex 必须位于 [0, {_slots.Length}) 范围内。")
                : _slots[workerIndex].Value!;

    /// <summary>
    /// 获取所有 worker 本地对象的只读列表。
    /// </summary>
    public IReadOnlyList<T> Slots => _slotList;

    // 每个槽位占用至少 64 字节，使相邻 worker 的 Value 引用落在不同 cache line 上，
    // 避免 fork-join 热路径里更新 worker 私有引用时产生 false sharing；此处不依赖尚未定稿的 EngineConstants。
    [StructLayout(LayoutKind.Sequential)]
    private struct PaddedSlot
    {
        public T? Value;
        private readonly long _padding0;
        private readonly long _padding1;
        private readonly long _padding2;
        private readonly long _padding3;
        private readonly long _padding4;
        private readonly long _padding5;
        private readonly long _padding6;
    }

    private sealed class SlotList(WorkerLocal<T> owner) : IReadOnlyList<T>
    {
        public int Count => owner._slots.Length;

        public T this[int index] => owner[index];

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < owner._slots.Length; i++)
            {
                yield return owner._slots[i].Value!;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
