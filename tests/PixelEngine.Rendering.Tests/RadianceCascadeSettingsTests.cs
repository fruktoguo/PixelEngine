using PixelEngine.Core;
using PixelEngine.Rendering.Compute;
using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// 辐射级联设置测试：默认参数与校验边界。
/// </summary>
public sealed class RadianceCascadeSettingsTests
{
    /// <summary>
    /// 验证Default Settings Come From Engine Constants And Stay Disabled。
    /// </summary>
    [Fact]
    public void DefaultSettingsComeFromEngineConstantsAndStayDisabled()
    {
        RadianceCascadeSettings settings = RadianceCascadeSettings.Default;

        Assert.False(settings.Enabled);
        Assert.Equal(EngineConstants.RadianceCascadeCount, settings.CascadeCount);
        Assert.Equal(EngineConstants.RadianceCascadeBaseRayCount, settings.BaseRayCount);
        Assert.Equal(EngineConstants.RadianceCascadeBaseStepPixels, settings.BaseStepPixels);
        Assert.Equal(EngineConstants.RadianceCascadeMaxRaySteps, settings.MaxRaySteps);
        Assert.Equal(settings, settings.Validate());
    }

    /// <summary>
    /// 验证Feature Switch保持Radiance Cascades Disabled By Default。
    /// </summary>
    [Fact]
    public void FeatureSwitchKeepsRadianceCascadesDisabledByDefault()
    {
        Assert.False(ComputeFeatureSwitches.Default.RadianceCascadesEnabled);
    }

    /// <summary>
    /// 验证Validate Rejects Non Power Of Two Ray Count。
    /// </summary>
    [Fact]
    public void ValidateRejectsNonPowerOfTwoRayCount()
    {
        RadianceCascadeSettings settings = RadianceCascadeSettings.Default with { BaseRayCount = 63 };

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
        Assert.Contains("2 的幂", exception.Message, StringComparison.Ordinal);
    }
}
