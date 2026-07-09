using System.Numerics;
using PixelEngine.Interop.Box2D;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// 刚体数据结构测试。
/// 不变式：刚体数据布局与序列化字段稳定。
/// </summary>
public sealed unsafe class RigidBodyDataTests
{
    /// <summary>
    /// 验证 BodyLocalMask 会复制输入并保持只读权威形状。
    /// </summary>
    [Fact]
    public void BodyLocalMaskCopiesInputBuffers()
    {
        byte[] solid = [1, 0, 1, 1];
        ushort[] materials = [2, 0, 3, 4];
        BodyLocalMask mask = new(2, 2, Vector2.One, solid, materials);
        solid[0] = 0;
        materials[0] = 9;

        Assert.Equal(2, mask.Width);
        Assert.Equal(2, mask.Height);
        Assert.Equal(Vector2.One, mask.LocalOrigin);
        Assert.True(mask.IsSolid(0, 0));
        Assert.False(mask.IsSolid(1, 0));
        Assert.Equal((ushort)2, mask.MaterialAt(0, 0));
    }

    /// <summary>
    /// 验证 stamp registry 可重建 world cell 到 body-local 像素的映射。
    /// </summary>
    [Fact]
    public void RigidStampRegistryMapsWorldCellToLocalPixel()
    {
        RigidStampRegistry registry = new();
        RigidStamp stamp = new(3, 4, 5, 7);

        registry.Register(-2, 8, in stamp);

        Assert.True(registry.TryGet(-2, 8, out RigidStamp actual));
        Assert.Equal(stamp, actual);
        registry.Clear();
        Assert.False(registry.TryGet(-2, 8, out _));
    }

    /// <summary>
    /// 验证 damage queue 实现 IRigidDamageSink 并可排空。
    /// </summary>
    [Fact]
    public void RigidDamageQueueEnqueuesAndDrainsDamageEvents()
    {
        RigidDamageQueue queue = new(capacityPow2: 4);
        queue.OnOwnedCellDamaged(10, 20);
        queue.OnOwnedCellDamaged(-1, 3);
        Span<RigidDamageEvent> destination = stackalloc RigidDamageEvent[4];

        int drained = queue.DrainTo(destination);

        Assert.Equal(2, drained);
        Assert.Equal(new RigidDamageEvent(10, 20), destination[0]);
        Assert.Equal(new RigidDamageEvent(-1, 3), destination[1]);
        Assert.Equal(0, queue.DroppedCount);
    }

    /// <summary>
    /// 验证 PhysicsWorld 分配 bodyKey 并写入 Box2D userData。
    /// </summary>
    [Fact]
    public void PhysicsWorldStoresBodyKeyInBox2DUserData()
    {
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            B2BodyDef bodyDef = Box2D.b2DefaultBodyDef();
            B2BodyId bodyId = Box2D.b2CreateBody(worldId, in bodyDef);
            BodyLocalMask mask = new(1, 1, Vector2.Zero, [1], [2]);
            PhysicsWorld physicsWorld = new();

            PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
            void* userData = Box2D.b2Body_GetUserData(bodyId);

            Assert.Equal(0, body.BodyKey);
            Assert.Same(body, physicsWorld.GetBody(0));
            Assert.Equal(1, (nint)userData);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }
}
