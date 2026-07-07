namespace PixelEngine.UI;

/// <summary>
/// UI 输入源抽象，由 Hosting 把窗口/平台输入适配为稳定 UI 输入。
/// </summary>
public interface IUiInputSource
{
    /// <summary>
    /// 获取当前指针状态。
    /// </summary>
    /// <param name="state">当前指针状态。</param>
    /// <returns>存在指针设备则返回 true。</returns>
    bool TryGetPointer(out UiPointerState state);

    /// <summary>
    /// 采集当前按下的键盘按键。
    /// </summary>
    /// <param name="destination">按键写入缓冲。</param>
    /// <param name="modifiers">当前键盘修饰键。</param>
    /// <returns>写入按键数量。</returns>
    int CaptureDownKeys(Span<UiKey> destination, out UiKeyModifiers modifiers);

    /// <summary>
    /// 采集本帧已提交文本输入；该通道不表示 IME 预编辑内容。
    /// </summary>
    /// <param name="destination">文本写入缓冲。</param>
    /// <returns>写入字符数量。</returns>
    int CaptureText(Span<char> destination);

    /// <summary>
    /// 当前输入源的真实平台 IME composition 能力诊断；没有事件来源时必须明确说明不可用原因。
    /// </summary>
    UiTextCompositionCapabilities TextCompositionCapabilities =>
        UiTextCompositionCapabilities.Unsupported("当前输入源未声明真实平台 IME composition 事件支持。");

    /// <summary>
    /// 采集当前平台 IME composition 预编辑文本；平台没有真实 composition 事件时必须返回非活动状态。
    /// </summary>
    /// <param name="destination">预编辑文本写入缓冲。</param>
    /// <param name="composition">当前预编辑状态。</param>
    /// <returns>写入预编辑文本字符数量。</returns>
    int CaptureTextComposition(Span<char> destination, out UiTextComposition composition)
    {
        _ = destination;
        composition = UiTextComposition.Inactive;
        return 0;
    }
}
