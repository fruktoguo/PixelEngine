namespace PixelEngine.Hosting;

/// <summary>
/// 描述 Hosting 聚合服务角色的可用性。
/// </summary>
/// <param name="Role">服务角色。</param>
/// <param name="Available">该角色是否已有真实后端。</param>
/// <param name="ServiceType">真实后端类型；不可用时为空。</param>
public readonly record struct EngineServiceAvailability(
    EngineServiceRole Role,
    bool Available,
    Type? ServiceType);
