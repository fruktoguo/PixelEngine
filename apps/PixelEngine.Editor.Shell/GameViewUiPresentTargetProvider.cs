using PixelEngine.Rendering;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 为 Game UI 提供 present target 矩形与 DPI 映射。
/// </summary>
internal sealed class GameViewUiPresentTargetProvider(
    Func<EditorMode> modeProvider,
    Func<GameViewViewportSnapshot> viewportProvider,
    Func<Vector2> panelOriginFramebufferProvider,
    Func<Vector2> framebufferScaleProvider,
    Func<bool> visibleProvider) : IUiPresentTargetProvider
{
    private readonly Func<EditorMode> _modeProvider =
        modeProvider ?? throw new ArgumentNullException(nameof(modeProvider));
    private readonly Func<GameViewViewportSnapshot> _viewportProvider =
        viewportProvider ?? throw new ArgumentNullException(nameof(viewportProvider));
    private readonly Func<Vector2> _panelOriginFramebufferProvider =
        panelOriginFramebufferProvider ?? throw new ArgumentNullException(nameof(panelOriginFramebufferProvider));
    private readonly Func<Vector2> _framebufferScaleProvider =
        framebufferScaleProvider ?? throw new ArgumentNullException(nameof(framebufferScaleProvider));
    private readonly Func<bool> _visibleProvider =
        visibleProvider ?? throw new ArgumentNullException(nameof(visibleProvider));

    public bool TryGetPresentTarget(out UiPresentTarget target)
    {
        if (_modeProvider() != EditorMode.Play || !_visibleProvider())
        {
            target = default;
            return false;
        }

        return _viewportProvider().TryCreateUiPresentTarget(
            _panelOriginFramebufferProvider(),
            _framebufferScaleProvider(),
            out target);
    }
}
