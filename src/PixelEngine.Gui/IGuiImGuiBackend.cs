using Hexa.NET.ImGui;

namespace PixelEngine.Gui;

/// <summary>
/// ImGui 静态 API 的中性适配层，运行时仍调用真实 Hexa.NET 后端。
/// </summary>
public interface IGuiImGuiBackend
{
    /// <summary>
    /// 创建 ImGui context 并初始化 OpenGL3 后端。
    /// </summary>
    void Initialize(GuiAppOptions options);

    /// <summary>
    /// 开始一帧 ImGui。
    /// </summary>
    /// <param name="deltaSeconds">距离上一帧的真实秒数。</param>
    /// <param name="width">平台窗口逻辑宽度。</param>
    /// <param name="height">平台窗口逻辑高度。</param>
    /// <param name="framebufferScaleX">逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。</param>
    /// <param name="framebufferScaleY">逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。</param>
    void NewFrame(float deltaSeconds, int width, int height, float framebufferScaleX, float framebufferScaleY);

    /// <summary>
    /// 结束当前 ImGui 帧并渲染 draw data。
    /// </summary>
    void Render();

    /// <summary>
    /// 读取当前输入捕获状态。
    /// </summary>
    GuiInputSnapshot Capture { get; }

    /// <summary>
    /// 注入鼠标位置事件。
    /// </summary>
    void AddMousePosition(float x, float y);

    /// <summary>
    /// 注入鼠标按键事件。
    /// </summary>
    void AddMouseButton(int button, bool down);

    /// <summary>
    /// 注入鼠标滚轮事件。
    /// </summary>
    void AddMouseWheel(float wheelX, float wheelY);

    /// <summary>
    /// 注入键盘事件。
    /// </summary>
    void AddKey(ImGuiKey key, bool down);

    /// <summary>
    /// 注入文本输入。
    /// </summary>
    void AddText(string text);

    /// <summary>
    /// 关闭 ImGui 后端并销毁 context。
    /// </summary>
    void Shutdown();
}
