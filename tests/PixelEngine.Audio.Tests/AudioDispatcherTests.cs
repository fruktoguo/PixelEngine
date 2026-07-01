using PixelEngine.Core.Events;
using Xunit;

namespace PixelEngine.Audio.Tests;

public sealed class AudioDispatcherTests
{
    [Fact]
    public void CoalescerMergesNearbyEventsAndDropsBeyondPerTypeCap()
    {
        AudioSettings settings = new()
        {
            MaxParticleImpactEventsPerFrame = 3,
            CoalesceBucketSize = 16,
        };
        AudioEventCoalescer coalescer = new(settings);
        coalescer.BeginFrame();

        coalescer.Add(new AudioEvent(AudioEventType.ParticleImpact, 0, 0, 1, 0.2f));
        coalescer.Add(new AudioEvent(AudioEventType.ParticleImpact, 1, 1, 2, 0.8f));
        coalescer.Add(new AudioEvent(AudioEventType.ParticleImpact, 2, 2, 3, 0.4f));
        coalescer.Add(new AudioEvent(AudioEventType.ParticleImpact, 3, 3, 4, 1.0f));

        Span<CoalescedAudioEvent> output = stackalloc CoalescedAudioEvent[8];
        int count = coalescer.Flush(output);

        Assert.Equal(1, count);
        Assert.Equal(2, coalescer.CoalescedCount);
        Assert.Equal(1, coalescer.DroppedCount);
        Assert.Equal((ushort)3, output[0].Count);
        Assert.Equal(4, output[0].MaterialId);
        Assert.Equal(1.0f, output[0].Magnitude);
        Assert.Equal(8, output[0].CellX);
        Assert.Equal(8, output[0].CellY);
    }

    [Fact]
    public void CoalescerKeepsSeparateTypesAndBuckets()
    {
        AudioEventCoalescer coalescer = new(new AudioSettings { CoalesceBucketSize = 8 });
        coalescer.BeginFrame();

        coalescer.Add(new AudioEvent(AudioEventType.ParticleImpact, 0, 0, 1, 0.1f));
        coalescer.Add(new AudioEvent(AudioEventType.ParticleImpact, 9, 0, 1, 0.2f));
        coalescer.Add(new AudioEvent(AudioEventType.LiquidSplash, 0, 0, 1, 0.3f));

        Span<CoalescedAudioEvent> output = stackalloc CoalescedAudioEvent[8];
        int count = coalescer.Flush(output);

        Assert.Equal(3, count);
        Assert.Equal(0, coalescer.CoalescedCount);
        Assert.Equal(0, coalescer.DroppedCount);
    }

    [Fact]
    public void CooldownTrackerSuppressesSameMaterialAndTypeWithinCooldown()
    {
        CooldownTracker cooldowns = new(16);

        Assert.True(cooldowns.ShouldPlay(7, AudioEventType.Explosion, tick: 10, cooldownTicks: 4));
        Assert.False(cooldowns.ShouldPlay(7, AudioEventType.Explosion, tick: 12, cooldownTicks: 4));
        Assert.True(cooldowns.ShouldPlay(8, AudioEventType.Explosion, tick: 12, cooldownTicks: 4));
        Assert.True(cooldowns.ShouldPlay(7, AudioEventType.LiquidSplash, tick: 12, cooldownTicks: 4));
        Assert.True(cooldowns.ShouldPlay(7, AudioEventType.Explosion, tick: 14, cooldownTicks: 4));
    }

    [Fact]
    public void DispatcherDrainsRingCoalescesCooldownsAndUsesVoicePool()
    {
        AudioSettings settings = new()
        {
            MaxVoices = 4,
            MaxParticleImpactEventsPerFrame = 8,
            CoalesceBucketSize = 16,
            DefaultCooldownTicks = 4,
            PixelsPerMeter = 16f,
        };
        MpscRingBuffer<AudioEvent> ring = new(32);
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 0, 0, 5, 0.25f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 1, 1, 5, 0.75f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.Explosion, 64, 0, 9, 1.0f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.Explosion, 80, 0, 9, 0.8f)));

        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, settings);
        AudioDispatcher dispatcher = new(ring, voices, settings);
        RecordingEventPlayer player = new();
        AudioListenerState listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);

        AudioDispatchStats first = dispatcher.Dispatch(listener, tick: 100, player);
        AudioDispatchStats second = dispatcher.Dispatch(listener, tick: 101, player);

        Assert.Equal(4, first.Drained);
        Assert.Equal(1, first.Coalesced);
        Assert.Equal(1, first.Dropped);
        Assert.Equal(2, first.Dispatched);
        Assert.Equal(2, first.Played);
        Assert.Equal(2, first.ActiveVoices);
        Assert.Equal(2, backend.PlayCalls);
        Assert.Equal(0, second.Drained);
        Assert.Equal(2, player.PlayedCount);
        Assert.Equal(AudioEventType.ParticleImpact, player.Events[0].Type);
        Assert.Equal((ushort)2, player.Events[0].Count);
        Assert.Equal(0.75f, player.Events[0].Magnitude);
        Assert.Equal(AudioEventType.Explosion, player.Events[1].Type);
    }

    [Fact]
    public void DispatcherCapsThousandSameCoordinateImpacts()
    {
        const int perTypeCap = 8;
        AudioSettings settings = new()
        {
            MaxVoices = 16,
            MaxDrainedAudioEventsPerFrame = 2048,
            MaxParticleImpactEventsPerFrame = perTypeCap,
            CoalesceBucketSize = 16,
            DefaultCooldownTicks = 0,
        };
        MpscRingBuffer<AudioEvent> ring = new(2048);
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 10, 10, 1, i)));
        }

        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, settings);
        AudioDispatcher dispatcher = new(ring, voices, settings);
        RecordingEventPlayer player = new();
        AudioListenerState listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);

        AudioDispatchStats stats = dispatcher.Dispatch(listener, tick: 1, player);

        Assert.Equal(1000, stats.Drained);
        Assert.Equal(1, stats.Played);
        Assert.True(stats.Played <= perTypeCap);
        Assert.Equal(999, stats.Dropped + stats.Coalesced);
        Assert.Equal(992, stats.Dropped);
        Assert.Equal((ushort)perTypeCap, player.Events[0].Count);
        Assert.Equal(999f, player.Events[0].Magnitude);
    }

    [Fact]
    public void DispatcherSuppressesSameMaterialAndTypeWithinCooldown()
    {
        AudioSettings settings = new()
        {
            MaxVoices = 8,
            CoalesceBucketSize = 4,
            DefaultCooldownTicks = 10,
        };
        MpscRingBuffer<AudioEvent> ring = new(16);
        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, settings);
        AudioDispatcher dispatcher = new(ring, voices, settings);
        RecordingEventPlayer player = new();
        AudioListenerState listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);

        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.LiquidSplash, 0, 0, 3, 1f)));
        AudioDispatchStats first = dispatcher.Dispatch(listener, tick: 20, player);
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.LiquidSplash, 64, 0, 3, 1f)));
        AudioDispatchStats second = dispatcher.Dispatch(listener, tick: 25, player);
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.LiquidSplash, 64, 0, 3, 1f)));
        AudioDispatchStats third = dispatcher.Dispatch(listener, tick: 30, player);

        Assert.Equal(1, first.Played);
        Assert.Equal(0, second.Played);
        Assert.Equal(1, second.Dropped);
        Assert.Equal(1, third.Played);
    }

    [Fact]
    public void DispatcherReportsVoiceDropWhenPoolCannotSteal()
    {
        AudioSettings settings = new()
        {
            MaxVoices = 1,
            CoalesceBucketSize = 4,
            DefaultCooldownTicks = 0,
        };
        MpscRingBuffer<AudioEvent> ring = new(8);
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.Explosion, 0, 0, 1, 1f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.AmbientRegion, 32, 0, 2, 1f)));

        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, settings);
        AudioDispatcher dispatcher = new(ring, voices, settings);
        RecordingEventPlayer player = new();
        AudioListenerState listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);

        AudioDispatchStats stats = dispatcher.Dispatch(listener, tick: 1, player);

        Assert.Equal(2, stats.Drained);
        Assert.Equal(1, stats.Played);
        Assert.Equal(1, stats.Dropped);
        Assert.Equal(1, voices.DroppedVoiceCount);
    }

    [Fact]
    public void DispatcherDoesNotAllocateDuringSteadyDispatch()
    {
        AudioSettings settings = new()
        {
            MaxVoices = 8,
            MaxParticleImpactEventsPerFrame = 8,
            CoalesceBucketSize = 16,
            DefaultCooldownTicks = 0,
        };
        MpscRingBuffer<AudioEvent> ring = new(64);
        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, settings);
        AudioDispatcher dispatcher = new(ring, voices, settings);
        NonAllocEventPlayer player = new();
        AudioListenerState listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);

        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 0, 0, 1, 1f)));
        _ = dispatcher.Dispatch(listener, tick: 1, player);
        for (int i = 0; i < voices.Capacity; i++)
        {
            backend.MarkStopped(voices[i].Source);
        }

        voices.RefreshFinishedVoices();
        player.PlayedCount = 0;
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 0, 0, 1, 1f)));
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 32, 0, 1, 1f)));

        long before = GC.GetAllocatedBytesForCurrentThread();
        AudioDispatchStats stats = dispatcher.Dispatch(listener, tick: 2, player);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(2, stats.Played);
        Assert.Equal(2, player.PlayedCount);
    }

    [Fact]
    public void DispatcherConsumesAllCoreAudioEventTypes()
    {
        AudioSettings settings = new()
        {
            MaxVoices = 8,
            CoalesceBucketSize = 4,
            DefaultCooldownTicks = 0,
        };
        MpscRingBuffer<AudioEvent> ring = new(16);
        AudioEventType[] types =
        [
            AudioEventType.ParticleImpact,
            AudioEventType.FireCrackle,
            AudioEventType.LiquidSplash,
            AudioEventType.Explosion,
            AudioEventType.RigidbodyShatter,
            AudioEventType.AmbientRegion,
        ];
        for (int i = 0; i < types.Length; i++)
        {
            Assert.True(ring.TryEnqueue(new AudioEvent(types[i], i * 16, 0, (ushort)(i + 1), i + 1f)));
        }

        using NullAudioBackend backend = new();
        using AudioVoicePool voices = new(backend, settings);
        AudioDispatcher dispatcher = new(ring, voices, settings);
        RecordingEventPlayer player = new();
        AudioListenerState listener = new(default, new(0f, 0f, -1f), new(0f, 1f, 0f), 1f);

        AudioDispatchStats stats = dispatcher.Dispatch(listener, tick: 10, player);

        Assert.Equal(types.Length, stats.Drained);
        Assert.Equal(types.Length, stats.Played);
        Assert.Equal(0, stats.Dropped);
        for (int i = 0; i < types.Length; i++)
        {
            Assert.Equal(types[i], player.Events[i].Type);
        }
    }

    private sealed class RecordingEventPlayer : IAudioEventPlayer
    {
        public readonly List<CoalescedAudioEvent> Events = [];

        public int PlayedCount { get; private set; }

        public bool TryPlay(in CoalescedAudioEvent audioEvent, AudioVoice voice, long tick)
        {
            _ = tick;
            Events.Add(audioEvent);
            voice.Play(buffer: 1, gain: 1f, pitch: 1f);
            PlayedCount++;
            return true;
        }
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
