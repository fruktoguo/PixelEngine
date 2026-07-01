using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本光照 API 测试。
/// </summary>
public sealed class ScriptLightingApiTests
{
    /// <summary>
    /// 验证光照 API 记录点光源与 fog reveal 请求。
    /// </summary>
    [Fact]
    public void LightingApiBuffersPointLightsAndFogReveals()
    {
        ScriptLightingApi lighting = new();

        lighting.RevealAround(10, 20, 30, alpha: 128);
        lighting.AddPointLight(12, 24, 40, 0xFFFF8040u, intensity: 0.75f);

        Assert.Equal(1, lighting.RevealCount);
        Assert.Equal(new FogRevealRequest(10, 20, 30, 128), lighting.GetReveal(0));
        Assert.Equal(1, lighting.PointLightCount);
        Assert.Equal(new ScriptPointLight(12, 24, 40, 0xFFFF8040u, 0.75f), lighting.GetPointLight(0));

        lighting.ClearPointLights();

        Assert.Equal(0, lighting.PointLightCount);
        Assert.Equal(1, lighting.RevealCount);
        lighting.ClearReveals();
        Assert.Equal(0, lighting.RevealCount);
    }

    /// <summary>
    /// 验证无效光照参数被拒绝。
    /// </summary>
    [Fact]
    public void LightingApiRejectsInvalidValues()
    {
        ScriptLightingApi lighting = new();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => lighting.RevealAround(0, 0, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => lighting.AddPointLight(0, 0, 1, 0xFFFFFFFFu, -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => lighting.GetPointLight(0));
    }
}
