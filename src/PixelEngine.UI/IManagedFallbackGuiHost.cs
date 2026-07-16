using PixelEngine.Gui;

namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 复用的中性 Gui host 抽象；实现必须共享既有玩家 GUI host。
/// </summary>
public interface IManagedFallbackGuiHost
{
    /// <summary>
    /// Gui host 是否已初始化并运行。
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 初始化 Gui host。
    /// </summary>
    void Initialize();

    /// <summary>
    /// 在当前 Gui host 上绘制一帧。
    /// </summary>
    /// <param name="deltaSeconds">渲染帧 dt。</param>
    /// <param name="width">framebuffer 宽度。</param>
    /// <param name="height">framebuffer 高度。</param>
    /// <param name="drawGui">绘制回调。</param>
    void DrawFrame(float deltaSeconds, int width, int height, Action<IGuiDrawContext> drawGui);

    /// <summary>
    /// 载入或复用一张 UI 图片资产。
    /// </summary>
    /// <param name="path">图片绝对路径。</param>
    /// <returns>可绘制的图片资产。</returns>
    ManagedFallbackImage LoadImage(string path);

    /// <summary>将 presentation 指针位置注入共享 Gui host。</summary>
    /// <param name="x">presentation X。</param>
    /// <param name="y">presentation Y。</param>
    void FeedPointerMove(float x, float y);

    /// <summary>将指针按钮边沿注入共享 Gui host。</summary>
    /// <param name="button">指针按钮。</param>
    /// <param name="isDown">是否按下。</param>
    void FeedPointerButton(UiPointerButton button, bool isDown);

    /// <summary>将滚轮增量注入共享 Gui host。</summary>
    /// <param name="deltaX">水平增量。</param>
    /// <param name="deltaY">垂直增量。</param>
    void FeedScroll(float deltaX, float deltaY);

    /// <summary>将规范化按键边沿与修饰键注入共享 Gui host。</summary>
    /// <param name="key">按键。</param>
    /// <param name="isDown">是否按下。</param>
    /// <param name="modifiers">修饰键。</param>
    void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers);

    /// <summary>将已提交文本注入共享 Gui host。</summary>
    /// <param name="text">已提交文本。</param>
    void FeedText(ReadOnlySpan<char> text);
}
