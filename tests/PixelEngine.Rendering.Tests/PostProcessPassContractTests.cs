using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class PostProcessPassContractTests
{
    [Fact]
    public void BloomSettingsNormalizeClampsValidValues()
    {
        BloomSettings settings = new(BloomMode.DualKawase, 12f, 2f, 99, 16f);

        BloomSettings normalized = settings.Normalize();

        Assert.Equal(8f, normalized.Threshold);
        Assert.Equal(2f, normalized.Intensity);
        Assert.Equal(8, normalized.Iterations);
        Assert.Equal(8f, normalized.KawaseOffset);
    }

    [Fact]
    public void BloomSettingsRejectInvalidValues()
    {
        AssertThrows<ArgumentOutOfRangeException>(() => new BloomSettings(BloomMode.DualKawase, float.NaN, 1f, 1, 1f).Normalize());
        AssertThrows<ArgumentOutOfRangeException>(() => new BloomSettings(BloomMode.DualKawase, 1f, -1f, 1, 1f).Normalize());
        AssertThrows<ArgumentOutOfRangeException>(() => new BloomSettings(BloomMode.DualKawase, 1f, 1f, 0, 1f).Normalize());
        AssertThrows<ArgumentOutOfRangeException>(() => new BloomSettings(BloomMode.DualKawase, 1f, 1f, 1, 0f).Normalize());
    }

    [Fact]
    public void BloomShaderSourcesContainRequiredPasses()
    {
        string bright = PostProcessShaderSources.BrightPassFragment(GlslProfile.DesktopGl330);
        string down = PostProcessShaderSources.KawaseDownFragment(GlslProfile.DesktopGl330);
        string up = PostProcessShaderSources.KawaseUpFragment(GlslProfile.DesktopGl330);
        string gaussian = PostProcessShaderSources.GaussianBlurFragment(GlslProfile.DesktopGl330);
        string composite = PostProcessShaderSources.BloomCompositeFragment(GlslProfile.DesktopGl330);

        Assert.Contains("uThreshold", bright, StringComparison.Ordinal);
        Assert.Contains("smoothstep", bright, StringComparison.Ordinal);
        Assert.Contains("uOffset", down, StringComparison.Ordinal);
        Assert.Contains("uOffset", up, StringComparison.Ordinal);
        Assert.Contains("uDirection", gaussian, StringComparison.Ordinal);
        Assert.Contains("scene.rgb + bloom", composite, StringComparison.Ordinal);
    }

    [Fact]
    public void FragmentBloomEnablesAdditiveBlendAfterFullscreenPassBegin()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "BloomPass.cs"));
        int upBegin = source.IndexOf("_kawaseUpPass.Begin(source, destination, quad);", StringComparison.Ordinal);
        int additiveBlend = source.IndexOf("EnableAdditiveBloomBlend();", StringComparison.Ordinal);
        int upDraw = source.IndexOf("_kawaseUpPass.Draw(quad);", StringComparison.Ordinal);

        Assert.True(upBegin >= 0);
        Assert.True(additiveBlend > upBegin);
        Assert.True(upDraw > additiveBlend);
        Assert.Contains("BlendFunc(BlendingFactor.One, BlendingFactor.One)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalPostShaderSourcesContainDitherGammaAndCrtControls()
    {
        string dither = PostProcessShaderSources.DitherFragment(GlslProfile.Gles300);
        string gamma = PostProcessShaderSources.GammaFragment(GlslProfile.Gles300);
        string crt = PostProcessShaderSources.CrtFragment(GlslProfile.Gles300);

        Assert.Contains("Bayer4", dither, StringComparison.Ordinal);
        Assert.Contains("uStrength", dither, StringComparison.Ordinal);
        Assert.Contains("uGamma", gamma, StringComparison.Ordinal);
        Assert.Contains("pow", gamma, StringComparison.Ordinal);
        Assert.Contains("uScanlineStrength", crt, StringComparison.Ordinal);
        Assert.Contains("uCurvature", crt, StringComparison.Ordinal);
    }

    private static void AssertThrows<T>(Action action)
        where T : Exception
    {
        T exception = Assert.Throws<T>(action);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    private static string ProjectPath(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            path = Directory.GetParent(path)!.FullName;
        }

        return Path.Combine([path, .. parts]);
    }
}
