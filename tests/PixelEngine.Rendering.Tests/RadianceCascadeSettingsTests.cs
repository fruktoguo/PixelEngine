using PixelEngine.Core;
using PixelEngine.Rendering.Compute;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class RadianceCascadeSettingsTests
{
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

    [Fact]
    public void FeatureSwitchKeepsRadianceCascadesDisabledByDefault()
    {
        Assert.False(ComputeFeatureSwitches.Default.RadianceCascadesEnabled);
    }

    [Fact]
    public void ValidateRejectsNonPowerOfTwoRayCount()
    {
        RadianceCascadeSettings settings = RadianceCascadeSettings.Default with { BaseRayCount = 63 };

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
        Assert.Contains("2 的幂", exception.Message, StringComparison.Ordinal);
    }
}
