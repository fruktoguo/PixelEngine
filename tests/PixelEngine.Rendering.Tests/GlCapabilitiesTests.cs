using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// GL 能力探测测试：GLES/桌面 GL 特性识别与回退标记。
/// </summary>
public sealed class GlCapabilitiesTests
{
    /// <summary>
    /// 验证Desktop Gl44Reports Compute And Buffer Storage。
    /// </summary>
    [Fact]
    public void DesktopGl44ReportsComputeAndBufferStorage()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "4.4.0 NVIDIA 551.23",
            "renderer",
            "vendor",
            []);

        Assert.False(capabilities.IsGles);
        Assert.False(capabilities.IsAngle);
        Assert.Equal(4, capabilities.MajorVersion);
        Assert.Equal(4, capabilities.MinorVersion);
        Assert.True(capabilities.HasComputeShader);
        Assert.True(capabilities.HasBufferStorage);
    }

    /// <summary>
    /// 验证Desktop Gl43Extension Reports Buffer Storage。
    /// </summary>
    [Fact]
    public void DesktopGl43ExtensionReportsBufferStorage()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "4.3 Mesa",
            "renderer",
            "vendor",
            ["GL_ARB_buffer_storage"]);

        Assert.True(capabilities.HasComputeShader);
        Assert.True(capabilities.HasBufferStorage);
    }

    /// <summary>
    /// 验证Gles30不会Report Compute By Default。
    /// </summary>
    [Fact]
    public void Gles30DoesNotReportComputeByDefault()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "OpenGL ES 3.0 ANGLE",
            "ANGLE",
            "vendor",
            []);

        Assert.True(capabilities.IsGles);
        Assert.True(capabilities.IsAngle);
        Assert.Equal(3, capabilities.MajorVersion);
        Assert.Equal(0, capabilities.MinorVersion);
        Assert.False(capabilities.HasComputeShader);
        Assert.False(capabilities.HasBufferStorage);
    }

    /// <summary>
    /// 验证Angle Renderer Reports Angle Even When Version Looks Desktop Gl。
    /// </summary>
    [Fact]
    public void AngleRendererReportsAngleEvenWhenVersionLooksDesktopGl()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "4.1.0",
            "ANGLE (NVIDIA, Vulkan 1.3)",
            "Google Inc.",
            []);

        Assert.False(capabilities.IsGles);
        Assert.True(capabilities.IsAngle);
        Assert.Equal(4, capabilities.MajorVersion);
        Assert.Equal(1, capabilities.MinorVersion);
    }
}
