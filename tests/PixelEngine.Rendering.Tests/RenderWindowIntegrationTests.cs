using System.Text.Json;
using PixelEngine.Rendering.Compute;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Testing;
using Silk.NET.OpenGL;
using Xunit;
using Xunit.Abstractions;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// RenderWindow 集成冒烟测试：需 PIXELENGINE_RENDERING_GL_SMOKE=1（ANGLE 用 PIXELENGINE_RENDERING_ANGLE_SMOKE=1），验证真实 GL 上下文与管线各阶段。
/// </summary>
[Trait("Category", "NativeSmoke")]
public sealed class RenderWindowIntegrationTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// 窗口层必须公开强类型平台 file-drop，并由订阅方显式管理生命周期。
    /// </summary>
    [Fact]
    public void WindowExposesTypedFileDropEvent()
    {
        System.Reflection.EventInfo? fileDrop =
            typeof(RenderWindow).GetEvent(nameof(RenderWindow.FilesDropped));

        Assert.NotNull(fileDrop);
        Assert.Equal(typeof(Action<string[]>), fileDrop.EventHandlerType);
    }

    /// <summary>
    /// 在 GL 冒烟环境变量启用时，能够创建窗口并交换缓冲区，且 Desktop GL 版本不低于 3.3。
    /// </summary>
    [NativeSmokeFact]
    public void CanCreateWindowWhenExplicitlyEnabled()
    {
        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine GL smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.DesktopGl33,
            EnableDebugContext = true,
        });

        window.DoEvents();
        window.SwapBuffers();
        Assert.False(window.Capabilities.IsGles, BackendMessage(window));
        Assert.True(
            window.Capabilities.MajorVersion > 3 ||
            (window.Capabilities.MajorVersion == 3 && window.Capabilities.MinorVersion >= 3),
            BackendMessage(window));
        WriteGraphicsCapability(window, "desktop-gl");
    }

    /// <summary>
    /// Windows capture-compatible presenter 所需的 WGL_NV_DX_interop2 入口必须由 desktop GL driver 暴露。
    /// </summary>
    [NativeSmokeFact]
    public void WindowsDesktopContextExposesDxInteropEntryPointsWhenExplicitlyEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine WGL DX interop smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.DesktopGl33,
        });

        string[] entryPoints =
        [
            "wglDXOpenDeviceNV",
            "wglDXCloseDeviceNV",
            "wglDXRegisterObjectNV",
            "wglDXUnregisterObjectNV",
            "wglDXLockObjectsNV",
            "wglDXUnlockObjectsNV",
        ];
        foreach (string entryPoint in entryPoints)
        {
            Assert.True(window.TryGetProcAddress(entryPoint, out IntPtr address) && address != IntPtr.Zero, entryPoint);
        }
    }

    /// <summary>
    /// Windows capture-compatible 后端必须真实创建共享 presentation FBO，完成 DXGI present，
    /// 并在窗口 resize 后重新注册 swap-chain backbuffer。
    /// </summary>
    [NativeSmokeFact]
    public void WindowsDxgiInteropPresenterRendersPresentsAndResizesWhenExplicitlyEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        List<string> diagnostics = [];
        using RenderWindow window = RenderWindow.Create(
            new RenderWindowOptions
            {
                Title = "PixelEngine DXGI GL presenter smoke",
                Width = 64,
                Height = 64,
                VSync = false,
                BackendPreference = RenderBackendPreference.CaptureCompatible,
            },
            diagnostics.Add);

        Assert.True(
            window.Backend == RenderBackend.DesktopGl33DxgiInterop,
            string.Join(Environment.NewLine, diagnostics));
        Assert.NotEqual(0u, window.PresentationFramebuffer);
        window.DoEvents();
        window.BindPresentationFramebuffer();
        Assert.Equal(
            GLEnum.FramebufferComplete,
            window.Gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
        window.Gl.Viewport(0, 0, (uint)window.Width, (uint)window.Height);
        window.Gl.ClearColor(0.12f, 0.35f, 0.78f, 1f);
        window.Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        window.SwapBuffers();

        window.Resize(96, 80);
        for (int i = 0; i < 16; i++)
        {
            window.DoEvents();
            if (window.Width == 96 && window.Height == 80)
            {
                break;
            }
        }

        window.BindPresentationFramebuffer();
        Assert.Equal(
            GLEnum.FramebufferComplete,
            window.Gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
        window.Gl.Viewport(0, 0, (uint)window.Width, (uint)window.Height);
        window.Gl.ClearColor(0.75f, 0.22f, 0.16f, 1f);
        window.Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        window.SwapBuffers();

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Contains("DesktopGl33DxgiInterop 创建失败", StringComparison.Ordinal));
    }

    /// <summary>
    /// 释放窗口后 IsClosing 应为 true。
    /// </summary>
    [NativeSmokeFact]
    public void IsClosingReturnsTrueAfterDisposeWhenExplicitlyEnabled()
    {

        RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine disposed window smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
        });

        window.Dispose();

        Assert.True(window.IsClosing);
    }

    /// <summary>
    /// 经 PBO 全量/脏矩形上传世界纹理；若支持则额外验证持久映射 PBO 模式。
    /// </summary>
    [NativeSmokeFact]
    public void CanUploadWorldTextureThroughPboWhenExplicitlyEnabled()
    {

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
        uint callerPbo = window.Gl.GenBuffer();
        window.Gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, callerPbo);
        using PboUploader uploader = new(window.Gl, buffer.ByteLength);
        window.Gl.GetInteger(GLEnum.PixelUnpackBufferBinding, out int bindingAfterConstruction);
        window.Gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
        window.Gl.DeleteBuffer(callerPbo);

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

        Assert.Equal((int)callerPbo, bindingAfterConstruction);
        Assert.True(uploader.CapacityBytes >= buffer.ByteLength);
    }

    /// <summary>
    /// render target 分配/resize 不得把已绑定 PBO 误当作 TexImage2D 数据源，并须恢复调用方 GL 状态。
    /// </summary>
    [NativeSmokeFact]
    public void ColorRenderTargetResizeIgnoresAndRestoresCallerPixelUnpackBuffer()
    {
        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine render target resize state smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });
        using GlBuffer callerBuffer = new(window.Gl, BufferTargetARB.PixelUnpackBuffer);
        callerBuffer.Bind();
        callerBuffer.Allocate(4, BufferUsageARB.StreamDraw);

        using ColorRenderTarget target = new(window.Gl, 8, 8);
        target.Resize(16, 16);
        window.Gl.GetInteger(GLEnum.PixelUnpackBufferBinding, out int restoredPbo);
        window.Gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);

        Assert.Equal((16, 16), (target.Width, target.Height));
        Assert.Equal((int)callerBuffer.Handle, restoredPbo);
        Assert.Equal(GLEnum.NoError, window.Gl.GetError());
    }

    /// <summary>
    /// UI 叠加纹理脏矩形上传后恢复 GL 像素解包状态与活动纹理单元。
    /// </summary>
    [NativeSmokeFact]
    public void UiOverlayTextureUploadsDirtyRectWhenExplicitlyEnabled()
    {

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine UI overlay upload smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });
        using UiOverlayTexture texture = new(window.Gl, 4, 4);
        uint[] pixels = new uint[16];

        texture.Upload(pixels);
        uint pbo = window.Gl.GenBuffer();
        window.Gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, pbo);
        window.Gl.PixelStore(GLEnum.UnpackRowLength, 9);
        window.Gl.PixelStore(GLEnum.UnpackSkipPixels, 3);
        window.Gl.PixelStore(GLEnum.UnpackSkipRows, 2);
        window.Gl.ActiveTexture(TextureUnit.Texture1);
        for (int y = 1; y <= 2; y++)
        {
            for (int x = 1; x <= 2; x++)
            {
                pixels[(y * 4) + x] = 0xFFFFFFFFu;
            }
        }

        texture.UploadDirtyRects(pixels, 4, 4, [new PixelUploadRect(1, 1, 2, 2)]);
        window.Gl.GetInteger(GLEnum.PixelUnpackBufferBinding, out int restoredPbo);
        window.Gl.GetInteger(GLEnum.UnpackRowLength, out int restoredRowLength);
        window.Gl.GetInteger(GLEnum.UnpackSkipPixels, out int restoredSkipPixels);
        window.Gl.GetInteger(GLEnum.UnpackSkipRows, out int restoredSkipRows);
        window.Gl.GetInteger(GLEnum.ActiveTexture, out int restoredActiveTexture);
        window.Gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
        window.Gl.DeleteBuffer(pbo);
        byte[] uploaded = ReadTextureRgba(window.Gl, texture.Handle, texture.Width, texture.Height);
        byte[] outsideA = RawTexturePixel(uploaded, texture.Width, x: 0, y: 0);
        byte[] insideA = RawTexturePixel(uploaded, texture.Width, x: 1, y: 1);
        byte[] insideB = RawTexturePixel(uploaded, texture.Width, x: 2, y: 2);
        byte[] outsideB = RawTexturePixel(uploaded, texture.Width, x: 3, y: 3);

        Assert.Equal((int)pbo, restoredPbo);
        Assert.Equal(9, restoredRowLength);
        Assert.Equal(3, restoredSkipPixels);
        Assert.Equal(2, restoredSkipRows);
        Assert.Equal((int)TextureUnit.Texture1, restoredActiveTexture);
        Assert.Equal([0, 0, 0, 0], outsideA);
        Assert.Equal([255, 255, 255, 255], insideA);
        Assert.Equal([255, 255, 255, 255], insideB);
        Assert.Equal([0, 0, 0, 0], outsideB);
    }

    /// <summary>
    /// 创建光照相关 GPU 资源并串联 shadow/composite/bloom/dither/gamma/crt 各 pass。
    /// </summary>
    [NativeSmokeFact]
    public void CanCreateLightingResourcesAndRunCompositeWhenExplicitlyEnabled()
    {

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

    /// <summary>
    /// 支持非 4 字节对齐宽度的光照遮罩上传。
    /// </summary>
    [NativeSmokeFact]
    public void CanUploadUnalignedLightMaskWidthWhenExplicitlyEnabled()
    {

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine light mask alignment smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });
        using LightMaskTexture texture = new(window.Gl, 7, 5);
        byte[] mask = new byte[texture.Width * texture.Height];
        mask.AsSpan().Fill(255);

        texture.Upload(mask);
        window.SwapBuffers();

        Assert.Equal(7, texture.Width);
        Assert.Equal(5, texture.Height);
    }

    /// <summary>
    /// WorldBlit 保持 CPU 第 0 行映射到视口顶部（Y 轴不翻转）。
    /// </summary>
    [NativeSmokeFact]
    public void WorldBlitKeepsCpuTopRowAtViewportTopWhenExplicitlyEnabled()
    {

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine world blit orientation smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });
        GlslProfile profile = window.Capabilities.IsGles ? GlslProfile.Gles300 : GlslProfile.DesktopGl330;
        RenderBuffer buffer = new(8, 8);
        using WorldTexture world = new(window.Gl, buffer.Width, buffer.Height);
        using PboUploader uploader = new(window.Gl, buffer.ByteLength);
        using ColorRenderTarget target = new(window.Gl, buffer.Width, buffer.Height);
        using FullscreenQuad quad = new(window.Gl);
        using WorldBlitPass blit = new(window.Gl, profile);

        for (int y = 0; y < buffer.Height; y++)
        {
            uint color = y < buffer.Height / 2 ? 0xFFFF0000u : 0xFF0000FFu;
            buffer.Pixels.Slice(y * buffer.Width, buffer.Width).Fill(color);
        }

        uploader.UploadFull(world, buffer);
        blit.Render(world, target, CameraState.OneToOne(0, 0, buffer.Width, buffer.Height), quad);
        target.BindFramebuffer();
        byte[] top = ReadPixelRgba(window.Gl, x: 4, y: buffer.Height - 1);
        byte[] bottom = ReadPixelRgba(window.Gl, x: 4, y: 0);

        Assert.True(top[0] > top[2], $"视口顶部应为 CPU 第 0 行红色，actual rgba=({top[0]},{top[1]},{top[2]},{top[3]})");
        Assert.True(bottom[2] > bottom[0], $"视口底部应为 CPU 末行蓝色，actual rgba=({bottom[0]},{bottom[1]},{bottom[2]},{bottom[3]})");
    }

    /// <summary>
    /// OverlayRenderer 能绘制实心/描边矩形与精灵命令。
    /// </summary>
    [NativeSmokeFact]
    public void CanRenderOverlayWhenExplicitlyEnabled()
    {

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
        target.BindFramebuffer();
        byte[] rectanglePixel = ReadPixelRgba(window.Gl, x: 8, y: 64 - 8 - 1);
        window.SwapBuffers();

        Assert.Equal(4, overlay.MaxCommandCount);
        Assert.True(rectanglePixel[0] > rectanglePixel[2], $"overlay 实色矩形应写入红色像素，actual rgba=({rectanglePixel[0]},{rectanglePixel[1]},{rectanglePixel[2]},{rectanglePixel[3]})");
    }

    private static byte[] ReadPixelRgba(GL gl, int x, int y)
    {
        byte[] pixel = new byte[4];
        gl.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);

        return pixel;
    }

    private static byte[] ReadTextureRgba(GL gl, uint texture, int width, int height)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        return pixels;
    }

    private static byte[] PixelAtTopLeftOrigin(byte[] pixels, int width, int x, int y)
    {
        int height = pixels.Length / (width * 4);
        int glY = height - 1 - y;
        int offset = ((glY * width) + x) * 4;
        return [pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]];
    }

    private static byte[] RawTexturePixel(byte[] pixels, int width, int x, int y)
    {
        int offset = ((y * width) + x) * 4;
        return [pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]];
    }

    private static int MaxRgb(byte[] rgba)
    {
        return Math.Max(rgba[0], Math.Max(rgba[1], rgba[2]));
    }

    /// <summary>
    /// RenderPipeline 单帧渲染（含 CRT 与叠加）不抛异常。
    /// </summary>
    [NativeSmokeFact]
    public void CanRenderFrameThroughRenderPipelineWhenExplicitlyEnabled()
    {

        using RenderWindow window = CreateSmokeWindow("PixelEngine pipeline smoke", RenderBackendPreference.Auto);
        RenderPipelineFrame(window);
    }

    /// <summary>
    /// 管线最终视口纹理保持 CPU 上下行序，不因后处理翻转。
    /// </summary>
    [NativeSmokeFact]
    public void RenderPipelineViewportKeepsSingleTopBottomOrientationWhenExplicitlyEnabled()
    {

        using RenderWindow window = CreateSmokeWindow("PixelEngine pipeline orientation smoke", RenderBackendPreference.Auto);
        using RenderPipeline pipeline = new(window, 16, 16);
        RenderBuffer buffer = new(16, 16);
        RenderAuxBuffers aux = new(16, 16);
        for (int y = 0; y < buffer.Height; y++)
        {
            uint color = y < buffer.Height / 2 ? 0xFFFF0000u : 0xFF0000FFu;
            buffer.Pixels.Slice(y * buffer.Width, buffer.Width).Fill(color);
        }

        pipeline.Settings.QualityLevel = LightingQualityLevel.BloomDisabled;
        pipeline.Settings.EnableDither = false;
        pipeline.Settings.Gamma = 1f;
        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, buffer.Width, buffer.Height), [], []);
        RenderViewportTexture viewport = pipeline.CurrentViewportTexture;
        byte[] pixels = ReadTextureRgba(window.Gl, viewport.Handle, viewport.Width, viewport.Height);
        byte[] top = PixelAtTopLeftOrigin(pixels, viewport.Width, x: 8, y: 0);
        byte[] bottom = PixelAtTopLeftOrigin(pixels, viewport.Width, x: 8, y: viewport.Height - 1);

        Assert.True(top[0] > top[2], $"最终 viewport 顶部应为 CPU 第 0 行红色，actual rgba=({top[0]},{top[1]},{top[2]},{top[3]})");
        Assert.True(bottom[2] > bottom[0], $"最终 viewport 底部应为 CPU 末行蓝色，actual rgba=({bottom[0]},{bottom[1]},{bottom[2]},{bottom[3]})");
    }

    /// <summary>
    /// Game View 消费的 runtime viewport 纹理已经包含 gameplay overlay，而不是裸 world post-process 结果。
    /// </summary>
    [NativeSmokeFact]
    public void RenderPipelineCurrentViewportTextureContainsGameplayOverlayWhenExplicitlyEnabled()
    {
        using RenderWindow window = CreateSmokeWindow("PixelEngine runtime viewport overlay smoke", RenderBackendPreference.Auto);
        using RenderPipeline pipeline = new(window, 16, 16);
        RenderBuffer buffer = new(16, 16);
        RenderAuxBuffers aux = new(16, 16);
        buffer.Pixels.Fill(0xFF101010u);
        OverlayCommand[] overlays =
        [
            OverlayCommand.SolidRectangle(6f, 7f, 4f, 5f, 0xFFF2D05Eu),
        ];

        pipeline.Settings.QualityLevel = LightingQualityLevel.BloomDisabled;
        pipeline.Settings.EnableDither = false;
        pipeline.Settings.Gamma = 1f;
        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 16), [], overlays);

        RenderViewportTexture viewport = pipeline.CurrentViewportTexture;
        byte[] pixels = ReadTextureRgba(window.Gl, viewport.Handle, viewport.Width, viewport.Height);
        byte[] player = PixelAtTopLeftOrigin(pixels, viewport.Width, x: 8, y: 9);

        Assert.Equal(1, pipeline.CurrentViewportOverlayCount);
        Assert.True(
            player[0] > 230 && player[1] > 190 && player[2] is > 70 and < 120 && player[3] > 240,
            $"runtime viewport 中应读回玩家黄色 overlay，actual rgba=({player[0]},{player[1]},{player[2]},{player[3]})");
    }

    /// <summary>
    /// 固定 world 会先 letterbox 到独立 presentation，Game UI 随后仍可覆盖整张 presentation，纹理 revision 同帧发布。
    /// </summary>
    [NativeSmokeFact]
    public void RenderPipelineComposesWorldLetterboxAndFullPresentationUiAtomically()
    {
        using RenderWindow window = CreateSmokeWindow("PixelEngine presentation compose smoke", RenderBackendPreference.Auto);
        using RenderPipeline pipeline = new(window, 16, 9);
        RenderPresentationDescriptor descriptor = new(
            16,
            16,
            PresentationViewport.Fit(16, 9, 16, 16),
            DisplayMetricsRevision: 3,
            Revision: 11);
        pipeline.CommitPresentation(in descriptor);
        RenderBuffer buffer = new(16, 9);
        RenderAuxBuffers aux = new(16, 9);
        buffer.Pixels.Fill(0xFFE06020u);
        SolidUiProbeLayer topBarUi = new(0xFF00FF00u, left: 1f, top: 1f, size: 3f);
        using IDisposable registration = pipeline.RegisterUiLayer(
            UiPresentSurface.RuntimeViewport,
            UiPresentLayerOrders.Game,
            topBarUi);
        pipeline.Settings.QualityLevel = LightingQualityLevel.BloomDisabled;
        pipeline.Settings.EnableDither = false;
        pipeline.Settings.Gamma = 1f;

        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 9));

        RenderViewportTexture texture = pipeline.CurrentViewportTexture;
        byte[] pixels = ReadTextureRgba(window.Gl, texture.Handle, texture.Width, texture.Height);
        byte[] untouchedTopBar = PixelAtTopLeftOrigin(pixels, texture.Width, x: 8, y: 1);
        byte[] uiInTopBar = PixelAtTopLeftOrigin(pixels, texture.Width, x: 2, y: 2);
        byte[] worldCenter = PixelAtTopLeftOrigin(pixels, texture.Width, x: 8, y: 8);
        Assert.Equal((16, 16, 11L), (texture.Width, texture.Height, texture.Revision));
        Assert.True(MaxRgb(untouchedTopBar) < 8, PixelMessage("presentation top letterbox", untouchedTopBar));
        Assert.True(uiInTopBar[1] > 240 && uiInTopBar[0] < 20, PixelMessage("UI across presentation", uiInTopBar));
        Assert.True(MaxRgb(worldCenter) > 80, PixelMessage("fixed world center", worldCenter));
        Assert.Equal(GLEnum.NoError, topBarUi.GlError);
    }

    /// <summary>
    /// Game UI 写入 runtime viewport，而 Editor UI 只写窗口 framebuffer；两者层级与目标表面不得串线。
    /// </summary>
    [NativeSmokeFact]
    public void UiPresentLayersCompositeRuntimeBeforePublishingViewportAndEditorAfterWindowPresent()
    {
        using RenderWindow window = CreateSmokeWindow("PixelEngine runtime/editor UI surface smoke", RenderBackendPreference.Auto);
        using RenderPipeline pipeline = new(window, 16, 16);
        RenderBuffer buffer = new(16, 16);
        RenderAuxBuffers aux = new(16, 16);
        buffer.Pixels.Fill(0xFF101010u);
        SolidUiProbeLayer runtimeLayer = new(0xFF00FF00u, left: 4f, top: 4f, size: 8f);
        SolidUiProbeLayer legacyWindowLayer = new(0xFF0000FFu, left: 4f, top: 4f, size: 8f);
        SolidUiProbeLayer editorLayer = new(0xFFFF0000u, left: 20f, top: 4f, size: 8f);
        using IDisposable runtimeRegistration = pipeline.RegisterUiLayer(
            UiPresentSurface.RuntimeViewport,
            UiPresentLayerOrders.Game,
            runtimeLayer);
        // 兼容 overload 即使 order 低于 Editor，也必须固定写 WindowFramebuffer，不能再由 order 推断 surface。
        using IDisposable legacyRegistration = pipeline.RegisterUiLayer(
            UiPresentLayerOrders.Game + 50,
            legacyWindowLayer);
        using IDisposable editorRegistration = pipeline.RegisterUiLayer(
            UiPresentSurface.WindowFramebuffer,
            UiPresentLayerOrders.Editor,
            editorLayer);
        byte[]? windowPixels = null;
        pipeline.BeforeSwapBuffers += gl =>
        {
            gl.ReadBuffer(ReadBufferMode.Back);
            windowPixels = ReadFramebufferRgba(gl, window.Width, window.Height);
        };

        pipeline.Settings.QualityLevel = LightingQualityLevel.BloomDisabled;
        pipeline.Settings.EnableDither = false;
        pipeline.Settings.Gamma = 1f;
        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 16));

        RenderViewportTexture viewport = pipeline.CurrentViewportTexture;
        byte[] viewportPixels = ReadTextureRgba(window.Gl, viewport.Handle, viewport.Width, viewport.Height);
        byte[] runtimeCenter = PixelAtTopLeftOrigin(viewportPixels, viewport.Width, x: 8, y: 8);
        Assert.NotNull(windowPixels);
        PresentationViewport presentation = PresentationViewport.Fit(16, 16, window.Width, window.Height);
        int presentedCenterX = presentation.X + (presentation.Width / 2);
        int presentedTop = presentation.TargetHeight - presentation.Y - presentation.Height;
        int presentedCenterY = presentedTop + (presentation.Height / 2);
        byte[] legacyWindowCenter = PixelAtTopLeftOrigin(windowPixels, window.Width, x: 8, y: 8);
        byte[] editorCenter = PixelAtTopLeftOrigin(windowPixels, window.Width, x: 24, y: 8);
        byte[] scaledRuntimeCenter = PixelAtTopLeftOrigin(windowPixels, window.Width, x: presentedCenterX, y: presentedCenterY);

        Assert.Equal((16, 16), (runtimeLayer.FramebufferWidth, runtimeLayer.FramebufferHeight));
        Assert.Equal((window.Width, window.Height), (legacyWindowLayer.FramebufferWidth, legacyWindowLayer.FramebufferHeight));
        Assert.Equal((window.Width, window.Height), (editorLayer.FramebufferWidth, editorLayer.FramebufferHeight));
        Assert.True(runtimeCenter[1] > 240 && runtimeCenter[0] < 20, PixelMessage("runtime texture", runtimeCenter));
        Assert.True(legacyWindowCenter[2] > 240 && legacyWindowCenter[0] < 20, PixelMessage("legacy window layer", legacyWindowCenter));
        Assert.True(editorCenter[0] > 240 && editorCenter[1] < 20, PixelMessage("window editor layer", editorCenter));
        Assert.True(scaledRuntimeCenter[1] > 240 && scaledRuntimeCenter[0] < 20, PixelMessage("window runtime present", scaledRuntimeCenter));
        Assert.Equal(GLEnum.NoError, runtimeLayer.GlError);
        Assert.Equal(GLEnum.NoError, legacyWindowLayer.GlError);
        Assert.Equal(GLEnum.NoError, editorLayer.GlError);
    }

    /// <summary>
    /// 泛光后视口上下半区颜色不互相镜像。
    /// </summary>
    [NativeSmokeFact]
    public void RenderPipelineBloomDoesNotMirrorTopAndBottomHalvesWhenExplicitlyEnabled()
    {

        using RenderWindow window = CreateSmokeWindow("PixelEngine bloom orientation smoke", RenderBackendPreference.Auto);
        using RenderPipeline pipeline = new(window, 32, 32);
        RenderBuffer buffer = new(32, 32);
        RenderAuxBuffers aux = new(32, 32);
        for (int y = 0; y < buffer.Height; y++)
        {
            uint color = y < buffer.Height / 2 ? 0xFFFF0000u : 0xFF0000FFu;
            buffer.Pixels.Slice(y * buffer.Width, buffer.Width).Fill(color);
        }

        pipeline.Settings.EnableDither = false;
        pipeline.Settings.Gamma = 1f;
        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, buffer.Width, buffer.Height), [], []);
        RenderViewportTexture viewport = pipeline.CurrentViewportTexture;
        byte[] pixels = ReadTextureRgba(window.Gl, viewport.Handle, viewport.Width, viewport.Height);
        byte[] top = PixelAtTopLeftOrigin(pixels, viewport.Width, x: 16, y: 2);
        byte[] middleTop = PixelAtTopLeftOrigin(pixels, viewport.Width, x: 16, y: 10);
        byte[] middleBottom = PixelAtTopLeftOrigin(pixels, viewport.Width, x: 16, y: 21);
        byte[] bottom = PixelAtTopLeftOrigin(pixels, viewport.Width, x: 16, y: 29);

        Assert.True(top[0] > top[2], $"bloom 后顶部应保持红色，actual rgba=({top[0]},{top[1]},{top[2]},{top[3]})");
        Assert.True(middleTop[0] > middleTop[2], $"bloom 后上半部应保持红色，actual rgba=({middleTop[0]},{middleTop[1]},{middleTop[2]},{middleTop[3]})");
        Assert.True(middleBottom[2] > middleBottom[0], $"bloom 后下半部应保持蓝色，actual rgba=({middleBottom[0]},{middleBottom[1]},{middleBottom[2]},{middleBottom[3]})");
        Assert.True(bottom[2] > bottom[0], $"bloom 后底部应保持蓝色，actual rgba=({bottom[0]},{bottom[1]},{bottom[2]},{bottom[3]})");
    }

    /// <summary>
    /// 泛光运行时 CPU 上传的自发光仍落在视口底部边沿。
    /// </summary>
    [NativeSmokeFact]
    public void RenderPipelineKeepsCpuUploadedEmissiveAtBottomEdgeWhenBloomRuns()
    {

        using RenderWindow window = CreateSmokeWindow("PixelEngine emissive edge orientation smoke", RenderBackendPreference.Auto);
        using RenderPipeline pipeline = new(window, 32, 32);
        RenderBuffer buffer = new(32, 32);
        RenderAuxBuffers aux = new(32, 32);
        int x = 16;
        for (int y = 28; y < 32; y++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                aux.Emissive[(y * aux.Width) + x + dx] = 0xFFFF7000u;
            }
        }

        pipeline.Settings.EnableDither = false;
        pipeline.Settings.Gamma = 1f;
        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, buffer.Width, buffer.Height), [], []);
        RenderViewportTexture viewport = pipeline.CurrentViewportTexture;
        byte[] pixels = ReadTextureRgba(window.Gl, viewport.Handle, viewport.Width, viewport.Height);
        byte[] top = PixelAtTopLeftOrigin(pixels, viewport.Width, x, y: 2);
        byte[] bottom = PixelAtTopLeftOrigin(pixels, viewport.Width, x, y: 29);

        Assert.True(bottom[0] > 180 && bottom[1] > 60, $"底部 emissive 应保持在底部，actual bottom rgba=({bottom[0]},{bottom[1]},{bottom[2]},{bottom[3]})");
        Assert.True(top[0] < 32 && top[1] < 32 && top[2] < 32, $"底部 emissive 不应翻到顶部，actual top rgba=({top[0]},{top[1]},{top[2]},{top[3]})");
    }

    /// <summary>
    /// 清空 Emissive 后多帧渲染，泛光残影应衰减至接近零。
    /// </summary>
    [NativeSmokeFact]
    public void BloomDoesNotRetainExpiredEmissiveInputAcrossFramesWhenExplicitlyEnabled()
    {

        using RenderWindow window = CreateSmokeWindow("PixelEngine bloom decay smoke", RenderBackendPreference.Auto);
        using RenderPipeline pipeline = new(window, 32, 32);
        RenderBuffer buffer = new(32, 32);
        RenderAuxBuffers aux = new(32, 32);
        CameraState camera = CameraState.OneToOne(0, 0, buffer.Width, buffer.Height);
        buffer.Pixels.Fill(0xFF000000u);
        aux.Emissive[(16 * aux.Width) + 16] = 0xFFFFFFFFu;
        pipeline.Settings.EnableDither = false;
        pipeline.Settings.Gamma = 1f;

        pipeline.RenderFrame(buffer, aux, camera, [], []);
        RenderViewportTexture viewport = pipeline.CurrentViewportTexture;
        byte[] litPixels = ReadTextureRgba(window.Gl, viewport.Handle, viewport.Width, viewport.Height);
        byte[] litCenter = PixelAtTopLeftOrigin(litPixels, viewport.Width, x: 16, y: 16);
        int litBrightness = MaxRgb(litCenter);

        aux.Clear();
        for (int i = 0; i < 4; i++)
        {
            pipeline.RenderFrame(buffer, aux, camera, [], []);
        }

        viewport = pipeline.CurrentViewportTexture;
        byte[] decayedPixels = ReadTextureRgba(window.Gl, viewport.Handle, viewport.Width, viewport.Height);
        byte[] decayedCenter = PixelAtTopLeftOrigin(decayedPixels, viewport.Width, x: 16, y: 16);
        int decayedBrightness = MaxRgb(decayedCenter);

        Assert.True(litBrightness > 64, $"首帧 emissive 应触发 bloom，actual rgba=({litCenter[0]},{litCenter[1]},{litCenter[2]},{litCenter[3]})");
        Assert.True(decayedBrightness <= 8, $"emissive 清空后 bloom 不应残留，actual rgba=({decayedCenter[0]},{decayedCenter[1]},{decayedCenter[2]},{decayedCenter[3]})");
    }

    /// <summary>
    /// PreferComputeLighting 下经 Compute Bloom 完成单帧渲染。
    /// </summary>
    [NativeSmokeFact]
    public void CanRenderFrameThroughComputeBloomWhenExplicitlyEnabled()
    {

        using RenderWindow window = CreateSmokeWindow("PixelEngine compute bloom smoke", RenderBackendPreference.Auto);
        RenderPipelineFrame(window, preferComputeLighting: true);
    }

    /// <summary>
    /// GpuPointSprite 模式下 GPU 粒子渲染单帧成功。
    /// </summary>
    [NativeSmokeFact]
    public void CanRenderFrameThroughGpuParticlesWhenExplicitlyEnabled()
    {

        using RenderWindow window = CreateSmokeWindow("PixelEngine GPU particle smoke", RenderBackendPreference.Auto);
        using RenderPipeline pipeline = new(window, 16, 16);
        RenderBuffer buffer = new(16, 16);
        RenderAuxBuffers aux = new(16, 16);
        MaterialTable materials = new(
        [
            new MaterialDef
            {
                Id = 0,
                Name = "fire",
                Type = CellType.Fire,
                HeatCapacity = 1f,
                TextureId = -1,
                BaseColorBGRA = 0xFFFF8040u,
                PropertyFlags = MaterialProperty.Emissive,
            },
        ]);
        Particle[] particles =
        [
            new Particle { X = 4.5f, Y = 4.5f, Material = 0, ColorVariant = 128, Life = 16 },
            new Particle { X = 11.5f, Y = 10.5f, Material = 0, ColorVariant = 220, Life = 16 },
        ];

        buffer.Pixels.Fill(0xFF101820u);
        pipeline.Settings.ParticleRenderMode = ParticleRenderMode.GpuPointSprite;
        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 16), [], [], particles, materials);
        window.SwapBuffers();

        Assert.Equal(16, pipeline.Width);
    }

    /// <summary>
    /// 在支持 GL Compute 时，Compute Bloom 与 Fragment Bloom 像素误差 ≤2。
    /// </summary>
    [NativeSmokeFact]
    public void ComputeBloomMatchesFragmentBloomForSolidInputWhenExplicitlyEnabled()
    {

        using RenderWindow window = CreateSmokeWindow("PixelEngine compute bloom equivalence smoke", RenderBackendPreference.Auto);
        GpuCapabilities gpuCapabilities = GpuCapabilities.Query(window.Gl, window.Capabilities);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(gpuCapabilities, ComputeFeatureSwitches.Default, preferComputeSharp: false);
        if (!gate.GlComputeAvailable)
        {
            EnsureGlComputeAvailable(gate);
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

    /// <summary>
    /// 独立 RadianceCascadePass 在遮挡/自发光输入下可完成渲染。
    /// </summary>
    [NativeSmokeFact]
    public void CanRunRadianceCascadePassWhenExplicitlyEnabled()
    {

        using RenderWindow window = CreateSmokeWindow("PixelEngine radiance cascades smoke", RenderBackendPreference.Auto);
        GpuCapabilities gpuCapabilities = GpuCapabilities.Query(window.Gl, window.Capabilities);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(gpuCapabilities, ComputeFeatureSwitches.Default, preferComputeSharp: false);
        if (!gate.GlComputeAvailable)
        {
            EnsureGlComputeAvailable(gate);
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

    /// <summary>
    /// RenderPipeline 启用辐射级联特性后单帧渲染成功。
    /// </summary>
    [NativeSmokeFact]
    public void CanRenderFrameThroughRadianceCascadesWhenExplicitlyEnabled()
    {

        using RenderWindow window = CreateSmokeWindow("PixelEngine render pipeline radiance cascades smoke", RenderBackendPreference.Auto);
        GpuCapabilities gpuCapabilities = GpuCapabilities.Query(window.Gl, window.Capabilities);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(gpuCapabilities, ComputeFeatureSwitches.Default, preferComputeSharp: false);
        if (!gate.GlComputeAvailable)
        {
            EnsureGlComputeAvailable(gate);
        }

        ComputeFeatureSwitches features = ComputeFeatureSwitches.Default with { RadianceCascadesEnabled = true };
        using RenderPipeline pipeline = new(window, 16, 16, features);
        RenderBuffer buffer = new(16, 16);
        RenderAuxBuffers aux = new(16, 16);
        buffer.Pixels.Fill(0xFF203040u);
        aux.Emissive[4 + (4 * 16)] = 0xFFFFFFFFu;
        aux.Occluder[8 + (8 * 16)] = 255;

        pipeline.Settings.PreferComputeLighting = true;
        pipeline.Settings.RadianceCascades = RadianceCascadeSettings.Default with
        {
            Enabled = true,
            CascadeCount = 2,
            BaseRayCount = 8,
            MaxRaySteps = 4,
        };
        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 16));
        window.Gl.Finish();

        Assert.Equal(16, pipeline.Width);
    }

    /// <summary>
    /// GpuAirSmokePipeline 扩散一步后密度纹理句柄非零。
    /// </summary>
    [NativeSmokeFact]
    public void CanRunAirSmokeDiffusePassWhenExplicitlyEnabled()
    {

        using RenderWindow window = CreateSmokeWindow("PixelEngine air smoke smoke", RenderBackendPreference.Auto);
        GpuCapabilities gpuCapabilities = GpuCapabilities.Query(window.Gl, window.Capabilities);
        ComputeCapabilityGate gate = ComputeCapabilityGate.Evaluate(gpuCapabilities, ComputeFeatureSwitches.Default, preferComputeSharp: false);
        if (!gate.GlComputeAvailable)
        {
            EnsureGlComputeAvailable(gate);
        }

        using IComputeBackend backend = ComputeBackendFactory.Create(window.Gl, gate);
        using AirSmokePass pass = new(window.Gl, new GpuAirSmokePipeline(backend), 16, 16);
        float[] seed = new float[16 * 16];
        seed[8 + (8 * 16)] = 1f;

        pass.UploadSeed(seed, 16, 16);
        pass.Step(16, 16, AirSmokeSettings.Default with { Enabled = true, Diffusion = 0.5f });
        window.Gl.Finish();

        Assert.NotEqual(0u, pass.DensityTexture);
    }

    /// <summary>
    /// ANGLE GLES 后端创建窗口并完成管线帧渲染。
    /// </summary>
    [NativeSmokeFact("PIXELENGINE_RENDERING_ANGLE_SMOKE")]
    public void CanRenderFrameThroughGlesAngleWhenExplicitlyEnabled()
    {
        using RenderWindow window = CreateSmokeWindow("PixelEngine ANGLE smoke", RenderBackendPreference.GlEs30Angle);

        Assert.Equal(RenderBackend.GlEs30Angle, window.Backend);
        Assert.True(window.Capabilities.IsGles, BackendMessage(window));
        Assert.True(
            window.Capabilities.MajorVersion > 3 ||
            (window.Capabilities.MajorVersion == 3 && window.Capabilities.MinorVersion >= 0),
            BackendMessage(window));
        Assert.True(window.Capabilities.IsAngle, BackendMessage(window));
        Assert.False(window.Capabilities.HasBufferStorage);
        WriteGraphicsCapability(window, "angle-gles");

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

    private static void EnsureGlComputeAvailable(ComputeCapabilityGate gate)
    {
        throw new InvalidOperationException($"当前 GL smoke context 不支持 GL compute，selected backend={gate.SelectedBackend}，native smoke 无法完成。");
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

    private void WriteGraphicsCapability(RenderWindow window, string backend)
    {
        GlCapabilities capabilities = window.Capabilities;
        Assert.False(string.IsNullOrWhiteSpace(capabilities.Vendor), "GL_VENDOR 不能为空。");
        Assert.False(string.IsNullOrWhiteSpace(capabilities.Renderer), "GL_RENDERER 不能为空。");
        Assert.False(string.IsNullOrWhiteSpace(capabilities.Version), "GL_VERSION 不能为空。");
        string json = JsonSerializer.Serialize(new
        {
            schema = "pixelengine.graphics-capability/v1",
            backend,
            vendor = capabilities.Vendor,
            renderer = capabilities.Renderer,
            version = capabilities.Version,
            major = capabilities.MajorVersion,
            minor = capabilities.MinorVersion,
            isGles = capabilities.IsGles,
            isAngle = capabilities.IsAngle,
        });
        _output.WriteLine("PIXELENGINE_GRAPHICS_CAPABILITY " + json);
    }

    private static byte[] ReadTarget(RenderWindow window, ColorRenderTarget target)
    {
        byte[] rgba = new byte[checked(target.Width * target.Height * 4)];
        target.BindFramebuffer();
        window.Gl.ReadPixels(0, 0, (uint)target.Width, (uint)target.Height, GLEnum.Rgba, GLEnum.UnsignedByte, out rgba[0]);
        return rgba;
    }

    private static byte[] ReadFramebufferRgba(GL gl, int width, int height)
    {
        byte[] rgba = new byte[checked(width * height * 4)];
        gl.ReadPixels(0, 0, (uint)width, (uint)height, GLEnum.Rgba, GLEnum.UnsignedByte, out rgba[0]);
        return rgba;
    }

    private static string PixelMessage(string label, byte[] pixel)
    {
        return $"{label} rgba=({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]})";
    }

    private sealed class SolidUiProbeLayer(uint colorBgra, float left, float top, float size) : IUiPresentLayer
    {
        private readonly UiVertex[] _vertices =
        [
            new UiVertex(left, top, 0f, 0f, colorBgra),
            new UiVertex(left + size, top, 0f, 0f, colorBgra),
            new UiVertex(left + size, top + size, 0f, 0f, colorBgra),
            new UiVertex(left, top + size, 0f, 0f, colorBgra),
        ];
        private readonly ushort[] _indices = [0, 1, 2, 0, 2, 3];

        public int FramebufferWidth { get; private set; }

        public int FramebufferHeight { get; private set; }

        public GLEnum GlError { get; private set; }

        public void Present(in UiPresentContext context)
        {
            FramebufferWidth = context.FramebufferWidth;
            FramebufferHeight = context.FramebufferHeight;
            while (context.Gl.GetError() != GLEnum.NoError)
            {
            }

            context.SubmitTriangles(_vertices, _indices, UiDrawState.Default);
            GlError = context.Gl.GetError();
        }
    }
}
