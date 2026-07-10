using System.Numerics;
using PixelEngine.Core;
using PixelEngine.Core.Mathematics;
using PixelEngine.Interop.Box2D;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// 静态地形 collider 测试。
/// 不变式：静态地形 collider 与固体栅格对齐。
/// </summary>
public sealed class StaticTerrainCollidersTests
{
    /// <summary>
    /// 验证只为活跃刚体邻近 chunk 建立地形 chain，内容变化时重建，离域时销毁。
    /// </summary>
    [Fact]
    public void UpdateBuildsRebuildsAndDestroysChunkChainsNearAwakeBodies()
    {
        // Arrange：准备输入与初始状态
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            Chunk chunk = new(new ChunkCoord(0, 0));
            FillRect(chunk, minX: 8, minY: 48, maxX: 56, maxY: 56, material: 2);
            TestChunkSource source = new(chunk);
            PhysicsWorld physicsWorld = new();
            B2BodyId bodyId = CreateBody(worldId, new Vector2(32, 32));
            _ = physicsWorld.AddBody(bodyId, CreateMask(16, 16));
            using StaticTerrainColliders colliders = new(worldId, expandedChunkRadius: 0);

            colliders.Update(source, physicsWorld);

            // Assert：验证预期结果
            Assert.Equal(1, colliders.ColliderChunkCount);
            Assert.Equal(1, colliders.LastRebuiltChunkCount);
            Assert.Equal(0, colliders.LastDestroyedChunkCount);

            colliders.Update(source, physicsWorld);

            Assert.Equal(1, colliders.ColliderChunkCount);
            Assert.Equal(0, colliders.LastRebuiltChunkCount);
            Assert.Equal(0, colliders.LastDestroyedChunkCount);

            chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(8, 48)] = 0;
            colliders.Update(source, physicsWorld);

            Assert.Equal(1, colliders.ColliderChunkCount);
            Assert.Equal(1, colliders.LastRebuiltChunkCount);
            Assert.Equal(1, colliders.LastDestroyedChunkCount);

            Box2D.b2Body_SetAwake(bodyId, 0);
            colliders.Update(source, physicsWorld);

            Assert.Equal(0, colliders.ColliderChunkCount);
            Assert.Equal(1, colliders.LastDestroyedChunkCount);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证 RigidOwned 像素不会进入静态地形 collider。
    /// </summary>
    [Fact]
    public void UpdateExcludesRigidOwnedCellsFromTerrainMask()
    {
        // Arrange：准备输入与初始状态
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            Chunk chunk = new(new ChunkCoord(0, 0));
            FillRect(chunk, minX: 8, minY: 48, maxX: 56, maxY: 56, material: 2);
            for (int i = 0; i < chunk.FlagsBuffer.Length; i++)
            {
                chunk.FlagsBuffer[i] = CellFlags.RigidOwned;
            }

            TestChunkSource source = new(chunk);
            PhysicsWorld physicsWorld = new();
            _ = physicsWorld.AddBody(CreateBody(worldId, new Vector2(32, 32)), CreateMask(16, 16));
            using StaticTerrainColliders colliders = new(worldId, expandedChunkRadius: 0);

            colliders.Update(source, physicsWorld);

            // Assert：验证预期结果
            Assert.Equal(0, colliders.ColliderChunkCount);
            Assert.Equal(0, colliders.LastRebuiltChunkCount);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证同一 chunk 在预热后的持续 collider 重建复用边界追踪和 chain scratch，
    /// 不在物理热路径产生托管堆分配。
    /// </summary>
    [Fact]
    public void UpdateReusesGeometryScratchAcrossSteadyRebuildsWithoutAllocations()
    {
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            Chunk chunk = new(new ChunkCoord(0, 0));
            FillRect(chunk, minX: 0, minY: 0, maxX: EngineConstants.ChunkSize, maxY: EngineConstants.ChunkSize, material: 2);
            int toggledCell = CellAddressing.LocalIndexFromLocal(0, 0);
            TestChunkSource source = new(chunk);
            PhysicsWorld physicsWorld = new();
            _ = physicsWorld.AddBody(CreateBody(worldId, new Vector2(32, 32)), CreateMask(16, 16));
            using StaticTerrainColliders colliders = new(worldId, expandedChunkRadius: 0);

            // 预热完整实心与单孔两种 topology，填满 ArrayPool 与长期 scratch。
            colliders.Update(source, physicsWorld);
            chunk.MaterialBuffer[toggledCell] = 0;
            colliders.Update(source, physicsWorld);
            chunk.MaterialBuffer[toggledCell] = 2;
            colliders.Update(source, physicsWorld);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 16; i++)
            {
                chunk.MaterialBuffer[toggledCell] = (i & 1) == 0 ? (ushort)0 : (ushort)2;
                colliders.Update(source, physicsWorld);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.Equal(0, allocated);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    /// <summary>
    /// 验证 Tilemap 粗回退会输出连续横向 run。
    /// </summary>
    [Fact]
    public void TilemapColliderBuildsRowRunRects()
    {
        Chunk chunk = new(new ChunkCoord(1, -1));
        FillRect(chunk, minX: 2, minY: 3, maxX: 5, maxY: 4, material: 2);
        Span<RectI> rects = stackalloc RectI[4];

        int count = TilemapCollider.BuildRowRunRects(chunk, rects);

        Assert.Equal(1, count);
        Assert.Equal(RectI.FromBounds(66, -61, 69, -60), rects[0]);
    }

    private static B2BodyId CreateBody(B2WorldId worldId, Vector2 positionPixels)
    {
        B2BodyDef bodyDef = Box2D.b2DefaultBodyDef();
        bodyDef.Type = B2BodyType.DynamicBody;
        bodyDef.Position = new B2Vec2
        {
            X = PhysicsScale.PixelToPhysics(positionPixels.X),
            Y = PhysicsScale.PixelToPhysics(positionPixels.Y),
        };
        return Box2D.b2CreateBody(worldId, in bodyDef);
    }

    private static BodyLocalMask CreateMask(int width, int height)
    {
        int area = width * height;
        byte[] solid = new byte[area];
        ushort[] material = new ushort[area];
        Array.Fill(solid, (byte)1);
        Array.Fill(material, (ushort)2);
        return new BodyLocalMask(width, height, Vector2.Zero, solid, material);
    }

    private static void FillRect(Chunk chunk, int minX, int minY, int maxX, int maxY, ushort material)
    {
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(x, y)] = material;
            }
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
