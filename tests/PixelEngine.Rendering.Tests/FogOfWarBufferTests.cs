using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class FogOfWarBufferTests
{
    [Fact]
    public void ConstructorMapsViewportCellsToCeilTileGrid()
    {
        FogOfWarBuffer fog = new(65, 33);

        Assert.Equal(65, fog.ViewportCellWidth);
        Assert.Equal(33, fog.ViewportCellHeight);
        Assert.Equal(32, fog.TileSize);
        Assert.Equal(3, fog.TileWidth);
        Assert.Equal(2, fog.TileHeight);
        Assert.Equal(6, fog.Length);
    }

    [Fact]
    public void RevealCircleMapsCellsToCoarseTiles()
    {
        FogOfWarBuffer fog = new(96, 64);

        fog.RevealCircle(centerCellX: 32, centerCellY: 10, radiusCells: 0, revealAlpha: 128);

        Assert.False(fog.IsRevealed(31, 10));
        Assert.True(fog.IsRevealed(32, 10));
        Assert.Equal(128, fog.RevealAlpha(63, 31));
        Assert.Equal(0, fog.RevealAlpha(64, 31));
    }

    [Fact]
    public void RevealCircleClipsToViewportBoundary()
    {
        FogOfWarBuffer fog = new(64, 64, tileSize: 16);

        fog.RevealCircle(centerCellX: -4, centerCellY: 8, radiusCells: 12);

        Assert.True(fog.IsRevealed(0, 0));
        Assert.True(fog.IsRevealed(15, 15));
        Assert.False(fog.IsRevealed(16, 0));
        Assert.Equal(0, fog.RevealAlpha(-1, 0));
        Assert.Equal(0, fog.RevealAlpha(0, 64));
    }

    [Fact]
    public void RevealAlphaKeepsLargestRevealAndClearResets()
    {
        FogOfWarBuffer fog = new(32, 32, tileSize: 8);

        fog.RevealCircle(centerCellX: 4, centerCellY: 4, radiusCells: 0, revealAlpha: 80);
        fog.RevealCircle(centerCellX: 4, centerCellY: 4, radiusCells: 0, revealAlpha: 20);

        Assert.True(fog.IsRevealed(7, 7));
        Assert.Equal(80, fog.RevealAlpha(0, 0));
        fog.RevealCircle(centerCellX: 4, centerCellY: 4, radiusCells: 0, revealAlpha: 120);
        Assert.Equal(120, fog.RevealAlpha(7, 7));
        fog.Clear();
        Assert.False(fog.IsRevealed(7, 7));
        Assert.Equal(0, fog.RevealAlpha(7, 7));
    }

    [Fact]
    public void ValidationRejectsInvalidSizesAndRadius()
    {
        AssertThrows<ArgumentOutOfRangeException>(() => new FogOfWarBuffer(0, 1));
        AssertThrows<ArgumentOutOfRangeException>(() => new FogOfWarBuffer(1, 0));
        AssertThrows<ArgumentOutOfRangeException>(() => new FogOfWarBuffer(1, 1, 0));

        FogOfWarBuffer fog = new(8, 8);

        AssertThrows<ArgumentOutOfRangeException>(() => fog.RevealCircle(0, 0, -1));
    }

    private static void AssertThrows<T>(Action action)
        where T : Exception
    {
        T exception = Assert.Throws<T>(action);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }
}
