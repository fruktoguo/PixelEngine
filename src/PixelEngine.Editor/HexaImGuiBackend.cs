using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Hexa.NET.ImPlot;
using PixelEngine.Gui;
using PixelEngine.Rendering;
using System.Diagnostics;

namespace PixelEngine.Editor;

/// <summary>
/// 基于 Hexa.NET.ImGui 与引擎 Silk GL renderer 的真实 ImGui 后端。
/// </summary>
/// <param name="window">提供当前 GL context 的渲染窗口。</param>
public sealed class HexaImGuiBackend(RenderWindow window) : IEditorImGuiBackend
{
    private readonly ImGuiGlRenderer _renderer = new(window ?? throw new ArgumentNullException(nameof(window)));
    private readonly ImGuiPlatformBridge _platform = new(window);
    private readonly EditorDockSpace _dockSpace = new();
    private readonly GuiFontManager _fontManager = new();
    private readonly GuiClipboardBridge _clipboard = new();
    private readonly ImGuiMouseReleaseScheduler _mouseReleaseScheduler = new();
    private ImGuiContextPtr _context;
    private ImPlotContextPtr _plotContext;
    private string _layoutPath = string.Empty;
    private ImGuiFrameMetrics _frameMetrics = ImGuiFrameMetrics.Create(1, 1, 1f, 1f);
    private string? _primaryFontPath;
    private string? _cjkFallbackFontPath;
    private float _fontSizePixels = 18f;
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
        ImGuizmo.SetImGuiContext(_context);
        ImPlot.SetImGuiContext(_context);
        _plotContext = ImPlot.CreateContext();
        ImPlot.SetCurrentContext(_plotContext);
        ImGuiIOPtr io = ImGui.GetIO();
        io.IniFilename = null;
        io.ConfigFlags |= EditorDockSpace.BuildConfigFlags(options.EnableMultiViewport);
        _clipboard.Attach();
        GuiTheme.ApplyCurrent(options.Theme);
        _primaryFontPath = GuiFontManager.ResolvePrimaryFontFile(options.PrimaryFontPath);
        _cjkFallbackFontPath = _fontManager.ResolveCjkFontPath(options.CjkFallbackFontPath);
        _fontSizePixels = options.FontSizePixels;
        _fontAtlasScale = NormalizeScale(options.DpiScale);
        RebuildConfiguredFont(io, _fontAtlasScale);
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

        _renderer.Initialize();
        _platform.Attach();
        _initialized = true;
    }

    /// <inheritdoc />
    public unsafe void SetUiScale(float scale)
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

        if (MathF.Abs(normalized - _fontAtlasScale) > 0.0001f)
        {
            RebuildConfiguredFont(ImGui.GetIO(), normalized);
            _fontAtlasScale = normalized;
        }

        ImGui.GetStyle().FontScaleMain = 1f;
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
            try
            {
                File.Delete(_layoutPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Shell 入口会把删除失败写入 Console；后端仍重建内存 dock tree，
                // 避免一个被同步软件短暂占用的 ini 让 Reset Layout 崩溃。
            }
        }

        _dockSpace.ResetLayoutState(buildDefaultLayout: true);
    }

    /// <inheritdoc />
    public string CaptureDockLayout()
    {
        ThrowIfNotInitialized();
        SetCurrentContext();
        return ImGui.SaveIniSettingsToMemoryS();
    }

    /// <inheritdoc />
    public void ApplyDockLayout(string layout)
    {
        ThrowIfNotInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(layout);
        SetCurrentContext();
        ImGui.LoadIniSettingsFromMemory(layout);
        ImGui.GetIO().WantSaveIniSettings = true;
    }

    /// <inheritdoc />
    public unsafe EditorDockWindowState CaptureDockWindow(string windowTitle)
    {
        ThrowIfNotInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(windowTitle);
        SetCurrentContext();
        ImGuiWindowPtr window = ImGuiP.FindWindowByName(windowTitle);
        return window.Handle == null
            ? default
            : new EditorDockWindowState(
                Known: true,
                window.DockId,
                window.Pos.X,
                window.Pos.Y,
                window.Size.X,
                window.Size.Y);
    }

    /// <inheritdoc />
    public unsafe bool TrySetDockWindow(EditorDockWindowRequest request, out string diagnostic)
    {
        ThrowIfNotInitialized();
        ArgumentNullException.ThrowIfNull(request);
        SetCurrentContext();
        ImGuiWindowPtr source = ImGuiP.FindWindowByName(request.WindowTitle);

        if (request.Placement == EditorDockPlacement.Floating)
        {
            if (source.Handle == null)
            {
                if (request.X.HasValue || request.Width.HasValue)
                {
                    diagnostic = "尚未 Begin 的 panel 可语义 undock，但不能设置 floating rect；先显示一帧后重试。";
                    return false;
                }

                ImGuiP.DockBuilderDockWindow(request.WindowTitle, 0);
            }
            else
            {
                ImGuiP.SetWindowDock(source, 0, ImGuiCond.Always);
            }
            if (request.X is { } x && request.Y is { } y)
            {
                ImGuiP.SetWindowPos(source, new System.Numerics.Vector2(x, y), ImGuiCond.Always);
            }

            if (request.Width is { } width && request.Height is { } height)
            {
                ImGuiP.SetWindowSize(source, new System.Numerics.Vector2(width, height), ImGuiCond.Always);
            }

            ImGui.GetIO().WantSaveIniSettings = true;
            diagnostic = string.Empty;
            return true;
        }

        ImGuiWindowPtr target = ImGuiP.FindWindowByName(request.TargetWindowTitle!);
        if (target.Handle == null || target.DockId == 0)
        {
            diagnostic = "目标 panel 尚未位于可拆分的 dock node。";
            return false;
        }

        if (request.Placement == EditorDockPlacement.Tab)
        {
            ImGuiP.DockBuilderDockWindow(request.WindowTitle, target.DockId);
            ImGui.GetIO().WantSaveIniSettings = true;
            diagnostic = string.Empty;
            return true;
        }

        ImGuiDockNodePtr targetNode = ImGuiP.DockBuilderGetNode(target.DockId);
        if (targetNode.Handle == null)
        {
            diagnostic = "目标 dock node 已失效。";
            return false;
        }

        ImGuiDockNodePtr rootNode = targetNode;
        while (rootNode.ParentNode.Handle != null)
        {
            rootNode = rootNode.ParentNode;
        }

        uint rootNodeId = rootNode.ID;

        uint newNode;
        uint remainingNode;
        ImGuiDir direction = request.Placement switch
        {
            EditorDockPlacement.Left => ImGuiDir.Left,
            EditorDockPlacement.Right => ImGuiDir.Right,
            EditorDockPlacement.Top => ImGuiDir.Up,
            EditorDockPlacement.Bottom => ImGuiDir.Down,
            EditorDockPlacement.Tab or EditorDockPlacement.Floating =>
                throw new InvalidOperationException("Tab/Floating 不应进入 split 分支。"),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Placement, "未知停靠位置。"),
        };
        _ = ImGuiP.DockBuilderSplitNode(
            target.DockId,
            direction,
            request.SplitRatio,
            &newNode,
            &remainingNode);
        if (newNode == 0 || remainingNode == 0)
        {
            diagnostic = "DockBuilder 未能拆分目标 node。";
            return false;
        }

        ImGuiP.DockBuilderDockWindow(request.WindowTitle, newNode);
        ImGuiP.DockBuilderFinish(rootNodeId);
        ImGui.GetIO().WantSaveIniSettings = true;
        diagnostic = string.Empty;
        return true;
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
        if (!_initialized)
        {
            return;
        }

        SetCurrentContext();
        System.Numerics.Vector2 mapped = _frameMetrics.MapMousePosition(x, y);
        _mouseReleaseScheduler.RecordPosition(mapped);
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
        _mouseReleaseScheduler.RecordPosition(new System.Numerics.Vector2(x, y));
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
    public void AddFocus(bool focused)
    {
        if (!_initialized)
        {
            return;
        }

        SetCurrentContext();
        if (!focused)
        {
            FlushMouseReleasesForFocusLoss(ImGui.GetIO());
        }

        _platform.SetWindowFocused(focused);
        ImGui.AddFocusEvent(ImGui.GetIO(), focused);
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
        _primaryFontPath = null;
        _cjkFallbackFontPath = null;
        _fontSizePixels = 18f;
        _fontAtlasScale = 1f;
        _appliedUiScale = 1f;
        _lastLayoutSaveTimestamp = 0;
        _initialized = false;
    }

    private unsafe void RebuildConfiguredFont(ImGuiIOPtr io, float scale)
    {
        float fontSize = GuiFontManager.ScaleFontSize(_fontSizePixels, scale);
        _ = _fontManager.RebuildFontAtlas(
            io.Fonts,
            _primaryFontPath,
            _cjkFallbackFontPath,
            fontSize);
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
