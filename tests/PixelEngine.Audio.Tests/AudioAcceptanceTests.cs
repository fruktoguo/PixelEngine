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
}
