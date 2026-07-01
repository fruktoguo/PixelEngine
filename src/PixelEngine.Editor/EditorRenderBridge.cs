using System.Diagnostics;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Rendering;
using Silk.NET.OpenGL;

namespace PixelEngine.Editor;

/// <summary>
/// 将 Editor 挂到 Rendering 的 present 前 UI hook，确保 ImGui 复用同一个 OpenGL context。
/// </summary>
public sealed class EditorRenderBridge : IDisposable
{
    private readonly RenderPipeline _pipeline;
    private readonly EditorApp _editor;
    private readonly EngineCounters _counters;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _previousSeconds;
    private bool _disposed;

    private EditorRenderBridge(RenderPipeline pipeline, EditorApp editor, EngineCounters counters)
    {
        _pipeline = pipeline;
        _editor = editor;
        _counters = counters;
        _previousSeconds = _clock.Elapsed.TotalSeconds;
        _pipeline.BeforePresentUi += OnBeforePresentUi;
    }

    /// <summary>
    /// 若 Editor 启用，则绑定到渲染管线；禁用时返回 null 且不订阅 hook。
    /// </summary>
    /// <param name="pipeline">渲染管线。</param>
    /// <param name="editor">Editor 门面。</param>
    /// <param name="counters">诊断计数器。</param>
    /// <returns>已绑定桥接器；禁用时为 null。</returns>
    public static EditorRenderBridge? AttachIfEnabled(RenderPipeline pipeline, EditorApp editor, EngineCounters counters)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(counters);
        return editor.Options.Enabled ? new EditorRenderBridge(pipeline, editor, counters) : null;
    }

    /// <summary>
    /// 已绘制的 Editor 帧数。
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
        if (!_editor.IsRunning)
        {
            _editor.Initialize();
        }

        double now = _clock.Elapsed.TotalSeconds;
        float deltaSeconds = (float)Math.Max(0.0, now - _previousSeconds);
        _previousSeconds = now;
        _editor.DrawFrame(deltaSeconds, _pipeline.Width, _pipeline.Height, _counters, ++FrameIndex);
    }
}
