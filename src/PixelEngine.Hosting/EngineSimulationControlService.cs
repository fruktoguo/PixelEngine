using PixelEngine.Editor;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Editor SimulationControlToolbar 连接到真实 Engine 的控制适配器。
/// </summary>
/// <param name="engine">运行时引擎。</param>
public sealed class EngineSimulationControlService(Engine engine) : ISimulationControlService
{
    private readonly Engine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    /// <summary>
    /// 捕获当前 Engine 模式、请求 sim 频率和 FrameClock 计数。
    /// </summary>
    /// <returns>Editor 可显示的 sim 控制快照。</returns>
    public SimulationControlSnapshot Capture()
    {
        return new SimulationControlSnapshot(
            _engine.Mode == EngineExecutionMode.Play,
            _engine.RequestedSimHz,
            _engine.Context.Clock.FrameIndex,
            _engine.Context.Clock.SimTickIndex,
            _engine.Context.Clock.RunSimThisFrame);
    }

    /// <summary>
    /// 切换 Engine 到 Play 模式，后续普通 tick 由 FrameClock 决定是否执行 sim。
    /// </summary>
    public void EnterPlayMode()
    {
        _engine.EnterPlayMode();
    }

    /// <summary>
    /// 切换 Engine 到 Edit 模式，后续普通 tick 只渲染不推进 sim。
    /// </summary>
    public void EnterEditMode()
    {
        _engine.EnterEditMode();
    }

    /// <summary>
    /// 通过 Engine.StepOnce 执行恰好一个 sim tick，随后回到 Edit 模式。
    /// </summary>
    public void StepOnce()
    {
        _ = _engine.StepOnce();
    }

    /// <summary>
    /// 设置 Engine 请求的基础 sim 频率。
    /// </summary>
    /// <param name="simHz">目标 sim 频率。</param>
    public void SetSimHz(double simHz)
    {
        _engine.SetRequestedSimHz(simHz);
    }
}
