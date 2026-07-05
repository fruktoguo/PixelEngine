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
    /// 采集本帧文本输入。
    /// </summary>
    /// <param name="destination">文本写入缓冲。</param>
    /// <returns>写入字符数量。</returns>
    int CaptureText(Span<char> destination);
}
