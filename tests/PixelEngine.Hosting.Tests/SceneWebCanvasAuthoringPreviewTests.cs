using PixelEngine.Editor.Shell;
using PixelEngine.Rendering;
using PixelEngine.Testing;
using PixelEngine.UI;
using Silk.NET.OpenGL;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Scene Web Canvas authoring preview 测试：路径安全、CanvasScaler、Scene 布局与真实 RmlUi 离屏像素。
/// </summary>
public sealed class SceneWebCanvasAuthoringPreviewTests
{
    /// <summary>
    /// 验证 stable asset id 优先解析、超大 reference resolution 等比限幅，且 logical layout 仍保持参考尺寸。
    /// </summary>
    [Fact]
    public void DescriptorUsesStableManifestResolverAndPreservesReferenceLayout()
    {
        string root = CreateContentRoot();
        try
        {
            string manifestPath = Path.Combine(root, "ui", UiManifestLoader.ManifestFileName);
            EditorSceneModel scene = EditorSceneModel.Empty("web-preview-descriptor");
            EditorGameObject canvasObject = scene.Create("Canvas");
            UiCanvasScalerSettings settings = UiCanvasScalerSettings.Default with
            {
                ScaleMode = UiScaleMode.ScaleWithScreenSize,
                ReferenceWidth = 4096f,
                ReferenceHeight = 2048f,
                ScreenMatchMode = UiScreenMatchMode.MatchWidthOrHeight,
                MatchWidthOrHeight = 0.5f,
            };
            scene.SetBuiltInCanvasComponents(
                canvasObject.StableId,
                new EditorWebCanvasComponent
                {
                    ManifestAssetId = "ui-manifest-stable-id",
                    ManifestPath = "ui/missing-fallback.json",
                    InitialScreenId = "preview",
                    Enabled = true,
                    Primary = true,
                },
                new EditorCanvasScalerComponent { Settings = settings });

            SceneWebCanvasPreviewDescriptor descriptor = SceneWebCanvasPreviewDescriptorResolver.Resolve(
                scene,
                canvasObject.StableId,
                root,
                assetId => assetId == "ui-manifest-stable-id" ? manifestPath : null,
                assetRevision: 7);

            Assert.True(descriptor.CanRender, descriptor.Diagnostic);
            Assert.Equal(Path.GetFullPath(manifestPath), descriptor.ManifestPath);
            Assert.Equal((2048, 1024),
                (descriptor.CanvasMetrics.PresentationWidth, descriptor.CanvasMetrics.PresentationHeight));
            Assert.Equal(4096f, descriptor.CanvasMetrics.LogicalWidth, precision: 2);
            Assert.Equal(2048f, descriptor.CanvasMetrics.LogicalHeight, precision: 2);
            Assert.Equal(0.5f, descriptor.CanvasMetrics.ScaleFactor, precision: 3);
            Assert.Equal(7, descriptor.Key.AssetRevision);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 验证 disabled parent 不会被 authoring preview 擅自复活，且 manifest 路径不能逃逸 content root。
    /// </summary>
    [Fact]
    public void DescriptorKeepsDisabledHierarchyInactiveAndRejectsEscapedManifest()
    {
        string root = CreateContentRoot();
        try
        {
            EditorSceneModel scene = EditorSceneModel.Empty("web-preview-disabled");
            EditorGameObject parent = scene.Create("Disabled Parent");
            EditorGameObject canvasObject = scene.Create("Canvas", parent.StableId);
            scene.SetEnabled(parent.StableId, enabled: false);
            scene.SetBuiltInCanvasComponents(
                canvasObject.StableId,
                new EditorWebCanvasComponent
                {
                    ManifestPath = "ui/ui-manifest.json",
                    InitialScreenId = "preview",
                    Enabled = true,
                },
                new EditorCanvasScalerComponent());

            SceneWebCanvasPreviewDescriptor descriptor = SceneWebCanvasPreviewDescriptorResolver.Resolve(
                scene,
                canvasObject.StableId,
                root,
                manifestAssetResolver: null,
                assetRevision: 0);

            Assert.False(descriptor.CanRender);
            Assert.Contains("disabled", descriptor.Diagnostic, StringComparison.Ordinal);
            _ = Assert.Throws<InvalidDataException>(() =>
                SceneWebCanvasPreviewDescriptorResolver.ResolveManifestPath(
                    root,
                    manifestAssetId: null,
                    manifestPath: "../outside/ui-manifest.json",
                    manifestAssetResolver: null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Scene reference frame 在横屏与竖屏 presentation 下都等比居中，不裁切也不拉伸。
    /// </summary>
    [Fact]
    public void ScenePreviewLayoutCentersLandscapeAndPortraitPresentations()
    {
        SceneWebCanvasPreviewLayout landscape = SceneViewPanel.ResolveWebCanvasPreviewLayout(
            new Vector2(100f, 50f),
            new Vector2(1000f, 700f),
            1920,
            1080);
        Assert.Equal(952f, landscape.Size.X, precision: 2);
        Assert.Equal(535.5f, landscape.Size.Y, precision: 1);
        Assert.Equal(124f, landscape.Min.X, precision: 2);
        Assert.Equal(132.25f, landscape.Min.Y, precision: 1);

        SceneWebCanvasPreviewLayout portrait = SceneViewPanel.ResolveWebCanvasPreviewLayout(
            Vector2.Zero,
            new Vector2(1000f, 700f),
            1080,
            1920);
        Assert.Equal(366.75f, portrait.Size.X, precision: 1);
        Assert.Equal(652f, portrait.Size.Y, precision: 2);
        Assert.Equal(316.625f, portrait.Min.X, precision: 1);
        Assert.Equal(24f, portrait.Min.Y, precision: 2);
    }

    /// <summary>
    /// 验证真实 RmlUi XHTML 在同一窗口 GL context 中写入独立 Scene 纹理；只投递 hover，click action 不会产生事件副作用。
    /// </summary>
    [NativeSmokeFact]
    [Trait("Category", "NativeSmoke")]
    public void RealXhtmlRendersIntoIsolatedSceneTextureWithoutActionSideEffects()
    {
        string root = CreateContentRoot();
        try
        {
            string screenPath = Path.Combine(root, "ui", "screens", "preview.xhtml");
            string stylePath = Path.Combine(root, "ui", "styles", "preview.rcss");
            string imagePath = Path.Combine(root, "ui", "images", "logo.png");
            string fontPath = Path.Combine(root, "ui", "fonts", "NotoSansSC-VF.ttf");
            _ = Directory.CreateDirectory(Path.GetDirectoryName(screenPath)!);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(stylePath)!);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(fontPath)!);
            File.Copy(
                RepositoryPath("demo", "PixelEngine.Demo", "content", "ui", "fonts", "NotoSansSC-VF.ttf"),
                fontPath);
            File.WriteAllText(
                stylePath,
                """
                body { margin: 0px; width: 320px; height: 180px; background-color: #16d9c5; font-family: "Noto Sans SC"; }
                #button { position: absolute; left: 20px; top: 20px; width: 120px; height: 60px; background-color: #ef3054; }
                #title { position: absolute; left: 20px; top: 96px; color: #ffffff; font-size: 24px; }
                #logo { position: absolute; left: 240px; top: 20px; width: 56px; height: 56px; }
                """);
            WritePng(imagePath, 4, 4);
            File.WriteAllText(
                screenPath,
                """
                <rml>
                  <head>
                    <link type="text/rcss" href="../styles/preview.rcss" />
                  </head>
                  <body>
                    <div id="button" data-event-click="must_not_dispatch"></div>
                    <div id="title">Canvas 预览</div>
                    <img id="logo" data-image="logo" />
                  </body>
                </rml>
                """);
            File.WriteAllText(
                Path.Combine(root, "ui", UiManifestLoader.ManifestFileName),
                """
                {
                  "screens": [
                    { "id": "preview", "path": "screens/preview.xhtml", "preload": true }
                  ],
                  "images": [
                    { "id": "logo", "path": "images/logo.png", "preload": true }
                  ]
                }
                """);

            using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
            {
                Title = "PixelEngine Scene Web Canvas authoring preview smoke",
                Width = 160,
                Height = 90,
                BackendPreference = RenderBackendPreference.DesktopGl33,
                EnableDebugContext = true,
            });
            using RenderPipeline pipeline = new(window, 16, 9);
            pipeline.Settings.QualityLevel = LightingQualityLevel.BloomDisabled;
            pipeline.Settings.EnableDither = false;
            pipeline.Settings.Gamma = 1f;
            RenderBuffer buffer = new(16, 9);
            RenderAuxBuffers aux = new(16, 9);
            buffer.Pixels.Fill(0xFF202020u);

            EditorSceneModel scene = EditorSceneModel.Empty("web-preview-native-smoke");
            EditorGameObject canvasObject = scene.Create("Canvas");
            UiCanvasScalerSettings scaler = UiCanvasScalerSettings.Default with
            {
                ScaleMode = UiScaleMode.ScaleWithScreenSize,
                ReferenceWidth = 320f,
                ReferenceHeight = 180f,
            };
            scene.SetBuiltInCanvasComponents(
                canvasObject.StableId,
                new EditorWebCanvasComponent
                {
                    ManifestPath = "ui/ui-manifest.json",
                    InitialScreenId = "preview",
                    Enabled = true,
                    Primary = true,
                },
                new EditorCanvasScalerComponent { Settings = scaler });

            using SceneWebCanvasAuthoringPreview preview = new(scene, root, window, pipeline);
            preview.Request(canvasObject.StableId, true, true, 40f, 40f);
            pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 9));
            pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 9));

            SceneWebCanvasPreviewSnapshot snapshot = preview.Snapshot;
            Assert.True(snapshot.Ready, snapshot.Diagnostic);
            Assert.NotEqual(0u, snapshot.TextureHandle);
            Assert.Equal("preview", snapshot.ScreenId);
            Assert.Equal((320, 180),
                (snapshot.CanvasMetrics.PresentationWidth, snapshot.CanvasMetrics.PresentationHeight));
            Assert.Equal(0, snapshot.SuppressedEventCount);
            byte[] pixels = ReadTextureRgba(window.Gl, snapshot.TextureHandle, 320, 180);
            Assert.True(
                CountCyanPixels(pixels) > 500,
                "真实 Scene XHTML 纹理中未找到预期青色 body 像素。");
            Assert.True(
                CountRedPixels(pixels) > 100,
                "真实 Scene XHTML 纹理中未找到预期红色交互元素像素。");
            Assert.True(
                CountBluePixels(pixels) > 50,
                "真实 Scene XHTML 纹理中未找到 manifest 图片像素。");
            Assert.True(
                CountWhitePixels(pixels) > 5,
                "真实 Scene XHTML 纹理中未找到 FontEngine 文本像素。");

            File.WriteAllText(
                stylePath,
                """
                body { margin: 0px; width: 320px; height: 180px; background-color: #35e85a; }
                """);
            preview.InvalidateAssets();
            pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 9));
            pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 9));
            SceneWebCanvasPreviewSnapshot refreshed = preview.Snapshot;
            Assert.True(refreshed.Ready, refreshed.Diagnostic);
            Assert.True(refreshed.Revision > snapshot.Revision);
            byte[] refreshedPixels = ReadTextureRgba(window.Gl, refreshed.TextureHandle, 320, 180);
            Assert.True(
                CountGreenPixels(refreshedPixels) > 500,
                "XHTML hot refresh 后 Scene 纹理未切换到新的绿色内容。");

            preview.Request(null, false, false, 0f, 0f);
            pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 9));
            Assert.False(preview.Snapshot.Visible);
            preview.Request(canvasObject.StableId, true, false, 0f, 0f);
            pipeline.RenderFrame(buffer, aux, CameraState.OneToOne(0, 0, 16, 9));
            Assert.True(preview.Snapshot.Ready, preview.Snapshot.Diagnostic);
            Assert.Equal(0, preview.Snapshot.SuppressedEventCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateContentRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-scene-web-preview-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(Path.Combine(root, "ui"));
        File.WriteAllText(
            Path.Combine(root, "ui", UiManifestLoader.ManifestFileName),
            """
            {
              "screens": [
                { "id": "preview", "path": "preview.xhtml", "preload": false }
              ]
            }
            """);
        return root;
    }

    private static byte[] ReadTextureRgba(GL gl, uint texture, int width, int height)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        return pixels;
    }

    private static int CountCyanPixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 80 && pixels[i + 1] > 130 && pixels[i + 2] > 130 && pixels[i + 3] > 100)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountRedPixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 150 && pixels[i] > pixels[i + 1] * 2 && pixels[i] > pixels[i + 2])
            {
                count++;
            }
        }

        return count;
    }

    private static int CountGreenPixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 1] > 150 && pixels[i + 1] > pixels[i] * 2 && pixels[i + 1] > pixels[i + 2])
            {
                count++;
            }
        }

        return count;
    }

    private static int CountBluePixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 2] > 140 && pixels[i + 2] > pixels[i] && pixels[i + 2] > pixels[i + 1])
            {
                count++;
            }
        }

        return count;
    }

    private static int CountWhitePixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 220 && pixels[i + 1] > 220 && pixels[i + 2] > 220 && pixels[i + 3] > 100)
            {
                count++;
            }
        }

        return count;
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

    private static string RepositoryPath(params string[] segments)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "PixelEngine.sln")))
        {
            root = Directory.GetParent(root)?.FullName
                ?? throw new DirectoryNotFoundException("找不到 PixelEngine repository root。");
        }

        return Path.Combine([root, .. segments]);
    }
}
