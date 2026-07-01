namespace PixelEngine.Audio;

/// <summary>
/// 把内容侧 cue 句柄解析为已上传的 OpenAL buffer。
/// </summary>
public interface IAudioCueBufferResolver
{
    /// <summary>
    /// 尝试解析 buffer 句柄。
    /// </summary>
    /// <param name="cueHandle">内容侧 cue 句柄。</param>
    /// <param name="buffer">OpenAL buffer 句柄。</param>
    /// <returns>若 cue 已加载且可播放则为 <see langword="true"/>。</returns>
    bool TryResolveBuffer(int cueHandle, out uint buffer);
}
