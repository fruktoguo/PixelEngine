using System.Collections.Concurrent;

namespace PixelEngine.Core.Events;

/// <summary>
/// 按事件载荷类型管理无锁事件通道。
/// </summary>
/// <remarks>
/// Core 只提供通用传输，不定义音频、玩法等事件载荷；载荷由上层以 unmanaged struct 定义。
/// 架构 §10.2 要求消费侧不进入 sim 热循环，生产侧仅做廉价 enqueue；容量满时由调用方做限频、合并或丢弃统计。
/// </remarks>
public sealed class EventBus
{
    private readonly ConcurrentDictionary<Type, object> _channels = new();

    /// <summary>
    /// 创建事件总线。
    /// </summary>
    /// <param name="capacityPerChannel">每个事件类型通道的容量，必须是正的 2 的幂。</param>
    public EventBus(int capacityPerChannel)
    {
        if (capacityPerChannel <= 0 || !System.Numerics.BitOperations.IsPow2(capacityPerChannel))
        {
            throw new ArgumentOutOfRangeException(nameof(capacityPerChannel), capacityPerChannel, "通道容量必须是正的 2 的幂。");
        }

        CapacityPerChannel = capacityPerChannel;
    }

    /// <summary>
    /// 获取每个事件类型通道的容量。
    /// </summary>
    public int CapacityPerChannel { get; }

    /// <summary>
    /// 获取指定事件载荷类型对应的 MPSC 通道；不存在时创建。
    /// </summary>
    /// <typeparam name="T">非托管事件载荷类型。</typeparam>
    /// <returns>类型专属事件通道。</returns>
    public MpscRingBuffer<T> Channel<T>()
        where T : unmanaged
    {
        object channel = _channels.GetOrAdd(typeof(T), _ => new MpscRingBuffer<T>(CapacityPerChannel));
        return (MpscRingBuffer<T>)channel;
    }
}
