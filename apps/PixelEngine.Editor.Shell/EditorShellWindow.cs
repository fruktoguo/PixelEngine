using PixelEngine.Gui;
using PixelEngine.Hosting;
using PixelEngine.Rendering;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorShellWindow : IDisposable
{
    public static readonly string DefaultLayoutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PixelEngine",
        "editor-shell-imgui.ini");

    private readonly EditorHostBootstrap _bootstrap;
    private bool _projectPickerGuiShutdown;
    private bool _disposed;

    private EditorShellWindow(EditorHostBootstrap bootstrap)
    {
        _bootstrap = bootstrap;
    }

    public RenderWindow Window => _bootstrap.Window;

    public GuiApp Gui => _bootstrap.Gui;

    public void ShutdownProjectPickerGui()
    {
        if (_projectPickerGuiShutdown)
        {
            return;
        }

        _bootstrap.DisposeInputConnector();
        if (Gui.IsRunning)
        {
            Gui.Shutdown();
        }

        _projectPickerGuiShutdown = true;
    }

    public static EditorShellWindow Create()
    {
        RenderWindowOptions windowOptions = new()
        {
            Title = "PixelEngine Editor",
            Width = 1280,
            Height = 720,
            VSync = true,
        };
        GuiAppOptions guiOptions = new()
        {
            Enabled = true,
            LayoutPath = DefaultLayoutPath,
        };
        return new EditorShellWindow(EditorHostBootstrap.Create(windowOptions, guiOptions));
    }

    public void SetTitle(string? projectName, string? sceneName, bool dirty)
    {
        string project = string.IsNullOrWhiteSpace(projectName) ? "No Project" : projectName;
        string scene = string.IsNullOrWhiteSpace(sceneName) ? "No Scene" : sceneName;
        Window.SetTitle($"PixelEngine Editor - {project} - {scene}{(dirty ? "*" : string.Empty)}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _bootstrap.Dispose();
        _disposed = true;
    }
}
