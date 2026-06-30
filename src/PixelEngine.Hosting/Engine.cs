using System.Diagnostics;
using PixelEngine.Core.Time;

namespace PixelEngine.Hosting;

/// <summary>
/// PixelEngine 运行时门面，拥有 EngineContext 并控制生命周期。
/// </summary>
public sealed class Engine : IDisposable
{
    private bool _disposed;

    internal Engine(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        State = EngineRunState.Created;
    }

    /// <summary>
    /// 当前运行上下文。
    /// </summary>
    public EngineContext Context { get; }

    /// <summary>
    /// 当前生命周期状态。
    /// </summary>
    public EngineRunState State { get; private set; }

    /// <summary>
    /// 持续运行直到收到取消请求或 Engine 被关闭。
    /// </summary>
    public void Run(CancellationToken cancellationToken = default)
    {
        ThrowIfShutdown();
        Stopwatch stopwatch = Stopwatch.StartNew();
        double previousSeconds = stopwatch.Elapsed.TotalSeconds;
        while (!cancellationToken.IsCancellationRequested && State != EngineRunState.Shutdown)
        {
            double now = stopwatch.Elapsed.TotalSeconds;
            _ = RunOneTick(now - previousSeconds);
            previousSeconds = now;
        }
    }

    /// <summary>
    /// 执行一个运行时 tick 并推进固定步长帧时钟。
    /// </summary>
    public FrameTiming RunOneTick(double realDeltaSeconds = 0)
    {
        ThrowIfShutdown();
        State = EngineRunState.Running;
        FrameTiming timing = Context.Clock.BeginFrame(realDeltaSeconds);
        Context.Counters.SimHz = Context.Clock.SimHz;
        return timing;
    }

    /// <summary>
    /// 关闭引擎并按生命周期顺序释放已装配资源。
    /// </summary>
    public void Shutdown()
    {
        if (State == EngineRunState.Shutdown)
        {
            return;
        }

        Context.Jobs.Dispose();
        State = EngineRunState.Shutdown;
    }

    /// <summary>
    /// 释放引擎资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Shutdown();
        _disposed = true;
    }

    private void ThrowIfShutdown()
    {
        ObjectDisposedException.ThrowIf(State == EngineRunState.Shutdown, this);
    }
}
