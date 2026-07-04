using PixelEngine.Gui;
using PixelEngine.Rendering;

namespace PixelEngine.Hosting;

/// <summary>
/// 独立编辑器壳在 Engine 装配前可使用的中性窗口与 GUI bootstrap。
/// </summary>
public sealed class EditorHostBootstrap : IDisposable
{
    private readonly GuiWindowInputConnector _inputConnector;
    private bool _disposed;

    private EditorHostBootstrap(RenderWindow window, GuiApp gui, GuiWindowInputConnector inputConnector)
    {
        Window = window;
        Gui = gui;
        _inputConnector = inputConnector;
    }

    /// <summary>
    /// 已创建且由 bootstrap 拥有的渲染窗口。
    /// </summary>
    public RenderWindow Window { get; }

    /// <summary>
    /// 已创建且由 bootstrap 拥有的中性 GUI host。
    /// </summary>
    public GuiApp Gui { get; }

    /// <summary>
    /// 创建编辑器壳启动阶段的窗口与中性 GUI host；调用方可先绘制项目选择器，再把窗口交给 Engine 外部 attach 路径。
    /// </summary>
    /// <param name="windowOptions">窗口创建参数。</param>
    /// <param name="guiOptions">GUI 创建参数。</param>
    /// <param name="diagnostics">渲染后端诊断回调。</param>
    /// <returns>拥有窗口、GUI 与输入连接器的 bootstrap 句柄。</returns>
    public static EditorHostBootstrap Create(
        RenderWindowOptions windowOptions,
        GuiAppOptions guiOptions,
        Action<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(windowOptions);
        ArgumentNullException.ThrowIfNull(guiOptions);
        RenderWindow window = RenderWindow.Create(windowOptions, diagnostics);
        try
        {
            GuiApp gui = new(new HexaImGuiBackend(), guiOptions);
            try
            {
                GuiWindowInputConnector input = new(window, gui.Input);
                return new EditorHostBootstrap(window, gui, input);
            }
            catch
            {
                gui.Dispose();
                throw;
            }
        }
        catch
        {
            window.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 释放输入连接器、GUI host 与 bootstrap 拥有的窗口。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _inputConnector.Dispose();
        Gui.Dispose();
        Window.Dispose();
        _disposed = true;
    }
}
