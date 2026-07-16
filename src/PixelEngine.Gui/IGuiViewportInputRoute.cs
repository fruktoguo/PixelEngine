namespace PixelEngine.Gui;

/// <summary>
/// 把宿主窗口输入裁剪并映射到 runtime GUI framebuffer。
/// </summary>
/// <remarks>
/// 独立 Player 不提供该路由，继续使用整个窗口坐标；Editor 等嵌入式宿主用它把 Game View
/// 图像区域（含 DPI 与 letterbox）映射到 runtime viewport，并阻止其他面板输入进入游戏 GUI。
/// </remarks>
public interface IGuiViewportInputRoute
{
    /// <summary>
    /// 当前 runtime GUI 是否拥有键盘焦点。
    /// </summary>
    bool AllowsKeyboardInput { get; }

    /// <summary>
    /// 映射拒绝后是否把共享 Gui 指针移出 viewport。
    /// </summary>
    /// <remarks>
    /// 默认 true 适用于离开 viewport 或 native UI 独占；共享同一 ImGui context 的托管 UI
    /// 会自行注入 presentation 指针，必须返回 false，避免先写入无效位置污染输入队列。
    /// </remarks>
    bool ClearsPointerWhenRejected => true;

    /// <summary>
    /// 尝试把窗口 framebuffer 指针坐标映射为 runtime GUI framebuffer 坐标。
    /// </summary>
    /// <param name="framebufferX">窗口 framebuffer X，左上角为原点。</param>
    /// <param name="framebufferY">窗口 framebuffer Y，左上角为原点。</param>
    /// <param name="viewportX">runtime viewport X，左上角为原点。</param>
    /// <param name="viewportY">runtime viewport Y，左上角为原点。</param>
    /// <returns>指针位于可交互 runtime 图像区域时返回 <see langword="true" />。</returns>
    bool TryMapPointer(
        float framebufferX,
        float framebufferY,
        out float viewportX,
        out float viewportY);
}
