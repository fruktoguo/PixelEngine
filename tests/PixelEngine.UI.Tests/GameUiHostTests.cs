using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// GameUiHost 宿主测试：初始化、帧循环与后端切换。
/// </summary>
public sealed class GameUiHostTests
{
    /// <summary>
    /// 验证Show Screen Loads Document Once And Tracks Stack。
    /// </summary>
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

    /// <summary>
    /// 验证Push Modal And Pop Modal Respect Top Only。
    /// </summary>
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

    /// <summary>
    /// 验证Disabled Host Rejects Document Load And不会Initialize Backend。
    /// </summary>
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

    /// <summary>
    /// 验证Initialize Passes Font Selection To Backend。
    /// </summary>
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

    /// <summary>
    /// 验证Preload Image路由To Supporting Backend。
    /// </summary>
    [Fact]
    public void PreloadImageRoutesToSupportingBackend()
    {
        FakeBackend backend = new();
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));

        Assert.True(host.PreloadImage("content/ui/images/logo.png"));

        Assert.Equal(1, backend.PreloadImageCount);
        Assert.Equal(Path.GetFullPath("content/ui/images/logo.png"), backend.LastPreloadedImage);
        Assert.Equal(0, host.Documents.DocumentCount);
        Assert.Equal(0, host.Documents.StackCount);
    }

    /// <summary>
    /// 验证Load Document Returns Existing Document Before Backend Load。
    /// </summary>
    [Fact]
    public void LoadDocumentReturnsExistingDocumentBeforeBackendLoad()
    {
        FakeBackend backend = new();
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiDocumentSource firstSource = UiDocumentSource.Asset("content/ui/main.html", 7);
        UiDocumentSource secondSource = UiDocumentSource.Asset("content/ui/main-duplicate.html", 7);

        UiDocumentHandle first = host.LoadDocument(new UiScreenId(7), in firstSource);
        UiDocumentHandle second = host.LoadDocument(new UiScreenId(7), in secondSource);

        Assert.Equal(first, second);
        Assert.Equal(1, backend.LoadCount);
        Assert.Equal(1, host.Documents.DocumentCount);
        Assert.Equal(0, host.Documents.StackCount);
        Assert.Equal(0, backend.LastScreenStackCount);
    }

    /// <summary>
    /// 验证Invoke Action For Visible Screen路由To Backend Document。
    /// </summary>
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

    /// <summary>
    /// 验证静态 backend 也会每帧 composite，避免每帧重建的 runtime framebuffer 丢失 HUD。
    /// </summary>
    [Fact]
    public void CompositeRunsEveryFrameWhileRuntimeSurfaceIsRebuilt()
    {
        FakeBackend backend = new();
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiDocumentSource source = UiDocumentSource.Asset("content/ui/main.html", 6);
        _ = host.ShowScreen(new UiScreenId(6), in source);

        Assert.True(host.NeedsComposite);
        host.Composite(default);

        Assert.Equal(1, backend.CompositeCount);
        Assert.Equal(1, backend.ClearDirtyCount);
        Assert.False(host.NeedsComposite);
        Assert.True(host.LastPaintMilliseconds >= 0);

        host.Composite(default);

        Assert.Equal(2, backend.CompositeCount);
        Assert.True(host.LastPaintMilliseconds >= 0);

        backend.IsAnimatingOverride = true;
        host.Composite(default);

        Assert.Equal(3, backend.CompositeCount);
        Assert.True(host.NeedsComposite);
    }

    /// <summary>
    /// 验证 paint interval 偏好不会跳过 final composite；静态 UI 在降频帧仍必须存在。
    /// </summary>
    [Fact]
    public void PresentationFrameIntervalDoesNotSkipRequiredFinalComposite()
    {
        FakeBackend backend = new()
        {
            IsAnimatingOverride = true,
        };
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        host.SetPresentationFrameInterval(2);

        host.Composite(default);
        host.Composite(default);
        host.Composite(default);

        Assert.Equal(3, backend.CompositeCount);
        Assert.Equal(0, host.SkippedPresentationFrames);
        Assert.Equal(2, host.PresentationIntervalFrames);
        Assert.True(host.NeedsComposite);
        Assert.True(host.LastPaintMilliseconds >= 0);
    }

    private sealed class FakeBackend : IGameUiBackend, IGameUiImagePreloader
    {
        private int _nextDocument = 1;

        public UiBackendKind Kind => UiBackendKind.ManagedFallback;

        public bool IsDirty { get; private set; }

        public bool IsAnimating => IsAnimatingOverride;

        public bool IsAnimatingOverride { get; set; }

        public bool Initialized { get; private set; }

        public UiFontSelection InitialFontSelection { get; private set; }

        public int LoadCount { get; private set; }

        public int LastScreenStackCount { get; private set; }

        public UiDocumentHandle LastInvokedDocument { get; private set; }

        public UiActionId LastInvokedAction { get; private set; }

        public UiValue LastInvokedPayload { get; private set; }

        public int CompositeCount { get; private set; }

        public int ClearDirtyCount { get; private set; }

        public int PreloadImageCount { get; private set; }

        public string LastPreloadedImage { get; private set; } = string.Empty;

        public void PreloadImage(string path)
        {
            PreloadImageCount++;
            LastPreloadedImage = Path.GetFullPath(path);
        }

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

        public void Composite(in Rendering.UiPresentContext context)
        {
            CompositeCount++;
            if (IsDirty)
            {
                ClearDirtyCount++;
            }

            IsDirty = false;
        }

        public void Dispose()
        {
        }
    }
}
