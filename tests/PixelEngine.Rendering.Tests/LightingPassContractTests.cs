using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// 光照通道契约测试：输入输出绑定与 pass 顺序。
/// </summary>
public sealed class LightingPassContractTests
{
    /// <summary>
    /// 验证Composite Shader Uses World Visibility And Emissive Inputs。
    /// </summary>
    [Fact]
    public void CompositeShaderUsesWorldVisibilityAndEmissiveInputs()
    {
        string fragment = LightingShaderSources.CompositeFragment(GlslProfile.DesktopGl330);

        Assert.Contains("#version 330 core", fragment, StringComparison.Ordinal);
        Assert.Contains("uWorldTexture", fragment, StringComparison.Ordinal);
        Assert.Contains("uEmissiveTexture", fragment, StringComparison.Ordinal);
        Assert.Contains("uVisibilityTexture", fragment, StringComparison.Ordinal);
        Assert.Contains("uDecodeWorldSrgb", fragment, StringComparison.Ordinal);
        Assert.Contains("AuthoredSrgbToLinear", fragment, StringComparison.Ordinal);
        Assert.Contains("worldLinear * visibility", fragment, StringComparison.Ordinal);
        Assert.Contains("max(litWorld, emissive)", fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("+ emissive", fragment, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Shader Sources Support Gles Profile。
    /// </summary>
    [Fact]
    public void ShaderSourcesSupportGlesProfile()
    {
        string vertex = LightingShaderSources.FullscreenVertex(GlslProfile.Gles300);
        string composite = LightingShaderSources.CompositeFragment(GlslProfile.Gles300);
        string shadow = LightingShaderSources.Shadow1DFragment(GlslProfile.Gles300);

        Assert.Contains("#version 300 es", vertex, StringComparison.Ordinal);
        Assert.Contains("precision mediump float", composite, StringComparison.Ordinal);
        Assert.Contains("textureSize", shadow, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证Shadow Cpu Finds Nearest Occluder Per Ray。
    /// </summary>
    [Fact]
    public void ShadowCpuFindsNearestOccluderPerRay()
    {
        byte[] occluder = new byte[9 * 9];
        occluder[(6 * 9) + 6] = 255;
        Span<float> distances = stackalloc float[4];
        LightSource light = new(4f, 4f, 8f, 0xFFFFFFFFu, 1f);

        ShadowMap1DPass.ComputeCpu(occluder, 9, 9, light, distances);

        Assert.True(distances[0] < light.Radius);
        Assert.Equal(light.Radius, distances[1]);
        Assert.Equal(light.Radius, distances[2]);
        Assert.Equal(light.Radius, distances[3]);
    }

    /// <summary>
    /// 验证Shadow Cpu Validates Dimensions And Output。
    /// </summary>
    [Fact]
    public void ShadowCpuValidatesDimensionsAndOutput()
    {
        LightSource light = new(0f, 0f, 1f, 0xFFFFFFFFu, 1f);
        byte[] occluder = new byte[4];
        float[] distances = new float[1];
        float[] emptyDistances = [];

        AssertThrows<ArgumentOutOfRangeException>(() => ShadowMap1DPass.ComputeCpu(occluder, 0, 2, light, distances));
        AssertThrows<ArgumentException>(() => ShadowMap1DPass.ComputeCpu(occluder, 2, 3, light, distances));
        AssertThrows<ArgumentException>(() => ShadowMap1DPass.ComputeCpu(occluder, 2, 2, light, emptyDistances));
    }

    /// <summary>
    /// 验证Light Source Validation Rejects Invalid Values。
    /// </summary>
    [Fact]
    public void LightSourceValidationRejectsInvalidValues()
    {
        AssertThrows<ArgumentOutOfRangeException>(() => new LightSource(float.NaN, 0f, 1f, 0, 1f).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() => new LightSource(0f, 0f, 0f, 0, 1f).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() => new LightSource(0f, 0f, 1f, 0, -1f).Validate());
    }

    private static void AssertThrows<T>(Action action)
        where T : Exception
    {
        T exception = Assert.Throws<T>(action);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }
}
