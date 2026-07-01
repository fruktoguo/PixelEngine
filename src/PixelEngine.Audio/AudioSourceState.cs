namespace PixelEngine.Audio;

/// <summary>
/// 音频 source 的后端无关播放状态。
/// </summary>
public enum AudioSourceState : byte
{
    /// <summary>
    /// 初始或未知状态。
    /// </summary>
    Initial,

    /// <summary>
    /// 正在播放。
    /// </summary>
    Playing,

    /// <summary>
    /// 已暂停。
    /// </summary>
    Paused,

    /// <summary>
    /// 已停止或播放结束。
    /// </summary>
    Stopped,
}
