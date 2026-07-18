using PixelEngine.Hosting;
using PixelEngine.Rendering;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 编辑器视口表面：Scene View 或 Game View。
/// </summary>
internal enum EditorViewportSurface
{
    SceneView,
    GameView,
}

/// <summary>
/// 视口相机归属：编辑相机或运行时管线相机。
/// </summary>
internal enum EditorViewportCameraOwner
{
    AuthoringCamera,
    RuntimePipelineCamera,
}

/// <summary>
/// 视图当前观察的权威世界来源；相机和 presentation 仍由各自 surface 独立拥有。
/// </summary>
internal enum EditorViewportWorldSource
{
    AuthoringWorld,
    RuntimeWorld,
}

/// <summary>
/// 视口输入归属：编辑工具或 Game UI/玩法。
/// </summary>
internal enum EditorViewportInputOwner
{
    AuthoringTools,
    GameUiThenGameplay,
}

/// <summary>
/// 输入坐标裁剪到图像矩形。
/// </summary>
internal enum EditorViewportInputClip
{
    ImageRect,
}

/// <summary>
/// 输出坐标裁剪到图像矩形。
/// </summary>
internal enum EditorViewportOutputClip
{
    ImageRect,
}

/// <summary>
/// UI 坐标空间：viewport 纹理或 framebuffer 像素。
/// </summary>
internal enum EditorViewportCoordinateSpace
{
    ViewportTexturePixels,
    FramebufferPixels,
}

/// <summary>
/// Game UI 命中测试坐标来源。
/// </summary>
internal enum EditorViewportHitTestSource
{
    PanelLocalImageRectMappedToViewport,
}

internal readonly record struct EditorViewportContract(
    EditorViewportSurface Surface,
    string WindowTitle,
    EditorViewportCameraOwner CameraOwner,
    EditorViewportWorldSource WorldSource,
    EditorViewportInputOwner InputOwner,
    bool UsesRuntimeViewportTexture,
    bool AllowsEditorOverlay,
    int GameUiLayerOrder,
    int EditorOverlayLayerOrder,
    EditorViewportInputClip InputClip,
    EditorViewportOutputClip OutputClip,
    EditorViewportCoordinateSpace GameUiCoordinateSpace,
    EditorViewportCoordinateSpace GameUiOutputCoordinateSpace,
    EditorViewportHitTestSource GameUiHitTestSource)
{
    public bool EditorOverlayHasPriority => AllowsEditorOverlay && EditorOverlayLayerOrder > GameUiLayerOrder;
}

/// <summary>
/// Scene View 与 Game View 的 viewport 契约工厂。
/// </summary>
internal static class EditorGameViewContract
{
    public static EditorViewportContract SceneView(EditorMode mode)
    {
        _ = mode;
        return new EditorViewportContract(
            EditorViewportSurface.SceneView,
            EditorDockSpace.ViewportWindowTitle,
            EditorViewportCameraOwner.AuthoringCamera,
            mode is EditorMode.Play or EditorMode.Paused
                ? EditorViewportWorldSource.RuntimeWorld
                : EditorViewportWorldSource.AuthoringWorld,
            EditorViewportInputOwner.AuthoringTools,
            UsesRuntimeViewportTexture: false,
            AllowsEditorOverlay: true,
            UiPresentLayerOrders.Game,
            UiPresentLayerOrders.Editor,
            EditorViewportInputClip.ImageRect,
            EditorViewportOutputClip.ImageRect,
            EditorViewportCoordinateSpace.ViewportTexturePixels,
            EditorViewportCoordinateSpace.FramebufferPixels,
            EditorViewportHitTestSource.PanelLocalImageRectMappedToViewport);
    }

    public static EditorViewportContract GameView(EditorMode mode)
    {
        return new EditorViewportContract(
            EditorViewportSurface.GameView,
            EditorDockSpace.GameViewWindowTitle,
            EditorViewportCameraOwner.RuntimePipelineCamera,
            mode is EditorMode.Play or EditorMode.Paused
                ? EditorViewportWorldSource.RuntimeWorld
                : EditorViewportWorldSource.AuthoringWorld,
            mode is EditorMode.Play or EditorMode.Paused
                ? EditorViewportInputOwner.GameUiThenGameplay
                : EditorViewportInputOwner.AuthoringTools,
            UsesRuntimeViewportTexture: true,
            AllowsEditorOverlay: true,
            UiPresentLayerOrders.Game,
            UiPresentLayerOrders.Editor,
            EditorViewportInputClip.ImageRect,
            EditorViewportOutputClip.ImageRect,
            EditorViewportCoordinateSpace.ViewportTexturePixels,
            EditorViewportCoordinateSpace.FramebufferPixels,
            EditorViewportHitTestSource.PanelLocalImageRectMappedToViewport);
    }

    public static EditorHostInputCapture ResolveEditorInputCapture(
        in EditorViewportContract contract,
        in EditorInputSnapshot editorCapture,
        in GameViewViewportSnapshot viewport,
        Vector2 panelPoint)
    {
        return ResolveEditorInputCapture(
            in contract,
            in editorCapture,
            viewportHasInputFocus: viewport.ContainsPanelPoint(panelPoint));
    }

    public static EditorHostInputCapture ResolveEditorInputCapture(
        in EditorViewportContract contract,
        in EditorInputSnapshot editorCapture,
        bool viewportHasInputFocus)
    {
        return ResolveEditorInputCapture(
            in contract,
            in editorCapture,
            pointerHasInputFocus: viewportHasInputFocus,
            keyboardHasInputFocus: viewportHasInputFocus);
    }

    /// <summary>
    /// 按独立 mouse/keyboard 所有权解析 Editor 对 gameplay 的输入捕获。
    /// </summary>
    /// <remarks>
    /// ImGui 的全局 WantCapture 会包含 Game View 画布自身造成的捕获，不能据此再次阻断 gameplay。
    /// 菜单、popup、modal 或其他面板取得 hover/focus 后，对应 Game View 所有权会变为 false，仍由 Editor 优先。
    /// </remarks>
    public static EditorHostInputCapture ResolveEditorInputCapture(
        in EditorViewportContract contract,
        in EditorInputSnapshot editorCapture,
        bool pointerHasInputFocus,
        bool keyboardHasInputFocus)
    {
        _ = editorCapture;
        return contract.InputOwner == EditorViewportInputOwner.AuthoringTools
            ? new EditorHostInputCapture(WantCaptureMouse: true, WantCaptureKeyboard: true)
            : new EditorHostInputCapture(
                WantCaptureMouse: !pointerHasInputFocus,
                WantCaptureKeyboard: !keyboardHasInputFocus);
    }
}
