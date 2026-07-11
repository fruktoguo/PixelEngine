using PixelEngine.Gui;
using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// runtime ImGui viewport 输入路由测试。
/// </summary>
public sealed class GuiViewportInputRouteTests
{
    /// <summary>
    /// 验证嵌入路由消费窗口 framebuffer 坐标并输出 runtime viewport 坐标。
    /// </summary>
    [Fact]
    public void EmbeddedRouteMapsFramebufferPointerAndRejectsInvalidOrOutsideCoordinates()
    {
        RecordingRoute route = new(succeeds: true, mappedX: 320f, mappedY: 180f);

        Assert.True(GuiWindowInputConnector.TryResolvePointer(route, 840f, 420f, out float x, out float y));
        Assert.Equal((840f, 420f), (route.LastFramebufferX, route.LastFramebufferY));
        Assert.Equal((320f, 180f), (x, y));

        route = new RecordingRoute(succeeds: false, mappedX: 0f, mappedY: 0f);
        Assert.False(GuiWindowInputConnector.TryResolvePointer(route, 840f, 420f, out x, out y));
        Assert.Equal((0f, 0f), (x, y));

        route = new RecordingRoute(succeeds: true, mappedX: float.NaN, mappedY: 180f);
        Assert.False(GuiWindowInputConnector.TryResolvePointer(route, 840f, 420f, out x, out y));
        Assert.Equal((0f, 0f), (x, y));
    }

    /// <summary>
    /// 验证独立 Player 不提供路由时保持原始整窗 framebuffer 坐标。
    /// </summary>
    [Fact]
    public void MissingRouteKeepsStandaloneWholeWindowCoordinates()
    {
        Assert.True(GuiWindowInputConnector.TryResolvePointer(
            route: null,
            framebufferX: 123.5f,
            framebufferY: 67.25f,
            out float x,
            out float y));

        Assert.Equal((123.5f, 67.25f), (x, y));
    }

    private sealed class RecordingRoute(bool succeeds, float mappedX, float mappedY) : IGuiViewportInputRoute
    {
        public bool AllowsKeyboardInput => true;

        public float LastFramebufferX { get; private set; }

        public float LastFramebufferY { get; private set; }

        public bool TryMapPointer(
            float framebufferX,
            float framebufferY,
            out float viewportX,
            out float viewportY)
        {
            LastFramebufferX = framebufferX;
            LastFramebufferY = framebufferY;
            viewportX = mappedX;
            viewportY = mappedY;
            return succeeds;
        }
    }
}
