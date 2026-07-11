using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGuizmo;
using Hexa.NET.ImPlot;
using PixelEngine.Gui;
using System.Diagnostics;

namespace PixelEngine.Editor;

/// <summary>
/// 基于 Hexa.NET.ImGui 与 OpenGL3 backend 的真实 ImGui 后端。
/// </summary>
public sealed class HexaImGuiBackend : IEditorImGuiBackend
{
    private readonly EditorDockSpace _dockSpace = new();
    private readonly GuiFontManager _fontManager = new();
    private readonly GuiClipboardBridge _clipboard = new();
    private ImGuiContextPtr _context;
    private ImPlotContextPtr _plotContext;
    private string _layoutPath = string.Empty;
    private ImGuiFrameMetrics _frameMetrics = ImGuiFrameMetrics.Create(1, 1, 1f, 1f);
    private bool _saveLayoutOnShutdown = true;
    private float _fontAtlasScale = 1f;
    private float _appliedUiScale = 1f;
    private long _lastLayoutSaveTimestamp;
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

            SetCurrentContext();
            ImGuiIOPtr io = ImGui.GetIO();
            return new EditorInputSnapshot(io.WantCaptureMouse, io.WantCaptureKeyboard);
        }
    }

    /// <inheritdoc />
    public unsafe void Initialize(EditorAppOptions options)
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
        ImGuizmo.SetImGuiContext(_context);
        ImPlot.SetImGuiContext(_context);
        _plotContext = ImPlot.CreateContext();
        ImPlot.SetCurrentContext(_plotContext);
        ImGuiIOPtr io = ImGui.GetIO();
        io.IniFilename = null;
        io.ConfigFlags |= EditorDockSpace.BuildConfigFlags(options.EnableMultiViewport);
        _clipboard.Attach();
        GuiTheme.ApplyCurrent(options.Theme);
        AddConfiguredFont(io, options);
        _fontAtlasScale = NormalizeScale(options.DpiScale);
        _appliedUiScale = _fontAtlasScale;
        if (MathF.Abs(_appliedUiScale - 1f) > 0.0001f)
        {
            ImGui.GetStyle().ScaleAllSizes(_appliedUiScale);
        }

        ImGui.GetStyle().FontScaleMain = 1f;
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
    public void SetUiScale(float scale)
    {
        ThrowIfNotInitialized();
        SetCurrentContext();
        float normalized = NormalizeScale(scale);
        float ratio = normalized / _appliedUiScale;
        if (MathF.Abs(ratio - 1f) > 0.0001f)
        {
            ImGui.GetStyle().ScaleAllSizes(ratio);
            _appliedUiScale = normalized;
        }

        ImGui.GetStyle().FontScaleMain = normalized / _fontAtlasScale;
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
    public void DrawDockSpace()
    {
        ThrowIfNotInitialized();
        SetCurrentContext();
        _dockSpace.Draw();
    }

    /// <inheritdoc />
    public void ResetDockLayout()
    {
        ThrowIfNotInitialized();
        SetCurrentContext();
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
        SetCurrentContext();
        ImGui.Render();
        ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
        TryAutoSaveLayout();
    }

    /// <inheritdoc />
    public void AddMousePosition(float x, float y)
    {
        if (!_initialized)
        {
            return;
        }

        SetCurrentContext();
        System.Numerics.Vector2 mapped = _frameMetrics.MapMousePosition(x, y);
        ImGui.AddMousePosEvent(ImGui.GetIO(), mapped.X, mapped.Y);
    }

    /// <inheritdoc />
    public void AddFramebufferMousePosition(float x, float y)
    {
        if (!_initialized)
        {
            return;
        }

        SetCurrentContext();
        ImGui.AddMousePosEvent(ImGui.GetIO(), x, y);
    }

    /// <inheritdoc />
    public void AddMouseButton(int button, bool down)
    {
        if (!_initialized)
        {
            return;
        }

        SetCurrentContext();
        ImGui.AddMouseButtonEvent(ImGui.GetIO(), button, down);
    }

    /// <inheritdoc />
    public void AddMouseWheel(float wheelX, float wheelY)
    {
        if (!_initialized)
        {
            return;
        }

        SetCurrentContext();
        ImGui.AddMouseWheelEvent(ImGui.GetIO(), wheelX, wheelY);
    }

    /// <inheritdoc />
    public void AddKey(ImGuiKey key, bool down)
    {
        if (!_initialized)
        {
            return;
        }

        SetCurrentContext();
        ImGui.AddKeyEvent(ImGui.GetIO(), key, down);
    }

    /// <inheritdoc />
    public void AddText(string text)
    {
        if (!_initialized || string.IsNullOrEmpty(text))
        {
            return;
        }

        SetCurrentContext();
        ImGui.AddInputCharactersUTF8(ImGui.GetIO(), text);
    }

    /// <inheritdoc />
    public void SetLayoutPersistence(bool enabled)
    {
        _saveLayoutOnShutdown = enabled;
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
        ImPlot.DestroyContext(_plotContext);
        if (_saveLayoutOnShutdown && !string.IsNullOrWhiteSpace(_layoutPath))
        {
            ImGui.SaveIniSettingsToDisk(_layoutPath);
        }

        ImGui.DestroyContext(_context);
        _fontManager.Dispose();
        _clipboard.Dispose();
        _context = default;
        _plotContext = default;
        _layoutPath = string.Empty;
        _frameMetrics = ImGuiFrameMetrics.Create(1, 1, 1f, 1f);
        _fontAtlasScale = 1f;
        _appliedUiScale = 1f;
        _lastLayoutSaveTimestamp = 0;
        _initialized = false;
    }

    private void AddConfiguredFont(ImGuiIOPtr io, EditorAppOptions options)
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
        ImGuizmo.SetImGuiContext(_context);
        ImPlot.SetImGuiContext(_context);
        ImPlot.SetCurrentContext(_plotContext);
    }

    private void TryAutoSaveLayout()
    {
        if (!_saveLayoutOnShutdown || string.IsNullOrWhiteSpace(_layoutPath))
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        long now = Stopwatch.GetTimestamp();
        if (!io.WantSaveIniSettings ||
            (_lastLayoutSaveTimestamp != 0 && Stopwatch.GetElapsedTime(_lastLayoutSaveTimestamp, now) < TimeSpan.FromSeconds(2)))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(_layoutPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        ImGui.SaveIniSettingsToDisk(_layoutPath);
        io.WantSaveIniSettings = false;
        _lastLayoutSaveTimestamp = now;
    }

    private static float NormalizeScale(float scale)
    {
        return float.IsFinite(scale) ? Math.Clamp(scale, 0.75f, 2f) : 1f;
    }

}
