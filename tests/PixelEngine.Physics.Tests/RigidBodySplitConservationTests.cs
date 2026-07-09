using System.Numerics;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics.Geometry;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// plan/14 刚体破坏拆分守恒验收测试。
/// 不变式：刚体拆分前后像素计数守恒。
/// </summary>
public sealed class RigidBodySplitConservationTests
{
    /// <summary>
    /// 验证挖断成两个连通块后，新刚体像素总数等于父体剩余像素数，且父体速度转移给子体。
    /// </summary>
    [Fact]
    public void SplitBodyConservesRemainingPixelsAndTransfersVelocity()
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
            BodyLocalMask mask = CreateFilledMask(48, 16, material: 2);
            B2BodyId bodyId = CreateBoxBody(worldId, width: 48, height: 16, new Vector2(8, 8));
            Box2D.b2Body_SetLinearVelocity(bodyId, new B2Vec2 { X = PhysicsScale.PixelToPhysics(16f), Y = 0f });
            Box2D.b2Body_SetAngularVelocity(bodyId, 2f);
            PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
            _ = RigidBodyRasterizer.StampInverseSampling(body, body.PreviousTransform, grid, registry);
            RigidDamageEvent[] damage = CreateVerticalCutDamage(grid, x: 32, minY: 8, maxY: 24);
            RigidBodyDestruction destruction = new(fragmentPixelThreshold: 4);
            using JobSystem jobs = new(workerCount: 2);

            RigidDestructionResult result = destruction.RebuildDirty(worldId, physicsWorld, grid, registry, damage, jobs);

            // Assert：验证预期结果
            Assert.Equal(2, result.CreatedBodies);
            Assert.Equal(mask.SolidPixelCount - damage.Length, CountBodyMaskPixels(physicsWorld));
            Assert.Equal(2, physicsWorld.ActiveBodyCount);
            AssertChildVelocityTransferred(physicsWorld);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证碎片阈值以下连通块转自由粒子，不创建新刚体，并保持剩余像素账本。
    /// </summary>
    [Fact]
    public void SmallComponentsBecomeParticlesInsteadOfBodies()
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
            BodyLocalMask mask = CreateFilledMask(2, 2, material: 2);
            B2BodyId bodyId = CreateBoxBody(worldId, width: 16, height: 16, new Vector2(8, 8));
            PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
            _ = RigidBodyRasterizer.StampInverseSampling(body, body.PreviousTransform, grid, registry);
            RigidStampedCell damaged = body.PreviousStamps[0];
            grid.FlagsAt(damaged.WorldX, damaged.WorldY) = 0;
            grid.MaterialAt(damaged.WorldX, damaged.WorldY) = 0;
            ParticleSystem particles = new(capacity: 8);
            RigidBodyDestruction destruction = new(fragmentPixelThreshold: 4, particles);

            RigidDestructionResult result = destruction.RebuildDirty(worldId, physicsWorld, grid, registry, [new RigidDamageEvent(damaged.WorldX, damaged.WorldY)]);

            // Assert：验证预期结果
            Assert.Equal(0, result.CreatedBodies);
            Assert.Equal(3, result.FragmentPixels);
            Assert.Equal(0, physicsWorld.ActiveBodyCount);
            Assert.Equal(3, particles.ActiveCount);
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

    private static int CountBodyMaskPixels(PhysicsWorld physicsWorld)
    {
        int total = 0;
        for (int i = 0; i < physicsWorld.BodySlotCount; i++)
        {
            if (physicsWorld.TryGetBody(i, out PixelRigidBody? body))
            {
                total += body.Mask.SolidPixelCount;
            }
        }

        return total;
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
            Assert.Equal(2f, Box2D.b2Body_GetAngularVelocity(body.BodyId));
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
