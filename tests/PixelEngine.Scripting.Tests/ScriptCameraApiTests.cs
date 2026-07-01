using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本相机 API 测试。
/// </summary>
public sealed class ScriptCameraApiTests
{
    /// <summary>
    /// 验证相机中心、缩放、视口与屏幕/世界坐标转换一致。
    /// </summary>
    [Fact]
    public void CameraConvertsBetweenScreenAndWorldUsingCenterAndZoom()
    {
        ScriptCameraApi camera = new(viewportWidth: 100, viewportHeight: 50, centerX: 200, centerY: 100, zoom: 2);

        Assert.Equal(200f, camera.CenterX);
        Assert.Equal(100f, camera.CenterY);
        Assert.Equal(2f, camera.Zoom);
        Assert.Equal(new RectF(0, 0, 100, 50), camera.Viewport);

        Point2F origin = camera.ScreenToWorld(0, 0);
        Assert.Equal(175f, origin.X);
        Assert.Equal(87.5f, origin.Y);
        Assert.Equal(new Point2F(50, 25), camera.WorldToScreen(200, 100));

        CameraSnapshot snapshot = camera.Snapshot();
        Assert.Equal(175f, snapshot.OriginWorldX);
        Assert.Equal(87.5f, snapshot.OriginWorldY);
        Assert.Equal(0.5f, snapshot.CellsPerPixel);
        Assert.Equal(100, snapshot.ViewportWidth);
        Assert.Equal(50, snapshot.ViewportHeight);
    }

    /// <summary>
    /// 验证脚本可更新相机视口与缩放。
    /// </summary>
    [Fact]
    public void CameraViewportAndZoomAreMutableWithValidation()
    {
        ScriptCameraApi camera = new(64, 64);

        camera.SetCenter(32, 48);
        camera.SetZoom(4);
        camera.SetViewport(128, 64);

        Assert.Equal(new Point2F(16, 40), camera.ScreenToWorld(0, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => camera.SetZoom(0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => camera.SetViewport(0, 64));
    }
}
