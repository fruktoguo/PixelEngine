using PixelEngine.Audio;
using PixelEngine.Core;
using PixelEngine.Core.Events;
using PixelEngine.Core.Time;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Audio phase driver 集成测试。
/// </summary>
public sealed class AudioPhaseDriverTests
{
    /// <summary>
    /// 验证 render-only 帧仍推进 listener 与 voice 状态。
    /// </summary>
    [Fact]
    public void AudioPhaseDriverUpdatesListenerOnRenderOnlyFrames()
    {
        using NullAudioBackend backend = new();
        using AudioSystem audio = new();
        audio.Initialize(new AudioSettings(), backend);
        Engine engine = new EngineBuilder()
            .UseHeadless()
            .AddPhaseDriver(new AudioPhaseDriver(
                audio,
                _ => new AudioListenerView(0f, 0f, 1f, 64, 64)))
            .Build();
        engine.EnterEditMode();

        Assert.Equal(1, engine.Phases.Count(EnginePhase.BuildRenderBuffer));

        _ = engine.RunOneTick();
        _ = engine.RunOneTick();

        Assert.Equal(2, backend.ListenerUpdates);
        Assert.Equal(0, engine.Context.Counters.AudioLoadedClips);
    }

    /// <summary>
    /// 验证 render-only 空事件帧仍推进 ambient 淡出并发布诊断。
    /// </summary>
    [Fact]
    public void AudioPhaseDriverAdvancesAmbientOnRenderOnlyEmptyFrames()
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
        using NullAudioBackend backend = new();
        using AudioSystem audio = new();
        audio.Initialize(settings, backend);
        MaterialAudioTable table = BuildAmbientTable();
        audio.AttachAmbientLoopManager(new AmbientLoopManager(backend, table, new BufferResolver(), settings));
        MpscRingBuffer<AudioEvent> ring = new(8);
        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.AmbientRegion, 4, 8, 1, 0.8f)));
        AudioDispatcher dispatcher = new(ring, audio.Voices, settings);
        MaterialAudioPlayer player = new(table, new BufferResolver(), settings);
        Engine engine = new EngineBuilder()
            .UseHeadless()
            .AddPhaseDriver(new AudioPhaseDriver(
                audio,
                _ => new AudioListenerView(0f, 0f, 1f, 64, 64),
                dispatcher,
                player))
            .Build();
        engine.EnterEditMode();

        _ = engine.RunOneTick();
        Assert.Equal(1, engine.Context.Counters.AudioActiveAmbientVoices);

        _ = engine.RunOneTick();
        _ = engine.RunOneTick();

        Assert.Equal(0, engine.Context.Counters.AudioActiveAmbientVoices);
        Assert.Equal(1, backend.StopCalls);
    }

    /// <summary>
    /// 验证 sim 降频时音频事件密度跟随 sim tick，render-only 帧仍更新 listener。
    /// </summary>
    [Fact]
    public void AudioPhaseDriverKeepsDispatchConsistentWithDownscaledSim()
    {
        AudioSettings settings = new()
        {
            MaxVoices = 2,
            MaxParticleImpactEventsPerFrame = 4,
            CoalesceBucketSize = 1,
            DefaultCooldownTicks = 0,
        };
        using NullAudioBackend backend = new();
        using AudioSystem audio = new();
        audio.Initialize(settings, backend);
        MpscRingBuffer<AudioEvent> ring = new(8);
        AudioDispatcher dispatcher = new(ring, audio.Voices, settings);
        CountingEventPlayer player = new();
        Engine engine = new EngineBuilder()
            .UseHeadless()
            .WithSimHz(EngineConstants.SimHzDownscaled)
            .OnPhase(EnginePhase.ParticleToCell, _ =>
            {
                Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, 0, 0, 1, 1f)));
            })
            .AddPhaseDriver(new AudioPhaseDriver(
                audio,
                _ => new AudioListenerView(0f, 0f, 1f, 64, 64),
                dispatcher,
                player))
            .Build();

        FrameTiming first = engine.RunOneTick();
        Assert.True(first.RunSim);
        Assert.Equal(1, engine.Context.Counters.AudioDrained);
        Assert.Equal(1, player.PlayedCount);

        FrameTiming second = engine.RunOneTick();

        Assert.False(second.RunSim);
        Assert.Equal(0, engine.Context.Counters.AudioDrained);
        Assert.Equal(1, player.PlayedCount);
        Assert.Equal(2, backend.ListenerUpdates);
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

    private sealed class BufferResolver : IAudioCueBufferResolver
    {
        public bool TryResolveBuffer(int cueHandle, out uint buffer)
        {
            buffer = (uint)cueHandle;
            return cueHandle > 0;
        }
    }

    private sealed class CountingEventPlayer : IAudioEventPlayer
    {
        public int PlayedCount { get; private set; }

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
