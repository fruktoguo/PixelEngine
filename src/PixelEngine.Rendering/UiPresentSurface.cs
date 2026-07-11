namespace PixelEngine.Rendering;

/// <summary>
/// UI present 层当前写入的目标表面。
/// </summary>
public enum UiPresentSurface
{
    /// <summary>
    /// 引擎内部 runtime viewport 离屏表面；游戏 UI 必须写入这里，确保 Editor Game View 与独立游戏看到同一画面。
    /// </summary>
    RuntimeViewport,

    /// <summary>
    /// 平台窗口默认 framebuffer；Editor chrome、菜单与 modal 使用该表面并覆盖在 runtime viewport 之上。
    /// </summary>
    WindowFramebuffer,
}
