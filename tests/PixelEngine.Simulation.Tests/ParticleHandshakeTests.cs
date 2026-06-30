using PixelEngine.Core.Threading;
using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Plan 05 节点 2 的自由粒子积分与 cell↔particle handshake 测试。
/// </summary>
public sealed class ParticleHandshakeTests
{
    private const ushort Solid = 1;
    private const ushort Sand = 2;
    private const ushort Water = 3;

    /// <summary>
    /// 验证弹道积分更新位置/速度/寿命，且飞行阶段不写网格。
    /// </summary>
    [Fact]
    public void IntegrateAndAdvanceUpdatesBallisticsWithoutWritingGrid()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        CellGrid grid = new(source, CreateMaterials());
        ParticleSystem particles = new(capacity: 1);
        _ = particles.TrySpawn(new ParticleSpawn(5.25f, 5.5f, 1.5f, 2.25f, Sand, 3, 10));

        particles.IntegrateAndAdvance(grid);

        Particle particle = particles.ActiveReadOnly[0];
        Assert.Equal(6.75f, particle.X);
        Assert.Equal(7.75f, particle.Y);
        Assert.Equal(2.45f, particle.Vy, precision: 5);
        Assert.Equal(9, particle.Life);
        Assert.Equal(0, Get(center, 6, 7));
        Assert.Equal(DirtyRect.Empty, center.CurrentDirty);
        Assert.Equal(DirtyRect.Empty, center.WorkingDirty);
    }

    /// <summary>
    /// 验证 JobSystem 并行积分与单线程积分在同初态下逐位一致。
    /// </summary>
    [Fact]
    public void ParallelIntegrationMatchesSingleThreadIntegration()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out _);
        CellGrid grid = new(source, CreateMaterials());
        ParticleSystem single = new(capacity: 16);
        ParticleSystem parallel = new(capacity: 16);
        for (int i = 0; i < 12; i++)
        {
            ParticleSpawn spawn = new(1 + i, 2 + i, 0.25f * i, 0.5f, Sand, (byte)i, 20);
            _ = single.TrySpawn(in spawn);
            _ = parallel.TrySpawn(in spawn);
        }

        single.IntegrateAndAdvance(grid);
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        parallel.IntegrateAndAdvance(jobs, grid);

        Assert.Equal(single.ActiveCount, parallel.ActiveCount);
        for (int i = 0; i < single.ActiveCount; i++)
        {
            Assert.Equal(single.ActiveReadOnly[i], parallel.ActiveReadOnly[i]);
        }
    }

    /// <summary>
    /// 验证相位 7 抛射会生成粒子并经 SimulationKernel 清空源 cell。
    /// </summary>
    [Fact]
    public void RunEjectionPassClearsSourceCellAndSpawnsParticle()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Sand);
        MaterialPropsTable materials = CreateMaterials();
        CellGrid grid = new(source, materials);
        SimulationKernel kernel = new(source, materials);
        ParticleSystem particles = new(capacity: 4);

        Assert.True(particles.RequestEjection(new EjectionRequest(10, 10, 0, 2, 0, EjectMask.Powder)));
        particles.RunEjectionPass(kernel, grid);

        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(1, particles.ActiveCount);
        Assert.Equal(Sand, particles.ActiveReadOnly[0].Material);
        Assert.Equal(10.5f, particles.ActiveReadOnly[0].X);
        Assert.Equal(2, particles.ActiveReadOnly[0].Vx);
        Assert.Equal(new DirtyRect(8, 8, 12, 12), center.CurrentDirty);
    }

    /// <summary>
    /// 验证粒子池容量满时，抛射不会清空源 cell，避免质量丢失。
    /// </summary>
    [Fact]
    public void RunEjectionPassDoesNotClearSourceCellWhenParticlePoolIsFull()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Sand);
        MaterialPropsTable materials = CreateMaterials();
        CellGrid grid = new(source, materials);
        SimulationKernel kernel = new(source, materials);
        ParticleSystem particles = new(capacity: 1);
        _ = particles.TrySpawn(new ParticleSpawn(1, 1, 0, 0, Sand, 0, 10));

        Assert.True(particles.RequestEjection(new EjectionRequest(10, 10, 0, 2, 0, EjectMask.Powder)));
        particles.RunEjectionPass(kernel, grid);

        Assert.Equal(Sand, Get(center, 10, 10));
        Assert.Equal(1, particles.ActiveCount);
        Assert.Equal(1, particles.Stats.DroppedThisTick);
    }

    /// <summary>
    /// 验证 DDA 碰撞归类后，相位 3b 沉积写回 cell 并释放粒子。
    /// </summary>
    [Fact]
    public void ResolveDepositsWritesParticleBackToGridAfterCollision()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Solid);
        MaterialPropsTable materials = CreateMaterials();
        CellGrid grid = new(source, materials);
        SimulationKernel kernel = new(source, materials);
        ParticleSystem particles = new(capacity: 1);
        _ = particles.TrySpawn(new ParticleSpawn(10.25f, 9.25f, 0, 1, Sand, 0, 10));

        particles.IntegrateAndAdvance(grid);
        Assert.Equal(0, Get(center, 10, 9));

        particles.ResolveDeposits(kernel, grid);

        Assert.Equal(0, particles.ActiveCount);
        Assert.Equal(Sand, Get(center, 10, 9));
        Assert.Equal(Solid, Get(center, 10, 10));
        Assert.Equal(1, particles.Stats.DepositedThisTick);
        Assert.Equal(new DirtyRect(8, 7, 12, 11), center.CurrentDirty);
    }

    /// <summary>
    /// 验证相位 3b 沉积写 current dirty，后续同帧 CA 可以立即处理新沉积的 cell。
    /// </summary>
    [Fact]
    public void DepositedParticleIsVisibleToCaInSameFrame()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        MaterialPropsTable materials = CreateMaterials();
        CellGrid grid = new(source, materials);
        SimulationKernel kernel = new(source, materials);
        ParticleSystem particles = new(capacity: 1);
        _ = particles.TrySpawn(new ParticleSpawn(10.25f, 10.25f, 0, 0, Sand, 0, 10));

        particles.IntegrateAndAdvance(grid);
        particles.ResolveDeposits(kernel, grid);
        kernel.StepCa();

        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Sand, Get(center, 10, 11));
    }

    /// <summary>
    /// 验证重粒子沉积到轻材料 cell 时，被替换的轻材料仍保留为粒子，保持质量守恒。
    /// </summary>
    [Fact]
    public void ResolveDepositsDisplacesLighterCellIntoParticleWithoutLosingMass()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Water);
        MaterialPropsTable materials = CreateMaterials();
        CellGrid grid = new(source, materials);
        SimulationKernel kernel = new(source, materials);
        ParticleSystem particles = new(capacity: 1);
        _ = particles.TrySpawn(new ParticleSpawn(10.25f, 10.25f, 0, 0, Sand, 0, 10));

        particles.IntegrateAndAdvance(grid);
        particles.ResolveDeposits(kernel, grid);

        Assert.Equal(Sand, Get(center, 10, 10));
        Assert.Equal(1, particles.ActiveCount);
        Assert.Equal(Water, particles.ActiveReadOnly[0].Material);
        Assert.Equal(0, particles.ActiveReadOnly[0].Vx);
        Assert.Equal(0, particles.ActiveReadOnly[0].Vy);
        Assert.Equal(0, particles.Stats.DepositedThisTick);
    }

    private static MaterialPropsTable CreateMaterials()
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder, CellType.Liquid],
            [0, 255, 120, 40],
            [0, 0, 0, 0],
            [0, 0, 0, 0],
            [0, 0, 0, 0],
            [0, 0, 0, 0]);
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
