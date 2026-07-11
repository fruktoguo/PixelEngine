namespace PixelEngine.Hosting;

/// <summary>
/// 外部宿主提供的 gameplay 指针坐标映射器。
/// </summary>
/// <remarks>
/// 独立游戏默认按整个窗口映射；Editor 等嵌入式宿主应把面板内图像坐标映射为 runtime viewport 像素，
/// 使脚本输入与 Game UI 使用同一 DPI、letterbox 和缩放契约。
/// </remarks>
public interface IGameplayViewportInputMapper
{
    /// <summary>
    /// runtime GUI 当前是否拥有键盘焦点。
    /// </summary>
    /// <remarks>
    /// 默认值保持既有独立/自定义宿主行为；Editor 应按 Game View 实际焦点覆盖。
    /// </remarks>
    bool AllowsRuntimeGuiKeyboardInput => true;

    /// <summary>
    /// 尝试获取当前指针在 runtime viewport 纹理中的坐标。
    /// </summary>
    /// <param name="viewportX">runtime viewport X，左上角为原点。</param>
    /// <param name="viewportY">runtime viewport Y，左上角为原点。</param>
    /// <returns>指针位于宿主 gameplay 图像内并允许映射时返回 <see langword="true" />。</returns>
    bool TryMapPointerToViewport(out float viewportX, out float viewportY);

    /// <summary>
    /// 把窗口 framebuffer 指针坐标映射到 runtime viewport。
    /// </summary>
    /// <remarks>
    /// 默认实现兼容既有 mapper，并复用其当前指针快照；需要精确 DPI/letterbox 映射的嵌入宿主应覆盖。
    /// </remarks>
    /// <param name="framebufferX">窗口 framebuffer X，左上角为原点。</param>
    /// <param name="framebufferY">窗口 framebuffer Y，左上角为原点。</param>
    /// <param name="viewportX">runtime viewport X。</param>
    /// <param name="viewportY">runtime viewport Y。</param>
    /// <returns>当前窗口指针可路由到 runtime viewport 时返回 <see langword="true" />。</returns>
    bool TryMapFramebufferPointerToViewport(
        float framebufferX,
        float framebufferY,
        out float viewportX,
        out float viewportY)
    {
        _ = framebufferX;
        _ = framebufferY;
        return TryMapPointerToViewport(out viewportX, out viewportY);
    }
}
