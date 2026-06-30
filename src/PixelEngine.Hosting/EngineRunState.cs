namespace PixelEngine.Hosting;

/// <summary>
/// Engine 生命周期状态。
/// </summary>
public enum EngineRunState
{
    /// <summary>
    /// 已构建但尚未进入运行循环。
    /// </summary>
    Created,

    /// <summary>
    /// 正在运行或已经执行过 tick。
    /// </summary>
    Running,

    /// <summary>
    /// 已关闭，不能再执行 tick。
    /// </summary>
    Shutdown,
}
