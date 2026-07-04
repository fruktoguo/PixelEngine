using PixelEngine.Gui;

namespace PixelEngine.UI;

/// <summary>
/// 可在既有 GuiApp frame 内绘制的托管 UI 后端。
/// </summary>
public interface IManagedGuiDrawable
{
    /// <summary>
    /// 在当前 Gui frame 内绘制 UI。
    /// </summary>
    /// <param name="gui">中性 Gui 绘制上下文。</param>
    void DrawGui(IGuiDrawContext gui);
}
