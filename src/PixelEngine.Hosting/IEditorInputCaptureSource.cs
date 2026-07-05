namespace PixelEngine.Hosting;

/// <summary>
/// 独立 Editor 壳向 Hosting 暴露的中性输入捕获状态。
/// </summary>
/// <param name="WantCaptureMouse">Editor UI 是否请求捕获鼠标。</param>
/// <param name="WantCaptureKeyboard">Editor UI 是否请求捕获键盘。</param>
public readonly record struct EditorHostInputCapture(bool WantCaptureMouse, bool WantCaptureKeyboard)
{
    /// <summary>
    /// 空捕获状态。
    /// </summary>
    public static readonly EditorHostInputCapture None = new(false, false);

    /// <summary>
    /// 游戏是否可消费鼠标。
    /// </summary>
    public bool AllowGameMouse => !WantCaptureMouse;

    /// <summary>
    /// 游戏是否可消费键盘。
    /// </summary>
    public bool AllowGameKeyboard => !WantCaptureKeyboard;
}

/// <summary>
/// Editor Shell 可选实现的输入捕获源；Hosting 通过它实现 Editor → UI → Game 的输入优先级。
/// </summary>
public interface IEditorInputCaptureSource
{
    /// <summary>
    /// 尝试读取当前帧 Editor UI 输入捕获状态。
    /// </summary>
    /// <param name="capture">当前捕获状态。</param>
    /// <returns>Editor 输入源可用则返回 true。</returns>
    bool TryGetInputCapture(out EditorHostInputCapture capture);
}
