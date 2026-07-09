using System.Numerics;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics.Geometry;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// 刚体破坏/挖掘重建测试。
/// 不变式：破坏/挖掘后刚体重建守恒像素质量。
/// </summary>
public sealed class RigidBodyDestructionTests
{
    /// <summary>
    /// 验证 damage 切断 mask 后会销毁父体、创建多个子体，并转移父体速度。
    /// </summary>
    [Fact]
    public void RebuildDirtySplitsDamagedBodyIntoChildBodies()
    {
        // Arrange：准备输入与初始状态
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PhysicsWorld physicsWorld = new();
            RigidStampRegistry registry = new();
            B2BodyId bodyId = CreateBoxBody(worldId, width: 48, height: 16, new Vector2(8, 8));
            Box2D.b2Body_SetLinearVelocity(bodyId, new B2Vec2 { X = PhysicsScale.PixelToPhysics(16f), Y = 0f });
            Box2D.b2Body_SetAngularVelocity(bodyId, 2f);
            BodyLocalMask mask = CreateFilledMask(48, 16, material: 2);
            PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
            _ = RigidBodyRasterizer.StampInverseSampling(body, PhysicsScale.ToTransform2D(Box2D.b2Body_GetTransform(bodyId)), grid, registry);
            RigidDamageEvent[] damage = CreateVerticalCutDamage(grid, x: 32, minY: 8, maxY: 24);
            RigidBodyDestruction destruction = new(fragmentPixelThreshold: 4);
            using JobSystem jobs = new(workerCount: 2);

            RigidDestructionResult result = destruction.RebuildDirty(worldId, physicsWorld, grid, registry, damage, jobs);

            // Assert：验证预期结果
            Assert.Equal(1, result.DamagedBodies);
            Assert.Equal(1, result.DestroyedBodies);
            Assert.Equal(2, result.CreatedBodies);
            Assert.Equal(0, result.FragmentPixels);
            Assert.True(destruction.LastPlanningUsedJobSystem);
            Assert.True(destruction.LastPlanningWorkerCount > 0);
            Assert.Equal(2, physicsWorld.ActiveBodyCount);
            AssertChildVelocityTransferred(physicsWorld);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证小于碎片阈值的剩余连通块会转为自由粒子而不是新建刚体。
    /// </summary>
    [Fact]
    public void RebuildDirtyTurnsSmallRemainingComponentsIntoParticles()
    {
        // Arrange：准备输入与初始状态
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PhysicsWorld physicsWorld = new();
            RigidStampRegistry registry = new();
            B2BodyId bodyId = CreateBoxBody(worldId, width: 16, height: 16, new Vector2(8, 8));
            BodyLocalMask mask = CreateFilledMask(2, 2, material: 2);
            PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
            _ = RigidBodyRasterizer.StampInverseSampling(body, PhysicsScale.ToTransform2D(Box2D.b2Body_GetTransform(bodyId)), grid, registry);
            RigidStampedCell damaged = body.PreviousStamps[0];
            grid.FlagsAt(damaged.WorldX, damaged.WorldY) = 0;
            grid.MaterialAt(damaged.WorldX, damaged.WorldY) = 0;
            ParticleSystem particles = new(capacity: 8);
            RigidBodyDestruction destruction = new(fragmentPixelThreshold: 4, particles);

            RigidDestructionResult result = destruction.RebuildDirty(worldId, physicsWorld, grid, registry, [new RigidDamageEvent(damaged.WorldX, damaged.WorldY)]);

            // Assert：验证预期结果
            Assert.Equal(1, result.DestroyedBodies);
            Assert.Equal(0, result.CreatedBodies);
            Assert.Equal(3, result.FragmentPixels);
            Assert.Equal(0, physicsWorld.ActiveBodyCount);
            Assert.Equal(3, particles.ActiveCount);
            Assert.All(particles.ActiveReadOnly.ToArray(), static particle => Assert.Equal((ushort)2, particle.Material));
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证破坏后剩余的高像素数退化细线不会让 Box2D 重建崩溃，而是转为碎片。
    /// </summary>
    [Fact]
    public void RebuildDirtyTurnsDegenerateRemainingComponentIntoFragments()
    {
        // Arrange：准备输入与初始状态
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PhysicsWorld physicsWorld = new();
            RigidStampRegistry registry = new();
            B2BodyId bodyId = CreateBoxBody(worldId, width: 8, height: 8, new Vector2(8, 8));
            BodyLocalMask mask = CreateFilledMask(8, 8, material: 2);
            PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
            _ = RigidBodyRasterizer.StampInverseSampling(body, PhysicsScale.ToTransform2D(Box2D.b2Body_GetTransform(bodyId)), grid, registry);
            RigidDamageEvent[] damage = CreateAllButOneColumnDamage(grid, body, keepX: 3);
            ParticleSystem particles = new(capacity: 16);
            RigidBodyDestruction destruction = new(fragmentPixelThreshold: 4, particles);

            RigidDestructionResult result = destruction.RebuildDirty(worldId, physicsWorld, grid, registry, damage);

            // Assert：验证预期结果
            Assert.Equal(1, result.DestroyedBodies);
            Assert.Equal(0, result.CreatedBodies);
            Assert.Equal(8, result.FragmentPixels);
            Assert.Equal(0, physicsWorld.ActiveBodyCount);
            Assert.Equal(8, particles.ActiveCount);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证 sleeping 刚体不会在本帧重建。
    /// </summary>
    [Fact]
    public void RebuildDirtySkipsSleepingBody()
    {
        // Arrange：准备输入与初始状态
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PhysicsWorld physicsWorld = new();
            RigidStampRegistry registry = new();
            B2BodyId bodyId = CreateBoxBody(worldId, width: 16, height: 16, new Vector2(8, 8));
            BodyLocalMask mask = CreateFilledMask(16, 16, material: 2);
            PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
            _ = RigidBodyRasterizer.StampInverseSampling(body, PhysicsScale.ToTransform2D(Box2D.b2Body_GetTransform(bodyId)), grid, registry);
            Box2D.b2Body_SetAwake(bodyId, 0);
            RigidDamageEvent[] damage = [new(12, 12)];
            RigidBodyDestruction destruction = new(fragmentPixelThreshold: 4);

            RigidDestructionResult result = destruction.RebuildDirty(worldId, physicsWorld, grid, registry, damage);

            // Assert：验证预期结果
            Assert.Equal(1, result.DamagedBodies);
            Assert.Equal(1, result.SkippedSleepingBodies);
            Assert.Equal(0, result.DestroyedBodies);
            Assert.Equal(1, physicsWorld.ActiveBodyCount);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    private static BodyLocalMask CreateFilledMask(int width, int height, ushort material)
    {
        int area = width * height;
        byte[] solid = new byte[area];
        ushort[] materials = new ushort[area];
        Array.Fill(solid, (byte)1);
        Array.Fill(materials, material);
        return new BodyLocalMask(width, height, Vector2.Zero, solid, materials);
    }

    private static B2BodyId CreateBoxBody(B2WorldId worldId, int width, int height, Vector2 bodyPositionPixels)
    {
        Span<ConvexPolygon> pieces = stackalloc ConvexPolygon[1];
        ReadOnlySpan<Vector2> vertices =
        [
            new(0, 0),
            new(width, 0),
            new(width, height),
            new(0, height),
        ];
        pieces[0] = ConvexPolygon.From(vertices);
        return ShapeBuilder.BuildBody(worldId, pieces, bodyPositionPixels, density: 1f);
    }

    private static RigidDamageEvent[] CreateVerticalCutDamage(CellGrid grid, int x, int minY, int maxY)
    {
        RigidDamageEvent[] damage = new RigidDamageEvent[maxY - minY];
        for (int y = minY; y < maxY; y++)
        {
            grid.FlagsAt(x, y) = 0;
            grid.MaterialAt(x, y) = 0;
            damage[y - minY] = new RigidDamageEvent(x, y);
        }

        return damage;
    }

    private static RigidDamageEvent[] CreateAllButOneColumnDamage(CellGrid grid, PixelRigidBody body, int keepX)
    {
        List<RigidDamageEvent> damage = [];
        foreach (RigidStampedCell stamp in body.PreviousStamps)
        {
            if (stamp.Stamp.LocalX == keepX)
            {
                continue;
            }

            grid.FlagsAt(stamp.WorldX, stamp.WorldY) = 0;
            grid.MaterialAt(stamp.WorldX, stamp.WorldY) = 0;
            damage.Add(new RigidDamageEvent(stamp.WorldX, stamp.WorldY));
        }

        return [.. damage];
    }

    private static void AssertChildVelocityTransferred(PhysicsWorld physicsWorld)
    {
        int checkedBodies = 0;
        for (int i = 0; i < physicsWorld.BodySlotCount; i++)
        {
            if (!physicsWorld.TryGetBody(i, out PixelRigidBody? body))
            {
                continue;
            }

            B2Vec2 velocity = Box2D.b2Body_GetLinearVelocity(body.BodyId);
            float angularVelocity = Box2D.b2Body_GetAngularVelocity(body.BodyId);
            Assert.Equal(2f, angularVelocity);
            Assert.NotEqual(0f, velocity.Y);
            checkedBodies++;
        }

        Assert.Equal(2, checkedBodies);
    }

    private sealed class TestChunkSource(params Chunk[] chunks) : IChunkSource
    {
        private readonly Dictionary<ChunkCoord, Chunk> _map = chunks.ToDictionary(static chunk => chunk.Coord);

        public ReadOnlySpan<Chunk> ResidentChunks => chunks;

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            return _map.TryGetValue(coord, out chunk!);
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            neighborhood = default;
            return false;
        }
    }
}
