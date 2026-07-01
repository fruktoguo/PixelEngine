using PixelEngine.Rendering.Compute;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class RenderPipelineContractTests
{
    [Fact]
    public void RenderPipelineSettingsValidateRejectsInvalidValues()
    {
        RenderPipelineSettings settings = new() { DitherStrength = -1f };

        AssertThrows<ArgumentOutOfRangeException>(settings.Validate);

        settings = new RenderPipelineSettings { Gamma = 0f };
        AssertThrows<ArgumentOutOfRangeException>(settings.Validate);

        settings = new RenderPipelineSettings { CrtScanlineStrength = float.NaN };
        AssertThrows<ArgumentOutOfRangeException>(settings.Validate);

        settings = new RenderPipelineSettings { RadianceCascades = RadianceCascadeSettings.Default with { BaseRayCount = 63 } };
        AssertThrows<ArgumentOutOfRangeException>(settings.Validate);
    }

    [Fact]
    public void RenderPipelineSourceDocumentsRequiredOrderingAndHooks()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderPipeline.cs"));

        Assert.Contains("_worldBlit.Render", source, StringComparison.Ordinal);
        Assert.Contains("_overlay.Render", source, StringComparison.Ordinal);
        Assert.Contains("_composite.Render", source, StringComparison.Ordinal);
        Assert.Contains("_bloom.Render", source, StringComparison.Ordinal);
        Assert.Contains("_computeBloom!.Render", source, StringComparison.Ordinal);
        Assert.Contains("ComputeCapabilityGate.Evaluate", source, StringComparison.Ordinal);
        Assert.Contains("_dither.Render", source, StringComparison.Ordinal);
        Assert.Contains("_gamma.Render", source, StringComparison.Ordinal);
        Assert.Contains("_crt.Render", source, StringComparison.Ordinal);
        Assert.Contains("BeforePresentUi?.Invoke", source, StringComparison.Ordinal);
        Assert.Contains("ShouldDelegateComputeLighting", source, StringComparison.Ordinal);
        Assert.Contains("DegradeGpuComputeOneStep", source, StringComparison.Ordinal);
        Assert.Contains("RadianceCascades.Enabled", source, StringComparison.Ordinal);
        Assert.Contains("Settings.PreferComputeLighting = false", source, StringComparison.Ordinal);
        Assert.Contains("CreateComputeResourcesSnapshot", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("_worldBlit.Render", StringComparison.Ordinal) < source.IndexOf("_overlay.Render", StringComparison.Ordinal));
        Assert.True(source.IndexOf("_overlay.Render", StringComparison.Ordinal) < source.IndexOf("_composite.Render", StringComparison.Ordinal));
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

    private static void AssertThrows<T>(Action action)
        where T : Exception
    {
        T exception = Assert.Throws<T>(action);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }
}
