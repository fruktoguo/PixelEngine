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
            mode == EditorMode.Play
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
        return !viewportHasInputFocus
            ? new EditorHostInputCapture(WantCaptureMouse: true, WantCaptureKeyboard: true)
            : contract.InputOwner == EditorViewportInputOwner.AuthoringTools
            ? new EditorHostInputCapture(WantCaptureMouse: true, WantCaptureKeyboard: true)
            : !editorCapture.WantCaptureMouse && !editorCapture.WantCaptureKeyboard
                ? EditorHostInputCapture.None
                : new EditorHostInputCapture(editorCapture.WantCaptureMouse, editorCapture.WantCaptureKeyboard);
    }
}
