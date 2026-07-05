using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// 将游戏大 UI 逻辑更新绑定到 Hosting 相位 1，使用渲染帧 dt 而不是 sim tick dt。
/// </summary>
public sealed class GameUiPhaseDriver : IEnginePhaseDriver
{
    private readonly GameUiHost _host;
    private readonly IGameUiEventSink? _eventSink;
    private readonly UiEvent[] _eventBuffer;

    /// <summary>
    /// 创建游戏 UI 相位驱动。
    /// </summary>
    /// <param name="host">游戏 UI 宿主。</param>
    /// <param name="eventCapacity">单帧事件 drain 缓冲容量。</param>
    /// <param name="eventSink">可选事件接收器。</param>
    public GameUiPhaseDriver(GameUiHost host, int eventCapacity = 128, IGameUiEventSink? eventSink = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventCapacity);
        _eventSink = eventSink;
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
        phases.Register(EnginePhase.GameLogicAndScripts, RunUi);
    }

    private void RunUi(EngineTickContext context)
    {
        float deltaSeconds = ResolveRenderDeltaSeconds(context);
        LastDeltaSeconds = deltaSeconds;
        _host.Update(deltaSeconds);
        LastDrainedEventCount = _host.DrainEvents(_eventBuffer);
        TotalDrainedEventCount += LastDrainedEventCount;
        if (LastDrainedEventCount > 0)
        {
            _eventSink?.OnGameUiEvents(_eventBuffer.AsSpan(0, LastDrainedEventCount));
        }
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
}
