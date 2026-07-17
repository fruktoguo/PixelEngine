using PixelEngine.Hosting;
using PixelEngine.UI;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Game View 上的 Game UI 输入源，按 viewport 契约裁剪坐标。
/// </summary>
internal sealed class GameViewUiInputSource(
    IUiInputSource inner,
    Func<EditorMode> modeProvider,
    Func<GameViewViewportSnapshot> viewportProvider,
    Func<Vector2> panelPointProvider,
    Func<bool> pointerHoveredProvider,
    Func<Vector2>? panelOriginFramebufferProvider = null,
    Func<Vector2>? framebufferScaleProvider = null,
    Func<bool>? keyboardFocusedProvider = null,
    Func<bool>? panelVisibleProvider = null) : IUiInputSource, IGameUiPresentationInputMapper
{
    private readonly IUiInputSource _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Func<EditorMode> _modeProvider = modeProvider ?? throw new ArgumentNullException(nameof(modeProvider));
    private readonly Func<GameViewViewportSnapshot> _viewportProvider = viewportProvider ?? throw new ArgumentNullException(nameof(viewportProvider));
    private readonly Func<Vector2> _panelPointProvider = panelPointProvider ?? throw new ArgumentNullException(nameof(panelPointProvider));
    private readonly Func<bool> _pointerHoveredProvider = pointerHoveredProvider ?? throw new ArgumentNullException(nameof(pointerHoveredProvider));
    private readonly Func<Vector2> _panelOriginFramebufferProvider =
        panelOriginFramebufferProvider ?? (() => Vector2.Zero);
    private readonly Func<Vector2> _framebufferScaleProvider =
        framebufferScaleProvider ?? (() => Vector2.One);
    private readonly Func<bool> _keyboardFocusedProvider =
        keyboardFocusedProvider ?? pointerHoveredProvider;
    private readonly Func<bool> _panelVisibleProvider =
        panelVisibleProvider ?? pointerHoveredProvider;
    private readonly bool _mapsCurrentFramebufferPointer =
        panelOriginFramebufferProvider is not null && framebufferScaleProvider is not null;
    private bool _previousRawLeftDown;
    private bool _previousForwardedLeftDown;
    private long _innerPointerSamples;
    private long _mappedPointerSamples;
    private long _rawLeftDownSamples;
    private long _rawLeftPressEdges;
    private long _rawLeftReleaseEdges;
    private long _forwardedLeftDownSamples;
    private long _forwardedLeftPressEdges;
    private long _forwardedLeftReleaseEdges;
    private Vector2 _lastWindowPoint;
    private Vector2 _lastViewportPoint;
    private bool _lastPanelVisible;
    private bool _lastMappingSucceeded;

    public UiTextCompositionCapabilities TextCompositionCapabilities => _inner.TextCompositionCapabilities;

    /// <inheritdoc />
    public bool AllowsGameUiKeyboardInput => CanForwardKeyboardInput();

    public bool TryGetPointer(out UiPointerState state)
    {
        state = default;
        _lastPanelVisible = _panelVisibleProvider();
        if (!_inner.TryGetPointer(out UiPointerState windowState))
        {
            ObserveForwardedLeft(isDown: false);
            return false;
        }

        // 即使当前不是 Play/Paused 或 Game View 不可见，也必须推进底层物理边沿队列，
        // 防止 Edit 模式的旧点击在之后进入 Play 时被延迟重放。
        if (!IsRuntimeInputMode(_modeProvider()) || !_lastPanelVisible)
        {
            ObserveForwardedLeft(isDown: false);
            return false;
        }

        _innerPointerSamples++;
        _lastWindowPoint = new Vector2(windowState.X, windowState.Y);
        ObserveRawLeft(windowState.LeftDown);
        if (!TryMapViewportPoint(in windowState, out Vector2 viewportPoint))
        {
            _lastMappingSucceeded = false;
            ObserveForwardedLeft(isDown: false);
            return false;
        }

        _lastMappingSucceeded = true;
        _mappedPointerSamples++;
        _lastViewportPoint = viewportPoint;
        ObserveForwardedLeft(windowState.LeftDown);

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

    /// <inheritdoc />
    public bool TryMapFramebufferPointerToGameUi(
        float framebufferX,
        float framebufferY,
        out float presentationX,
        out float presentationY)
    {
        presentationX = 0f;
        presentationY = 0f;
        if (!IsRuntimeInputMode(_modeProvider()) || !_panelVisibleProvider())
        {
            return false;
        }

        GameViewViewportSnapshot viewport = _viewportProvider();
        if (!viewport.TryMapFramebufferToViewport(
                new Vector2(framebufferX, framebufferY),
                _panelOriginFramebufferProvider(),
                _framebufferScaleProvider(),
                out Vector2 point))
        {
            return false;
        }

        presentationX = point.X;
        presentationY = point.Y;
        return true;
    }

    /// <summary>捕获当前进程内只读物理输入诊断；不注入、消费或改写输入。</summary>
    internal GameViewUiInputDiagnostics CaptureDiagnostics()
    {
        return new GameViewUiInputDiagnostics(
            Attached: true,
            _innerPointerSamples,
            _mappedPointerSamples,
            _rawLeftDownSamples,
            _rawLeftPressEdges,
            _rawLeftReleaseEdges,
            _forwardedLeftDownSamples,
            _forwardedLeftPressEdges,
            _forwardedLeftReleaseEdges,
            _lastWindowPoint,
            _lastViewportPoint,
            _lastPanelVisible,
            _lastMappingSucceeded);
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

    /// <summary>
    /// 将 viewport 纹理坐标中的 IME 几何映射到窗口 client/framebuffer 坐标后交给窗口输入源，供 IMM32 定位候选窗。
    /// 映射与 Game View present target 同源：viewport → panel-local → panel origin + DPI。
    /// </summary>
    /// <param name="geometry">viewport 坐标中的定位几何。</param>
    public void ApplyImeGeometry(in UiImeGeometry geometry)
    {
        if (!geometry.HasAny)
        {
            _inner.ApplyImeGeometry(UiImeGeometry.None);
            return;
        }

        GameViewViewportSnapshot viewport = _viewportProvider();
        if (!viewport.TryMapViewportImeGeometryToWindowClient(
                in geometry,
                _panelOriginFramebufferProvider(),
                _framebufferScaleProvider(),
                out UiImeGeometry mapped))
        {
            _inner.ApplyImeGeometry(UiImeGeometry.None);
            return;
        }

        _inner.ApplyImeGeometry(in mapped);
    }

    private bool CanForwardKeyboardInput()
    {
        return IsRuntimeInputMode(_modeProvider()) && _keyboardFocusedProvider();
    }

    private bool TryMapViewportPoint(in UiPointerState windowState, out Vector2 viewportPoint)
    {
        GameViewViewportSnapshot viewport = _viewportProvider();
        if (_mapsCurrentFramebufferPointer)
        {
            return viewport.TryMapFramebufferToViewport(
                new Vector2(windowState.X, windowState.Y),
                _panelOriginFramebufferProvider(),
                _framebufferScaleProvider(),
                out viewportPoint);
        }

        viewportPoint = default;
        return _pointerHoveredProvider() &&
            viewport.TryMapPanelToViewport(_panelPointProvider(), out viewportPoint);
    }

    private void ObserveRawLeft(bool isDown)
    {
        if (isDown)
        {
            _rawLeftDownSamples++;
        }

        if (isDown != _previousRawLeftDown)
        {
            if (isDown)
            {
                _rawLeftPressEdges++;
            }
            else
            {
                _rawLeftReleaseEdges++;
            }

            _previousRawLeftDown = isDown;
        }
    }

    private void ObserveForwardedLeft(bool isDown)
    {
        if (isDown)
        {
            _forwardedLeftDownSamples++;
        }

        if (isDown != _previousForwardedLeftDown)
        {
            if (isDown)
            {
                _forwardedLeftPressEdges++;
            }
            else
            {
                _forwardedLeftReleaseEdges++;
            }

            _previousForwardedLeftDown = isDown;
        }
    }

    private static bool IsRuntimeInputMode(EditorMode mode)
    {
        return mode is EditorMode.Play or EditorMode.Paused;
    }
}

/// <summary>Game View 当前 framebuffer 指针到 presentation UI 的只读诊断快照。</summary>
internal readonly record struct GameViewUiInputDiagnostics(
    bool Attached,
    long InnerPointerSamples,
    long MappedPointerSamples,
    long RawLeftDownSamples,
    long RawLeftPressEdges,
    long RawLeftReleaseEdges,
    long ForwardedLeftDownSamples,
    long ForwardedLeftPressEdges,
    long ForwardedLeftReleaseEdges,
    Vector2 LastWindowPoint,
    Vector2 LastViewportPoint,
    bool LastPanelVisible,
    bool LastMappingSucceeded);
