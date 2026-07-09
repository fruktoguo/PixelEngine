using PixelEngine.Gui;
using PixelEngine.Rendering;
using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// UI 后端一致性测试：托管回退与 RmlUi 在生命周期、屏幕栈、命中测试与模型路径上共享同一契约。
/// </summary>
public sealed class BackendConformanceTests : IDisposable
{
    private readonly List<string> _temporaryUiFiles = [];

    /// <summary>
    /// 验证托管回退生命周期、Resize、加载与屏幕栈暴露共享契约。
    /// </summary>
    [Fact]
    public void ManagedFallbackLifecycleResizeLoadAndScreenStackExposeSharedContract()
    {
        string passivePath = WriteUi("""
            <ui title="Status">
              <text id="title">Status</text>
            </ui>
            """);
        string interactivePath = WriteUi("""
            <ui title="Main">
              <text id="title">Main</text>
              <button id="start" data-event-click="start_game">Start</button>
            </ui>
            """);
        using ManagedFallbackBackend backend = CreateManagedBackend(out _);

        backend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        backend.Resize(new UiViewport(0, 0, 640, 480, 1f));
        UiDocumentHandle passiveDocument = backend.LoadDocument(UiDocumentSource.Asset(passivePath, 1));
        UiDocumentHandle interactiveDocument = backend.LoadDocument(UiDocumentSource.Asset(interactivePath, 2));
        backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(1), passiveDocument, Modal: false)]);

        UiHitResult passThrough = backend.HitTest(12, 16);
        Assert.True(backend.IsDirty);
        Assert.True(passThrough.HitsUi);
        Assert.False(passThrough.Opaque);
        Assert.False(passThrough.WantsMouse);
        Assert.False(passThrough.WantsKeyboard);

        backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(2), new UiScreenId(2), interactiveDocument, Modal: false)]);
        UiHitResult interactive = backend.HitTest(12, 16);
        Assert.True(interactive.HitsUi);
        Assert.False(interactive.Opaque);
        Assert.True(interactive.WantsMouse);
        Assert.True(interactive.WantsKeyboard);

        backend.Composite(default);
        Assert.False(backend.IsDirty);

        backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(3), new UiScreenId(2), interactiveDocument, Modal: true)]);
        UiHitResult modal = backend.HitTest(12, 16);

        Assert.True(modal.HitsUi);
        Assert.True(modal.Opaque);
        Assert.True(modal.WantsMouse);
        Assert.True(modal.WantsKeyboard);
    }

    /// <summary>
    /// 验证复制模型路径截断到目标缓冲And保持稳定唯一顺序。
    /// </summary>
    [Fact]
    public void CopyModelPathsTruncatesToDestinationAndKeepsStableUniqueOrder()
    {
        string path = WriteUi("""
            <ui title="Hud">
              <progress id="health" path="hud.health" value="0.5" />
              <checkbox id="music" path="settings.music" text="Music" checked="false" />
              <progress id="health_mirror" path="hud.health" value="0.5" />
              <progress id="mission" path="mission.progress" value="0.25" />
            </ui>
            """);
        using ManagedFallbackBackend backend = CreateManagedBackend(out _);
        backend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(path, 2));
        UiPathId health = UiModelPathName.ToPathId("hud.health");
        UiPathId music = UiModelPathName.ToPathId("settings.music");
        UiPathId mission = UiModelPathName.ToPathId("mission.progress");

        Span<UiPathId> shortBuffer = stackalloc UiPathId[2];
        int shortCount = backend.CopyModelPaths(document, shortBuffer);
        UiPathId[] fullBuffer = [new UiPathId(-1), new UiPathId(-1), new UiPathId(-1), new UiPathId(-1)];
        int fullCount = backend.CopyModelPaths(document, fullBuffer);

        Assert.Equal(2, shortCount);
        Assert.Equal(health, shortBuffer[0]);
        Assert.Equal(music, shortBuffer[1]);
        Assert.Equal(3, fullCount);
        Assert.Equal([health, music, mission], fullBuffer[..fullCount]);
        Assert.Equal(new UiPathId(-1), fullBuffer[3]);
        Span<UiPathId> empty = [];
        Assert.Equal(0, backend.CopyModelPaths(document, empty));
    }

    /// <summary>
    /// 验证Drain Events Respects Destination Capacity And Ring Overflow Boundary。
    /// </summary>
    [Fact]
    public void DrainEventsRespectsDestinationCapacityAndRingOverflowBoundary()
    {
        string path = WriteUi("""
            <ui title="Menu">
              <button id="one" data-event-click="one">One</button>
              <button id="two" data-event-click="two">Two</button>
              <button id="three" data-event-click="three">Three</button>
            </ui>
            """);
        using ManagedFallbackBackend backend = CreateManagedBackend(
            out FakeGuiHost gui,
            new ManagedFallbackBackendOptions(MaxDocuments: 4, MaxControlsPerDocument: 8, MaxVisibleScreens: 4, EventCapacity: 2));
        backend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(path, 3));
        backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(3), document, Modal: false)]);
        _ = gui.Context.ClickedButtons.Add("One");
        _ = gui.Context.ClickedButtons.Add("Two");
        _ = gui.Context.ClickedButtons.Add("Three");

        backend.Composite(default);
        UiEvent sentinel = new(new UiDocumentHandle(-1), new UiElementId(-1), new UiActionId(-1), default);
        UiEvent[] events = [sentinel, sentinel];
        int firstCount = backend.DrainEvents(events.AsSpan(0, 1));
        UiEvent first = events[0];
        int secondCount = backend.DrainEvents(events.AsSpan(0, 1));
        UiEvent second = events[0];
        int emptyCount = backend.DrainEvents(events.AsSpan(0, 1));

        Assert.Equal(1, firstCount);
        Assert.Equal(new UiActionId(UiStableId.Hash("two")), first.Action);
        Assert.Equal(sentinel, events[1]);
        Assert.Equal(1, secondCount);
        Assert.Equal(new UiActionId(UiStableId.Hash("three")), second.Action);
        Assert.Equal(0, emptyCount);
        Assert.Equal(second, events[0]);
    }

    /// <summary>
    /// 验证Ui Input Router Maps Hit Test Into World Input Capture Semantics。
    /// </summary>
    [Fact]
    public void UiInputRouterMapsHitTestIntoWorldInputCaptureSemantics()
    {
        string path = WriteUi("""
            <ui title="Menu">
              <button id="start" data-event-click="start_game">Start</button>
            </ui>
            """);
        using ManagedFallbackBackend backend = CreateManagedBackend(out _);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiDocumentSource source = UiDocumentSource.Asset(path, 4);
        _ = host.ShowScreen(new UiScreenId(4), in source);
        TestInputSource input = new()
        {
            HasPointer = true,
            Pointer = new UiPointerState(16, 20, 0, 0, LeftDown: false, RightDown: false, MiddleDown: false),
        };
        UiInputRouter router = new(host, input);

        UiInputCapture interactive = router.RefreshCapture();
        _ = host.PushModal(new UiScreenId(4), in source);
        UiInputCapture modal = router.RefreshCapture();

        Assert.True(interactive.HitsUi);
        Assert.False(interactive.Opaque);
        Assert.False(interactive.AllowWorldMouse);
        Assert.False(interactive.AllowWorldKeyboard);
        Assert.True(modal.HitsUi);
        Assert.True(modal.Opaque);
        Assert.False(modal.AllowWorldMouse);
        Assert.False(modal.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证Ui Input Router馈送Committed Text And Composition经Separate Backend Channels。
    /// </summary>
    [Fact]
    public void UiInputRouterFeedsCommittedTextAndCompositionThroughSeparateBackendChannels()
    {
        ContractProbeBackend backend = new()
        {
            HitResult = new UiHitResult(HitsUi: true, Opaque: true, WantsMouse: true, WantsKeyboard: true),
        };
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        TestInputSource input = new()
        {
            HasPointer = true,
            Pointer = new UiPointerState(10, 12, 0, 0, LeftDown: false, RightDown: false, MiddleDown: false),
            Text = "中",
            CompositionText = "かな\0\n",
            Composition = new UiTextComposition(isActive: true, cursorIndex: 99, selectionStart: 1, selectionLength: 99),
        };
        UiInputRouter router = new(host, input);

        _ = router.Pump();
        input.Text = string.Empty;
        input.CompositionText = string.Empty;
        input.Composition = UiTextComposition.Inactive;
        _ = router.Pump();

        Assert.Equal("中", backend.CommittedText);
        Assert.Equal(["かな", string.Empty], backend.CompositionTexts);
        Assert.Equal(2, backend.Compositions.Count);
        Assert.True(backend.Compositions[0].IsActive);
        Assert.Equal(2, backend.Compositions[0].CursorIndex);
        Assert.Equal(1, backend.Compositions[0].SelectionStart);
        Assert.Equal(1, backend.Compositions[0].SelectionLength);
        Assert.False(backend.Compositions[1].IsActive);
    }

    /// <summary>
    /// 验证托管回退Model Values And Actions Expose Shared Contract Boundaries。
    /// </summary>
    [Fact]
    public void ManagedFallbackModelValuesAndActionsExposeSharedContractBoundaries()
    {
        string path = WriteUi("""
            <ui title="Settings">
              <progress id="health" path="hud.health" value="0.25" />
              <checkbox id="music" data-event-change="toggle_music" path="settings.music" text="Music" checked="false" />
            </ui>
            """);
        using ManagedFallbackBackend backend = CreateManagedBackend(out _);
        backend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(path, 5));
        backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(5), document, Modal: false)]);
        backend.Composite(default);
        UiPathId health = UiModelPathName.ToPathId("hud.health");
        UiPathId music = UiModelPathName.ToPathId("settings.music");
        UiPathId missing = UiModelPathName.ToPathId("settings.missing");
        UiActionId toggleMusic = new(UiStableId.Hash("toggle_music"));

        Assert.True(backend.TryGetModelValue(document, health, out UiValue initialHealth));
        Assert.Equal(0.25, initialHealth.AsDouble());
        Assert.True(backend.TryGetModelValue(document, music, out UiValue initialMusic));
        Assert.False(initialMusic.AsBoolean());
        Assert.False(backend.TryGetModelValue(document, missing, out UiValue missingValue));
        Assert.Equal(default, missingValue);

        backend.SetModelValue(document, health, new UiValue(0.75));
        Assert.True(backend.IsDirty);
        Assert.True(backend.TryGetModelValue(document, health, out UiValue updatedHealth));
        Assert.Equal(0.75, updatedHealth.AsDouble());
        backend.Composite(default);
        Assert.False(backend.IsDirty);

        Assert.True(backend.InvokeAction(document, toggleMusic, UiValue.FromBoolean(true)));
        Assert.True(backend.IsDirty);
        Assert.True(backend.TryGetModelValue(document, music, out UiValue updatedMusic));
        Assert.True(updatedMusic.AsBoolean());
        Assert.False(backend.InvokeAction(document, new UiActionId(-1), UiValue.FromBoolean(false)));
        Assert.False(backend.InvokeAction(new UiDocumentHandle(999), toggleMusic, UiValue.FromBoolean(false)));
        backend.SetModelValue(new UiDocumentHandle(999), health, new UiValue(0.1));
        Assert.True(backend.TryGetModelValue(document, health, out UiValue unchangedHealth));
        Assert.Equal(0.75, unchangedHealth.AsDouble());
    }

    /// <summary>
    /// 验证托管回退Unload Document And Invalid Viewport Stay Safe At Shared Boundary。
    /// </summary>
    [Fact]
    public void ManagedFallbackUnloadDocumentAndInvalidViewportStaySafeAtSharedBoundary()
    {
        string path = WriteUi("""
            <ui title="Hud">
              <progress id="health" path="hud.health" value="0.5" />
            </ui>
            """);
        using ManagedFallbackBackend backend = CreateManagedBackend(out _);
        backend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(path, 6));
        backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(6), document, Modal: false)]);
        Assert.True(backend.HitTest(8, 8).HitsUi);

        backend.UnloadDocument(document);

        Assert.True(backend.IsDirty);
        Assert.False(backend.HitTest(8, 8).HitsUi);
        Assert.False(backend.TryGetModelValue(document, UiModelPathName.ToPathId("hud.health"), out UiValue value));
        Assert.Equal(default, value);
        UiPathId[] paths = [new UiPathId(-1)];
        Assert.Equal(0, backend.CopyModelPaths(document, paths));
        Assert.Equal(new UiPathId(-1), paths[0]);
        Assert.False(backend.InvokeAction(document, new UiActionId(UiStableId.Hash("noop")), default));
        backend.Composite(default);
        Assert.False(backend.IsDirty);

        using ManagedFallbackBackend invalidBackend = CreateManagedBackend(out _);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => invalidBackend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 0, 240, 1f), UiBackendKind.ManagedFallback)));
        invalidBackend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => invalidBackend.Resize(new UiViewport(0, 0, 320, 240, 0f)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => invalidBackend.Resize(new UiViewport(0, 0, 320, 240, float.NaN)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => invalidBackend.Resize(new UiViewport(0, 0, 320, 240, float.PositiveInfinity)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => invalidBackend.Resize(new UiViewport(0, 0, 320, 240, float.NegativeInfinity)));
    }

    /// <summary>
    /// 验证托管回退Publishes Ime Caret And Candidate Geometry For Active Composition。
    /// </summary>
    [Fact]
    public void ManagedFallbackPublishesImeCaretAndCandidateGeometryForActiveComposition()
    {
        using ManagedFallbackBackend backend = CreateManagedBackend(out _);
        backend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiImeGeometry expected = UiImeGeometryLayout.ComputePreeditOverlayGeometry(
            new UiViewport(0, 0, 320, 240, 1f),
            textLength: 2,
            cursorIndex: 1);

        backend.FeedTextComposition(
            "候補",
            new UiTextComposition(isActive: true, cursorIndex: 1));

        Assert.True(backend.TryGetImeGeometry(out UiImeGeometry geometry));
        Assert.True(geometry.HasCaretRect);
        Assert.True(geometry.HasCandidateAnchor);
        Assert.Equal(expected.CaretX, geometry.CaretX);
        Assert.Equal(expected.CaretY, geometry.CaretY);
        Assert.Equal(expected.CandidateAnchorX, geometry.CandidateAnchorX);
        Assert.Equal(expected.CandidateAnchorY, geometry.CandidateAnchorY);

        backend.FeedTextComposition([], UiTextComposition.Inactive);
        Assert.False(backend.TryGetImeGeometry(out UiImeGeometry inactive));
        Assert.False(inactive.HasAny);
    }

    /// <summary>
    /// 验证托管回退Publishes Selection Exclude Rect Distinct From Caret Point。
    /// </summary>
    [Fact]
    public void ManagedFallbackPublishesSelectionExcludeRectDistinctFromCaretPoint()
    {
        using ManagedFallbackBackend backend = CreateManagedBackend(out _);
        backend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiImeGeometry expected = UiImeGeometryLayout.ComputePreeditOverlayGeometry(
            new UiViewport(0, 0, 320, 240, 1f),
            textLength: 4,
            cursorIndex: 3,
            selectionStart: 1,
            selectionLength: 2);

        backend.FeedTextComposition(
            "かな候補",
            new UiTextComposition(isActive: true, cursorIndex: 3, selectionStart: 1, selectionLength: 2));

        Assert.True(backend.TryGetImeGeometry(out UiImeGeometry geometry));
        Assert.True(geometry.HasCaretRect);
        Assert.True(geometry.HasExcludeRect);
        Assert.Equal(expected.CaretX, geometry.CaretX, precision: 3);
        Assert.Equal(expected.ExcludeX, geometry.ExcludeX, precision: 3);
        Assert.Equal(expected.ExcludeWidth, geometry.ExcludeWidth, precision: 3);
        // 选区排除区应宽于 caret，且起点在 caret 左侧（光标在选区末尾）。
        Assert.True(geometry.ExcludeWidth > geometry.CaretWidth);
        Assert.True(geometry.ExcludeX < geometry.CaretX);
        Assert.True(geometry.TryGetExcludeRect(out float ex, out _, out float ew, out _));
        Assert.Equal(geometry.ExcludeX, ex, precision: 3);
        Assert.Equal(geometry.ExcludeWidth, ew, precision: 3);
    }

    /// <summary>
    /// 验证托管回退Draws Ime Composition Overlay Without Mixing Committed Text。
    /// </summary>
    [Fact]
    public void ManagedFallbackDrawsImeCompositionOverlayWithoutMixingCommittedText()
    {
        string path = WriteUi("""
            <ui title="Input">
              <text id="label">Input</text>
            </ui>
            """);
        using ManagedFallbackBackend backend = CreateManagedBackend(out FakeGuiHost gui);
        backend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(path, 7));
        backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(7), document, Modal: false)]);
        backend.Composite(default);
        gui.Context.Texts.Clear();

        backend.FeedTextComposition(
            "候補",
            new UiTextComposition(isActive: true, cursorIndex: 1));
        Assert.True(backend.IsDirty);
        Assert.Equal("IME 候|補", backend.DebugCompositionOverlayText);
        Assert.True(backend.DebugCompositionOverlayState.IsActive);

        backend.Composite(default);

        Assert.Contains("IME 候|補", gui.Context.Texts);
        Assert.False(backend.IsDirty);

        backend.FeedText("中");
        Assert.False(backend.IsDirty);

        backend.FeedTextComposition([], UiTextComposition.Inactive);
        Assert.True(backend.IsDirty);
        Assert.Equal(string.Empty, backend.DebugCompositionOverlayText);
        Assert.False(backend.DebugCompositionOverlayState.IsActive);
        gui.Context.Texts.Clear();
        backend.Composite(default);

        Assert.DoesNotContain("IME 候|補", gui.Context.Texts);
        Assert.False(backend.IsDirty);
    }

    /// <summary>
    /// 验证托管回退Draws Ime Composition Selection Overlay。
    /// </summary>
    [Fact]
    public void ManagedFallbackDrawsImeCompositionSelectionOverlay()
    {
        string path = WriteUi("""
            <ui title="Input">
              <text id="label">Input</text>
            </ui>
            """);
        using ManagedFallbackBackend backend = CreateManagedBackend(out FakeGuiHost gui);
        backend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.ManagedFallback));
        UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(path, 8));
        backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(8), document, Modal: false)]);
        backend.Composite(default);
        gui.Context.Texts.Clear();

        backend.FeedTextComposition(
            "かな候補",
            new UiTextComposition(isActive: true, cursorIndex: 3, selectionStart: 1, selectionLength: 2));

        Assert.Equal("IME か[な候]|補", backend.DebugCompositionOverlayText);
        Assert.True(backend.DebugCompositionOverlayState.IsActive);
        Assert.Equal(3, backend.DebugCompositionOverlayState.CursorIndex);
        Assert.Equal(1, backend.DebugCompositionOverlayState.SelectionStart);
        Assert.Equal(2, backend.DebugCompositionOverlayState.SelectionLength);

        backend.Composite(default);

        Assert.Contains("IME か[な候]|補", gui.Context.Texts);
        Assert.False(backend.IsDirty);
    }

    /// <summary>
    /// 验证 Ultralight 未激活后端暴露统一诊断，且不会捕获输入、模型、事件或合成输出。
    /// </summary>
    [Fact]
    public void UltralightInactiveBackendRejectsExecutionWithoutCapturingInput()
    {
        using UltralightBackend backend = new();

        Assert.Equal(UiBackendKind.Ultralight, backend.Kind);
        Assert.False(backend.IsAvailable);
        Assert.False(backend.IsDirty);
        Assert.False(backend.IsAnimating);
        Assert.Contains("Ultralight optional profile inactive", backend.ActivationFailureReason, StringComparison.Ordinal);
        Assert.Contains("commercial redistribution license", backend.ActivationFailureReason, StringComparison.Ordinal);
        Assert.Contains("ManagedFallback", backend.ActivationFailureReason, StringComparison.Ordinal);

        NotSupportedException initialize = Assert.Throws<NotSupportedException>(() =>
            backend.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 240, 1f), UiBackendKind.Ultralight)));
        NotSupportedException load = Assert.Throws<NotSupportedException>(() =>
            backend.LoadDocument(UiDocumentSource.Asset("missing.xhtml", 9)));

        Assert.Equal(backend.ActivationFailureReason, initialize.Message);
        Assert.Equal(backend.ActivationFailureReason, load.Message);

        backend.FeedPointerMove(10, 12);
        backend.FeedPointerButton(UiPointerButton.Left, isDown: true);
        backend.FeedScroll(0, -1);
        backend.FeedKey(new UiKey(13), isDown: true, UiKeyModifiers.Control);
        backend.FeedText("text");
        backend.FeedTextComposition("候補", new UiTextComposition(isActive: true, cursorIndex: 1));
        backend.SetScreenStack([]);
        backend.Update(1f / 60f);
        backend.Composite(default);

        Assert.Equal(UiHitResult.None, backend.HitTest(10, 12));
        Assert.False(backend.TryGetImeGeometry(out UiImeGeometry geometry));
        Assert.False(geometry.HasAny);
        Assert.False(backend.TryGetModelValue(new UiDocumentHandle(1), UiModelPathName.ToPathId("hud.health"), out UiValue value));
        Assert.Equal(default, value);
        UiPathId[] paths = [new UiPathId(-1)];
        Assert.Equal(0, backend.CopyModelPaths(new UiDocumentHandle(1), paths));
        Assert.Equal(new UiPathId(-1), paths[0]);
        Assert.False(backend.InvokeAction(new UiDocumentHandle(1), new UiActionId(1), UiValue.FromBoolean(true)));
        UiEvent[] events = [new(new UiDocumentHandle(-1), new UiElementId(-1), new UiActionId(-1), default)];
        Assert.Equal(0, backend.DrainEvents(events));
        Assert.Equal(new UiDocumentHandle(-1), events[0].Document);
    }

    /// <summary>
    /// 验证Rml Ui Visible Screen Pruning Removes Unloaded Modal And保持Stack Order。
    /// </summary>
    [Fact]
    public void RmlUiVisibleScreenPruningRemovesUnloadedModalAndPreservesStackOrder()
    {
        UiDocumentHandle background = new(10);
        UiDocumentHandle modal = new(20);
        UiDocumentHandle overlay = new(30);
        UiScreenStackEntry[] stack =
        [
            new(new UiScreenHandle(1), new UiScreenId(1), background, Modal: false),
            new(new UiScreenHandle(2), new UiScreenId(2), modal, Modal: true),
            new(new UiScreenHandle(3), new UiScreenId(3), overlay, Modal: false),
            new(new UiScreenHandle(4), new UiScreenId(2), modal, Modal: true),
        ];

        int count = RmlUiBackend.PruneVisibleScreensForDocument(stack, stack.Length, modal);

        Assert.Equal(2, count);
        Assert.Equal(background, stack[0].Document);
        Assert.Equal(overlay, stack[1].Document);
        Assert.Equal(default, stack[2]);
        Assert.Equal(default, stack[3]);
    }

    private static ManagedFallbackBackend CreateManagedBackend(
        out FakeGuiHost gui,
        ManagedFallbackBackendOptions options = default)
    {
        gui = new FakeGuiHost();
        return new ManagedFallbackBackend(gui, options);
    }

    private string WriteUi(string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"pixelengine-backend-contract-{Guid.NewGuid():N}.xhtml");
        File.WriteAllText(path, contents);
        _temporaryUiFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string path in _temporaryUiFiles)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FakeGuiHost : IManagedFallbackGuiHost
    {
        public FakeGuiDrawContext Context { get; } = new();

        public bool IsRunning { get; private set; }

        public void Initialize()
        {
            IsRunning = true;
        }

        public void DrawFrame(float deltaSeconds, int width, int height, Action<IGuiDrawContext> drawGui)
        {
            drawGui(Context);
        }

        public ManagedFallbackImage LoadImage(string path)
        {
            _ = path;
            return new ManagedFallbackImage(1, 1, 1);
        }
    }

    private sealed class FakeGuiDrawContext : IGuiDrawContext
    {
        public List<string> Texts { get; } = [];

        public HashSet<string> ClickedButtons { get; } = [];

        public int Width => 320;

        public int Height => 240;

        public float DeltaTime => 1f / 60f;

        public bool WantsMouse => false;

        public bool WantsKeyboard => false;

        public void SetNextWindow(float x, float y, float width, float height, GuiDrawCondition condition = GuiDrawCondition.Always)
        {
            _ = x;
            _ = y;
            _ = width;
            _ = height;
            _ = condition;
        }

        public bool BeginWindow(string id, string title, GuiDrawWindowFlags flags = GuiDrawWindowFlags.None)
        {
            _ = id;
            _ = title;
            _ = flags;
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
            _ = colorBgra;
        }

        public void SameLine()
        {
        }

        public void Separator()
        {
        }

        public bool Button(string label)
        {
            return ClickedButtons.Remove(label);
        }

        public bool Checkbox(string label, ref bool value)
        {
            _ = label;
            _ = value;
            return false;
        }

        public void ProgressBar(float value01, string? label = null)
        {
            _ = value01;
            _ = label;
        }

        public void ColorSwatch(string id, uint colorBgra, float size = 16f)
        {
            _ = id;
            _ = colorBgra;
            _ = size;
        }

        public void Image(string id, uint textureHandle, int textureWidth, int textureHeight, float width, float height, uint tintBgra = 0xFF_FF_FF_FF)
        {
            _ = id;
            _ = textureHandle;
            _ = textureWidth;
            _ = textureHeight;
            _ = width;
            _ = height;
            _ = tintBgra;
        }
    }

    private sealed class TestInputSource : IUiInputSource
    {
        public bool HasPointer { get; set; }

        public UiPointerState Pointer { get; set; }

        public UiKey[] DownKeys { get; set; } = [];

        public UiKeyModifiers Modifiers { get; set; }

        public string Text { get; set; } = string.Empty;

        public string CompositionText { get; set; } = string.Empty;

        public UiTextComposition Composition { get; set; }

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

        public int CaptureTextComposition(Span<char> destination, out UiTextComposition composition)
        {
            int count = Math.Min(destination.Length, CompositionText.Length);
            CompositionText.AsSpan(0, count).CopyTo(destination);
            composition = Composition;
            return count;
        }
    }

    private sealed class ContractProbeBackend : IGameUiBackend
    {
        public UiHitResult HitResult { get; init; } = UiHitResult.None;

        public string CommittedText { get; private set; } = string.Empty;

        public List<string> CompositionTexts { get; } = [];

        public List<UiTextComposition> Compositions { get; } = [];

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
            _ = document;
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
            _ = x;
            _ = y;
        }

        public void FeedPointerButton(UiPointerButton button, bool isDown)
        {
            _ = button;
            _ = isDown;
        }

        public void FeedScroll(float deltaX, float deltaY)
        {
            _ = deltaX;
            _ = deltaY;
        }

        public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
        {
            _ = key;
            _ = isDown;
            _ = modifiers;
        }

        public void FeedText(ReadOnlySpan<char> text)
        {
            CommittedText += text.ToString();
        }

        public void FeedTextComposition(ReadOnlySpan<char> text, in UiTextComposition composition)
        {
            CompositionTexts.Add(text.ToString());
            Compositions.Add(composition);
        }

        public UiHitResult HitTest(float x, float y)
        {
            _ = x;
            _ = y;
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
