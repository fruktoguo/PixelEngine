namespace PixelEngine.Audio;

/// <summary>
/// 材质化音频表解析出的单次播放参数。
/// </summary>
/// <param name="CueHandle">内容侧稳定 cue 句柄。</param>
/// <param name="Gain">播放增益。</param>
/// <param name="Pitch">播放 pitch。</param>
/// <param name="Priority">voice 抢占优先级。</param>
public readonly record struct MaterialAudioPlayback(int CueHandle, float Gain, float Pitch, byte Priority);
