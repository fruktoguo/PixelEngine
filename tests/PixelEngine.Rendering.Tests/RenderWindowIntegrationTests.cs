using PixelEngine.Rendering.Compute;
using Silk.NET.OpenGL;
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

        if (window.Capabilities.HasBufferStorage)
        {
            using PboUploader persistentUploader = new(
                window.Gl,
                buffer.ByteLength,
                window.Capabilities,
                PboUploadMode.PersistentMapped);
            persistentUploader.UploadFull(texture, buffer);
            Assert.Equal(PboUploadMode.PersistentMapped, persistentUploader.Mode);
        }

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

        using RenderWindow window = CreateSmokeWindow("PixelEngine pipeline smoke", RenderBackendPreference.Auto);
        RenderPipelineFrame(window);
    }

    [Fact]
    public void CanRenderFrameThroughComputeBloomWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = CreateSmokeWindow("PixelEngine compute bloom smoke", RenderBackendPreference.Auto);
        RenderPipelineFrame(window, preferComputeLighting: true);
    }

    [Fact]
    public void ComputeBloomMatchesFragmentBloomForSolidInputWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = CreateSmokeWindow("PixelEngine compute bloom equivalence smoke", RenderBackendPreference.Auto);
        GpuCapabilities gpuCapabilities = GpuCapabilities.Query(window.Gl, window.Capabilities);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(gpuCapabilities, ComputeFeatureSwitches.Default, preferComputeSharp: false);
        if (!gate.GlComputeAvailable)
        {
            return;
        }

        using FullscreenQuad quad = new(window.Gl);
        using ColorRenderTarget scene = new(window.Gl, 16, 16);
        using ColorRenderTarget fragmentOutput = new(window.Gl, 16, 16);
        using ColorRenderTarget computeOutput = new(window.Gl, 16, 16);
        using BloomPass fragmentBloom = new(window.Gl, GlslProfile.DesktopGl330);
        using IComputeBackend backend = ComputeBackendFactory.Create(window.Gl, gate);
        using ComputeBloomPass computeBloom = new(window.Gl, new GpuComputeBloomPipeline(backend));
        BloomSettings settings = new(BloomMode.DualKawase, Threshold: 0.1f, Intensity: 0.75f, Iterations: 3, KawaseOffset: 1.5f);

        scene.BindFramebuffer();
        window.Gl.Viewport(0, 0, 16, 16);
        window.Gl.ClearColor(0.9f, 0.45f, 0.2f, 1f);
        window.Gl.Clear(ClearBufferMask.ColorBufferBit);

        fragmentBloom.Render(scene, fragmentOutput, quad, settings);
        computeBloom.Render(scene, computeOutput, settings);
        window.Gl.Finish();

        byte[] fragmentPixels = ReadTarget(window, fragmentOutput);
        byte[] computePixels = ReadTarget(window, computeOutput);

        Assert.Equal(fragmentPixels.Length, computePixels.Length);
        for (int i = 0; i < fragmentPixels.Length; i++)
        {
            int delta = Math.Abs(fragmentPixels[i] - computePixels[i]);
            Assert.True(delta <= 2, $"像素 byte {i} 不等价：fragment={fragmentPixels[i]} compute={computePixels[i]} delta={delta}");
        }
    }

    [Fact]
    public void CanRunRadianceCascadePassWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = CreateSmokeWindow("PixelEngine radiance cascades smoke", RenderBackendPreference.Auto);
        GpuCapabilities gpuCapabilities = GpuCapabilities.Query(window.Gl, window.Capabilities);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(gpuCapabilities, ComputeFeatureSwitches.Default, preferComputeSharp: false);
        if (!gate.GlComputeAvailable)
        {
            return;
        }

        using IComputeBackend backend = ComputeBackendFactory.Create(window.Gl, gate);
        using LightMaskTexture occluder = new(window.Gl, 16, 16);
        using EmissiveBuffer emissive = new(window.Gl, 16, 16);
        using ColorRenderTarget scene = new(window.Gl, 16, 16);
        using ColorRenderTarget destination = new(window.Gl, 16, 16);
        using RadianceCascadePass pass = new(window.Gl, new GpuRadianceCascadePipeline(backend), 16, 16);
        byte[] occluderMask = new byte[16 * 16];
        uint[] emissivePixels = new uint[16 * 16];
        occluderMask[8 + (8 * 16)] = 255;
        emissivePixels[4 + (4 * 16)] = 0xFFFFFFFFu;
        occluder.Upload(occluderMask);
        emissive.Upload(emissivePixels);
        scene.BindFramebuffer();
        window.Gl.Viewport(0, 0, 16, 16);
        window.Gl.ClearColor(0.1f, 0.1f, 0.12f, 1f);
        window.Gl.Clear(ClearBufferMask.ColorBufferBit);

        RadianceCascadeSettings settings = RadianceCascadeSettings.Default with
        {
            Enabled = true,
            CascadeCount = 2,
            BaseRayCount = 8,
            MaxRaySteps = 4,
        };
        pass.Render(occluder, emissive, scene, destination, settings);
        window.Gl.Finish();

        Assert.Equal(16, destination.Width);
    }

    [Fact]
    public void CanRenderFrameThroughGlesAngleWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_ANGLE_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = CreateSmokeWindow("PixelEngine ANGLE smoke", RenderBackendPreference.GlEs30Angle);

        Assert.Equal(RenderBackend.GlEs30Angle, window.Backend);
        Assert.True(window.Capabilities.IsGles, BackendMessage(window));
        Assert.True(
            window.Capabilities.MajorVersion > 3 ||
            (window.Capabilities.MajorVersion == 3 && window.Capabilities.MinorVersion >= 0),
            BackendMessage(window));
        Assert.False(window.Capabilities.HasBufferStorage);

        RenderPipelineFrame(window);
    }

    private static RenderWindow CreateSmokeWindow(string title, RenderBackendPreference preference)
    {
        return RenderWindow.Create(new RenderWindowOptions
        {
            Title = title,
            Width = 64,
            Height = 64,
            BackendPreference = preference,
            EnableDebugContext = true,
        });
    }

    private static void RenderPipelineFrame(RenderWindow window, bool preferComputeLighting = false)
    {
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
        pipeline.Settings.PreferComputeLighting = preferComputeLighting;
        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 16), [], overlays);
        window.SwapBuffers();

        Assert.Equal(16, pipeline.Width);
    }

    private static string BackendMessage(RenderWindow window)
    {
        return $"{window.Backend}: {window.Capabilities.Version} / {window.Capabilities.Renderer} / {window.Capabilities.Vendor}";
    }

    private static byte[] ReadTarget(RenderWindow window, ColorRenderTarget target)
    {
        byte[] rgba = new byte[checked(target.Width * target.Height * 4)];
        target.BindFramebuffer();
        window.Gl.ReadPixels<byte>(0, 0, (uint)target.Width, (uint)target.Height, GLEnum.Rgba, GLEnum.UnsignedByte, out rgba[0]);
        return rgba;
    }
}
