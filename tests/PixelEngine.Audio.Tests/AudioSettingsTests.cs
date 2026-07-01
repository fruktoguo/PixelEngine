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
        Assert.Equal(32, settings.MaxParticleImpactEventsPerFrame);
        Assert.Equal(16, settings.CoalesceBucketSize);
        Assert.Equal(4, settings.DefaultCooldownTicks);
    }

    [Fact]
    public void SettingsRejectInvalidValues()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MaxVoices = 0 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MaxAmbientVoices = -1 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { PixelsPerMeter = 0f }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MasterVolume = -0.1f }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { ReferenceDistance = float.NaN }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MaxDrainedAudioEventsPerFrame = 0 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MaxParticleImpactEventsPerFrame = 0 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { CoalesceBucketSize = 0 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { DefaultCooldownTicks = -1 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { CooldownTableCapacity = 3 }.Validate());
    }
}
