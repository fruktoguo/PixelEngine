namespace PixelEngine.Audio;

/// <summary>
/// 单帧音频事件派发统计。
/// </summary>
/// <param name="Drained">从 Core ring 排空的原始事件数。</param>
/// <param name="Coalesced">因近坐标合并而未形成独立播放项的事件数。</param>
/// <param name="Dropped">因类型上限、未知类型、冷却或 voice 不可用而丢弃的事件数。</param>
/// <param name="Dispatched">进入播放解析器的合并事件数。</param>
/// <param name="Played">播放解析器确认播放的事件数。</param>
/// <param name="ActiveVoices">派发后活跃 positional voice 数。</param>
/// <param name="VoiceSteals">累计 voice 抢占次数。</param>
public readonly record struct AudioDispatchStats(
    int Drained,
    int Coalesced,
    int Dropped,
    int Dispatched,
    int Played,
    int ActiveVoices,
    long VoiceSteals);
