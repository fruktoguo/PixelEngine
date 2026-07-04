namespace PixelEngine.UI;

/// <summary>
/// UI 命中测试与输入捕获结果。
/// </summary>
/// <param name="HitsUi">是否命中 UI。</param>
/// <param name="Opaque">命中区域是否不透明。</param>
/// <param name="WantsMouse">是否捕获鼠标。</param>
/// <param name="WantsKeyboard">是否捕获键盘。</param>
public readonly record struct UiHitResult(bool HitsUi, bool Opaque, bool WantsMouse, bool WantsKeyboard)
{
    /// <summary>
    /// 完全未命中 UI。
    /// </summary>
    public static readonly UiHitResult None = new(false, false, false, false);
}
