using PixelEngine.Core.Diagnostics;

namespace PixelEngine.Editor;

/// <summary>
/// Editor 顶层门面，负责 ImGui 帧生命周期、dockspace 与面板调度。
/// </summary>
public sealed class EditorApp : IDisposable
{
    private readonly ImGuiController _controller;
    private readonly List<IEditorPanel> _panels = [];
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// 创建 Editor 门面。
    /// </summary>
    /// <param name="backend">ImGui 后端。</param>
    /// <param name="options">Editor 选项。</param>
    public EditorApp(IEditorImGuiBackend backend, EditorAppOptions options)
        : this(new ImGuiController(backend, options))
    {
    }

    /// <summary>
    /// 创建 Editor 门面。
    /// </summary>
    /// <param name="controller">ImGui 控制器。</param>
    public EditorApp(ImGuiController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Options = _controller.Options;
        Selection = new EditorSelection();
        Input = new ImGuiInputBridge(_controller.Backend);
    }

    /// <summary>
    /// 当前选项。
    /// </summary>
    public EditorAppOptions Options { get; }

    /// <summary>
    /// 共享选择态。
    /// </summary>
    public EditorSelection Selection { get; }

    /// <summary>
    /// 输入桥。
    /// </summary>
    public ImGuiInputBridge Input { get; }

    /// <summary>
    /// 已注册面板数量。
    /// </summary>
    public int PanelCount => _panels.Count;

    /// <summary>
    /// Editor 是否已初始化且启用。
    /// </summary>
    public bool IsRunning => _initialized && Options.Enabled;

    /// <summary>
    /// 注册一个面板。
    /// </summary>
    /// <param name="panel">面板实例。</param>
    public void AddPanel(IEditorPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        _panels.Add(panel);
    }

    /// <summary>
    /// 初始化 Editor。禁用时不触碰 ImGui 后端。
    /// </summary>
    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized || !Options.Enabled)
        {
            return;
        }

        _controller.Initialize();
        _initialized = true;
    }

    /// <summary>
    /// 绘制一帧 Editor UI。
    /// </summary>
    /// <param name="deltaSeconds">帧间隔秒数。</param>
    /// <param name="width">framebuffer 宽度。</param>
    /// <param name="height">framebuffer 高度。</param>
    /// <param name="counters">诊断计数器。</param>
    /// <param name="frameIndex">当前帧索引。</param>
    public void DrawFrame(float deltaSeconds, int width, int height, EngineCounters counters, long frameIndex)
    {
        DrawFrame(deltaSeconds, width, height, counters, frameIndex, EditorPerformanceSnapshot.FromCounters(counters));
    }

    /// <summary>
    /// 绘制一帧 Editor UI。
    /// </summary>
    /// <param name="deltaSeconds">帧间隔秒数。</param>
    /// <param name="width">framebuffer 宽度。</param>
    /// <param name="height">framebuffer 高度。</param>
    /// <param name="counters">诊断计数器。</param>
    /// <param name="frameIndex">当前帧索引。</param>
    /// <param name="performance">性能 HUD 只读诊断快照。</param>
    public void DrawFrame(
        float deltaSeconds,
        int width,
        int height,
        EngineCounters counters,
        long frameIndex,
        EditorPerformanceSnapshot performance)
    {
        ArgumentNullException.ThrowIfNull(counters);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsRunning)
        {
            return;
        }

        _controller.NewFrame(deltaSeconds, width, height);
        _controller.DrawDockSpace();
        EditorContext context = new(counters, Selection, frameIndex, performance);
        for (int i = 0; i < _panels.Count; i++)
        {
            IEditorPanel panel = _panels[i];
            if (panel.Visible)
            {
                panel.Draw(in context);
            }
        }

        _controller.Render();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_initialized)
        {
            _controller.Shutdown();
            _initialized = false;
        }

        _disposed = true;
    }
}
