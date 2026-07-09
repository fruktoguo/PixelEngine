using PixelEngine.Core.Events;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Audio.Tests;

/// <summary>
/// 环境循环管理器测试：循环淡入淡出与并发上限。
/// </summary>
public sealed class AmbientLoopManagerTests
{
    /// <summary>
    /// 验证环境循环在滞回区间内淡入，离开后淡出。
    /// </summary>
    [Fact]
    public void AmbientLoopManagerFadesInHysteresisAndFadesOut()
    {
        using NullAudioBackend backend = new();
        MaterialAudioTable table = BuildTable(ambientCue: 9);
        BufferResolver buffers = new();
        AudioSettings settings = new()
        {
            MaxAmbientVoices = 1,
            AmbientEnterThreshold = 0.3f,
            AmbientExitThreshold = 0.2f,
            AmbientFadeRate = 0.5f,
            AmbientVolume = 0.25f,
        };
        using AmbientLoopManager manager = new(backend, table, buffers, settings);
        CoalescedAudioEvent ambient = new(AudioEventType.AmbientRegion, 0, 0, 1, 0.8f, 4);

        manager.Update([ambient]);
        Assert.Equal(0.2f, manager[0].TargetGain, precision: 5);
        manager.Update([]);
        manager.Update([]);

        Assert.Equal(1, backend.PlayCalls);
        Assert.Equal(1, backend.StopCalls);
        Assert.Equal(0, manager.ActiveVoiceCount);
        Assert.Equal(9, buffers.LastCueHandle);
    }

    /// <summary>
    /// 验证环境循环管理器忽略低于阈值And缺失提示音。
    /// </summary>
    [Fact]
    public void AmbientLoopManagerIgnoresBelowThresholdAndMissingCue()
    {
        using NullAudioBackend backend = new();
        BufferResolver buffers = new();
        using AmbientLoopManager manager = new(
            backend,
            BuildTable(ambientCue: 0),
            buffers,
            new AudioSettings { MaxAmbientVoices = 1, AmbientEnterThreshold = 0.5f });

        manager.Update([new CoalescedAudioEvent(AudioEventType.AmbientRegion, 0, 0, 1, 0.2f, 1)]);
        manager.Update([new CoalescedAudioEvent(AudioEventType.AmbientRegion, 0, 0, 1, 0.9f, 1)]);

        Assert.Equal(0, backend.PlayCalls);
        Assert.Equal(0, manager.ActiveVoiceCount);
    }

    /// <summary>
    /// 验证环境循环管理器可被禁用With零环境声道。
    /// </summary>
    [Fact]
    public void AmbientLoopManagerCanBeDisabledWithZeroAmbientVoices()
    {
        using NullAudioBackend backend = new();
        using AmbientLoopManager manager = new(
            backend,
            BuildTable(ambientCue: 9),
            new BufferResolver(),
            new AudioSettings { MaxAmbientVoices = 0 });

        manager.Update([new CoalescedAudioEvent(AudioEventType.AmbientRegion, 0, 0, 1, 1f, 1)]);

        Assert.Equal(0, backend.PlayCalls);
        Assert.Equal(0, manager.ActiveVoiceCount);
    }

    /// <summary>
    /// 验证环境循环管理器应用设置调整大小And更新阈值。
    /// </summary>
    [Fact]
    public void AmbientLoopManagerApplySettingsResizesAndUpdatesThresholds()
    {
        using NullAudioBackend backend = new();
        using AmbientLoopManager manager = new(
            backend,
            BuildTable(ambientCue: 9),
            new BufferResolver(),
            new AudioSettings
            {
                MaxAmbientVoices = 0,
                AmbientEnterThreshold = 0.9f,
                AmbientExitThreshold = 0.2f,
                AmbientFadeRate = 0.5f,
            });
        CoalescedAudioEvent ambient = new(AudioEventType.AmbientRegion, 0, 0, 1, 0.5f, 1);

        manager.Update([ambient]);
        manager.ApplySettings(new AudioSettings
        {
            MaxAmbientVoices = 1,
            AmbientEnterThreshold = 0.3f,
            AmbientExitThreshold = 0.2f,
            AmbientFadeRate = 0.5f,
        });
        manager.Update([ambient]);

        Assert.Equal(1, manager.ActiveVoiceCount);
        Assert.Equal(1, backend.PlayCalls);
    }

    private static MaterialAudioTable BuildTable(int ambientCue)
    {
        return MaterialAudioTable.FromDefinitions(
        [
            new() { Id = 0, Name = "empty", HeatCapacity = 1f },
            new()
            {
                Id = 1,
                Name = "water",
                HeatCapacity = 1f,
                AudioCues = new AudioCueSet { AmbientCue = ambientCue },
            },
        ]);
    }

    private sealed class BufferResolver : IAudioCueBufferResolver
    {
        public int LastCueHandle { get; private set; }

        public bool TryResolveBuffer(int cueHandle, out uint buffer)
        {
            LastCueHandle = cueHandle;
            buffer = (uint)cueHandle;
            return cueHandle > 0;
        }
    }
}
