using PixelEngine.Core.Events;
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
}
