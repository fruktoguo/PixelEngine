using Hexa.NET.ImGui;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Rendering;
using System.Numerics;
using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// Editor 框架契约测试。
/// </summary>
public sealed class EditorAppTests
{
    /// <summary>
    /// 验证禁用开关不会初始化 ImGui 后端，也不会绘制面板。
    /// </summary>
    [Fact]
    public void DisabledEditorDoesNotTouchBackendOrPanels()
    {
        RecordingBackend backend = new();
        RecordingPanel panel = new();
        using EditorApp app = new(backend, new EditorAppOptions { Enabled = false });
        app.AddPanel(panel);

        app.Initialize();
        app.DrawFrame(1f / 60f, 1280, 720, new EngineCounters(), 3);

        Assert.False(app.IsRunning);
        Assert.Equal(0, backend.InitializeCount);
        Assert.Equal(0, backend.RenderCount);
        Assert.Equal(0, panel.DrawCount);
    }

    /// <summary>
    /// 验证启用时按 NewFrame → DockSpace → Panel → Render 顺序调度。
    /// </summary>
    [Fact]
    public void DrawFrameDispatchesDockSpacePanelsAndRenderInOrder()
    {
        RecordingBackend backend = new();
        RecordingPanel panel = new();
        using EditorApp app = new(backend, new EditorAppOptions());
        app.AddPanel(panel);

        app.Initialize();
        app.DrawFrame(0.016f, 800, 600, new EngineCounters(), 42);

        Assert.True(app.IsRunning);
        Assert.Equal(["Initialize", "NewFrame:800x600", "DockSpace", "Panel:42", "Render"], backend.Events);
        Assert.Equal(1, panel.DrawCount);
    }

    /// <summary>
    /// 验证游戏 HUD 模式复用 EditorApp 的 ImGui frame，但不绘制 dockspace 与 Editor 面板。
    /// </summary>
    [Fact]
    public void DrawFrameCanDispatchScriptGuiWithoutDockSpace()
    {
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

        Assert.Equal(640, guiWidth);
        Assert.Equal(360, guiHeight);
        Assert.Equal(["Initialize", "NewFrame:640x360", "ScriptGui", "Render"], backend.Events);
        Assert.Equal(0, panel.DrawCount);
    }

    /// <summary>
    /// 验证 ImGuiController 在禁用时不初始化后端，启用时能成对初始化与关闭。
    /// </summary>
    [Fact]
    public void ImGuiControllerHonorsEnabledFlagAndShutdown()
    {
        RecordingBackend disabledBackend = new();
        ImGuiController disabled = new(disabledBackend, new EditorAppOptions { Enabled = false });

        disabled.Initialize();

        Assert.False(disabled.IsInitialized);
        Assert.Equal(0, disabledBackend.InitializeCount);

        RecordingBackend enabledBackend = new();
        ImGuiController enabled = new(enabledBackend, new EditorAppOptions());

        enabled.Initialize();
        enabled.Shutdown();

        Assert.False(enabled.IsInitialized);
        Assert.Equal(["Initialize", "Shutdown"], enabledBackend.Events);
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
        Assert.Contains(EditorDockSpace.SceneHierarchyWindowTitle, titles);
        Assert.Contains(EditorDockSpace.InspectorWindowTitle, titles);
        Assert.Contains(EditorDockSpace.PerformanceHudWindowTitle, titles);
    }

    /// <summary>
    /// 验证字体管理器能解析本机可用 CJK 字体并正确应用 DPI 缩放。
    /// </summary>
    [Fact]
    public void FontManagerResolvesCjkFontAndScalesSize()
    {
        EditorFontManager manager = new();
        string? fontPath = manager.ResolveCjkFontPath();

        Assert.False(string.IsNullOrWhiteSpace(fontPath));
        Assert.True(File.Exists(fontPath));
        Assert.Equal(27f, EditorFontManager.ScaleFontSize(18f, 1.5f));
    }

    /// <summary>
    /// 验证世界视口面板按可用区域等比适配 Rendering 纹理。
    /// </summary>
    [Fact]
    public void ViewportPanelFitsRenderTextureInsideAvailableRegion()
    {
        ViewportPanel panel = new(() => new RenderViewportTexture(12, 320, 180));

        Vector2 fitted = ViewportPanel.FitTexture(320, 180, new Vector2(160, 160));

        Assert.Equal("世界视口", panel.Title);
        Assert.Equal(160f, fitted.X);
        Assert.Equal(90f, fitted.Y);
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

        public void NewFrame(float deltaSeconds, int width, int height)
        {
            Events.Add($"NewFrame:{width}x{height}");
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

        public void AddMousePosition(float x, float y)
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
        }

        public void AddText(string text)
        {
        }

        public void Shutdown()
        {
            Events.Add("Shutdown");
            Current = null;
        }
    }
}
