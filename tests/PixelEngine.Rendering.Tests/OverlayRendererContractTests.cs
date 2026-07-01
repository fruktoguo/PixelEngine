using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class OverlayRendererContractTests
{
    [Fact]
    public void CommandsUseViewportPixelCoordinates()
    {
        OverlayCommand solid = OverlayCommand.SolidRectangle(10f, 20f, 30f, 40f, 0x80403020u);
        OverlayCommand outline = OverlayCommand.OutlineRectangle(1f, 2f, 3f, 4f, 0.5f, 0xFF112233u);
        OverlayCommand sprite = OverlayCommand.SpriteRectangle(5f, 6f, 7f, 8f, new OverlaySprite(9, 16, 32), 0xFFFFFFFFu);
        OverlayCommand line = OverlayCommand.Line(1f, 2f, 9f, 10f, 1.5f, 0xFF010203u);

        Assert.Equal(OverlayPrimitiveType.SolidRectangle, solid.PrimitiveType);
        Assert.Equal(10f, solid.ViewportX);
        Assert.Equal(20f, solid.ViewportY);
        Assert.Equal(30f, solid.Width);
        Assert.Equal(40f, solid.Height);
        Assert.Equal(OverlayPrimitiveType.OutlineRectangle, outline.PrimitiveType);
        Assert.Equal(0.5f, outline.OutlineThickness);
        Assert.Equal(OverlayPrimitiveType.Sprite, sprite.PrimitiveType);
        Assert.Equal((uint)9, sprite.Sprite.TextureHandle);
        Assert.Equal(OverlayPrimitiveType.Line, line.PrimitiveType);
        Assert.Equal(9f, line.LineEndX);
        Assert.Equal(10f, line.LineEndY);
    }

    [Fact]
    public void CommandValidationRejectsInvalidInput()
    {
        AssertThrows<ArgumentOutOfRangeException>(() => OverlayCommand.SolidRectangle(float.NaN, 0f, 1f, 1f, 0).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() => OverlayCommand.SolidRectangle(0f, 0f, 0f, 1f, 0).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() => OverlayCommand.OutlineRectangle(0f, 0f, 1f, 1f, 0f, 0).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() => OverlayCommand.SpriteRectangle(0f, 0f, 1f, 1f, new OverlaySprite(0, 16, 16)).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() => OverlayCommand.Line(0f, 0f, 0f, 0f, 1f, 0).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() => OverlayCommand.Line(0f, 0f, 1f, 1f, 0f, 0).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() => new OverlaySprite(1, 16, 16, 0.75f, 0f, 0.25f, 1f).Validate());
    }

    [Fact]
    public void ShaderSourcesExposeOverlayContract()
    {
        string vertex = OverlayShaderSources.Vertex(GlslProfile.DesktopGl330);
        string fragment = OverlayShaderSources.Fragment(GlslProfile.Gles300);

        Assert.Contains("#version 330 core", vertex, StringComparison.Ordinal);
        Assert.Contains("aPosition", vertex, StringComparison.Ordinal);
        Assert.Contains("aTexCoord", vertex, StringComparison.Ordinal);
        Assert.Contains("aColor", vertex, StringComparison.Ordinal);
        Assert.Contains("aUseTexture", vertex, StringComparison.Ordinal);
        Assert.Contains("uViewportSize", vertex, StringComparison.Ordinal);
        Assert.Contains("1.0 - (normalized.y * 2.0)", vertex, StringComparison.Ordinal);
        Assert.Contains("#version 300 es", fragment, StringComparison.Ordinal);
        Assert.Contains("uSpriteTexture", fragment, StringComparison.Ordinal);
        Assert.Contains("texture(uSpriteTexture, vTexCoord)", fragment, StringComparison.Ordinal);
    }

    private static void AssertThrows<T>(Action action)
        where T : Exception
    {
        T exception = Assert.Throws<T>(action);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }
}
