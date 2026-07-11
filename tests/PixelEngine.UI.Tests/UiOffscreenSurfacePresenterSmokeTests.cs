using PixelEngine.Core.Diagnostics;
using PixelEngine.Rendering;
using PixelEngine.Testing;
using Silk.NET.OpenGL;
using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// UI 离屏表面呈现器冒烟测试：离屏合成与上传。
/// </summary>
public sealed class UiOffscreenSurfacePresenterSmokeTests
{
    /// <summary>
    /// 验证Offscreen Surface上传Dirty Rect And Composites经Render Pipeline When Gl Smoke Is Enabled。
    /// </summary>
    [NativeSmokeFact]
    [Trait("Category", "NativeSmoke")]
    public void OffscreenSurfaceUploadsDirtyRectAndCompositesThroughRenderPipelineWhenGlSmokeIsEnabled()
    {
        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine offscreen UI surface smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
            EnableDebugContext = true,
        });
        using RenderPipeline pipeline = new(window, 16, 16);
        RenderBuffer buffer = new(16, 16);
        RenderAuxBuffers aux = new(16, 16);
        buffer.Pixels.Fill(0xFF202020u);
        pipeline.Settings.QualityLevel = LightingQualityLevel.BloomDisabled;
        pipeline.Settings.EnableDither = false;
        pipeline.Settings.Gamma = 1f;

        using OffscreenLayer layer = new();
        using IDisposable registration = pipeline.RegisterUiLayer(UiPresentLayerOrders.Game, layer);
        layer.SetFullDirty();
        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, buffer.Width, buffer.Height));

        layer.PaintWhiteCenter();
        FrameProfiler profiler = new();
        byte[]? framebuffer = null;
        byte[]? texturePixels = null;
        pipeline.BeforeSwapBuffers += gl =>
        {
            gl.ReadBuffer(ReadBufferMode.Back);
            framebuffer = ReadFramebufferRgba(gl, window.Width, window.Height);
            texturePixels = ReadTextureRgba(gl, layer.TextureHandle, 4, 4);
        };

        profiler.BeginFrame();
        pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, buffer.Width, buffer.Height), profiler);
        profiler.EndFrame();

        Assert.NotNull(framebuffer);
        Assert.NotNull(texturePixels);
        Assert.True(CountWhitePixelsRaw(texturePixels, width: 4, x0: 1, y0: 1, x1: 3, y1: 3) == 4, "overlay texture dirty rect 应已上传为 2x2 白色区域。");
        Assert.True(layer.PresentCount >= 2, $"UI layer 应被 RenderPipeline 调用，actual={layer.PresentCount}");
        Assert.Equal(GLEnum.NoError, layer.PresenterGlError);
        Assert.Equal(GLEnum.NoError, layer.ProbeGlError);
        Assert.True(
            CountWhitePixels(
                framebuffer,
                window.Width,
                x0: layer.ProbeX,
                y0: layer.ProbeY,
                x1: layer.ProbeX + 8,
                y1: layer.ProbeY + 8) > 0,
            MaxPixelMessage(framebuffer, window.Width));
        Assert.True(CountWhitePixels(framebuffer, window.Width, x0: 4, y0: 4, x1: 12, y1: 12) > 0, MaxPixelMessage(framebuffer, window.Width));
        byte[] outside = PixelAtTopLeftOrigin(framebuffer, window.Width, x: 1, y: 1);
        Assert.True(outside[0] < 240 || outside[1] < 240 || outside[2] < 240, $"脏矩形外不应被错误涂成白色，actual rgba=({outside[0]},{outside[1]},{outside[2]},{outside[3]})");
        Assert.True(profiler.LastSubFrame[(int)FrameSubPhase.UiUpload] > 0);
        Assert.NotEqual(0u, layer.TextureHandle);
    }

    /// <summary>
    /// 验证Offscreen Surface Presenter Source Documents Real Upload Contract。
    /// </summary>
    [Fact]
    public void OffscreenSurfacePresenterSourceDocumentsRealUploadContract()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "UiOffscreenSurfacePresenter.cs"));

        Assert.Contains("UiOverlayTexture", source, StringComparison.Ordinal);
        Assert.Contains("UploadOverlayTexture", source, StringComparison.Ordinal);
        Assert.Contains("context.Target.Validate()", source, StringComparison.Ordinal);
        Assert.Contains("PresentCore(in context, pixelsBgra, sourceWidth, sourceHeight, dirtyRects, context.Target)", source, StringComparison.Ordinal);
        Assert.Contains("SubmitTriangles", source, StringComparison.Ordinal);
        Assert.Contains("UiDrawState.Textured", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToArray", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerable", source, StringComparison.Ordinal);
    }

    private static byte[] ReadFramebufferRgba(GL gl, int width, int height)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        return pixels;
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

    private static int CountWhitePixels(byte[] pixels, int width, int x0, int y0, int x1, int y1)
    {
        int count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                byte[] pixel = PixelAtTopLeftOrigin(pixels, width, x, y);
                if (pixel[0] > 240 && pixel[1] > 240 && pixel[2] > 240 && pixel[3] > 240)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountWhitePixelsRaw(byte[] pixels, int width, int x0, int y0, int x1, int y1)
    {
        int count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int offset = ((y * width) + x) * 4;
                if (pixels[offset] > 240 && pixels[offset + 1] > 240 && pixels[offset + 2] > 240 && pixels[offset + 3] > 240)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static string MaxPixelMessage(byte[] pixels, int width)
    {
        int height = pixels.Length / (width * 4);
        int max = 0;
        int maxX = 0;
        int maxY = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte[] pixel = PixelAtTopLeftOrigin(pixels, width, x, y);
                int value = pixel[0] + pixel[1] + pixel[2];
                if (value > max)
                {
                    max = value;
                    maxX = x;
                    maxY = y;
                }
            }
        }

        return $"脏矩形区域应合成为白色；当前最亮像素 top-left=({maxX},{maxY}) rgbSum={max}";
    }

    private static string ProjectPath(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            path = Directory.GetParent(path)!.FullName;
        }

        return Path.Combine([path, .. parts]);
    }

    private sealed class OffscreenLayer : IUiPresentLayer, IDisposable
    {
        private readonly UiOffscreenSurfacePresenter _presenter = new();
        private readonly uint[] _pixels = new uint[16];
        private readonly PixelUploadRect[] _rects = new PixelUploadRect[1];
        private readonly UiVertex[] _probeVertices =
        [
            new UiVertex(20f, 0f, 0f, 0f, 0xFFFFFFFFu),
            new UiVertex(28f, 0f, 0f, 0f, 0xFFFFFFFFu),
            new UiVertex(28f, 8f, 0f, 0f, 0xFFFFFFFFu),
            new UiVertex(20f, 8f, 0f, 0f, 0xFFFFFFFFu),
        ];
        private readonly ushort[] _probeIndices = [0, 1, 2, 0, 2, 3];
        private int _rectCount;

        public uint TextureHandle => _presenter.TextureHandle;

        public int PresentCount { get; private set; }

        public int ProbeX { get; private set; }

        public int ProbeY { get; private set; }

        public GLEnum PresenterGlError { get; private set; } = GLEnum.NoError;

        public GLEnum ProbeGlError { get; private set; } = GLEnum.NoError;

        public void SetFullDirty()
        {
            _rects[0] = new PixelUploadRect(0, 0, 4, 4);
            _rectCount = 1;
        }

        public void PaintWhiteCenter()
        {
            for (int y = 1; y <= 2; y++)
            {
                for (int x = 1; x <= 2; x++)
                {
                    _pixels[(y * 4) + x] = 0xFFFFFFFFu;
                }
            }

            _rects[0] = new PixelUploadRect(1, 1, 2, 2);
            _rectCount = 1;
        }

        public void Present(in UiPresentContext context)
        {
            PresentCount++;
            ClearErrors(context.Gl);
            UiPresentContext targetContext = context.WithTarget(new UiPresentTarget(4, 4, 16, 16, 1f));
            _presenter.Present(
                in targetContext,
                _pixels,
                4,
                4,
                _rects.AsSpan(0, _rectCount));
            PresenterGlError = context.Gl.GetError();
            ClearErrors(context.Gl);
            ProbeX = context.WorldViewport.X + 20;
            ProbeY = context.WorldViewport.Y;
            _probeVertices[0] = new UiVertex(ProbeX, ProbeY, 0f, 0f, 0xFFFFFFFFu);
            _probeVertices[1] = new UiVertex(ProbeX + 8f, ProbeY, 0f, 0f, 0xFFFFFFFFu);
            _probeVertices[2] = new UiVertex(ProbeX + 8f, ProbeY + 8f, 0f, 0f, 0xFFFFFFFFu);
            _probeVertices[3] = new UiVertex(ProbeX, ProbeY + 8f, 0f, 0f, 0xFFFFFFFFu);
            context.SubmitTriangles(_probeVertices, _probeIndices, UiDrawState.Default);
            ProbeGlError = context.Gl.GetError();
            _rectCount = 0;
        }

        public void Dispose()
        {
            _presenter.Dispose();
        }

        private static void ClearErrors(GL gl)
        {
            while (gl.GetError() != GLEnum.NoError)
            {
            }
        }
    }
}
