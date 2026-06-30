using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Plan 04 温度场测试。
/// </summary>
public sealed class TemperatureFieldTests
{
    private const ushort Empty = 0;
    private const ushort Ice = 1;
    private const ushort Water = 2;
    private const ushort Steam = 3;

    /// <summary>
    /// 验证温度场按 1/4 分辨率存储，AddHeat 后 ConductStep 可跨 chunk halo 传导。
    /// </summary>
    [Fact]
    public void ConductStepSpreadsHeatAcrossChunkBoundary()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Chunk east = source.GetRequired(new ChunkCoord(1, 0));
        Fill(center, Water);
        Fill(east, Water);
        MaterialTable materials = CreateMaterials();
        TemperatureField field = new();
        field.AddHeat(60, 8, 100);

        field.ConductStep(source, materials.Hot);

        Assert.True(field.GetTemperature(60, 8) < 100);
        Assert.True(field.GetTemperature(64, 8) > 0);
        Assert.Equal(4, field.Downscale);
        Assert.True(TemperatureField.BlockSize == 16);
        Assert.Equal(TemperatureStorageKind.Float16, field.StorageKind);
    }

    /// <summary>
    /// 验证阈值相变使用温度场读数直接改材质、打 parity 并标记 dirty。
    /// </summary>
    [Fact]
    public void ApplyPhaseTransitionsMeltsAndBoilsByThreshold()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 8, 10, Ice);
        Set(center, 12, 10, Water);
        center.SetCurrentDirty(new DirtyRect(8, 10, 12, 10));
        MaterialTable materials = CreateMaterials();
        TemperatureField field = new();
        field.AddHeat(8, 10, 20);
        field.AddHeat(12, 10, 120);

        field.ApplyPhaseTransitions(source, materials, CellFlags.Parity);

        Assert.Equal(Water, Get(center, 8, 10));
        Assert.Equal(Steam, Get(center, 12, 10));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 8, 10), CellFlags.Parity));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 12, 10), CellFlags.Parity));
        Assert.False(center.WorkingDirty.IsEmpty);
    }

    /// <summary>
    /// 验证热源可按材质热容缩放注热。
    /// </summary>
    [Fact]
    public void AddHeatWithMaterialHeatCapacityScalesDelta()
    {
        MaterialTable materials = CreateMaterials(waterHeatCapacity: 2f);
        TemperatureField field = new(storageKind: TemperatureStorageKind.Float32);

        field.AddHeat(10, 10, Water, materials.Hot, 20);

        Assert.Equal(10, field.GetTemperature(10, 10));
    }

    /// <summary>
    /// 验证 float SIMD 路径与 scalar fallback 对同一 stencil 产生一致结果。
    /// </summary>
    [Fact]
    public void FloatStorageSimdAndScalarConductStepMatch()
    {
        TestChunkSource simdSource = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk simdCenter);
        TestChunkSource scalarSource = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk scalarCenter);
        Fill(simdCenter, Water);
        Fill(scalarCenter, Water);
        MaterialTable materials = CreateMaterials();
        TemperatureField simd = new(storageKind: TemperatureStorageKind.Float32, enableSimd: true);
        TemperatureField scalar = new(storageKind: TemperatureStorageKind.Float32, enableSimd: false);
        simd.AddHeat(32, 32, 80);
        scalar.AddHeat(32, 32, 80);

        simd.ConductStep(simdSource, materials.Hot);
        scalar.ConductStep(scalarSource, materials.Hot);

        Assert.Equal(scalar.GetTemperature(32, 32), simd.GetTemperature(32, 32), 5);
        Assert.Equal(scalar.GetTemperature(28, 32), simd.GetTemperature(28, 32), 5);
        Assert.Equal(scalar.GetTemperature(36, 32), simd.GetTemperature(36, 32), 5);
    }

    /// <summary>
    /// 验证 HeatConduct=0 时概率 gate 阻止热传导。
    /// </summary>
    [Fact]
    public void ConductStepHonorsZeroHeatConductProbability()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Fill(center, Water);
        MaterialTable materials = CreateMaterials(waterHeatConduct: 0);
        TemperatureField field = new(storageKind: TemperatureStorageKind.Float32);
        field.AddHeat(32, 32, 100);

        field.ConductStep(source, materials.Hot, frameIndex: 7, worldSeed: 11);

        Assert.Equal(100, field.GetTemperature(32, 32));
        Assert.Equal(0, field.GetTemperature(28, 32));
    }

    /// <summary>
    /// 验证降频与 contact-fire-only 降级开关生效。
    /// </summary>
    [Fact]
    public void StepIntervalAndContactFireOnlyDegradeGateTemperaturePasses()
    {
        TemperatureField field = new(stepInterval: 3);

        Assert.True(field.ShouldRun(0));
        Assert.False(field.ShouldRun(1));
        Assert.True(field.ShouldRun(3));

        field.DegradeToContactFireOnly();

        Assert.False(field.ShouldRun(6));
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out _);
        MaterialTable materials = CreateMaterials();
        field.AddHeat(10, 10, 100);
        field.ConductStep(source, materials.Hot);
        Assert.Equal(100, field.GetTemperature(10, 10));
    }

    private static MaterialTable CreateMaterials(float waterHeatCapacity = 1f, byte waterHeatConduct = 255)
    {
        MaterialDef[] definitions =
        [
            Material(Empty, "empty", CellType.Empty),
            Material(Ice, "ice", CellType.Solid) with
            {
                MeltPoint = 10,
                MeltTarget = Water,
            },
            Material(Water, "water", CellType.Liquid) with
            {
                HeatCapacity = waterHeatCapacity,
                HeatConduct = waterHeatConduct,
                BoilPoint = 100,
                BoilTarget = Steam,
            },
            Material(Steam, "steam", CellType.Gas),
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
            HeatConduct = 255,
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

    private static void Fill(Chunk chunk, ushort material)
    {
        Array.Fill(chunk.Material, material);
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private static byte GetFlags(Chunk chunk, int lx, int ly)
    {
        return chunk.Flags[CellAddressing.LocalIndexFromLocal(lx, ly)];
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

        public Chunk GetRequired(ChunkCoord coord)
        {
            return _byCoord[coord];
        }
    }
}
