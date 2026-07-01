using PixelEngine.Core.Events;
using PixelEngine.Simulation;

namespace PixelEngine.Physics;

/// <summary>
/// CA 相位写入、physics 相位排空的刚体损伤队列。
/// </summary>
public sealed class RigidDamageQueue(int capacityPow2 = 4096) : IRigidDamageSink
{
    private readonly MpscRingBuffer<RigidDamageEvent> _events = new(capacityPow2);
    private int _dropped;

    /// <summary>队列满时丢弃的事件数。</summary>
    public int DroppedCount => Volatile.Read(ref _dropped);

    /// <inheritdoc />
    public void OnOwnedCellDamaged(int wx, int wy)
    {
        RigidDamageEvent damage = new(wx, wy);
        if (!_events.TryEnqueue(in damage))
        {
            _ = Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>
    /// 将队列内容排入目标缓冲。
    /// </summary>
    public int DrainTo(Span<RigidDamageEvent> destination)
    {
        return _events.DrainTo(destination);
    }
}

/// <summary>
/// 一个 RigidOwned cell 被 CA 消耗/覆盖的事件。
/// </summary>
/// <param name="WorldX">world X。</param>
/// <param name="WorldY">world Y。</param>
public readonly record struct RigidDamageEvent(int WorldX, int WorldY);
