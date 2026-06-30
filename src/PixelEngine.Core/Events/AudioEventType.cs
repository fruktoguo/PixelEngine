namespace PixelEngine.Core.Events;

/// <summary>
/// 音频事件类型。事件由 sim/physics 等生产，Audio 子系统消费。
/// </summary>
public enum AudioEventType : byte
{
    /// <summary>
    /// 自由粒子高速沉积或碰撞。
    /// </summary>
    ParticleImpact = 1,

    /// <summary>
    /// 火焰或燃烧区域提示。
    /// </summary>
    FireCrackle = 2,

    /// <summary>
    /// 液体飞溅或落水。
    /// </summary>
    LiquidSplash = 3,

    /// <summary>
    /// 爆炸或冲击抛射。
    /// </summary>
    Explosion = 4,

    /// <summary>
    /// 刚体破碎。
    /// </summary>
    RigidbodyShatter = 5,

    /// <summary>
    /// 材质化环境声区域提示。
    /// </summary>
    AmbientRegion = 6,
}
