using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImPlot;
using System.Numerics;

namespace PixelEngine.Editor;

/// <summary>
/// 基于 Hexa.NET.ImGui 与 OpenGL3 backend 的真实 ImGui 后端。
/// </summary>
public sealed class HexaImGuiBackend : IEditorImGuiBackend
{
    private readonly EditorDockSpace _dockSpace = new();
    private readonly EditorFontManager _fontManager = new();
    private ImGuiContextPtr _context;
    private ImPlotContextPtr _plotContext;
    private string _layoutPath = string.Empty;
    private bool _initialized;

    /// <inheritdoc />
    public EditorInputSnapshot Capture
    {
        get
        {
            if (!_initialized)
            {
                return default;
            }

            ImGuiIOPtr io = ImGui.GetIO();
            return new EditorInputSnapshot(io.WantCaptureMouse, io.WantCaptureKeyboard);
        }
    }

    /// <inheritdoc />
    public void Initialize(EditorAppOptions options)
    {
        options = options.Normalize();
        if (_initialized)
        {
            throw new InvalidOperationException("ImGui 后端已经初始化。");
        }

        _context = ImGui.CreateContext();
        _layoutPath = options.LayoutPath;
        ImGui.SetCurrentContext(_context);
        ImGuiImplOpenGL3.SetCurrentContext(_context);
        ImPlot.SetImGuiContext(_context);
        _plotContext = ImPlot.CreateContext();
        ImPlot.SetCurrentContext(_plotContext);
        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= EditorDockSpace.BuildConfigFlags(options.EnableMultiViewport);
        AddConfiguredFont(io, options);
        bool hasSavedLayout = File.Exists(_layoutPath);
        _dockSpace.ResetLayoutState(buildDefaultLayout: !hasSavedLayout);
        if (hasSavedLayout)
        {
            ImGui.LoadIniSettingsFromDisk(_layoutPath);
        }

        _ = ImGuiImplOpenGL3.Init(options.GlslVersion);
        _initialized = true;
    }

    /// <inheritdoc />
    public void NewFrame(float deltaSeconds, int width, int height)
    {
        ThrowIfNotInitialized();
        if (deltaSeconds <= 0)
        {
            deltaSeconds = 1f / 60f;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(Math.Max(1, width), Math.Max(1, height));
        io.DeltaTime = deltaSeconds;
        ImGuiImplOpenGL3.NewFrame();
        ImGui.NewFrame();
    }

    /// <inheritdoc />
    public void DrawDockSpace()
    {
        ThrowIfNotInitialized();
        _dockSpace.Draw();
    }

    /// <inheritdoc />
    public void ResetDockLayout()
    {
        ThrowIfNotInitialized();
        if (!string.IsNullOrWhiteSpace(_layoutPath))
        {
            File.Delete(_layoutPath);
        }

        _dockSpace.ResetLayoutState(buildDefaultLayout: true);
    }

    /// <inheritdoc />
    public void Render()
    {
        ThrowIfNotInitialized();
        ImGui.Render();
        ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
    }

    /// <inheritdoc />
    public void AddMousePosition(float x, float y)
    {
        if (!_initialized)
        {
            return;
        }

        ImGui.AddMousePosEvent(ImGui.GetIO(), x, y);
    }

    /// <inheritdoc />
    public void AddMouseButton(int button, bool down)
    {
        if (!_initialized)
        {
            return;
        }

        ImGui.AddMouseButtonEvent(ImGui.GetIO(), button, down);
    }

    /// <inheritdoc />
    public void AddMouseWheel(float wheelX, float wheelY)
    {
        if (!_initialized)
        {
            return;
        }

        ImGui.AddMouseWheelEvent(ImGui.GetIO(), wheelX, wheelY);
    }

    /// <inheritdoc />
    public void AddKey(ImGuiKey key, bool down)
    {
        if (!_initialized)
        {
            return;
        }

        ImGui.AddKeyEvent(ImGui.GetIO(), key, down);
    }

    /// <inheritdoc />
    public void AddText(string text)
    {
        if (!_initialized || string.IsNullOrEmpty(text))
        {
            return;
        }

        ImGui.AddInputCharactersUTF8(ImGui.GetIO(), text);
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        ImGuiImplOpenGL3.Shutdown();
        ImPlot.DestroyContext(_plotContext);
        if (!string.IsNullOrWhiteSpace(_layoutPath))
        {
            ImGui.SaveIniSettingsToDisk(_layoutPath);
        }

        ImGui.DestroyContext(_context);
        _fontManager.Dispose();
        _context = default;
        _plotContext = default;
        _layoutPath = string.Empty;
        _initialized = false;
    }

    private void AddConfiguredFont(ImGuiIOPtr io, EditorAppOptions options)
    {
        string? fontPath = _fontManager.ResolveCjkFontPath(options.PreferredFontPath);
        float fontSize = EditorFontManager.ScaleFontSize(options.FontSizePixels, options.DpiScale);
        if (fontPath is null)
        {
            _ = ImGui.AddFontDefault(io.Fonts);
            return;
        }

        _ = _fontManager.AddCjkFont(io.Fonts, fontPath, fontSize);
    }

    private void ThrowIfNotInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("ImGui 后端尚未初始化。");
        }
    }
}
