using System.Diagnostics;
using PixelEngine.Core.Diagnostics;
using PixelEngine.UI;
using ScriptUi = PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 将游戏大 UI 逻辑更新绑定到 Hosting 相位 1，使用渲染帧 dt 而不是 sim tick dt。
/// </summary>
public sealed class GameUiPhaseDriver : IEnginePhaseDriver
{
    private readonly GameUiHost? _host;
    private readonly GameUiCanvasRegistry? _registry;
    private readonly IGameUiEventSink? _eventSink;
    private readonly IGameUiModelPusher? _modelPusher;
    private readonly IGameUiCompositionPolicy? _runtimePolicy;
    private readonly UiEvent[] _eventBuffer;

    /// <summary>
    /// 创建游戏 UI 相位驱动。
    /// </summary>
    /// <param name="host">游戏 UI 宿主。</param>
    /// <param name="eventCapacity">单帧事件 drain 缓冲容量。</param>
    /// <param name="eventSink">可选事件接收器。</param>
    /// <param name="modelPusher">可选模型推送器。</param>
    /// <param name="runtimePolicy">可选 Edit/Play runtime UI 更新与合成策略。</param>
    public GameUiPhaseDriver(
        GameUiHost host,
        int eventCapacity = 128,
        IGameUiEventSink? eventSink = null,
        IGameUiModelPusher? modelPusher = null,
        IGameUiCompositionPolicy? runtimePolicy = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventCapacity);
        _eventSink = eventSink;
        _modelPusher = modelPusher;
        _runtimePolicy = runtimePolicy;
        _eventBuffer = new UiEvent[eventCapacity];
    }

    /// <summary>
    /// 创建可驱动多个独立 Canvas 的游戏 UI 相位驱动。
    /// </summary>
    /// <param name="registry">当前场景 Canvas 注册表。</param>
    /// <param name="eventCapacity">单 Canvas 单次事件 drain 缓冲容量。</param>
    /// <param name="eventSink">可选事件接收器。</param>
    /// <param name="modelPusher">可选模型推送器。</param>
    /// <param name="runtimePolicy">可选 Edit/Play runtime UI 更新与合成策略。</param>
    public GameUiPhaseDriver(
        GameUiCanvasRegistry registry,
        int eventCapacity = 128,
        IGameUiEventSink? eventSink = null,
        IGameUiModelPusher? modelPusher = null,
        IGameUiCompositionPolicy? runtimePolicy = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventCapacity);
        _eventSink = eventSink;
        _modelPusher = modelPusher;
        _runtimePolicy = runtimePolicy;
        _eventBuffer = new UiEvent[eventCapacity];
    }

    /// <summary>
    /// 最近一次传给 UI 的渲染 dt。
    /// </summary>
    public float LastDeltaSeconds { get; private set; }

    /// <summary>
    /// 最近一帧 drain 的 UI 事件数。
    /// </summary>
    public int LastDrainedEventCount { get; private set; }

    /// <summary>
    /// 累计 drain 的 UI 事件数。
    /// </summary>
    public long TotalDrainedEventCount { get; private set; }

    /// <summary>
    /// 将游戏 UI 更新入口注册到相位管线。
    /// </summary>
    /// <param name="phases">Hosting 相位管线。</param>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.RegisterPausedSafe(EnginePhase.GameLogicAndScripts, RunUi);
    }

    private void RunUi(EngineTickContext context)
    {
        if (_runtimePolicy is not null && !_runtimePolicy.AllowsGameUiComposition)
        {
            LastDeltaSeconds = 0f;
            LastDrainedEventCount = 0;
            return;
        }

        // 游戏 UI 使用渲染帧 dt（非 sim tick dt），与动画/交互节奏对齐墙钟帧率。
        float deltaSeconds = ResolveRenderDeltaSeconds(context);
        LastDeltaSeconds = deltaSeconds;
        long started = Stopwatch.GetTimestamp();
        // 先推送脚本/服务层模型，再 Update 后端并 drain 本帧 UI 事件到桥接层。
        _modelPusher?.PushGameUiModels();
        if (_registry is null)
        {
            _host!.Update(deltaSeconds);
            LastDrainedEventCount = _host.DrainEvents(_eventBuffer);
            TotalDrainedEventCount += LastDrainedEventCount;
            RecordSub(context.Context.Profiler, started);
            if (LastDrainedEventCount > 0)
            {
                _eventSink?.OnGameUiEvents(_eventBuffer.AsSpan(0, LastDrainedEventCount));
            }

            return;
        }

        _registry.Update(deltaSeconds);
        int drainedThisFrame = 0;
        for (int canvasIndex = 0; canvasIndex < _registry.Count; canvasIndex++)
        {
            int drained = _registry.DrainEventsAt(canvasIndex, _eventBuffer, out ScriptUi.UiCanvasHandle canvas);
            drainedThisFrame += drained;
            if (drained > 0)
            {
                _eventSink?.OnGameUiEvents(canvas, _eventBuffer.AsSpan(0, drained));
            }
        }

        LastDrainedEventCount = drainedThisFrame;
        TotalDrainedEventCount += drainedThisFrame;
        RecordSub(context.Context.Profiler, started);
    }

    private static void RecordSub(FrameProfiler profiler, long started)
    {
        long elapsed = Stopwatch.GetTimestamp() - started;
        profiler.RecordSub(FrameSubPhase.UiUpdate, elapsed * 1000.0 / Stopwatch.Frequency);
    }

    private static float ResolveRenderDeltaSeconds(EngineTickContext context)
    {
        double delta = context.Timing.RealDeltaSeconds > 0
            ? context.Timing.RealDeltaSeconds
            : context.Timing.Dt;
        return !double.IsFinite(delta) || delta <= 0
            ? 0f
            : (float)delta;
    }
}

/// <summary>
/// 游戏 UI 事件 drain 接收器。
/// </summary>
public interface IGameUiEventSink
{
    /// <summary>
    /// 接收本帧 drain 出来的 UI 事件。
    /// </summary>
    /// <param name="events">本帧 UI 事件。</param>
    void OnGameUiEvents(ReadOnlySpan<UiEvent> events);

    /// <summary>
    /// 接收指定 Canvas 本帧 drain 出来的 UI 事件。旧单 Canvas sink 自动转发到兼容入口。
    /// </summary>
    /// <param name="canvas">来源 Canvas 运行时句柄。</param>
    /// <param name="events">该 Canvas 的本帧 UI 事件。</param>
    void OnGameUiEvents(ScriptUi.UiCanvasHandle canvas, ReadOnlySpan<UiEvent> events)
    {
        _ = canvas;
        OnGameUiEvents(events);
    }
}
