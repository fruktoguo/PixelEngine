using PixelEngine.Core.Time;

namespace PixelEngine.Hosting;

/// <summary>
/// 12 相位同步调度管线，按架构 §3.3 的固定顺序执行已注册 hook。
/// </summary>
public sealed class EnginePhasePipeline
{
    private const int PhaseCount = 12;
    private readonly List<EnginePhaseAction>[] _actions;
    private readonly EngineCommandQueue _commands;

    /// <summary>
    /// 创建空相位管线。
    /// </summary>
    public EnginePhasePipeline(EngineCommandQueue commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        _actions = new List<EnginePhaseAction>[PhaseCount];
        for (int i = 0; i < _actions.Length; i++)
        {
            _actions[i] = [];
        }
    }

    /// <summary>
    /// 注册指定相位的同步 hook。
    /// </summary>
    public void Register(EnginePhase phase, EnginePhaseAction action)
    {
        ValidatePhase(phase);
        ArgumentNullException.ThrowIfNull(action);
        _actions[(int)phase].Add(action);
    }

    /// <summary>
    /// 获取指定相位已注册 hook 数量。
    /// </summary>
    public int Count(EnginePhase phase)
    {
        ValidatePhase(phase);
        return _actions[(int)phase].Count;
    }

    /// <summary>
    /// 按固定顺序执行本 tick 应运行的相位。
    /// </summary>
    public void Execute(Engine engine, FrameTiming timing)
    {
        ArgumentNullException.ThrowIfNull(engine);
        for (int raw = 0; raw < PhaseCount; raw++)
        {
            EnginePhase phase = (EnginePhase)raw;
            if (!ShouldRunPhase(phase, timing))
            {
                continue;
            }

            EngineTickContext context = new(engine, engine.Context, timing, phase);
            _ = _commands.Flush(context);
            List<EnginePhaseAction> actions = _actions[raw];
            for (int i = 0; i < actions.Count; i++)
            {
                actions[i](context);
            }
        }
    }

    /// <summary>
    /// 判断给定相位在当前帧是否应运行。
    /// </summary>
    public static bool ShouldRunPhase(EnginePhase phase, FrameTiming timing)
    {
        ValidatePhase(phase);
        return timing.RunSim || phase is
            EnginePhase.InputAndTime or
            EnginePhase.BuildRenderBuffer or
            EnginePhase.GpuUploadAndRender or
            EnginePhase.WorldStreaming;
    }

    private static void ValidatePhase(EnginePhase phase)
    {
        if ((uint)phase >= PhaseCount)
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "未知 Engine 相位。");
        }
    }
}
