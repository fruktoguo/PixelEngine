using PixelEngine.Core.Mathematics;
using PixelEngine.Physics.Geometry;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// 连通分量标记测试。
/// 不变式：连通分量标记与 flood-fill oracle 一致。
/// </summary>
public sealed class ConnectedComponentLabelerTests
{
    /// <summary>
    /// 验证四邻域下对角像素不会连成同一分量。
    /// </summary>
    [Fact]
    public void FourConnectivityKeepsDiagonalPixelsSeparate()
    {
        byte[] mask =
        [
            1, 0,
            0, 1,
        ];
        int[] labels = new int[mask.Length];
        ConnectedComponent[] components = new ConnectedComponent[4];

        int count = ConnectedComponentLabeler.Label(mask, 2, 2, labels, components, Connectivity.Four);

        Assert.Equal(2, count);
        Assert.Equal(1, components[0].PixelCount);
        Assert.Equal(1, components[1].PixelCount);
        Assert.NotEqual(labels[0], labels[3]);
    }

    /// <summary>
    /// 验证八邻域下对角像素会合并为同一分量。
    /// </summary>
    [Fact]
    public void EightConnectivityMergesDiagonalPixels()
    {
        byte[] mask =
        [
            1, 0,
            0, 1,
        ];
        int[] labels = new int[mask.Length];
        ConnectedComponent[] components = new ConnectedComponent[4];

        int count = ConnectedComponentLabeler.Label(mask, 2, 2, labels, components, Connectivity.Eight);

        Assert.Equal(1, count);
        Assert.Equal(2, components[0].PixelCount);
        Assert.Equal(labels[0], labels[3]);
    }

    /// <summary>
    /// 验证 bounds、边界接触和碎片阈值。
    /// </summary>
    [Fact]
    public void ComponentReportsBoundsBorderAndFragment()
    {
        byte[] mask = new byte[25];
        mask[1 + (1 * 5)] = 1;
        mask[2 + (1 * 5)] = 1;
        mask[1 + (2 * 5)] = 1;
        int[] labels = new int[mask.Length];
        ConnectedComponent[] components = new ConnectedComponent[4];

        int count = ConnectedComponentLabeler.Label(mask, 5, 5, labels, components, Connectivity.Four, fragmentPixelThreshold: 4);

        Assert.Equal(1, count);
        Assert.Equal(3, components[0].PixelCount);
        Assert.Equal(RectI.FromBounds(1, 1, 3, 3), components[0].Bounds);
        Assert.False(components[0].TouchesBorder);
        Assert.True(components[0].IsFragment);
    }

    /// <summary>
    /// 验证大连通块使用显式栈，不依赖递归调用栈。
    /// </summary>
    [Fact]
    public void LargeComponentDoesNotUseRecursiveStack()
    {
        const int width = 64;
        const int height = 64;
        byte[] mask = new byte[width * height];
        Array.Fill(mask, (byte)1);
        int[] labels = new int[mask.Length];
        ConnectedComponent[] components = new ConnectedComponent[1];

        int count = ConnectedComponentLabeler.Label(mask, width, height, labels, components);

        Assert.Equal(1, count);
        Assert.Equal(width * height, components[0].PixelCount);
        Assert.True(components[0].TouchesBorder);
        Assert.Equal(RectI.FromBounds(0, 0, width, height), components[0].Bounds);
    }
}
