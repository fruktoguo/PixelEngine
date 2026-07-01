using PixelEngine.Core.Events;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Audio.Tests;

public sealed class AudioAcceptanceTests
{
    [Fact]
    public void DispatcherPlacesSourceInMeterSpaceForPositionedEvents()
    {
        AudioSettings settings = new()
        {
            MaxVoices = 2,
            PixelsPerMeter = 16f,
            CoalesceBucketSize = 1,
            DefaultCooldownTicks = 0,
        };
        MpscRingBuffer<AudioEvent> ring = new(8);
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 32, 48, 1, 1f)));
        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, settings);
        AudioDispatcher dispatcher = new(ring, voices, settings);
        NonAllocEventPlayer player = new();
        AudioListenerState listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);

        AudioDispatchStats stats = dispatcher.Dispatch(listener, tick: 1, player);

        Assert.Equal(1, stats.Played);
        Assert.Equal(new System.Numerics.Vector3(2f, 3f, 0f), backend.GetSourcePosition(voices[0].Source));
    }

    [Fact]
    public async Task MpscRingFeedsDispatcherFromConcurrentProducers()
    {
        const int producerCount = 4;
        const int eventsPerProducer = 128;
        const int totalEvents = producerCount * eventsPerProducer;
        AudioSettings settings = new()
        {
            MaxVoices = totalEvents,
            MaxDrainedAudioEventsPerFrame = 1024,
            MaxParticleImpactEventsPerFrame = totalEvents,
            CoalesceBucketSize = 1,
            DefaultCooldownTicks = 0,
        };
        MpscRingBuffer<AudioEvent> ring = new(1024);
        Task[] producers = new Task[producerCount];
        for (int p = 0; p < producers.Length; p++)
        {
            int producer = p;
            producers[p] = Task.Run(() =>
            {
                for (int i = 0; i < eventsPerProducer; i++)
                {
                    AudioEvent audioEvent = new(AudioEventType.ParticleImpact, (producer * 10_000) + i, 0, 1, 1f);
                    while (!ring.TryEnqueue(in audioEvent))
                    {
                        _ = Thread.Yield();
                    }
                }
            });
        }

        await Task.WhenAll(producers);
        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, settings);
        AudioDispatcher dispatcher = new(ring, voices, settings);
        NonAllocEventPlayer player = new();
        AudioListenerState listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);

        AudioDispatchStats stats = dispatcher.Dispatch(listener, tick: 1, player);

        Assert.Equal(totalEvents, stats.Drained);
        Assert.Equal(totalEvents, stats.Played);
        Assert.Equal(0, stats.Dropped);
        Assert.Equal(totalEvents, player.PlayedCount);
    }

    [Fact]
    public void DispatcherRoutesAmbientRegionToLoopManagerWithoutPositionalVoice()
    {
        AudioSettings settings = new()
        {
            MaxVoices = 1,
            MaxAmbientVoices = 1,
            MaxAmbientRegionEventsPerFrame = 4,
            CoalesceBucketSize = 1,
            DefaultCooldownTicks = 0,
            AmbientEnterThreshold = 0.3f,
            AmbientExitThreshold = 0.2f,
            AmbientFadeRate = 0.5f,
        };
        MpscRingBuffer<AudioEvent> ring = new(8);
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.AmbientRegion, 4, 8, 1, 0.8f)));
        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, settings);
        AudioDispatcher dispatcher = new(ring, voices, settings);
        using AmbientLoopManager ambient = new(backend, BuildAmbientTable(), new BufferResolver(), settings);
        NonAllocEventPlayer player = new();
        AudioListenerState listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);

        AudioDispatchStats first = dispatcher.Dispatch(listener, tick: 1, player, ambient);
        Assert.Equal(1, first.Drained);
        Assert.Equal(0, first.Played);
        Assert.Equal(0, player.PlayedCount);
        Assert.Equal(0, voices.ActiveVoiceCount);
        Assert.Equal(1, ambient.ActiveVoiceCount);
        Assert.Equal(1, backend.PlayCalls);

        AudioDispatchStats second = dispatcher.Dispatch(listener, tick: 2, player, ambient);
        AudioDispatchStats third = dispatcher.Dispatch(listener, tick: 3, player, ambient);

        Assert.Equal(0, second.Played);
        Assert.Equal(0, third.Played);
        Assert.Equal(0, ambient.ActiveVoiceCount);
        Assert.Equal(1, backend.StopCalls);
    }

    [Fact]
    public void DispatcherTriggersAllMaterialEventFamiliesWithDistinctCues()
    {
        AudioSettings settings = new()
        {
            MaxVoices = 5,
            MaxAmbientVoices = 1,
            MaxParticleImpactEventsPerFrame = 4,
            MaxFireCrackleEventsPerFrame = 4,
            MaxLiquidSplashEventsPerFrame = 4,
            MaxExplosionEventsPerFrame = 4,
            MaxRigidbodyShatterEventsPerFrame = 4,
            MaxAmbientRegionEventsPerFrame = 4,
            CoalesceBucketSize = 1,
            DefaultCooldownTicks = 0,
            AmbientEnterThreshold = 0.3f,
            AmbientExitThreshold = 0.2f,
            AmbientFadeRate = 0.5f,
        };
        MpscRingBuffer<AudioEvent> ring = new(16);
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 0, 0, 1, 1f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.FireCrackle, 8, 0, 1, 1f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.LiquidSplash, 16, 0, 1, 1f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.Explosion, 24, 0, 1, 1f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.RigidbodyShatter, 32, 0, 1, 1f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.AmbientRegion, 40, 0, 1, 1f)));
        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, settings);
        AudioDispatcher dispatcher = new(ring, voices, settings);
        MaterialAudioTable table = BuildAllCuesTable();
        RecordingBufferResolver buffers = new();
        MaterialAudioPlayer player = new(table, buffers, settings);
        using AmbientLoopManager ambient = new(backend, table, buffers, settings);
        AudioListenerState listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);

        AudioDispatchStats stats = dispatcher.Dispatch(listener, tick: 1, player, ambient);

        Assert.Equal(6, stats.Drained);
        Assert.Equal(5, stats.Played);
        Assert.Equal(5, voices.ActiveVoiceCount);
        Assert.Equal(1, ambient.ActiveVoiceCount);
        Assert.Equal(6, backend.PlayCalls);
        Assert.Contains(10, buffers.ResolvedCueHandles);
        Assert.Contains(20, buffers.ResolvedCueHandles);
        Assert.Contains(30, buffers.ResolvedCueHandles);
        Assert.Contains(40, buffers.ResolvedCueHandles);
        Assert.Contains(50, buffers.ResolvedCueHandles);
        Assert.Contains(60, buffers.ResolvedCueHandles);
    }

    private static MaterialAudioTable BuildAmbientTable()
    {
        return MaterialAudioTable.FromDefinitions(
        [
            new() { Id = 0, Name = "empty", HeatCapacity = 1f },
            new()
            {
                Id = 1,
                Name = "water",
                HeatCapacity = 1f,
                AudioCues = new AudioCueSet { AmbientCue = 9 },
            },
        ]);
    }

    private static MaterialAudioTable BuildAllCuesTable()
    {
        return MaterialAudioTable.FromDefinitions(
        [
            new() { Id = 0, Name = "empty", HeatCapacity = 1f },
            new()
            {
                Id = 1,
                Name = "mixed",
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
        ]);
    }

    private sealed class NonAllocEventPlayer : IAudioEventPlayer
    {
        public int PlayedCount;

        public bool TryPlay(in CoalescedAudioEvent audioEvent, AudioVoice voice, long tick)
        {
            _ = audioEvent;
            _ = tick;
            voice.Play(buffer: 1, gain: 1f, pitch: 1f);
            PlayedCount++;
            return true;
        }
    }

    private sealed class BufferResolver : IAudioCueBufferResolver
    {
        public bool TryResolveBuffer(int cueHandle, out uint buffer)
        {
            buffer = (uint)cueHandle;
            return cueHandle > 0;
        }
    }

    private sealed class RecordingBufferResolver : IAudioCueBufferResolver
    {
        public List<int> ResolvedCueHandles { get; } = [];

        public bool TryResolveBuffer(int cueHandle, out uint buffer)
        {
            ResolvedCueHandles.Add(cueHandle);
            buffer = (uint)cueHandle;
            return cueHandle > 0;
        }
    }
}
