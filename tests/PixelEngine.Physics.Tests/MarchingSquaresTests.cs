using System.Numerics;
using PixelEngine.Physics.Geometry;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// Marching Squares 外轮廓测试。
/// </summary>
public sealed class MarchingSquaresTests
{
    /// <summary>
    /// 验证单像素固体输出闭合 CCW 方形。
    /// </summary>
    [Fact]
    public void TraceOuterContourSinglePixelReturnsClosedSquare()
    {
        byte[] mask = [1];
        Span<Vector2> contour = stackalloc Vector2[MarchingSquares.GetMaximumContourPointCount(1, 1)];

        int written = MarchingSquares.TraceOuterContour(mask, 1, 1, contour);

        Assert.Equal(5, written);
        Assert.Equal(contour[0], contour[written - 1]);
        Assert.True(SignedArea(contour[..written]) > 0f);
    }

    /// <summary>
    /// 验证矩形固体区域输出其外包边界。
    /// </summary>
    [Fact]
    public void TraceOuterContourRectangleReturnsOuterBounds()
    {
        byte[] mask =
        [
            1, 1,
            1, 1,
        ];
        Span<Vector2> contour = stackalloc Vector2[MarchingSquares.GetMaximumContourPointCount(2, 2)];

        int written = MarchingSquares.TraceOuterContour(mask, 2, 2, contour);

        Assert.Equal(9, written);
        Assert.Contains(new Vector2(0, 0), contour[..written].ToArray());
        Assert.Contains(new Vector2(2, 0), contour[..written].ToArray());
        Assert.Contains(new Vector2(2, 2), contour[..written].ToArray());
        Assert.Contains(new Vector2(0, 2), contour[..written].ToArray());
    }

    /// <summary>
    /// 验证空 mask 没有轮廓。
    /// </summary>
    [Fact]
    public void TraceOuterContourEmptyMaskReturnsZero()
    {
        byte[] mask = [0, 0, 0, 0];
        Span<Vector2> contour = stackalloc Vector2[MarchingSquares.GetMaximumContourPointCount(2, 2)];

        int written = MarchingSquares.TraceOuterContour(mask, 2, 2, contour);

        Assert.Equal(0, written);
    }

    /// <summary>
    /// 验证带内孔 mask 会输出外轮廓和 CW 内孔。
    /// </summary>
    [Fact]
    public void TraceContoursReportsOuterAndHoleWinding()
    {
        byte[] mask =
        [
            1, 1, 1,
            1, 0, 1,
            1, 1, 1,
        ];
        Span<Vector2> points = stackalloc Vector2[MarchingSquares.GetMaximumContourPointCount(3, 3)];
        Span<ContourRange> ranges = stackalloc ContourRange[4];

        int count = MarchingSquares.TraceContours(mask, 3, 3, points, ranges);

        Assert.Equal(2, count);
        int outer = ranges[0].IsHole ? 1 : 0;
        int hole = ranges[0].IsHole ? 0 : 1;
        Assert.False(ranges[outer].IsHole);
        Assert.True(ranges[hole].IsHole);
        Assert.True(SignedArea(points.Slice(ranges[outer].Start, ranges[outer].Count)) > 0f);
        Assert.True(SignedArea(points.Slice(ranges[hole].Start, ranges[hole].Count)) < 0f);
    }

    private static float SignedArea(ReadOnlySpan<Vector2> contour)
    {
        float area = 0f;
        for (int i = 0; i < contour.Length - 1; i++)
        {
            Vector2 a = contour[i];
            Vector2 b = contour[i + 1];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        return area * 0.5f;
    }
}
