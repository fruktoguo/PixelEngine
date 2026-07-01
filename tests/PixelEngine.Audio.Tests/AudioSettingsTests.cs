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
        Assert.Equal(1f, settings.GetCategoryVolume(AudioVolumeCategory.Sfx));
        Assert.Equal(1f, settings.GetCategoryVolume(AudioVolumeCategory.Ui));
        Assert.Equal(1f, settings.GetCategoryVolume(AudioVolumeCategory.Ambient));
        Assert.Equal(32, settings.MaxParticleImpactEventsPerFrame);
        Assert.Equal(16, settings.CoalesceBucketSize);
        Assert.Equal(4, settings.DefaultCooldownTicks);

        settings.SfxVolume = 0.25f;
        settings.UiVolume = 0.5f;
        settings.AmbientVolume = 0.75f;

        Assert.Equal(0.25f, settings.GetCategoryVolume(AudioVolumeCategory.Sfx));
        Assert.Equal(0.5f, settings.GetCategoryVolume(AudioVolumeCategory.Ui));
        Assert.Equal(0.75f, settings.GetCategoryVolume(AudioVolumeCategory.Ambient));
    }

    [Fact]
    public void SettingsRejectInvalidValues()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MaxVoices = 0 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MaxAmbientVoices = -1 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { PixelsPerMeter = 0f }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MasterVolume = -0.1f }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { SfxVolume = -0.1f }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { UiVolume = float.NaN }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { AmbientVolume = -0.1f }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { ReferenceDistance = float.NaN }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MaxDrainedAudioEventsPerFrame = 0 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { MaxParticleImpactEventsPerFrame = 0 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { CoalesceBucketSize = 0 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { DefaultCooldownTicks = -1 }.Validate());
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSettings { CooldownTableCapacity = 3 }.Validate());
    }
}
