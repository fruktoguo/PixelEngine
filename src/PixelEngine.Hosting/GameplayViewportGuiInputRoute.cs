using PixelEngine.Gui;

namespace PixelEngine.Hosting;

/// <summary>
/// 把 Hosting 的 gameplay viewport mapper 适配为中性 runtime ImGui 输入路由。
/// </summary>
internal sealed class GameplayViewportGuiInputRoute(IGameplayViewportInputMapper mapper) : IGuiViewportInputRoute
{
    private readonly IGameplayViewportInputMapper _mapper =
        mapper ?? throw new ArgumentNullException(nameof(mapper));

    /// <summary>当前 runtime GUI 是否拥有键盘焦点。</summary>
    public bool AllowsKeyboardInput => _mapper.AllowsRuntimeGuiKeyboardInput;

    /// <summary>
    /// 把窗口 framebuffer 指针映射到 runtime viewport。
    /// </summary>
    /// <param name="framebufferX">窗口 framebuffer X。</param>
    /// <param name="framebufferY">窗口 framebuffer Y。</param>
    /// <param name="viewportX">runtime viewport X。</param>
    /// <param name="viewportY">runtime viewport Y。</param>
    /// <returns>指针位于 runtime viewport 时返回 true。</returns>
    public bool TryMapPointer(
        float framebufferX,
        float framebufferY,
        out float viewportX,
        out float viewportY)
    {
        return _mapper.TryMapFramebufferPointerToViewport(
            framebufferX,
            framebufferY,
            out viewportX,
            out viewportY);
    }
}
