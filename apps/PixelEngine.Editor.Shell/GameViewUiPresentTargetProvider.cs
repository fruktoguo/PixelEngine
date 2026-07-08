using PixelEngine.Rendering;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

internal sealed class GameViewUiPresentTargetProvider(
    Func<GameViewViewportSnapshot> viewportProvider,
    Func<Vector2> panelOriginFramebufferProvider,
    Func<bool> visibleProvider) : IUiPresentTargetProvider
{
    private readonly Func<GameViewViewportSnapshot> _viewportProvider =
        viewportProvider ?? throw new ArgumentNullException(nameof(viewportProvider));
    private readonly Func<Vector2> _panelOriginFramebufferProvider =
        panelOriginFramebufferProvider ?? throw new ArgumentNullException(nameof(panelOriginFramebufferProvider));
    private readonly Func<bool> _visibleProvider =
        visibleProvider ?? throw new ArgumentNullException(nameof(visibleProvider));

    public bool TryGetPresentTarget(out UiPresentTarget target)
    {
        if (!_visibleProvider())
        {
            target = default;
            return false;
        }

        return _viewportProvider().TryCreateUiPresentTarget(_panelOriginFramebufferProvider(), out target);
    }
}
