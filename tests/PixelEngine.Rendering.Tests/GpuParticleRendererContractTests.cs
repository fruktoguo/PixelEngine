using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class GpuParticleRendererContractTests
{
    [Fact]
    public void ParticleRenderModeDefaultsToCpuStampAndValidatesEnum()
    {
        RenderPipelineSettings settings = new();

        Assert.Equal(ParticleRenderMode.CpuStamp, settings.ParticleRenderMode);

        settings.ParticleRenderMode = (ParticleRenderMode)99;
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
        Assert.Contains(nameof(RenderPipelineSettings.ParticleRenderMode), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GpuParticleRendererDeclaresStablePackedVertexContract()
    {
        Assert.Equal(10 * sizeof(float), GpuParticleRenderer.VertexStrideBytes);

        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "GpuParticleRenderer.cs"));
        Assert.Contains("GC.AllocateArray<GpuParticleVertex>(initialCapacity, pinned: true)", source, StringComparison.Ordinal);
        Assert.Contains("BufferSubData", source, StringComparison.Ordinal);
        Assert.Contains("DrawArrays(PrimitiveType.Points", source, StringComparison.Ordinal);
        Assert.Contains("BlendFunc(BlendingFactor.One, BlendingFactor.One)", source, StringComparison.Ordinal);
        Assert.Contains("ReadOnlySpan<Particle> particles", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GpuParticleShaderSourcesExposeCameraAndPointSpriteContracts()
    {
        string vertex = GpuParticleShaderSources.Vertex(GlslProfile.DesktopGl330);
        Assert.Contains("aWorldPosition", vertex, StringComparison.Ordinal);
        Assert.Contains("aMaterialId", vertex, StringComparison.Ordinal);
        Assert.Contains("aColorVariant", vertex, StringComparison.Ordinal);
        Assert.Contains("uCameraWorldOrigin", vertex, StringComparison.Ordinal);
        Assert.Contains("uCellsPerPixel", vertex, StringComparison.Ordinal);
        Assert.Contains("gl_PointSize", vertex, StringComparison.Ordinal);

        string fragment = GpuParticleShaderSources.Fragment(GlslProfile.DesktopGl330);
        Assert.Contains("gl_PointCoord", fragment, StringComparison.Ordinal);
        Assert.Contains("uEmissivePass", fragment, StringComparison.Ordinal);
        Assert.Contains("discard", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderPipelineOrdersGpuParticlesBetweenWorldAndOverlay()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderPipeline.cs"));

        Assert.Contains("RenderGpuParticlesIfEnabled", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("_worldBlit.Render", StringComparison.Ordinal) < source.IndexOf("RenderGpuParticlesIfEnabled", StringComparison.Ordinal));
        Assert.True(source.IndexOf("RenderGpuParticlesIfEnabled", StringComparison.Ordinal) < source.IndexOf("_overlay.Render", StringComparison.Ordinal));
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
