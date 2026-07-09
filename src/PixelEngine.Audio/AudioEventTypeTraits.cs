using PixelEngine.Core.Events;

namespace PixelEngine.Audio;

/// <summary>
/// <see cref="AudioEventType" /> 的稠密索引、帧预算、优先级与音量分类映射。
/// 供 <see cref="AudioDispatcher" /> 按类型分桶限流与排序。
/// </summary>
internal static class AudioEventTypeTraits
{
    /// <summary>
    /// 已支持的音频事件类型数量，与分桶数组长度一致。
    /// </summary>
    public const int TypeCount = 6;

    /// <summary>
    /// 将事件类型映射为 0..<see cref="TypeCount" />-1 的稠密索引。
    /// </summary>
    /// <param name="type">音频事件类型。</param>
    /// <param name="index">成功时写入稠密索引。</param>
    /// <returns>类型受支持时返回 true。</returns>
    public static bool TryGetIndex(AudioEventType type, out int index)
    {
        index = type switch
        {
            AudioEventType.ParticleImpact => 0,
            AudioEventType.FireCrackle => 1,
            AudioEventType.LiquidSplash => 2,
            AudioEventType.Explosion => 3,
            AudioEventType.RigidbodyShatter => 4,
            AudioEventType.AmbientRegion => 5,
            _ => -1,
        };

        return index >= 0;
    }

    /// <summary>
    /// 读取指定事件类型在当前 <see cref="AudioSettings" /> 下的每帧播放上限。
    /// </summary>
    /// <param name="settings">音频设置。</param>
    /// <param name="type">音频事件类型。</param>
    /// <returns>每帧允许派发的事件数；未知类型返回 0。</returns>
    public static int GetPerFrameCap(AudioSettings settings, AudioEventType type)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return type switch
        {
            AudioEventType.ParticleImpact => settings.MaxParticleImpactEventsPerFrame,
            AudioEventType.FireCrackle => settings.MaxFireCrackleEventsPerFrame,
            AudioEventType.LiquidSplash => settings.MaxLiquidSplashEventsPerFrame,
            AudioEventType.Explosion => settings.MaxExplosionEventsPerFrame,
            AudioEventType.RigidbodyShatter => settings.MaxRigidbodyShatterEventsPerFrame,
            AudioEventType.AmbientRegion => settings.MaxAmbientRegionEventsPerFrame,
            _ => 0,
        };
    }

    /// <summary>
    /// 返回事件类型的播放优先级；数值越大越优先保留。
    /// </summary>
    /// <param name="type">音频事件类型。</param>
    /// <returns>优先级字节值；未知类型返回 0。</returns>
    public static byte GetPriority(AudioEventType type)
    {
        return type switch
        {
            AudioEventType.Explosion => 220,
            AudioEventType.RigidbodyShatter => 180,
            AudioEventType.LiquidSplash => 130,
            AudioEventType.ParticleImpact => 110,
            AudioEventType.FireCrackle => 80,
            AudioEventType.AmbientRegion => 40,
            _ => 0,
        };
    }

    /// <summary>
    /// 将事件类型映射到主音量分类；环境区域走 Ambient，其余走 Sfx。
    /// </summary>
    /// <param name="type">音频事件类型。</param>
    /// <returns>对应的音量分类。</returns>
    public static AudioVolumeCategory GetVolumeCategory(AudioEventType type)
    {
        return type == AudioEventType.AmbientRegion
            ? AudioVolumeCategory.Ambient
            : AudioVolumeCategory.Sfx;
    }
}
