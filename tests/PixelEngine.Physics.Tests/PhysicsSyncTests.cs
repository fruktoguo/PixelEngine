using System.Numerics;
using PixelEngine.Core.Mathematics;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics.Geometry;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// 相位 8 CA↔刚体同步测试。
/// 不变式：stamp/erase 不误删普通 cell、SyncStep 排空伤害队列并重 stamp、角色 proxy 先于写回约束刚体。
/// </summary>
public sealed class PhysicsSyncTests
{
    /// <summary>
    /// 验证 erase 只清仍由刚体占用的旧 stamp，不误删 CA 已消耗后的普通 cell。
    /// </summary>
    [Fact]
    public void EraseDoesNotClearCellAfterRigidOwnedFlagWasConsumed()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
        CellGrid grid = new(source, MaterialPropsTable.Empty);
        BodyLocalMask mask = CreateFilledMask(1, 1, material: 2, Vector2.Zero);
        PixelRigidBody body = new(0, default, mask);
        RigidStampRegistry registry = new();
        Transform2D transform = new(new Vector2(20, 20), 0f);

        int stamped = RigidBodyRasterizer.StampInverseSampling(body, in transform, grid, registry);
        RigidStampedCell cell = body.PreviousStamps[0];
        grid.MaterialAt(cell.WorldX, cell.WorldY) = 9;
        grid.FlagsAt(cell.WorldX, cell.WorldY) = 0;

        int erased = RigidBodyRasterizer.EraseAtCurrentTransform(body, grid, registry);

        // Assert：验证预期结果
        Assert.Equal(1, stamped);
        Assert.Equal(0, erased);
        Assert.Equal((ushort)9, grid.GetMaterial(cell.WorldX, cell.WorldY));
        Assert.Empty(body.PreviousStamps);
    }

    /// <summary>
    /// 验证旋转刚体使用 inverse sampling 重 stamp 时不会在填充 mask 内部留下封闭空洞。
    /// </summary>
    [Fact]
    public void StampInverseSamplingRotatedFilledMaskHasNoInternalHoles()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
        CellGrid grid = new(source, MaterialPropsTable.Empty);
        BodyLocalMask mask = CreateFilledMask(7, 7, material: 2, new Vector2(3.5f, 3.5f));
        PixelRigidBody body = new(0, default, mask);
        RigidStampRegistry registry = new();
        Transform2D transform = new(new Vector2(24, 24), radians: 0.47f);

        int stamped = RigidBodyRasterizer.StampInverseSampling(body, in transform, grid, registry);

        // Assert：验证预期结果
        Assert.Equal(stamped, body.PreviousStamps.Count);
        Assert.True(stamped >= mask.SolidPixelCount);
        AssertNoInternalHoles(body.PreviousStamps);
        foreach (RigidStampedCell cell in body.PreviousStamps)
        {
            Assert.True(registry.TryGet(cell.WorldX, cell.WorldY, out RigidStamp stamp));
            Assert.Equal(cell.Stamp, stamp);
            Assert.Equal((ushort)2, grid.GetMaterial(cell.WorldX, cell.WorldY));
            Assert.True(CellFlags.Has(grid.FlagsAt(cell.WorldX, cell.WorldY), CellFlags.RigidOwned));
        }
    }

    /// <summary>
    /// 验证 PhysicsSystem.SyncStep 会排空 damage queue、step Box2D，并用读回 transform 重建 stamp registry。
    /// </summary>
    [Fact]
    public void SyncStepStepsWorldAndRestampsRigidBody()
    {
        // Arrange：搭建测试场景与依赖
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
            B2BodyId bodyId = CreateDynamicBoxBody(worldId, new Vector2(20, 20));
            Box2D.b2Body_SetLinearVelocity(bodyId, new B2Vec2 { X = PhysicsScale.PixelToPhysics(64f), Y = 0f });
            BodyLocalMask mask = CreateFilledMask(1, 1, material: 2, Vector2.Zero);
            PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
            PhysicsSystem system = new(worldId, physicsWorld, grid, registry, damageQueue);
            damageQueue.OnOwnedCellDamaged(3, 4);

            // Act：执行被测操作
            system.SyncStep(0.25f);

            // Assert：验证不变式与预期结果
            RigidDamageEvent damage = Assert.Single(system.PendingDamage);
            Assert.Equal(new RigidDamageEvent(3, 4), damage);
            Assert.True(body.PreviousTransform.Position.X > 30f);
            Assert.True(system.LastStampedCellCount > 0);
            Assert.Equal(system.LastStampedCellCount, body.PreviousStamps.Count);
            RigidStampedCell stamped = body.PreviousStamps[0];
            Assert.True(registry.TryGet(stamped.WorldX, stamped.WorldY, out RigidStamp stamp));
            Assert.Equal(body.BodyKey, stamp.BodyKey);
            Assert.Equal((ushort)2, grid.GetMaterial(stamped.WorldX, stamped.WorldY));
            Assert.True(CellFlags.Has(grid.FlagsAt(stamped.WorldX, stamped.WorldY), CellFlags.RigidOwned));
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证刚体同步稳态调用不为 erase/inverse-sample 计时路径创建实例委托。
    /// </summary>
    [Fact]
    public void SyncStepSteadyStateDoesNotAllocateAfterWarmup()
    {
        // Arrange：准备一个固定位置的单像素刚体，使测量只覆盖稳态同步路径。
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
        worldDef.EnableSleep = 0;
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PhysicsWorld physicsWorld = new();
            RigidStampRegistry registry = new();
            B2BodyId bodyId = CreateDynamicBoxBody(worldId, new Vector2(20, 20));
            BodyLocalMask mask = CreateFilledMask(1, 1, material: 2, Vector2.Zero);
            _ = physicsWorld.AddBody(bodyId, mask);
            PhysicsSystem system = new(worldId, physicsWorld, grid, registry);

            system.SyncStep(1f / 60f);
            system.SyncStep(1f / 60f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            system.SyncStep(1f / 60f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证动态刚体在 inverse-sampling 写回前会被角色 AABB proxy 约束，而不是穿过玩家后再由脚本层解卡。
    /// </summary>
    [Fact]
    public void CharacterProxyBlocksFallingRigidBodyBeforeRestamp()
    {
        // Arrange：搭建测试场景与依赖
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 10f };
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PhysicsWorld physicsWorld = new();
            RigidStampRegistry registry = new();
            PhysicsSystem system = new(worldId, physicsWorld, grid, registry);
            FillRect(grid, material: 2, minX: 36, minY: 22, maxX: 56, maxY: 30);
            int bodyKey = system.CreateBodyFromRegion(36, 22, 20, 8);
            PixelRigidBody body = physicsWorld.GetBody(bodyKey);
            Box2D.b2Body_SetLinearVelocity(body.BodyId, new B2Vec2 { X = 0f, Y = PhysicsScale.PixelToPhysics(280f) });

            CharacterController character = new(grid, new Vector2(42f, 48f), new Vector2(6f, 12f));
            system.RegisterCharacterProxy(character);

            bool everOverlappedCharacter = false;
            int firstOverlapFrame = -1;
            int overlapX = 0;
            int overlapY = 0;
            int totalProxyContacts = 0;
            int firstProxyContactFrame = -1;
            for (int i = 0; i < 90; i++)
            {
                // Act：执行被测操作
                system.SyncStep(1f / 60f);
                if (system.LastCharacterProxyContactCount > 0)
                {
                    totalProxyContacts += system.LastCharacterProxyContactCount;
                    if (firstProxyContactFrame < 0)
                    {
                        firstProxyContactFrame = i;
                    }
                }

                if (TryFindRigidOwnedInAabb(grid, character.Bounds, out overlapX, out overlapY))
                {
                    everOverlappedCharacter = true;
                    firstOverlapFrame = i;
                    break;
                }
            }

            // Assert：验证不变式与预期结果
            Assert.False(
                everOverlappedCharacter,
                $"动态刚体 stamp 不应进入角色 AABB，firstFrame={firstOverlapFrame}, cell=({overlapX},{overlapY}), bodyY={body.PreviousTransform.Position.Y:F2}。");
            Assert.True(
                body.PreviousTransform.Position.Y < 48f,
                $"刚体应被角色 proxy 阻挡在玩家上方，actualY={body.PreviousTransform.Position.Y:F2}, contacts={totalProxyContacts}, firstContact={firstProxyContactFrame}。");
            Assert.Equal(1, totalProxyContacts);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证高速下落碎块即使单步跨过角色 AABB，也会通过扫掠 AABB proxy 被截停。
    /// </summary>
    [Fact]
    public void CharacterProxySweepsFastFallingRigidBodyBeforeRestamp()
    {
        // Arrange：搭建测试场景与依赖
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
            PhysicsSystem system = new(worldId, physicsWorld, grid, registry);
            FillRect(grid, material: 2, minX: 36, minY: 34, maxX: 56, maxY: 42);
            int bodyKey = system.CreateBodyFromRegion(36, 34, 20, 8);
            PixelRigidBody body = physicsWorld.GetBody(bodyKey);
            Box2D.b2Body_SetLinearVelocity(body.BodyId, new B2Vec2 { X = 0f, Y = PhysicsScale.PixelToPhysics(1_200f) });

            CharacterController character = new(grid, new Vector2(42f, 48f), new Vector2(6f, 12f));
            system.RegisterCharacterProxy(character);

            // Act：执行被测操作
            system.SyncStep(1f / 60f);

            // Assert：验证不变式与预期结果
            Assert.True(
                system.LastCharacterProxyContactCount > 0,
                $"高速碎块扫过玩家时应被 proxy 捕获，bodyY={body.PreviousTransform.Position.Y:F2}。");
            Assert.False(
                TryFindRigidOwnedInAabb(grid, character.Bounds, out int overlapX, out int overlapY),
                $"高速碎块 stamp 不应进入角色 AABB，cell=({overlapX},{overlapY}), bodyY={body.PreviousTransform.Position.Y:F2}。");
            Assert.True(
                body.PreviousTransform.Position.Y < 48f,
                $"高速碎块应被截停在玩家上方，actualY={body.PreviousTransform.Position.Y:F2}。");
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证旋转碎块即使中心没有向下线速度，也会在 inverse-sampling 写回前被角色 proxy 截停。
    /// </summary>
    [Fact]
    public void CharacterProxyBlocksRotatingRigidBodyBeforeRestamp()
    {
        // Arrange：搭建测试场景与依赖
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
            PhysicsSystem system = new(worldId, physicsWorld, grid, registry);
            FillRect(grid, material: 2, minX: 34, minY: 30, maxX: 58, maxY: 38);
            int bodyKey = system.CreateBodyFromRegion(34, 30, 24, 8);
            PixelRigidBody body = physicsWorld.GetBody(bodyKey);
            Box2D.b2Body_SetLinearVelocity(body.BodyId, new B2Vec2 { X = 0f, Y = 0f });
            Box2D.b2Body_SetAngularVelocity(body.BodyId, 18f);

            CharacterController character = new(grid, new Vector2(42f, 48f), new Vector2(6f, 12f));
            system.RegisterCharacterProxy(character);

            // Act：执行被测操作
            system.SyncStep(1f / 30f);

            // Assert：验证不变式与预期结果
            Assert.True(
                system.LastCharacterProxyContactCount > 0,
                $"旋转碎块下沿扫到玩家时应被 proxy 记录接触，bodyY={body.PreviousTransform.Position.Y:F2}。");
            Assert.False(
                TryFindRigidOwnedInAabb(grid, character.Bounds, out int overlapX, out int overlapY),
                $"旋转碎块 stamp 不应进入角色 AABB，cell=({overlapX},{overlapY}), bodyY={body.PreviousTransform.Position.Y:F2}。");
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证角色 proxy 清理刚体重叠时跳过未驻留 chunk，避免玩家跨 chunk 边界时因冷路径查询崩溃。
    /// </summary>
    [Fact]
    public void CharacterProxyOverlapCleanupSkipsNonResidentChunks()
    {
        // Arrange：搭建测试场景与依赖
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            TestChunkSource source = new(new Chunk(new ChunkCoord(0, 2)));
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PhysicsSystem system = new(worldId, new PhysicsWorld(), grid, new RigidStampRegistry());
            CharacterController character = new(grid, new Vector2(12f, 190f), new Vector2(6f, 12f));
            system.RegisterCharacterProxy(character);

            // Act：执行被测操作
            system.SyncStep(1f / 60f);

            // Assert：验证不变式与预期结果
            Assert.Equal(0, system.LastCharacterProxyContactCount);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    private static BodyLocalMask CreateFilledMask(int width, int height, ushort material, Vector2 origin)
    {
        int area = width * height;
        byte[] solid = new byte[area];
        ushort[] materials = new ushort[area];
        Array.Fill(solid, (byte)1);
        Array.Fill(materials, material);
        return new BodyLocalMask(width, height, origin, solid, materials);
    }

    private static B2BodyId CreateDynamicBoxBody(B2WorldId worldId, Vector2 bodyPositionPixels)
    {
        Span<ConvexPolygon> pieces = stackalloc ConvexPolygon[1];
        ReadOnlySpan<Vector2> vertices =
        [
            new(0, 0),
            new(16, 0),
            new(16, 16),
            new(0, 16),
        ];
        pieces[0] = ConvexPolygon.From(vertices);
        return ShapeBuilder.BuildBody(worldId, pieces, bodyPositionPixels, density: 1f);
    }

    private static void FillRect(CellGrid grid, ushort material, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                grid.MaterialAt(x, y) = material;
                grid.FlagsAt(x, y) = default;
            }
        }
    }

    private static bool TryFindRigidOwnedInAabb(CellGrid grid, in AABB bounds, out int hitX, out int hitY)
    {
        RectI rect = bounds.ToRectI();
        for (int y = rect.MinY; y < rect.MaxY; y++)
        {
            for (int x = rect.MinX; x < rect.MaxX; x++)
            {
                if (CellFlags.Has(grid.FlagsAt(x, y), CellFlags.RigidOwned))
                {
                    hitX = x;
                    hitY = y;
                    return true;
                }
            }
        }

        hitX = 0;
        hitY = 0;
        return false;
    }

    private static void AssertNoInternalHoles(List<RigidStampedCell> stamps)
    {
        HashSet<long> occupied = new(stamps.Count);
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        foreach (RigidStampedCell stamp in stamps)
        {
            Assert.True(occupied.Add(Pack(stamp.WorldX, stamp.WorldY)));
            minX = Math.Min(minX, stamp.WorldX);
            minY = Math.Min(minY, stamp.WorldY);
            maxX = Math.Max(maxX, stamp.WorldX);
            maxY = Math.Max(maxY, stamp.WorldY);
        }

        minX--;
        minY--;
        maxX++;
        maxY++;
        Queue<(int X, int Y)> queue = new();
        HashSet<long> exterior = [];
        queue.Enqueue((minX, minY));
        Assert.True(exterior.Add(Pack(minX, minY)));

        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            TryVisit(x - 1, y);
            TryVisit(x + 1, y);
            TryVisit(x, y - 1);
            TryVisit(x, y + 1);

            void TryVisit(int nx, int ny)
            {
                if (nx < minX || nx > maxX || ny < minY || ny > maxY)
                {
                    return;
                }

                long key = Pack(nx, ny);
                if (occupied.Contains(key) || !exterior.Add(key))
                {
                    return;
                }

                queue.Enqueue((nx, ny));
            }
        }

        for (int y = minY + 1; y < maxY; y++)
        {
            for (int x = minX + 1; x < maxX; x++)
            {
                long key = Pack(x, y);
                Assert.True(occupied.Contains(key) || exterior.Contains(key), $"检测到内部空洞：({x}, {y})。");
            }
        }
    }

    private static long Pack(int x, int y)
    {
        return ((long)x << 32) ^ (uint)y;
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
            if (!TryGetChunk(new ChunkCoord(center.X - 1, center.Y - 1), out Chunk slot0) ||
                !TryGetChunk(new ChunkCoord(center.X, center.Y - 1), out Chunk slot1) ||
                !TryGetChunk(new ChunkCoord(center.X + 1, center.Y - 1), out Chunk slot2) ||
                !TryGetChunk(new ChunkCoord(center.X - 1, center.Y), out Chunk slot3) ||
                !TryGetChunk(center, out Chunk slot4) ||
                !TryGetChunk(new ChunkCoord(center.X + 1, center.Y), out Chunk slot5) ||
                !TryGetChunk(new ChunkCoord(center.X - 1, center.Y + 1), out Chunk slot6) ||
                !TryGetChunk(new ChunkCoord(center.X, center.Y + 1), out Chunk slot7) ||
                !TryGetChunk(new ChunkCoord(center.X + 1, center.Y + 1), out Chunk slot8))
            {
                neighborhood = default;
                return false;
            }

            neighborhood = new ChunkNeighborhood(slot0, slot1, slot2, slot3, slot4, slot5, slot6, slot7, slot8);
            return true;
        }
    }
}
