namespace PixelEngine.UI;

/// <summary>
/// 游戏 UI 输入捕获快照，用于仲裁 UI 与游戏/脚本输入。
/// </summary>
/// <param name="HitsUi">指针是否命中 UI。</param>
/// <param name="Opaque">命中区域是否不透明。</param>
/// <param name="WantCaptureMouse">UI 是否请求捕获鼠标。</param>
/// <param name="WantCaptureKeyboard">UI 是否请求捕获键盘。</param>
public readonly record struct UiInputCapture(bool HitsUi, bool Opaque, bool WantCaptureMouse, bool WantCaptureKeyboard)
{
    /// <summary>
    /// 完全不捕获输入的快照。
    /// </summary>
    public static UiInputCapture None { get; } = new(false, false, false, false);

    /// <summary>
    /// 游戏/脚本是否可以消费鼠标输入。
    /// </summary>
    public bool AllowWorldMouse => !WantCaptureMouse;

    /// <summary>
    /// 游戏/脚本是否可以消费键盘输入。
    /// </summary>
    public bool AllowWorldKeyboard => !WantCaptureKeyboard;
}
