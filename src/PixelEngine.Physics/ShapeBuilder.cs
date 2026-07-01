using System.Numerics;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics.Geometry;

namespace PixelEngine.Physics;

/// <summary>
/// 将像素轮廓凸片构建为 Box2D 复合刚体。
/// </summary>
public static unsafe class ShapeBuilder
{
    /// <summary>
    /// 创建动态 body 并挂载全部凸片 shape。
    /// </summary>
    /// <param name="worldId">Box2D world。</param>
    /// <param name="convexPieces">≤8 顶点凸片。</param>
    /// <param name="bodyPositionPixels">body 原点像素坐标。</param>
    /// <param name="density">shape 密度。</param>
    /// <returns>body 句柄。</returns>
    public static B2BodyId BuildBody(
        B2WorldId worldId,
        ReadOnlySpan<ConvexPolygon> convexPieces,
        Vector2 bodyPositionPixels,
        float density = 1f)
    {
        if (convexPieces.IsEmpty)
        {
            throw new ArgumentException("至少需要一个凸片。", nameof(convexPieces));
        }

        if (density <= 0f || float.IsNaN(density) || float.IsInfinity(density))
        {
            throw new ArgumentOutOfRangeException(nameof(density), density, "density 必须是有限正数。");
        }

        B2BodyDef bodyDef = Box2D.b2DefaultBodyDef();
        bodyDef.Type = B2BodyType.DynamicBody;
        bodyDef.Position = new B2Vec2
        {
            X = PhysicsScale.PixelToPhysics(bodyPositionPixels.X),
            Y = PhysicsScale.PixelToPhysics(bodyPositionPixels.Y),
        };
        B2BodyId bodyId = Box2D.b2CreateBody(worldId, in bodyDef);

        B2ShapeDef shapeDef = Box2D.b2DefaultShapeDef();
        shapeDef.Density = density;

        int createdShapes = 0;
        Span<Vector2> vertices = stackalloc Vector2[Box2DConstants.MaxPolygonVertices];
        B2Vec2* hullPoints = stackalloc B2Vec2[Box2DConstants.MaxPolygonVertices];

        for (int i = 0; i < convexPieces.Length; i++)
        {
            ConvexPolygon piece = convexPieces[i];
            int vertexCount = piece.CopyTo(vertices);
            for (int j = 0; j < vertexCount; j++)
            {
                hullPoints[j] = new B2Vec2
                {
                    X = PhysicsScale.PixelToPhysics(vertices[j].X),
                    Y = PhysicsScale.PixelToPhysics(vertices[j].Y),
                };
            }

            B2Hull hull = Box2D.b2ComputeHull(hullPoints, vertexCount);
            if (hull.Count < 3)
            {
                continue;
            }

            B2Polygon polygon = PhysicsScale.MakeSharpPolygon(in hull);
            _ = Box2D.b2CreatePolygonShape(bodyId, in shapeDef, in polygon);
            createdShapes++;
        }

        if (createdShapes == 0)
        {
            Box2D.b2DestroyBody(bodyId);
            throw new InvalidOperationException("所有凸片均退化，无法创建 Box2D shape。");
        }

        Box2D.b2Body_ApplyMassFromShapes(bodyId);
        return bodyId;
    }
}
