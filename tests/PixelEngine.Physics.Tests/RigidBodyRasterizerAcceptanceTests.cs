using System.Numerics;
using PixelEngine.Core.Mathematics;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// 刚体 inverse-sampling 与跨 chunk 同步验收测试。
/// 不变式：inverse-sampling 跨 chunk 同步无空洞与重影。
/// </summary>
public sealed class RigidBodyRasterizerAcceptanceTests
{
    /// <summary>
    /// 验证多个旋转角下 inverse-sampling 不产生内部空洞，并且不会少于不可变权威 mask 的固体像素数。
    /// </summary>
    [Fact]
    public void InverseSamplingMultipleRotationsIsWatertightAndDoesNotErodeMask()
    {
        // Arrange：准备输入与初始状态
        float[] angles = [0f, 0.13f, 0.37f, 0.79f, 1.21f, 1.57f, 2.44f];
        BodyLocalMask mask = CreateFilledMask(width: 9, height: 7, material: 2, origin: new Vector2(4.5f, 3.5f));

        for (int i = 0; i < angles.Length; i++)
        {
            TestChunkSource source = TestChunkSource.CreateSquare(radius: 2);
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PixelRigidBody body = new(7, default, mask);
            RigidStampRegistry registry = new();
            Transform2D transform = new(new Vector2(80.25f + i, 78.5f - i), angles[i]);

            int stamped = RigidBodyRasterizer.StampInverseSampling(body, in transform, grid, registry);

            // Assert：验证预期结果
            Assert.True(stamped >= mask.SolidPixelCount, $"角度 {angles[i]} 出现亚像素侵蚀：{stamped} < {mask.SolidPixelCount}。");
            Assert.Equal(stamped, body.PreviousStamps.Count);
            AssertNoInternalHoles(body.PreviousStamps);
            Assert.All(body.PreviousStamps, stamp =>
            {
                Assert.True(registry.TryGet(stamp.WorldX, stamp.WorldY, out RigidStamp registered));
                Assert.Equal(body.BodyKey, registered.BodyKey);
                Assert.True(CellFlags.Has(grid.FlagsAt(stamp.WorldX, stamp.WorldY), CellFlags.RigidOwned));
                Assert.Equal(stamp.Stamp.Material, grid.GetMaterial(stamp.WorldX, stamp.WorldY));
            });
        }
    }

    /// <summary>
    /// 验证刚体跨 chunk 边界 stamp 时 registry、grid flag 与 erase 语义保持一致。
    /// </summary>
    [Fact]
    public void StampAndEraseAcrossChunkBoundaryKeepsRegistryConsistent()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = TestChunkSource.CreateSquare(radius: 1);
        CellGrid grid = new(source, MaterialPropsTable.Empty);
        BodyLocalMask mask = CreateFilledMask(width: 8, height: 8, material: 3, origin: new Vector2(4f, 4f));
        PixelRigidBody body = new(11, default, mask);
        RigidStampRegistry registry = new();
        Transform2D transform = new(new Vector2(64f, 64f), 0.23f);

        int stamped = RigidBodyRasterizer.StampInverseSampling(body, in transform, grid, registry);

        // Assert：验证预期结果
        Assert.True(stamped >= mask.SolidPixelCount);
        Assert.Contains(body.PreviousStamps, static stamp => stamp.WorldX < 64);
        Assert.Contains(body.PreviousStamps, static stamp => stamp.WorldX >= 64);
        Assert.Contains(body.PreviousStamps, static stamp => stamp.WorldY < 64);
        Assert.Contains(body.PreviousStamps, static stamp => stamp.WorldY >= 64);
        foreach (RigidStampedCell cell in body.PreviousStamps)
        {
            Assert.True(registry.TryGet(cell.WorldX, cell.WorldY, out RigidStamp registered));
            Assert.Equal(body.BodyKey, registered.BodyKey);
            Assert.Equal(cell.Stamp.LocalX, registered.LocalX);
            Assert.Equal(cell.Stamp.LocalY, registered.LocalY);
            Assert.True(CellFlags.Has(grid.FlagsAt(cell.WorldX, cell.WorldY), CellFlags.RigidOwned));
            Assert.Equal((ushort)3, grid.GetMaterial(cell.WorldX, cell.WorldY));
        }

        RigidStampedCell consumed = body.PreviousStamps[stamped / 2];
        grid.FlagsAt(consumed.WorldX, consumed.WorldY) = 0;
        grid.MaterialAt(consumed.WorldX, consumed.WorldY) = 9;

        int erased = RigidBodyRasterizer.EraseAtCurrentTransform(body, grid, registry);

        Assert.Equal(stamped - 1, erased);
        Assert.Empty(body.PreviousStamps);
        Assert.Equal((ushort)9, grid.GetMaterial(consumed.WorldX, consumed.WorldY));
        Assert.False(CellFlags.Has(grid.FlagsAt(consumed.WorldX, consumed.WorldY), CellFlags.RigidOwned));
    }

    /// <summary>
    /// 验证刚体离开当前驻留 ring 时，不会把 stamp 写入缺少 dirty 邻居保障的边界 cell。
    /// </summary>
    [Fact]
    public void StampNearMissingBoundaryNeighborSkipsCellInsteadOfThrowing()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = new(new Chunk(new ChunkCoord(0, 0)));
        CellGrid grid = new(source, MaterialPropsTable.Empty);
        BodyLocalMask mask = CreateFilledMask(width: 1, height: 1, material: 4, origin: Vector2.Zero);
        PixelRigidBody body = new(13, default, mask);
        RigidStampRegistry registry = new();
        Transform2D transform = new(new Vector2(63f, 20f), 0f);

        int stamped = RigidBodyRasterizer.StampInverseSampling(body, in transform, grid, registry);

        // Assert：验证预期结果
        Assert.Equal(0, stamped);
        Assert.Empty(body.PreviousStamps);
        Assert.False(registry.TryGet(63, 20, out _));
        Assert.Equal((ushort)0, grid.GetMaterial(63, 20));
        Assert.False(CellFlags.Has(grid.FlagsAt(63, 20), CellFlags.RigidOwned));
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
