using PixelEngine.Rendering;
using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class RmlUiNativeProfileGateTests
{
    [Fact]
    public void DesktopGl33AllowsRmlUiGl3Renderer()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "4.6.0 NVIDIA 551.23",
            "NVIDIA GeForce",
            "NVIDIA Corporation",
            []);

        bool allowed = RmlUiNativeProfileGate.CanUseDesktopGl3(capabilities, out string? fallbackReason);

        Assert.True(allowed);
        Assert.Null(fallbackReason);
    }

    [Fact]
    public void GlesAngleRequestFallsBackWithExplicitRendererReason()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "OpenGL ES 3.0 ANGLE",
            "ANGLE renderer",
            "Google Inc.",
            []);

        bool allowed = RmlUiNativeProfileGate.CanUseDesktopGl3(capabilities, out string? fallbackReason);

        Assert.False(allowed);
        Assert.NotNull(fallbackReason);
        Assert.Contains("desktop GL3 renderer", fallbackReason, StringComparison.Ordinal);
        Assert.Contains("GLES/ANGLE", fallbackReason, StringComparison.Ordinal);
        Assert.Contains("GLES3/ANGLE renderer", fallbackReason, StringComparison.Ordinal);
        Assert.Contains("ManagedFallback", fallbackReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AngleDesktopLookingContextStillFallsBackInsteadOfUsingGl3Renderer()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "4.1.0",
            "ANGLE (NVIDIA, Vulkan 1.3)",
            "Google Inc.",
            []);

        bool allowed = RmlUiNativeProfileGate.CanUseDesktopGl3(capabilities, out string? fallbackReason);

        Assert.False(allowed);
        Assert.NotNull(fallbackReason);
        Assert.Contains("ANGLE", fallbackReason, StringComparison.Ordinal);
        Assert.Contains("GL3 renderer", fallbackReason, StringComparison.Ordinal);
        Assert.Contains("ManagedFallback", fallbackReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitGlesAngleBackendRequestFallsBackEvenWhenCapabilityStringLooksDesktop()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "4.6.0 NVIDIA 551.23",
            "NVIDIA GeForce",
            "NVIDIA Corporation",
            []);

        bool allowed = RmlUiNativeProfileGate.CanUseDesktopGl3(RenderBackend.GlEs30Angle, capabilities, out string? fallbackReason);

        Assert.False(allowed);
        Assert.NotNull(fallbackReason);
        Assert.Contains("GLES/ANGLE request", fallbackReason, StringComparison.Ordinal);
        Assert.Contains("GL3 renderer", fallbackReason, StringComparison.Ordinal);
        Assert.Contains("ManagedFallback", fallbackReason, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopGlBelow33FallsBackWithVersionReason()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "3.2.0 Mesa",
            "llvmpipe",
            "Mesa",
            []);

        bool allowed = RmlUiNativeProfileGate.CanUseDesktopGl3(capabilities, out string? fallbackReason);

        Assert.False(allowed);
        Assert.NotNull(fallbackReason);
        Assert.Contains("OpenGL 3.3+", fallbackReason, StringComparison.Ordinal);
        Assert.Contains("ManagedFallback", fallbackReason, StringComparison.Ordinal);
    }
}
