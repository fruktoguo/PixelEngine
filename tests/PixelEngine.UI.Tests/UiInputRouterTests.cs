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
        Assert.Empty(backend.Keys);
    }

    [Fact]
    public void KeyboardFocusPersistsAfterPointerLeavesAndClearsOnOutsideClick()
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
            Pointer = new UiPointerState(12, 34, 0, 0, LeftDown: false, RightDown: false, MiddleDown: false),
            DownKeys = [new UiKey(65)],
            Text = "a",
        };
        UiInputRouter router = new(host, input);

        UiInputCapture focused = router.Pump();
        backend.HitResult = UiHitResult.None;
        input.Pointer = new UiPointerState(200, 220, 0, 0, LeftDown: false, RightDown: false, MiddleDown: false);
        input.DownKeys = [new UiKey(66)];
        input.Text = "b";
        UiInputCapture movedOutside = router.Pump();
        input.Pointer = new UiPointerState(200, 220, 0, 0, LeftDown: true, RightDown: false, MiddleDown: false);
        input.DownKeys = [new UiKey(67)];
        input.Text = "c";
        UiInputCapture clickedOutside = router.Pump();

        Assert.False(focused.AllowWorldKeyboard);
        Assert.False(movedOutside.AllowWorldKeyboard);
        Assert.True(clickedOutside.AllowWorldKeyboard);
        Assert.Equal("ab", backend.Text);
        Assert.Equal(
            [
                (new UiKey(65), true, UiKeyModifiers.None),
                (new UiKey(66), true, UiKeyModifiers.None),
                (new UiKey(65), false, UiKeyModifiers.None),
                (new UiKey(66), false, UiKeyModifiers.None),
            ],
            backend.Keys);
    }

    [Fact]
    public void PumpRespectsUpstreamCaptureAndDrainsTextWithoutFeedingUi()
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
            Text = "stale",
        };
        UiInputRouter router = new(host, input);

        UiInputCapture capture = router.Pump(allowPointer: false, allowKeyboard: false);

        Assert.True(capture.AllowWorldMouse);
        Assert.True(capture.AllowWorldKeyboard);
        Assert.Equal((0f, 0f), backend.LastPointer);
        Assert.Equal((0f, 0f), backend.LastScroll);
        Assert.Empty(backend.PointerButtons);
        Assert.Empty(backend.Keys);
        Assert.Equal(string.Empty, backend.Text);
        Assert.Equal(string.Empty, input.Text);
    }

    [Fact]
    public void PumpFeedsCommittedTextInOrderAndFiltersControlCharacters()
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
            Pointer = new UiPointerState(12, 34, 0, 0, LeftDown: false, RightDown: false, MiddleDown: false),
            Text = "a\0中\r\nb\t",
        };
        UiInputRouter router = new(host, input);

        _ = router.Pump();

        Assert.Equal("a中b", backend.Text);
    }

    [Fact]
    public void PumpClampsCommittedTextToRouterBufferCapacity()
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
            Pointer = new UiPointerState(12, 34, 0, 0, LeftDown: false, RightDown: false, MiddleDown: false),
            Text = "abcdef",
            ReportedTextCount = 6,
        };
        UiInputRouter router = new(host, input, textCapacity: 3);

        _ = router.Pump();

        Assert.Equal("abc", backend.Text);
        Assert.Equal(string.Empty, input.Text);
    }

    [Fact]
    public void PumpDrainsCommittedTextWhenKeyboardIsBlocked()
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
            Pointer = new UiPointerState(12, 34, 0, 0, LeftDown: false, RightDown: false, MiddleDown: false),
            Text = "stale",
        };
        UiInputRouter router = new(host, input);

        _ = router.Pump(allowPointer: true, allowKeyboard: false);
        _ = router.Pump(allowPointer: true, allowKeyboard: true);

        Assert.Equal(string.Empty, backend.Text);
        Assert.Equal(string.Empty, input.Text);
    }

    [Fact]
    public void PumpFeedsImeCompositionSeparatelyFromCommittedTextAndClearsWhenInactive()
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
            Pointer = new UiPointerState(12, 34, 0, 0, LeftDown: false, RightDown: false, MiddleDown: false),
            Text = "中",
            CompositionText = "かな\0\n",
            Composition = new UiTextComposition(isActive: true, cursorIndex: 99, selectionStart: 1, selectionLength: 99),
        };
        UiInputRouter router = new(host, input);

        _ = router.Pump();
        input.CompositionText = string.Empty;
        input.Composition = UiTextComposition.Inactive;
        _ = router.Pump();

        Assert.Equal("中", backend.Text);
        Assert.Equal(["かな", string.Empty], backend.CompositionTexts);
        Assert.Equal(2, backend.Compositions.Count);
        Assert.True(backend.Compositions[0].IsActive);
        Assert.Equal(2, backend.Compositions[0].CursorIndex);
        Assert.Equal(1, backend.Compositions[0].SelectionStart);
        Assert.Equal(1, backend.Compositions[0].SelectionLength);
        Assert.False(backend.Compositions[1].IsActive);
    }

    [Fact]
    public void PumpDoesNotTreatCommittedTextAsImeCompositionWhenSourceHasNoComposition()
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
            Pointer = new UiPointerState(12, 34, 0, 0, LeftDown: false, RightDown: false, MiddleDown: false),
            Text = "a中",
        };
        UiInputRouter router = new(host, input);

        _ = router.Pump();

        Assert.Equal("a中", backend.Text);
        Assert.Empty(backend.Compositions);
        Assert.Empty(backend.CompositionTexts);
    }

    [Fact]
    public void TextCompositionCapabilitiesPassThroughInputSourceDiagnostic()
    {
        RecordingBackend backend = new()
        {
            HitResult = UiHitResult.None,
        };
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        FakeInputSource input = new()
        {
            TextCompositionCapabilities = UiTextCompositionCapabilities.Supported("synthetic platform IME composition events"),
        };

        UiInputRouter router = new(host, input);

        Assert.True(router.TextCompositionCapabilities.SupportsPlatformComposition);
        Assert.Equal("synthetic platform IME composition events", router.TextCompositionCapabilities.Diagnostic);
        router.TextCompositionCapabilities.Validate();
    }

    [Fact]
    public void InputSourceDefaultCompositionCapabilitiesAreUnsupportedAndDiagnostic()
    {
        IUiInputSource input = new DefaultCapabilityInputSource();

        UiTextCompositionCapabilities capabilities = input.TextCompositionCapabilities;

        Assert.False(capabilities.SupportsPlatformComposition);
        Assert.Contains("未声明真实平台 IME composition 事件支持", capabilities.Diagnostic, StringComparison.Ordinal);
        capabilities.Validate();
    }

    private sealed class FakeInputSource : IUiInputSource
    {
        public bool HasPointer { get; set; }

        public UiPointerState Pointer { get; set; }

        public UiKey[] DownKeys { get; set; } = [];

        public UiKeyModifiers Modifiers { get; set; }

        public string Text { get; set; } = string.Empty;

        public string CompositionText { get; set; } = string.Empty;

        public UiTextComposition Composition { get; set; }

        public UiTextCompositionCapabilities TextCompositionCapabilities { get; set; } =
            UiTextCompositionCapabilities.Unsupported("Fake input source does not support platform composition.");

        public int? ReportedTextCount { get; set; }

        public int? ReportedCompositionTextCount { get; set; }

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
            return ReportedTextCount ?? count;
        }

        public int CaptureTextComposition(Span<char> destination, out UiTextComposition composition)
        {
            int count = Math.Min(destination.Length, CompositionText.Length);
            CompositionText.AsSpan(0, count).CopyTo(destination);
            composition = Composition;
            return ReportedCompositionTextCount ?? count;
        }
    }

    private sealed class DefaultCapabilityInputSource : IUiInputSource
    {
        public bool TryGetPointer(out UiPointerState state)
        {
            state = default;
            return false;
        }

        public int CaptureDownKeys(Span<UiKey> destination, out UiKeyModifiers modifiers)
        {
            _ = destination;
            modifiers = UiKeyModifiers.None;
            return 0;
        }

        public int CaptureText(Span<char> destination)
        {
            _ = destination;
            return 0;
        }
    }

    private sealed class RecordingBackend : IGameUiBackend
    {
        public UiHitResult HitResult { get; set; }

        public (float X, float Y) LastPointer { get; private set; }

        public (float X, float Y) LastScroll { get; private set; }

        public List<(UiPointerButton Button, bool IsDown)> PointerButtons { get; } = [];

        public List<(UiKey Key, bool IsDown, UiKeyModifiers Modifiers)> Keys { get; } = [];

        public string Text { get; private set; } = string.Empty;

        public List<string> CompositionTexts { get; } = [];

        public List<UiTextComposition> Compositions { get; } = [];

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

        public void FeedTextComposition(ReadOnlySpan<char> text, in UiTextComposition composition)
        {
            CompositionTexts.Add(text.ToString());
            Compositions.Add(composition);
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

        public int CopyModelPaths(UiDocumentHandle document, Span<UiPathId> destination)
        {
            _ = document;
            _ = destination;
            return 0;
        }

        public bool InvokeAction(UiDocumentHandle document, UiActionId action, in UiValue payload)
        {
            _ = document;
            _ = action;
            _ = payload;
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
