using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class GpuComputeShaderSourcesTests
{
    private static readonly string[] ExpectedPassNames =
    [
        GpuComputeShaderSources.BloomBrightPassName,
        GpuComputeShaderSources.BloomDownsampleName,
        GpuComputeShaderSources.BloomDualKawaseDownName,
        GpuComputeShaderSources.BloomDualKawaseUpName,
        GpuComputeShaderSources.BloomUpsampleCompositeName,
        GpuComputeShaderSources.LightCompositeName,
    ];

    [Fact]
    public void PassNamesExposePlan09InitialComputeSet()
    {
        Assert.Equal(ExpectedPassNames.Order(StringComparer.Ordinal), GpuComputeShaderSources.PassNames.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ShaderSources))]
    public void ShaderSourcesContainComputeSkeletonContract(string passName, string source)
    {
        Assert.False(string.IsNullOrWhiteSpace(passName));
        Assert.False(string.IsNullOrWhiteSpace(source));
        Assert.Contains("#version 430", source, StringComparison.Ordinal);
        Assert.Contains("layout(local_size_x", source, StringComparison.Ordinal);
        Assert.Contains("layout(rgba8", source, StringComparison.Ordinal);
        Assert.Contains("void main", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BloomComputeSourcesMatchFragmentBloomSemantics()
    {
        Assert.Contains("smoothstep(uThreshold, uThreshold + 0.2", GpuComputeShaderSources.BloomBrightPass, StringComparison.Ordinal);
        Assert.Contains("uSourceTexelSize", GpuComputeShaderSources.BloomDownsample, StringComparison.Ordinal);
        Assert.Contains("uBaseTexture", GpuComputeShaderSources.BloomDualKawaseUp, StringComparison.Ordinal);
        Assert.Contains("scene.rgb + bloom", GpuComputeShaderSources.BloomUpsampleComposite, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSourceReturnsRegisteredSourcesOnly()
    {
        foreach (string passName in ExpectedPassNames)
        {
            string source = GpuComputeShaderSources.GetSource(passName);

            Assert.False(string.IsNullOrWhiteSpace(source));
        }

        ArgumentException exception = Assert.Throws<ArgumentException>(() => GpuComputeShaderSources.GetSource("missing_pass"));
        Assert.Contains("missing_pass", exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> ShaderSources()
    {
        TheoryData<string, string> data = [];
        foreach (string passName in ExpectedPassNames)
        {
            data.Add(passName, GpuComputeShaderSources.GetSource(passName));
        }

        return data;
    }
}
