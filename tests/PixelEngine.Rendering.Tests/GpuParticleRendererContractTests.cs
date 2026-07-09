using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// GPU 粒子渲染器契约测试：缓冲布局与绘制调用约定。
/// </summary>
public sealed class GpuParticleRendererContractTests
{
    /// <summary>
    /// 验证Particle Render Mode Defaults To Cpu Stamp And Validates Enum。
    /// </summary>
    [Fact]
    public void ParticleRenderModeDefaultsToCpuStampAndValidatesEnum()
    {
        RenderPipelineSettings settings = new();

        Assert.Equal(ParticleRenderMode.CpuStamp, settings.ParticleRenderMode);

        settings.ParticleRenderMode = (ParticleRenderMode)99;
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
        Assert.Contains(nameof(RenderPipelineSettings.ParticleRenderMode), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Gpu Particle Renderer Declares Stable Packed Vertex Contract。
    /// </summary>
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

    /// <summary>
    /// 验证Gpu Particle Upload不会Reallocate Vbo On Steady Frames。
    /// </summary>
    [Fact]
    public void GpuParticleUploadDoesNotReallocateVboOnSteadyFrames()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "GpuParticleRenderer.cs"));
        int uploadStart = source.IndexOf("private void UploadVertices", StringComparison.Ordinal);
        int uploadEnd = source.IndexOf("private void ConfigureVertexAttributes", StringComparison.Ordinal);
        Assert.True(uploadStart > 0);
        Assert.True(uploadEnd > uploadStart);

        string uploadMethod = source[uploadStart..uploadEnd];
        Assert.Contains("BufferSubData", uploadMethod, StringComparison.Ordinal);
        Assert.DoesNotContain(".Allocate(", uploadMethod, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Gpu Particle Shader Sources Expose Camera And Point Sprite Contracts。
    /// </summary>
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

    /// <summary>
    /// 验证Render Pipeline Orders Gpu Particles Between World And Overlay行为符合预期。
    /// </summary>
    [Fact]
    public void RenderPipelineOrdersGpuParticlesBetweenWorldAndOverlay()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderPipeline.cs"));

        Assert.Contains("RenderGpuParticlesIfEnabled", source, StringComparison.Ordinal);
        Assert.Contains("_computeGate.FeatureSwitches.GpuParticlesEnabled", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("_worldBlit.Render", StringComparison.Ordinal) < source.IndexOf("RenderGpuParticlesIfEnabled", StringComparison.Ordinal));
        Assert.True(source.IndexOf("RenderGpuParticlesIfEnabled", StringComparison.Ordinal) < source.IndexOf("_overlay.Render", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证Render Pipeline Gates Gpu Particles By G4Feature Switch行为符合预期。
    /// </summary>
    [Fact]
    public void RenderPipelineGatesGpuParticlesByG4FeatureSwitch()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.Rendering", "RenderPipeline.cs"));
        int methodStart = source.IndexOf("private void RenderGpuParticlesIfEnabled", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("private bool ShouldUseComputeBloom", StringComparison.Ordinal);

        Assert.True(methodStart > 0);
        Assert.True(methodEnd > methodStart);
        string method = source[methodStart..methodEnd];
        Assert.Contains("Settings.ParticleRenderMode != ParticleRenderMode.GpuPointSprite", method, StringComparison.Ordinal);
        Assert.Contains("!_computeGate.FeatureSwitches.GpuParticlesEnabled", method, StringComparison.Ordinal);
        Assert.True(method.IndexOf("!_computeGate.FeatureSwitches.GpuParticlesEnabled", StringComparison.Ordinal) < method.IndexOf("materials is null", StringComparison.Ordinal));
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
