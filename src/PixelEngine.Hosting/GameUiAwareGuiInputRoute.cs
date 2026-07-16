using PixelEngine.Gui;
using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// 在完整 presentation 上仲裁 Game UI 与 runtime Gui 输入所有权。
/// </summary>
internal sealed class GameUiAwareGuiInputRoute(
    IGameUiPresentationInputMapper mapper,
    GameUiCanvasRegistry registry,
    UiInputRouter uiInput) : IGuiViewportInputRoute
{
    private readonly IGameUiPresentationInputMapper _mapper =
        mapper ?? throw new ArgumentNullException(nameof(mapper));
    private readonly GameUiCanvasRegistry _registry =
        registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly UiInputRouter _uiInput =
        uiInput ?? throw new ArgumentNullException(nameof(uiInput));

    /// <summary>Game UI 未拥有键盘时，runtime Gui 才能接收同一组按键。</summary>
    public bool AllowsKeyboardInput =>
        _mapper.AllowsGameUiKeyboardInput && !_uiInput.Capture.WantCaptureKeyboard;

    /// <summary>native UI 拒绝时清空 Gui；共享 ImGui 的 ManagedFallback 拒绝时保留队列。</summary>
    public bool ClearsPointerWhenRejected { get; private set; } = true;

    /// <summary>
    /// 将窗口指针映射到 presentation；Game UI 命中或捕获期间返回 false，防止底层 Gui 重复消费。
    /// </summary>
    public bool TryMapPointer(
        float framebufferX,
        float framebufferY,
        out float viewportX,
        out float viewportY)
    {
        ClearsPointerWhenRejected = true;
        if (!_mapper.TryMapFramebufferPointerToGameUi(
                framebufferX,
                framebufferY,
                out viewportX,
                out viewportY))
        {
            viewportX = 0f;
            viewportY = 0f;
            return false;
        }

        if (_registry.TryResolvePointerInputOwner(
                viewportX,
                viewportY,
                out UiBackendKind backendKind))
        {
            ClearsPointerWhenRejected = backendKind != UiBackendKind.ManagedFallback;
            viewportX = 0f;
            viewportY = 0f;
            return false;
        }

        return true;
    }
}
