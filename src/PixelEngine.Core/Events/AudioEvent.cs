namespace PixelEngine.Core.Events;

/// <summary>
/// 跨子系统传递的粗粒度音频事件载荷。
/// </summary>
/// <remarks>
/// 该类型保持非托管布局，供 <see cref="MpscRingBuffer{T}"/> 零装箱传输。
/// </remarks>
public readonly struct AudioEvent(
    AudioEventType type,
    int cellX,
    int cellY,
    ushort materialId,
    float magnitude,
    ushort count = 1)
{
    /// <summary>
    /// 事件类型。
    /// </summary>
    public AudioEventType Type { get; } = type;

    /// <summary>
    /// 世界 cell X 坐标。
    /// </summary>
    public int CellX { get; } = cellX;

    /// <summary>
    /// 世界 cell Y 坐标。
    /// </summary>
    public int CellY { get; } = cellY;

    /// <summary>
    /// 关联材质 runtime id；无单一材质时为 0。
    /// </summary>
    public ushort MaterialId { get; } = materialId;

    /// <summary>
    /// 事件强度，含义由事件类型解释。
    /// </summary>
    public float Magnitude { get; } = magnitude;

    /// <summary>
    /// 聚合事件数量。产生侧可写 1，消费侧 coalescer 可累加。
    /// </summary>
    public ushort Count { get; } = count;
}
