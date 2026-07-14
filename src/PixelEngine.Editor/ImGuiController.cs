namespace PixelEngine.Editor;

#pragma warning disable IDE0290

/// <summary>
/// ImGui context、OpenGL3 后端与帧绘制生命周期控制器。
/// </summary>
public sealed class ImGuiController
{
    /// <summary>
    /// 创建 ImGui 控制器。
    /// </summary>
    /// <param name="backend">ImGui 后端适配器。</param>
    /// <param name="options">Editor 选项。</param>
    public ImGuiController(IEditorImGuiBackend backend, EditorAppOptions options)
    {
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
        Options = (options ?? throw new ArgumentNullException(nameof(options))).Normalize();
    }

    /// <summary>
    /// 后端适配器。
    /// </summary>
    public IEditorImGuiBackend Backend { get; }

    /// <summary>
    /// Editor 选项。
    /// </summary>
    public EditorAppOptions Options { get; }

    /// <summary>
    /// 是否已经初始化。
    /// </summary>
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// 创建 ImGui context、字体 atlas 与 OpenGL3 backend。
    /// </summary>
    public void Initialize()
    {
        if (IsInitialized || !Options.Enabled)
        {
            return;
        }

        Backend.Initialize(Options);
        IsInitialized = true;
    }

    /// <summary>
    /// 开始一帧 ImGui。
    /// </summary>
    /// <param name="deltaSeconds">帧间隔秒数。</param>
    /// <param name="width">平台窗口逻辑宽度。</param>
    /// <param name="height">平台窗口逻辑高度。</param>
    /// <param name="framebufferScaleX">逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。</param>
    /// <param name="framebufferScaleY">逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。</param>
    public void NewFrame(float deltaSeconds, int width, int height, float framebufferScaleX = 1f, float framebufferScaleY = 1f)
    {
        ThrowIfNotInitialized();
        Backend.NewFrame(deltaSeconds, width, height, framebufferScaleX, framebufferScaleY);
    }

    /// <summary>在下一帧开始前应用 UI 缩放。</summary>
    public void SetUiScale(float scale)
    {
        ThrowIfNotInitialized();
        Backend.SetUiScale(scale);
    }

    /// <summary>
    /// 绘制主 dockspace。
    /// </summary>
    public void DrawDockSpace()
    {
        ThrowIfNotInitialized();
        Backend.DrawDockSpace();
    }

    /// <summary>
    /// 重置当前 dockspace 默认布局。
    /// </summary>
    public void ResetDockLayout()
    {
        ThrowIfNotInitialized();
        Backend.ResetDockLayout();
    }

    /// <summary>捕获当前完整 dock layout 文本。</summary>
    /// <returns>Dear ImGui 当前会话的完整 ini layout 文本。</returns>
    public string CaptureDockLayout()
    {
        ThrowIfNotInitialized();
        return Backend.CaptureDockLayout();
    }

    /// <summary>应用经过宿主校验的完整 dock layout 文本。</summary>
    /// <param name="layout">Dear ImGui ini 文本。</param>
    public void ApplyDockLayout(string layout)
    {
        ThrowIfNotInitialized();
        Backend.ApplyDockLayout(layout);
    }

    /// <summary>读取指定 window 的实时 dock 状态。</summary>
    /// <param name="windowTitle">需要读取的完整稳定窗口标题。</param>
    /// <returns>窗口是否已实例化、当前 dock node 与矩形。</returns>
    public EditorDockWindowState CaptureDockWindow(string windowTitle)
    {
        ThrowIfNotInitialized();
        return Backend.CaptureDockWindow(windowTitle);
    }

    /// <summary>执行指定 window 的语义 dock 变更。</summary>
    /// <param name="request">源窗口、目标窗口、位置与可选浮动矩形。</param>
    /// <param name="diagnostic">失败时的稳定诊断；成功为空。</param>
    /// <returns>变更已由 Dear ImGui docking API 接受时返回 <see langword="true"/>。</returns>
    public bool TrySetDockWindow(EditorDockWindowRequest request, out string diagnostic)
    {
        ThrowIfNotInitialized();
        return Backend.TrySetDockWindow(request, out diagnostic);
    }

    /// <summary>
    /// 渲染 ImGui draw data。Hexa.NET OpenGL3 backend 负责恢复被它修改的 GL 状态。
    /// </summary>
    public void Render()
    {
        ThrowIfNotInitialized();
        Backend.Render();
    }

    /// <summary>
    /// 设置关闭 Editor backend 时是否保存当前布局。
    /// </summary>
    /// <param name="enabled">是否持久化布局。</param>
    public void SetLayoutPersistence(bool enabled)
    {
        Backend.SetLayoutPersistence(enabled);
    }

    /// <summary>
    /// 关闭 backend 并销毁 context。
    /// </summary>
    public void Shutdown()
    {
        if (!IsInitialized)
        {
            return;
        }

        Backend.Shutdown();
        IsInitialized = false;
    }

    private void ThrowIfNotInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("ImGuiController 尚未初始化。");
        }
    }
}

#pragma warning restore IDE0290
