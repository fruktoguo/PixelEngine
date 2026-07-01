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
    /// 每帧最多消费的原始音频事件数；多余事件留在 ring 中由后续帧继续排空。
    /// </summary>
    public int MaxDrainedAudioEventsPerFrame { get; init; } = 4096;

    /// <summary>
    /// 同帧可接收的 impact 事件上限。
    /// </summary>
    public int MaxParticleImpactEventsPerFrame { get; init; } = 32;

    /// <summary>
    /// 同帧可接收的 fire crackle 事件上限。
    /// </summary>
    public int MaxFireCrackleEventsPerFrame { get; init; } = 16;

    /// <summary>
    /// 同帧可接收的 splash 事件上限。
    /// </summary>
    public int MaxLiquidSplashEventsPerFrame { get; init; } = 16;

    /// <summary>
    /// 同帧可接收的 explosion 事件上限。
    /// </summary>
    public int MaxExplosionEventsPerFrame { get; init; } = 8;

    /// <summary>
    /// 同帧可接收的 rigidbody shatter 事件上限。
    /// </summary>
    public int MaxRigidbodyShatterEventsPerFrame { get; init; } = 8;

    /// <summary>
    /// 同帧可接收的 ambient region 事件上限。
    /// </summary>
    public int MaxAmbientRegionEventsPerFrame { get; init; } = 16;

    /// <summary>
    /// 近坐标合并的世界 cell 桶尺寸。
    /// </summary>
    public int CoalesceBucketSize { get; init; } = 16;

    /// <summary>
    /// 同一材质同一事件类型的默认冷却 tick 数。
    /// </summary>
    public int DefaultCooldownTicks { get; init; } = 4;

    /// <summary>
    /// 冷却表容量，必须是正的 2 的幂。
    /// </summary>
    public int CooldownTableCapacity { get; init; } = 1024;

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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDrainedAudioEventsPerFrame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxParticleImpactEventsPerFrame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxFireCrackleEventsPerFrame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxLiquidSplashEventsPerFrame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxExplosionEventsPerFrame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxRigidbodyShatterEventsPerFrame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxAmbientRegionEventsPerFrame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CoalesceBucketSize);
        ArgumentOutOfRangeException.ThrowIfNegative(DefaultCooldownTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CooldownTableCapacity);
        return System.Numerics.BitOperations.IsPow2(CooldownTableCapacity)
            ? this
            : throw new ArgumentOutOfRangeException(nameof(CooldownTableCapacity), "CooldownTableCapacity 必须是正的 2 的幂。");
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
