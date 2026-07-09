using PixelEngine.Core.Diagnostics;
using PixelEngine.Scripting;

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
    /// 将指定标题的已注册面板设为可见。
    /// </summary>
    /// <param name="title">面板标题。</param>
    /// <returns>找到并打开面板时为 true。</returns>
    public bool TryShowPanel(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        for (int i = 0; i < _panels.Count; i++)
        {
            IEditorPanel panel = _panels[i];
            if (string.Equals(panel.Title, title, StringComparison.Ordinal))
            {
                panel.Visible = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 将当前 dockspace 中注册的所有面板设为可见。
    /// </summary>
    /// <returns>被打开的面板数量。</returns>
    public int ShowAllPanels()
    {
        int count = 0;
        for (int i = 0; i < _panels.Count; i++)
        {
            if (!_panels[i].Visible)
            {
                _panels[i].Visible = true;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// 重置当前 Editor dockspace 布局并显示所有已注册面板。
    /// </summary>
    public void ResetDockLayout()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized && Options.EnableDockSpace)
        {
            _controller.ResetDockLayout();
        }

        _ = ShowAllPanels();
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
    /// <param name="width">平台窗口逻辑宽度。</param>
    /// <param name="height">平台窗口逻辑高度。</param>
    /// <param name="counters">诊断计数器。</param>
    /// <param name="frameIndex">当前帧索引。</param>
    /// <param name="framebufferScaleX">逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。</param>
    /// <param name="framebufferScaleY">逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。</param>
    public void DrawFrame(
        float deltaSeconds,
        int width,
        int height,
        EngineCounters counters,
        long frameIndex,
        float framebufferScaleX = 1f,
        float framebufferScaleY = 1f)
    {
        DrawFrame(
            deltaSeconds,
            width,
            height,
            counters,
            frameIndex,
            EditorPerformanceSnapshot.FromCounters(counters),
            framebufferScaleX,
            framebufferScaleY);
    }

    /// <summary>
    /// 绘制一帧 Editor UI。
    /// </summary>
    /// <param name="deltaSeconds">帧间隔秒数。</param>
    /// <param name="width">平台窗口逻辑宽度。</param>
    /// <param name="height">平台窗口逻辑高度。</param>
    /// <param name="counters">诊断计数器。</param>
    /// <param name="frameIndex">当前帧索引。</param>
    /// <param name="performance">性能 HUD 只读诊断快照。</param>
    /// <param name="framebufferScaleX">逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。</param>
    /// <param name="framebufferScaleY">逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。</param>
    public void DrawFrame(
        float deltaSeconds,
        int width,
        int height,
        EngineCounters counters,
        long frameIndex,
        EditorPerformanceSnapshot performance,
        float framebufferScaleX = 1f,
        float framebufferScaleY = 1f)
    {
        ArgumentNullException.ThrowIfNull(counters);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsRunning)
        {
            return;
        }

        _controller.NewFrame(deltaSeconds, width, height, framebufferScaleX, framebufferScaleY);
        // ImGui 帧内顺序：DockSpace 宿主 → 各可见面板 Draw → Render 提交 draw data。
        if (Options.EnableDockSpace)
        {
            _controller.DrawDockSpace();
        }

        EditorContext context = new(counters, Selection, frameIndex, performance);
        if (Options.EnableDockSpace)
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                IEditorPanel panel = _panels[i];
                if (panel.Visible)
                {
                    panel.Draw(in context);
                }
            }
        }

        _controller.Render();
    }

    /// <summary>
    /// 绘制一帧 Editor UI，并在同一 ImGui frame 内调度脚本 GUI。
    /// </summary>
    public void DrawFrame(
        float deltaSeconds,
        int width,
        int height,
        EngineCounters counters,
        long frameIndex,
        EditorPerformanceSnapshot performance,
        Action<IGuiContext>? drawScriptGui,
        float framebufferScaleX = 1f,
        float framebufferScaleY = 1f)
    {
        ArgumentNullException.ThrowIfNull(counters);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsRunning)
        {
            return;
        }

        _controller.NewFrame(deltaSeconds, width, height, framebufferScaleX, framebufferScaleY);
        if (Options.EnableDockSpace)
        {
            _controller.DrawDockSpace();
        }

        EditorContext context = new(counters, Selection, frameIndex, performance);
        if (Options.EnableDockSpace)
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                IEditorPanel panel = _panels[i];
                if (panel.Visible)
                {
                    panel.Draw(in context);
                }
            }
        }

        if (drawScriptGui is not null)
        {
            ScriptGuiContext gui = new(width, height, deltaSeconds, Input.Capture);
            drawScriptGui(gui);
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
