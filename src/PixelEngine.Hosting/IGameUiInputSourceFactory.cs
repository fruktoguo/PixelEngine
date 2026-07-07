using PixelEngine.Rendering;
using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// 允许外部宿主把窗口输入适配为游戏 UI 坐标空间；默认实现应回退到原始窗口输入源。
/// </summary>
public interface IGameUiInputSourceFactory
{
    /// <summary>
    /// 创建游戏 UI 输入源。
    /// </summary>
    /// <param name="window">当前渲染窗口。</param>
    /// <param name="fallback">Hosting 默认窗口输入源。</param>
    /// <returns>用于 <see cref="UiInputRouter" /> 的最终输入源。</returns>
    IUiInputSource CreateGameUiInputSource(RenderWindow window, IUiInputSource fallback);
}
