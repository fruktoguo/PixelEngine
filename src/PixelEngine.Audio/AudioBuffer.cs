namespace PixelEngine.Audio;

/// <summary>
/// 已上传到后端的音频 buffer 元数据。
/// </summary>
/// <param name="Handle">后端 buffer 句柄。</param>
/// <param name="Format">PCM 样本格式。</param>
/// <param name="SampleRate">采样率。</param>
/// <param name="ByteLength">PCM 字节数。</param>
public readonly record struct AudioBuffer(uint Handle, AudioSampleFormat Format, int SampleRate, int ByteLength);
