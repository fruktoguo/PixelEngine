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
    }

    private void RunScripts(EngineTickContext context)
    {
        Runtime.BeginFrame();
        Runtime.Update((float)(context.Timing.Dt * context.Context.Clock.TimeScale));
        if (context.Timing.RunSim)
        {
            Runtime.FixedSimTick();
        }

        Runtime.EndFrame();
    }
}
