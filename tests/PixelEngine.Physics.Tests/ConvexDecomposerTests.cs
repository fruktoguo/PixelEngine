using System.Numerics;
using PixelEngine.Physics.Geometry;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// 凸分解测试。
/// </summary>
public sealed class ConvexDecomposerTests
{
    /// <summary>
    /// 验证凸四边形保留为单个凸片。
    /// </summary>
    [Fact]
    public void DecomposeConvexQuadReturnsSinglePiece()
    {
        Vector2[] polygon =
        [
            new(0, 0),
            new(2, 0),
            new(2, 2),
            new(0, 2),
        ];
        Span<ConvexPolygon> pieces = stackalloc ConvexPolygon[8];

        int count = ConvexDecomposer.Decompose(polygon, pieces);

        Assert.Equal(1, count);
        Assert.Equal(4, pieces[0].Count);
    }

    /// <summary>
    /// 验证凹 L 形可以拆为凸片。
    /// </summary>
    [Fact]
    public void DecomposeConcavePolygonReturnsConvexPieces()
    {
        Vector2[] polygon =
        [
            new(0, 0),
            new(3, 0),
            new(3, 1),
            new(1, 1),
            new(1, 3),
            new(0, 3),
        ];
        Span<ConvexPolygon> pieces = stackalloc ConvexPolygon[8];
        Span<Vector2> vertices = stackalloc Vector2[8];

        int count = ConvexDecomposer.Decompose(polygon, pieces);
        float totalArea = 0f;

        Assert.InRange(count, 2, 4);
        for (int i = 0; i < count; i++)
        {
            Assert.InRange(pieces[i].Count, 3, 8);
            int written = pieces[i].CopyTo(vertices);
            Assert.True(ConvexDecomposer.IsConvex(vertices[..written]));
            totalArea += Math.Abs(SignedArea(vertices[..written]));
        }

        Assert.Equal(Math.Abs(SignedArea(polygon)), totalArea, precision: 4);
    }

    /// <summary>
    /// 验证顺时针输入会被规范化。
    /// </summary>
    [Fact]
    public void DecomposeAcceptsClockwiseInput()
    {
        Vector2[] polygon =
        [
            new(0, 0),
            new(0, 2),
            new(2, 2),
            new(2, 0),
        ];
        Span<ConvexPolygon> pieces = stackalloc ConvexPolygon[8];

        int count = ConvexDecomposer.Decompose(polygon, pieces);

        Assert.Equal(1, count);
        Assert.Equal(4, pieces[0].Count);
    }

    private static float SignedArea(ReadOnlySpan<Vector2> polygon)
    {
        float area = 0f;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Length];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        return area * 0.5f;
    }
}
