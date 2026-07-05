using System.Buffers;
using System.Text;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Gui;

namespace PixelEngine.UI;

/// <summary>
/// Game UI 字体选择与 CJK 覆盖检测入口，复用 PixelEngine.Gui 的共享字体契约。
/// </summary>
public sealed class FontEngine
{
    private readonly FontEngineOptions _options;

    /// <summary>
    /// 创建字体引擎。
    /// </summary>
    /// <param name="options">字体配置。</param>
    public FontEngine(FontEngineOptions options)
    {
        _options = options.Normalize();
        ContentFontsDirectory = Path.Combine(_options.UiRootDirectory, "fonts");
    }

    /// <summary>
    /// content/ui/fonts 目录。
    /// </summary>
    public string ContentFontsDirectory { get; }

    /// <summary>
    /// 解析当前应使用的字体。
    /// </summary>
    /// <returns>字体选择。</returns>
    public UiFontSelection Resolve()
    {
        float pixelSize = GuiFontManager.ScaleFontSize(_options.BaseSizePixels, _options.DpiScale);
        if (_options.PreferredFontPath is not null)
        {
            return File.Exists(_options.PreferredFontPath)
                ? new UiFontSelection(_options.PreferredFontPath, pixelSize, UiFontSource.PreferredPath)
                : new UiFontSelection(null, pixelSize, UiFontSource.BackendDefault);
        }

        if (TryResolveContentFont(out string? contentFont))
        {
            return new UiFontSelection(contentFont, pixelSize, UiFontSource.ContentFonts);
        }

        string? sharedFont = GuiFontManager.ResolveCjkFontFile();
        return sharedFont is not null
            ? new UiFontSelection(sharedFont, pixelSize, UiFontSource.SharedSystemCandidate)
            : new UiFontSelection(null, pixelSize, UiFontSource.BackendDefault);
    }

    /// <summary>
    /// 判断码点是否落在共享 glyph range 中。
    /// </summary>
    /// <param name="codePoint">Unicode 码点。</param>
    /// <returns>落在共享范围内则返回 true。</returns>
    public static bool IsCodePointCovered(int codePoint)
    {
        return GuiFontManager.IsGlyphCovered(codePoint);
    }

    /// <summary>
    /// 扫描文本是否存在不在共享 glyph range 中的码点。
    /// </summary>
    /// <param name="text">待扫描文本。</param>
    /// <returns>覆盖扫描结果。</returns>
    public static UiFontCoverageResult ScanCoverage(ReadOnlySpan<char> text)
    {
        return ScanCoverage(text, counters: null);
    }

    /// <summary>
    /// 扫描文本是否存在不在共享 glyph range 中的码点，并把缺字数发布到诊断计数器。
    /// </summary>
    /// <param name="text">待扫描文本。</param>
    /// <param name="counters">可选引擎计数器；为空时只返回扫描结果。</param>
    /// <returns>覆盖扫描结果。</returns>
    public static UiFontCoverageResult ScanCoverage(ReadOnlySpan<char> text, EngineCounters? counters)
    {
        int scanned = 0;
        int missing = 0;
        while (!text.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(text, out Rune rune, out int consumed);
            if (status != OperationStatus.Done)
            {
                missing++;
                text = text[1..];
                continue;
            }

            scanned++;
            if (!IsCodePointCovered(rune.Value))
            {
                missing++;
            }

            text = text[consumed..];
        }

        if (missing > 0)
        {
            counters?.AddUiFontMissingGlyphs(missing);
        }

        return new UiFontCoverageResult(scanned, missing);
    }

    private bool TryResolveContentFont(out string? fontPath)
    {
        if (Directory.Exists(ContentFontsDirectory))
        {
            ReadOnlySpan<string> candidates = GuiFontManager.GetCjkCandidateFontNames();
            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = Path.Combine(ContentFontsDirectory, candidates[i]);
                if (File.Exists(candidate))
                {
                    fontPath = candidate;
                    return true;
                }
            }
        }

        fontPath = null;
        return false;
    }
}
