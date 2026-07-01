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
    /// <param name="width">framebuffer 宽度。</param>
    /// <param name="height">framebuffer 高度。</param>
    public void NewFrame(float deltaSeconds, int width, int height)
    {
        ThrowIfNotInitialized();
        Backend.NewFrame(deltaSeconds, width, height);
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
    /// 渲染 ImGui draw data。Hexa.NET OpenGL3 backend 负责恢复被它修改的 GL 状态。
    /// </summary>
    public void Render()
    {
        ThrowIfNotInitialized();
        Backend.Render();
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
