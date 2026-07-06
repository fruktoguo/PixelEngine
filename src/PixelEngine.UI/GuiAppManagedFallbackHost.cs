using PixelEngine.Gui;

namespace PixelEngine.UI;

/// <summary>
/// 把 <see cref="GuiApp" /> 适配为 ManagedFallbackBackend 可复用的绘制宿主。
/// </summary>
public sealed class GuiAppManagedFallbackHost(GuiApp gui) : IManagedFallbackGuiHost
{
    private readonly GuiApp _gui = gui ?? throw new ArgumentNullException(nameof(gui));

    /// <summary>
    /// 底层 GuiApp 是否已经运行。
    /// </summary>
    public bool IsRunning => _gui.IsRunning;

    /// <summary>
    /// 初始化底层 GuiApp。
    /// </summary>
    public void Initialize()
    {
        _gui.Initialize();
    }

    /// <summary>
    /// 在 GuiApp 的托管绘制帧中执行回调。
    /// </summary>
    /// <param name="deltaSeconds">渲染帧 dt，单位秒。</param>
    /// <param name="width">帧缓冲宽度。</param>
    /// <param name="height">帧缓冲高度。</param>
    /// <param name="drawGui">托管 GUI 绘制回调。</param>
    public void DrawFrame(float deltaSeconds, int width, int height, Action<IGuiDrawContext> drawGui)
    {
        _gui.DrawManagedFrame(deltaSeconds, width, height, drawGui);
    }
}
