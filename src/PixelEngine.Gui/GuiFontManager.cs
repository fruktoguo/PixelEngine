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
        return ResolveCjkFontFile(preferredPath);
    }

    /// <summary>
    /// 共享 CJK glyph range，供 Editor、玩家 HUD 与 Game UI 使用同一份码点范围。
    /// </summary>
    /// <returns>ImGui/FreeType 风格的 start/end 成对范围，以 0 结尾。</returns>
    public static ReadOnlySpan<uint> GetCjkGlyphRanges()
    {
        return CjkGlyphRanges;
    }

    /// <summary>
    /// 共享 CJK 候选字体文件名，按优先级排序。
    /// </summary>
    /// <returns>候选字体文件名。</returns>
    public static ReadOnlySpan<string> GetCjkCandidateFontNames()
    {
        return CandidateFontNames;
    }

    /// <summary>
    /// 判断码点是否落在共享 CJK/拉丁/标点 glyph range 中。
    /// </summary>
    /// <param name="codePoint">Unicode 码点。</param>
    /// <returns>落在共享范围内则返回 true。</returns>
    public static bool IsGlyphCovered(int codePoint)
    {
        if (codePoint < 0)
        {
            return false;
        }

        uint value = (uint)codePoint;
        for (int i = 0; i < CjkGlyphRanges.Length - 1; i += 2)
        {
            uint start = CjkGlyphRanges[i];
            if (start == 0)
            {
                return false;
            }

            uint end = CjkGlyphRanges[i + 1];
            if (value >= start && value <= end)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 解析共享 CJK 字体路径。
    /// </summary>
    /// <param name="preferredPath">显式指定的字体路径。</param>
    /// <returns>存在的字体路径；找不到时返回 null。</returns>
    public static string? ResolveCjkFontFile(string? preferredPath = null)
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
