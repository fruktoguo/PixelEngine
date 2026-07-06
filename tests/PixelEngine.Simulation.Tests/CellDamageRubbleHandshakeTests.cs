using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// 结构破坏动作与 rubble / 碎屑 / 采集事件握手测试。
/// </summary>
public sealed class CellDamageRubbleHandshakeTests
{
    private const ushort Empty = 0;
    private const ushort Stone = 1;
    private const ushort Gravel = 2;
    private const ushort Crystal = 3;
    private const ushort Dirt = 4;

    /// <summary>
    /// 验证破坏到 rubble target 时清 Damage、写默认 lifetime，并发布碎屑与采集事件。
    /// </summary>
    [Fact]
    public void StructuralDamageDestroyedCellPublishesRubbleDebrisAndMineYield()
    {
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(-1, -1, 1, 1);
        Chunk chunk = source.GetRequired(new ChunkCoord(0, 0));
        MaterialTable materials = new(CreateMaterials());
        RecordingCellDestructionSink sink = new();
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot), cellDestructionSink: sink);
        int local = CellAddressing.LocalIndexFromLocal(63, 10);
        chunk.Material[local] = Crystal;
        chunk.Damage[local] = 2;

        bool destroyed = kernel.ApplyStructuralDamage(63, 10, damage: 20);

        Assert.True(destroyed);
        Assert.Equal(Gravel, chunk.Material[local]);
        Assert.Equal(CellFlags.SetParity(0, kernel.CurrentParity), chunk.Flags[local]);
        Assert.Equal(9, chunk.Lifetime[local]);
        Assert.Equal(0, chunk.Damage[local]);
        Assert.Equal(1, sink.Count);
        Assert.Equal(new CellDestructionEvent(63, 10, Crystal, Gravel, Gravel, 4, 3), sink.Last);

        BoundaryWakeSnapshot[] wake = new BoundaryWakeSnapshot[8];
        int wakeCount = kernel.CopyBoundaryWakeSnapshots(wake);
        Assert.True(wakeCount > 0);
        Assert.Contains(wake.AsSpan(0, wakeCount).ToArray(), item => item.TargetCoord == new ChunkCoord(1, 0));
    }

    /// <summary>
    /// 验证结构破坏事件接入粒子系统后，会生成真实碎屑粒子且不二次清空 rubble cell。
    /// </summary>
    [Fact]
    public void StructuralDamageDestroyedCellSpawnsDebrisParticlesThroughParticleSystem()
    {
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 0, 0);
        Chunk chunk = source.GetRequired(new ChunkCoord(0, 0));
        MaterialTable materials = new(CreateMaterials());
        ParticleSystem particles = new(capacity: 8);
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot), cellDestructionSink: particles);
        int local = CellAddressing.LocalIndexFromLocal(20, 20);
        chunk.Material[local] = Crystal;

        bool destroyed = kernel.ApplyStructuralDamage(20, 20, damage: 20);

        Assert.True(destroyed);
        Assert.Equal(Gravel, chunk.Material[local]);
        Assert.Equal(4, particles.ActiveCount);
        Assert.Equal(4, particles.Stats.SpawnedThisTick);
        for (int i = 0; i < particles.ActiveCount; i++)
        {
            Particle particle = particles.ActiveReadOnly[i];
            Assert.Equal(Gravel, particle.Material);
            Assert.Equal(20.5f, particle.X);
            Assert.Equal(20.5f, particle.Y);
            Assert.True(particle.Life > 0);
        }
    }

    /// <summary>
    /// 验证未达到破坏阈值或 DebrisCount=0 时不会生成碎屑粒子。
    /// </summary>
    [Fact]
    public void StructuralDamageSpawnsDebrisOnlyForDestroyedCellsWithPositiveDebrisCount()
    {
        const ushort NoDebrisStone = 5;
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 0, 0);
        Chunk chunk = source.GetRequired(new ChunkCoord(0, 0));
        MaterialTable materials = new(
        [
            .. CreateMaterials(),
            Material(NoDebrisStone, "no_debris_stone", CellType.Solid) with { Integrity = 1 },
        ]);
        ParticleSystem particles = new(capacity: 8);
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot), cellDestructionSink: particles);
        Set(chunk, 22, 20, Stone);
        Set(chunk, 24, 20, NoDebrisStone);

        Assert.False(kernel.ApplyStructuralDamage(22, 20, damage: 3));
        Assert.Equal(0, particles.ActiveCount);

        Assert.True(kernel.ApplyStructuralDamage(24, 20, damage: 10));
        Assert.Equal(0, particles.ActiveCount);
        Assert.Equal(0, particles.Stats.SpawnedThisTick);
    }

    /// <summary>
    /// 验证破坏到 Empty 时清 flags/lifetime，碎屑材质回退为原材质，非 Diggable 不产采集量。
    /// </summary>
    [Fact]
    public void StructuralDamageDestroyedToEmptyPublishesSourceDebrisMaterialAndNoMineYieldWithoutDiggable()
    {
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 0, 0);
        Chunk chunk = source.GetRequired(new ChunkCoord(0, 0));
        MaterialTable materials = new(CreateMaterials());
        RecordingCellDestructionSink sink = new();
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot), cellDestructionSink: sink);
        int local = CellAddressing.LocalIndexFromLocal(12, 12);
        chunk.Material[local] = Dirt;
        chunk.Flags[local] = CellFlags.Burning;
        chunk.Lifetime[local] = 7;
        chunk.Damage[local] = 4;

        bool destroyed = kernel.ApplyStructuralDamage(12, 12, damage: 10);

        Assert.True(destroyed);
        Assert.Equal(Empty, chunk.Material[local]);
        Assert.Equal(0, chunk.Flags[local]);
        Assert.Equal(0, chunk.Lifetime[local]);
        Assert.Equal(0, chunk.Damage[local]);
        Assert.Equal(new CellDestructionEvent(12, 12, Dirt, Empty, Dirt, 2, 0), sink.Last);
    }

    /// <summary>
    /// 验证未破坏、刚体占用和不可破坏 cell 都不会发布普通 cell destruction 事件。
    /// </summary>
    [Fact]
    public void StructuralDamageDoesNotPublishForRigidOwnedAccumulationOrIndestructibleCells()
    {
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 0, 0);
        Chunk chunk = source.GetRequired(new ChunkCoord(0, 0));
        MaterialTable materials = new(CreateMaterials());
        CountingRigidDamageSink rigidSink = new();
        RecordingCellDestructionSink destructionSink = new();
        SimulationKernel kernel = new(
            source,
            new MaterialPropsTable(materials.Hot),
            rigidDamageSink: rigidSink,
            cellDestructionSink: destructionSink);
        Set(chunk, 10, 10, Stone);
        Set(chunk, 11, 10, Stone, CellFlags.RigidOwned);
        Set(chunk, 12, 10, Stone);
        Set(chunk, 13, 10, Crystal);
        chunk.Damage[CellAddressing.LocalIndexFromLocal(11, 10)] = 6;

        Assert.False(kernel.ApplyStructuralDamage(10, 10, damage: 3));
        Assert.False(kernel.ApplyStructuralDamage(11, 10, damage: 100));
        Assert.False(kernel.ApplyStructuralDamage(13, 10, damage: 0));
        _ = materials.ReloadStable(
            [
                Material(Empty, "empty", CellType.Empty),
                Material(Stone, "stone", CellType.Solid) with { PropertyFlags = MaterialProperty.Indestructible },
                Material(Gravel, "gravel", CellType.Powder),
                Material(Crystal, "crystal", CellType.Solid),
                Material(Dirt, "dirt", CellType.Solid),
            ],
            [],
            fallbackId: Empty);
        kernel.ReloadMaterialHotTable(materials.Hot);
        Assert.False(kernel.ApplyStructuralDamage(12, 10, damage: 100));

        Assert.Equal(1, rigidSink.Count);
        Assert.Equal(0, chunk.Damage[CellAddressing.LocalIndexFromLocal(11, 10)]);
        Assert.Equal(0, destructionSink.Count);
    }

    private static MaterialDef[] CreateMaterials()
    {
        return
        [
            Material(Empty, "empty", CellType.Empty),
            Material(Stone, "stone", CellType.Solid) with { Hardness = 2, Integrity = 24, DestroyedTarget = Gravel, DebrisCount = 1 },
            Material(Gravel, "gravel", CellType.Powder) with { DefaultLifetime = 9 },
            Material(Crystal, "crystal", CellType.Solid) with
            {
                Integrity = 10,
                DestroyedTarget = Gravel,
                DebrisCount = 4,
                MineYield = 3,
                PropertyFlags = MaterialProperty.Diggable,
            },
            Material(Dirt, "dirt", CellType.Solid) with { Integrity = 1, DebrisCount = 2, MineYield = 5 },
        ];
    }

    private static MaterialDef Material(ushort id, string name, CellType type)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = type == CellType.Empty ? (byte)0 : (byte)120,
            HeatCapacity = 1,
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material, byte flags = 0)
    {
        int local = CellAddressing.LocalIndexFromLocal(lx, ly);
        chunk.Material[local] = material;
        chunk.Flags[local] = flags;
    }

    private sealed class RecordingCellDestructionSink : ICellDestructionSink
    {
        public int Count { get; private set; }

        public CellDestructionEvent Last { get; private set; }

        public void OnCellDestroyed(in CellDestructionEvent item)
        {
            Count++;
            Last = item;
        }
    }

    private sealed class CountingRigidDamageSink : IRigidDamageSink
    {
        public int Count { get; private set; }

        public void OnOwnedCellDamaged(int wx, int wy)
        {
            Count++;
        }
    }
}
