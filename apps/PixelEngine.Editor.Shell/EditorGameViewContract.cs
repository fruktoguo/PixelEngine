using PixelEngine.Hosting;
using PixelEngine.Rendering;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

internal enum EditorViewportSurface
{
    SceneView,
    GameView,
}

internal enum EditorViewportCameraOwner
{
    AuthoringCamera,
    RuntimePipelineCamera,
}

internal enum EditorViewportInputOwner
{
    AuthoringTools,
    GameUiThenGameplay,
}

internal enum EditorViewportInputClip
{
    ImageRect,
}

internal enum EditorViewportCoordinateSpace
{
    ViewportTexturePixels,
}

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
    EditorViewportCoordinateSpace GameUiCoordinateSpace,
    EditorViewportHitTestSource GameUiHitTestSource)
{
    public bool EditorOverlayHasPriority => AllowsEditorOverlay && EditorOverlayLayerOrder > GameUiLayerOrder;
}

internal static class EditorGameViewContract
{
    public static EditorViewportContract SceneView(PixelEngine.Editor.EditorMode mode)
    {
        _ = mode;
        return new EditorViewportContract(
            EditorViewportSurface.SceneView,
            PixelEngine.Editor.EditorDockSpace.ViewportWindowTitle,
            EditorViewportCameraOwner.AuthoringCamera,
            EditorViewportInputOwner.AuthoringTools,
            UsesRuntimeViewportTexture: true,
            AllowsEditorOverlay: true,
            UiPresentLayerOrders.Game,
            UiPresentLayerOrders.Editor,
            EditorViewportInputClip.ImageRect,
            EditorViewportCoordinateSpace.ViewportTexturePixels,
            EditorViewportHitTestSource.PanelLocalImageRectMappedToViewport);
    }

    public static EditorViewportContract GameView(PixelEngine.Editor.EditorMode mode)
    {
        return new EditorViewportContract(
            EditorViewportSurface.GameView,
            PixelEngine.Editor.EditorDockSpace.GameViewWindowTitle,
            EditorViewportCameraOwner.RuntimePipelineCamera,
            mode == PixelEngine.Editor.EditorMode.Play
                ? EditorViewportInputOwner.GameUiThenGameplay
                : EditorViewportInputOwner.AuthoringTools,
            UsesRuntimeViewportTexture: true,
            AllowsEditorOverlay: true,
            UiPresentLayerOrders.Game,
            UiPresentLayerOrders.Editor,
            EditorViewportInputClip.ImageRect,
            EditorViewportCoordinateSpace.ViewportTexturePixels,
            EditorViewportHitTestSource.PanelLocalImageRectMappedToViewport);
    }

    public static EditorHostInputCapture ResolveEditorInputCapture(
        in EditorViewportContract contract,
        in PixelEngine.Editor.EditorInputSnapshot editorCapture,
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
        in PixelEngine.Editor.EditorInputSnapshot editorCapture,
        bool viewportHasInputFocus)
    {
        if (!viewportHasInputFocus)
        {
            return new EditorHostInputCapture(WantCaptureMouse: true, WantCaptureKeyboard: true);
        }

        return contract.InputOwner == EditorViewportInputOwner.AuthoringTools
            ? new EditorHostInputCapture(WantCaptureMouse: true, WantCaptureKeyboard: true)
            : !editorCapture.WantCaptureMouse && !editorCapture.WantCaptureKeyboard
                ? EditorHostInputCapture.None
                : new EditorHostInputCapture(editorCapture.WantCaptureMouse, editorCapture.WantCaptureKeyboard);
    }
}
