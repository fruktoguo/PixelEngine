using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// ScriptSimulationContext facade 的真实后端验收。
/// </summary>
public sealed class ScriptSimulationContextTests
{
    /// <summary>
    /// 验证材质、cell 与固体采样 facade 直接读取真实 Simulation 后端。
    /// </summary>
    [Fact]
    public void FacadesReadMaterialCellsAndSolidsFromSimulationBackends()
    {
        Fixture fixture = Fixture.Create();
        fixture.Grid.SetMaterial(4, 4, 2);
        fixture.Grid.FlagsAt(4, 4) = 7;
        fixture.Grid.LifetimeAt(4, 4) = 9;

        Assert.True(fixture.Context.Materials.TryResolve("stone", out MaterialId stone));
        Assert.Equal((ushort)2, stone.Value);
        MaterialInfo info = fixture.Context.Materials.GetInfo(stone);
        Assert.Equal("stone", info.Name);
        Assert.True(info.IsSolid);
        Assert.Equal(stone, fixture.Context.Cells.GetMaterial(4, 4));
        Assert.Equal(new CellView(stone, 7, 9), fixture.Context.Cells.Sample(4, 4));
        Assert.True(fixture.Context.Cells.IsSolid(4, 4));
        Assert.True(fixture.Context.Solids.SampleSolidAabb(3.5f, 3.5f, 2, 2));

        Assert.True(fixture.Context.Solids.Raycast(0, 4, 1, 0, 8, out RaycastHit hit));
        Assert.Equal(4, hit.X);
        Assert.Equal(4, hit.Y);
        Assert.Equal(stone, hit.Material);
    }

    /// <summary>
    /// 验证脚本 cell 写命令延迟到 flush 后写入 working dirty，供下一次 CA 可见。
    /// </summary>
    [Fact]
    public void CellCommandsFlushIntoWorkingDirtyWithoutImmediateMutation()
    {
        Fixture fixture = Fixture.Create();
        MaterialId sand = fixture.Context.Materials.Resolve("sand");

        fixture.Context.Cells.SetCell(2, 2, sand);
        Assert.Equal((ushort)0, fixture.Grid.GetMaterial(2, 2));
        Assert.True(fixture.Chunk.WorkingDirty.IsEmpty);

        int flushed = fixture.Context.FlushCellCommands();

        Assert.Equal(1, flushed);
        Assert.Equal(sand.Value, fixture.Grid.GetMaterial(2, 2));
        Assert.True(fixture.Chunk.CurrentDirty.IsEmpty);
        Assert.False(fixture.Chunk.WorkingDirty.IsEmpty);

        fixture.Kernel.SwapDirtyRects();
        Assert.False(fixture.Chunk.CurrentDirty.IsEmpty);
        Assert.True(fixture.Chunk.WorkingDirty.IsEmpty);
    }

    /// <summary>
    /// 验证脚本粒子命令延迟到粒子 flush 后进入真实 ParticleSystem。
    /// </summary>
    [Fact]
    public void ParticleCommandsFlushIntoParticleSystem()
    {
        Fixture fixture = Fixture.Create();
        MaterialId sand = fixture.Context.Materials.Resolve("sand");

        fixture.Context.Particles.Spawn(new ParticleSpawnDesc(1, 2, 3, 4, sand, 5));
        fixture.Context.Particles.Burst(8, 9, sand, count: 3, speed: 6);
        Assert.Equal(0, fixture.Particles.ActiveCount);

        int flushed = fixture.Context.FlushParticleCommands();

        Assert.Equal(2, flushed);
        Assert.Equal(4, fixture.Particles.ActiveCount);
        Particle first = fixture.Particles.ActiveReadOnly[0];
        Assert.Equal(1, first.X);
        Assert.Equal(2, first.Y);
        Assert.Equal(3, first.Vx);
        Assert.Equal(4, first.Vy);
        Assert.Equal(sand.Value, first.Material);
        Assert.Equal(5, first.Life);
    }

    private sealed class Fixture
    {
        private Fixture(
            Chunk chunk,
            CellGrid grid,
            SimulationKernel kernel,
            ParticleSystem particles,
            ScriptSimulationContext context)
        {
            Chunk = chunk;
            Grid = grid;
            Kernel = kernel;
            Particles = particles;
            Context = context;
        }

        public Chunk Chunk { get; }

        public CellGrid Grid { get; }

        public SimulationKernel Kernel { get; }

        public ParticleSystem Particles { get; }

        public ScriptSimulationContext Context { get; }

        public static Fixture Create()
        {
            MaterialTable materials = Materials(
                ("empty", CellType.Empty),
                ("sand", CellType.Powder),
                ("stone", CellType.Solid));
            Chunk chunk = new(new ChunkCoord(0, 0));
            TestChunkSource chunks = new(chunk);
            MaterialPropsTable props = new(materials.Hot);
            CellGrid grid = new(chunks, props);
            SimulationKernel kernel = new(chunks, props);
            ParticleSystem particles = new(capacity: 16);
            ScriptSimulationContext context = new(new ScriptScene(), grid, kernel, particles, materials);
            return new Fixture(chunk, grid, kernel, particles, context);
        }
    }

    private static MaterialTable Materials(params (string Name, CellType Type)[] definitions)
    {
        MaterialDef[] materials = new MaterialDef[definitions.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = new MaterialDef
            {
                Id = (ushort)i,
                Name = definitions[i].Name,
                Type = definitions[i].Type,
                Density = i == 0 ? (byte)0 : (byte)100,
                HeatCapacity = 1,
                HeatConduct = 255,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            };
        }

        return new MaterialTable(materials);
    }

    private sealed class TestChunkSource(params Chunk[] chunks) : IChunkSource
    {
        public ReadOnlySpan<Chunk> ResidentChunks => chunks;

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i].Coord == coord)
                {
                    chunk = chunks[i];
                    return true;
                }
            }

            chunk = null!;
            return false;
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            if (!TryGetChunk(center, out Chunk chunk))
            {
                neighborhood = default;
                return false;
            }

            neighborhood = new ChunkNeighborhood(chunk, chunk, chunk, chunk, chunk, chunk, chunk, chunk, chunk);
            return true;
        }
    }
}
