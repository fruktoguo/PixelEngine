using Hexa.NET.ImGui;
using PixelEngine.Rendering;
using System.Diagnostics;

namespace PixelEngine.Gui;

/// <summary>
/// 基于 Hexa.NET.ImGui 与引擎 Silk GL renderer 的中性 ImGui 后端。
/// </summary>
/// <param name="window">提供当前 GL context 的渲染窗口。</param>
public sealed class HexaImGuiBackend(RenderWindow window) : IGuiImGuiBackend
{
    private readonly ImGuiGlRenderer _renderer = new(window ?? throw new ArgumentNullException(nameof(window)));
    private readonly ImGuiPlatformBridge _platform = new(window);
    private readonly GuiFontManager _fontManager = new();
    private readonly GuiClipboardBridge _clipboard = new();
    private readonly ImGuiMouseReleaseScheduler _mouseReleaseScheduler = new();
    private ImGuiContextPtr _context;
    private string _layoutPath = string.Empty;
    private ImGuiFrameMetrics _frameMetrics = ImGuiFrameMetrics.Create(1, 1, 1f, 1f);
    private bool _saveLayoutOnShutdown = true;
    private float _fontAtlasScale = 1f;
    private float _appliedUiScale = 1f;
    private long _lastLayoutSaveTimestamp;
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
    public unsafe void Initialize(GuiAppOptions options)
    {
        options = options.Normalize();
        if (_initialized)
        {
            throw new InvalidOperationException("ImGui 后端已经初始化。");
        }

        // 创建独立 ImGui context，与 Editor/Game 共享同一 OpenGL context 但隔离 ini 与字体 atlas。
        _context = ImGui.CreateContext();
        _layoutPath = options.LayoutPath;
        SetCurrentContext();
        ImGuiIOPtr io = ImGui.GetIO();
        io.IniFilename = null;
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        _clipboard.Attach();
        if (options.EnableMultiViewport)
        {
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
        }

        GuiTheme.ApplyCurrent(options.Theme);
        AddConfiguredFont(io, options);
        _fontAtlasScale = NormalizeScale(options.DpiScale);
        _appliedUiScale = _fontAtlasScale;
        if (MathF.Abs(_appliedUiScale - 1f) > 0.0001f)
        {
            ImGui.GetStyle().ScaleAllSizes(_appliedUiScale);
        }

        ImGui.GetStyle().FontScaleMain = 1f;
        if (File.Exists(_layoutPath))
        {
            ImGui.LoadIniSettingsFromDisk(_layoutPath);
        }

        _renderer.Initialize();
        _platform.Attach();
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
        FlushScheduledMouseReleases(io);
        _platform.NewFrame();
        _renderer.NewFrame();
        ImGui.NewFrame();
    }

    /// <inheritdoc />
    public void Render()
    {
        ThrowIfNotInitialized();
        SetCurrentContext();
        ImGui.Render();
        _renderer.Render(ImGui.GetDrawData());
        TryAutoSaveLayout();
    }

    /// <inheritdoc />
    public void AddMousePosition(float x, float y)
    {
        if (_initialized)
        {
            SetCurrentContext();
            System.Numerics.Vector2 mapped = _frameMetrics.MapMousePosition(x, y);
            _mouseReleaseScheduler.RecordPosition(mapped);
            ImGui.AddMousePosEvent(ImGui.GetIO(), mapped.X, mapped.Y);
        }
    }

    /// <inheritdoc />
    public void AddFramebufferMousePosition(float x, float y)
    {
        if (_initialized)
        {
            SetCurrentContext();
            _mouseReleaseScheduler.RecordPosition(new System.Numerics.Vector2(x, y));
            ImGui.AddMousePosEvent(ImGui.GetIO(), x, y);
        }
    }

    /// <inheritdoc />
    public void AddMouseButton(int button, bool down)
    {
        if (_initialized)
        {
            SetCurrentContext();
            bool emitButtonEvent = _mouseReleaseScheduler.ShouldEmitButtonEvent(
                button,
                down,
                out bool releaseBeforeEvent);
            if (releaseBeforeEvent)
            {
                ImGui.AddMouseButtonEvent(ImGui.GetIO(), button, false);
            }

            if (emitButtonEvent)
            {
                ImGui.AddMouseButtonEvent(ImGui.GetIO(), button, down);
            }
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
    public void AddFocus(bool focused)
    {
        if (_initialized)
        {
            SetCurrentContext();
            if (!focused)
            {
                FlushMouseReleasesForFocusLoss(ImGui.GetIO());
            }

            _platform.SetWindowFocused(focused);
            ImGui.AddFocusEvent(ImGui.GetIO(), focused);
        }
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
        _renderer.Shutdown();
        _platform.Dispose();
        _clipboard.Detach();
        if (_saveLayoutOnShutdown && !string.IsNullOrWhiteSpace(_layoutPath))
        {
            ImGui.SaveIniSettingsToDisk(_layoutPath);
        }

        ImGui.DestroyContext(_context);
        _fontManager.Dispose();
        _clipboard.Dispose();
        _context = default;
        _layoutPath = string.Empty;
        _frameMetrics = ImGuiFrameMetrics.Create(1, 1, 1f, 1f);
        _fontAtlasScale = 1f;
        _appliedUiScale = 1f;
        _lastLayoutSaveTimestamp = 0;
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

    private void FlushScheduledMouseReleases(ImGuiIOPtr io)
    {
        Span<int> buttons = stackalloc int[ImGuiMouseReleaseScheduler.MaximumMouseButtons];
        int count = _mouseReleaseScheduler.BeginFrame(buttons);
        for (int i = 0; i < count; i++)
        {
            ImGui.AddMouseButtonEvent(io, buttons[i], false);
        }
    }

    private void FlushMouseReleasesForFocusLoss(ImGuiIOPtr io)
    {
        Span<int> buttons = stackalloc int[ImGuiMouseReleaseScheduler.MaximumMouseButtons];
        int count = _mouseReleaseScheduler.FlushForFocusLoss(buttons);
        for (int i = 0; i < count; i++)
        {
            ImGui.AddMouseButtonEvent(io, buttons[i], false);
        }
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
