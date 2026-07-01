using Xunit;
using PixelEngine.Core.Events;

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

    [Fact]
    public void RuntimeSettingsResizeVoicePoolAndDispatcherCaps()
    {
        AudioSettings initial = new()
        {
            MaxVoices = 1,
            MaxParticleImpactEventsPerFrame = 1,
            CoalesceBucketSize = 1,
            DefaultCooldownTicks = 0,
        };
        MpscRingBuffer<AudioEvent> ring = new(8);
        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, initial);
        AudioDispatcher dispatcher = new(ring, voices, initial);
        CountingPlayer player = new();
        AudioListenerState listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);

        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 0, 0, 1, 1f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 2, 0, 1, 1f)));
        AudioDispatchStats first = dispatcher.Dispatch(listener, tick: 1, player);

        AudioSettings updated = new()
        {
            MaxVoices = 2,
            MaxParticleImpactEventsPerFrame = 2,
            CoalesceBucketSize = 1,
            DefaultCooldownTicks = 0,
        };
        voices.ApplySettings(updated);
        dispatcher.ApplySettings(updated);
        for (int i = 0; i < voices.Capacity; i++)
        {
            backend.MarkStopped(voices[i].Source);
        }

        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 0, 0, 1, 1f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 2, 0, 1, 1f)));
        AudioDispatchStats second = dispatcher.Dispatch(listener, tick: 2, player);

        Assert.Equal(1, first.Played);
        Assert.Equal(1, first.Dropped);
        Assert.Equal(2, voices.Capacity);
        Assert.Equal(2, second.Played);
    }

    [Fact]
    public void AudioSystemApplySettingsResizesVoicesAndUpdatesListenerGain()
    {
        using NullAudioBackend backend = new();
        using AudioSystem system = new();
        system.Initialize(new AudioSettings { MaxVoices = 1 }, backend);

        system.ApplySettings(new AudioSettings { MaxVoices = 2, MasterVolume = 0.5f });

        Assert.Equal(2, system.Voices.Capacity);
        Assert.Equal(0.5f, backend.LastListener.Gain);
    }

    private sealed class CountingPlayer : IAudioEventPlayer
    {
        public bool TryPlay(in CoalescedAudioEvent audioEvent, AudioVoice voice, long tick)
        {
            _ = audioEvent;
            _ = tick;
            voice.Play(buffer: 1, gain: 1f, pitch: 1f);
            return true;
        }
    }
}
