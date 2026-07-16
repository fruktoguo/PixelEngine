using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using PixelEngine.Gui;
using PixelEngine.Rendering;
using PixelEngine.UI;
using System.Numerics;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// EditorShell Game View 产品契约测试。
/// 不变式：Game View 输入路由与运行态一致、视口缩放不破坏像素对齐。
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
    /// 验证默认产品 UI 只恢复主菜单，拒绝把旧的菜单+HUD 叠加数量继续当成 Play 重入成功。
    /// </summary>
    [Theory]
    [InlineData(1, 0, 1, true)]
    [InlineData(2, 0, 2, false)]
    [InlineData(1, 1, 1, false)]
    [InlineData(1, 0, 0, false)]
    [InlineData(1, 0, 2, false)]
    public void ScriptedProbeRequiresSingleDefaultScreenAcrossPlayReentry(
        int firstUiStackDepth,
        int exitUiStackDepth,
        int secondUiStackDepth,
        bool expected)
    {
        bool actual = ScriptedGameViewProbeState.IsDefaultUiStackLifecycleRestored(
            firstUiStackDepth,
            exitUiStackDepth,
            secondUiStackDepth);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 验证 Scene View 始终使用 authoring 相机与工具输入，不把它冒充玩家视角。
    /// </summary>
    [Fact]
    public void SceneViewKeepsAuthoringCameraAndToolInput()
    {
        // Arrange：准备输入与初始状态
        EditorViewportContract contract = EditorGameViewContract.SceneView(Editor.EditorMode.Play);

        // Assert：验证预期结果
        Assert.Equal(EditorViewportSurface.SceneView, contract.Surface);
        Assert.Equal(EditorDockSpace.ViewportWindowTitle, contract.WindowTitle);
        Assert.Equal(EditorViewportCameraOwner.AuthoringCamera, contract.CameraOwner);
        Assert.Equal(EditorViewportInputOwner.AuthoringTools, contract.InputOwner);
        Assert.False(contract.UsesRuntimeViewportTexture);
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
        // Arrange：准备输入与初始状态
        EditorViewportContract contract = EditorGameViewContract.GameView(Editor.EditorMode.Play);

        // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
        EditorViewportContract contract = EditorGameViewContract.GameView(Editor.EditorMode.Play);
        EditorInputSnapshot editorCapture = new(WantCaptureMouse: false, WantCaptureKeyboard: false);

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            viewportHasInputFocus: true);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, capture);

        // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
        EditorViewportContract contract = EditorGameViewContract.GameView(Editor.EditorMode.Play);
        EditorInputSnapshot editorCapture = new(WantCaptureMouse: false, WantCaptureKeyboard: false);

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            viewportHasInputFocus: false);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, capture);

        // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
        EditorViewportContract contract = EditorGameViewContract.SceneView(Editor.EditorMode.Play);
        EditorInputSnapshot editorCapture = new(WantCaptureMouse: false, WantCaptureKeyboard: false);

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            viewportHasInputFocus: true);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, capture);

        // Assert：验证预期结果
        Assert.True(capture.WantCaptureMouse);
        Assert.True(capture.WantCaptureKeyboard);
        Assert.False(state.AllowWorldMouse);
        Assert.False(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证 Game View 画布自身产生的 ImGui capture 不会反过来吞掉 gameplay 输入。
    /// </summary>
    [Fact]
    public void GameViewCanvasSelfCaptureDoesNotBlockGameplayWhenItOwnsPointerAndKeyboard()
    {
        // Arrange：准备输入与初始状态
        EditorViewportContract contract = EditorGameViewContract.GameView(Editor.EditorMode.Play);
        EditorInputSnapshot editorCapture = new(WantCaptureMouse: true, WantCaptureKeyboard: true);

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            editorCapture,
            pointerHasInputFocus: true,
            keyboardHasInputFocus: true);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, capture);

        // Assert：验证预期结果
        Assert.Equal(EditorHostInputCapture.None, capture);
        Assert.True(state.AllowWorldMouse);
        Assert.True(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证键盘焦点与图像 hover 独立；鼠标离开 Game View 后 WASD 仍归已聚焦的 Game View。
    /// </summary>
    [Fact]
    public void GameViewKeyboardFocusPersistsIndependentlyFromPointerHover()
    {
        EditorViewportContract contract = EditorGameViewContract.GameView(Editor.EditorMode.Play);

        EditorHostInputCapture capture = EditorGameViewContract.ResolveEditorInputCapture(
            contract,
            new EditorInputSnapshot(WantCaptureMouse: true, WantCaptureKeyboard: true),
            pointerHasInputFocus: false,
            keyboardHasInputFocus: true);

        Assert.True(capture.WantCaptureMouse);
        Assert.False(capture.WantCaptureKeyboard);
    }

    /// <summary>
    /// 验证 Game View 记录图像矩形、fit scale 与 panel-local 到 viewport 坐标映射。
    /// </summary>
    [Fact]
    public void GameViewViewportSnapshotMapsViewportPixelsBackToPanelLocal()
    {
        // 验证 Game View viewport 纹理坐标可映射回面板局部坐标，供 IME 几何回写。
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 200,
            textureHeight: 100,
            imageMinPanel: new Vector2(80f, 40f),
            availablePanelSize: new Vector2(100f, 50f));

        Assert.True(snapshot.TryMapViewportToPanel(new Vector2(100f, 50f), out Vector2 panelPoint));
        Assert.Equal(130f, panelPoint.X, 3);
        Assert.Equal(65f, panelPoint.Y, 3);
    }

    /// <summary>
    /// 验证 GameView viewport 快照能把面板局部图像区域坐标映射回 viewport 像素。
    /// </summary>
    [Fact]
    public void GameViewViewportSnapshotMapsPanelLocalImageRectToViewportPixels()
    {
        // Arrange：准备输入与初始状态
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));

        // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
        EditorViewportContract contract = EditorGameViewContract.GameView(Editor.EditorMode.Play);
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

        // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
        EditorViewportContract contract = EditorGameViewContract.GameView(Editor.EditorMode.Play);
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

        // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
        EditorViewportContract contract = EditorGameViewContract.GameView(Editor.EditorMode.Play);
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

        // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
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
                () => Editor.EditorMode.Play,
                () => snapshot,
                () => panelPoint,
                () => true));

        EditorHostInputCapture editorCapture = EditorGameViewContract.ResolveEditorInputCapture(
            EditorGameViewContract.GameView(Editor.EditorMode.Play),
            new EditorInputSnapshot(WantCaptureMouse: false, WantCaptureKeyboard: false),
            snapshot,
            panelPoint);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, editorCapture);
        UiInputCapture uiCapture = router.Pump(allowPointer: state.AllowWorldMouse, allowKeyboard: state.AllowWorldKeyboard);
        state = InputArbitrator.ApplyGameUi(state, uiCapture);

        // Assert：验证预期结果
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
    /// 验证生产路径按本帧 framebuffer 指针映射 Game UI；即使上一帧 panel hover/point 尚未更新，
    /// 同一物理点击的按下与释放边沿也不能丢失。
    /// </summary>
    [Fact]
    public void GameViewUiInputSourceUsesCurrentFramebufferPointerWithoutPriorHoverSnapshot()
    {
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));
        FixedUiInputSource input = new(new UiPointerState(
            280f,
            330f,
            0f,
            0f,
            LeftDown: true,
            RightDown: false,
            MiddleDown: false));
        RecordingBackend backend = new((x, y) => x == 160f && y == 90f
            ? new UiHitResult(HitsUi: true, Opaque: false, WantsMouse: true, WantsKeyboard: false)
            : UiHitResult.None);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 180, 1f), UiBackendKind.ManagedFallback));
        GameViewUiInputSource source = new(
            input,
            () => Editor.EditorMode.Play,
            () => snapshot,
            () => Vector2.Zero,
            () => false,
            () => new Vector2(100f, 200f),
            () => new Vector2(2f, 2f),
            keyboardFocusedProvider: () => false,
            panelVisibleProvider: () => true);
        UiInputRouter router = new(host, source);

        _ = router.Pump();
        input.Pointer = input.Pointer with { LeftDown = false };
        _ = router.Pump();

        Assert.Equal(160f, backend.LastPointerMoveX, precision: 3);
        Assert.Equal(90f, backend.LastPointerMoveY, precision: 3);
        Assert.Equal(
            [(UiPointerButton.Left, true), (UiPointerButton.Left, false)],
            backend.PointerButtons);
        GameViewUiInputDiagnostics diagnostics = source.CaptureDiagnostics();
        Assert.True(diagnostics.Attached);
        Assert.Equal(4, diagnostics.InnerPointerSamples);
        Assert.Equal(4, diagnostics.MappedPointerSamples);
        Assert.Equal(2, diagnostics.RawLeftDownSamples);
        Assert.Equal(1, diagnostics.RawLeftPressEdges);
        Assert.Equal(1, diagnostics.RawLeftReleaseEdges);
        Assert.Equal(2, diagnostics.ForwardedLeftDownSamples);
        Assert.Equal(1, diagnostics.ForwardedLeftPressEdges);
        Assert.Equal(1, diagnostics.ForwardedLeftReleaseEdges);
        Assert.Equal(new Vector2(280f, 330f), diagnostics.LastWindowPoint);
        Assert.Equal(new Vector2(160f, 90f), diagnostics.LastViewportPoint);
        Assert.True(diagnostics.LastPanelVisible);
        Assert.True(diagnostics.LastMappingSucceeded);
    }

    /// <summary>
    /// 验证 Game UI 键盘输入只依赖 Game View 键盘焦点，不错误依赖 mouse hover。
    /// </summary>
    [Fact]
    public void GameViewUiInputSourceForwardsKeyboardWhenFocusedWithoutPointerHover()
    {
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));
        FixedUiInputSource inner = new(new UiPointerState(0f, 0f, 0f, 0f, false, false, false))
        {
            DownKeys = [new UiKey(65)],
        };
        GameViewUiInputSource source = new(
            inner,
            () => Editor.EditorMode.Play,
            () => snapshot,
            () => new Vector2(0f, 0f),
            () => false,
            keyboardFocusedProvider: () => true);
        Span<UiKey> keys = stackalloc UiKey[4];

        int count = source.CaptureDownKeys(keys, out _);

        Assert.Equal(1, count);
        Assert.Equal(new UiKey(65), keys[0]);
        Assert.False(source.TryGetPointer(out _));
    }

    /// <summary>
    /// 验证 Game View 坐标闭环仍保留透明 UI 区域 pass-through 语义。
    /// </summary>
    [Fact]
    public void GameViewUiInputSourceMappedTransparentHitPassesThroughToGameplay()
    {
        // Arrange：准备输入与初始状态
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
                () => Editor.EditorMode.Play,
                () => snapshot,
                () => panelPoint,
                () => true));

        EditorHostInputCapture editorCapture = EditorGameViewContract.ResolveEditorInputCapture(
            EditorGameViewContract.GameView(Editor.EditorMode.Play),
            new EditorInputSnapshot(WantCaptureMouse: false, WantCaptureKeyboard: false),
            snapshot,
            panelPoint);
        InputArbitrationState state = InputArbitrator.ApplyEditor(InputArbitrationState.Allowed, editorCapture);
        UiInputCapture uiCapture = router.Pump(allowPointer: state.AllowWorldMouse, allowKeyboard: state.AllowWorldKeyboard);
        state = InputArbitrator.ApplyGameUi(state, uiCapture);

        // Assert：验证预期结果
        Assert.Equal(EditorHostInputCapture.None, editorCapture);
        Assert.Equal(160f, backend.LastHitTestX, precision: 3);
        Assert.Equal(90f, backend.LastHitTestY, precision: 3);
        Assert.Equal(UiInputCapture.None, uiCapture);
        Assert.True(state.AllowWorldMouse);
        Assert.True(state.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证 Game View 输入源只在 runtime mode 且拥有键盘焦点时转发键盘与文本输入。
    /// </summary>
    [Fact]
    public void GameViewUiInputSourceBlocksKeyboardTextAndCompositionOutsideRuntimeKeyboardFocus()
    {
        // Arrange：准备输入与初始状态
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));

        AssertKeyboardBlocked(Editor.EditorMode.Edit, focused: true, panelPoint: new Vector2(90f, 65f));
        AssertKeyboardBlocked(Editor.EditorMode.Play, focused: false, panelPoint: new Vector2(90f, 65f));

        void AssertKeyboardBlocked(Editor.EditorMode mode, bool focused, Vector2 panelPoint)
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

            // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
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
            () => Editor.EditorMode.Play,
            () => snapshot,
            () => new Vector2(90f, 65f),
            () => true);
        Span<UiKey> keys = stackalloc UiKey[4];
        Span<char> text = stackalloc char[4];

        // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
        GameViewPanel panel = new(() => new RenderViewportTexture(12, 320, 180));

        EditorViewportContract contract = panel.CaptureContract(Editor.EditorMode.Play);

        // Assert：验证预期结果
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
    /// 验证 Editor runtime ImGui 输入使用 Game View 的 pointer/keyboard 焦点，而不是整窗直通。
    /// </summary>
    [Fact]
    public void EditorRuntimeGuiInputRouteUsesGameViewFocusAndFramebufferMapper()
    {
        string root = FindRepositoryRoot();
        string extension = File.ReadAllText(Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorShellHostExtension.cs"));

        Assert.Contains("public bool AllowsRuntimeGuiKeyboardInput =>", extension, StringComparison.Ordinal);
        Assert.Contains("KeyboardFocused: true", extension, StringComparison.Ordinal);
        Assert.Contains("public bool TryMapFramebufferPointerToViewport(", extension, StringComparison.Ordinal);
        Assert.Contains("PointerHovered: true", extension, StringComparison.Ordinal);
        Assert.Contains("LastViewportSnapshot.TryMapFramebufferToWorld(", extension, StringComparison.Ordinal);
        Assert.Contains("LastPanelOriginFramebuffer", extension, StringComparison.Ordinal);
        Assert.Contains("LastFramebufferScale", extension, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Game View 输出侧把 panel-local image rect 转成 framebuffer-space UI present target。
    /// </summary>
    [Fact]
    public void GameViewViewportSnapshotCreatesFramebufferUiPresentTargetFromImageRect()
    {
        // Arrange：准备输入与初始状态
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10.25f, 20.5f),
            availablePanelSize: new Vector2(160f, 160f));

        // Assert：验证预期结果
        Assert.True(snapshot.TryCreateUiPresentTarget(new Vector2(201f, 80.5f), new Vector2(2f, 2f), out UiPresentTarget target));

        Assert.Equal(221, target.X);
        Assert.Equal(121, target.Y);
        Assert.Equal(321, target.Width);
        Assert.Equal(181, target.Height);
        Assert.Equal(2f, target.DpiScale);
        Assert.Equal(target.Scissor, new UiScissorRect(221, 121, 321, 181));
        Assert.True(target.IsValid);
    }

    /// <summary>
    /// 验证 runtime ImGui 指针按窗口 framebuffer、DPI 和 Game View letterbox 映射到纹理像素。
    /// </summary>
    [Fact]
    public void GameViewViewportSnapshotMapsFramebufferPointerThroughDpiAndLetterbox()
    {
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));
        Vector2 panelOriginFramebuffer = new(100f, 40f);
        Vector2 dpi = new(2f, 2f);

        // panel-local image center=(90,65)，window framebuffer=(100,40)+(90,65)*2=(280,170)。
        Assert.True(snapshot.TryMapFramebufferToViewport(
            new Vector2(280f, 170f),
            panelOriginFramebuffer,
            dpi,
            out Vector2 viewportPoint));
        Assert.Equal(160f, viewportPoint.X, precision: 3);
        Assert.Equal(90f, viewportPoint.Y, precision: 3);

        // 同一面板的下方 letterbox 空白不能命中 runtime GUI。
        Assert.False(snapshot.TryMapFramebufferToViewport(
            new Vector2(280f, 300f),
            panelOriginFramebuffer,
            dpi,
            out _));
        Assert.False(GameViewViewportSnapshot.Empty.TryMapFramebufferToViewport(
            new Vector2(280f, 170f),
            panelOriginFramebuffer,
            dpi,
            out _));
        Assert.False(snapshot.TryMapFramebufferToViewport(
            new Vector2(float.NaN, 170f),
            panelOriginFramebuffer,
            dpi,
            out _));
    }

    /// <summary>
    /// 验证 IME 几何从 viewport 映射到 window client 时与 present target 使用同一 panel origin + DPI 约定。
    /// </summary>
    [Fact]
    public void GameViewViewportSnapshotMapsImeGeometryToWindowClientWithPanelOriginAndDpi()
    {
        // fitScale = 0.5（160x90 image for 320x180 texture）
        // panel-local caret = (100*0.5+10, 80*0.5+20) = (60, 60)；size *= 0.5 → (2, 9)
        // window = origin + panel-local * dpi = (100,40) + (60,60)*2 = (220, 160)；size *= 2 → (4, 18)
        // Arrange：准备输入与初始状态
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));
        UiImeGeometry viewportGeometry = UiImeGeometry.FromCaretRect(100f, 80f, 4f, 18f);

        // Assert：验证预期结果
        Assert.True(snapshot.TryMapViewportImeGeometryToWindowClient(
            in viewportGeometry,
            panelOriginFramebuffer: new Vector2(100f, 40f),
            framebufferScale: new Vector2(2f, 2f),
            out UiImeGeometry windowGeometry));

        Assert.True(windowGeometry.HasCaretRect);
        Assert.Equal(220f, windowGeometry.CaretX, precision: 3);
        Assert.Equal(160f, windowGeometry.CaretY, precision: 3);
        Assert.Equal(4f, windowGeometry.CaretWidth, precision: 3);
        Assert.Equal(18f, windowGeometry.CaretHeight, precision: 3);
        Assert.True(windowGeometry.HasCandidateAnchor);
        Assert.Equal(220f, windowGeometry.CandidateAnchorX, precision: 3);
        Assert.Equal(178f, windowGeometry.CandidateAnchorY, precision: 3);

        Assert.False(snapshot.TryMapViewportImeGeometryToWindowClient(
            UiImeGeometry.None,
            new Vector2(100f, 40f),
            Vector2.One,
            out UiImeGeometry none));
        Assert.False(none.HasAny);
        Assert.False(GameViewViewportSnapshot.Empty.TryMapViewportImeGeometryToWindowClient(
            in viewportGeometry,
            new Vector2(100f, 40f),
            Vector2.One,
            out _));
    }

    /// <summary>
    /// 验证 GameViewUiInputSource 把 viewport IME 几何发布为 window client 坐标，而不是停留在 panel-local。
    /// </summary>
    [Fact]
    public void GameViewUiInputSourceMapsImeGeometryToWindowClientCoordinates()
    {
        // Arrange：准备输入与初始状态
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));
        FixedUiInputSource inner = new(new UiPointerState(0f, 0f, 0f, 0f, LeftDown: false, RightDown: false, MiddleDown: false));
        GameViewUiInputSource source = new(
            inner,
            () => Editor.EditorMode.Play,
            () => snapshot,
            () => new Vector2(90f, 65f),
            () => true,
            () => new Vector2(100f, 40f),
            () => new Vector2(2f, 2f));

        source.ApplyImeGeometry(UiImeGeometry.FromCaretRect(100f, 80f, 4f, 18f));

        // Assert：验证预期结果
        Assert.Equal(1, inner.ApplyImeGeometryCalls);
        Assert.True(inner.LastImeGeometry.HasCaretRect);
        Assert.Equal(220f, inner.LastImeGeometry.CaretX, precision: 3);
        Assert.Equal(160f, inner.LastImeGeometry.CaretY, precision: 3);
        Assert.Equal(4f, inner.LastImeGeometry.CaretWidth, precision: 3);
        Assert.Equal(18f, inner.LastImeGeometry.CaretHeight, precision: 3);

        source.ApplyImeGeometry(UiImeGeometry.None);
        Assert.Equal(2, inner.ApplyImeGeometryCalls);
        Assert.False(inner.LastImeGeometry.HasAny);
    }

    /// <summary>
    /// 验证 shipped 路径：ManagedFallback 预编辑布局 → UiInputRouter 发布 → GameView 映射到 window client。
    /// </summary>
    [Fact]
    public void UiInputRouterPublishesManagedFallbackImeGeometryThroughGameViewMapping()
    {
        // 320x180 viewport；fitScale=0.5；panel origin fb=(100,40)；dpi=2
        // Arrange：准备输入与初始状态
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));
        // 指针必须落在图像内以透传；modal 屏保证 WantCaptureKeyboard。
        // panel-local (90,65) 经 panel origin framebuffer=(100,40)、DPI=2 映射为当前 framebuffer (280,170)。
        FixedUiInputSource inner = new(new UiPointerState(280f, 170f, 0f, 0f, LeftDown: false, RightDown: false, MiddleDown: false))
        {
            CompositionText = "候補",
            Composition = new UiTextComposition(isActive: true, cursorIndex: 1),
        };
        GameViewUiInputSource gameViewSource = new(
            inner,
            () => Editor.EditorMode.Play,
            () => snapshot,
            () => new Vector2(90f, 65f),
            () => true,
            () => new Vector2(100f, 40f),
            () => new Vector2(2f, 2f));

        NullGuiHost gui = new();
        using ManagedFallbackBackend backend = new(gui);
        using GameUiHost host = new(backend);
        host.Initialize(new UiBackendInitializeInfo(new UiViewport(0, 0, 320, 180, 1f), UiBackendKind.ManagedFallback));
        string uiPath = Path.Combine(Path.GetTempPath(), $"pixelengine-gameview-ime-{Guid.NewGuid():N}.xhtml");
        File.WriteAllText(uiPath, """
            <ui title="Ime">
              <text id="label">IME</text>
            </ui>
            """);
        try
        {
            UiDocumentHandle document = backend.LoadDocument(UiDocumentSource.Asset(uiPath, 1));
            // Modal 使任意图像内指针都 WantCaptureKeyboard，从而泵入 composition。
            backend.SetScreenStack([new UiScreenStackEntry(new UiScreenHandle(1), new UiScreenId(1), document, Modal: true)]);
            UiInputRouter router = new(host, gameViewSource);

            _ = router.Pump(allowPointer: true, allowKeyboard: true);

            // Assert：验证预期结果
            Assert.True(backend.TryGetImeGeometry(out UiImeGeometry viewportGeometry));
            Assert.True(viewportGeometry.HasAny);
            UiImeGeometry expected = UiImeGeometryLayout.ComputePreeditOverlayGeometry(
                new UiViewport(0, 0, 320, 180, 1f),
                textLength: 2,
                cursorIndex: 1);
            Assert.Equal(expected.CaretX, viewportGeometry.CaretX, precision: 3);
            Assert.Equal(expected.CaretY, viewportGeometry.CaretY, precision: 3);

            Assert.True(inner.ApplyImeGeometryCalls >= 1);
            Assert.True(inner.LastImeGeometry.HasCaretRect);
            Assert.True(snapshot.TryMapViewportImeGeometryToWindowClient(
                in expected,
                new Vector2(100f, 40f),
                new Vector2(2f, 2f),
                out UiImeGeometry mappedExpected));
            Assert.Equal(mappedExpected.CaretX, inner.LastImeGeometry.CaretX, precision: 3);
            Assert.Equal(mappedExpected.CaretY, inner.LastImeGeometry.CaretY, precision: 3);
            Assert.Equal(mappedExpected.CaretWidth, inner.LastImeGeometry.CaretWidth, precision: 3);
            Assert.Equal(mappedExpected.CaretHeight, inner.LastImeGeometry.CaretHeight, precision: 3);

            // 关闭 composition 后应清除 window 几何。
            inner.Composition = UiTextComposition.Inactive;
            inner.CompositionText = string.Empty;
            _ = router.Pump(allowPointer: true, allowKeyboard: true);
            Assert.False(inner.LastImeGeometry.HasAny);
        }
        finally
        {
            try
            {
                File.Delete(uiPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// 验证 Game View UI present target provider 只在 runtime 模式、面板可见且 snapshot 有效时输出纹理内全尺寸目标。
    /// </summary>
    [Fact]
    public void GameViewUiPresentTargetProviderUsesVisibleRuntimeTextureRect()
    {
        // Arrange：准备输入与初始状态
        GameViewViewportSnapshot snapshot = GameViewViewportSnapshot.Create(
            textureWidth: 320,
            textureHeight: 180,
            imageMinPanel: new Vector2(10f, 20f),
            availablePanelSize: new Vector2(160f, 160f));
        GameViewUiPresentTargetProvider provider = new(
            () => Editor.EditorMode.Play,
            () => snapshot,
            () => true);

        // Assert：验证预期结果
        Assert.True(provider.TryGetPresentTarget(out UiPresentTarget target));
        Assert.Equal(new UiPresentTarget(0, 0, 320, 180, 1f), target);

        provider = new GameViewUiPresentTargetProvider(
            () => Editor.EditorMode.Play,
            () => snapshot,
            () => false);

        Assert.False(provider.TryGetPresentTarget(out _));

        provider = new GameViewUiPresentTargetProvider(
            () => Editor.EditorMode.Edit,
            () => snapshot,
            () => true);

        Assert.False(provider.TryGetPresentTarget(out _));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("未找到 PixelEngine 仓库根目录。");
    }

    private sealed class NullGuiHost : IManagedFallbackGuiHost
    {
        public bool IsRunning { get; private set; }

        public void Initialize()
        {
            IsRunning = true;
        }

        public void DrawFrame(float deltaSeconds, int width, int height, Action<IGuiDrawContext> drawGui)
        {
            _ = deltaSeconds;
            _ = width;
            _ = height;
            _ = drawGui;
        }

        public ManagedFallbackImage LoadImage(string path)
        {
            _ = path;
            return new ManagedFallbackImage(1, 1, 1);
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
            _ = text;
        }
    }

    private sealed class FixedUiInputSource(UiPointerState pointer) : IUiInputSource
    {
        public UiPointerState Pointer { get; set; } = pointer;

        public UiKey[] DownKeys { get; init; } = [];

        public UiKeyModifiers Modifiers { get; init; }

        public string Text { get; set; } = string.Empty;

        public string CompositionText { get; set; } = string.Empty;

        public UiTextComposition Composition { get; set; }

        public int CaptureDownKeysCalls { get; private set; }

        public int CaptureTextCalls { get; private set; }

        public int CaptureTextCompositionCalls { get; private set; }

        public int ApplyImeGeometryCalls { get; private set; }

        public UiImeGeometry LastImeGeometry { get; private set; }

        public bool TryGetPointer(out UiPointerState state)
        {
            state = Pointer;
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

        public void ApplyImeGeometry(in UiImeGeometry geometry)
        {
            ApplyImeGeometryCalls++;
            LastImeGeometry = geometry;
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

        public List<(UiPointerButton Button, bool IsDown)> PointerButtons { get; } = [];

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
            PointerButtons.Add((button, isDown));
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
