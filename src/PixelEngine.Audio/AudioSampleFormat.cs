namespace PixelEngine.Audio;

/// <summary>
/// OpenAL 可直接上传的 PCM 样本格式。
/// </summary>
public enum AudioSampleFormat : byte
{
    /// <summary>单声道 8-bit PCM。</summary>
    Mono8,

    /// <summary>单声道 16-bit PCM。</summary>
    Mono16,

    /// <summary>立体声 8-bit PCM。</summary>
    Stereo8,

    /// <summary>立体声 16-bit PCM。</summary>
    Stereo16,
}
