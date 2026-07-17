using Hexa.NET.ImGui;
using PixelEngine.Gui;
using Silk.NET.Input;
using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// Gui 应用组合帧测试：UI 与世界渲染合帧顺序。
/// </summary>
public sealed class GuiAppCombinedFrameTests
{
    /// <summary>
    /// 绝对 style 缩放必须消除相邻倍率反复截断，且始终满足 ImGui 的窗口边框命中宽度约束。
    /// </summary>
    [Fact]
    public void ImGuiStyleScaleStateRestoresBaselineAcrossFineGrainedScaleChanges()
    {
        ImGuiContextPtr context = ImGui.CreateContext();
        try
        {
            ImGui.SetCurrentContext(context);
            GuiTheme.ApplyCurrent(GuiThemeKind.Unity6Dark);
            ImGuiStyleScaleState state = new();
            state.CaptureCurrent();

            for (int percent = 200; percent >= 75; percent -= 5)
            {
                state.Apply(percent / 100f);
                Assert.True(ImGui.GetStyle().WindowBorderHoverPadding > 0f);
            }

            ImGui.GetStyle().WindowBorderHoverPadding = 0f;
            state.Apply(1.5f);

            Assert.Equal(1.5f, state.AppliedScale);
            Assert.Equal(6f, ImGui.GetStyle().WindowBorderHoverPadding);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    /// <summary>
    /// 验证Draw Combined Frame Runs Managed Then Script In Single Frame。
    /// </summary>
    [Fact]
    public void DrawCombinedFrameRunsManagedThenScriptInSingleFrame()
    {
        List<string> calls = [];
        FakeGuiBackend backend = new(calls);
        using GuiApp app = new(backend, new GuiAppOptions());
        app.Initialize();

        app.DrawCombinedFrame(
            1f / 60f,
            800,
            600,
            _ => calls.Add("managed"),
            _ => calls.Add("script"),
            framebufferScaleX: 2f,
            framebufferScaleY: 1.5f);

        Assert.Equal(["initialize", "new:800x600@2x1.5", "managed", "script", "render"], calls);
    }

    /// <summary>
    /// 验证组合帧会在 Managed UI 与脚本之间、以及连续帧之间复用同一个 GUI 上下文。
    /// </summary>
    [Fact]
    public void DrawCombinedFrameReusesGuiContextAcrossCallbacksAndFrames()
    {
        List<string> calls = [];
        FakeGuiBackend backend = new(calls);
        using GuiApp app = new(backend, new GuiAppOptions());
        app.Initialize();
        IGuiDrawContext? firstManaged = null;
        PixelEngine.Scripting.IGuiContext? firstScript = null;
        PixelEngine.Scripting.IGuiContext? secondScript = null;
        int firstWidth = 0;
        int secondWidth = 0;

        app.DrawCombinedFrame(
            1f / 60f,
            800,
            600,
            gui => firstManaged = gui,
            gui =>
            {
                firstScript = gui;
                firstWidth = gui.Width;
            });
        app.DrawCombinedFrame(
            1f / 120f,
            1280,
            720,
            drawManagedGui: null,
            gui =>
            {
                secondScript = gui;
                secondWidth = gui.Width;
            });

        Assert.Same(firstManaged, firstScript);
        Assert.Same(firstScript, secondScript);
        Assert.Equal(800, firstWidth);
        Assert.Equal(1280, secondWidth);
    }

    /// <summary>
    /// 完整 Canvas scale 作用域同时缩放字体与布局 token，并在弹栈后恢复全局 Editor 样式。
    /// </summary>
    [Fact]
    public void ScriptGuiContextCanvasScaleRestoresFontAndStyleState()
    {
        ImGuiContextPtr context = ImGui.CreateContext();
        try
        {
            ImGui.SetCurrentContext(context);
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new System.Numerics.Vector2(800f, 600f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            ImGui.NewFrame();
            ScriptGuiContext gui = new(800, 600, 1f / 60f, default);
            ImGuiStylePtr style = ImGui.GetStyle();
            float originalFontSize = ImGui.GetFontSize();
            System.Numerics.Vector2 originalWindowPadding = style.WindowPadding;
            System.Numerics.Vector2 originalFramePadding = style.FramePadding;
            float originalScrollbarSize = style.ScrollbarSize;

            gui.PushCanvasScale(2f);

            Assert.Equal(originalFontSize * 2f, ImGui.GetFontSize());
            Assert.Equal(originalWindowPadding * 2f, style.WindowPadding);
            Assert.Equal(originalFramePadding * 2f, style.FramePadding);
            Assert.Equal(originalScrollbarSize * 2f, style.ScrollbarSize);

            gui.PopCanvasScale();

            Assert.Equal(originalFontSize, ImGui.GetFontSize());
            Assert.Equal(originalWindowPadding, style.WindowPadding);
            Assert.Equal(originalFramePadding, style.FramePadding);
            Assert.Equal(originalScrollbarSize, style.ScrollbarSize);
            ImGui.EndFrame();
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    /// <summary>
    /// 验证Gui Input Bridge Publishes Modifier Keys For Clipboard Shortcuts。
    /// </summary>
    [Fact]
    public void GuiInputBridgePublishesModifierKeysForClipboardShortcuts()
    {
        List<string> calls = [];
        FakeGuiBackend backend = new(calls);
        GuiInputBridge input = new(backend);

        input.Key(Key.ControlLeft, down: true);
        input.Key(Key.V, down: true);
        input.Key(Key.V, down: false);
        input.Key(Key.Comma, down: true);
        input.Key(Key.Comma, down: false);
        input.Key(Key.ControlLeft, down: false);

        Assert.Equal(
            [
                "key:LeftCtrl=True",
                "key:ModCtrl=True",
                "key:V=True",
                "key:V=False",
                "key:Comma=True",
                "key:Comma=False",
                "key:LeftCtrl=False",
                "key:ModCtrl=False",
            ],
            calls);
    }

    /// <summary>
    /// 验证窗口失焦会清空修饰键聚合状态，并把焦点事件按顺序交给 ImGui。
    /// </summary>
    [Fact]
    public void GuiInputBridgeClearsModifierAggregationWhenWindowLosesFocus()
    {
        List<string> calls = [];
        FakeGuiBackend backend = new(calls);
        GuiInputBridge input = new(backend);

        input.Key(Key.ControlLeft, down: true);
        input.Focus(focused: false);
        input.Key(Key.ControlRight, down: true);
        input.Focus(focused: true);

        Assert.Equal(
            [
                "key:LeftCtrl=True",
                "key:ModCtrl=True",
                "focus:False",
                "key:RightCtrl=True",
                "key:ModCtrl=True",
                "focus:True",
            ],
            calls);
    }

    private sealed class FakeGuiBackend(List<string> calls) : IGuiImGuiBackend
    {
        private readonly List<string> _calls = calls;

        public GuiInputSnapshot Capture => default;

        public void Initialize(GuiAppOptions options)
        {
            _calls.Add("initialize");
        }

        public void SetUiScale(float scale)
        {
            _ = scale;
        }

        public void NewFrame(float deltaSeconds, int width, int height, float framebufferScaleX, float framebufferScaleY)
        {
            _calls.Add($"new:{width}x{height}@{framebufferScaleX}x{framebufferScaleY}");
        }

        public void Render()
        {
            _calls.Add("render");
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
            _calls.Add($"key:{key}={down}");
        }

        public void AddText(string text)
        {
        }

        public void AddFocus(bool focused)
        {
            _calls.Add($"focus:{focused}");
        }

        public void SetLayoutPersistence(bool enabled)
        {
            _calls.Add($"layout:{enabled}");
        }

        public void Shutdown()
        {
        }
    }
}
