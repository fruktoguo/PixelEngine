using System.Diagnostics;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Rendering;
using PixelEngine.Scripting;

namespace PixelEngine.Editor;

/// <summary>
/// 将 Editor 挂到 Rendering 的显式 present UI 层，确保 ImGui 复用同一个 OpenGL context。
/// </summary>
public sealed class EditorRenderBridge : IUiPresentLayer, IDisposable
{
    private readonly RenderPipeline _pipeline;
    private readonly EditorApp _editor;
    private readonly EngineCounters _counters;
    private readonly FrameProfiler? _profiler;
    private readonly Func<EditorRuntimeDiagnostics>? _runtimeDiagnostics;
    private readonly IScriptRuntime? _legacyScriptRuntime;
    private readonly IDisposable _registration;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _previousSeconds;
    private bool _disposed;

    private EditorRenderBridge(
        RenderPipeline pipeline,
        EditorApp editor,
        EngineCounters counters,
        FrameProfiler? profiler,
        Func<EditorRuntimeDiagnostics>? runtimeDiagnostics,
        IScriptRuntime? legacyScriptRuntime)
    {
        _pipeline = pipeline;
        _editor = editor;
        _counters = counters;
        _profiler = profiler;
        _runtimeDiagnostics = runtimeDiagnostics;
        _legacyScriptRuntime = legacyScriptRuntime;
        _previousSeconds = _clock.Elapsed.TotalSeconds;
        _registration = _pipeline.RegisterUiLayer(
            UiPresentSurface.WindowFramebuffer,
            UiPresentLayerOrders.Editor,
            this);
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
        return AttachIfEnabled(pipeline, editor, counters, null, null);
    }

    /// <summary>
    /// 若 Editor 启用，则绑定到渲染管线，并把 profiler/运行态快照传给 HUD。
    /// </summary>
    public static EditorRenderBridge? AttachIfEnabled(
        RenderPipeline pipeline,
        EditorApp editor,
        EngineCounters counters,
        FrameProfiler? profiler,
        Func<EditorRuntimeDiagnostics>? runtimeDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(counters);
        return editor.Options.Enabled
            ? new EditorRenderBridge(pipeline, editor, counters, profiler, runtimeDiagnostics, legacyScriptRuntime: null)
            : null;
    }

    /// <summary>
    /// 兼容旧调用方的绑定重载，并保持在 Editor ImGui frame 内调度
    /// <paramref name="scriptRuntime" /> GUI 的既有行为。新宿主应改用独立 runtime surface owner。
    /// </summary>
    /// <param name="pipeline">Rendering 管线。</param>
    /// <param name="editor">Editor 门面。</param>
    /// <param name="counters">引擎计数器。</param>
    /// <param name="profiler">可选 profiler。</param>
    /// <param name="runtimeDiagnostics">可选运行时诊断。</param>
    /// <param name="scriptRuntime">可选旧式脚本 GUI 运行时。</param>
    /// <returns>已绑定桥接器；Editor 禁用时为 null。</returns>
    public static EditorRenderBridge? AttachIfEnabled(
        RenderPipeline pipeline,
        EditorApp editor,
        EngineCounters counters,
        FrameProfiler? profiler,
        Func<EditorRuntimeDiagnostics>? runtimeDiagnostics,
        IScriptRuntime? scriptRuntime)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(counters);
        return editor.Options.Enabled
            ? new EditorRenderBridge(pipeline, editor, counters, profiler, runtimeDiagnostics, scriptRuntime)
            : null;
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

        _registration.Dispose();
        _disposed = true;
    }

    /// <inheritdoc />
    public void Present(in UiPresentContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context.Gl);
        if (!_editor.IsRunning)
        {
            _editor.Initialize();
        }

        double now = _clock.Elapsed.TotalSeconds;
        float deltaSeconds = (float)Math.Max(0.0, now - _previousSeconds);
        _previousSeconds = now;
        // 新 Hosting 只使用不带 scriptRuntime 的重载，因此 Runtime OnGui 由 GuiRenderBridge 唯一调度。
        // legacy overload 仍保留旧的 Editor-context 调度语义，避免已编译外部扩展静默失效。
        EditorPerformanceSnapshot performance = EditorPerformanceSnapshot.Create(
            _counters,
            _profiler,
            _runtimeDiagnostics?.Invoke() ?? EditorRuntimeDiagnostics.FullQuality,
            _pipeline);
        _editor.DrawFrame(
            deltaSeconds,
            context.LogicalWidth,
            context.LogicalHeight,
            _counters,
            ++FrameIndex,
            performance,
            _legacyScriptRuntime is null ? null : _legacyScriptRuntime.DrawGui,
            context.FramebufferScaleX,
            context.FramebufferScaleY);
    }
}
