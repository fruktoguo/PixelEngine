namespace PixelEngine.Rendering;

/// <summary>
/// UI present 层的稳定 order 约定。数值越小越早绘制。
/// </summary>
public static class UiPresentLayerOrders
{
    /// <summary>
    /// 游戏 UI 层，位于世界画面之后、编辑器 UI 之前。
    /// </summary>
    public const int Game = 100;

    /// <summary>
    /// 编辑器 UI 层，仅编辑器/开发构建注册。
    /// Surface 必须由注册 overload 显式声明；order 不隐含 framebuffer 所有权。
    /// </summary>
    public const int Editor = 200;
}
