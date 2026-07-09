using PixelEngine.Rendering;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 脚本光照请求同步测试。
/// 不变式：脚本光照请求合并到渲染相位、无效句柄被忽略。
/// </summary>
public sealed class ScriptLightingSynchronizerTests
{
    /// <summary>
    /// 验证脚本点光源会转换到视口像素坐标并在消费后清空。
    /// </summary>
    [Fact]
    public void SynchronizerConvertsPointLightsToViewportSpace()
    {
        ScriptCameraApi camera = new(viewportWidth: 100, viewportHeight: 50, centerX: 200, centerY: 100, zoom: 2);
        ScriptCameraSynchronizer cameraSync = new(camera);
        _ = cameraSync.Sync();
        ScriptLightingApi lighting = new();
        lighting.AddPointLight(180, 90, 10, 0xFF_80_40_20, intensity: 0.5f);
        ScriptLightingSynchronizer synchronizer = new(lighting, cameraSync);

        synchronizer.Sync();

        ReadOnlySpan<LightSource> lights = synchronizer.PointLights;
        Assert.Equal(1, lights.Length);
        Assert.Equal(10f, lights[0].X);
        Assert.Equal(5f, lights[0].Y);
        Assert.Equal(20f, lights[0].Radius);
        Assert.Equal(0xFF_80_40_20u, lights[0].ColorBgra);
        Assert.Equal(0.5f, lights[0].Intensity);
        Assert.Equal(0, lighting.PointLightCount);
    }

    /// <summary>
    /// 验证 fog reveal 请求会写入 buffer 并在消费后清空。
    /// </summary>
    [Fact]
    public void SynchronizerAppliesFogRevealRequests()
    {
        ScriptCameraApi camera = new(viewportWidth: 64, viewportHeight: 64, centerX: 32, centerY: 32, zoom: 1);
        ScriptCameraSynchronizer cameraSync = new(camera);
        _ = cameraSync.Sync();
        ScriptLightingApi lighting = new();
        lighting.RevealAround(32, 32, 4, alpha: 128);
        ScriptLightingSynchronizer synchronizer = new(lighting, cameraSync);

        synchronizer.Sync();

        Assert.Equal(64, synchronizer.FogOfWar.ViewportCellWidth);
        Assert.Equal(64, synchronizer.FogOfWar.ViewportCellHeight);
        Assert.Equal(128, synchronizer.FogOfWar.RevealAlpha(32, 32));
        Assert.Equal(0, lighting.RevealCount);
    }

    /// <summary>
    /// 验证缩放相机下 fog buffer 使用屏幕像素尺寸，并把世界坐标 reveal 转成像素坐标。
    /// </summary>
    [Fact]
    public void SynchronizerKeepsFogInViewportPixelSpaceWhenZoomed()
    {
        ScriptCameraApi camera = new(viewportWidth: 100, viewportHeight: 50, centerX: 200, centerY: 100, zoom: 2);
        ScriptCameraSynchronizer cameraSync = new(camera);
        _ = cameraSync.Sync();
        ScriptLightingApi lighting = new();
        lighting.RevealAround(200, 100, 8, alpha: 200);
        ScriptLightingSynchronizer synchronizer = new(lighting, cameraSync);

        synchronizer.Sync();

        Assert.Equal(100, synchronizer.FogOfWar.ViewportCellWidth);
        Assert.Equal(50, synchronizer.FogOfWar.ViewportCellHeight);
        Assert.Equal(200, synchronizer.FogOfWar.RevealAlpha(50, 25));
        Assert.Equal(0, lighting.RevealCount);
    }

    /// <summary>
    /// 验证完整视口 reveal 会覆盖所有屏幕边角，避免可玩 Demo 出现圆形黑边。
    /// </summary>
    [Fact]
    public void SynchronizerAppliesViewportRevealToEveryFogTile()
    {
        ScriptCameraApi camera = new(viewportWidth: 96, viewportHeight: 64, centerX: 48, centerY: 32, zoom: 1);
        ScriptCameraSynchronizer cameraSync = new(camera);
        _ = cameraSync.Sync();
        ScriptLightingApi lighting = new();
        ScriptLightingSynchronizer synchronizer = new(lighting, cameraSync);

        lighting.RevealViewport(alpha: 210);
        synchronizer.Sync();

        Assert.Equal(0, lighting.RevealCount);
        Assert.Equal(0, lighting.ViewportRevealAlpha);
        Assert.True(synchronizer.FogOfWar.RevealAlpha(0, 0) >= 210);
        Assert.True(synchronizer.FogOfWar.RevealAlpha(95, 0) >= 210);
        Assert.True(synchronizer.FogOfWar.RevealAlpha(0, 63) >= 210);
        Assert.True(synchronizer.FogOfWar.RevealAlpha(95, 63) >= 210);
    }
}
