using Xunit;

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
}
