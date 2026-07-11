using System.Diagnostics;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Rendering;
using PixelEngine.Scripting;

namespace PixelEngine.Gui;

/// <summary>
/// 将中性 GUI 宿主挂到 Rendering 的显式 present UI 层。
/// </summary>
public sealed class GuiRenderBridge : IUiPresentLayer, IDisposable
{
    private readonly RenderPipeline _pipeline;
    private readonly GuiApp _gui;
    private readonly Action<IGuiContext>? _scriptGui;
    private readonly Action<IGuiDrawContext>? _managedGui;
    private readonly Action<UiPresentTarget>? _presentTargetChanged;
    private readonly IDisposable _registration;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _previousSeconds;
    private UiPresentTarget _lastPresentTarget;
    private bool _hasPresentTarget;
    private bool _disposed;

    private GuiRenderBridge(
        RenderPipeline pipeline,
        UiPresentSurface surface,
        GuiApp gui,
        IScriptRuntime? scriptRuntime,
        Action<IGuiDrawContext>? managedGui,
        Action<UiPresentTarget>? presentTargetChanged)
    {
        _pipeline = pipeline;
        _gui = gui;
        _scriptGui = scriptRuntime is null ? null : scriptRuntime.DrawGui;
        _managedGui = managedGui;
        _presentTargetChanged = presentTargetChanged;
        _previousSeconds = _clock.Elapsed.TotalSeconds;
        _registration = _pipeline.RegisterUiLayer(surface, UiPresentLayerOrders.Game, this);
    }

    /// <summary>
    /// 若 GUI host 启用，则绑定到渲染管线，并可调度脚本 GUI。
    /// </summary>
    public static GuiRenderBridge? AttachIfEnabled(RenderPipeline pipeline, GuiApp gui, IScriptRuntime? scriptRuntime)
    {
        return AttachIfEnabled(pipeline, gui, scriptRuntime, managedGui: null, presentTargetChanged: null);
    }

    /// <summary>
    /// 若 GUI host 启用，则绑定到渲染管线，并可在同一 ImGui frame 中调度 Managed UI 与脚本 GUI。
    /// </summary>
    public static GuiRenderBridge? AttachIfEnabled(
        RenderPipeline pipeline,
        GuiApp gui,
        IScriptRuntime? scriptRuntime,
        Action<IGuiDrawContext>? managedGui)
    {
        return AttachIfEnabled(pipeline, gui, scriptRuntime, managedGui, presentTargetChanged: null);
    }

    /// <summary>
    /// 若 GUI host 启用，则绑定到渲染管线；present 目标尺寸变化时通知共享的 Managed Game UI 后端同步 viewport。
    /// </summary>
    /// <param name="pipeline">目标渲染管线。</param>
    /// <param name="gui">runtime GUI 宿主。</param>
    /// <param name="scriptRuntime">可选脚本运行时；其 OnGui 每个 runtime present frame 仅调用一次。</param>
    /// <param name="managedGui">可选 Managed Game UI 绘制回调。</param>
    /// <param name="presentTargetChanged">runtime present target 首次可用或尺寸变化时的通知。</param>
    /// <returns>已绑定桥接器；GUI 禁用时返回 null。</returns>
    public static GuiRenderBridge? AttachIfEnabled(
        RenderPipeline pipeline,
        GuiApp gui,
        IScriptRuntime? scriptRuntime,
        Action<IGuiDrawContext>? managedGui,
        Action<UiPresentTarget>? presentTargetChanged)
    {
        return AttachIfEnabled(
            pipeline,
            UiPresentSurface.WindowFramebuffer,
            gui,
            scriptRuntime,
            managedGui,
            presentTargetChanged);
    }

    /// <summary>
    /// 若 GUI host 启用，则绑定到显式 present surface。引擎 runtime GUI 应选择 RuntimeViewport。
    /// </summary>
    /// <param name="pipeline">目标渲染管线。</param>
    /// <param name="surface">目标 present surface。</param>
    /// <param name="gui">GUI 宿主。</param>
    /// <param name="scriptRuntime">可选脚本运行时。</param>
    /// <param name="managedGui">可选 Managed UI 绘制回调。</param>
    /// <param name="presentTargetChanged">present target 变化通知。</param>
    /// <returns>已绑定桥接器；GUI 禁用时返回 null。</returns>
    public static GuiRenderBridge? AttachIfEnabled(
        RenderPipeline pipeline,
        UiPresentSurface surface,
        GuiApp gui,
        IScriptRuntime? scriptRuntime,
        Action<IGuiDrawContext>? managedGui,
        Action<UiPresentTarget>? presentTargetChanged)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(gui);
        return gui.Options.Enabled
            ? new GuiRenderBridge(pipeline, surface, gui, scriptRuntime, managedGui, presentTargetChanged)
            : null;
    }

    /// <summary>
    /// 已绘制的 GUI 帧数。
    /// </summary>
    public long FrameIndex { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _registration.Dispose();
        _disposed = true;
    }

    /// <inheritdoc />
    public void Present(in UiPresentContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context.Gl);
        long started = Stopwatch.GetTimestamp();
        if (!_gui.IsRunning)
        {
            _gui.Initialize();
        }

        if (!_hasPresentTarget || context.Target != _lastPresentTarget)
        {
            _presentTargetChanged?.Invoke(context.Target);
            _lastPresentTarget = context.Target;
            _hasPresentTarget = true;
        }

        // present 相位驱动 GuiApp：Managed UI 与脚本 OnGui 共享同一 ImGui frame 与输入 capture。
        double now = _clock.Elapsed.TotalSeconds;
        float deltaSeconds = (float)Math.Max(0.0, now - _previousSeconds);
        _previousSeconds = now;
        _gui.DrawCombinedFrame(
            deltaSeconds,
            context.LogicalWidth,
            context.LogicalHeight,
            _managedGui,
            _scriptGui,
            context.FramebufferScaleX,
            context.FramebufferScaleY);
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
}
