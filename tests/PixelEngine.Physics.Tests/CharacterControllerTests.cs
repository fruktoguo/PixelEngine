using System.Numerics;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// kinematic AABB 角色控制器测试。
/// </summary>
public sealed class CharacterControllerTests
{
    /// <summary>
    /// 验证向下移动会停在固体地面上且不会穿透。
    /// </summary>
    [Fact]
    public void MoveDownStopsOnSolidGround()
    {
        TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
        FillRect(source.Center, 0, 10, 32, 11, material: 1);
        CharacterController controller = CreateController(source, new Vector2(4, 0), new Vector2(4, 4));

        controller.Move(new Vector2(0, 20), out CharacterCollisionInfo info);

        Assert.True(info.IsGrounded);
        Assert.Equal(6f, controller.Position.Y);
        Assert.False(controller.OverlapsSolid(controller.Bounds));
    }

    /// <summary>
    /// 验证水平移动会被墙体阻挡。
    /// </summary>
    [Fact]
    public void MoveHorizontalStopsAtWall()
    {
        TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
        FillRect(source.Center, 10, 0, 11, 16, material: 1);
        CharacterController controller = CreateController(source, new Vector2(2, 2), new Vector2(4, 4));

        controller.Move(new Vector2(20, 0), out CharacterCollisionInfo info);

        Assert.True(info.HitWallRight);
        Assert.Equal(6f, controller.Position.X);
        Assert.False(controller.OverlapsSolid(controller.Bounds));
    }

    /// <summary>
    /// 验证 grounded 状态下水平受阻会按 StepUpHeight 爬上小台阶。
    /// </summary>
    [Fact]
    public void MoveHorizontalCanStepUpSmallObstacle()
    {
        TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
        FillRect(source.Center, 0, 10, 32, 11, material: 1);
        FillRect(source.Center, 8, 9, 9, 10, material: 1);
        CharacterController controller = CreateController(source, new Vector2(3, 6), new Vector2(4, 4));
        controller.StepUpHeight = 2;

        controller.Move(new Vector2(8, 0), out CharacterCollisionInfo info);

        Assert.False(info.HitWallRight);
        Assert.True(controller.Position.X > 8f);
        Assert.True(controller.Position.Y < 6f);
        Assert.False(controller.OverlapsSolid(controller.Bounds));
    }

    /// <summary>
    /// 验证 RigidOwned cell 即使 material 为 Empty 也被视为固体。
    /// </summary>
    [Fact]
    public void RigidOwnedCellsAreSolidForCharacter()
    {
        TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
        FillRigidOwned(source.Center, 0, 10, 32, 11);
        CharacterController controller = CreateController(source, new Vector2(4, 0), new Vector2(4, 4));

        controller.Move(new Vector2(0, 20), out CharacterCollisionInfo info);

        Assert.True(info.IsGrounded);
        Assert.Equal(6f, controller.Position.Y);
    }

    /// <summary>
    /// 验证液体材质不会阻挡 AABB。
    /// </summary>
    [Fact]
    public void LiquidCellsDoNotBlockCharacter()
    {
        TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
        FillRect(source.Center, 0, 10, 32, 11, material: 3);
        CharacterController controller = CreateController(source, new Vector2(4, 0), new Vector2(4, 4));

        controller.Move(new Vector2(0, 12), out CharacterCollisionInfo info);

        Assert.False(info.IsGrounded);
        Assert.Equal(12f, controller.Position.Y);
    }

    private static CharacterController CreateController(TestChunkSource source, Vector2 position, Vector2 size)
    {
        CellGrid grid = new(source, CreateMaterials());
        CharacterController controller = new(grid, position, size)
        {
            SkinWidth = 0.05f,
            MaxSubIterations = 64,
            StepUpHeight = 2,
        };
        return controller;
    }

    private static MaterialPropsTable CreateMaterials()
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder, CellType.Liquid],
            [0, 255, 180, 50],
            [0, 0, 0, 4],
            [0, 0, 0, 0],
            [0, 0, 0, 0],
            [0, 0, 0, 0]);
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

    private static void FillRigidOwned(Chunk chunk, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                int index = CellAddressing.LocalIndexFromLocal(x, y);
                chunk.Flags[index] = CellFlags.RigidOwned;
            }
        }
    }

    private sealed class TestChunkSource(params Chunk[] chunks) : IChunkSource
    {
        private readonly Dictionary<ChunkCoord, Chunk> _map = chunks.ToDictionary(static chunk => chunk.Coord);

        public Chunk Center => chunks[0];

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
