namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 聚合并暴露给 Demo、脚本与 Editor 的服务角色。
/// </summary>
public enum EngineServiceRole
{
    /// <summary>
    /// 世界 cell 读写能力。
    /// </summary>
    WorldAccess,

    /// <summary>
    /// 自由粒子服务。
    /// </summary>
    ParticleService,

    /// <summary>
    /// 物理与刚体服务。
    /// </summary>
    PhysicsService,

    /// <summary>
    /// 材质注册与查询服务。
    /// </summary>
    MaterialRegistry,

    /// <summary>
    /// 相机服务。
    /// </summary>
    Camera,

    /// <summary>
    /// 输入服务。
    /// </summary>
    Input,

    /// <summary>
    /// 事件总线服务。
    /// </summary>
    EventBus,

    /// <summary>
    /// 音频服务。
    /// </summary>
    AudioService,

    /// <summary>
    /// 场景服务。
    /// </summary>
    SceneService,

    /// <summary>
    /// 诊断服务。
    /// </summary>
    Diagnostics,

    /// <summary>
    /// 脚本运行时服务。
    /// </summary>
    Scripting,
}
