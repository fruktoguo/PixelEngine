using PixelEngine.Editor;
using PixelEngine.Editor.Shell;
using PixelEngine.Rendering;
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
    }
}
