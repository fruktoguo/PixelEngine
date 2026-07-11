using Hexa.NET.ImGui;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Gui;
using PixelEngine.Rendering;
using Silk.NET.Input;
using System.Numerics;
using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// Editor 框架契约测试。
/// 不变式：Editor 框架服务解析与模式切换契约稳定。
/// </summary>
public sealed class EditorAppTests
{
    /// <summary>
    /// 验证禁用开关不会初始化 ImGui 后端，也不会绘制面板。
    /// </summary>
    [Fact]
    public void DisabledEditorDoesNotTouchBackendOrPanels()
    {
        // Arrange：准备输入与初始状态
        RecordingBackend backend = new();
        RecordingPanel panel = new();
        using EditorApp app = new(backend, new EditorAppOptions { Enabled = false });
        app.AddPanel(panel);

        app.Initialize();
        app.DrawFrame(1f / 60f, 1280, 720, new EngineCounters(), 3);

        // Assert：验证预期结果
        Assert.False(app.IsRunning);
        Assert.Equal(0, backend.InitializeCount);
        Assert.Equal(0, backend.RenderCount);
        Assert.Equal(0, panel.DrawCount);
    }

    /// <summary>
    /// 验证启用时按 NewFrame → Chrome → DockSpace → Panel → Render 顺序调度。
    /// </summary>
    [Fact]
    public void DrawFrameDispatchesDockSpacePanelsAndRenderInOrder()
    {
        RecordingBackend backend = new();
        RecordingChromePanel chrome = new();
        RecordingPanel panel = new();
        using EditorApp app = new(backend, new EditorAppOptions());
        app.AddPanel(chrome);
        app.AddPanel(panel);

        app.Initialize();
        app.DrawFrame(0.016f, 800, 600, new EngineCounters(), 42, framebufferScaleX: 2f, framebufferScaleY: 1.5f);

        Assert.True(app.IsRunning);
        Assert.Equal(["Initialize", "NewFrame:800x600@2x1.5", "Chrome:42", "DockSpace", "Panel:42", "Render"], backend.Events);
        Assert.Equal(1, chrome.DrawCount);
        Assert.Equal(1, panel.DrawCount);
    }

    /// <summary>
    /// 验证游戏 HUD 模式复用 EditorApp 的 ImGui frame，但不绘制 dockspace 与 Editor 面板。
    /// </summary>
    [Fact]
    public void DrawFrameCanDispatchScriptGuiWithoutDockSpace()
    {
        // Arrange：准备输入与初始状态
        RecordingBackend backend = new();
        RecordingPanel panel = new();
        using EditorApp app = new(backend, new EditorAppOptions { EnableDockSpace = false });
        app.AddPanel(panel);
        int guiWidth = 0;
        int guiHeight = 0;

        app.Initialize();
        app.DrawFrame(
            0.016f,
            640,
            360,
            new EngineCounters(),
            7,
            EditorPerformanceSnapshot.FromCounters(new EngineCounters()),
            gui =>
            {
                guiWidth = gui.Width;
                guiHeight = gui.Height;
                RecordingBackend.Current?.Events.Add("ScriptGui");
            });

        // Assert：验证预期结果
        Assert.Equal(640, guiWidth);
        Assert.Equal(360, guiHeight);
        Assert.Equal(["Initialize", "NewFrame:640x360@1x1", "ScriptGui", "Render"], backend.Events);
        Assert.Equal(0, panel.DrawCount);
    }

    /// <summary>
    /// 验证 Editor Play 模式连续帧复用脚本 GUI 上下文并刷新尺寸快照。
    /// </summary>
    [Fact]
    public void DrawFrameReusesScriptGuiContextAcrossFrames()
    {
        RecordingBackend backend = new();
        using EditorApp app = new(backend, new EditorAppOptions { EnableDockSpace = false });
        object? first = null;
        object? second = null;
        int secondWidth = 0;
        app.Initialize();

        app.DrawFrame(
            0.016f,
            640,
            360,
            new EngineCounters(),
            1,
            EditorPerformanceSnapshot.FromCounters(new EngineCounters()),
            gui => first = gui);
        app.DrawFrame(
            0.008f,
            1280,
            720,
            new EngineCounters(),
            2,
            EditorPerformanceSnapshot.FromCounters(new EngineCounters()),
            gui =>
            {
                second = gui;
                secondWidth = gui.Width;
            });

        Assert.Same(first, second);
        Assert.Equal(1280, secondWidth);
    }

    /// <summary>
    /// 验证 EditorApp 可按标题重新显示已注册面板，未知标题不会伪造成功。
    /// </summary>
    [Fact]
    public void TryShowPanelSetsRegisteredPanelVisible()
    {
        using EditorApp app = new(new RecordingBackend(), new EditorAppOptions());
        RecordingPanel panel = new() { Visible = false };
        app.AddPanel(panel);

        Assert.True(app.TryShowPanel(panel.Title));
        Assert.True(panel.Visible);
        Assert.False(app.TryShowPanel("missing"));

        panel.Visible = false;
        Assert.Equal(1, app.ShowAllPanels());
        Assert.True(panel.Visible);
    }

    /// <summary>
    /// 重置布局恢复各面板注册时的默认可见性，不再把全部工具窗口强制展开。
    /// </summary>
    [Fact]
    public void ResetDockLayoutRestoresRegisteredDefaultVisibility()
    {
        RecordingBackend backend = new();
        using EditorApp app = new(backend, new EditorAppOptions());
        RecordingPanel corePanel = new() { Visible = true };
        RecordingPanel utilityPanel = new() { Visible = false };
        app.AddPanel(corePanel);
        app.AddPanel(utilityPanel);
        app.Initialize();
        corePanel.Visible = false;
        utilityPanel.Visible = true;

        app.ResetDockLayout();

        Assert.True(corePanel.Visible);
        Assert.False(utilityPanel.Visible);
        Assert.Contains("ResetDockLayout", backend.Events);
    }

    /// <summary>
    /// 验证 ImGuiController 在禁用时不初始化后端，启用时能成对初始化与关闭。
    /// </summary>
    [Fact]
    public void ImGuiControllerHonorsEnabledFlagAndShutdown()
    {
        // Arrange：准备输入与初始状态
        RecordingBackend disabledBackend = new();
        ImGuiController disabled = new(disabledBackend, new EditorAppOptions { Enabled = false });

        disabled.Initialize();

        // Assert：验证预期结果
        Assert.False(disabled.IsInitialized);
        Assert.Equal(0, disabledBackend.InitializeCount);

        RecordingBackend enabledBackend = new();
        ImGuiController enabled = new(enabledBackend, new EditorAppOptions());

        enabled.Initialize();
        enabled.SetLayoutPersistence(false);
        enabled.Shutdown();

        Assert.False(enabled.IsInitialized);
        Assert.Equal(["Initialize", "LayoutPersistence:False", "Shutdown"], enabledBackend.Events);
    }

    /// <summary>
    /// 验证输入捕获快照会阻止世界侧消费对应输入。
    /// </summary>
    [Fact]
    public void InputCaptureArbitratesWorldInput()
    {
        EditorInputSnapshot capture = new(WantCaptureMouse: true, WantCaptureKeyboard: false);

        Assert.False(capture.AllowWorldMouse);
        Assert.True(capture.AllowWorldKeyboard);
    }

    /// <summary>
    /// 验证编辑器输入桥会为 Ctrl+V/C/A 这类文本编辑快捷键同步 ImGui 聚合修饰键。
    /// </summary>
    [Fact]
    public void EditorInputBridgePublishesModifierKeysForClipboardShortcuts()
    {
        // Arrange：准备输入与初始状态
        RecordingBackend backend = new();
        ImGuiInputBridge input = new(backend);

        input.Key(Key.ControlLeft, down: true);
        input.Key(Key.V, down: true);
        input.Key(Key.V, down: false);
        input.Key(Key.Comma, down: true);
        input.Key(Key.Comma, down: false);
        input.Key(Key.ControlLeft, down: false);

        // Assert：验证预期结果
        Assert.Equal(
            [
                "Key:LeftCtrl=True",
                "Key:ModCtrl=True",
                "Key:V=True",
                "Key:V=False",
                "Key:Comma=True",
                "Key:Comma=False",
                "Key:LeftCtrl=False",
                "Key:ModCtrl=False",
            ],
            backend.Events);
    }

    /// <summary>
    /// 验证 docking flags 默认启用 docking 与键盘导航，但不启用多视口。
    /// </summary>
    [Fact]
    public void DockSpaceConfigEnablesDockingWithoutMultiViewportByDefault()
    {
        ImGuiConfigFlags flags = EditorDockSpace.BuildConfigFlags(enableMultiViewport: false);

        Assert.True((flags & ImGuiConfigFlags.DockingEnable) != 0);
        Assert.True((flags & ImGuiConfigFlags.NavEnableKeyboard) != 0);
        Assert.False((flags & ImGuiConfigFlags.ViewportsEnable) != 0);
    }

    /// <summary>
    /// 验证默认 docking 布局注册了世界视口与主要工具窗口。
    /// </summary>
    [Fact]
    public void DockSpaceDefaultLayoutDeclaresViewportAndToolWindows()
    {
        string[] titles = EditorDockSpace.GetDefaultWindowTitles().ToArray();

        Assert.Contains(EditorDockSpace.ViewportWindowTitle, titles);
        Assert.Contains(EditorDockSpace.GameViewWindowTitle, titles);
        Assert.Contains(EditorDockSpace.SceneHierarchyWindowTitle, titles);
        Assert.Contains(EditorDockSpace.AssetBrowserWindowTitle, titles);
        Assert.Contains(EditorDockSpace.InspectorWindowTitle, titles);
        Assert.Contains(EditorDockSpace.PerformanceHudWindowTitle, titles);
        Assert.Equal("Scene", EditorDockSpace.ViewportWindowTitle);
        Assert.Equal("Game View", EditorDockSpace.GameViewWindowTitle);
        Assert.Equal("Hierarchy", EditorDockSpace.SceneHierarchyWindowTitle);
        Assert.Equal("Project", EditorDockSpace.AssetBrowserWindowTitle);
        Assert.Equal("Console", EditorDockSpace.ConsoleDiagnosticsWindowTitle);
        Assert.Equal("Profiler", EditorDockSpace.PerformanceHudWindowTitle);
    }

    /// <summary>
    /// 验证 Editor 默认主题使用 Unity 6 深灰 token，而玩家侧中性 GUI 仍保留 neutral 默认。
    /// </summary>
    [Fact]
    public void EditorDefaultsToUnity6DarkThemeWithoutChangingNeutralGuiDefault()
    {
        EditorAppOptions editorOptions = new();
        GuiAppOptions guiOptions = new();
        GuiThemeTokens tokens = GuiTheme.GetTokens(editorOptions.Theme);

        Assert.Equal(GuiThemeKind.Unity6Dark, editorOptions.Theme);
        Assert.Equal(GuiThemeKind.NeutralDark, guiOptions.Theme);
        Assert.Equal("PixelEngine Modern Dark", tokens.Name);
        Assert.Equal(4f, tokens.WindowRounding);
        Assert.Equal(4f, tokens.TabRounding);
        Assert.Equal(new Vector4(0x1E / 255f, 0x1F / 255f, 0x22 / 255f, 1f), tokens.WindowBg);
        Assert.Equal(new Vector4(0x4C / 255f, 0x8D / 255f, 1f, 1f), tokens.Accent);
    }

    /// <summary>
    /// 验证字体管理器能解析本机可用 CJK 字体并正确应用 DPI 缩放。
    /// </summary>
    [Fact]
    public void FontManagerResolvesCjkFontAndScalesSize()
    {
        GuiFontManager manager = new();
        string? fontPath = manager.ResolveCjkFontPath();

        Assert.False(string.IsNullOrWhiteSpace(fontPath));
        Assert.True(File.Exists(fontPath));
        Assert.Equal(27f, GuiFontManager.ScaleFontSize(18f, 1.5f));
    }

    /// <summary>
    /// 验证世界视口面板按可用区域等比适配 Rendering 纹理。
    /// </summary>
    [Fact]
    public void ViewportPanelFitsRenderTextureInsideAvailableRegion()
    {
        ViewportPanel panel = new(() => new RenderViewportTexture(12, 320, 180));

        Vector2 fitted = ViewportPanel.FitTexture(320, 180, new Vector2(160, 160));

        Assert.Equal("Scene", panel.Title);
        Assert.Equal(160f, fitted.X);
        Assert.Equal(90f, fitted.Y);
    }

    /// <summary>
    /// 验证高 DPI ImGui 帧对 Hexa OpenGL 后端使用 framebuffer 尺寸，避免 viewport 只覆盖左下 1/4。
    /// </summary>
    [Fact]
    public void ImGuiFrameMetricsUsesFramebufferDisplaySizeForHexaBackend()
    {
        ImGuiFrameMetrics metrics = ImGuiFrameMetrics.Create(640, 360, 2f, 2f);

        Assert.Equal(640, metrics.LogicalWidth);
        Assert.Equal(360, metrics.LogicalHeight);
        Assert.Equal(1280, metrics.FramebufferWidth);
        Assert.Equal(720, metrics.FramebufferHeight);
        Assert.Equal(new Vector2(1280f, 720f), metrics.DisplaySize);
        Assert.Equal(Vector2.One, metrics.DisplayFramebufferScale);
        Assert.Equal(new Vector2(200f, 100f), metrics.MapMousePosition(100f, 50f));
    }

    /// <summary>
    /// 验证非法 framebuffer scale 会退回 1，避免 DPI 查询异常污染 ImGui 投影。
    /// </summary>
    [Fact]
    public void ImGuiFrameMetricsNormalizesInvalidFramebufferScale()
    {
        ImGuiFrameMetrics metrics = ImGuiFrameMetrics.Create(0, 0, 0f, float.NaN);

        Assert.Equal(new Vector2(1f, 1f), metrics.DisplaySize);
        Assert.Equal(new Vector2(1f, 1f), metrics.DisplayFramebufferScale);
    }

    private sealed class RecordingPanel : IEditorPanel
    {
        public string Title => "录制面板";

        public bool Visible { get; set; } = true;

        public int DrawCount { get; private set; }

        public void Draw(in EditorContext context)
        {
            DrawCount++;
            RecordingBackend.Current?.Events.Add($"Panel:{context.FrameIndex}");
        }
    }

    private sealed class RecordingChromePanel : IEditorChromePanel
    {
        public string Title => "录制工具栏";

        public bool Visible { get; set; } = true;

        public int DrawCount { get; private set; }

        public void Draw(in EditorContext context)
        {
            DrawCount++;
            RecordingBackend.Current?.Events.Add($"Chrome:{context.FrameIndex}");
        }
    }

    private sealed class RecordingBackend : IEditorImGuiBackend
    {
        [ThreadStatic]
        public static RecordingBackend? Current;

        public List<string> Events { get; } = [];

        public int InitializeCount { get; private set; }

        public int RenderCount { get; private set; }

        public EditorInputSnapshot Capture { get; private set; }

        public void Initialize(EditorAppOptions options)
        {
            Current = this;
            InitializeCount++;
            Events.Add("Initialize");
        }

        public void SetUiScale(float scale)
        {
            _ = scale;
        }

        public void NewFrame(float deltaSeconds, int width, int height, float framebufferScaleX, float framebufferScaleY)
        {
            Events.Add($"NewFrame:{width}x{height}@{framebufferScaleX}x{framebufferScaleY}");
        }

        public void DrawDockSpace()
        {
            Events.Add("DockSpace");
        }

        public void Render()
        {
            RenderCount++;
            Events.Add("Render");
        }

        public void ResetDockLayout()
        {
            Events.Add("ResetDockLayout");
        }

        public void AddMousePosition(float x, float y)
        {
        }

        public void AddFramebufferMousePosition(float x, float y)
        {
        }

        public void AddMouseButton(int button, bool down)
        {
        }

        public void AddMouseWheel(float wheelX, float wheelY)
        {
        }

        public void AddKey(ImGuiKey key, bool down)
        {
            Events.Add($"Key:{key}={down}");
        }

        public void AddText(string text)
        {
        }

        public void SetLayoutPersistence(bool enabled)
        {
            Events.Add($"LayoutPersistence:{enabled}");
        }

        public void Shutdown()
        {
            Events.Add("Shutdown");
            Current = null;
        }
    }
}
