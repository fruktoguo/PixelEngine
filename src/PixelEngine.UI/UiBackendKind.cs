namespace PixelEngine.UI;

/// <summary>
/// 游戏 UI 后端种类。
/// </summary>
public enum UiBackendKind : byte
{
    /// <summary>
    /// 纯托管回退后端，复用 PixelEngine.Gui。
    /// </summary>
    ManagedFallback = 0,

    /// <summary>
    /// RmlUi HTML/CSS 子集主后端。
    /// </summary>
    RmlUi = 1,

    /// <summary>
    /// Ultralight 标准 HTML 可选后端。
    /// </summary>
    Ultralight = 2,
}
