using System.Numerics;
using Xunit;

namespace PixelEngine.Audio.Tests;

public sealed class AudioListenerStateTests
{
    [Fact]
    public void AudioSpaceConvertsCellsToMeters()
    {
        AudioSpace space = new(32f);

        Vector3 meters = space.ToMeters(64f, -16f);

        Assert.Equal(new Vector3(2f, -0.5f, 0f), meters);
    }

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

    [Fact]
    public void ListenerViewRejectsInvalidViewport()
    {
        AudioSettings settings = new();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => AudioListenerState.FromView(new AudioListenerView(0f, 0f, 0f, 1, 1), settings));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => AudioListenerState.FromView(new AudioListenerView(0f, 0f, 1f, 0, 1), settings));
    }
}
