namespace PixelEngine.Audio;

/// <summary>
/// 音频子系统运行设置，覆盖 voice 池、listener 换算与 OpenAL 距离衰减参数。
/// </summary>
public sealed class AudioSettings
{
    /// <summary>
    /// 最大一次性 positional voice 数。
    /// </summary>
    public int MaxVoices { get; init; } = 64;

    /// <summary>
    /// 最大 ambient loop voice 数。
    /// </summary>
    public int MaxAmbientVoices { get; init; } = 8;

    /// <summary>
    /// 每米对应的世界 cell 数。
    /// </summary>
    public float PixelsPerMeter { get; init; } = 32f;

    /// <summary>
    /// listener 在 OpenAL 3D 空间中的 Z 深度。
    /// </summary>
    public float ListenerDepth { get; init; } = 32f;

    /// <summary>
    /// 全局主音量。
    /// </summary>
    public float MasterVolume { get; init; } = 1f;

    /// <summary>
    /// OpenAL source reference distance。
    /// </summary>
    public float ReferenceDistance { get; init; } = 1f;

    /// <summary>
    /// OpenAL source max distance。
    /// </summary>
    public float MaxDistance { get; init; } = 64f;

    /// <summary>
    /// OpenAL source rolloff factor。
    /// </summary>
    public float RolloffFactor { get; init; } = 1f;

    /// <summary>
    /// 返回已校验设置。
    /// </summary>
    /// <returns>当前设置。</returns>
    public AudioSettings Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxVoices);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxAmbientVoices);
        ValidatePositiveFinite(PixelsPerMeter, nameof(PixelsPerMeter));
        ValidateFinite(ListenerDepth, nameof(ListenerDepth));
        ValidateFinite(MasterVolume, nameof(MasterVolume));
        if (MasterVolume < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(MasterVolume), "MasterVolume 必须为非负有限数。");
        }

        ValidatePositiveFinite(ReferenceDistance, nameof(ReferenceDistance));
        ValidatePositiveFinite(MaxDistance, nameof(MaxDistance));
        ValidatePositiveFinite(RolloffFactor, nameof(RolloffFactor));
        return this;
    }

    private static void ValidatePositiveFinite(float value, string parameterName)
    {
        ValidateFinite(value, parameterName);
        if (value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} 必须为正有限数。");
        }
    }

    private static void ValidateFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} 必须为有限数。");
        }
    }
}
