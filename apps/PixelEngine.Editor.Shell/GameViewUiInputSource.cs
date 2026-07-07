using PixelEngine.UI;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

internal sealed class GameViewUiInputSource(
    IUiInputSource inner,
    Func<PixelEngine.Editor.EditorMode> modeProvider,
    Func<GameViewViewportSnapshot> viewportProvider,
    Func<Vector2> panelPointProvider,
    Func<bool> inputFocusedProvider) : IUiInputSource
{
    private readonly IUiInputSource _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Func<PixelEngine.Editor.EditorMode> _modeProvider = modeProvider ?? throw new ArgumentNullException(nameof(modeProvider));
    private readonly Func<GameViewViewportSnapshot> _viewportProvider = viewportProvider ?? throw new ArgumentNullException(nameof(viewportProvider));
    private readonly Func<Vector2> _panelPointProvider = panelPointProvider ?? throw new ArgumentNullException(nameof(panelPointProvider));
    private readonly Func<bool> _inputFocusedProvider = inputFocusedProvider ?? throw new ArgumentNullException(nameof(inputFocusedProvider));

    public UiTextCompositionCapabilities TextCompositionCapabilities => _inner.TextCompositionCapabilities;

    public bool TryGetPointer(out UiPointerState state)
    {
        state = default;
        if (!TryMapFocusedViewportPoint(out Vector2 viewportPoint))
        {
            return false;
        }

        if (!_inner.TryGetPointer(out UiPointerState windowState))
        {
            return false;
        }

        state = new UiPointerState(
            viewportPoint.X,
            viewportPoint.Y,
            windowState.WheelDeltaX,
            windowState.WheelDeltaY,
            windowState.LeftDown,
            windowState.RightDown,
            windowState.MiddleDown);
        return true;
    }

    public int CaptureDownKeys(Span<UiKey> destination, out UiKeyModifiers modifiers)
    {
        if (!CanForwardKeyboardInput())
        {
            modifiers = UiKeyModifiers.None;
            return 0;
        }

        return _inner.CaptureDownKeys(destination, out modifiers);
    }

    public int CaptureText(Span<char> destination)
    {
        if (!CanForwardKeyboardInput())
        {
            _ = _inner.CaptureText(destination);
            destination.Clear();
            return 0;
        }

        return _inner.CaptureText(destination);
    }

    public int CaptureTextComposition(Span<char> destination, out UiTextComposition composition)
    {
        if (!CanForwardKeyboardInput())
        {
            _ = _inner.CaptureTextComposition(destination, out _);
            destination.Clear();
            composition = UiTextComposition.Inactive;
            return 0;
        }

        return _inner.CaptureTextComposition(destination, out composition);
    }

    private bool CanForwardKeyboardInput()
    {
        return TryMapFocusedViewportPoint(out _);
    }

    private bool TryMapFocusedViewportPoint(out Vector2 viewportPoint)
    {
        viewportPoint = default;
        return _modeProvider() == PixelEngine.Editor.EditorMode.Play &&
            _inputFocusedProvider() &&
            _viewportProvider().TryMapPanelToViewport(_panelPointProvider(), out viewportPoint);
    }
}
