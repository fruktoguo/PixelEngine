namespace PixelEngine.Audio;

/// <summary>
/// 音频字节解码器。
/// </summary>
public interface IAudioDecoder
{
    /// <summary>
    /// 尝试解码音频字节。
    /// </summary>
    /// <param name="bytes">输入字节。</param>
    /// <param name="data">解码后的 PCM 数据。</param>
    /// <returns>是否成功识别并解码。</returns>
    bool TryDecode(ReadOnlySpan<byte> bytes, out DecodedAudioData data);
}
