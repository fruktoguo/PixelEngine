using System.Numerics;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics.Geometry;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// ShapeBuilder 测试。
/// </summary>
public sealed class ShapeBuilderTests
{
    /// <summary>
    /// 验证凸片能创建动态 body、polygon shape，并应用质量。
    /// </summary>
    [Fact]
    public void BuildBodyCreatesDynamicBodyWithMass()
    {
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            Vector2[] polygon =
            [
                new(0, 0),
                new(16, 0),
                new(16, 16),
                new(0, 16),
            ];
            Span<ConvexPolygon> pieces = stackalloc ConvexPolygon[4];
            int pieceCount = ConvexDecomposer.Decompose(polygon, pieces);

            B2BodyId bodyId = ShapeBuilder.BuildBody(worldId, pieces[..pieceCount], Vector2.Zero, density: 1f);
            float mass = Box2D.b2Body_GetMass(bodyId);

            Assert.True(mass > 0f);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }
}
