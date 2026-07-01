namespace PixelEngine.Audio;

/// <summary>
/// 解码后的 PCM 音频数据。
/// </summary>
/// <param name="Pcm">PCM 字节。</param>
/// <param name="Format">样本格式。</param>
/// <param name="SampleRate">采样率。</param>
/// <param name="Channels">声道数。</param>
/// <param name="BitsPerSample">每样本 bit 数。</param>
public sealed record DecodedAudioData(
    byte[] Pcm,
    AudioSampleFormat Format,
    int SampleRate,
    short Channels,
    short BitsPerSample);
