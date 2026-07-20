using PixelEngine.Gui;
using PixelEngine.Hosting;
using PixelEngine.Rendering;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor Shell 主窗口：ImGui 帧循环、面板布局与输入路由。
/// </summary>
internal sealed class EditorShellWindow : IDisposable
{
    private const string LayoutPathEnvironmentVariable = "PIXELENGINE_EDITOR_LAYOUT_PATH";

    public static string DefaultLayoutPath
    {
        get
        {
            string? overridePath = Environment.GetEnvironmentVariable(LayoutPathEnvironmentVariable);
            return string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PixelEngine",
                    "editor-shell-imgui.ini")
                : Path.GetFullPath(overridePath);
        }
    }

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

    public void EnsureProjectPickerGui()
    {
        if (!_projectPickerGuiShutdown)
        {
            return;
        }

        _bootstrap.EnsureInputConnector();
        _projectPickerGuiShutdown = false;
    }

    public static EditorShellWindow Create(
        float uiScale = EditorUiScale.Default,
        string? layoutPath = null,
        int width = EditorWorkspaceWindowState.DefaultWidth,
        int height = EditorWorkspaceWindowState.DefaultHeight,
        int? x = null,
        int? y = null,
        EditorWorkspaceWindowStateKind state = EditorWorkspaceWindowStateKind.Normal)
    {
        int normalizedWidth = width > 0 ? width : EditorWorkspaceWindowState.DefaultWidth;
        int normalizedHeight = height > 0 ? height : EditorWorkspaceWindowState.DefaultHeight;
        RenderWindowOptions windowOptions = new()
        {
            Title = PixelEngineProduct.Name,
            Width = normalizedWidth,
            Height = normalizedHeight,
            PositionX = x,
            PositionY = y,
            InitialState = state switch
            {
                EditorWorkspaceWindowStateKind.Normal => RenderWindowState.Normal,
                EditorWorkspaceWindowStateKind.Minimized => RenderWindowState.Minimized,
                EditorWorkspaceWindowStateKind.Maximized => RenderWindowState.Maximized,
                EditorWorkspaceWindowStateKind.Fullscreen => RenderWindowState.Fullscreen,
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, "未知 workspace 窗口状态。"),
            },
            BackendPreference = OperatingSystem.IsWindows()
                ? RenderBackendPreference.CaptureCompatible
                : RenderBackendPreference.Auto,
            VSync = true,
            UseDarkWindowChrome = true,
            TitleBarColorRgb = 0x202226,
            TitleBarTextColorRgb = 0xE7E9ED,
            WindowBorderColorRgb = 0x111216,
        };
        EditorFontStackPaths fonts = EditorFontAssets.ResolveRuntime();
        GuiAppOptions guiOptions = new()
        {
            Enabled = true,
            LayoutPath = string.IsNullOrWhiteSpace(layoutPath) ? DefaultLayoutPath : Path.GetFullPath(layoutPath),
            Theme = GuiThemeKind.Unity6Dark,
            DpiScale = EditorUiScale.Normalize(uiScale),
            PrimaryFontPath = fonts.PrimaryFontPath,
            CjkFallbackFontPath = fonts.CjkFallbackFontPath,
            FontSizePixels = EditorFontAssets.BaseFontSizePixels,
        };
        return new EditorShellWindow(EditorHostBootstrap.Create(windowOptions, guiOptions));
    }

    public void SetTitle(string? projectName, string? sceneName, bool dirty)
    {
        Window.SetTitle(PixelEngineProduct.FormatWindowTitle(projectName, sceneName, dirty));
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
