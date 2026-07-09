using BenchmarkDotNet.Attributes;
using PixelEngine.Simulation;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 温度 5-point stencil 的 scalar 与 Intrinsics SIMD 传导基准。
/// </summary>
[MemoryDiagnoser]
public class TemperatureConductStencilBenchmarks
{
    private TestChunkSource _source = null!;
    private MaterialTable _materials = null!;
    private TemperatureField _float32Scalar = null!;
    private TemperatureField _float32Intrinsics = null!;
    private TemperatureField _float16Scalar = null!;

    /// <summary>
    /// 创建固定 3x3 水场，保证中心 chunk 内部行可走确定性 conduct=255 路径。
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _materials = CreateMaterials();
        Chunk[] chunks = new Chunk[9];
        int write = 0;
        for (int cy = -1; cy <= 1; cy++)
        {
            for (int cx = -1; cx <= 1; cx++)
            {
                Chunk chunk = new(new ChunkCoord(cx, cy));
                Array.Fill(chunk.Material, (ushort)1);
                chunks[write++] = chunk;
            }
        }

        _source = new TestChunkSource(chunks);
    }

    /// <summary>
    /// 每次迭代重建温度场，避免连续扩散改变负载形状。
    /// </summary>
    [IterationSetup]
    public void IterationSetup()
    {
        _float32Scalar = new TemperatureField(storageKind: TemperatureStorageKind.Float32, enableSimd: false);
        _float32Intrinsics = new TemperatureField(storageKind: TemperatureStorageKind.Float32, enableSimd: true);
        _float16Scalar = new TemperatureField(storageKind: TemperatureStorageKind.Float16, enableSimd: false);
        AddHeatPattern(_float32Scalar);
        AddHeatPattern(_float32Intrinsics);
        AddHeatPattern(_float16Scalar);
        _float32Scalar.ConductStep(_source, _materials.Hot, frameIndex: 17, worldSeed: 23);
        _float32Intrinsics.ConductStep(_source, _materials.Hot, frameIndex: 17, worldSeed: 23);
        _float16Scalar.ConductStep(_source, _materials.Hot, frameIndex: 17, worldSeed: 23);
    }

    /// <summary>
    /// 验证Float32Scalar。
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Float32Scalar()
    {
        _float32Scalar.ConductStep(_source, _materials.Hot, frameIndex: 19, worldSeed: 23);
    }

    /// <summary>
    /// 验证Float32Intrinsics。
    /// </summary>
    [Benchmark]
    public void Float32Intrinsics()
    {
        _float32Intrinsics.ConductStep(_source, _materials.Hot, frameIndex: 19, worldSeed: 23);
    }

    /// <summary>
    /// 验证Float16Scalar。
    /// </summary>
    [Benchmark]
    public void Float16Scalar()
    {
        _float16Scalar.ConductStep(_source, _materials.Hot, frameIndex: 19, worldSeed: 23);
    }

    private static void AddHeatPattern(TemperatureField field)
    {
        for (int cy = -1; cy <= 1; cy++)
        {
            for (int cx = -1; cx <= 1; cx++)
            {
                int baseX = cx << 6;
                int baseY = cy << 6;
                for (int y = 8; y < 56; y += 8)
                {
                    for (int x = 8; x < 56; x += 8)
                    {
                        field.AddHeat(baseX + x, baseY + y, (x * 3) + y);
                    }
                }
            }
        }
    }

    private static MaterialTable CreateMaterials()
    {
        return new MaterialTable(
        [
            new MaterialDef
            {
                Id = 0,
                Name = "empty",
                Type = CellType.Empty,
                HeatCapacity = 1f,
                HeatConduct = 0,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            },
            new MaterialDef
            {
                Id = 1,
                Name = "water",
                Type = CellType.Liquid,
                HeatCapacity = 1f,
                HeatConduct = byte.MaxValue,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            },
        ]);
    }

    private sealed class TestChunkSource(params Chunk[] chunks) : IChunkSource
    {
        private readonly Dictionary<ChunkCoord, Chunk> _byCoord = chunks.ToDictionary(static chunk => chunk.Coord);

        public ReadOnlySpan<Chunk> ResidentChunks => chunks;

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
