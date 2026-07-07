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
        Assert.Equal(EditorViewportCoordinateSpace.ViewportTexturePixels, contract.GameUiCoordinateSpace);
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
        Assert.Equal(EditorViewportCoordinateSpace.ViewportTexturePixels, contract.GameUiCoordinateSpace);
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
    }

    private sealed class FixedUiInputSource(UiPointerState pointer) : IUiInputSource
    {
        public bool TryGetPointer(out UiPointerState state)
        {
            state = pointer;
            return true;
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
