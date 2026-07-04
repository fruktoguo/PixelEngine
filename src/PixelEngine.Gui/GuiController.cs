namespace PixelEngine.Gui;

#pragma warning disable IDE0290

/// <summary>
/// ImGui context、OpenGL3 后端与帧绘制生命周期控制器。
/// </summary>
public sealed class GuiController
{
    /// <summary>
    /// 创建 ImGui 控制器。
    /// </summary>
    public GuiController(IGuiImGuiBackend backend, GuiAppOptions options)
    {
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
        Options = (options ?? throw new ArgumentNullException(nameof(options))).Normalize();
    }

    /// <summary>
    /// 后端适配器。
    /// </summary>
    public IGuiImGuiBackend Backend { get; }

    /// <summary>
    /// GUI 选项。
    /// </summary>
    public GuiAppOptions Options { get; }

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
    public void NewFrame(float deltaSeconds, int width, int height)
    {
        ThrowIfNotInitialized();
        Backend.NewFrame(deltaSeconds, width, height);
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
            throw new InvalidOperationException("GuiController 尚未初始化。");
        }
    }
}

#pragma warning restore IDE0290
