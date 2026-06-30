namespace PixelEngine.Hosting;

/// <summary>
/// 表示能把真实子系统入口绑定到 12 相位管线的 Hosting 适配器。
/// </summary>
public interface IEnginePhaseDriver
{
    /// <summary>
    /// 将该驱动拥有的相位入口注册到运行时管线。
    /// </summary>
    /// <param name="phases">目标 12 相位管线。</param>
    void RegisterPhases(EnginePhasePipeline phases);
}
