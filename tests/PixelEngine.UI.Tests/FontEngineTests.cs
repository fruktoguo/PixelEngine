using Xunit;
using PixelEngine.Core.Diagnostics;

namespace PixelEngine.UI.Tests;

public sealed class FontEngineTests
{
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
}
