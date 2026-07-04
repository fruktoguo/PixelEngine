using System.Diagnostics;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using Silk.NET.OpenGL;

namespace PixelEngine.Gui;

/// <summary>
/// 将中性 GUI 宿主挂到 Rendering 的 present 前 UI hook。
/// </summary>
public sealed class GuiRenderBridge : IDisposable
{
    private readonly RenderPipeline _pipeline;
    private readonly GuiApp _gui;
    private readonly IScriptRuntime? _scriptRuntime;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _previousSeconds;
    private bool _disposed;

    private GuiRenderBridge(RenderPipeline pipeline, GuiApp gui, IScriptRuntime? scriptRuntime)
    {
        _pipeline = pipeline;
        _gui = gui;
        _scriptRuntime = scriptRuntime;
        _previousSeconds = _clock.Elapsed.TotalSeconds;
        _pipeline.BeforePresentUi += OnBeforePresentUi;
    }

    /// <summary>
    /// 若 GUI host 启用，则绑定到渲染管线，并可调度脚本 GUI。
    /// </summary>
    public static GuiRenderBridge? AttachIfEnabled(RenderPipeline pipeline, GuiApp gui, IScriptRuntime? scriptRuntime)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(gui);
        return gui.Options.Enabled
            ? new GuiRenderBridge(pipeline, gui, scriptRuntime)
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

        _pipeline.BeforePresentUi -= OnBeforePresentUi;
        _disposed = true;
    }

    private void OnBeforePresentUi(GL gl)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(gl);
        if (!_gui.IsRunning)
        {
            _gui.Initialize();
        }

        double now = _clock.Elapsed.TotalSeconds;
        float deltaSeconds = (float)Math.Max(0.0, now - _previousSeconds);
        _previousSeconds = now;
        _gui.DrawFrame(
            deltaSeconds,
            _pipeline.Width,
            _pipeline.Height,
            _scriptRuntime is null ? null : _scriptRuntime.DrawGui);
        FrameIndex++;
    }
}
