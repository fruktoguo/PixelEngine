using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PixelEngine.Interop.Box2D;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// Box2D v3.1 C ABI 薄绑定测试。
/// 不变式：blittable 结构体尺寸与 vendored Box2D v3.1 header 对齐。
/// </summary>
public sealed unsafe class Box2DInteropTests
{
    /// <summary>
    /// 验证关键 blittable 结构体尺寸与 vendored Box2D v3.1.1 header 对齐。
    /// </summary>
    [Fact]
    public void Box2DStructSizesMatchVendoredHeader()
    {
        Assert.Equal(4, Unsafe.SizeOf<B2WorldId>());
        Assert.Equal(8, Unsafe.SizeOf<B2BodyId>());
        Assert.Equal(8, Unsafe.SizeOf<B2ShapeId>());
        Assert.Equal(8, Unsafe.SizeOf<B2ChainId>());
        Assert.Equal(8, Unsafe.SizeOf<B2Vec2>());
        Assert.Equal(8, Unsafe.SizeOf<B2Rot>());
        Assert.Equal(16, Unsafe.SizeOf<B2Transform>());
        Assert.Equal(16, Unsafe.SizeOf<B2AABB>());
        Assert.Equal(68, Unsafe.SizeOf<B2Hull>());
        Assert.Equal(144, Unsafe.SizeOf<B2Polygon>());
        Assert.Equal(16, Unsafe.SizeOf<B2Segment>());
        Assert.Equal(96, Unsafe.SizeOf<B2WorldDef>());
        Assert.Equal(80, Unsafe.SizeOf<B2BodyDef>());
        Assert.Equal(80, Unsafe.SizeOf<B2ShapeDef>());
        Assert.Equal(72, Unsafe.SizeOf<B2ChainDef>());
        Assert.Equal(40, Unsafe.SizeOf<B2ContactEvents>());
        Assert.Equal(16, Unsafe.SizeOf<B2BodyEvents>());
    }

    /// <summary>
    /// 验证绑定层声明使用 LibraryImport，且没有误加 SuppressGCTransition。
    /// </summary>
    [Fact]
    public void Box2DFunctionsDeclareLibraryImportWithoutSuppressGcTransition()
    {
        MethodInfo[] methods = typeof(Box2D).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            Assert.NotNull(method.GetCustomAttribute<LibraryImportAttribute>());
            Assert.Null(method.GetCustomAttribute<SuppressGCTransitionAttribute>());
        });
    }

    /// <summary>
    /// 验证 win-x64 动态库路径下可创建 world、dynamic body、polygon shape，并在 step 后受重力移动。
    /// </summary>
    [Fact]
    public void Box2DWorldCanStepDynamicBody()
    {
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 10f };
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            B2BodyDef bodyDef = Box2D.b2DefaultBodyDef();
            bodyDef.Type = B2BodyType.DynamicBody;
            bodyDef.Position = new B2Vec2 { X = 0f, Y = 0f };
            B2BodyId bodyId = Box2D.b2CreateBody(worldId, in bodyDef);

            B2Vec2* points = stackalloc B2Vec2[4];
            points[0] = new B2Vec2 { X = -0.5f, Y = -0.5f };
            points[1] = new B2Vec2 { X = 0.5f, Y = -0.5f };
            points[2] = new B2Vec2 { X = 0.5f, Y = 0.5f };
            points[3] = new B2Vec2 { X = -0.5f, Y = 0.5f };

            B2Hull hull = Box2D.b2ComputeHull(points, 4);
            Assert.Equal(4, hull.Count);
            B2Polygon polygon = PhysicsScale.MakeSharpPolygon(in hull);
            Assert.Equal(0f, polygon.Radius);

            B2ShapeDef shapeDef = Box2D.b2DefaultShapeDef();
            shapeDef.Density = 1f;
            _ = Box2D.b2CreatePolygonShape(bodyId, in shapeDef, in polygon);
            Box2D.b2Body_ApplyMassFromShapes(bodyId);

            Box2D.b2World_Step(worldId, 1f / 60f, subStepCount: 4);
            B2Vec2 position = Box2D.b2Body_GetPosition(bodyId);

            Assert.True(position.Y > 0f);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }
}
