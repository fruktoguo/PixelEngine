using PixelEngine.Gui;
using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// UiInputRouter 可驱动的中性游戏 UI 输入目标；单 Canvas host 与多 Canvas registry 共用。
/// </summary>
public interface IGameUiInputTarget
{
    /// <summary>注入指针移动。</summary>
    void FeedPointerMove(float x, float y);

    /// <summary>注入指针按钮。</summary>
    void FeedPointerButton(UiPointerButton button, bool isDown);

    /// <summary>注入滚轮。</summary>
    void FeedScroll(float deltaX, float deltaY);

    /// <summary>注入键盘按键。</summary>
    void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers);

    /// <summary>注入已提交文本。</summary>
    void FeedText(ReadOnlySpan<char> text);

    /// <summary>注入 IME composition。</summary>
    void FeedTextComposition(ReadOnlySpan<char> text, in UiTextComposition composition);

    /// <summary>读取 presentation 坐标下的 IME 几何。</summary>
    bool TryGetImeGeometry(out UiImeGeometry geometry);

    /// <summary>按 presentation 坐标执行命中测试。</summary>
    UiHitResult HitTest(float x, float y);
}

/// <summary>
/// Rendering present 层可驱动的中性游戏 UI 合成目标。
/// </summary>
public interface IGameUiPresentationTarget
{
    /// <summary>在帧边界提交 display metrics。</summary>
    void Resize(in UiDisplayMetrics displayMetrics);

    /// <summary>把 direct/native UI 合成到当前 present surface。</summary>
    void Composite(in UiPresentContext context);

    /// <summary>在共享 ImGui frame 中绘制托管回退 Canvas。</summary>
    void DrawGui(IGuiDrawContext gui);
}
