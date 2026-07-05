namespace PixelEngine.UI;

/// <summary>
/// FontEngine 解析出的字体选择。
/// </summary>
/// <param name="FontPath">字体文件路径；找不到可用字体时为 null，由后端使用自身默认字体。</param>
/// <param name="PixelSize">DPI 缩放后的字号像素。</param>
/// <param name="Source">字体来源。</param>
public readonly record struct UiFontSelection(string? FontPath, float PixelSize, UiFontSource Source);

/// <summary>
/// UI 字体来源。
/// </summary>
public enum UiFontSource : byte
{
    /// <summary>
    /// 未找到显式字体，后端应使用默认字体。
    /// </summary>
    BackendDefault,

    /// <summary>
    /// 来自显式 PreferredFontPath。
    /// </summary>
    PreferredPath,

    /// <summary>
    /// 来自 content/ui/fonts。
    /// </summary>
    ContentFonts,

    /// <summary>
    /// 来自 PixelEngine.Gui 共享系统候选。
    /// </summary>
    SharedSystemCandidate,
}
