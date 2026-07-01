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

    [Fact]
    public void CanUploadWorldTextureThroughPboWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine PBO smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });
        RenderBuffer buffer = new(16, 16);
        buffer.Pixels.Fill(0xFF204060u);

        using WorldTexture texture = new(window.Gl, buffer.Width, buffer.Height);
        using PboUploader uploader = new(window.Gl, buffer.ByteLength);

        uploader.UploadFull(texture, buffer);
        buffer.Pixels[0] = 0xFFFFFFFFu;
        uploader.UploadDirtyRects(texture, buffer, [new PixelUploadRect(0, 0, 4, 4)]);
        window.SwapBuffers();

        Assert.True(uploader.CapacityBytes >= buffer.ByteLength);
    }
}
