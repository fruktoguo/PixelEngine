using PixelEngine.Rendering;
using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class UiInputRouterTests
{
    [Fact]
    public void PumpFeedsPointerKeyboardTextAndReturnsCapture()
    {
        RecordingBackend backend = new()
        {
            HitResult = new UiHitResult(HitsUi: true, Opaque: true, WantsMouse: true, WantsKeyboard: true),
        };
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        FakeInputSource input = new()
        {
            HasPointer = true,
            Pointer = new UiPointerState(12, 34, 1, -2, LeftDown: true, RightDown: false, MiddleDown: false),
            DownKeys = [new UiKey(65)],
            Modifiers = UiKeyModifiers.Control,
            Text = "a",
        };
        UiInputRouter router = new(host, input);

        UiInputCapture capture = router.Pump();

        Assert.False(capture.AllowWorldMouse);
        Assert.False(capture.AllowWorldKeyboard);
        Assert.Equal((12f, 34f), backend.LastPointer);
        Assert.Equal((1f, -2f), backend.LastScroll);
        Assert.Equal([(UiPointerButton.Left, true)], backend.PointerButtons);
        Assert.Equal([(new UiKey(65), true, UiKeyModifiers.Control)], backend.Keys);
        Assert.Equal("a", backend.Text);
        Assert.Equal(12f, backend.HitX);
        Assert.Equal(34f, backend.HitY);
    }

    [Fact]
    public void PumpRaisesReleaseEdgesAndAllowsWorldWhenUiDoesNotCapture()
    {
        RecordingBackend backend = new()
        {
            HitResult = UiHitResult.None,
        };
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        FakeInputSource input = new()
        {
            HasPointer = true,
            Pointer = new UiPointerState(5, 6, 0, 0, LeftDown: true, RightDown: false, MiddleDown: false),
            DownKeys = [new UiKey(65)],
        };
        UiInputRouter router = new(host, input);

        _ = router.Pump();
        input.Pointer = new UiPointerState(5, 6, 0, 0, LeftDown: false, RightDown: false, MiddleDown: false);
        input.DownKeys = [];
        UiInputCapture capture = router.Pump();

        Assert.True(capture.AllowWorldMouse);
        Assert.True(capture.AllowWorldKeyboard);
        Assert.Equal(
            [(UiPointerButton.Left, true), (UiPointerButton.Left, false)],
            backend.PointerButtons);
        Assert.Equal(
            [(new UiKey(65), true, UiKeyModifiers.None), (new UiKey(65), false, UiKeyModifiers.None)],
            backend.Keys);
    }

    private sealed class FakeInputSource : IUiInputSource
    {
        public bool HasPointer { get; set; }

        public UiPointerState Pointer { get; set; }

        public UiKey[] DownKeys { get; set; } = [];

        public UiKeyModifiers Modifiers { get; set; }

        public string Text { get; set; } = string.Empty;

        public bool TryGetPointer(out UiPointerState state)
        {
            state = Pointer;
            return HasPointer;
        }

        public int CaptureDownKeys(Span<UiKey> destination, out UiKeyModifiers modifiers)
        {
            modifiers = Modifiers;
            int count = Math.Min(destination.Length, DownKeys.Length);
            DownKeys.AsSpan(0, count).CopyTo(destination);
            return count;
        }

        public int CaptureText(Span<char> destination)
        {
            int count = Math.Min(destination.Length, Text.Length);
            Text.AsSpan(0, count).CopyTo(destination);
            Text = string.Empty;
            return count;
        }
    }

    private sealed class RecordingBackend : IGameUiBackend
    {
        public UiHitResult HitResult { get; init; }

        public (float X, float Y) LastPointer { get; private set; }

        public (float X, float Y) LastScroll { get; private set; }

        public List<(UiPointerButton Button, bool IsDown)> PointerButtons { get; } = [];

        public List<(UiKey Key, bool IsDown, UiKeyModifiers Modifiers)> Keys { get; } = [];

        public string Text { get; private set; } = string.Empty;

        public float HitX { get; private set; }

        public float HitY { get; private set; }

        public UiBackendKind Kind => UiBackendKind.ManagedFallback;

        public bool IsDirty => false;

        public bool IsAnimating => false;

        public void Initialize(in UiBackendInitializeInfo info)
        {
            info.Viewport.Validate();
        }

        public void Resize(in UiViewport viewport)
        {
            viewport.Validate();
        }

        public UiDocumentHandle LoadDocument(in UiDocumentSource source)
        {
            _ = source;
            return new UiDocumentHandle(1);
        }

        public void UnloadDocument(UiDocumentHandle document)
        {
            document.Validate();
        }

        public void SetScreenStack(ReadOnlySpan<UiScreenStackEntry> stack)
        {
            _ = stack;
        }

        public void Update(float deltaSeconds)
        {
            _ = deltaSeconds;
        }

        public void FeedPointerMove(float x, float y)
        {
            LastPointer = (x, y);
        }

        public void FeedPointerButton(UiPointerButton button, bool isDown)
        {
            PointerButtons.Add((button, isDown));
        }

        public void FeedScroll(float deltaX, float deltaY)
        {
            LastScroll = (deltaX, deltaY);
        }

        public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
        {
            Keys.Add((key, isDown, modifiers));
        }

        public void FeedText(ReadOnlySpan<char> text)
        {
            Text += text.ToString();
        }

        public UiHitResult HitTest(float x, float y)
        {
            HitX = x;
            HitY = y;
            return HitResult;
        }

        public void SetModelValue(UiDocumentHandle document, UiPathId path, in UiValue value)
        {
            _ = document;
            _ = path;
            _ = value;
        }

        public bool TryGetModelValue(UiDocumentHandle document, UiPathId path, out UiValue value)
        {
            _ = document;
            _ = path;
            value = default;
            return false;
        }

        public int DrainEvents(Span<UiEvent> destination)
        {
            _ = destination;
            return 0;
        }

        public void Composite(in UiPresentContext context)
        {
            _ = context;
        }

        public void Dispose()
        {
        }
    }
}
