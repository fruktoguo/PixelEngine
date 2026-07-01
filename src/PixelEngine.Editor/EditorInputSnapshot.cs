namespace PixelEngine.Editor;

/// <summary>
/// ImGui 输入捕获快照，用于仲裁 UI 与世界/游戏输入。
/// </summary>
/// <param name="WantCaptureMouse">ImGui 是否请求捕获鼠标。</param>
/// <param name="WantCaptureKeyboard">ImGui 是否请求捕获键盘。</param>
public readonly record struct EditorInputSnapshot(bool WantCaptureMouse, bool WantCaptureKeyboard)
{
    /// <summary>
    /// 世界工具是否可以消费鼠标输入。
    /// </summary>
    public bool AllowWorldMouse => !WantCaptureMouse;

    /// <summary>
    /// Demo/脚本是否可以消费键盘输入。
    /// </summary>
    public bool AllowWorldKeyboard => !WantCaptureKeyboard;
}
