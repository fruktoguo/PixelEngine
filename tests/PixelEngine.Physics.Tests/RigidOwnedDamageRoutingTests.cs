using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// demo-playability RigidOwned 破坏路由测试。
/// 不变式：RigidOwned 伤害只路由到所属刚体队列。
/// </summary>
public sealed class RigidOwnedDamageRoutingTests
{
    private const ushort Empty = 0;
    private const ushort Stone = 1;

    /// <summary>
    /// DamageCircle 命中 RigidOwned cell 时只入刚体损伤队列，不在 cell Damage 平面累加。
    /// </summary>
    [Fact]
    public void DamageCircleRoutesRigidOwnedCellToPhysicsQueueWithoutAccumulatingDamage()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = TestChunkSource.CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        int local = CellAddressing.LocalIndexFromLocal(10, 10);
        center.MaterialBuffer[local] = Stone;
        center.FlagsBuffer[local] = CellFlags.RigidOwned;
        center.DamageBuffer[local] = 77;
        RigidDamageQueue damageQueue = new();
        SimulationKernel kernel = new(source, CreateMaterials(), rigidDamageSink: damageQueue);

        int destroyed = kernel.DamageCircle(10, 10, radius: 0, damage: 255, falloff: false);

        // Assert：验证预期结果
        Assert.Equal(0, destroyed);
        Assert.Equal(Stone, center.MaterialBuffer[local]);
        Assert.True(CellFlags.Has(center.FlagsBuffer[local], CellFlags.RigidOwned));
        Assert.Equal(0, center.DamageBuffer[local]);
        Span<RigidDamageEvent> drained = stackalloc RigidDamageEvent[1];
        Assert.Equal(1, damageQueue.DrainTo(drained));
        Assert.Equal(new RigidDamageEvent(10, 10, Stone), drained[0]);
    }

    /// <summary>
    /// DamageBeam 命中 RigidOwned cell 时沿同一 IRigidDamageSink 路由给 PhysicsSystem。
    /// </summary>
    [Fact]
    public void DamageBeamRoutesRigidOwnedCellToPhysicsQueueWithoutMutatingMaterial()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = TestChunkSource.CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        int normalLocal = CellAddressing.LocalIndexFromLocal(8, 10);
        int rigidLocal = CellAddressing.LocalIndexFromLocal(9, 10);
        center.MaterialBuffer[normalLocal] = Stone;
        center.MaterialBuffer[rigidLocal] = Stone;
        center.FlagsBuffer[rigidLocal] = CellFlags.RigidOwned;
        RigidDamageQueue damageQueue = new();
        SimulationKernel kernel = new(source, CreateMaterials(), rigidDamageSink: damageQueue);

        int destroyed = kernel.DamageBeam(8, 10, dirX: 1f, dirY: 0f, length: 1, damagePerCell: 255);

        // Assert：验证预期结果
        Assert.Equal(1, destroyed);
        Assert.Equal(Empty, center.MaterialBuffer[normalLocal]);
        Assert.Equal(Stone, center.MaterialBuffer[rigidLocal]);
        Assert.True(CellFlags.Has(center.FlagsBuffer[rigidLocal], CellFlags.RigidOwned));
        Assert.Equal(0, center.DamageBuffer[rigidLocal]);
        Span<RigidDamageEvent> drained = stackalloc RigidDamageEvent[1];
        Assert.Equal(1, damageQueue.DrainTo(drained));
        Assert.Equal(new RigidDamageEvent(9, 10, Stone), drained[0]);
    }

    private static MaterialPropsTable CreateMaterials()
    {
        MaterialTable table = new(
        [
            Material(Empty, "empty", CellType.Empty),
            Material(Stone, "stone", CellType.Solid),
        ]);
        return new MaterialPropsTable(table.Hot);
    }

    private static MaterialDef Material(ushort id, string name, CellType type)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = type == CellType.Empty ? (byte)0 : (byte)200,
            HeatCapacity = 1,
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }

    private sealed class TestChunkSource : IChunkSource
    {
        private readonly Dictionary<ChunkCoord, Chunk> _byCoord;
        private readonly Chunk[] _resident;

        private TestChunkSource(Chunk[] chunks)
        {
            _resident = chunks;
            _byCoord = new Dictionary<ChunkCoord, Chunk>(chunks.Length);
            foreach (Chunk chunk in chunks)
            {
                _byCoord.Add(chunk.Coord, chunk);
            }
        }

        public ReadOnlySpan<Chunk> ResidentChunks => _resident;

        public static TestChunkSource CreateNeighborhood(ChunkCoord centerCoord, out Chunk center)
        {
            Chunk[] chunks = new Chunk[9];
            int index = 0;
            center = null!;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Chunk chunk = new(new ChunkCoord(centerCoord.X + dx, centerCoord.Y + dy));
                    chunks[index++] = chunk;
                    if (dx == 0 && dy == 0)
                    {
                        center = chunk;
                    }
                }
            }

            return new TestChunkSource(chunks);
        }

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            return _byCoord.TryGetValue(coord, out chunk!);
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
