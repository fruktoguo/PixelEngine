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
        GpuComputeShaderSources.RadianceCascadeSdfJfaName,
        GpuComputeShaderSources.RadianceCascadeBuildName,
        GpuComputeShaderSources.RadianceCascadeMergeName,
        GpuComputeShaderSources.RadianceCascadeApplyName,
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
        Assert.Contains("layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;", source, StringComparison.Ordinal);
        Assert.Contains("writeonly uniform image2D", source, StringComparison.Ordinal);
        Assert.Contains("void main()", source, StringComparison.Ordinal);
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
    public void RadianceCascadeSourcesExposeRenderSideContracts()
    {
        Assert.Contains("uSdfTexture", GpuComputeShaderSources.RadianceCascadeBuild, StringComparison.Ordinal);
        Assert.Contains("uCascadeIndex", GpuComputeShaderSources.RadianceCascadeBuild, StringComparison.Ordinal);
        Assert.Contains("uRayCount", GpuComputeShaderSources.RadianceCascadeBuild, StringComparison.Ordinal);
        Assert.Contains("uMergedCascadeImage", GpuComputeShaderSources.RadianceCascadeMerge, StringComparison.Ordinal);
        Assert.Contains("uRadianceTexture", GpuComputeShaderSources.RadianceCascadeApply, StringComparison.Ordinal);

        Assert.Contains("GPU->CPU readback", GpuComputeShaderSources.RadianceCascadeSdfJfa, StringComparison.Ordinal);
        Assert.Contains("GPU->CPU readback", GpuComputeShaderSources.RadianceCascadeBuild, StringComparison.Ordinal);
        Assert.Contains("GPU->CPU readback", GpuComputeShaderSources.RadianceCascadeMerge, StringComparison.Ordinal);
        Assert.Contains("GPU->CPU readback", GpuComputeShaderSources.RadianceCascadeApply, StringComparison.Ordinal);
    }

    [Fact]
    public void ParticlePointSpriteSourcesExposeRenderSideContracts()
    {
        Assert.Equal("particle_pointsprite.vert", GpuComputeShaderSources.ParticlePointSpriteVertexName);
        Assert.Equal("particle_pointsprite.frag", GpuComputeShaderSources.ParticlePointSpriteFragmentName);

        string vertex = GpuComputeShaderSources.ParticlePointSpriteVertex;
        Assert.Contains("#version 430", vertex, StringComparison.Ordinal);
        Assert.Contains("particle_pointsprite.vert", vertex, StringComparison.Ordinal);
        Assert.Contains("void main()", vertex, StringComparison.Ordinal);
        Assert.Contains("aWorldPosition", vertex, StringComparison.Ordinal);
        Assert.Contains("aMaterialId", vertex, StringComparison.Ordinal);
        Assert.Contains("aColorVariant", vertex, StringComparison.Ordinal);
        Assert.Contains("uCameraWorldOrigin", vertex, StringComparison.Ordinal);
        Assert.Contains("uViewportSize", vertex, StringComparison.Ordinal);
        Assert.Contains("uPixelsPerWorldUnit", vertex, StringComparison.Ordinal);
        Assert.Contains("gl_Position", vertex, StringComparison.Ordinal);
        Assert.Contains("gl_PointSize", vertex, StringComparison.Ordinal);
        Assert.Contains("vMaterialId", vertex, StringComparison.Ordinal);
        Assert.Contains("vColorVariant", vertex, StringComparison.Ordinal);

        string fragment = GpuComputeShaderSources.ParticlePointSpriteFragment;
        Assert.Contains("#version 430", fragment, StringComparison.Ordinal);
        Assert.Contains("particle_pointsprite.frag", fragment, StringComparison.Ordinal);
        Assert.Contains("void main()", fragment, StringComparison.Ordinal);
        Assert.Contains("gl_PointCoord", fragment, StringComparison.Ordinal);
        Assert.Contains("vColor", fragment, StringComparison.Ordinal);
        Assert.Contains("vMaterialId", fragment, StringComparison.Ordinal);
        Assert.Contains("vColorVariant", fragment, StringComparison.Ordinal);
        Assert.Contains("oSceneColor", fragment, StringComparison.Ordinal);
        Assert.Contains("oEmissiveColor", fragment, StringComparison.Ordinal);
        Assert.Contains("uEmissiveScale", fragment, StringComparison.Ordinal);
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
