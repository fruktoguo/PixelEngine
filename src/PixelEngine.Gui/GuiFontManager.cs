using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Utilities;

namespace PixelEngine.Gui;

/// <summary>
/// GUI 字体选择与 DPI 缩放管理。
/// </summary>
public sealed class GuiFontManager : IDisposable
{
    private static readonly uint[] CjkGlyphRanges =
    [
        0x0020, 0x00FF,
        0x2000, 0x206F,
        0x3000, 0x30FF,
        0x31F0, 0x31FF,
        0x3400, 0x4DBF,
        0x4E00, 0x9FFF,
        0xF900, 0xFAFF,
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
    public static float ScaleFontSize(float baseSizePixels, float dpiScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseSizePixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiScale);

        return baseSizePixels * dpiScale;
    }

    /// <summary>
    /// 使用拉丁、标点与中文 CJK glyph range 加载字体。
    /// </summary>
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
