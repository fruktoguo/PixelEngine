namespace PixelEngine.Audio;

/// <summary>
/// 将合并后的音频事件解析为具体 clip/buffer 并驱动已占用 voice 播放。
/// </summary>
public interface IAudioEventPlayer
{
    /// <summary>
    /// 尝试播放一个合并事件。实现必须只使用初始化期准备好的数据结构，热路径不得做字符串或字典查询。
    /// </summary>
    /// <param name="audioEvent">合并后的音频事件。</param>
    /// <param name="voice">已占用的 positional voice。</param>
    /// <param name="tick">当前模拟 tick。</param>
    /// <returns>若成功驱动 voice 播放则为 <see langword="true"/>；没有可用 cue/clip 时为 <see langword="false"/>。</returns>
    bool TryPlay(in CoalescedAudioEvent audioEvent, AudioVoice voice, long tick);
}
