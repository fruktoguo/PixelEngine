using System.Numerics;
using PixelEngine.Core.Mathematics;
using PixelEngine.Interop.Box2D;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// 静态地形 collider 测试。
/// </summary>
public sealed class StaticTerrainCollidersTests
{
    /// <summary>
    /// 验证只为活跃刚体邻近 chunk 建立地形 chain，内容变化时重建，离域时销毁。
    /// </summary>
    [Fact]
    public void UpdateBuildsRebuildsAndDestroysChunkChainsNearAwakeBodies()
    {
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

            Assert.Equal(1, colliders.ColliderChunkCount);
            Assert.Equal(1, colliders.LastRebuiltChunkCount);
            Assert.Equal(0, colliders.LastDestroyedChunkCount);

            colliders.Update(source, physicsWorld);

            Assert.Equal(1, colliders.ColliderChunkCount);
            Assert.Equal(0, colliders.LastRebuiltChunkCount);
            Assert.Equal(0, colliders.LastDestroyedChunkCount);

            chunk.Material[CellAddressing.LocalIndexFromLocal(8, 48)] = 0;
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
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

        try
        {
            Chunk chunk = new(new ChunkCoord(0, 0));
            FillRect(chunk, minX: 8, minY: 48, maxX: 56, maxY: 56, material: 2);
            for (int i = 0; i < chunk.Flags.Length; i++)
            {
                chunk.Flags[i] = CellFlags.RigidOwned;
            }

            TestChunkSource source = new(chunk);
            PhysicsWorld physicsWorld = new();
            _ = physicsWorld.AddBody(CreateBody(worldId, new Vector2(32, 32)), CreateMask(16, 16));
            using StaticTerrainColliders colliders = new(worldId, expandedChunkRadius: 0);

            colliders.Update(source, physicsWorld);

            Assert.Equal(0, colliders.ColliderChunkCount);
            Assert.Equal(0, colliders.LastRebuiltChunkCount);
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
                chunk.Material[CellAddressing.LocalIndexFromLocal(x, y)] = material;
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
