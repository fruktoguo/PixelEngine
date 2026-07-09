using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;

namespace PixelEngine.Gui;

/// <summary>
/// 基于 Hexa.NET.ImGui 与 OpenGL3 backend 的中性 ImGui 后端。
/// </summary>
public sealed class HexaImGuiBackend : IGuiImGuiBackend
{
    private readonly GuiFontManager _fontManager = new();
    private readonly GuiClipboardBridge _clipboard = new();
    private ImGuiContextPtr _context;
    private string _layoutPath = string.Empty;
    private ImGuiFrameMetrics _frameMetrics = ImGuiFrameMetrics.Create(1, 1, 1f, 1f);
    private bool _initialized;

    /// <inheritdoc />
    public GuiInputSnapshot Capture
    {
        get
        {
            if (!_initialized)
            {
                return default;
            }

            SetCurrentContext();
            ImGuiIOPtr io = ImGui.GetIO();
            return new GuiInputSnapshot(io.WantCaptureMouse, io.WantCaptureKeyboard);
        }
    }

    /// <inheritdoc />
    public void Initialize(GuiAppOptions options)
    {
        options = options.Normalize();
        if (_initialized)
        {
            throw new InvalidOperationException("ImGui 后端已经初始化。");
        }

        _context = ImGui.CreateContext();
        _layoutPath = options.LayoutPath;
        SetCurrentContext();
        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        _clipboard.Attach();
        if (options.EnableMultiViewport)
        {
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
        }

        AddConfiguredFont(io, options);
        if (File.Exists(_layoutPath))
        {
            ImGui.LoadIniSettingsFromDisk(_layoutPath);
        }

        _ = ImGuiImplOpenGL3.Init(options.GlslVersion);
        _initialized = true;
    }

    /// <inheritdoc />
    public void NewFrame(float deltaSeconds, int width, int height, float framebufferScaleX, float framebufferScaleY)
    {
        ThrowIfNotInitialized();
        SetCurrentContext();
        if (deltaSeconds <= 0)
        {
            deltaSeconds = 1f / 60f;
        }

        ImGuiFrameMetrics metrics = ImGuiFrameMetrics.Create(width, height, framebufferScaleX, framebufferScaleY);
        _frameMetrics = metrics;

        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = metrics.DisplaySize;
        io.DisplayFramebufferScale = metrics.DisplayFramebufferScale;
        io.DeltaTime = deltaSeconds;
        ImGuiImplOpenGL3.NewFrame();
        ImGui.NewFrame();
    }

    /// <inheritdoc />
    public void Render()
    {
        ThrowIfNotInitialized();
        SetCurrentContext();
        ImGui.Render();
        ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
    }

    /// <inheritdoc />
    public void AddMousePosition(float x, float y)
    {
        if (_initialized)
        {
            SetCurrentContext();
            System.Numerics.Vector2 mapped = _frameMetrics.MapMousePosition(x, y);
            ImGui.AddMousePosEvent(ImGui.GetIO(), mapped.X, mapped.Y);
        }
    }

    /// <inheritdoc />
    public void AddFramebufferMousePosition(float x, float y)
    {
        if (_initialized)
        {
            SetCurrentContext();
            ImGui.AddMousePosEvent(ImGui.GetIO(), x, y);
        }
    }

    /// <inheritdoc />
    public void AddMouseButton(int button, bool down)
    {
        if (_initialized)
        {
            SetCurrentContext();
            ImGui.AddMouseButtonEvent(ImGui.GetIO(), button, down);
        }
    }

    /// <inheritdoc />
    public void AddMouseWheel(float wheelX, float wheelY)
    {
        if (_initialized)
        {
            SetCurrentContext();
            ImGui.AddMouseWheelEvent(ImGui.GetIO(), wheelX, wheelY);
        }
    }

    /// <inheritdoc />
    public void AddKey(ImGuiKey key, bool down)
    {
        if (_initialized)
        {
            SetCurrentContext();
            ImGui.AddKeyEvent(ImGui.GetIO(), key, down);
        }
    }

    /// <inheritdoc />
    public void AddText(string text)
    {
        if (_initialized && !string.IsNullOrEmpty(text))
        {
            SetCurrentContext();
            ImGui.AddInputCharactersUTF8(ImGui.GetIO(), text);
        }
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        SetCurrentContext();
        ImGuiImplOpenGL3.Shutdown();
        _clipboard.Detach();
        if (!string.IsNullOrWhiteSpace(_layoutPath))
        {
            ImGui.SaveIniSettingsToDisk(_layoutPath);
        }

        ImGui.DestroyContext(_context);
        _fontManager.Dispose();
        _clipboard.Dispose();
        _context = default;
        _layoutPath = string.Empty;
        _frameMetrics = ImGuiFrameMetrics.Create(1, 1, 1f, 1f);
        _initialized = false;
    }

    private void AddConfiguredFont(ImGuiIOPtr io, GuiAppOptions options)
    {
        string? fontPath = _fontManager.ResolveCjkFontPath(options.PreferredFontPath);
        float fontSize = GuiFontManager.ScaleFontSize(options.FontSizePixels, options.DpiScale);
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

    private void SetCurrentContext()
    {
        ImGui.SetCurrentContext(_context);
        ImGuiImplOpenGL3.SetCurrentContext(_context);
    }

}
