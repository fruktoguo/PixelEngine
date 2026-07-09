using System.Numerics;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics.Geometry;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// CA 与像素刚体双向耦合验收测试。
/// 不变式：CA↔刚体双向耦合不穿透、伤害与 stamp 一致。
/// </summary>
public sealed class PhysicsCouplingAcceptanceTests
{
    private const ushort Empty = 0;
    private const ushort Sand = 1;
    private const ushort RigidStone = 2;
    private const ushort Fire = 3;
    private const ushort Acid = 4;
    private const ushort Ash = 5;

    /// <summary>
    /// 验证粉末会堆在 RigidOwned 固体上方，不穿透动态刚体占用像素。
    /// </summary>
    [Fact]
    public void SandPilesOnRigidOwnedSolidCell()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = TestChunkSource.CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 10, Sand);
        Set(center, 9, 11, RigidStone);
        Set(center, 10, 11, RigidStone);
        Set(center, 11, 11, RigidStone);
        SetFlags(center, 9, 11, CellFlags.RigidOwned);
        SetFlags(center, 10, 11, CellFlags.RigidOwned);
        SetFlags(center, 11, 11, CellFlags.RigidOwned);
        RigidDamageQueue damageQueue = new();
        SimulationKernel kernel = new(source, CreateMaterialProps(), rigidDamageSink: damageQueue);

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(Sand, Get(center, 10, 10));
        Assert.Equal(RigidStone, Get(center, 10, 11));
        Assert.True(CellFlags.Has(GetFlags(center, 10, 11), CellFlags.RigidOwned));
        Span<RigidDamageEvent> drained = stackalloc RigidDamageEvent[1];
        Assert.Equal(0, damageQueue.DrainTo(drained));
    }

    /// <summary>
    /// 验证火焰和酸反应会把 RigidOwned 目标入队给 physics 破坏重建，并清除 owned 位。
    /// </summary>
    [Theory]
    [InlineData(Fire, Ash)]
    [InlineData(Acid, Empty)]
    public void ReactionsDamageRigidOwnedCells(ushort reactiveMaterial, ushort expectedRigidOutput)
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = TestChunkSource.CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SetCurrentDirty(center, DirtyRect.Full);
        Set(center, 10, 10, reactiveMaterial);
        int targetX = reactiveMaterial == Acid ? 10 : 11;
        int targetY = reactiveMaterial == Acid ? 9 : 10;
        Set(center, targetX, targetY, RigidStone);
        SetFlags(center, targetX, targetY, CellFlags.RigidOwned);
        if (reactiveMaterial == Acid)
        {
            Set(center, 9, 10, RigidStone);
            Set(center, 11, 10, RigidStone);
            Set(center, 10, 11, RigidStone);
        }
        RigidDamageQueue damageQueue = new();
        MaterialTable materials = CreateMaterialTable();
        ReactionEngine reactions = new(materials, CreateReactionTable(materials));
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot), rigidDamageSink: damageQueue, reactionExecutor: reactions);

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(expectedRigidOutput, Get(center, targetX, targetY));
        Assert.False(CellFlags.Has(GetFlags(center, targetX, targetY), CellFlags.RigidOwned));
        Span<RigidDamageEvent> drained = stackalloc RigidDamageEvent[2];
        Assert.Equal(1, damageQueue.DrainTo(drained));
        Assert.Equal(new RigidDamageEvent(targetX, targetY, RigidStone), drained[0]);
    }

    /// <summary>
    /// 验证 CA 挖断刚体后，PhysicsSystem 会将连通块拆成多个可继续 step、带角速度的子刚体。
    /// </summary>
    [Fact]
    public void DamagedRigidBodySplitsIntoSteppingRotatingChildBodies()
    {
        // Arrange：搭建测试场景与依赖
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 10f };
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            TestChunkSource source = TestChunkSource.CreateSquare(radius: 1);
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PhysicsWorld physicsWorld = new();
            RigidStampRegistry registry = new();
            RigidDamageQueue damageQueue = new();
            RigidBodyDestruction destruction = new(fragmentPixelThreshold: 4);
            B2BodyId bodyId = CreateBoxBody(worldId, width: 48, height: 16, new Vector2(8, 8));
            Box2D.b2Body_SetLinearVelocity(bodyId, new B2Vec2 { X = PhysicsScale.PixelToPhysics(24f), Y = 0f });
            Box2D.b2Body_SetAngularVelocity(bodyId, 1.5f);
            BodyLocalMask mask = CreateFilledMask(48, 16, material: RigidStone, origin: Vector2.Zero);
            PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
            _ = RigidBodyRasterizer.StampInverseSampling(body, body.PreviousTransform, grid, registry);
            QueueVerticalCut(grid, damageQueue, x: 32, minY: 8, maxY: 24);
            PhysicsSystem system = new(worldId, physicsWorld, grid, registry, damageQueue, destruction);

            // Act：执行被测操作
            system.SyncStep(1f / 30f);

            // Assert：验证不变式与预期结果
            Assert.Equal(1, system.LastDestructionResult.DestroyedBodies);
            Assert.Equal(2, system.LastDestructionResult.CreatedBodies);
            Assert.Equal(2, physicsWorld.ActiveBodyCount);
            int checkedBodies = 0;
            for (int i = 0; i < physicsWorld.BodySlotCount; i++)
            {
                if (!physicsWorld.TryGetBody(i, out PixelRigidBody? child))
                {
                    continue;
                }

                B2Vec2 velocity = Box2D.b2Body_GetLinearVelocity(child.BodyId);
                Assert.NotEqual(0f, velocity.Y);
                Assert.Equal(1.5f, Box2D.b2Body_GetAngularVelocity(child.BodyId), precision: 5);
                Assert.True(child.PreviousStamps.Count > 0);
                checkedBodies++;
            }

            Assert.Equal(2, checkedBodies);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    private static MaterialPropsTable CreateMaterialProps()
    {
        return new MaterialPropsTable(CreateMaterialTable().Hot);
    }

    private static MaterialTable CreateMaterialTable()
    {
        return new MaterialTable(
        [
            Material(Empty, "empty", CellType.Empty, density: 0),
            Material(Sand, "sand", CellType.Powder, density: 180),
            Material(RigidStone, "rigid_stone", CellType.Solid, density: 255),
            Material(Fire, "fire", CellType.Fire, density: 1, reactionStart: 0, reactionCount: 1),
            Material(Acid, "acid", CellType.Solid, density: 120, reactionStart: 1, reactionCount: 1, propertyFlags: MaterialProperty.Acid),
            Material(Ash, "ash", CellType.Powder, density: 80),
        ]);
    }

    private static ReactionTable CreateReactionTable(MaterialTable materials)
    {
        Reaction[] reactions =
        [
            new()
            {
                InputA = Fire,
                InputB = RigidStone,
                OutputA = Fire,
                OutputB = Ash,
                Probability = 255,
            },
            new()
            {
                InputA = Acid,
                InputB = RigidStone,
                OutputA = Acid,
                OutputB = Empty,
                Probability = 255,
            },
        ];
        return new ReactionTable(reactions, CreateDefinitionsFrom(materials));
    }

    private static MaterialDef[] CreateDefinitionsFrom(MaterialTable materials)
    {
        MaterialDef[] definitions = new MaterialDef[materials.Count];
        for (ushort i = 0; i < definitions.Length; i++)
        {
            definitions[i] = materials.Get(i);
        }

        return definitions;
    }

    private static MaterialDef Material(
        ushort id,
        string name,
        CellType type,
        byte density,
        byte dispersion = 0,
        int reactionStart = 0,
        byte reactionCount = 0,
        MaterialProperty propertyFlags = MaterialProperty.None)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = density,
            Dispersion = dispersion,
            PropertyFlags = propertyFlags,
            ReactionStart = reactionStart,
            ReactionCount = reactionCount,
            HeatCapacity = 1f,
            TextureId = -1,
        };
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

    private static void QueueVerticalCut(CellGrid grid, RigidDamageQueue damageQueue, int x, int minY, int maxY)
    {
        for (int y = minY; y < maxY; y++)
        {
            grid.FlagsAt(x, y) = 0;
            grid.MaterialAt(x, y) = 0;
            damageQueue.OnOwnedCellDamaged(x, y);
        }
    }

    private static void SetCurrentDirty(Chunk chunk, DirtyRect rect)
    {
        chunk.SetCurrentDirty(rect);
    }

    private static void Set(Chunk chunk, int x, int y, ushort material)
    {
        chunk.MaterialBuffer[CellAddressing.LocalIndex(x, y)] = material;
    }

    private static ushort Get(Chunk chunk, int x, int y)
    {
        return chunk.MaterialBuffer[CellAddressing.LocalIndex(x, y)];
    }

    private static void SetFlags(Chunk chunk, int x, int y, byte flags)
    {
        chunk.FlagsBuffer[CellAddressing.LocalIndex(x, y)] = flags;
    }

    private static byte GetFlags(Chunk chunk, int x, int y)
    {
        return chunk.FlagsBuffer[CellAddressing.LocalIndex(x, y)];
    }

    private sealed class TestChunkSource(params Chunk[] chunks) : IChunkSource
    {
        private readonly Dictionary<ChunkCoord, Chunk> _map = chunks.ToDictionary(static chunk => chunk.Coord);

        public ReadOnlySpan<Chunk> ResidentChunks => chunks;

        public static TestChunkSource CreateNeighborhood(ChunkCoord center, out Chunk centerChunk)
        {
            List<Chunk> chunks = [];
            centerChunk = new Chunk(center);
            for (int y = center.Y - 1; y <= center.Y + 1; y++)
            {
                for (int x = center.X - 1; x <= center.X + 1; x++)
                {
                    chunks.Add(x == center.X && y == center.Y ? centerChunk : new Chunk(new ChunkCoord(x, y)));
                }
            }

            return new TestChunkSource([.. chunks]);
        }

        public static TestChunkSource CreateSquare(int radius)
        {
            List<Chunk> chunks = [];
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    chunks.Add(new Chunk(new ChunkCoord(x, y)));
                }
            }

            return new TestChunkSource([.. chunks]);
        }

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
