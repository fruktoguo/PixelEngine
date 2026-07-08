using Hexa.NET.ImGui;
using PixelEngine.Gui;
using Xunit;

namespace PixelEngine.UI.Tests;

public sealed class GuiAppCombinedFrameTests
{
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

    private sealed class FakeGuiBackend(List<string> calls) : IGuiImGuiBackend
    {
        private readonly List<string> _calls = calls;

        public GuiInputSnapshot Capture => default;

        public void Initialize(GuiAppOptions options)
        {
            _calls.Add("initialize");
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
        }
    }
}
