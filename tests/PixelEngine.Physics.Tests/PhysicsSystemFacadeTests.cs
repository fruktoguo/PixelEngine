using System.Numerics;
using PixelEngine.Core.Events;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics.Geometry;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// PhysicsSystem 生命周期、快照与事件 facade 测试。
/// </summary>
public sealed class PhysicsSystemFacadeTests
{
    /// <summary>
    /// 验证 Initialize 创建并接管 Box2D world，同时注入 task bridge；Shutdown 后 facade 拒绝继续使用。
    /// </summary>
    [Fact]
    public void InitializeCreatesOwnedWorldWithTaskBridgeAndShutdownDisposesFacade()
    {
        TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
        CellGrid grid = new(source, MaterialPropsTable.Empty);
        using JobSystem jobs = new(workerCount: 2);
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };

        PhysicsSystem system = PhysicsSystem.Initialize(grid, jobs, worldDef: worldDef);

        Assert.True(system.WorldId.Index1 > 0);
        Assert.Equal(jobs.WorkerCount, system.TaskBridgeWorkerCount);
        Assert.Equal(0, system.TaskBridgeFaultedCallbackCount);
        Assert.Equal(0, system.Stats.ActiveBodyCount);
        Assert.Equal(jobs.WorkerCount, system.Stats.TaskBridgeWorkerCount);

        system.Shutdown();
        system.Shutdown();

        _ = Assert.Throws<ObjectDisposedException>(() => system.CopyBodySnapshots([]));
        _ = Assert.Throws<ObjectDisposedException>(() => system.SyncStep(1f / 60f));
    }

    /// <summary>
    /// 验证 owned PhysicsSystem 暴露 Box2D body live-count，供 native leak detector 在 shutdown 后采集归零证据。
    /// </summary>
    [Fact]
    public void OwnedWorldLiveBodyCountReturnsZeroAfterDestroyAndShutdown()
    {
        Chunk chunk = new(new ChunkCoord(0, 0));
        TestChunkSource source = new(chunk);
        CellGrid grid = new(source, MaterialPropsTable.Empty);
        using JobSystem jobs = new(workerCount: 1);
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
        PhysicsSystem system = PhysicsSystem.Initialize(grid, jobs, worldDef: worldDef);

        FillSolidRegion(grid, x: 8, y: 8, width: 8, height: 8, material: 2);
        int firstBody = system.CreateBodyFromRegion(8, 8, 8, 8);

        Assert.Equal(1, system.LiveBodyCount);

        FillSolidRegion(grid, x: 40, y: 40, width: 8, height: 8, material: 3);
        system.UpdateStaticTerrainColliders(source);
        Assert.Equal(2, system.LiveBodyCount);

        Assert.True(system.DestroyBody(firstBody));
        Assert.Equal(1, system.LiveBodyCount);

        FillSolidRegion(grid, x: 24, y: 8, width: 8, height: 8, material: 2);
        _ = system.CreateBodyFromRegion(24, 8, 8, 8);
        Assert.Equal(2, system.LiveBodyCount);

        system.Shutdown();

        Assert.Equal(0, system.LiveBodyCount);
        Assert.Equal(0, system.PhysicsWorld.ActiveBodyCount);
    }

    /// <summary>
    /// 验证 PhysicsSystem 公开调参会修改 Box2D 重力、默认 sub-step 与碎片阈值。
    /// </summary>
    [Fact]
    public void RuntimeTuningUpdatesGravitySubStepsAndFragmentThreshold()
    {
        TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
        CellGrid grid = new(source, MaterialPropsTable.Empty);
        using JobSystem jobs = new(workerCount: 1);
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
        RigidBodyDestruction destruction = new(fragmentPixelThreshold: 4);
        PhysicsSystem system = PhysicsSystem.Initialize(grid, jobs, destruction: destruction, worldDef: worldDef);

        try
        {
            system.SetSubStepCount(8);
            system.SetGravity(new Vector2(1.25f, 9.5f));
            system.SetFragmentPixelThreshold(9);

            Assert.Equal(8, system.SubStepCount);
            Assert.Equal(new Vector2(1.25f, 9.5f), system.Gravity);
            Assert.Equal(9, system.FragmentPixelThreshold);
            Assert.Equal(9, destruction.FragmentPixelThreshold);
        }
        finally
        {
            system.Shutdown();
        }
    }

    /// <summary>
    /// 验证 CopyBodySnapshots 返回 body-local 不可变 mask、像素变换、线速度和角速度。
    /// </summary>
    [Fact]
    public void CopyBodySnapshotsReturnsImmutableMaskTransformAndVelocities()
    {
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
            B2BodyId bodyId = CreateBoxBody(worldId, width: 16, height: 16, new Vector2(12, 14));
            float angle = 0.375f;
            Box2D.b2Body_SetTransform(
                bodyId,
                new B2Vec2 { X = PhysicsScale.PixelToPhysics(42f), Y = PhysicsScale.PixelToPhysics(24f) },
                new B2Rot { C = MathF.Cos(angle), S = MathF.Sin(angle) });
            Box2D.b2Body_SetLinearVelocity(
                bodyId,
                new B2Vec2 { X = PhysicsScale.PixelToPhysics(96f), Y = PhysicsScale.PixelToPhysics(-32f) });
            Box2D.b2Body_SetAngularVelocity(bodyId, -2.25f);

            BodyLocalMask mask = CreateMaskFromMutableSource();
            PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
            PhysicsSystem system = new(worldId, physicsWorld, grid, registry);
            RigidBodySnapshot[] snapshots = new RigidBodySnapshot[2];

            int written = system.CopyBodySnapshots(snapshots);

            Assert.Equal(1, written);
            RigidBodySnapshot snapshot = snapshots[0];
            Assert.Equal(body.BodyKey, snapshot.BodyKey);
            Assert.Same(mask, snapshot.Mask);
            Assert.Equal(3, snapshot.Mask.Width);
            Assert.Equal(2, snapshot.Mask.Height);
            Assert.Equal(6, snapshot.Mask.SolidPixelCount);
            Assert.Equal((ushort)7, snapshot.Mask.MaterialAt(0, 0));
            Assert.Equal(42f, snapshot.Transform.Position.X, precision: 3);
            Assert.Equal(24f, snapshot.Transform.Position.Y, precision: 3);
            Assert.Equal(MathF.Cos(angle), snapshot.Transform.Cos, precision: 5);
            Assert.Equal(MathF.Sin(angle), snapshot.Transform.Sin, precision: 5);
            Assert.Equal(96f, snapshot.LinearVelocityPixelsPerSecond.X, precision: 3);
            Assert.Equal(-32f, snapshot.LinearVelocityPixelsPerSecond.Y, precision: 3);
            Assert.Equal(-2.25f, snapshot.AngularVelocityRadiansPerSecond, precision: 5);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证 CopyConnectedComponentDebugSnapshots 返回活跃刚体 mask 的 CCL 组件 bounds。
    /// </summary>
    [Fact]
    public void CopyConnectedComponentDebugSnapshotsReturnsBodyMaskComponents()
    {
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
            B2BodyId bodyId = CreateBoxBody(worldId, width: 16, height: 16, new Vector2(10, 10));
            PixelRigidBody body = physicsWorld.AddBody(bodyId, CreateSplitMask());
            PhysicsSystem system = new(worldId, physicsWorld, grid, registry);
            ConnectedComponentDebugSnapshot[] snapshots = new ConnectedComponentDebugSnapshot[4];

            int written = system.CopyConnectedComponentDebugSnapshots(snapshots);

            Assert.Equal(2, written);
            Assert.All(snapshots.AsSpan(0, written).ToArray(), snapshot =>
            {
                Assert.Equal(body.BodyKey, snapshot.BodyKey);
                Assert.False(snapshot.WorldBounds.IsEmpty);
            });
            Assert.Contains(snapshots.AsSpan(0, written).ToArray(), static snapshot => snapshot.Label == 1);
            Assert.Contains(snapshots.AsSpan(0, written).ToArray(), static snapshot => snapshot.Label == 2);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证刚体破碎通过 PhysicsSystem 写入 RigidbodyShatter 音频事件。
    /// </summary>
    [Fact]
    public void SyncStepPublishesRigidbodyShatterAudioEventWhenBodySplits()
    {
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
            RigidDamageQueue damageQueue = new();
            RigidBodyDestruction destruction = new(fragmentPixelThreshold: 4);
            EventBus events = new(capacityPerChannel: 8);
            B2BodyId bodyId = CreateBoxBody(worldId, width: 48, height: 16, new Vector2(8, 8));
            BodyLocalMask mask = CreateFilledMask(48, 16, material: 2);
            PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
            _ = RigidBodyRasterizer.StampInverseSampling(body, body.PreviousTransform, grid, registry);
            QueueVerticalCutDamage(grid, damageQueue, x: 32, minY: 8, maxY: 24);
            PhysicsSystem system = new(worldId, physicsWorld, grid, registry, damageQueue, destruction, eventBus: events);

            system.SyncStep(1f / 60f);

            Assert.Equal(1, system.LastDestructionResult.DamagedBodies);
            Assert.Equal(1, system.LastDestructionResult.DestroyedBodies);
            Assert.Equal(2, system.LastDestructionResult.CreatedBodies);
            AudioEvent[] drained = new AudioEvent[2];
            int eventCount = events.Channel<AudioEvent>().DrainTo(drained);
            Assert.Equal(1, eventCount);
            Assert.Equal(AudioEventType.RigidbodyShatter, drained[0].Type);
            Assert.Equal(32, drained[0].CellX);
            Assert.Equal(15, drained[0].CellY);
            Assert.Equal(2, drained[0].MaterialId);
            Assert.Equal(2f, drained[0].Magnitude);
            Assert.Equal((ushort)1, drained[0].Count);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    private static BodyLocalMask CreateMaskFromMutableSource()
    {
        byte[] solid = [1, 1, 1, 1, 1, 1];
        ushort[] materials = [7, 7, 7, 7, 7, 7];
        BodyLocalMask mask = new(3, 2, new Vector2(1, 1), solid, materials);
        Array.Fill(solid, (byte)0);
        Array.Fill(materials, (ushort)99);
        return mask;
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

    private static BodyLocalMask CreateSplitMask()
    {
        byte[] solid =
        [
            1, 1, 0, 1, 1,
            1, 1, 0, 1, 1,
            1, 1, 0, 1, 1,
            1, 1, 0, 1, 1,
        ];
        ushort[] materials = new ushort[solid.Length];
        Array.Fill(materials, (ushort)2);
        return new BodyLocalMask(5, 4, Vector2.Zero, solid, materials);
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

    private static void FillSolidRegion(CellGrid grid, int x, int y, int width, int height, ushort material)
    {
        for (int py = y; py < y + height; py++)
        {
            for (int px = x; px < x + width; px++)
            {
                grid.MaterialAt(px, py) = material;
                grid.FlagsAt(px, py) = 0;
                grid.MarkDirty(px, py);
            }
        }
    }

    private static void QueueVerticalCutDamage(CellGrid grid, RigidDamageQueue damageQueue, int x, int minY, int maxY)
    {
        for (int y = minY; y < maxY; y++)
        {
            ushort material = grid.MaterialAt(x, y);
            grid.FlagsAt(x, y) = 0;
            grid.MaterialAt(x, y) = 0;
            damageQueue.OnOwnedCellDamaged(x, y, material);
        }
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
