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

    [Fact]
    public void CanCreateLightingResourcesAndRunCompositeWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine lighting smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });
        GlslProfile profile = window.Capabilities.IsGles ? GlslProfile.Gles300 : GlslProfile.DesktopGl330;
        RenderBuffer buffer = new(16, 16);
        using WorldTexture world = new(window.Gl, buffer.Width, buffer.Height);
        using PboUploader uploader = new(window.Gl, buffer.ByteLength);
        using EmissiveBuffer emissive = new(window.Gl, buffer.Width, buffer.Height);
        using LightMaskTexture occluder = new(window.Gl, buffer.Width, buffer.Height);
        using LightMaskTexture visibility = new(window.Gl, buffer.Width, buffer.Height);
        using ColorRenderTarget scene = new(window.Gl, buffer.Width, buffer.Height);
        using ColorRenderTarget postA = new(window.Gl, buffer.Width, buffer.Height);
        using ColorRenderTarget postB = new(window.Gl, buffer.Width, buffer.Height);
        using FullscreenQuad quad = new(window.Gl);
        using ShadowMap1DPass shadow = new(window.Gl, profile, 32);
        using CompositePass composite = new(window.Gl, profile);
        using BloomPass bloom = new(window.Gl, profile);
        using DitherPass dither = new(window.Gl, profile);
        using GammaPass gamma = new(window.Gl, profile);
        using CrtPass crt = new(window.Gl, profile);
        byte[] mask = new byte[buffer.Width * buffer.Height];
        mask.AsSpan().Fill(255);

        buffer.Pixels.Fill(0xFF102030u);
        uploader.UploadFull(world, buffer);
        emissive.Upload(buffer.Pixels);
        occluder.Upload(mask);
        visibility.Upload(mask);
        shadow.Render(occluder, new LightSource(8f, 8f, 12f, 0xFFFFFFFFu, 1f), quad);
        scene.BindFramebuffer();
        window.Gl.Viewport(0, 0, (uint)scene.Width, (uint)scene.Height);
        composite.Render(world, emissive, visibility, quad);
        bloom.Render(scene, postA, quad, BloomSettings.Default);
        dither.Render(postA, postB, quad);
        gamma.Render(postB, postA, quad);
        crt.Render(postA, postB, quad);
        window.SwapBuffers();

        Assert.Equal(32, shadow.RayCount);
    }

    [Fact]
    public void CanRenderOverlayWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine overlay smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });
        GlslProfile profile = window.Capabilities.IsGles ? GlslProfile.Gles300 : GlslProfile.DesktopGl330;
        using ColorRenderTarget target = new(window.Gl, 64, 64);
        using WorldTexture spriteTexture = new(window.Gl, 8, 8);
        using OverlayRenderer overlay = new(window.Gl, profile, 4);
        OverlayCommand[] commands =
        [
            OverlayCommand.SolidRectangle(4f, 4f, 16f, 10f, 0x80FF0000u),
            OverlayCommand.OutlineRectangle(24f, 4f, 20f, 14f, 2f, 0xFF00FF00u),
            OverlayCommand.SpriteRectangle(8f, 28f, 16f, 16f, new OverlaySprite(spriteTexture.Handle, spriteTexture.Width, spriteTexture.Height)),
        ];

        overlay.Render(commands, target);
        window.SwapBuffers();

        Assert.Equal(4, overlay.MaxCommandCount);
    }

    [Fact]
    public void CanRenderFrameThroughRenderPipelineWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine pipeline smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });
        using RenderPipeline pipeline = new(window, 16, 16);
        RenderBuffer buffer = new(16, 16);
        RenderAuxBuffers aux = new(16, 16);
        buffer.Pixels.Fill(0xFF203040u);
        aux.Emissive[0] = 0xFF808080u;
        aux.Occluder[5] = 255;
        OverlayCommand[] overlays =
        [
            OverlayCommand.SolidRectangle(2f, 2f, 4f, 4f, 0x80FFFFFFu),
            OverlayCommand.OutlineRectangle(1f, 1f, 8f, 8f, 1f, 0xFFFF0000u),
        ];

        pipeline.Settings.EnableCrt = true;
        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 16), [], overlays);

        Assert.Equal(16, pipeline.Width);
    }
}
