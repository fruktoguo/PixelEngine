using PixelEngine.Rendering;
using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// RmlUi 原生配置门控测试：特性开关与降级路径。
/// </summary>
public sealed class RmlUiNativeProfileGateTests
{
    /// <summary>
    /// 验证Desktop Gl33Allows Rml Ui Gl3Renderer。
    /// </summary>
    [Fact]
    public void DesktopGl33AllowsRmlUiGl3Renderer()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "4.6.0 NVIDIA 551.23",
            "NVIDIA GeForce",
            "NVIDIA Corporation",
            []);

        bool allowed = RmlUiNativeProfileGate.CanUseDesktopGl3(capabilities, out string? fallbackReason);
        RmlUiNativeProfileDecision decision = RmlUiNativeProfileGate.Evaluate(RenderBackend.DesktopGl33, capabilities);

        Assert.True(allowed);
        Assert.Null(fallbackReason);
        Assert.Equal(RmlUiNativeRendererProfile.DesktopGl3, decision.RequestedProfile);
        Assert.True(decision.CanUseNativeRenderer);
        Assert.Equal("RmlUi_Renderer_GL3", decision.NativeRendererSymbol);
        Assert.Equal("#version 330 core", decision.ShaderVersionDirective);
        Assert.True(decision.RequiresSameContextFunctionResolver);
        Assert.Null(decision.FallbackReason);
        Assert.Equal(RmlUiNativeProfileGate.NativeProfileDesktopGl3, RmlUiNativeProfileGate.ToNativeProfileId(decision.RequestedProfile));
    }

    /// <summary>
    /// 验证Gles Angle Request选择Gles3Angle Native Profile。
    /// </summary>
    [Fact]
    public void GlesAngleRequestSelectsGles3AngleNativeProfile()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "OpenGL ES 3.0 ANGLE",
            "ANGLE renderer",
            "Google Inc.",
            []);

        bool desktopOnly = RmlUiNativeProfileGate.CanUseDesktopGl3(capabilities, out string? desktopFallback);
        bool nativeAllowed = RmlUiNativeProfileGate.CanUseNativeRenderer(
            RenderBackend.DesktopGl33,
            capabilities,
            out string? nativeFallback);
        RmlUiNativeProfileDecision decision = RmlUiNativeProfileGate.Evaluate(RenderBackend.DesktopGl33, capabilities);

        Assert.False(desktopOnly);
        Assert.NotNull(desktopFallback);
        Assert.True(nativeAllowed);
        Assert.Null(nativeFallback);
        Assert.Equal(RmlUiNativeRendererProfile.Gles3Angle, decision.RequestedProfile);
        Assert.True(decision.CanUseNativeRenderer);
        Assert.Equal("RmlUi_Renderer_GLES3_ANGLE", decision.NativeRendererSymbol);
        Assert.Equal("#version 300 es", decision.ShaderVersionDirective);
        Assert.True(decision.RequiresSameContextFunctionResolver);
        Assert.Equal(RmlUiNativeProfileGate.NativeProfileGles3Angle, RmlUiNativeProfileGate.ToNativeProfileId(decision.RequestedProfile));
    }

    /// <summary>
    /// 验证Angle Desktop Looking Context选择Gles3Angle Instead Of Desktop Gl3。
    /// </summary>
    [Fact]
    public void AngleDesktopLookingContextSelectsGles3AngleInsteadOfDesktopGl3()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "4.1.0",
            "ANGLE (NVIDIA, Vulkan 1.3)",
            "Google Inc.",
            []);

        bool desktopOnly = RmlUiNativeProfileGate.CanUseDesktopGl3(capabilities, out _);
        RmlUiNativeProfileDecision decision = RmlUiNativeProfileGate.Evaluate(RenderBackend.DesktopGl33, capabilities);

        Assert.False(desktopOnly);
        Assert.Equal(RmlUiNativeRendererProfile.Gles3Angle, decision.RequestedProfile);
        Assert.True(decision.CanUseNativeRenderer);
        Assert.Equal("#version 300 es", decision.ShaderVersionDirective);
        Assert.Equal("RmlUi_Renderer_GLES3_ANGLE", decision.NativeRendererSymbol);
    }

    /// <summary>
    /// 验证Explicit Gles Angle Backend Request选择Gles Profile Even When Capability String Looks Desktop。
    /// </summary>
    [Fact]
    public void ExplicitGlesAngleBackendRequestSelectsGlesProfileEvenWhenCapabilityStringLooksDesktop()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "4.6.0 NVIDIA 551.23",
            "NVIDIA GeForce",
            "NVIDIA Corporation",
            []);

        bool desktopOnly = RmlUiNativeProfileGate.CanUseDesktopGl3(
            RenderBackend.GlEs30Angle,
            capabilities,
            out _);
        RmlUiNativeProfileDecision decision = RmlUiNativeProfileGate.Evaluate(RenderBackend.GlEs30Angle, capabilities);

        Assert.False(desktopOnly);
        Assert.Equal(RmlUiNativeRendererProfile.Gles3Angle, decision.RequestedProfile);
        Assert.True(decision.CanUseNativeRenderer);
        Assert.Equal("RmlUi_Renderer_GLES3_ANGLE", decision.NativeRendererSymbol);
        Assert.Equal("#version 300 es", decision.ShaderVersionDirective);
    }

    /// <summary>
    /// 验证Desktop Gl Below33回退With Version Reason。
    /// </summary>
    [Fact]
    public void DesktopGlBelow33FallsBackWithVersionReason()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "3.2.0 Mesa",
            "llvmpipe",
            "Mesa",
            []);

        bool allowed = RmlUiNativeProfileGate.CanUseDesktopGl3(capabilities, out string? fallbackReason);
        RmlUiNativeProfileDecision decision = RmlUiNativeProfileGate.Evaluate(RenderBackend.DesktopGl33, capabilities);

        Assert.False(allowed);
        Assert.NotNull(fallbackReason);
        Assert.Contains("OpenGL 3.3+", fallbackReason, StringComparison.Ordinal);
        Assert.Contains("ManagedFallback", fallbackReason, StringComparison.Ordinal);
        Assert.Equal(RmlUiNativeRendererProfile.DesktopGl3, decision.RequestedProfile);
        Assert.False(decision.CanUseNativeRenderer);
        Assert.Equal("RmlUi_Renderer_GL3", decision.NativeRendererSymbol);
        Assert.Equal("#version 330 core", decision.ShaderVersionDirective);
    }

    /// <summary>
    /// 验证Gles Below30回退With Es Version Reason。
    /// </summary>
    [Fact]
    public void GlesBelow30FallsBackWithEsVersionReason()
    {
        GlCapabilities capabilities = GlCapabilities.FromRaw(
            "OpenGL ES 2.0",
            "WebGL 1.0",
            "Google Inc.",
            []);

        bool nativeAllowed = RmlUiNativeProfileGate.CanUseNativeRenderer(
            RenderBackend.GlEs30Angle,
            capabilities,
            out string? fallbackReason);
        RmlUiNativeProfileDecision decision = RmlUiNativeProfileGate.Evaluate(RenderBackend.GlEs30Angle, capabilities);

        Assert.False(nativeAllowed);
        Assert.NotNull(fallbackReason);
        Assert.Contains("OpenGL ES 3.0+", fallbackReason, StringComparison.Ordinal);
        Assert.Contains("ManagedFallback", fallbackReason, StringComparison.Ordinal);
        Assert.Contains("#version 330", fallbackReason, StringComparison.Ordinal);
        Assert.Equal(RmlUiNativeRendererProfile.Gles3Angle, decision.RequestedProfile);
        Assert.False(decision.CanUseNativeRenderer);
    }
}
