using Xunit;
using PixelEngine.Core.Diagnostics;

namespace PixelEngine.UI.Tests;

/// <summary>
/// 字体引擎测试：字形度量、光栅化与缓存。
/// </summary>
public sealed class FontEngineTests
{
    /// <summary>
    /// 验证Font Engine Uses Content Fonts Before System Candidates。
    /// </summary>
    [Fact]
    public void FontEngineUsesContentFontsBeforeSystemCandidates()
    {
        string root = Path.Combine(Path.GetTempPath(), "pixelengine-fontengine", Guid.NewGuid().ToString("N"), "content", "ui");
        string fonts = Path.Combine(root, "fonts");
        _ = Directory.CreateDirectory(fonts);
        string expected = Path.Combine(fonts, "msyh.ttc");
        File.WriteAllBytes(expected, [0x00]);

        FontEngine engine = new(new FontEngineOptions(root, BaseSizePixels: 20f, DpiScale: 1.5f));
        UiFontSelection selection = engine.Resolve();

        Assert.Equal(Path.GetFullPath(expected), selection.FontPath);
        Assert.Equal(UiFontSource.ContentFonts, selection.Source);
        Assert.Equal(30f, selection.PixelSize);
    }

    /// <summary>
    /// 验证Font Engine解析Demo Bundled Cjk Subset Font。
    /// </summary>
    [Fact]
    public void FontEngineResolvesDemoBundledCjkSubsetFont()
    {
        string root = FindRepositoryRoot();
        string uiRoot = Path.Combine(root, "demo", "PixelEngine.Demo", "content", "ui");
        string fontPath = Path.Combine(uiRoot, "fonts", "NotoSansSC-VF.ttf");
        string licensePath = Path.Combine(uiRoot, "fonts", "OFL.txt");
        string sourcePath = Path.Combine(uiRoot, "fonts", "SOURCE.txt");

        Assert.True(File.Exists(fontPath), "Demo content/ui/fonts 必须包含可发行的 Noto Sans SC CJK 子集字体。");
        Assert.True(new FileInfo(fontPath).Length > 1_000_000, "NotoSansSC-VF.ttf 不应是占位文件。");
        Assert.Contains("SIL OPEN FONT LICENSE", File.ReadAllText(licensePath), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotoSansSC-VF.ttf", File.ReadAllText(sourcePath), StringComparison.Ordinal);

        FontEngine engine = new(new FontEngineOptions(uiRoot));
        UiFontSelection selection = engine.Resolve();

        Assert.Equal(Path.GetFullPath(fontPath), selection.FontPath);
        Assert.Equal(UiFontSource.ContentFonts, selection.Source);
    }

    /// <summary>
    /// 验证Font Engine Shares Glyph Coverage With Gui Font Manager。
    /// </summary>
    [Fact]
    public void FontEngineSharesGlyphCoverageWithGuiFontManager()
    {
        Assert.True(FontEngine.IsCodePointCovered('A'));
        Assert.True(FontEngine.IsCodePointCovered('你'));
        Assert.False(FontEngine.IsCodePointCovered(0x1F642));

        UiFontCoverageResult result = FontEngine.ScanCoverage("Hello你好🙂");

        Assert.Equal(8, result.ScannedCodePoints);
        Assert.Equal(1, result.MissingCodePoints);
        Assert.True(result.HasMissingGlyphs);
    }

    /// <summary>
    /// 验证Font Engine Publishes Missing Glyph Diagnostics。
    /// </summary>
    [Fact]
    public void FontEnginePublishesMissingGlyphDiagnostics()
    {
        EngineCounters counters = new();

        UiFontCoverageResult first = FontEngine.ScanCoverage("你好🙂", counters);
        UiFontCoverageResult second = FontEngine.ScanCoverage("ABC", counters);

        Assert.Equal(3, first.ScannedCodePoints);
        Assert.Equal(1, first.MissingCodePoints);
        Assert.Equal(3, second.ScannedCodePoints);
        Assert.Equal(0, second.MissingCodePoints);
        Assert.Equal(1, counters.UiFontMissingGlyphs);
    }

    /// <summary>
    /// 验证Preferred Font Path Is Normalized Before Resolve。
    /// </summary>
    [Fact]
    public void PreferredFontPathIsNormalizedBeforeResolve()
    {
        string root = Path.Combine(Path.GetTempPath(), "pixelengine-fontengine", Guid.NewGuid().ToString("N"), "content", "ui");
        string fontDirectory = Path.Combine(root, "custom");
        _ = Directory.CreateDirectory(fontDirectory);
        string fontPath = Path.Combine(fontDirectory, "preferred.ttf");
        File.WriteAllBytes(fontPath, [0x00]);

        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = fontDirectory;
            FontEngine engine = new(new FontEngineOptions(root, PreferredFontPath: ".\\preferred.ttf"));
            UiFontSelection selection = engine.Resolve();

            Assert.Equal(Path.GetFullPath(fontPath), selection.FontPath);
            Assert.Equal(UiFontSource.PreferredPath, selection.Source);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "PixelEngine.sln")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("找不到 PixelEngine 仓库根目录。");
    }
}
