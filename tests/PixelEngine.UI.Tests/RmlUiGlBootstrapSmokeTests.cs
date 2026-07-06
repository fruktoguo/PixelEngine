using System.Buffers.Binary;
using System.IO.Compression;
using PixelEngine.Rendering;
using Silk.NET.OpenGL;
using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class RmlUiGlBootstrapSmokeTests
{
    [Fact]
    public void CanCreateNativeRendererWhenGlSmokeIsEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine RmlUi GL smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.DesktopGl33,
            EnableDebugContext = true,
        });

        Assert.True(RmlUiGlBootstrap.TryProbeRenderer(window, out RmlUiGlVersion version));
        Assert.True(version.Major >= 3);
    }

    [Fact]
    public void RmlUiBackendCanLoadAndRenderDocumentWhenGlSmokeIsEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine RmlUi backend smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.DesktopGl33,
            EnableDebugContext = true,
        });
        using RmlUiBackend backend = new(window);
        backend.Initialize(new UiBackendInitializeInfo(
            new UiViewport(0, 0, window.Width, window.Height, 1f),
            UiBackendKind.RmlUi));

        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-rmlui-{Guid.NewGuid():N}");
        string screens = Path.Combine(root, "screens");
        string images = Path.Combine(root, "images");
        _ = Directory.CreateDirectory(screens);
        _ = Directory.CreateDirectory(images);
        string documentPath = Path.Combine(screens, "main.rml");
        string imagePath = Path.Combine(images, "logo.png");
        WritePng(imagePath, 4, 4);
        try
        {
            File.WriteAllText(
                documentPath,
                """
                <rml>
                  <head>
                    <style>
                      body { background-color: transparent; pointer-events: none; }
                      #panel { position: absolute; left: 4px; top: 4px; width: 24px; height: 24px; background-color: #ff4040; pointer-events: auto; }
                      #score { position: absolute; left: 32px; top: 4px; width: 28px; height: 24px; color: #ffffff; pointer-events: none; }
                      .armed_probe { position: absolute; left: 4px; top: 36px; width: 12px; height: 12px; }
                      #logo { position: absolute; left: 36px; top: 36px; width: 16px; height: 16px; pointer-events: none; }
                    </style>
                  </head>
                  <body>
                    <div id="panel" data-event-click="start_game"></div>
                    <div id="score" data-model="score">0</div>
                    <div id="score_mirror" path="score">0</div>
                    <div id="health" path="hud.health.current">0</div>
                    <input class="armed_probe" type="checkbox" path="weapon.armed" />
                    <img id="logo" data-image="logo" />
                  </body>
                </rml>
                """);

            ((IGameUiImagePreloader)backend).PreloadImage(imagePath);
            UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(documentPath, 1));
            backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(1), document, Modal: false)]);
            backend.Update(1f / 60f);
            UiPathId scorePath = new(UiStableId.Hash("score"));
            UiPathId healthPath = UiModelPathName.ToPathId("hud.health.current");
            UiPathId armedPath = UiModelPathName.ToPathId("weapon.armed");
            UiPathId[] paths = new UiPathId[4];
            int pathCount = backend.CopyModelPaths(document, paths);
            Assert.Equal(3, pathCount);
            Assert.Contains(scorePath, paths[..pathCount]);
            Assert.Contains(healthPath, paths[..pathCount]);
            Assert.Contains(armedPath, paths[..pathCount]);
            backend.SetModelValue(document, scorePath, new UiValue(42L));
            backend.SetModelValue(document, healthPath, new UiValue(0.75));
            backend.SetModelValue(document, armedPath, UiValue.FromBoolean(true));
            backend.Update(1f / 60f);
            Assert.True(backend.TryGetModelValue(document, scorePath, out UiValue score));
            Assert.True(backend.TryGetModelValue(document, healthPath, out UiValue health));
            Assert.True(backend.TryGetModelValue(document, armedPath, out UiValue armed));
            Assert.Equal(42L, score.AsInt64());
            Assert.Equal(0.75, health.AsDouble());
            Assert.True(armed.AsBoolean());
            Assert.True(backend.InvokeAction(
                document,
                new UiActionId(UiStableId.Hash("start_game")),
                UiValue.FromBoolean(true)));
            UiEvent[] events = new UiEvent[4];
            Assert.Equal(0, backend.DrainEvents(events));

            backend.FeedPointerMove(20, 24);
            Assert.True(backend.HitTest(20, 24).WantsMouse);
            backend.FeedPointerMove(60, 60);
            Assert.False(backend.HitTest(60, 60).WantsMouse);
            backend.FeedPointerMove(20, 24);
            backend.FeedPointerButton(UiPointerButton.Left, isDown: true);
            backend.FeedPointerButton(UiPointerButton.Left, isDown: false);
            backend.Update(1f / 60f);
            int eventCount = backend.DrainEvents(events);
            Assert.Equal(1, eventCount);
            Assert.Equal(document, events[0].Document);
            Assert.Equal(new UiElementId(UiStableId.Hash("panel")), events[0].Element);
            Assert.Equal(new UiActionId(UiStableId.Hash("start_game")), events[0].Action);

            backend.FeedScroll(0, 1);
            backend.FeedKey(new UiKey(65), isDown: true, UiKeyModifiers.Control);
            backend.FeedKey(new UiKey(65), isDown: false, UiKeyModifiers.Control);
            backend.FeedText("a");
            backend.Update(1f / 60f);

            UiPresentContext context = default;
            backend.Composite(in context);
            window.SwapBuffers();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RmlUiCompositeRestoresGlStateWhenGlSmokeIsEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine RmlUi GL state smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.DesktopGl33,
            EnableDebugContext = true,
        });
        GL gl = window.Gl;
        using RmlUiBackend backend = new(window);
        backend.Initialize(new UiBackendInitializeInfo(
            new UiViewport(0, 0, window.Width, window.Height, 1f),
            UiBackendKind.RmlUi));

        string documentPath = Path.Combine(Path.GetTempPath(), $"pixelengine-rmlui-state-{Guid.NewGuid():N}.rml");
        uint texture = gl.GenTexture();
        try
        {
            File.WriteAllText(
                documentPath,
                """
                <rml>
                  <head>
                    <style>
                      body { background-color: transparent; }
                      #panel { position: absolute; left: 0px; top: 0px; width: 32px; height: 32px; background-color: #40ff40; }
                    </style>
                  </head>
                  <body><div id="panel"></div></body>
                </rml>
                """);

            UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(documentPath, 1));
            backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(1), document, Modal: false)]);
            backend.Update(1f / 60f);

            gl.Enable(EnableCap.DepthTest);
            gl.Disable(EnableCap.Blend);
            gl.Enable(EnableCap.ScissorTest);
            gl.Scissor(1, 2, 9, 10);
            gl.Viewport(3, 4, 33, 34);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
            gl.ActiveTexture(TextureUnit.Texture1);
            gl.BindTexture(TextureTarget.Texture2D, texture);

            GlStateSample before = GlStateSample.Capture(gl);
            UiPresentContext context = default;
            backend.Composite(in context);
            GlStateSample after = GlStateSample.Capture(gl);

            Assert.Equal(before, after);
        }
        finally
        {
            gl.DeleteTexture(texture);
            File.Delete(documentPath);
        }
    }

    private readonly record struct GlStateSample(
        int CurrentProgram,
        int VertexArray,
        int ArrayBuffer,
        int ElementArrayBuffer,
        int DrawFramebuffer,
        int ReadFramebuffer,
        int ActiveTexture,
        int TextureBinding2D,
        int BlendSrcRgb,
        int BlendDstRgb,
        int BlendSrcAlpha,
        int BlendDstAlpha,
        int BlendEquationRgb,
        int BlendEquationAlpha,
        int UnpackAlignment,
        bool Blend,
        bool Depth,
        bool Cull,
        bool Scissor,
        int ViewportX,
        int ViewportY,
        int ViewportWidth,
        int ViewportHeight,
        int ScissorX,
        int ScissorY,
        int ScissorWidth,
        int ScissorHeight)
    {
        public static GlStateSample Capture(GL gl)
        {
            Span<int> viewport = stackalloc int[4];
            Span<int> scissor = stackalloc int[4];
            gl.GetInteger(GLEnum.Viewport, viewport);
            gl.GetInteger(GLEnum.ScissorBox, scissor);
            gl.GetInteger(GLEnum.CurrentProgram, out int currentProgram);
            gl.GetInteger(GLEnum.VertexArrayBinding, out int vertexArray);
            gl.GetInteger(GLEnum.ArrayBufferBinding, out int arrayBuffer);
            gl.GetInteger(GLEnum.ElementArrayBufferBinding, out int elementArrayBuffer);
            gl.GetInteger(GLEnum.DrawFramebufferBinding, out int drawFramebuffer);
            gl.GetInteger(GLEnum.ReadFramebufferBinding, out int readFramebuffer);
            gl.GetInteger(GLEnum.ActiveTexture, out int activeTexture);
            gl.GetInteger(GLEnum.TextureBinding2D, out int textureBinding2D);
            gl.GetInteger(GLEnum.BlendSrcRgb, out int blendSrcRgb);
            gl.GetInteger(GLEnum.BlendDstRgb, out int blendDstRgb);
            gl.GetInteger(GLEnum.BlendSrcAlpha, out int blendSrcAlpha);
            gl.GetInteger(GLEnum.BlendDstAlpha, out int blendDstAlpha);
            gl.GetInteger(GLEnum.BlendEquationRgb, out int blendEquationRgb);
            gl.GetInteger(GLEnum.BlendEquationAlpha, out int blendEquationAlpha);
            gl.GetInteger(GLEnum.UnpackAlignment, out int unpackAlignment);
            return new GlStateSample(
                currentProgram,
                vertexArray,
                arrayBuffer,
                elementArrayBuffer,
                drawFramebuffer,
                readFramebuffer,
                activeTexture,
                textureBinding2D,
                blendSrcRgb,
                blendDstRgb,
                blendSrcAlpha,
                blendDstAlpha,
                blendEquationRgb,
                blendEquationAlpha,
                unpackAlignment,
                gl.IsEnabled(EnableCap.Blend),
                gl.IsEnabled(EnableCap.DepthTest),
                gl.IsEnabled(EnableCap.CullFace),
                gl.IsEnabled(EnableCap.ScissorTest),
                viewport[0],
                viewport[1],
                viewport[2],
                viewport[3],
                scissor[0],
                scissor[1],
                scissor[2],
                scissor[3]);
        }
    }

    private static void WritePng(string path, int width, int height)
    {
        using MemoryStream idat = new();
        using (ZLibStream zlib = new(idat, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                for (int x = 0; x < width; x++)
                {
                    zlib.WriteByte((byte)(x * 50));
                    zlib.WriteByte((byte)(y * 50));
                    zlib.WriteByte(180);
                    zlib.WriteByte(255);
                }
            }
        }

        using FileStream file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(file, "IHDR"u8, ihdr);
        WriteChunk(file, "IDAT"u8, idat.ToArray());
        WriteChunk(file, "IEND"u8, []);
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);
        stream.Write(stackalloc byte[4]);
    }
}
