using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 将脚本运行时控制请求映射到真实 Engine 执行模式与生命周期。
/// </summary>
/// <param name="engine">运行时引擎。</param>
public sealed class EngineScriptRuntimeControlApi(Engine engine) : IRuntimeControlApi
{
    private readonly Engine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    /// <summary>
    /// 捕获当前 Engine 运行模式、关闭请求、sim 频率与帧号。
    /// </summary>
    /// <returns>脚本可读的运行控制快照。</returns>
    public RuntimeControlSnapshot Capture()
    {
        return new RuntimeControlSnapshot(
            _engine.Mode == EngineExecutionMode.Play,
            _engine.IsShutdownRequested,
            _engine.RequestedSimHz,
            _engine.Context.Clock.FrameIndex);
    }

    /// <summary>
    /// 切换到 Edit 模式，暂停 sim/physics 推进但保留渲染与 GUI。
    /// </summary>
    public void PauseSimulation()
    {
        _engine.EnterEditMode();
    }

    /// <summary>
    /// 切换到 Play 模式，恢复 sim/physics 推进。
    /// </summary>
    public void ResumeSimulation()
    {
        _engine.EnterPlayMode();
    }

    /// <summary>
    /// 请求 Engine 在当前 tick 结束后关闭。
    /// </summary>
    /// <returns>关闭请求结果。</returns>
    public RuntimeControlResult RequestShutdown()
    {
        _engine.RequestShutdown();
        return new RuntimeControlResult(true, "已请求关闭。");
    }
}
