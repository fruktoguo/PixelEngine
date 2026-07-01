using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Utilities;

namespace PixelEngine.Editor;

/// <summary>
/// Editor 字体选择与 DPI 缩放管理。
/// </summary>
public sealed class EditorFontManager : IDisposable
{
    private static readonly uint[] CjkGlyphRanges =
    [
        0x0020, 0x00FF, // Basic Latin + Latin Supplement
        0x2000, 0x206F, // General Punctuation
        0x3000, 0x30FF, // CJK Symbols, Hiragana, Katakana
        0x31F0, 0x31FF, // Katakana Phonetic Extensions
        0x3400, 0x4DBF, // CJK Unified Ideographs Extension A
        0x4E00, 0x9FFF, // CJK Unified Ideographs
        0xF900, 0xFAFF, // CJK Compatibility Ideographs
        0,
    ];

    private static readonly string[] CandidateFontNames =
    [
        "NotoSansSC-VF.ttf",
        "Noto Sans SC (TrueType).otf",
        "Noto Sans SC Medium (TrueType).otf",
        "msyh.ttc",
        "simhei.ttf",
    ];

    private GlyphRanges _cjkGlyphRanges;
    private bool _hasCjkGlyphRanges;

    /// <summary>
    /// 解析应使用的中文字体路径。
    /// </summary>
    /// <param name="preferredPath">显式指定的字体路径。</param>
    /// <returns>存在的字体路径；找不到时返回 null。</returns>
    public string? ResolveCjkFontPath(string? preferredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            return File.Exists(preferredPath) ? preferredPath : null;
        }

        string fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (string.IsNullOrWhiteSpace(fontsDirectory))
        {
            fontsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        }

        for (int i = 0; i < CandidateFontNames.Length; i++)
        {
            string candidate = Path.Combine(fontsDirectory, CandidateFontNames[i]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 计算 DPI 缩放后的字体大小。
    /// </summary>
    /// <param name="baseSizePixels">基准像素大小。</param>
    /// <param name="dpiScale">DPI 缩放。</param>
    /// <returns>缩放后字体大小。</returns>
    public static float ScaleFontSize(float baseSizePixels, float dpiScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseSizePixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiScale);

        return baseSizePixels * dpiScale;
    }

    /// <summary>
    /// 使用拉丁、标点与中文 CJK glyph range 加载字体。
    /// </summary>
    /// <param name="fonts">ImGui 字体 atlas。</param>
    /// <param name="fontPath">字体文件路径。</param>
    /// <param name="fontSize">字号像素。</param>
    /// <returns>已加载字体指针。</returns>
    public unsafe ImFontPtr AddCjkFont(ImFontAtlasPtr fonts, string fontPath, float fontSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontPath);
        if (_hasCjkGlyphRanges)
        {
            _cjkGlyphRanges.Dispose();
        }

        _cjkGlyphRanges = new GlyphRanges(CjkGlyphRanges);
        _hasCjkGlyphRanges = true;
        return fonts.AddFontFromFileTTF(fontPath, fontSize, _cjkGlyphRanges.GetRanges());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_hasCjkGlyphRanges)
        {
            return;
        }

        _cjkGlyphRanges.Dispose();
        _hasCjkGlyphRanges = false;
    }
}
