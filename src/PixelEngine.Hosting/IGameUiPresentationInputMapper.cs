namespace PixelEngine.Hosting;

/// <summary>
/// 将宿主窗口 framebuffer 输入映射到完整 Game UI presentation 坐标。
/// </summary>
/// <remarks>
/// Game UI 与 runtime Gui 都绘制在完整 presentation 上；该映射与 gameplay world 映射分离，
/// 避免把内部 world 坐标误送给 presentation UI。
/// </remarks>
public interface IGameUiPresentationInputMapper
{
    /// <summary>当前宿主是否允许 Game UI 与 runtime Gui 接收键盘输入。</summary>
    bool AllowsGameUiKeyboardInput { get; }

    /// <summary>
    /// 将窗口 framebuffer 指针映射到完整 Game UI presentation。
    /// </summary>
    /// <param name="framebufferX">窗口 framebuffer X。</param>
    /// <param name="framebufferY">窗口 framebuffer Y。</param>
    /// <param name="presentationX">成功时的 presentation X。</param>
    /// <param name="presentationY">成功时的 presentation Y。</param>
    /// <returns>指针位于当前可交互 presentation 时返回 <see langword="true"/>。</returns>
    bool TryMapFramebufferPointerToGameUi(
        float framebufferX,
        float framebufferY,
        out float presentationX,
        out float presentationY);
}
