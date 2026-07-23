using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 IScriptRuntime 绑定到 Hosting 相位 1。
/// </summary>
/// <param name="runtime">脚本运行时。</param>
/// <param name="scriptContext">脚本上下文。</param>
public sealed class ScriptingPhaseDriver(IScriptRuntime runtime, IScriptContext scriptContext) : IEnginePhaseDriver
{
    /// <summary>
    /// 脚本运行时。
    /// </summary>
    public IScriptRuntime Runtime { get; } = runtime ?? throw new ArgumentNullException(nameof(runtime));

    /// <summary>
    /// 脚本上下文。
    /// </summary>
    public IScriptContext ScriptContext { get; } = scriptContext ?? throw new ArgumentNullException(nameof(scriptContext));

    /// <summary>
    /// 注册脚本相位入口。
    /// </summary>
    /// <param name="phases">目标 12 相位管线。</param>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        Runtime.Initialize(ScriptContext);
        phases.Register(EnginePhase.GameLogicAndScripts, RunScripts);
        phases.RegisterPausedOnly(EnginePhase.GameLogicAndScripts, DispatchPausedEvents);
    }

    // 相位 1：Update 用渲染帧 dt（含 TimeScale），FixedSimTick 仅在 RunSim 帧执行一次。
    private void RunScripts(EngineTickContext context)
    {
        Runtime.BeginFrame();
        Runtime.Update((float)ResolveUpdateDeltaSeconds(context));
        if (context.Timing.RunSim)
        {
            Runtime.FixedSimTick();
        }

        Runtime.EndFrame();
    }

    // Paused 不执行 Behaviour Update/Fixed/EndFrame，只派发 UI 等已入队事件以保持菜单可交互。
    private void DispatchPausedEvents(EngineTickContext context)
    {
        _ = context;
        if (ScriptContext.Events is IScriptEventDispatcher dispatcher)
        {
            dispatcher.DrainEvents();
        }
    }

    private static double ResolveUpdateDeltaSeconds(EngineTickContext context)
    {
        double timeScale = context.Context.Clock.TimeScale;
        double dt = context.Timing.RealDeltaSeconds > 0
            ? context.Timing.RealDeltaSeconds * timeScale
            : context.Timing.Dt * timeScale;
        return !double.IsFinite(dt) || dt <= 0
            ? 0
            : Math.Min(dt, context.Timing.Dt);
    }
}
