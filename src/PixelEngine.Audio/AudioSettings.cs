namespace PixelEngine.Audio;

/// <summary>
/// 音频子系统运行设置，覆盖 voice 池、listener 换算与 OpenAL 距离衰减参数。
/// </summary>
public sealed class AudioSettings
{
    /// <summary>
    /// 最大一次性 positional voice 数。
    /// </summary>
    public int MaxVoices { get; set; } = 64;

    /// <summary>
    /// 最大 ambient loop voice 数。
    /// </summary>
    public int MaxAmbientVoices { get; set; } = 8;

    /// <summary>
    /// 每米对应的世界 cell 数。
    /// </summary>
    public float PixelsPerMeter { get; set; } = 32f;

    /// <summary>
    /// listener 在 OpenAL 3D 空间中的 Z 深度。
    /// </summary>
    public float ListenerDepth { get; set; } = 32f;

    /// <summary>
    /// 全局主音量。
    /// </summary>
    public float MasterVolume { get; set; } = 1f;

    /// <summary>
    /// 世界内一次性音效与材质事件音量。
    /// </summary>
    public float SfxVolume { get; set; } = 1f;

    /// <summary>
    /// UI 与非定位反馈音效音量。
    /// </summary>
    public float UiVolume { get; set; } = 1f;

    /// <summary>
    /// 材质区域 ambient loop 音量。
    /// </summary>
    public float AmbientVolume { get; set; } = 1f;

    /// <summary>
    /// OpenAL source reference distance。
    /// </summary>
    public float ReferenceDistance { get; set; } = 1f;

    /// <summary>
    /// OpenAL source max distance。
    /// </summary>
    public float MaxDistance { get; set; } = 64f;

    /// <summary>
    /// OpenAL source rolloff factor。
    /// </summary>
    public float RolloffFactor { get; set; } = 1f;

    /// <summary>
    /// 每帧最多消费的原始音频事件数；多余事件留在 ring 中由后续帧继续排空。
    /// </summary>
    public int MaxDrainedAudioEventsPerFrame { get; set; } = 4096;

    /// <summary>
    /// 同帧可接收的 impact 事件上限。
    /// </summary>
    public int MaxParticleImpactEventsPerFrame { get; set; } = 32;

    /// <summary>
    /// 同帧可接收的 fire crackle 事件上限。
    /// </summary>
    public int MaxFireCrackleEventsPerFrame { get; set; } = 16;

    /// <summary>
    /// 同帧可接收的 splash 事件上限。
    /// </summary>
    public int MaxLiquidSplashEventsPerFrame { get; set; } = 16;

    /// <summary>
    /// 同帧可接收的 explosion 事件上限。
    /// </summary>
    public int MaxExplosionEventsPerFrame { get; set; } = 8;

    /// <summary>
    /// 同帧可接收的 rigidbody shatter 事件上限。
    /// </summary>
    public int MaxRigidbodyShatterEventsPerFrame { get; set; } = 8;

    /// <summary>
    /// 同帧可接收的 ambient region 事件上限。
    /// </summary>
    public int MaxAmbientRegionEventsPerFrame { get; set; } = 16;

    /// <summary>
    /// 近坐标合并的世界 cell 桶尺寸。
    /// </summary>
    public int CoalesceBucketSize { get; set; } = 16;

    /// <summary>
    /// 同一材质同一事件类型的默认冷却 tick 数。
    /// </summary>
    public int DefaultCooldownTicks { get; set; } = 4;

    /// <summary>
    /// ambient 区域进入阈值，基于聚合事件 Magnitude。
    /// </summary>
    public float AmbientEnterThreshold { get; set; } = 0.35f;

    /// <summary>
    /// ambient 区域退出阈值，基于聚合事件 Magnitude。
    /// </summary>
    public float AmbientExitThreshold { get; set; } = 0.15f;

    /// <summary>
    /// ambient 每次 Update 的线性淡变步长。
    /// </summary>
    public float AmbientFadeRate { get; set; } = 0.08f;

    /// <summary>
    /// 冷却表容量，必须是正的 2 的幂。
    /// </summary>
    public int CooldownTableCapacity { get; set; } = 1024;

    /// <summary>
    /// 获取指定混音类别的线性音量。
    /// </summary>
    /// <param name="category">混音类别。</param>
    /// <returns>线性音量。</returns>
    public float GetCategoryVolume(AudioVolumeCategory category)
    {
        return category switch
        {
            AudioVolumeCategory.Sfx => SfxVolume,
            AudioVolumeCategory.Ui => UiVolume,
            AudioVolumeCategory.Ambient => AmbientVolume,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "未知音频音量类别。"),
        };
    }

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

        ValidateNonNegativeFinite(SfxVolume, nameof(SfxVolume));
        ValidateNonNegativeFinite(UiVolume, nameof(UiVolume));
        ValidateNonNegativeFinite(AmbientVolume, nameof(AmbientVolume));
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
        ValidateUnitRange(AmbientEnterThreshold, nameof(AmbientEnterThreshold));
        ValidateUnitRange(AmbientExitThreshold, nameof(AmbientExitThreshold));
        ValidateUnitRange(AmbientFadeRate, nameof(AmbientFadeRate));
        if (AmbientExitThreshold > AmbientEnterThreshold)
        {
            throw new ArgumentOutOfRangeException(nameof(AmbientExitThreshold), "AmbientExitThreshold 必须小于等于 AmbientEnterThreshold。");
        }

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

    private static void ValidateNonNegativeFinite(float value, string parameterName)
    {
        ValidateFinite(value, parameterName);
        if (value < 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} 必须为非负有限数。");
        }
    }

    private static void ValidateFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} 必须为有限数。");
        }
    }

    private static void ValidateUnitRange(float value, string parameterName)
    {
        ValidateFinite(value, parameterName);
        if (value is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} 必须位于 [0,1]。");
        }
    }
}
