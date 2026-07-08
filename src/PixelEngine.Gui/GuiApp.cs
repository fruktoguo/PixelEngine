using PixelEngine.Scripting;

namespace PixelEngine.Gui;

/// <summary>
/// 中性 GUI 顶层门面，负责 ImGui 帧生命周期与脚本 GUI 调度。
/// </summary>
public sealed class GuiApp : IDisposable
{
    private readonly GuiController _controller;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// 创建 GUI 门面。
    /// </summary>
    public GuiApp(IGuiImGuiBackend backend, GuiAppOptions options)
        : this(new GuiController(backend, options))
    {
    }

    /// <summary>
    /// 创建 GUI 门面。
    /// </summary>
    public GuiApp(GuiController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Options = _controller.Options;
        Input = new GuiInputBridge(_controller.Backend);
    }

    /// <summary>
    /// 当前选项。
    /// </summary>
    public GuiAppOptions Options { get; }

    /// <summary>
    /// 输入桥。
    /// </summary>
    public GuiInputBridge Input { get; }

    /// <summary>
    /// GUI 是否已初始化且启用。
    /// </summary>
    public bool IsRunning => _initialized && Options.Enabled;

    /// <summary>
    /// 初始化 GUI。禁用时不触碰 ImGui 后端。
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
    /// 绘制一帧 GUI，并在同一 ImGui frame 内调度脚本 GUI。
    /// </summary>
    /// <param name="deltaSeconds">距离上一帧的真实秒数。</param>
    /// <param name="width">平台窗口逻辑宽度。</param>
    /// <param name="height">平台窗口逻辑高度。</param>
    /// <param name="drawScriptGui">脚本 GUI 绘制回调。</param>
    /// <param name="framebufferScaleX">逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。</param>
    /// <param name="framebufferScaleY">逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。</param>
    public void DrawFrame(
        float deltaSeconds,
        int width,
        int height,
        Action<IGuiContext>? drawScriptGui,
        float framebufferScaleX = 1f,
        float framebufferScaleY = 1f)
    {
        DrawCombinedFrame(deltaSeconds, width, height, drawManagedGui: null, drawScriptGui, framebufferScaleX, framebufferScaleY);
    }

    /// <summary>
    /// 绘制一帧 GUI，并在同一 ImGui frame 内调度中性 GUI 绘制回调。
    /// </summary>
    /// <param name="deltaSeconds">距离上一帧的真实秒数。</param>
    /// <param name="width">平台窗口逻辑宽度。</param>
    /// <param name="height">平台窗口逻辑高度。</param>
    /// <param name="drawGui">中性 GUI 绘制回调。</param>
    /// <param name="framebufferScaleX">逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。</param>
    /// <param name="framebufferScaleY">逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。</param>
    public void DrawManagedFrame(
        float deltaSeconds,
        int width,
        int height,
        Action<IGuiDrawContext>? drawGui,
        float framebufferScaleX = 1f,
        float framebufferScaleY = 1f)
    {
        DrawCombinedFrame(deltaSeconds, width, height, drawGui, drawScriptGui: null, framebufferScaleX, framebufferScaleY);
    }

    /// <summary>
    /// 绘制一帧 GUI，并按固定顺序在同一个 ImGui frame 内调度 Managed UI 与脚本 GUI。
    /// </summary>
    /// <param name="deltaSeconds">距离上一帧的真实秒数。</param>
    /// <param name="width">平台窗口逻辑宽度。</param>
    /// <param name="height">平台窗口逻辑高度。</param>
    /// <param name="drawManagedGui">Managed UI 绘制回调。</param>
    /// <param name="drawScriptGui">脚本 GUI 绘制回调。</param>
    /// <param name="framebufferScaleX">逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。</param>
    /// <param name="framebufferScaleY">逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。</param>
    public void DrawCombinedFrame(
        float deltaSeconds,
        int width,
        int height,
        Action<IGuiDrawContext>? drawManagedGui,
        Action<IGuiContext>? drawScriptGui,
        float framebufferScaleX = 1f,
        float framebufferScaleY = 1f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsRunning)
        {
            return;
        }

        _controller.NewFrame(deltaSeconds, width, height, framebufferScaleX, framebufferScaleY);
        ScriptGuiContext gui = new(width, height, deltaSeconds, Input.Capture);
        drawManagedGui?.Invoke(gui);
        if (drawScriptGui is not null)
        {
            drawScriptGui(gui);
        }

        _controller.Render();
    }

    /// <summary>
    /// 关闭当前 GUI backend/context，但保留门面实例以便之后重新 Initialize。
    /// </summary>
    public void Shutdown()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            _controller.Shutdown();
            _initialized = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Shutdown();
        _disposed = true;
    }
}
