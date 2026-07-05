using PixelEngine.Gui;
using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class ManagedFallbackBackendTests
{
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
        Assert.Equal(1, count);
        Assert.Equal(new UiActionId(UiStableId.Hash("start_game")), events[0].Action);
    }

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

    private sealed class FakeGuiHost : IManagedFallbackGuiHost
    {
        public FakeGuiDrawContext Context { get; } = new();

        public bool IsRunning { get; private set; }

        public bool Initialized { get; private set; }

        public int DrawCount { get; private set; }

        public void Initialize()
        {
            Initialized = true;
            IsRunning = true;
        }

        public void DrawFrame(float deltaSeconds, int width, int height, Action<IGuiDrawContext> drawGui)
        {
            DrawCount++;
            drawGui(Context);
        }
    }

    private sealed class FakeGuiDrawContext : IGuiDrawContext
    {
        public List<string> Texts { get; } = [];

        public List<string> Buttons { get; } = [];

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
            LastWindowFlags = flags;
            return true;
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

        public bool Button(string label)
        {
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

        public void ColorSwatch(string id, uint colorBgra, float size = 16f)
        {
        }
    }
}
