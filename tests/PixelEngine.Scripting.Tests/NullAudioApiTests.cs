using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 无音频内容项目的显式脚本后端测试。
/// </summary>
public sealed class NullAudioApiTests
{
    /// <summary>
    /// 验证合法播放请求保持无副作用且共享单例稳定。
    /// </summary>
    [Fact]
    public void ValidPlaybackRequestsAreIntentionalNoOps()
    {
        NullAudioApi audio = NullAudioApi.Instance;

        audio.PlayOneShot("ui.confirm", 0.5f);
        audio.PlayAt("world.impact", 12f, -4f, 1.25f);

        Assert.Same(audio, NullAudioApi.Instance);
    }

    /// <summary>
    /// 验证非法 cue、坐标和音量保留精确参数诊断。
    /// </summary>
    [Fact]
    public void InvalidRequestsRetainPreciseParameterDiagnostics()
    {
        _ = Assert.Throws<ArgumentException>(() => NullAudioApi.Instance.PlayOneShot(" "));

        ArgumentOutOfRangeException x = Assert.Throws<ArgumentOutOfRangeException>(
            () => NullAudioApi.Instance.PlayAt("impact", float.NaN, 0f));
        ArgumentOutOfRangeException y = Assert.Throws<ArgumentOutOfRangeException>(
            () => NullAudioApi.Instance.PlayAt("impact", 0f, float.PositiveInfinity));
        ArgumentOutOfRangeException volume = Assert.Throws<ArgumentOutOfRangeException>(
            () => NullAudioApi.Instance.PlayOneShot("impact", -0.1f));

        Assert.Equal("x", x.ParamName);
        Assert.Equal("y", y.ParamName);
        Assert.Equal("volume", volume.ParamName);
    }
}
