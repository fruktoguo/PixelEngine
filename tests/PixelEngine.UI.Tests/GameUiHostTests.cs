using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class GameUiHostTests
{
    [Fact]
    public void ShowScreenLoadsDocumentOnceAndTracksStack()
    {
        FakeBackend backend = new();
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 1280, 720, 1f), UiBackendKind.ManagedFallback));

        UiDocumentSource source = UiDocumentSource.Asset("content/ui/main.html", 100);
        UiScreenHandle first = host.ShowScreen(new UiScreenId(1), in source);
        UiScreenHandle second = host.ShowScreen(new UiScreenId(1), in source);

        Assert.NotEqual(default, first);
        Assert.NotEqual(default, second);
        Assert.Equal(1, backend.LoadCount);
        Assert.Equal(2, backend.LastScreenStackCount);
        Assert.Equal(1, host.Documents.DocumentCount);
        Assert.Equal(2, host.Documents.StackCount);
    }

    [Fact]
    public void PushModalAndPopModalRespectTopOnly()
    {
        FakeBackend backend = new();
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 640, 480, 1f), UiBackendKind.ManagedFallback));

        UiDocumentSource main = UiDocumentSource.Asset("content/ui/main.html", 1);
        UiDocumentSource modal = UiDocumentSource.Asset("content/ui/settings.html", 2);
        _ = host.ShowScreen(new UiScreenId(1), in main);
        _ = host.PushModal(new UiScreenId(2), in modal);

        Assert.True(host.Documents.HasModalTop);
        Assert.Equal(2, backend.LastScreenStackCount);
        Assert.True(host.PopModal());
        Assert.False(host.Documents.HasModalTop);
        Assert.Equal(1, backend.LastScreenStackCount);
        Assert.False(host.PopModal());
    }

    [Fact]
    public void DisabledHostRejectsDocumentLoadAndDoesNotInitializeBackend()
    {
        FakeBackend backend = new();
        using GameUiHost host = new(backend, new GameUiHostOptions(false, 4, 4));

        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));

        UiDocumentSource source = UiDocumentSource.Asset("content/ui/main.html", 3);
        _ = Assert.Throws<InvalidOperationException>(() => host.ShowScreen(new UiScreenId(1), in source));
        Assert.False(backend.Initialized);
    }

    [Fact]
    public void InitializePassesFontSelectionToBackend()
    {
        FakeBackend backend = new();
        using GameUiHost host = new(backend);
        UiFontSelection selection = new("content/ui/fonts/msyh.ttc", 24f, UiFontSource.ContentFonts);

        host.Initialize(new UiBackendInitializeInfo(
            new UiViewport(0, 0, 320, 240, 1f),
            UiBackendKind.ManagedFallback,
            selection));

        Assert.Equal(selection, backend.InitialFontSelection);
    }

    [Fact]
    public void InvokeActionForVisibleScreenRoutesToBackendDocument()
    {
        FakeBackend backend = new();
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiDocumentSource source = UiDocumentSource.Asset("content/ui/main.html", 4);
        UiScreenHandle screen = host.ShowScreen(new UiScreenId(4), in source);
        UiActionId action = new(17);
        UiValue payload = new(42L);

        Assert.True(host.InvokeAction(screen, action, in payload));
        Assert.Equal(new UiDocumentHandle(1), backend.LastInvokedDocument);
        Assert.Equal(action, backend.LastInvokedAction);
        Assert.Equal(payload, backend.LastInvokedPayload);
        Assert.False(host.InvokeAction(new UiScreenHandle(999), action, in payload));
    }

    private sealed class FakeBackend : IGameUiBackend
    {
        private int _nextDocument = 1;

        public UiBackendKind Kind => UiBackendKind.ManagedFallback;

        public bool IsDirty { get; private set; }

        public bool IsAnimating => false;

        public bool Initialized { get; private set; }

        public UiFontSelection InitialFontSelection { get; private set; }

        public int LoadCount { get; private set; }

        public int LastScreenStackCount { get; private set; }

        public UiDocumentHandle LastInvokedDocument { get; private set; }

        public UiActionId LastInvokedAction { get; private set; }

        public UiValue LastInvokedPayload { get; private set; }

        public void Initialize(in UiBackendInitializeInfo info)
        {
            info.Viewport.Validate();
            InitialFontSelection = info.FontSelection;
            Initialized = true;
        }

        public void Resize(in UiViewport viewport)
        {
            viewport.Validate();
        }

        public UiDocumentHandle LoadDocument(in UiDocumentSource source)
        {
            LoadCount++;
            IsDirty = true;
            return new UiDocumentHandle(_nextDocument++);
        }

        public void UnloadDocument(UiDocumentHandle document)
        {
        }

        public void SetScreenStack(ReadOnlySpan<UiScreenStackEntry> stack)
        {
            LastScreenStackCount = stack.Length;
        }

        public void Update(float deltaSeconds)
        {
        }

        public void FeedPointerMove(float x, float y)
        {
        }

        public void FeedPointerButton(UiPointerButton button, bool isDown)
        {
        }

        public void FeedScroll(float deltaX, float deltaY)
        {
        }

        public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
        {
        }

        public void FeedText(ReadOnlySpan<char> text)
        {
        }

        public UiHitResult HitTest(float x, float y)
        {
            return UiHitResult.None;
        }

        public void SetModelValue(UiDocumentHandle document, UiPathId path, in UiValue value)
        {
        }

        public bool TryGetModelValue(UiDocumentHandle document, UiPathId path, out UiValue value)
        {
            value = default;
            return false;
        }

        public int CopyModelPaths(UiDocumentHandle document, Span<UiPathId> destination)
        {
            _ = document;
            _ = destination;
            return 0;
        }

        public bool InvokeAction(UiDocumentHandle document, UiActionId action, in UiValue payload)
        {
            LastInvokedDocument = document;
            LastInvokedAction = action;
            LastInvokedPayload = payload;
            return true;
        }

        public int DrainEvents(Span<UiEvent> destination)
        {
            return 0;
        }

        public void Composite(in PixelEngine.Rendering.UiPresentContext context)
        {
        }

        public void Dispose()
        {
        }
    }
}
