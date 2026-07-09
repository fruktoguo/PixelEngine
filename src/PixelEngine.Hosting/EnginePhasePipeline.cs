using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Time;

namespace PixelEngine.Hosting;

/// <summary>
/// 12 相位同步调度管线，按架构 §3.3 的固定顺序执行已注册 hook。
/// </summary>
/// <remarks>
/// 相位 0-11 对应 <see cref="EnginePhase"/>；每个相位可注册多个同步 action，
/// 由 <see cref="IEnginePhaseDriver"/> 或 <see cref="EngineBuilder.OnPhase"/> 注入。
/// Edit 模式跳过相位 1（脚本/sim 命令）；无 sim 帧仍运行输入、UI、渲染与流式后台相位。
/// </remarks>
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
        // 严格按相位 0→11 顺序推进；单相位内先 flush 延迟命令再执行已注册 hook。
        for (int raw = 0; raw < PhaseCount; raw++)
        {
            EnginePhase phase = (EnginePhase)raw;
            // Edit 暂停态不跑脚本与 sim 命令，但保留输入采样与渲染相位以维持编辑器交互。
            if (engine.Mode == EngineExecutionMode.Edit && phase == EnginePhase.GameLogicAndScripts)
            {
                continue;
            }

            if (!ShouldRunPhase(phase, timing))
            {
                continue;
            }

            EngineTickContext context = new(engine, engine.Context, timing, phase);
            using (engine.Context.Profiler.Measure(ToFramePhase(phase)))
            {
                _ = _commands.Flush(context);
                List<EnginePhaseAction> actions = _actions[raw];
                for (int i = 0; i < actions.Count; i++)
                {
                    actions[i](context);
                }
            }
        }
    }

    /// <summary>
    /// 判断给定相位在当前帧是否应运行。
    /// </summary>
    /// <remarks>
    /// 无 sim 的渲染帧仍执行相位 0/1/9/10/11，保证 UI、present 与世界流式后台不随 sim 暂停而停摆。
    /// </remarks>
    public static bool ShouldRunPhase(EnginePhase phase, FrameTiming timing)
    {
        ValidatePhase(phase);
        return timing.RunSim || phase is
            EnginePhase.InputAndTime or
            EnginePhase.GameLogicAndScripts or
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

    private static FramePhase ToFramePhase(EnginePhase phase)
    {
        ValidatePhase(phase);
        return (FramePhase)phase;
    }
}
