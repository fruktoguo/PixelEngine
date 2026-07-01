using Xunit;

namespace PixelEngine.Audio.Tests;

public sealed class AudioSettingsTests
{
    [Fact]
    public void DefaultSettingsValidateAndExposeVoicePoolDefaults()
    {
        AudioSettings settings = new();

        Assert.Same(settings, settings.Validate());
        Assert.Equal(64, settings.MaxVoices);
        Assert.Equal(8, settings.MaxAmbientVoices);
        Assert.Equal(32f, settings.PixelsPerMeter);
    }

    [Fact]
    public void SettingsRejectInvalidValues()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MaxVoices = 0 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MaxAmbientVoices = -1 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { PixelsPerMeter = 0f }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MasterVolume = -0.1f }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { ReferenceDistance = float.NaN }.Validate());
    }
}
