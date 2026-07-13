using System.Numerics;
using PixelEngine.Rendering;
using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// 独立 Player 的 gameplay 输入映射器：先从 OS framebuffer 进入完整 presentation，再只允许 world content rect。
/// </summary>
internal sealed class GamePresentationViewportInputMapper(
    RenderWindow window,
    GamePresentationCoordinator presentation) : IGameplayViewportInputMapper
{
    private readonly RenderWindow _window = window ?? throw new ArgumentNullException(nameof(window));
    private readonly GamePresentationCoordinator _presentation =
        presentation ?? throw new ArgumentNullException(nameof(presentation));

    /// <summary>当前未持有平台指针快照；调用方应使用带 framebuffer 坐标的重载完成原子映射。</summary>
    /// <param name="viewportX">失败时写入零。</param>
    /// <param name="viewportY">失败时写入零。</param>
    /// <returns>始终为 <see langword="false"/>。</returns>
    public bool TryMapPointerToViewport(out float viewportX, out float viewportY)
    {
        viewportX = 0f;
        viewportY = 0f;
        return false;
    }

    /// <summary>把 OS framebuffer 指针映射到固定 world 坐标，并拒绝 presentation letterbox 区域。</summary>
    /// <param name="framebufferX">framebuffer X 坐标。</param>
    /// <param name="framebufferY">framebuffer Y 坐标。</param>
    /// <param name="viewportX">成功时的 world X 坐标。</param>
    /// <param name="viewportY">成功时的 world Y 坐标。</param>
    /// <returns>仅当指针位于 OS presentation 与 world content rect 内时为 <see langword="true"/>。</returns>
    public bool TryMapFramebufferPointerToViewport(
        float framebufferX,
        float framebufferY,
        out float viewportX,
        out float viewportY)
    {
        viewportX = 0f;
        viewportY = 0f;
        GamePresentationDescriptor descriptor = _presentation.Current;
        if (!GamePresentationInputTransform.TryMapFramebufferToPresentation(
                in descriptor,
                _window.Width,
                _window.Height,
                framebufferX,
                framebufferY,
                out Vector2 presentationPoint))
        {
            return false;
        }

        GamePresentationInputMapping mapping = GamePresentationInputMapping.Resolve(
            in descriptor,
            presentationPoint);
        if (!mapping.IsInsideWorldContent)
        {
            return false;
        }

        viewportX = mapping.WorldPoint.X;
        viewportY = mapping.WorldPoint.Y;
        return true;
    }
}

/// <summary>
/// 独立 Player 的 Game UI 输入源：UI 命中完整 presentation，外层 OS letterbox 不产生指针输入。
/// </summary>
internal sealed class GamePresentationUiInputSource(
    IUiInputSource inner,
    RenderWindow window,
    GamePresentationCoordinator presentation) : IUiInputSource
{
    private readonly IUiInputSource _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly RenderWindow _window = window ?? throw new ArgumentNullException(nameof(window));
    private readonly GamePresentationCoordinator _presentation =
        presentation ?? throw new ArgumentNullException(nameof(presentation));

    /// <summary>底层平台输入源支持的文本组合能力。</summary>
    public UiTextCompositionCapabilities TextCompositionCapabilities => _inner.TextCompositionCapabilities;

    /// <summary>读取指针并从 OS framebuffer 映射到完整 presentation；外层黑边不产生 UI 指针。</summary>
    /// <param name="state">成功时的 presentation 指针状态。</param>
    /// <returns>存在有效且位于 presentation 内的指针时为 <see langword="true"/>。</returns>
    public bool TryGetPointer(out UiPointerState state)
    {
        state = default;
        if (!_inner.TryGetPointer(out UiPointerState framebufferState))
        {
            return false;
        }

        GamePresentationDescriptor descriptor = _presentation.Current;
        if (!GamePresentationInputTransform.TryMapFramebufferToPresentation(
                in descriptor,
                _window.Width,
                _window.Height,
                framebufferState.X,
                framebufferState.Y,
                out Vector2 point))
        {
            return false;
        }

        state = new UiPointerState(
            point.X,
            point.Y,
            framebufferState.WheelDeltaX,
            framebufferState.WheelDeltaY,
            framebufferState.LeftDown,
            framebufferState.RightDown,
            framebufferState.MiddleDown);
        return true;
    }

    /// <summary>转发当前按键快照。</summary>
    /// <param name="destination">接收按键的缓冲区。</param>
    /// <param name="modifiers">当前修饰键。</param>
    /// <returns>写入的按键数量。</returns>
    public int CaptureDownKeys(Span<UiKey> destination, out UiKeyModifiers modifiers)
    {
        return _inner.CaptureDownKeys(destination, out modifiers);
    }

    /// <summary>转发本帧已提交文本。</summary>
    /// <param name="destination">接收文本的缓冲区。</param>
    /// <returns>写入的字符数量。</returns>
    public int CaptureText(Span<char> destination)
    {
        return _inner.CaptureText(destination);
    }

    /// <summary>转发本帧 IME composition 文本与 selection。</summary>
    /// <param name="destination">接收 composition 文本的缓冲区。</param>
    /// <param name="composition">组合态 selection 与长度。</param>
    /// <returns>写入的字符数量。</returns>
    public int CaptureTextComposition(Span<char> destination, out UiTextComposition composition)
    {
        return _inner.CaptureTextComposition(destination, out composition);
    }

    /// <summary>把 presentation 内的 IME 光标几何反向映射到 OS framebuffer。</summary>
    /// <param name="geometry">presentation 坐标系中的 IME 几何。</param>
    public void ApplyImeGeometry(in UiImeGeometry geometry)
    {
        if (!geometry.HasAny)
        {
            _inner.ApplyImeGeometry(UiImeGeometry.None);
            return;
        }

        GamePresentationDescriptor descriptor = _presentation.Current;
        if (!GamePresentationInputTransform.TryMapPresentationGeometryToFramebuffer(
                in descriptor,
                _window.Width,
                _window.Height,
                in geometry,
                out UiImeGeometry framebufferGeometry))
        {
            _inner.ApplyImeGeometry(UiImeGeometry.None);
            return;
        }

        _inner.ApplyImeGeometry(in framebufferGeometry);
    }
}

/// <summary>OS framebuffer、presentation 与 IME 几何之间的无状态原子变换。</summary>
internal static class GamePresentationInputTransform
{
    /// <summary>把 OS framebuffer 坐标映射到完整 presentation 坐标。</summary>
    /// <param name="descriptor">当前已提交的 presentation 描述。</param>
    /// <param name="framebufferWidth">OS framebuffer 宽度。</param>
    /// <param name="framebufferHeight">OS framebuffer 高度。</param>
    /// <param name="framebufferX">framebuffer X 坐标。</param>
    /// <param name="framebufferY">framebuffer Y 坐标。</param>
    /// <param name="presentationPoint">成功时的 presentation 坐标。</param>
    /// <returns>输入有效且位于 OS presentation rect 内时为 <see langword="true"/>。</returns>
    public static bool TryMapFramebufferToPresentation(
        in GamePresentationDescriptor descriptor,
        int framebufferWidth,
        int framebufferHeight,
        float framebufferX,
        float framebufferY,
        out Vector2 presentationPoint)
    {
        presentationPoint = default;
        if (!descriptor.IsValid ||
            framebufferWidth <= 0 ||
            framebufferHeight <= 0 ||
            !float.IsFinite(framebufferX) ||
            !float.IsFinite(framebufferY))
        {
            return false;
        }

        PresentationViewport viewport = PresentationViewport.Fit(
            descriptor.PresentationWidth,
            descriptor.PresentationHeight,
            framebufferWidth,
            framebufferHeight);
        if (!SilkInputPhaseDriver.ContainsFramebufferPoint(in viewport, framebufferX, framebufferY))
        {
            return false;
        }

        (float x, float y) = viewport.MapFramebufferToSource(framebufferX, framebufferY);
        presentationPoint = new Vector2(x, y);
        return true;
    }

    /// <summary>把 presentation IME 几何映射到 OS framebuffer，供平台候选窗定位。</summary>
    /// <param name="descriptor">当前已提交的 presentation 描述。</param>
    /// <param name="framebufferWidth">OS framebuffer 宽度。</param>
    /// <param name="framebufferHeight">OS framebuffer 高度。</param>
    /// <param name="geometry">presentation 坐标系中的 IME 几何。</param>
    /// <param name="framebufferGeometry">成功时的 OS framebuffer 几何。</param>
    /// <returns>描述与几何都有效时为 <see langword="true"/>。</returns>
    public static bool TryMapPresentationGeometryToFramebuffer(
        in GamePresentationDescriptor descriptor,
        int framebufferWidth,
        int framebufferHeight,
        in UiImeGeometry geometry,
        out UiImeGeometry framebufferGeometry)
    {
        framebufferGeometry = UiImeGeometry.None;
        if (!descriptor.IsValid || !geometry.HasAny || framebufferWidth <= 0 || framebufferHeight <= 0)
        {
            return false;
        }

        PresentationViewport viewport = PresentationViewport.Fit(
            descriptor.PresentationWidth,
            descriptor.PresentationHeight,
            framebufferWidth,
            framebufferHeight);
        float top = viewport.TargetHeight - viewport.Y - viewport.Height;
        framebufferGeometry = geometry.Transform(
            viewport.X,
            top,
            viewport.Width / (float)descriptor.PresentationWidth,
            viewport.Height / (float)descriptor.PresentationHeight);
        return framebufferGeometry.HasAny;
    }
}
