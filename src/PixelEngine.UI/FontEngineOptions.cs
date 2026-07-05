namespace PixelEngine.UI;

/// <summary>
/// Game UI 字体引擎配置。
/// </summary>
/// <param name="UiRootDirectory">content/ui 根目录。</param>
/// <param name="PreferredFontPath">显式指定字体路径；为空时先查 content/ui/fonts，再查共享系统候选。</param>
/// <param name="BaseSizePixels">基础字号像素。</param>
/// <param name="DpiScale">DPI 缩放。</param>
public readonly record struct FontEngineOptions(
    string UiRootDirectory,
    string? PreferredFontPath = null,
    float BaseSizePixels = 18f,
    float DpiScale = 1f)
{
    /// <summary>
    /// 规范化配置并校验数值范围。
    /// </summary>
    /// <returns>规范化后的配置。</returns>
    public FontEngineOptions Normalize()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(UiRootDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BaseSizePixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DpiScale);
        return this with
        {
            UiRootDirectory = Path.GetFullPath(UiRootDirectory),
            PreferredFontPath = string.IsNullOrWhiteSpace(PreferredFontPath) ? null : PreferredFontPath.Trim(),
        };
    }
}
