namespace PixelEngine.Audio;

/// <summary>
/// 音频距离衰减模型。
/// </summary>
public enum AudioDistanceModel : byte
{
    /// <summary>
    /// OpenAL inverse distance clamped，适合作为 2D positional audio 默认模型。
    /// </summary>
    InverseDistanceClamped,
}
