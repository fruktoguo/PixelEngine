using PixelEngine.Rendering;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 为 Game UI 提供 runtime viewport 纹理内的 present target。
/// </summary>
internal sealed class GameViewUiPresentTargetProvider(
    Func<EditorMode> modeProvider,
    Func<GameViewViewportSnapshot> viewportProvider,
    Func<bool> visibleProvider) : IUiPresentTargetProvider
{
    private readonly Func<EditorMode> _modeProvider =
        modeProvider ?? throw new ArgumentNullException(nameof(modeProvider));
    private readonly Func<GameViewViewportSnapshot> _viewportProvider =
        viewportProvider ?? throw new ArgumentNullException(nameof(viewportProvider));
    private readonly Func<bool> _visibleProvider =
        visibleProvider ?? throw new ArgumentNullException(nameof(visibleProvider));

    public bool TryGetPresentTarget(out UiPresentTarget target)
    {
        if (_modeProvider() is not (EditorMode.Play or EditorMode.Paused) || !_visibleProvider())
        {
            target = default;
            return false;
        }

        return _viewportProvider().TryCreateRuntimeUiPresentTarget(out target);
    }
}
