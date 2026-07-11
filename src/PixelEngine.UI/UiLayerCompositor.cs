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
    private UiPresentTarget _lastPresentTarget;
    private bool _hasPresentTarget;
    private bool _disposed;

    private UiLayerCompositor(
        RenderPipeline pipeline,
        UiPresentSurface surface,
        GameUiHost host,
        IUiPresentTargetProvider? targetProvider)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _targetProvider = targetProvider;
        _registration = pipeline.RegisterUiLayer(surface, UiPresentLayerOrders.Game, this);
    }

    /// <summary>
    /// 使用游戏 UI 固定层级注册合成层。
    /// </summary>
    /// <param name="pipeline">目标渲染管线。</param>
    /// <param name="host">游戏 UI 宿主。</param>
    /// <returns>已注册的合成层。</returns>
    public static UiLayerCompositor Attach(RenderPipeline pipeline, GameUiHost host)
    {
        return new UiLayerCompositor(
            pipeline,
            UiPresentSurface.WindowFramebuffer,
            host,
            targetProvider: null);
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
        return new UiLayerCompositor(
            pipeline,
            UiPresentSurface.WindowFramebuffer,
            host,
            targetProvider);
    }

    /// <summary>
    /// 在显式 present surface 注册游戏 UI 合成层。
    /// </summary>
    /// <param name="pipeline">目标渲染管线。</param>
    /// <param name="surface">目标 present surface。</param>
    /// <param name="host">游戏 UI 宿主。</param>
    /// <returns>已注册的合成层。</returns>
    public static UiLayerCompositor Attach(
        RenderPipeline pipeline,
        UiPresentSurface surface,
        GameUiHost host)
    {
        return new UiLayerCompositor(pipeline, surface, host, targetProvider: null);
    }

    /// <summary>
    /// 在显式 present surface 注册游戏 UI 合成层，并允许覆盖该 surface 内的目标区域。
    /// </summary>
    /// <param name="pipeline">目标渲染管线。</param>
    /// <param name="surface">目标 present surface。</param>
    /// <param name="host">游戏 UI 宿主。</param>
    /// <param name="targetProvider">可选目标区域提供者；坐标属于所选 surface。</param>
    /// <returns>已注册的合成层。</returns>
    public static UiLayerCompositor Attach(
        RenderPipeline pipeline,
        UiPresentSurface surface,
        GameUiHost host,
        IUiPresentTargetProvider? targetProvider)
    {
        return new UiLayerCompositor(pipeline, surface, host, targetProvider);
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
        // 可选 target 覆盖：GameView 等宿主可把 UI 合成到子视口而非全屏 framebuffer。
        UiPresentContext presentContext = _targetProvider is not null &&
            _targetProvider.TryGetPresentTarget(out UiPresentTarget target)
                ? context.WithTarget(target)
                : context;
        if (!_hasPresentTarget || presentContext.Target != _lastPresentTarget)
        {
            UiPresentTarget presentTarget = presentContext.Target;
            _host.Resize(new UiViewport(0, 0, presentTarget.Width, presentTarget.Height, presentTarget.DpiScale));
            _lastPresentTarget = presentTarget;
            _hasPresentTarget = true;
        }

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
