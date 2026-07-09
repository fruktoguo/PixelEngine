using PixelEngine.Core;
using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Plan 05 节点 3 的自由粒子生命周期与 R13 无泄漏回退测试。
/// 不变式：粒子生命周期与 R13 无泄漏回退路径可触发。
/// </summary>
public sealed class ParticleLifecycleTests
{
    private const ushort Solid = 1;
    private const ushort Sand = 2;

    /// <summary>
    /// 验证 Life 归零的粒子即使位于可沉积空 cell，也会被硬性 max-lifetime 删除。
    /// </summary>
    [Fact]
    public void ResolveDepositsKillsExpiredParticleBeforeDepositingIntoEmptyCell()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        MaterialPropsTable materials = CreateMaterials();
        CellGrid grid = new(source, materials);
        SimulationKernel kernel = new(source, materials);
        ParticleSystem particles = new(capacity: 1);
        _ = particles.TrySpawn(new ParticleSpawn(10.25f, 10.25f, 0, 0, Sand, 0, 1));
        particles.ResetTickStats();

        particles.IntegrateAndAdvance(grid);
        particles.ResolveDeposits(kernel, grid);

        // Assert：验证预期结果
        Assert.Equal(0, particles.ActiveCount);
        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(1, particles.Stats.KilledByLifetimeThisTick);
        Assert.Equal(0, particles.Stats.DepositedThisTick);
    }

    /// <summary>
    /// 验证无处沉积且 Life 仍大于 0 的粒子保留为短命粒子，Life 到期后删除。
    /// </summary>
    [Fact]
    public void BlockedParticleStaysWhileLifeRemainsThenDies()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        FillAll(source.ResidentChunks, Solid);
        MaterialPropsTable materials = CreateMaterials();
        CellGrid grid = new(source, materials);
        SimulationKernel kernel = new(source, materials);
        ParticleSystem particles = new(capacity: 1);
        _ = particles.TrySpawn(new ParticleSpawn(10.25f, 10.25f, 0, 0, Sand, 0, 2));
        particles.ResetTickStats();

        particles.IntegrateAndAdvance(grid);
        particles.ResolveDeposits(kernel, grid);

        // Assert：验证预期结果
        Assert.Equal(1, particles.ActiveCount);
        Assert.Equal(1, particles.ActiveReadOnly[0].Life);
        Assert.Equal(0, particles.Stats.KilledByLifetimeThisTick);
        Assert.Equal(Solid, Get(center, 10, 10));

        particles.ResetTickStats();
        particles.IntegrateAndAdvance(grid);
        particles.ResolveDeposits(kernel, grid);

        Assert.Equal(0, particles.ActiveCount);
        Assert.Equal(1, particles.Stats.KilledByLifetimeThisTick);
        Assert.Equal(Solid, Get(center, 10, 10));
    }

    /// <summary>
    /// 验证持续抛射且无沉积空间的压力场景中，活跃粒子数由 max-lifetime 约束，停止抛射后完全清空。
    /// </summary>
    [Fact]
    public void ContinuousEjectionWithoutDepositSpaceIsBoundedAndEventuallyDrains()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        FillAll(source.ResidentChunks, Solid);
        MaterialPropsTable materials = CreateMaterials();
        CellGrid grid = new(source, materials);
        SimulationKernel kernel = new(source, materials);
        ParticleSystem particles = new(capacity: EngineConstants.ParticleMaxLifetimeTicks + 32);

        int maxActive = 0;
        int totalKilled = 0;
        int ejectionTicks = EngineConstants.ParticleMaxLifetimeTicks + 80;
        for (int tick = 0; tick < ejectionTicks; tick++)
        {
            particles.ResetTickStats();
            Set(center, 10, 10, Sand);
            // Assert：验证预期结果
            Assert.True(particles.RequestEjection(new EjectionRequest(10, 10, 0, 0, 0, EjectMask.Powder)));
            particles.RunEjectionPass(kernel, grid);
            Set(center, 10, 10, Solid);
            particles.IntegrateAndAdvance(grid);
            particles.ResolveDeposits(kernel, grid);
            maxActive = Math.Max(maxActive, particles.ActiveCount);
            totalKilled += particles.Stats.KilledByLifetimeThisTick;
        }

        Assert.InRange(maxActive, 1, EngineConstants.ParticleMaxLifetimeTicks - 1);

        for (int tick = 0; tick < EngineConstants.ParticleMaxLifetimeTicks + 2; tick++)
        {
            particles.ResetTickStats();
            particles.IntegrateAndAdvance(grid);
            particles.ResolveDeposits(kernel, grid);
            totalKilled += particles.Stats.KilledByLifetimeThisTick;
        }

        Assert.Equal(0, particles.ActiveCount);
        Assert.True(totalKilled >= ejectionTicks);
    }

    /// <summary>
    /// 验证粒子飞向非驻留 chunk 时不会沉积到最后一个空 cell，而是走无处沉积回退。
    /// </summary>
    [Fact]
    public void NonResidentLandingKeepsShortLivedParticleWithoutDepositingAtLastOpenCell()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        MaterialPropsTable materials = CreateMaterials();
        CellGrid grid = new(source, materials);
        SimulationKernel kernel = new(source, materials);
        ParticleSystem particles = new(capacity: 1);
        _ = particles.TrySpawn(new ParticleSpawn(63.25f, 10.25f, 65, 0, Sand, 0, 3));
        particles.ResetTickStats();

        particles.IntegrateAndAdvance(grid);
        particles.ResolveDeposits(kernel, grid);

        // Assert：验证预期结果
        Assert.Equal(1, particles.ActiveCount);
        Assert.Equal(2, particles.ActiveReadOnly[0].Life);
        Assert.Equal(0, Get(center, 63, 10));
        Assert.Equal(0, particles.Stats.DepositedThisTick);
        Assert.Equal(0, particles.Stats.KilledByLifetimeThisTick);
    }

    private static MaterialPropsTable CreateMaterials()
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder],
            [0, 255, 120],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, EngineConstants.ParticleMaxLifetimeTicks]);
    }

    private static TestChunkSource CreateNeighborhood(ChunkCoord centerCoord, out Chunk center)
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

    private static void FillAll(ReadOnlySpan<Chunk> chunks, ushort material)
    {
        for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
        {
            chunks[chunkIndex].Material.AsSpan().Fill(material);
        }
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private sealed class TestChunkSource : IChunkSource
    {
        private readonly Dictionary<ChunkCoord, Chunk> _byCoord;
        private readonly Chunk[] _resident;

        public TestChunkSource(params Chunk[] chunks)
        {
            _resident = chunks;
            _byCoord = new Dictionary<ChunkCoord, Chunk>(chunks.Length);
            foreach (Chunk chunk in chunks)
            {
                _byCoord.Add(chunk.Coord, chunk);
            }
        }

        public ReadOnlySpan<Chunk> ResidentChunks => _resident;

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
