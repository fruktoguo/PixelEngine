using PixelEngine.Gui;

namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 复用的中性 Gui host 抽象；实现必须共享既有玩家 GUI host。
/// </summary>
public interface IManagedFallbackGuiHost
{
    /// <summary>
    /// Gui host 是否已初始化并运行。
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 初始化 Gui host。
    /// </summary>
    void Initialize();

    /// <summary>
    /// 在当前 Gui host 上绘制一帧。
    /// </summary>
    /// <param name="deltaSeconds">渲染帧 dt。</param>
    /// <param name="width">framebuffer 宽度。</param>
    /// <param name="height">framebuffer 高度。</param>
    /// <param name="drawGui">绘制回调。</param>
    void DrawFrame(float deltaSeconds, int width, int height, Action<IGuiDrawContext> drawGui);
}
