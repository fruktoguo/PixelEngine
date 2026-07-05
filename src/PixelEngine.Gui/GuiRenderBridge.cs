using System.Diagnostics;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Rendering;
using PixelEngine.Scripting;

namespace PixelEngine.Gui;

/// <summary>
/// 将中性 GUI 宿主挂到 Rendering 的 present 前 UI hook。
/// </summary>
public sealed class GuiRenderBridge : IUiPresentLayer, IDisposable
{
    private readonly RenderPipeline _pipeline;
    private readonly GuiApp _gui;
    private readonly IScriptRuntime? _scriptRuntime;
    private readonly Action<IGuiDrawContext>? _managedGui;
    private readonly IDisposable _registration;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _previousSeconds;
    private bool _disposed;

    private GuiRenderBridge(
        RenderPipeline pipeline,
        GuiApp gui,
        IScriptRuntime? scriptRuntime,
        Action<IGuiDrawContext>? managedGui)
    {
        _pipeline = pipeline;
        _gui = gui;
        _scriptRuntime = scriptRuntime;
        _managedGui = managedGui;
        _previousSeconds = _clock.Elapsed.TotalSeconds;
        _registration = _pipeline.RegisterUiLayer(UiPresentLayerOrders.Game, this);
    }

    /// <summary>
    /// 若 GUI host 启用，则绑定到渲染管线，并可调度脚本 GUI。
    /// </summary>
    public static GuiRenderBridge? AttachIfEnabled(RenderPipeline pipeline, GuiApp gui, IScriptRuntime? scriptRuntime)
    {
        return AttachIfEnabled(pipeline, gui, scriptRuntime, managedGui: null);
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
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(gui);
        return gui.Options.Enabled
            ? new GuiRenderBridge(pipeline, gui, scriptRuntime, managedGui)
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

        double now = _clock.Elapsed.TotalSeconds;
        float deltaSeconds = (float)Math.Max(0.0, now - _previousSeconds);
        _previousSeconds = now;
        _gui.DrawCombinedFrame(
            deltaSeconds,
            _pipeline.Width,
            _pipeline.Height,
            _managedGui,
            _scriptRuntime is null ? null : _scriptRuntime.DrawGui);
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
