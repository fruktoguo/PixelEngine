using System.Numerics;
using PixelEngine.Core.Events;
using Xunit;

namespace PixelEngine.Audio.Tests;

/// <summary>
/// 音频声道池测试：借还、上限与饥饿策略。
/// </summary>
public sealed class AudioVoicePoolTests
{
    /// <summary>
    /// 验证声道池预分配声源And复用已停止声道。
    /// </summary>
    [Fact]
    public void VoicePoolPreallocatesSourcesAndReusesStoppedVoices()
    {
        using NullAudioBackend backend = new();
        using AudioVoicePool pool = new(backend, new AudioSettings { MaxVoices = 2 });

        AudioVoice? first = pool.Acquire(1, AudioEventType.ParticleImpact, Vector3.Zero, Vector3.Zero, 1);
        Assert.NotNull(first);
        first.Play(1, 1f, 1f);
        first.Stop();

        AudioVoice? second = pool.Acquire(1, AudioEventType.ParticleImpact, Vector3.Zero, Vector3.Zero, 2);

        Assert.Same(first, second);
        Assert.Equal(1, backend.StopCalls);
    }

    /// <summary>
    /// 验证声道池释放全部预分配声源用于泄漏取证。
    /// </summary>
    [Fact]
    public void VoicePoolDisposesAllPreallocatedSourcesForLeakEvidence()
    {
        using NullAudioBackend backend = new();
        AudioVoicePool pool = new(backend, new AudioSettings { MaxVoices = 3 });

        Assert.Equal(3, backend.LiveSourceCount);

        pool.Dispose();

        Assert.Equal(0, backend.LiveSourceCount);
        Assert.Equal(0, backend.LiveObjectCount);
    }

    /// <summary>
    /// 验证声道池窃取更低优先级更远声道当池满时。
    /// </summary>
    [Fact]
    public void VoicePoolStealsLowerPriorityFartherVoiceWhenFull()
    {
        using NullAudioBackend backend = new();
        using AudioVoicePool pool = new(backend, new AudioSettings { MaxVoices = 2 });
        AudioVoice near = pool.Acquire(1, AudioEventType.ParticleImpact, new Vector3(1f, 0f, 0f), Vector3.Zero, 1)!;
        near.Play(1, 1f, 1f);
        AudioVoice far = pool.Acquire(1, AudioEventType.ParticleImpact, new Vector3(100f, 0f, 0f), Vector3.Zero, 2)!;
        far.Play(1, 1f, 1f);

        AudioVoice? stolen = pool.Acquire(2, AudioEventType.Explosion, new Vector3(0f, 1f, 0f), Vector3.Zero, 3);

        Assert.Same(far, stolen);
        Assert.Equal(1, pool.StealCount);
        Assert.Equal(1, backend.StopCalls);
    }

    /// <summary>
    /// 验证声道池拒绝更低优先级当全部声道优先级更高时。
    /// </summary>
    [Fact]
    public void VoicePoolRejectsLowerPriorityWhenAllVoicesAreHigherPriority()
    {
        using NullAudioBackend backend = new();
        using AudioVoicePool pool = new(backend, new AudioSettings { MaxVoices = 1 });
        AudioVoice voice = pool.Acquire(10, AudioEventType.Explosion, Vector3.Zero, Vector3.Zero, 1)!;
        voice.Play(1, 1f, 1f);

        AudioVoice? rejected = pool.Acquire(1, AudioEventType.ParticleImpact, Vector3.Zero, Vector3.Zero, 2);

        Assert.Null(rejected);
        Assert.Equal(1, pool.DroppedVoiceCount);
        Assert.Equal(0, pool.StealCount);
    }

    /// <summary>
    /// 验证空后端记录监听器与播放。
    /// </summary>
    [Fact]
    public void NullBackendRecordsListenerAndPlayback()
    {
        using NullAudioBackend backend = new();
        uint source = backend.CreateSource();
        uint buffer = backend.CreateBuffer();
        AudioListenerState listener = new(Vector3.One, new Vector3(0f, 0f, -1f), Vector3.UnitY, 0.5f);

        backend.SetListener(listener);
        backend.Play(source, 7, new Vector3(2f, 3f, 0f), 0.8f, 1.2f);

        Assert.Equal(listener, backend.LastListener);
        Assert.Equal(1, backend.ListenerUpdates);
        Assert.Equal(1, backend.PlayCalls);
        Assert.Equal(AudioSourceState.Playing, backend.GetState(source));
        Assert.Equal(1, backend.LiveSourceCount);
        Assert.Equal(1, backend.LiveBufferCount);
        backend.DeleteSource(source);
        backend.DeleteBuffer(buffer);
        Assert.Equal(0, backend.LiveObjectCount);
    }
}
