using PixelEngine.Gui;

namespace PixelEngine.UI;

/// <summary>
/// 把 <see cref="GuiApp" /> 适配为 ManagedFallbackBackend 可复用的绘制宿主。
/// </summary>
public sealed class GuiAppManagedFallbackHost(GuiApp gui) : IManagedFallbackGuiHost
{
    private readonly GuiApp _gui = gui ?? throw new ArgumentNullException(nameof(gui));

    /// <inheritdoc />
    public bool IsRunning => _gui.IsRunning;

    /// <inheritdoc />
    public void Initialize()
    {
        _gui.Initialize();
    }

    /// <inheritdoc />
    public void DrawFrame(float deltaSeconds, int width, int height, Action<IGuiDrawContext> drawGui)
    {
        _gui.DrawManagedFrame(deltaSeconds, width, height, drawGui);
    }
}
