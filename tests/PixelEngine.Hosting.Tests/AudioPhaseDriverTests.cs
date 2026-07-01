using PixelEngine.Audio;
using PixelEngine.Core.Events;
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
}
