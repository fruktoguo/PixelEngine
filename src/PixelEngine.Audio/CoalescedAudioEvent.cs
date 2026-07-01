using PixelEngine.Core.Events;

namespace PixelEngine.Audio;

/// <summary>
/// 音频消费侧同帧合并后的事件，供材质音效解析与 voice 派发使用。
/// </summary>
/// <param name="Type">事件类型。</param>
/// <param name="CellX">合并桶中心的世界 cell X。</param>
/// <param name="CellY">合并桶中心的世界 cell Y。</param>
/// <param name="MaterialId">最大强度事件对应的材质 runtime id。</param>
/// <param name="Magnitude">同桶内最大事件强度。</param>
/// <param name="Count">同桶内接收的事件数量。</param>
public readonly record struct CoalescedAudioEvent(
    AudioEventType Type,
    int CellX,
    int CellY,
    ushort MaterialId,
    float Magnitude,
    ushort Count);
