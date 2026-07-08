using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using System.Numerics;

namespace PixelEngine.Gui;

/// <summary>
/// 基于 Hexa.NET.ImGui 与 OpenGL3 backend 的中性 ImGui 后端。
/// </summary>
public sealed class HexaImGuiBackend : IGuiImGuiBackend
{
    private readonly GuiFontManager _fontManager = new();
    private ImGuiContextPtr _context;
    private string _layoutPath = string.Empty;
    private float _mouseScaleX = 1f;
    private float _mouseScaleY = 1f;
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
        ImGui.SetCurrentContext(_context);
        ImGuiImplOpenGL3.SetCurrentContext(_context);
        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
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
        if (deltaSeconds <= 0)
        {
            deltaSeconds = 1f / 60f;
        }

        float scaleX = NormalizeScale(framebufferScaleX);
        float scaleY = NormalizeScale(framebufferScaleY);
        int displayWidth = ScaleFramebufferDimension(width, scaleX);
        int displayHeight = ScaleFramebufferDimension(height, scaleY);
        _mouseScaleX = displayWidth / (float)Math.Max(1, width);
        _mouseScaleY = displayHeight / (float)Math.Max(1, height);

        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(displayWidth, displayHeight);
        io.DisplayFramebufferScale = Vector2.One;
        io.DeltaTime = deltaSeconds;
        ImGuiImplOpenGL3.NewFrame();
        ImGui.NewFrame();
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
        if (_initialized)
        {
            ImGui.AddMousePosEvent(ImGui.GetIO(), x * _mouseScaleX, y * _mouseScaleY);
        }
    }

    /// <inheritdoc />
    public void AddMouseButton(int button, bool down)
    {
        if (_initialized)
        {
            ImGui.AddMouseButtonEvent(ImGui.GetIO(), button, down);
        }
    }

    /// <inheritdoc />
    public void AddMouseWheel(float wheelX, float wheelY)
    {
        if (_initialized)
        {
            ImGui.AddMouseWheelEvent(ImGui.GetIO(), wheelX, wheelY);
        }
    }

    /// <inheritdoc />
    public void AddKey(ImGuiKey key, bool down)
    {
        if (_initialized)
        {
            ImGui.AddKeyEvent(ImGui.GetIO(), key, down);
        }
    }

    /// <inheritdoc />
    public void AddText(string text)
    {
        if (_initialized && !string.IsNullOrEmpty(text))
        {
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

        ImGuiImplOpenGL3.Shutdown();
        if (!string.IsNullOrWhiteSpace(_layoutPath))
        {
            ImGui.SaveIniSettingsToDisk(_layoutPath);
        }

        ImGui.DestroyContext(_context);
        _fontManager.Dispose();
        _context = default;
        _layoutPath = string.Empty;
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

    private static float NormalizeScale(float scale)
    {
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }

    private static int ScaleFramebufferDimension(int logical, float scale)
    {
        return Math.Max(1, (int)MathF.Round(Math.Max(1, logical) * scale));
    }
}
