using PixelEngine.Core.Events;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Audio.Tests;

public sealed class MaterialAudioTableTests
{
    [Fact]
    public void MaterialAudioTableMapsAllEventTypesFromCueSet()
    {
        MaterialDef[] definitions =
        [
            new() { Id = 0, Name = "empty", HeatCapacity = 1f },
            new()
            {
                Id = 1,
                Name = "stone",
                HeatCapacity = 1f,
                AudioCues = new AudioCueSet
                {
                    ImpactCue = 10,
                    FireCue = 20,
                    SplashCue = 30,
                    ExplosionCue = 40,
                    ShatterCue = 50,
                    AmbientCue = 60,
                },
            },
        ];
        MaterialAudioTable table = MaterialAudioTable.FromDefinitions(definitions);

        AssertCue(table, AudioEventType.ParticleImpact, 10);
        AssertCue(table, AudioEventType.FireCrackle, 20);
        AssertCue(table, AudioEventType.LiquidSplash, 30);
        AssertCue(table, AudioEventType.Explosion, 40);
        AssertCue(table, AudioEventType.RigidbodyShatter, 50);
        AssertCue(table, AudioEventType.AmbientRegion, 60);
    }

    [Fact]
    public void MaterialAudioTableRejectsUnknownMaterialUnconfiguredCueAndMisorderedDefinitions()
    {
        MaterialDef[] definitions =
        [
            new() { Id = 0, Name = "empty", HeatCapacity = 1f },
            new() { Id = 1, Name = "water", HeatCapacity = 1f },
        ];
        MaterialAudioTable table = MaterialAudioTable.FromDefinitions(definitions);

        Assert.False(table.TryResolve(new CoalescedAudioEvent(AudioEventType.LiquidSplash, 0, 0, 1, 1f, 1), 0, out _));
        Assert.False(table.TryResolve(new CoalescedAudioEvent(AudioEventType.LiquidSplash, 0, 0, 5, 1f, 1), 0, out _));
        _ = Assert.Throws<ArgumentException>(() => MaterialAudioTable.FromDefinitions(
        [
            new() { Id = 1, Name = "bad", HeatCapacity = 1f },
        ]));
    }

    [Fact]
    public void MaterialAudioPlayerResolvesBufferAndPlaysVoice()
    {
        MaterialAudioTable table = MaterialAudioTable.FromDefinitions(
        [
            new() { Id = 0, Name = "empty", HeatCapacity = 1f },
            new()
            {
                Id = 1,
                Name = "stone",
                HeatCapacity = 1f,
                AudioCues = new AudioCueSet { ImpactCue = 7 },
            },
        ]);
        BufferResolver buffers = new();
        MaterialAudioPlayer player = new(table, buffers);
        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, new AudioSettings { MaxVoices = 1 });
        AudioVoice voice = voices.Acquire(1, AudioEventType.ParticleImpact, default, default, 1)!;

        bool played = player.TryPlay(new CoalescedAudioEvent(AudioEventType.ParticleImpact, 0, 0, 1, 1f, 1), voice, 1);

        Assert.True(played);
        Assert.Equal(1, backend.PlayCalls);
        Assert.Equal(7, buffers.LastCueHandle);
    }

    [Fact]
    public void MaterialAudioPlayerAppliesRuntimeCategoryVolume()
    {
        MaterialAudioTable table = MaterialAudioTable.FromDefinitions(
        [
            new() { Id = 0, Name = "empty", HeatCapacity = 1f },
            new()
            {
                Id = 1,
                Name = "stone",
                HeatCapacity = 1f,
                AudioCues = new AudioCueSet { ImpactCue = 7 },
            },
        ]);
        BufferResolver buffers = new();
        AudioSettings settings = new() { SfxVolume = 0.25f };
        MaterialAudioPlayer player = new(table, buffers, settings);
        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, new AudioSettings { MaxVoices = 1 });
        AudioVoice voice = voices.Acquire(1, AudioEventType.ParticleImpact, default, default, 1)!;

        bool played = player.TryPlay(new CoalescedAudioEvent(AudioEventType.ParticleImpact, 0, 0, 1, 1f, 1), voice, 1);
        settings.SfxVolume = 0.5f;
        voice = voices.Acquire(1, AudioEventType.ParticleImpact, default, default, 2)!;
        bool playedAfterChange = player.TryPlay(new CoalescedAudioEvent(AudioEventType.ParticleImpact, 0, 0, 1, 1f, 1), voice, 2);

        Assert.True(played);
        Assert.True(playedAfterChange);
        Assert.Equal(0.5f, backend.GetSourceGain(voices[0].Source), precision: 5);
    }

    private static void AssertCue(MaterialAudioTable table, AudioEventType type, int expectedCue)
    {
        bool resolved = table.TryResolve(new CoalescedAudioEvent(type, 0, 0, 1, 0.8f, 4), 123, out MaterialAudioPlayback playback);

        Assert.True(resolved);
        Assert.Equal(expectedCue, playback.CueHandle);
        Assert.True(playback.Gain > 0f);
        Assert.True(playback.Pitch > 0f);
        Assert.Equal(ExpectedPriority(type), playback.Priority);
    }

    private static byte ExpectedPriority(AudioEventType type)
    {
        return type switch
        {
            AudioEventType.Explosion => 220,
            AudioEventType.RigidbodyShatter => 180,
            AudioEventType.LiquidSplash => 130,
            AudioEventType.ParticleImpact => 110,
            AudioEventType.FireCrackle => 80,
            AudioEventType.AmbientRegion => 40,
            _ => 0,
        };
    }

    private sealed class BufferResolver : IAudioCueBufferResolver
    {
        public int LastCueHandle { get; private set; }

        public bool TryResolveBuffer(int cueHandle, out uint buffer)
        {
            LastCueHandle = cueHandle;
            buffer = (uint)cueHandle;
            return true;
        }
    }
}
