using System.Buffers.Binary;
using PixelEngine.Audio;
using PixelEngine.Core;
using PixelEngine.Core.Diagnostics;
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
    /// 验证 Hosting 能从 content/audio 预加载 WAV，并把脚本音频 API 接到真实 AudioSystem。
    /// </summary>
    [Fact]
    public async Task EngineLoadsContentAudioAndInjectsScriptAudioApi()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-hosting-audio-{Guid.NewGuid():N}");
        try
        {
            string audioRoot = Path.Combine(contentRoot, "audio");
            _ = Directory.CreateDirectory(audioRoot);
            await File.WriteAllBytesAsync(
                Path.Combine(audioRoot, "ui_click.wav"),
                CreateWav(channels: 1, bitsPerSample: 8, sampleRate: 8_000, [128]));
            using NullAudioBackend backend = new();
            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .WithContentRoot(contentRoot)
                .Build();

            int loaded = await engine.AttachAudioFromContentAsync(backend);
            PixelEngine.Scripting.IAudioApi api = engine.Context.GetService<PixelEngine.Scripting.IAudioApi>();
            api.PlayOneShot("ui_click.wav");
            _ = engine.RunOneTick();

            Assert.Equal(1, loaded);
            Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.AudioService));
            Assert.Equal(1, engine.Context.GetService<AudioClipCache>().LoadedCount);
            Assert.Equal(1, engine.Phases.Count(EnginePhase.BuildRenderBuffer));
            Assert.Equal(1, backend.PlayCalls);
            Assert.Equal(1, engine.Context.Counters.AudioLoadedClips);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 Hosting 会按 audio/cues.json 把材质音频事件解析到已加载 clip buffer。
    /// </summary>
    [Fact]
    public async Task EngineMapsMaterialAudioCuesToLoadedClips()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-hosting-audio-cues-{Guid.NewGuid():N}");
        try
        {
            string audioRoot = Path.Combine(contentRoot, "audio");
            _ = Directory.CreateDirectory(audioRoot);
            await File.WriteAllBytesAsync(
                Path.Combine(audioRoot, "impact_sand.wav"),
                CreateWav(channels: 1, bitsPerSample: 8, sampleRate: 8_000, [128]));
            await File.WriteAllTextAsync(
                Path.Combine(audioRoot, "cues.json"),
                """
                {
                  "cues": [
                    { "handle": 1, "asset": "impact_sand.wav" }
                  ]
                }
                """);
            using NullAudioBackend backend = new();
            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .WithContentRoot(contentRoot)
                .Build();
            engine.Context.RegisterService(new MaterialTable(
            [
                new() { Id = 0, Name = "empty", Type = CellType.Empty, HeatCapacity = 1, TextureId = -1 },
                new()
                {
                    Id = 1,
                    Name = "sand",
                    Type = CellType.Powder,
                    Density = 90,
                    HeatCapacity = 1,
                    TextureId = -1,
                    AudioCues = new AudioCueSet { ImpactCue = 1 },
                },
            ]));

            int loaded = await engine.AttachAudioFromContentAsync(backend);
            Assert.True(engine.Context.TryGetService(out IAudioCueBufferResolver _));
            Assert.True(engine.Context.Events.Channel<AudioEvent>().TryEnqueue(
                new AudioEvent(AudioEventType.ParticleImpact, 4, 8, materialId: 1, magnitude: 1f)));

            _ = engine.RunOneTick();

            Assert.Equal(1, loaded);
            Assert.Equal(1, backend.PlayCalls);
            Assert.Equal(1, engine.Context.Counters.AudioPlayed);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

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

    /// <summary>
    /// 验证音频派发耗时写入 HUD 计数器和子阶段。
    /// </summary>
    [Fact]
    public void AudioPhaseDriverRecordsDispatchDiagnostics()
    {
        AudioSettings settings = new()
        {
            MaxVoices = 64,
            MaxParticleImpactEventsPerFrame = 32,
            MaxDrainedAudioEventsPerFrame = 64,
            CoalesceBucketSize = 1,
            DefaultCooldownTicks = 0,
        };
        using NullAudioBackend backend = new();
        using AudioSystem audio = new();
        audio.Initialize(settings, backend);
        MpscRingBuffer<AudioEvent> ring = new(128);
        AudioDispatcher dispatcher = new(ring, audio.Voices, settings);
        CountingEventPlayer player = new();
        Engine engine = new EngineBuilder()
            .UseHeadless()
            .AddPhaseDriver(new AudioPhaseDriver(
                audio,
                _ => new AudioListenerView(0f, 0f, 1f, 64, 64),
                dispatcher,
                player))
            .Build();
        engine.EnterEditMode();

        Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, -4, 0, 1, 1f)));
        _ = engine.RunOneTick();
        for (int i = 0; i < 32; i++)
        {
            Assert.True(ring.TryEnqueue(new AudioEvent(AudioEventType.ParticleImpact, i * 4, 0, 1, 1f)));
        }

        _ = engine.RunOneTick();

        double profiled = engine.Context.Profiler.LastSubFrame[(int)FrameSubPhase.AudioDispatch];
        Assert.Equal(32, engine.Context.Counters.AudioDrained);
        Assert.Equal(32, engine.Context.Counters.AudioPlayed);
        Assert.True(engine.Context.Counters.AudioDispatchMilliseconds > 0);
        Assert.True(profiled > 0);
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

    private static byte[] CreateWav(short channels, short bitsPerSample, int sampleRate, ReadOnlySpan<byte> pcm)
    {
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        byte[] wav = new byte[44 + pcm.Length];
        WriteAscii(wav, 0, "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4, 4), 36 + pcm.Length);
        WriteAscii(wav, 8, "WAVE");
        WriteAscii(wav, 12, "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22, 2), channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32, 2), blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34, 2), bitsPerSample);
        WriteAscii(wav, 36, "data");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40, 4), pcm.Length);
        pcm.CopyTo(wav.AsSpan(44));
        return wav;
    }

    private static void WriteAscii(byte[] target, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            target[offset + i] = (byte)text[i];
        }
    }
}
