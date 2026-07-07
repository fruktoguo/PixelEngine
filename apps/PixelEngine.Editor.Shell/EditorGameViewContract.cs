using PixelEngine.Hosting;
using PixelEngine.Rendering;

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

internal readonly record struct EditorViewportContract(
    EditorViewportSurface Surface,
    string WindowTitle,
    EditorViewportCameraOwner CameraOwner,
    EditorViewportInputOwner InputOwner,
    bool UsesRuntimeViewportTexture,
    bool AllowsEditorOverlay,
    int GameUiLayerOrder,
    int EditorOverlayLayerOrder);

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
            UiPresentLayerOrders.Editor);
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
            UiPresentLayerOrders.Editor);
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
