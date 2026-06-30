namespace PixelEngine.Hosting;

/// <summary>
/// 表示由 Hosting 统一装配、初始化并按逆序关闭的引擎子系统。
/// </summary>
public interface IEngineSubsystem
{
    /// <summary>
    /// 子系统诊断名称，用于初始化或关闭失败时定位来源。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 在 EngineContext 已创建后初始化子系统，并注册它提供的真实服务。
    /// </summary>
    /// <param name="context">当前引擎运行上下文。</param>
    void Initialize(EngineContext context);

    /// <summary>
    /// 关闭子系统并释放它持有的托管或 native 资源。
    /// </summary>
    void Shutdown();
}
