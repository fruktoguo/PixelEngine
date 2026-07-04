namespace PixelEngine.Rendering;

/// <summary>
/// present 前 UI 层。渲染管线按注册 order 升序调用，确保游戏 UI 先于编辑器 UI。
/// </summary>
public interface IUiPresentLayer
{
    /// <summary>
    /// 在 gamma 后、SwapBuffers 前绘制 UI。
    /// </summary>
    /// <param name="context">UI present 上下文。</param>
    void Present(in UiPresentContext context);
}
