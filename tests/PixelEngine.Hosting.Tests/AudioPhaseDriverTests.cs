using PixelEngine.Audio;
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
}
