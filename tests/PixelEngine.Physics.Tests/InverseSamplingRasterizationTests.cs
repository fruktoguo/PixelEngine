using System.Numerics;
using PixelEngine.Core.Mathematics;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// plan/14 inverse-sampling 栅格化验收测试。
/// </summary>
public sealed class InverseSamplingRasterizationTests
{
    /// <summary>
    /// 验证刚体 mask 在一圈角度下 inverse-sampling 栅格化连通、无内部空洞，且面积在 ±1px 容差内。
    /// </summary>
    [Fact]
    public void RotatedMaskRasterizesWatertightWithAreaTolerance()
    {
        float[] angles = [0f, 0.001f, MathF.PI / 2f, MathF.PI, MathF.PI * 1.5f];
        BodyLocalMask mask = CreateFilledMask(width: 3, height: 3, material: 2, origin: new Vector2(1.5f, 1.5f));

        foreach (float angle in angles)
        {
            TestChunkSource source = TestChunkSource.CreateSquare(radius: 1);
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PixelRigidBody body = new(1, default, mask);
            RigidStampRegistry registry = new();
            Transform2D transform = new(new Vector2(32f, 32f), angle);

            int stamped = RigidBodyRasterizer.StampInverseSampling(body, in transform, grid, registry);

            Assert.InRange(stamped, mask.SolidPixelCount - 1, mask.SolidPixelCount + 1);
            AssertConnected(body.PreviousStamps);
            AssertNoInternalHoles(body.PreviousStamps);
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

    private static void AssertConnected(List<RigidStampedCell> stamps)
    {
        HashSet<long> occupied = new(stamps.Count);
        foreach (RigidStampedCell stamp in stamps)
        {
            _ = occupied.Add(Pack(stamp.WorldX, stamp.WorldY));
        }

        Queue<RigidStampedCell> queue = new();
        HashSet<long> visited = [];
        queue.Enqueue(stamps[0]);
        _ = visited.Add(Pack(stamps[0].WorldX, stamps[0].WorldY));
        while (queue.Count > 0)
        {
            RigidStampedCell current = queue.Dequeue();
            TryVisit(current.WorldX - 1, current.WorldY);
            TryVisit(current.WorldX + 1, current.WorldY);
            TryVisit(current.WorldX, current.WorldY - 1);
            TryVisit(current.WorldX, current.WorldY + 1);
        }

        Assert.Equal(occupied.Count, visited.Count);

        void TryVisit(int x, int y)
        {
            long key = Pack(x, y);
            if (!occupied.Contains(key) || !visited.Add(key))
            {
                return;
            }

            queue.Enqueue(new RigidStampedCell(x, y, default));
        }
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
            _ = occupied.Add(Pack(stamp.WorldX, stamp.WorldY));
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
        _ = exterior.Add(Pack(minX, minY));
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            TryVisit(x - 1, y);
            TryVisit(x + 1, y);
            TryVisit(x, y - 1);
            TryVisit(x, y + 1);
        }

        for (int y = minY + 1; y < maxY; y++)
        {
            for (int x = minX + 1; x < maxX; x++)
            {
                long key = Pack(x, y);
                Assert.True(occupied.Contains(key) || exterior.Contains(key), $"检测到内部空洞：({x}, {y})。");
            }
        }

        void TryVisit(int x, int y)
        {
            if (x < minX || x > maxX || y < minY || y > maxY)
            {
                return;
            }

            long key = Pack(x, y);
            if (occupied.Contains(key) || !exterior.Add(key))
            {
                return;
            }

            queue.Enqueue((x, y));
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
            neighborhood = default;
            return false;
        }
    }
}
