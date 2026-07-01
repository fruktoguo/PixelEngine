using Hexa.NET.ImGui;

namespace PixelEngine.Editor;

/// <summary>
/// ImGui 静态 API 的适配层，便于测试 Editor 框架且运行时仍调用真实 Hexa.NET 后端。
/// </summary>
public interface IEditorImGuiBackend
{
    /// <summary>
    /// 创建 ImGui context 并初始化 OpenGL3 后端。
    /// </summary>
    /// <param name="options">Editor 选项。</param>
    void Initialize(EditorAppOptions options);

    /// <summary>
    /// 开始一帧 ImGui。
    /// </summary>
    /// <param name="deltaSeconds">距离上一帧的真实秒数。</param>
    /// <param name="width">framebuffer 宽度。</param>
    /// <param name="height">framebuffer 高度。</param>
    void NewFrame(float deltaSeconds, int width, int height);

    /// <summary>
    /// 绘制 dockspace。
    /// </summary>
    void DrawDockSpace();

    /// <summary>
    /// 结束当前 ImGui 帧并渲染 draw data。
    /// </summary>
    void Render();

    /// <summary>
    /// 读取当前输入捕获状态。
    /// </summary>
    /// <returns>捕获快照。</returns>
    EditorInputSnapshot Capture { get; }

    /// <summary>
    /// 注入鼠标位置事件。
    /// </summary>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    void AddMousePosition(float x, float y);

    /// <summary>
    /// 注入鼠标按键事件。
    /// </summary>
    /// <param name="button">ImGui 鼠标键索引。</param>
    /// <param name="down">是否按下。</param>
    void AddMouseButton(int button, bool down);

    /// <summary>
    /// 注入鼠标滚轮事件。
    /// </summary>
    /// <param name="wheelX">水平滚轮。</param>
    /// <param name="wheelY">垂直滚轮。</param>
    void AddMouseWheel(float wheelX, float wheelY);

    /// <summary>
    /// 注入键盘事件。
    /// </summary>
    /// <param name="key">ImGui 键码。</param>
    /// <param name="down">是否按下。</param>
    void AddKey(ImGuiKey key, bool down);

    /// <summary>
    /// 注入文本输入。
    /// </summary>
    /// <param name="text">UTF-16 文本。</param>
    void AddText(string text);

    /// <summary>
    /// 关闭 ImGui 后端并销毁 context。
    /// </summary>
    void Shutdown();
}
