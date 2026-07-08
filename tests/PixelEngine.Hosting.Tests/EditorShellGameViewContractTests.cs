using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using PixelEngine.Rendering;
using PixelEngine.UI;
using System.Numerics;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// EditorShell Game View 产品契约测试。
/// </summary>
public sealed class EditorShellGameViewContractTests
{
    /// <summary>
    /// 验证默认布局把 Scene View 与 Game View 作为两个独立产品面注册。
    /// </summary>
    [Fact]
    public void DockSpaceDeclaresSceneViewAndGameViewSeparately()
    {
        string[] titles = EditorDockSpace.GetDefaultWindowTitles().ToArray();

        Assert.Contains(EditorDockSpace.ViewportWindowTitle, titles);
        Assert.Contains(EditorDockSpace.GameViewWindowTitle, titles);
        Assert.NotEqual(EditorDockSpace.ViewportWindowTitle, EditorDockSpace.GameViewWindowTitle);
    }

    /// <summary>
    /// 验证 Scene View 始终使用 authoring 相机与工具输入，不把它冒充玩家视角。
    /// </summary>
    [Fact]
    public void SceneViewKeepsAuthoringCameraAndToolInput()
    {
        EditorViewportContract contract = EditorGameViewContract.SceneView(PixelEngine.Editor.EditorMode.Play);

        Assert.Equal(EditorViewportSurface.SceneView, contract.Surface);
        Assert.Equal(EditorDockSpace.ViewportWindowTitle, contract.WindowTitle);
        Assert.Equal(EditorViewportCameraOwner.AuthoringCamera, contract.CameraOwner);
        Assert.Equal(EditorViewportInputOwner.AuthoringTools, contract.InputOwner);
        Assert.True(contract.UsesRuntimeViewportTexture);
        Assert.True(contract.AllowsEditorOverlay);
        Assert.Equal(UiPresentLayerOrders.Game, contract.GameUiLayerOrder);
        Assert.Equal(UiPresentLayerOrders.Editor, contract.EditorOverlayLayerOrder);
        Assert.Equal(EditorViewportInputClip.ImageRect, contract.InputClip);
        Assert.Equal(EditorViewportOutputClip.ImageRect, contract.OutputClip);
        Assert.Equal(EditorViewportCoordinateSpace.ViewportTexturePixels, contract.GameUiCoordinateSpace);
        Assert.Equal(EditorViewportCoordinateSpace.FramebufferPixels, contract.GameUiOutputCoordinateSpace);
        Assert.Equal(EditorViewportHitTestSource.PanelLocalImageRectMappedToViewport, contract.GameUiHitTestSource);
    }

    /// <summary>
    /// 验证 Game View 在 Play 模式消费 runtime viewport texture，且 editor overlay 层级仍高于 game UI。
    /// </summary>
    [Fact]
    public void GameViewPlayModeUsesRuntimeViewportTextureAndEditorOverlayOrder()
    {
        EditorViewportContract contract = EditorGameViewContract.GameView(PixelEngine.Editor.EditorMode.Play);

        Assert.Equal(EditorViewportSurface.GameView, contract.Surface);
        Assert.Equal(EditorDockSpace.GameViewWindowTitle, contract.WindowTitle);
        Assert.Equal(EditorViewportCameraOwner.RuntimePipelineCamera, contract.CameraOwner);
        Assert.Equal(EditorViewportInputOwner.GameUiThenGameplay, contract.InputOwner);
        Assert.True(contract.UsesRuntimeViewportTexture);
        Assert.True(contract.AllowsEditorOverlay);
        Assert.Equal(UiPresentLayerOrders.Game, contract.GameUiLayerOrder);
        Assert.Equal(UiPresentLayerOrders.Editor, contract.EditorOverlayLayerOrder);
        Assert.True(contract.EditorOverlayLayerOrder > contract.GameUiLayerOrder);
        Assert.True(contract.EditorOverlayHasPriority);
        Assert.Equal(EditorViewportInputClip.ImageRect, contract.InputClip);
        Assert.Equal(EditorViewportOutputClip.ImageRect, contract.OutputClip);
        Assert.Equal(EditorViewportCoordinateSpace.ViewportTexturePixels, contract.GameUiCoordinateSpace);
        Assert.Equal(EditorViewportCoordinateSpace.FramebufferPixels, contract.GameUiOutputCoordinateSpace);
        Assert.Equal(EditorViewportHitTestSource.PanelLocalImageRectMappedToViewport, contract.GameUiHitTestSource);
    }

    /// <summary>
    /// 验证 Play 模式 Game View 在 editor 未捕获输入时让位给 Web-first UI 与 gameplay。
    /// </summary>
    [Fact]
    public void GameViewPlayModeLetsGameUiAndGameplayCaptureWhenEditorDoesNotCapture()
    {
        EditorViewportContract contract = EditorGameViewContract.GameView(PixelEngine.Editor.EditorMode.Play);
        EditorInputSnapshot editorCapture = new(WantCaptureMouse: false, WantCaptureKeyboard: false);

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            viewportHasInputFocus: true);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, capture);

        Assert.Equal(EditorHostInputCapture.None, capture);
        Assert.True(state.AllowWorldMouse);
        Assert.True(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证 Play 模式 Game View 失去输入焦点时不会向 gameplay 透传输入。
    /// </summary>
    [Fact]
    public void GameViewWithoutInputFocusBlocksGameplayInput()
    {
        EditorViewportContract contract = EditorGameViewContract.GameView(PixelEngine.Editor.EditorMode.Play);
        EditorInputSnapshot editorCapture = new(WantCaptureMouse: false, WantCaptureKeyboard: false);

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            viewportHasInputFocus: false);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, capture);

        Assert.True(capture.WantCaptureMouse);
        Assert.True(capture.WantCaptureKeyboard);
        Assert.False(state.AllowWorldMouse);
        Assert.False(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证 Scene View 获得输入焦点时仍阻止 gameplay 消费 authoring 工具输入。
    /// </summary>
    [Fact]
    public void SceneViewFocusedAuthoringToolsBlockGameplayInput()
    {
        EditorViewportContract contract = EditorGameViewContract.SceneView(PixelEngine.Editor.EditorMode.Play);
        EditorInputSnapshot editorCapture = new(WantCaptureMouse: false, WantCaptureKeyboard: false);

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            viewportHasInputFocus: true);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, capture);

        Assert.True(capture.WantCaptureMouse);
        Assert.True(capture.WantCaptureKeyboard);
        Assert.False(state.AllowWorldMouse);
        Assert.False(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证 Game View 仍尊重菜单、dock、modal 等 editor overlay 的输入捕获。
    /// </summary>
    [Fact]
    public void GameViewStillBlocksGameplayWhenEditorOverlayCapturesInput()
    {
        EditorViewportContract contract = EditorGameViewContract.GameView(PixelEngine.Editor.EditorMode.Play);
        EditorInputSnapshot editorCapture = new(WantCaptureMouse: true, WantCaptureKeyboard: false);

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            viewportHasInputFocus: true);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, capture);

        Assert.True(capture.WantCaptureMouse);
        Assert.False(state.AllowWorldMouse);
        Assert.True(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证 Game View 记录图像矩形、fit scale 与 panel-local 到 viewport 坐标映射。
    /// </summary>
    [Fact]
    public void GameViewViewportSnapshotMapsPanelLocalImageRectToViewportPixels()
    {
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));

        Assert.True(snapshot.IsValid);
        Assert.Equal(new GameViewRect(10f, 20f, 160f, 90f), snapshot.ImageRect);
        Assert.Equal(new GameViewRect(0f, 0f, 320f, 180f), snapshot.VisibleViewportRect);
        Assert.Equal(0.5f, snapshot.FitScale, precision: 3);
        Assert.True(snapshot.TryMapPanelToViewport(new Vector2(90f, 65f), out Vector2 viewportPoint));
        Assert.Equal(160f, viewportPoint.X, precision: 3);
        Assert.Equal(90f, viewportPoint.Y, precision: 3);
        Assert.False(snapshot.TryMapPanelToViewport(new Vector2(90f, 130f), out _));
        Assert.False(snapshot.TryMapPanelToViewport(new Vector2(170f, 65f), out _));
        Assert.False(snapshot.TryMapPanelToViewport(new Vector2(90f, 110f), out _));
    }

    /// <summary>
    /// 验证 Game View 面板空白区会在 Editor 层阻断输入，避免 panel 外点击进入 UI 或 gameplay。
    /// </summary>
    [Fact]
    public void GameViewPanelOutsideImageRectBlocksInputBeforeGameUi()
    {
        EditorViewportContract contract = EditorGameViewContract.GameView(PixelEngine.Editor.EditorMode.Play);
        EditorInputSnapshot editorCapture = new(WantCaptureMouse: false, WantCaptureKeyboard: false);
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            snapshot,
            panelPoint: new Vector2(90f, 130f));
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, capture);
        state = InputArbitrator.ApplyGameUi(
            state,
            new UiInputCapture(HitsUi: true, Opaque: false, WantCaptureMouse: true, WantCaptureKeyboard: true));

        Assert.True(capture.WantCaptureMouse);
        Assert.True(capture.WantCaptureKeyboard);
        Assert.False(state.AllowWorldMouse);
        Assert.False(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证 Game View 图像内透明 UI 区域可 pass-through 到 gameplay。
    /// </summary>
    [Fact]
    public void GameViewImageTransparentUiPassesThroughToGameplay()
    {
        EditorViewportContract contract = EditorGameViewContract.GameView(PixelEngine.Editor.EditorMode.Play);
        EditorInputSnapshot editorCapture = new(WantCaptureMouse: false, WantCaptureKeyboard: false);
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            snapshot,
            panelPoint: new Vector2(90f, 65f));
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, capture);
        state = InputArbitrator.ApplyGameUi(state, UiInputCapture.None);

        Assert.Equal(EditorHostInputCapture.None, capture);
        Assert.True(state.AllowWorldMouse);
        Assert.True(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证 Game View 图像内交互 UI 区域可在 UI 层截断 gameplay 输入。
    /// </summary>
    [Fact]
    public void GameViewImageInteractiveUiCapturesBeforeGameplay()
    {
        EditorViewportContract contract = EditorGameViewContract.GameView(PixelEngine.Editor.EditorMode.Play);
        EditorInputSnapshot editorCapture = new(WantCaptureMouse: false, WantCaptureKeyboard: false);
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            snapshot,
            panelPoint: new Vector2(90f, 65f));
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, capture);
        state = InputArbitrator.ApplyGameUi(
            state,
            new UiInputCapture(HitsUi: true, Opaque: false, WantCaptureMouse: true, WantCaptureKeyboard: true));

        Assert.Equal(EditorHostInputCapture.None, capture);
        Assert.False(state.AllowWorldMouse);
        Assert.False(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证 Game View 输入源把 panel-local 图像点映射成 UI hit-test 使用的 viewport 坐标。
    /// </summary>
    [Fact]
    public void GameViewUiInputSourceFeedsMappedViewportCoordinatesIntoUiRouterHitTest()
    {
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));
        Vector2 panelPoint = new(90f, 65f);
        FixedUiInputSource input = new(new UiPointerState(999f, 888f, 1f, -2f, LeftDown: true, RightDown: false, MiddleDown: false));
        RecordingBackend backend = new((x, y) => x == 160f && y == 90f
            ? new UiHitResult(HitsUi: true, Opaque: false, WantsMouse: true, WantsKeyboard: true)
            : UiHitResult.None);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 180, 1f), UiBackendKind.ManagedFallback));
        UiInputRouter router = new(
            host,
            new GameViewUiInputSource(
                input,
                () => PixelEngine.Editor.EditorMode.Play,
                () => snapshot,
                () => panelPoint,
                () => true));

        EditorHostInputCapture editorCapture = EditorGameViewContract.ResolveEditorInputCapture(
            EditorGameViewContract.GameView(PixelEngine.Editor.EditorMode.Play),
            new EditorInputSnapshot(WantCaptureMouse: false, WantCaptureKeyboard: false),
            snapshot,
            panelPoint);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, editorCapture);
        UiInputCapture uiCapture = router.Pump(allowPointer: state.AllowWorldMouse, allowKeyboard: state.AllowWorldKeyboard);
        state = InputArbitrator.ApplyGameUi(state, uiCapture);

        Assert.Equal(EditorHostInputCapture.None, editorCapture);
        Assert.Equal(160f, backend.LastPointerMoveX, precision: 3);
        Assert.Equal(90f, backend.LastPointerMoveY, precision: 3);
        Assert.Equal(160f, backend.LastHitTestX, precision: 3);
        Assert.Equal(90f, backend.LastHitTestY, precision: 3);
        Assert.True(uiCapture.WantCaptureMouse);
        Assert.True(uiCapture.WantCaptureKeyboard);
        Assert.False(state.AllowWorldMouse);
        Assert.False(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证 Game View 坐标闭环仍保留透明 UI 区域 pass-through 语义。
    /// </summary>
    [Fact]
    public void GameViewUiInputSourceMappedTransparentHitPassesThroughToGameplay()
    {
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));
        Vector2 panelPoint = new(90f, 65f);
        FixedUiInputSource input = new(new UiPointerState(0f, 0f, 0f, 0f, LeftDown: false, RightDown: false, MiddleDown: false));
        RecordingBackend backend = new((_, _) => UiHitResult.None);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 180, 1f), UiBackendKind.ManagedFallback));
        UiInputRouter router = new(
            host,
            new GameViewUiInputSource(
                input,
                () => PixelEngine.Editor.EditorMode.Play,
                () => snapshot,
                () => panelPoint,
                () => true));

        EditorHostInputCapture editorCapture = EditorGameViewContract.ResolveEditorInputCapture(
            EditorGameViewContract.GameView(PixelEngine.Editor.EditorMode.Play),
            new EditorInputSnapshot(WantCaptureMouse: false, WantCaptureKeyboard: false),
            snapshot,
            panelPoint);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, editorCapture);
        UiInputCapture uiCapture = router.Pump(allowPointer: state.AllowWorldMouse, allowKeyboard: state.AllowWorldKeyboard);
        state = InputArbitrator.ApplyGameUi(state, uiCapture);

        Assert.Equal(EditorHostInputCapture.None, editorCapture);
        Assert.Equal(160f, backend.LastHitTestX, precision: 3);
        Assert.Equal(90f, backend.LastHitTestY, precision: 3);
        Assert.Equal(UiInputCapture.None, uiCapture);
        Assert.True(state.AllowWorldMouse);
        Assert.True(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证 Game View 输入源只在 Play、聚焦且指针位于图像区域内时转发键盘与文本输入。
    /// </summary>
    [Fact]
    public void GameViewUiInputSourceBlocksKeyboardTextAndCompositionOutsideFocusedPlayImage()
    {
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));

        AssertKeyboardBlocked(PixelEngine.Editor.EditorMode.Edit, focused: true, panelPoint: new Vector2(90f, 65f));
        AssertKeyboardBlocked(PixelEngine.Editor.EditorMode.Play, focused: false, panelPoint: new Vector2(90f, 65f));
        AssertKeyboardBlocked(PixelEngine.Editor.EditorMode.Play, focused: true, panelPoint: new Vector2(90f, 130f));

        void AssertKeyboardBlocked(PixelEngine.Editor.EditorMode mode, bool focused, Vector2 panelPoint)
        {
            FixedUiInputSource input = new(new UiPointerState(1f, 2f, 0f, 0f, LeftDown: false, RightDown: false, MiddleDown: false))
            {
                DownKeys = [new UiKey(65)],
                Modifiers = UiKeyModifiers.Control,
                Text = "go",
                CompositionText = "あい",
                Composition = new UiTextComposition(isActive: true, cursorIndex: 1),
            };
            GameViewUiInputSource source = new(
                input,
                () => mode,
                () => snapshot,
                () => panelPoint,
                () => focused);
            Span<UiKey> keys = stackalloc UiKey[4];
            Span<char> text = stackalloc char[4];
            text.Fill('x');

            Assert.False(source.TryGetPointer(out _));
            Assert.Equal(0, source.CaptureDownKeys(keys, out UiKeyModifiers modifiers));
            Assert.Equal(UiKeyModifiers.None, modifiers);
            Assert.Equal(0, input.CaptureDownKeysCalls);
            Assert.Equal(0, source.CaptureText(text));
            Assert.True(text.ToArray().All(c => c == '\0'));
            Assert.Equal(1, input.CaptureTextCalls);
            Assert.Equal(0, source.CaptureTextComposition(text, out UiTextComposition composition));
            Assert.False(composition.IsActive);
            Assert.True(text.ToArray().All(c => c == '\0'));
            Assert.Equal(1, input.CaptureTextCompositionCalls);
        }
    }

    /// <summary>
    /// 验证 Game View 输入源在合法 Game View 图像内转发键盘、committed text 与 composition 预编辑。
    /// </summary>
    [Fact]
    public void GameViewUiInputSourceForwardsKeyboardTextAndCompositionInsideFocusedPlayImage()
    {
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));
        FixedUiInputSource input = new(new UiPointerState(1f, 2f, 0f, 0f, LeftDown: false, RightDown: false, MiddleDown: false))
        {
            DownKeys = [new UiKey(65)],
            Modifiers = UiKeyModifiers.Control,
            Text = "go",
            CompositionText = "あい",
            Composition = new UiTextComposition(isActive: true, cursorIndex: 1, selectionStart: 0, selectionLength: 1),
        };
        GameViewUiInputSource source = new(
            input,
            () => PixelEngine.Editor.EditorMode.Play,
            () => snapshot,
            () => new Vector2(90f, 65f),
            () => true);
        Span<UiKey> keys = stackalloc UiKey[4];
        Span<char> text = stackalloc char[4];

        Assert.True(source.TryGetPointer(out UiPointerState pointer));
        Assert.Equal(160f, pointer.X, precision: 3);
        Assert.Equal(90f, pointer.Y, precision: 3);
        Assert.Equal(1, source.CaptureDownKeys(keys, out UiKeyModifiers modifiers));
        Assert.Equal(new UiKey(65), keys[0]);
        Assert.Equal(UiKeyModifiers.Control, modifiers);
        Assert.Equal(2, source.CaptureText(text));
        Assert.Equal("go", new string(text[..2]));
        Assert.Equal(2, source.CaptureTextComposition(text, out UiTextComposition composition));
        Assert.Equal("あい", new string(text[..2]));
        Assert.True(composition.IsActive);
        Assert.Equal(1, composition.CursorIndex);
        Assert.Equal(1, composition.SelectionLength);
    }

    /// <summary>
    /// 验证 GameViewPanel 只声明运行时视图契约，不复用 Scene View 的 gizmo / 画刷职责。
    /// </summary>
    [Fact]
    public void GameViewPanelExposesRuntimeContractWithoutAuthoringTools()
    {
        GameViewPanel panel = new(() => new RenderViewportTexture(12, 320, 180));

        EditorViewportContract contract = panel.CaptureContract(PixelEngine.Editor.EditorMode.Play);

        Assert.Equal(EditorDockSpace.GameViewWindowTitle, panel.Title);
        Assert.Equal(EditorViewportSurface.GameView, contract.Surface);
        Assert.Equal(EditorViewportCameraOwner.RuntimePipelineCamera, contract.CameraOwner);
        Assert.Equal(EditorViewportInputOwner.GameUiThenGameplay, contract.InputOwner);
        Assert.True(contract.UsesRuntimeViewportTexture);
        Assert.Equal(EditorViewportInputClip.ImageRect, contract.InputClip);
        Assert.Equal(EditorViewportCoordinateSpace.ViewportTexturePixels, contract.GameUiCoordinateSpace);
        Assert.Equal(EditorViewportOutputClip.ImageRect, contract.OutputClip);
        Assert.Equal(EditorViewportCoordinateSpace.FramebufferPixels, contract.GameUiOutputCoordinateSpace);
    }

    /// <summary>
    /// 验证 Game View 输出侧把 panel-local image rect 转成 framebuffer-space UI present target。
    /// </summary>
    [Fact]
    public void GameViewViewportSnapshotCreatesFramebufferUiPresentTargetFromImageRect()
    {
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10.25f, 20.5f),
            availablePanelSize: new Vector2(160f, 160f));

        Assert.True(snapshot.TryCreateUiPresentTarget(new Vector2(100.5f, 40.25f), out UiPresentTarget target));

        Assert.Equal(110, target.X);
        Assert.Equal(60, target.Y);
        Assert.Equal(161, target.Width);
        Assert.Equal(91, target.Height);
        Assert.Equal(target.Scissor, new UiScissorRect(110, 60, 161, 91));
        Assert.True(target.IsValid);
    }

    /// <summary>
    /// 验证 Game View UI present target provider 只在 Play 模式、面板可见且 snapshot 有效时输出 framebuffer-space 目标。
    /// </summary>
    [Fact]
    public void GameViewUiPresentTargetProviderUsesVisiblePlayModeFramebufferImageRect()
    {
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));
        GameViewUiPresentTargetProvider provider = new(
            () => PixelEngine.Editor.EditorMode.Play,
            () => snapshot,
            () => new Vector2(100f, 40f),
            () => true);

        Assert.True(provider.TryGetPresentTarget(out UiPresentTarget target));
        Assert.Equal(new UiPresentTarget(110, 60, 160, 90, 1f), target);

        provider = new GameViewUiPresentTargetProvider(
            () => PixelEngine.Editor.EditorMode.Play,
            () => snapshot,
            () => new Vector2(100f, 40f),
            () => false);

        Assert.False(provider.TryGetPresentTarget(out _));

        provider = new GameViewUiPresentTargetProvider(
            () => PixelEngine.Editor.EditorMode.Edit,
            () => snapshot,
            () => new Vector2(100f, 40f),
            () => true);

        Assert.False(provider.TryGetPresentTarget(out _));
    }

    private sealed class FixedUiInputSource(UiPointerState pointer) : IUiInputSource
    {
        public UiKey[] DownKeys { get; init; } = [];

        public UiKeyModifiers Modifiers { get; init; }

        public string Text { get; set; } = string.Empty;

        public string CompositionText { get; set; } = string.Empty;

        public UiTextComposition Composition { get; init; }

        public int CaptureDownKeysCalls { get; private set; }

        public int CaptureTextCalls { get; private set; }

        public int CaptureTextCompositionCalls { get; private set; }

        public bool TryGetPointer(out UiPointerState state)
        {
            state = pointer;
            return true;
        }

        public int CaptureDownKeys(Span<UiKey> destination, out UiKeyModifiers modifiers)
        {
            CaptureDownKeysCalls++;
            modifiers = Modifiers;
            int count = Math.Min(destination.Length, DownKeys.Length);
            DownKeys.AsSpan(0, count).CopyTo(destination);
            return count;
        }

        public int CaptureText(Span<char> destination)
        {
            CaptureTextCalls++;
            int count = Math.Min(destination.Length, Text.Length);
            Text.AsSpan(0, count).CopyTo(destination);
            Text = string.Empty;
            return count;
        }

        public int CaptureTextComposition(Span<char> destination, out UiTextComposition composition)
        {
            CaptureTextCompositionCalls++;
            int count = Math.Min(destination.Length, CompositionText.Length);
            CompositionText.AsSpan(0, count).CopyTo(destination);
            composition = Composition;
            return count;
        }
    }

    private sealed class RecordingBackend(Func<float, float, UiHitResult> hitTest) : IGameUiBackend
    {
        private readonly Func<float, float, UiHitResult> _hitTest = hitTest ?? throw new ArgumentNullException(nameof(hitTest));

        public UiBackendKind Kind => UiBackendKind.ManagedFallback;

        public bool IsDirty => false;

        public bool IsAnimating => false;

        public float LastPointerMoveX { get; private set; } = float.NaN;

        public float LastPointerMoveY { get; private set; } = float.NaN;

        public float LastHitTestX { get; private set; } = float.NaN;

        public float LastHitTestY { get; private set; } = float.NaN;

        public void Dispose()
        {
        }

        public void Initialize(in UiBackendInitializeInfo info)
        {
            _ = info;
        }

        public void Resize(in UiViewport viewport)
        {
            _ = viewport;
        }

        public UiDocumentHandle LoadDocument(in UiDocumentSource source)
        {
            _ = source;
            return default;
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
            LastPointerMoveX = x;
            LastPointerMoveY = y;
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
            _ = text;
        }

        public UiHitResult HitTest(float x, float y)
        {
            LastHitTestX = x;
            LastHitTestY = y;
            return _hitTest(x, y);
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
    }
}
