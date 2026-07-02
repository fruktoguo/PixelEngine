using Silk.NET.Windowing;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class RenderBackendSelectorTests
{
    [Fact]
    public void AutoPreferenceTriesDesktopThenGles()
    {
        ReadOnlySpan<RenderBackend> order = RenderBackendSelector.GetAttemptOrder(RenderBackendPreference.Auto);

        Assert.Equal(2, order.Length);
        Assert.Equal(RenderBackend.DesktopGl33, order[0]);
        Assert.Equal(RenderBackend.GlEs30Angle, order[1]);
    }

    [Fact]
    public void DesktopOptionsRequestOpenGl33Core()
    {
        RenderWindowOptions options = new()
        {
            Width = 320,
            Height = 180,
            Title = "render-test",
            EnableDebugContext = true,
        };

        WindowOptions windowOptions = RenderBackendSelector.CreateWindowOptions(options, RenderBackend.DesktopGl33);

        Assert.Equal(ContextAPI.OpenGL, windowOptions.API.API);
        Assert.Equal(ContextProfile.Core, windowOptions.API.Profile);
        Assert.Equal(ContextFlags.Debug, windowOptions.API.Flags);
        Assert.Equal(new APIVersion(3, 3), windowOptions.API.Version);
        Assert.Equal("render-test", windowOptions.Title);
        Assert.Equal(320, windowOptions.Size.X);
        Assert.Equal(180, windowOptions.Size.Y);
        Assert.True(windowOptions.VSync);
        Assert.Equal(0, windowOptions.FramesPerSecond);
        Assert.Equal(0, windowOptions.UpdatesPerSecond);
        Assert.False(windowOptions.ShouldSwapAutomatically);
    }

    [Fact]
    public void GlesOptionsRequestOpenGles30()
    {
        RenderWindowOptions options = new()
        {
            EnableDebugContext = false,
        };

        WindowOptions windowOptions = RenderBackendSelector.CreateWindowOptions(options, RenderBackend.GlEs30Angle);

        Assert.Equal(ContextAPI.OpenGLES, windowOptions.API.API);
        Assert.Equal(ContextProfile.Core, windowOptions.API.Profile);
        Assert.Equal(ContextFlags.Default, windowOptions.API.Flags);
        Assert.Equal(new APIVersion(3, 0), windowOptions.API.Version);
    }

    [Fact]
    public void WindowTimingOptionsAreForwardedToSilk()
    {
        RenderWindowOptions options = new()
        {
            VSync = false,
            FramesPerSecond = 144,
            UpdatesPerSecond = 60,
        };

        WindowOptions windowOptions = RenderBackendSelector.CreateWindowOptions(options, RenderBackend.DesktopGl33);

        Assert.False(windowOptions.VSync);
        Assert.Equal(144, windowOptions.FramesPerSecond);
        Assert.Equal(60, windowOptions.UpdatesPerSecond);
        Assert.False(windowOptions.ShouldSwapAutomatically);
    }

    [Fact]
    public void RejectsInvalidWindowTimingRates()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderBackendSelector.CreateWindowOptions(
            new RenderWindowOptions { FramesPerSecond = double.NaN },
            RenderBackend.DesktopGl33));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderBackendSelector.CreateWindowOptions(
            new RenderWindowOptions { UpdatesPerSecond = -1 },
            RenderBackend.DesktopGl33));
    }
}
