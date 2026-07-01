using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class RenderWindowIntegrationTests
{
    [Fact]
    public void CanCreateWindowWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine GL smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });

        window.DoEvents();
        window.SwapBuffers();
        Assert.True(window.Capabilities.MajorVersion >= 3);
    }
}
