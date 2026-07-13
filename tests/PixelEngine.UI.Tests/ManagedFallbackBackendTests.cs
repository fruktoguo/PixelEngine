using PixelEngine.Gui;
using System.Buffers.Binary;
using System.IO.Compression;
using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// 托管回退 UI 后端测试：XHTML 控件绘制、静态屏幕跳过、事件排空与离屏合成。
/// </summary>
public sealed class ManagedFallbackBackendTests
{
    /// <summary>
    /// 验证托管回退绘制 XHTML 控件并排空按钮点击事件。
    /// </summary>
    [Fact]
    public void ManagedFallbackDrawsXhtmlControlsAndDrainsButtonEvent()
    {
        string path = WriteUi("""
            <ui title="Main">
              <text id="greeting">Hello</text>
              <button id="start" data-event-click="start_game">Start</button>
            </ui>
            """);
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));

        UiDocumentSource source = UiDocumentSource.Asset(path, 1);
        _ = host.ShowScreen(new UiScreenId(1), in source);
        _ = gui.Context.ClickedButtons.Add("Start");

        host.Composite(default);
        UiEvent[] events = new UiEvent[4];
        int count = backend.DrainEvents(events);

        Assert.True(gui.Initialized);
        Assert.Equal(1, gui.DrawCount);
        Assert.Contains("Hello", gui.Context.Texts);
        Assert.Contains("Start", gui.Context.Buttons);
        Assert.True(gui.Context.LastWindowFlags.HasFlag(GuiDrawWindowFlags.NoTitleBar));
        Assert.True(gui.Context.LastWindowFlags.HasFlag(GuiDrawWindowFlags.NoSavedSettings));
        Assert.True(gui.Context.LastWindowFlags.HasFlag(GuiDrawWindowFlags.NoScrollbar));
        Assert.Equal(1, count);
        Assert.Equal(new UiActionId(UiStableId.Hash("start_game")), events[0].Action);
    }

    /// <summary>
    /// 验证托管回退跳过静态屏幕直到模型变更。
    /// </summary>
    [Fact]
    public void ManagedFallbackSkipsStaticScreenUntilModelChanges()
    {
        string path = WriteUi("""
            <ui title="Hud">
              <progress id="health" path="hud.health" text="HP" value="0.5" />
            </ui>
            """);
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));

        UiDocumentSource source = UiDocumentSource.Asset(path, 6);
        UiScreenHandle screen = host.ShowScreen(new UiScreenId(6), in source);
        host.Composite(default);
        host.Composite(default);

        Assert.Equal(2, gui.DrawCount);
        Assert.False(host.NeedsComposite);
        Assert.True(host.LastPaintMilliseconds >= 0);

        UiPathId health = new(UiStableId.Hash("hud.health"));
        host.SetModelValue(screen, health, new UiValue(0.75));
        host.Composite(default);

        Assert.Equal(3, gui.DrawCount);
        Assert.False(host.NeedsComposite);
    }

    /// <summary>
    /// 验证托管回退每帧重放静态屏幕，模型变更后继续使用同一 runtime surface 路径。
    /// </summary>
    [Fact]
    public void ManagedFallbackDrawGuiReplaysStaticScreenEveryFrame()
    {
        string path = WriteUi("""
            <ui title="Hud">
              <button id="refresh" data-event-click="refresh">Refresh</button>
            </ui>
            """);
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));

        UiDocumentSource source = UiDocumentSource.Asset(path, 7);
        UiScreenHandle screen = host.ShowScreen(new UiScreenId(7), in source);
        host.DrawGui(gui.Context);
        host.DrawGui(gui.Context);

        Assert.Equal(["Refresh", "Refresh"], gui.Context.Buttons);
        Assert.False(host.NeedsComposite);
        Assert.True(host.LastPaintMilliseconds >= 0);

        Assert.True(host.InvokeAction(screen, new UiActionId(UiStableId.Hash("refresh")), new UiValue(1L)));
        host.DrawGui(gui.Context);

        Assert.Equal(3, gui.Context.Buttons.Count);
        Assert.False(host.NeedsComposite);
    }

    /// <summary>
    /// 多 Canvas 共用同一 ImGui context 时，本地 screen handle 即使相同也必须生成不同窗口 id。
    /// </summary>
    [Fact]
    public void ManagedFallbackNamespacesWindowIdsAcrossBackendsSharingGuiContext()
    {
        string firstPath = WriteUi("""
            <ui title="First" style="left: 12px; top: 20px; width: 120px; height: 48px">
              <text id="first">First canvas</text>
            </ui>
            """);
        string secondPath = WriteUi("""
            <ui title="Second" style="left: 180px; top: 80px; width: 140px; height: 48px">
              <text id="second">Second canvas</text>
            </ui>
            """);
        FakeGuiHost gui = new();
        using ManagedFallbackBackend firstBackend = new(gui);
        using ManagedFallbackBackend secondBackend = new(gui);
        using GameUiHost firstHost = new(firstBackend);
        using GameUiHost secondHost = new(secondBackend);
        UiBackendInitializeInfo initialize = new(
            new UiViewport(0, 0, 320, 240, 1f),
            UiBackendKind.ManagedFallback);
        firstHost.Initialize(in initialize);
        secondHost.Initialize(in initialize);

        UiScreenHandle firstScreen = firstHost.ShowScreen(
            new UiScreenId(1),
            UiDocumentSource.Asset(firstPath, 1));
        UiScreenHandle secondScreen = secondHost.ShowScreen(
            new UiScreenId(2),
            UiDocumentSource.Asset(secondPath, 2));
        firstHost.DrawGui(gui.Context);
        secondHost.DrawGui(gui.Context);

        Assert.Equal(1, firstScreen.Value);
        Assert.Equal(1, secondScreen.Value);
        Assert.Equal(2, gui.Context.WindowIds.Count);
        Assert.Equal(2, gui.Context.WindowIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(gui.Context.WindowIds, static id => Assert.EndsWith("_1", id, StringComparison.Ordinal));
        Assert.Contains("First canvas", gui.Context.Texts);
        Assert.Contains("Second canvas", gui.Context.Texts);
    }


    /// <summary>
    /// 验证托管回退Checkbox Updates Model Value And Raises Event。
    /// </summary>
    [Fact]
    public void ManagedFallbackCheckboxUpdatesModelValueAndRaisesEvent()
    {
        string path = WriteUi("""
            <ui title="Settings">
              <checkbox id="music" path="settings.music" text="Music" checked="false" />
            </ui>
            """);
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));

        UiDocumentSource source = UiDocumentSource.Asset(path, 2);
        UiDocumentHandle document = host.LoadDocument(new UiScreenId(2), in source);
        _ = host.PushModal(new UiScreenId(2), in source);
        _ = gui.Context.ToggledCheckboxes.Add("Music");

        host.Composite(default);
        UiPathId pathId = new(UiStableId.Hash("settings.music"));
        UiPathId[] paths = new UiPathId[4];

        Assert.True(backend.TryGetModelValue(document, pathId, out UiValue value));
        Assert.True(value.AsBoolean());
        Assert.Equal(1, backend.CopyModelPaths(document, paths));
        Assert.Equal(pathId, paths[0]);
        UiEvent[] events = new UiEvent[4];
        Assert.Equal(1, backend.DrainEvents(events));
        Assert.True(events[0].Payload.AsBoolean());
        Assert.True(backend.HitTest(10, 10).WantsMouse);
    }

    /// <summary>
    /// 验证托管回退Invoke Action Updates Matching Control Value。
    /// </summary>
    [Fact]
    public void ManagedFallbackInvokeActionUpdatesMatchingControlValue()
    {
        string path = WriteUi("""
            <ui title="Settings">
              <checkbox id="music" data-event-change="toggle_music" path="settings.music" text="Music" checked="false" />
            </ui>
            """);
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));

        UiDocumentSource source = UiDocumentSource.Asset(path, 5);
        UiScreenHandle screen = host.ShowScreen(new UiScreenId(5), in source);
        UiActionId action = new(UiStableId.Hash("toggle_music"));
        UiPathId pathId = new(UiStableId.Hash("settings.music"));

        Assert.True(host.InvokeAction(screen, action, UiValue.FromBoolean(true)));
        Assert.True(host.TryGetModelValue(screen, pathId, out UiValue value));
        Assert.True(value.AsBoolean());
        Assert.True(host.NeedsComposite);
    }

    /// <summary>
    /// 验证托管回退Applies Root Box Model Before Drawing Window。
    /// </summary>
    [Fact]
    public void ManagedFallbackAppliesRootBoxModelBeforeDrawingWindow()
    {
        string path = WriteUi("""
            <ui title="Panel" style="left: 12px; top: 20px; width: 240px; height: 96px">
              <p>Boxed</p>
            </ui>
            """);
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));

        UiDocumentSource source = UiDocumentSource.Asset(path, 4);
        _ = host.ShowScreen(new UiScreenId(4), in source);
        host.Composite(default);

        Assert.Equal((12f, 20f, 240f, 96f), gui.Context.LastWindow);
        Assert.Contains("Boxed", gui.Context.Texts);
        Assert.True((gui.Context.LastWindowFlags & GuiDrawWindowFlags.NoResize) != 0);
        Assert.True((gui.Context.LastWindowFlags & GuiDrawWindowFlags.NoMove) != 0);
    }

    /// <summary>
    /// 验证托管回退Consumes Simple Style Rules For Control Layout。
    /// </summary>
    [Fact]
    public void ManagedFallbackConsumesSimpleStyleRulesForControlLayout()
    {
        string path = WriteUi("""
            <rml title="Menu" style="left: 24px; top: 24px; width: 280px; height: 206px">
              <head>
                <style>
                  button { width: 180px; height: 28px; margin: 10px 0px; margin-top: 6px; }
                  .primary { width: 196px; height: 32px; margin-top: 8px; }
                  progress { width: 240px; height: 14px; margin-top: 4px; }
                  p { margin: 5px 0px; }
                  #start_game { position: absolute; left: 28px; top: 72px; }
                </style>
              </head>
              <body>
                <p>Ready</p>
                <button id="start_game" class="primary" data-event-click="start_game">开始游戏</button>
                <progress id="health" path="hud.health" value="0.5" text="生命" />
              </body>
            </rml>
            """);
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));

        _ = host.ShowScreen(new UiScreenId(10), UiDocumentSource.Asset(path, 10));
        host.Composite(default);

        Assert.Equal((28f, 72f), Assert.Single(gui.Context.Cursors));
        Assert.Equal(("开始游戏", 196f, 32f), Assert.Single(gui.Context.SizedButtons));
        Assert.Equal((0.5f, "生命", 240f, 14f), Assert.Single(gui.Context.SizedProgressBars));
        Assert.Contains("Ready", gui.Context.Texts);
        Assert.Contains(5f, gui.Context.VerticalSpacings);
        Assert.Contains(4f, gui.Context.VerticalSpacings);
    }

    /// <summary>
    /// CanvasScaler 必须按同一比例缩放窗口、显式控件布局和 GUI 样式作用域，并在绘制后恢复作用域。
    /// </summary>
    [Fact]
    public void ManagedFallbackAppliesAndBalancesCompleteCanvasScaleScope()
    {
        string path = WriteUi("""
            <rml title="Scaled" style="left: 12px; top: 20px; width: 240px; height: 96px">
              <head><style>button { width: 100px; height: 20px; margin-top: 6px; }</style></head>
              <body><button id="scaled">Scaled</button></body>
            </rml>
            """);
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        UiDisplayMetrics display = new(640, 480, 1f, 1f, 96f, 1, 1);
        UiCanvasScalerSettings settings = UiCanvasScalerSettings.Default with { ScaleFactor = 2f };
        host.Initialize(new UiBackendInitializeInfo(display, settings, UiBackendKind.ManagedFallback));

        _ = host.ShowScreen(new UiScreenId(12), UiDocumentSource.Asset(path, 12));
        host.Composite(default);

        Assert.Equal((24f, 40f, 480f, 192f), gui.Context.LastWindow);
        Assert.Equal(("Scaled", 200f, 40f), Assert.Single(gui.Context.SizedButtons));
        Assert.Contains(12f, gui.Context.VerticalSpacings);
        Assert.Equal([2f], gui.Context.CanvasScales);
        Assert.Equal(1, gui.Context.CanvasScalePopCount);
        Assert.Equal(0, gui.Context.CanvasScaleDepth);
    }

    /// <summary>
    /// 验证托管回退Consumes Tag Class Rules In Source Order。
    /// </summary>
    [Fact]
    public void ManagedFallbackConsumesTagClassRulesInSourceOrder()
    {
        string path = WriteUi("""
            <rml title="Menu" style="left: 24px; top: 24px; width: 280px; height: 206px">
              <head>
                <style>
                  .primary { width: 180px; height: 26px; margin-top: 4px; }
                  button.primary { height: 34px; margin-top: 9px; }
                  button.primary { width: 220px; }
                  .wide { width: 196px; }
                  #start_game { top: 64px; }
                </style>
              </head>
              <body>
                <button id="start_game" class="wide primary" data-event-click="start_game">开始游戏</button>
              </body>
            </rml>
            """);
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));

        _ = host.ShowScreen(new UiScreenId(11), UiDocumentSource.Asset(path, 11));
        host.Composite(default);

        (string _, float width, float height) = Assert.Single(gui.Context.SizedButtons, static item => item.Label == "开始游戏");
        Assert.Equal((220f, 34f), (width, height));
        Assert.Contains(9f, gui.Context.VerticalSpacings);
    }

    /// <summary>
    /// 验证托管回退Draws Image Control From Images Directory。
    /// </summary>
    [Fact]
    public void ManagedFallbackDrawsImageControlFromImagesDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pixelengine-ui-image-{Guid.NewGuid():N}");
        string screens = Path.Combine(root, "screens");
        string images = Path.Combine(root, "images");
        _ = Directory.CreateDirectory(screens);
        _ = Directory.CreateDirectory(images);
        string imagePath = Path.Combine(images, "logo.png");
        WritePng(imagePath, 3, 2);
        string documentPath = Path.Combine(screens, "main.xhtml");
        File.WriteAllText(
            documentPath,
            """
            <ui title="Image">
              <img id="logo" data-image="logo" width="24" height="16" alt="Logo" />
            </ui>
            """);

        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));

        _ = host.ShowScreen(new UiScreenId(8), UiDocumentSource.Asset(documentPath, 8));
        host.Composite(default);

        Assert.Equal(Path.GetFullPath(imagePath), Assert.Single(gui.LoadedImages));
        FakeGuiDrawContext.ImageCall image = Assert.Single(gui.Context.Images);
        Assert.Equal("logo", image.Id);
        Assert.Equal(3, image.TextureWidth);
        Assert.Equal(2, image.TextureHeight);
        Assert.Equal(24f, image.Width);
        Assert.Equal(16f, image.Height);
    }

    /// <summary>
    /// 验证托管回退Composite Uses Initialized Viewport When Present Context Has No Target。
    /// </summary>
    [Fact]
    public void ManagedFallbackCompositeUsesInitializedViewportWhenPresentContextHasNoTarget()
    {
        string path = WriteUi("""
            <ui title="Hud">
              <text id="status">Ready</text>
            </ui>
            """);
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(4, 8, 320, 180, 1f), UiBackendKind.ManagedFallback));

        _ = host.ShowScreen(new UiScreenId(9), UiDocumentSource.Asset(path, 9));
        host.Composite(default);

        Assert.Equal(320, gui.LastFrameWidth);
        Assert.Equal(180, gui.LastFrameHeight);
    }

    /// <summary>
    /// 验证托管回退Composite Source Documents Present Target Frame Size。
    /// </summary>
    [Fact]
    public void ManagedFallbackCompositeSourceDocumentsPresentTargetFrameSize()
    {
        string source = File.ReadAllText(ProjectPath("src", "PixelEngine.UI", "ManagedFallbackBackend.cs"));

        Assert.Contains("context.Target.IsValid", source, StringComparison.Ordinal);
        Assert.Contains("context.Target.Width", source, StringComparison.Ordinal);
        Assert.Contains("context.Target.Height", source, StringComparison.Ordinal);
        Assert.DoesNotContain("context.FramebufferWidth, context.FramebufferHeight", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证托管回退Throws For Missing Document Instead Of Inventing Placeholder。
    /// </summary>
    [Fact]
    public void ManagedFallbackThrowsForMissingDocumentInsteadOfInventingPlaceholder()
    {
        FakeGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);

        UiDocumentSource source = UiDocumentSource.Asset(Path.Combine(Path.GetTempPath(), "missing-ui-file.xhtml"), 3);

        _ = Assert.Throws<FileNotFoundException>(() => backend.LoadDocument(in source));
    }

    private static string WriteUi(string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"pixelengine-ui-{Guid.NewGuid():N}.xhtml");
        File.WriteAllText(path, contents);
        return path;
    }

    private static string ProjectPath(params string[] parts)
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "PixelEngine.sln")))
            {
                string result = current;
                foreach (string part in parts)
                {
                    result = Path.Combine(result, part);
                }

                return result;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("无法定位 PixelEngine.sln。");
    }

    private sealed class FakeGuiHost : IManagedFallbackGuiHost
    {
        public FakeGuiDrawContext Context { get; } = new();

        public bool IsRunning { get; private set; }

        public bool Initialized { get; private set; }

        public int DrawCount { get; private set; }

        public int LastFrameWidth { get; private set; }

        public int LastFrameHeight { get; private set; }

        public List<string> LoadedImages { get; } = [];

        public void Initialize()
        {
            Initialized = true;
            IsRunning = true;
        }

        public void DrawFrame(float deltaSeconds, int width, int height, Action<IGuiDrawContext> drawGui)
        {
            DrawCount++;
            LastFrameWidth = width;
            LastFrameHeight = height;
            drawGui(Context);
        }

        public ManagedFallbackImage LoadImage(string path)
        {
            string fullPath = Path.GetFullPath(path);
            LoadedImages.Add(fullPath);
            Assert.True(File.Exists(fullPath), fullPath);
            return new ManagedFallbackImage(123, 3, 2);
        }
    }

    private sealed class FakeGuiDrawContext : IGuiDrawContext
    {
        public List<string> Texts { get; } = [];

        public List<string> Buttons { get; } = [];

        public List<ImageCall> Images { get; } = [];

        public List<(float X, float Y)> Cursors { get; } = [];

        public List<float> VerticalSpacings { get; } = [];

        public List<(string Label, float Width, float Height)> SizedButtons { get; } = [];

        public List<(float Value, string? Label, float Width, float Height)> SizedProgressBars { get; } = [];

        public List<float> CanvasScales { get; } = [];

        public List<string> WindowIds { get; } = [];

        public int CanvasScalePopCount { get; private set; }

        public int CanvasScaleDepth { get; private set; }

        public HashSet<string> ClickedButtons { get; } = [];

        public HashSet<string> ToggledCheckboxes { get; } = [];

        public (float X, float Y, float Width, float Height) LastWindow { get; private set; }

        public GuiDrawWindowFlags LastWindowFlags { get; private set; }

        public int Width => 320;

        public int Height => 240;

        public float DeltaTime => 1f / 60f;

        public bool WantsMouse => false;

        public bool WantsKeyboard => false;

        public void SetNextWindow(float x, float y, float width, float height, GuiDrawCondition condition = GuiDrawCondition.Always)
        {
            LastWindow = (x, y, width, height);
            _ = condition;
        }

        public bool BeginWindow(string id, string title, GuiDrawWindowFlags flags = GuiDrawWindowFlags.None)
        {
            WindowIds.Add(id);
            LastWindowFlags = flags;
            return true;
        }

        public void PushCanvasScale(float scale)
        {
            CanvasScales.Add(scale);
            CanvasScaleDepth++;
        }

        public void PopCanvasScale()
        {
            CanvasScalePopCount++;
            CanvasScaleDepth--;
        }

        public void EndWindow()
        {
        }

        public void Text(string text)
        {
            Texts.Add(text);
        }

        public void TextColored(string text, uint colorBgra)
        {
            Texts.Add(text);
        }

        public void SameLine()
        {
        }

        public void Separator()
        {
        }

        public void SetCursor(float x, float y)
        {
            Cursors.Add((x, y));
        }

        public void AddVerticalSpacing(float height)
        {
            VerticalSpacings.Add(height);
        }

        public bool Button(string label)
        {
            Buttons.Add(label);
            return ClickedButtons.Remove(label);
        }

        public bool Button(string label, float width, float height)
        {
            SizedButtons.Add((label, width, height));
            Buttons.Add(label);
            return ClickedButtons.Remove(label);
        }

        public bool Checkbox(string label, ref bool value)
        {
            if (!ToggledCheckboxes.Remove(label))
            {
                return false;
            }

            value = !value;
            return true;
        }

        public void ProgressBar(float value01, string? label = null)
        {
        }

        public void ProgressBar(float value01, string? label, float width, float height)
        {
            SizedProgressBars.Add((value01, label, width, height));
        }

        public void ColorSwatch(string id, uint colorBgra, float size = 16f)
        {
        }

        public void Image(string id, uint textureHandle, int textureWidth, int textureHeight, float width, float height, uint tintBgra = 0xFF_FF_FF_FF)
        {
            Images.Add(new ImageCall(id, textureHandle, textureWidth, textureHeight, width, height, tintBgra));
        }

        public readonly record struct ImageCall(string Id, uint TextureHandle, int TextureWidth, int TextureHeight, float Width, float Height, uint TintBgra);
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
                    zlib.WriteByte((byte)(x * 40));
                    zlib.WriteByte((byte)(y * 80));
                    zlib.WriteByte(160);
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
