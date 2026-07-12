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
