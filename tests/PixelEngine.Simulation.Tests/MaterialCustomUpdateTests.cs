using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Plan 04 custom-update 与 burning cell 系统测试。
/// 不变式：custom-update 与燃烧 cell 行为符合 Plan 04 语义。
/// </summary>
public sealed class MaterialCustomUpdateTests
{
    private const ushort Empty = 0;
    private const ushort Fire = 1;
    private const ushort Custom = 2;
    private const ushort Ash = 3;

    /// <summary>
    /// 验证 RegisterCustomUpdate 设置 HasCustomUpdate 位，CA 只对该材质调用委托。
    /// </summary>
    [Fact]
    public void RegisteredCustomUpdateRunsOnlyForFlaggedMaterial()
    {
        // Arrange：准备输入与初始状态
        MaterialTable materials = CreateMaterials();
        int callCount = 0;
        materials.RegisterCustomUpdate("custom", (ref cell, ref window, ref context) =>
        {
            callCount++;
            context.SetCell(ref window, cell.X, cell.Y, Ash, persistentFlags: 0, lifetime: 0);
        });
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Custom);
        Set(center, 11, 10, Ash);
        center.SetCurrentDirty(new DirtyRect(10, 10, 11, 10));
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot), customUpdateExecutor: materials);

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(1, callCount);
        Assert.Equal(Ash, Get(center, 10, 10));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 10, 10), kernel.CurrentParity));
        Assert.True((materials.Hot.PropertyFlags[Custom] & MaterialProperty.HasCustomUpdate) != 0);
    }

    /// <summary>
    /// 验证 custom-update 通过 ChunkWorkContext 跨界写入时会标记目标 dirty 与边界 KeepAlive。
    /// </summary>
    [Fact]
    public void CustomUpdateCrossBoundaryWriteMarksKeepAlive()
    {
        MaterialTable materials = CreateMaterials();
        materials.RegisterCustomUpdate("custom", (ref cell, ref window, ref context) =>
        {
            context.SetCell(ref window, cell.X + 1, cell.Y, Ash, persistentFlags: 0, lifetime: 0);
        });
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Chunk east = source.GetRequired(new ChunkCoord(1, 0));
        Set(center, 63, 10, Custom);
        center.SetCurrentDirty(new DirtyRect(63, 10, 63, 10));
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot), customUpdateExecutor: materials);

        kernel.StepCa();

        Assert.Equal(Ash, Get(east, 0, 10));
        Assert.Equal(new DirtyRect(0, 8, 2, 12), east.WorkingDirty);
    }

    /// <summary>
    /// 验证 burning custom-update 每 tick 注热产烟且不依赖温度场存在。
    /// </summary>
    [Fact]
    public void BurningCustomUpdateEmitsHeatAndSmoke()
    {
        // Arrange：准备输入与初始状态
        MaterialTable materials = CreateMaterials();
        RecordingReactionSideEffects sideEffects = new();
        BurningCellSystem burning = new(materials, Ash, sideEffects);
        materials.RegisterCustomUpdate("fire", burning.UpdateBurning);
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Fire);
        SetFlags(center, 10, 10, CellFlags.Burning);
        SetLifetime(center, 10, 10, 2);
        center.SetCurrentDirty(new DirtyRect(10, 10, 10, 10));
        SimulationKernel kernel = new(
            source,
            new MaterialPropsTable(materials.Hot),
            lifetimeSink: burning,
            customUpdateExecutor: materials);

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(1, sideEffects.HeatCount);
        Assert.Equal((10, 10, Fire, (byte)77), sideEffects.FirstHeat);
        Assert.Equal(1, sideEffects.SmokeCount);
        Assert.Equal((10, 10, Fire, (byte)4), sideEffects.FirstSmoke);
        Assert.Equal(1, GetLifetime(center, 10, 10));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 10, 10), kernel.CurrentParity));
    }

    /// <summary>
    /// 验证 burning lifetime 归零时燃尽为指定 ash/empty 材质。
    /// </summary>
    [Fact]
    public void BurningLifetimeExpiryConvertsToBurnoutMaterial()
    {
        // Arrange：准备输入与初始状态
        MaterialTable materials = CreateMaterials();
        BurningCellSystem burning = new(materials, Ash);
        materials.RegisterCustomUpdate("fire", burning.UpdateBurning);
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Fire);
        SetFlags(center, 10, 10, CellFlags.Burning);
        SetLifetime(center, 10, 10, 1);
        center.SetCurrentDirty(new DirtyRect(10, 10, 10, 10));
        SimulationKernel kernel = new(
            source,
            new MaterialPropsTable(materials.Hot),
            lifetimeSink: burning,
            customUpdateExecutor: materials);

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(Ash, Get(center, 10, 10));
        Assert.False(CellFlags.Has(GetFlags(center, 10, 10), CellFlags.Burning));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 10, 10), kernel.CurrentParity));
    }

    private static MaterialTable CreateMaterials()
    {
        MaterialDef[] definitions =
        [
            Material(Empty, "empty", CellType.Empty),
            Material(Fire, "fire", CellType.Fire) with
            {
                PropertyFlags = MaterialProperty.Fire,
                TemperatureOfFire = 77,
                GeneratesSmoke = 4,
                DefaultLifetime = 3,
            },
            Material(Custom, "custom", CellType.Solid),
            Material(Ash, "ash", CellType.Powder),
        ];

        return new MaterialTable(definitions);
    }

    private static MaterialDef Material(ushort id, string name, CellType type)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = id == Empty ? (byte)0 : (byte)100,
            HeatCapacity = 1f,
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
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
        chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static void SetFlags(Chunk chunk, int lx, int ly, byte flags)
    {
        chunk.FlagsBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)] = flags;
    }

    private static void SetLifetime(Chunk chunk, int lx, int ly, byte lifetime)
    {
        chunk.LifetimeBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)] = lifetime;
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private static byte GetFlags(Chunk chunk, int lx, int ly)
    {
        return chunk.FlagsBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private static byte GetLifetime(Chunk chunk, int lx, int ly)
    {
        return chunk.LifetimeBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private sealed class RecordingReactionSideEffects : IReactionSideEffectSink
    {
        public int HeatCount { get; private set; }

        public int SmokeCount { get; private set; }

        public (int X, int Y, ushort Material, byte Heat) FirstHeat { get; private set; }

        public (int X, int Y, ushort Material, byte Amount) FirstSmoke { get; private set; }

        public void AddHeat(int wx, int wy, ushort sourceMaterial, byte heat)
        {
            if (HeatCount == 0)
            {
                FirstHeat = (wx, wy, sourceMaterial, heat);
            }

            HeatCount++;
        }

        public bool RequestParticleEjection(in EjectionRequest request)
        {
            return true;
        }

        public void EmitSmoke(int wx, int wy, ushort sourceMaterial, byte amount)
        {
            if (SmokeCount == 0)
            {
                FirstSmoke = (wx, wy, sourceMaterial, amount);
            }

            SmokeCount++;
        }
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

        public Chunk GetRequired(ChunkCoord coord)
        {
            return _byCoord[coord];
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
