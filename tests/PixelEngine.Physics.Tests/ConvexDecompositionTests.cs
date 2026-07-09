using System.Numerics;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics.Geometry;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// plan/14 凸分解验收测试。
/// 不变式：凹形状分解后可被 Box2D 稳定接受。
/// </summary>
public sealed unsafe class ConvexDecompositionTests
{
    /// <summary>
    /// 验证凹多边形拆分后每片 ≤8 顶点、严格凸，且面积并集覆盖原 mask 轮廓。
    /// </summary>
    [Fact]
    public void ConcaveMaskDecomposesIntoSmallConvexPiecesCoveringOriginalArea()
    {
        Vector2[] polygon =
        [
            new(0, 0),
            new(5, 0),
            new(5, 2),
            new(2, 2),
            new(2, 5),
            new(0, 5),
        ];
        Span<ConvexPolygon> pieces = stackalloc ConvexPolygon[16];
        Span<Vector2> vertices = stackalloc Vector2[Box2DConstants.MaxPolygonVertices];

        int pieceCount = ConvexDecomposer.Decompose(polygon, pieces);

        Assert.InRange(pieceCount, 2, 8);
        float pieceArea = 0f;
        for (int i = 0; i < pieceCount; i++)
        {
            Assert.InRange(pieces[i].Count, 3, Box2DConstants.MaxPolygonVertices);
            int count = pieces[i].CopyTo(vertices);
            Assert.True(ConvexDecomposer.IsConvex(vertices[..count]));
            pieceArea += Math.Abs(SignedArea(vertices[..count]));
        }

        Assert.Equal(Math.Abs(SignedArea(polygon)), pieceArea, precision: 4);
    }

    /// <summary>
    /// 验证退化重复顶点输入走健壮回退，不崩溃且输出仍为合法凸片。
    /// </summary>
    [Fact]
    public void DegenerateRepeatedVerticesUseRobustFallback()
    {
        Vector2[] polygon =
        [
            new(0, 0),
            new(4, 0),
            new(4, 0),
            new(4, 1),
            new(1, 1),
            new(1, 4),
            new(0, 4),
        ];
        Span<ConvexPolygon> pieces = stackalloc ConvexPolygon[16];
        Span<Vector2> vertices = stackalloc Vector2[Box2DConstants.MaxPolygonVertices];

        int pieceCount = ConvexDecomposer.Decompose(polygon, pieces);

        Assert.InRange(pieceCount, 1, 8);
        for (int i = 0; i < pieceCount; i++)
        {
            Assert.InRange(pieces[i].Count, 3, Box2DConstants.MaxPolygonVertices);
            int count = pieces[i].CopyTo(vertices);
            Assert.True(ConvexDecomposer.IsConvex(vertices[..count]));
        }
    }

    /// <summary>
    /// 验证 PixelEngine 的 Box2D 多边形创建半径恒为 0，保持像素锐利边缘。
    /// </summary>
    [Fact]
    public void MakeSharpPolygonUsesZeroRadius()
    {
        B2Vec2* points = stackalloc B2Vec2[4];
        points[0] = new B2Vec2 { X = 0, Y = 0 };
        points[1] = new B2Vec2 { X = 1, Y = 0 };
        points[2] = new B2Vec2 { X = 1, Y = 1 };
        points[3] = new B2Vec2 { X = 0, Y = 1 };
        B2Hull hull = Box2D.b2ComputeHull(points, 4);

        B2Polygon polygon = PhysicsScale.MakeSharpPolygon(in hull);

        Assert.Equal(4, polygon.Count);
        Assert.Equal(0f, polygon.Radius);
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
