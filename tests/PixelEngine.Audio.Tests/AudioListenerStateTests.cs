using System.Numerics;
using Xunit;

namespace PixelEngine.Audio.Tests;

/// <summary>
/// 音频监听器状态测试：位置/朝向更新与平滑。
/// </summary>
public sealed class AudioListenerStateTests
{
    /// <summary>
    /// 验证音频空间将单元格坐标转换为米。
    /// </summary>
    [Fact]
    public void AudioSpaceConvertsCellsToMeters()
    {
        AudioSpace space = new(32f);

        Vector3 meters = space.ToMeters(64f, -16f);

        Assert.Equal(new Vector3(2f, -0.5f, 0f), meters);
    }

    /// <summary>
    /// 验证监听器状态使用视口中心And与配置的深度。
    /// </summary>
    [Fact]
    public void ListenerStateUsesViewportCenterAndConfiguredDepth()
    {
        AudioSettings settings = new()
        {
            PixelsPerMeter = 16f,
            ListenerDepth = 4f,
            MasterVolume = 0.75f,
        };
        AudioListenerView view = new(32f, 64f, 2f, 20, 10);

        AudioListenerState listener = AudioListenerState.FromView(view, settings);

        Assert.Equal(new Vector3(3.25f, 4.625f, 4f), listener.Position);
        Assert.Equal(new Vector3(0f, 0f, -1f), listener.Forward);
        Assert.Equal(Vector3.UnitY, listener.Up);
        Assert.Equal(0.75f, listener.Gain);
    }

    /// <summary>
    /// 验证监听视图拒绝无效视口。
    /// </summary>
    [Fact]
    public void ListenerViewRejectsInvalidViewport()
    {
        AudioSettings settings = new();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => AudioListenerState.FromView(new AudioListenerView(0f, 0f, 0f, 1, 1), settings));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => AudioListenerState.FromView(new AudioListenerView(0f, 0f, 1f, 0, 1), settings));
    }
}
