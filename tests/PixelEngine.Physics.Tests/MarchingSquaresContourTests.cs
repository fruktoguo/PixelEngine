using System.Numerics;
using PixelEngine.Physics.Geometry;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// plan/14 Marching Squares 轮廓验收测试。
/// 不变式：轮廓顶点闭合、与参考栅格边界对齐。
/// </summary>
public sealed class MarchingSquaresContourTests
{
    /// <summary>
    /// 验证 2x2 的 16 种 case 都能生成闭合像素分辨率轮廓，空 case 除外。
    /// </summary>
    [Fact]
    public void AllSixteenCasesProduceClosedPixelContours()
    {
        Vector2[] points = new Vector2[MarchingSquares.GetMaximumContourPointCount(2, 2)];
        ContourRange[] ranges = new ContourRange[4];
        for (int maskBits = 0; maskBits < 16; maskBits++)
        {
            byte[] mask =
            [
                (byte)((maskBits & 0b0001) != 0 ? 1 : 0),
                (byte)((maskBits & 0b0010) != 0 ? 1 : 0),
                (byte)((maskBits & 0b0100) != 0 ? 1 : 0),
                (byte)((maskBits & 0b1000) != 0 ? 1 : 0),
            ];

            int rangeCount = MarchingSquares.TraceContours(mask, 2, 2, points, ranges);

            if (maskBits == 0)
            {
                Assert.Equal(0, rangeCount);
                continue;
            }

            Assert.InRange(rangeCount, 1, 2);
            for (int i = 0; i < rangeCount; i++)
            {
                ContourRange range = ranges[i];
                ReadOnlySpan<Vector2> contour = points.AsSpan(range.Start, range.Count);
                Assert.True(range.Count >= 5);
                Assert.Equal(contour[0], contour[^1]);
                Assert.NotEqual(0f, SignedArea(contour));
            }
        }
    }

    /// <summary>
    /// 验证带孔和多连通输入会输出多个闭合轮廓，并正确标记孔洞方向。
    /// </summary>
    [Fact]
    public void HolesAndMultipleComponentsProduceSeparateClosedContours()
    {
        byte[] mask =
        [
            1, 1, 1, 0, 1,
            1, 0, 1, 0, 1,
            1, 1, 1, 0, 1,
        ];
        Span<Vector2> points = stackalloc Vector2[MarchingSquares.GetMaximumContourPointCount(5, 3)];
        Span<ContourRange> ranges = stackalloc ContourRange[8];

        int rangeCount = MarchingSquares.TraceContours(mask, 5, 3, points, ranges);

        Assert.Equal(3, rangeCount);
        Assert.Equal(2, CountRanges(ranges[..rangeCount], isHole: false));
        Assert.Equal(1, CountRanges(ranges[..rangeCount], isHole: true));
        for (int i = 0; i < rangeCount; i++)
        {
            ReadOnlySpan<Vector2> contour = points.Slice(ranges[i].Start, ranges[i].Count);
            Assert.Equal(contour[0], contour[^1]);
        }
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

    private static int CountRanges(ReadOnlySpan<ContourRange> ranges, bool isHole)
    {
        int count = 0;
        for (int i = 0; i < ranges.Length; i++)
        {
            if (ranges[i].IsHole == isHole)
            {
                count++;
            }
        }

        return count;
    }
}
