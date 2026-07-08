using System.Diagnostics;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// 将游戏大 UI 宿主注册为渲染管线的显式 UI present 层。
/// </summary>
public sealed class UiLayerCompositor : IUiPresentLayer, IDisposable
{
    private readonly GameUiHost _host;
    private readonly IUiPresentTargetProvider? _targetProvider;
    private readonly IDisposable _registration;
    private bool _disposed;

    private UiLayerCompositor(RenderPipeline pipeline, GameUiHost host, IUiPresentTargetProvider? targetProvider)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _targetProvider = targetProvider;
        _registration = pipeline.RegisterUiLayer(UiPresentLayerOrders.Game, this);
    }

    /// <summary>
    /// 使用游戏 UI 固定层级注册合成层。
    /// </summary>
    /// <param name="pipeline">目标渲染管线。</param>
    /// <param name="host">游戏 UI 宿主。</param>
    /// <returns>已注册的合成层。</returns>
    public static UiLayerCompositor Attach(RenderPipeline pipeline, GameUiHost host)
    {
        return new UiLayerCompositor(pipeline, host, targetProvider: null);
    }

    /// <summary>
    /// 使用游戏 UI 固定层级注册合成层，并允许外部宿主覆盖 present 目标区域。
    /// </summary>
    /// <param name="pipeline">目标渲染管线。</param>
    /// <param name="host">游戏 UI 宿主。</param>
    /// <param name="targetProvider">可选目标区域提供者。</param>
    /// <returns>已注册的合成层。</returns>
    public static UiLayerCompositor Attach(
        RenderPipeline pipeline,
        GameUiHost host,
        IUiPresentTargetProvider? targetProvider)
    {
        return new UiLayerCompositor(pipeline, host, targetProvider);
    }

    /// <summary>
    /// present 层被调用的帧数。
    /// </summary>
    public long FrameIndex { get; private set; }

    /// <summary>
    /// 在渲染管线 UI present 阶段合成游戏大 UI。
    /// </summary>
    /// <param name="context">UI present 上下文。</param>
    public void Present(in UiPresentContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context.Gl);
        long started = Stopwatch.GetTimestamp();
        UiPresentContext presentContext = _targetProvider is not null &&
            _targetProvider.TryGetPresentTarget(out UiPresentTarget target)
                ? context.WithTarget(target)
                : context;
        _host.Composite(in presentContext);
        RecordSub(context.Profiler, started);
        FrameIndex++;
    }

    private static void RecordSub(FrameProfiler? profiler, long started)
    {
        if (profiler is null)
        {
            return;
        }

        long elapsed = Stopwatch.GetTimestamp() - started;
        profiler.RecordSub(FrameSubPhase.UiComposite, elapsed * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>
    /// 注销渲染管线 UI 层。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _registration.Dispose();
        _disposed = true;
    }
}
