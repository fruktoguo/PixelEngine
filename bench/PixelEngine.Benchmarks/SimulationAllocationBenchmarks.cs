using BenchmarkDotNet.Attributes;
using PixelEngine.Simulation;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 热路径：SimulationKernel 单帧在不同脏区比例下的分配。
/// </summary>
[MemoryDiagnoser]
public class SimulationAllocationBenchmarks
{
    private const ushort Stone = 1;
    private const ushort Sand = 2;
    private readonly Chunk[] _chunks = new Chunk[9];
    private readonly TestChunkSource _source;
    private readonly SimulationKernel _kernel;
    private readonly Chunk _center;

    /// <summary>
    /// 创建 Simulation allocation benchmark fixture。
    /// </summary>
    public SimulationAllocationBenchmarks()
    {
        int index = 0;
        _center = null!;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Chunk chunk = new(new ChunkCoord(dx, dy));
                _chunks[index++] = chunk;
                if (dx == 0 && dy == 0)
                {
                    _center = chunk;
                }
            }
        }

        _source = new TestChunkSource(_chunks);
        _kernel = new SimulationKernel(_source, CreateMaterials());
    }

    /// <summary>
    /// 准备单 cell 结构伤害累计场景。
    /// </summary>
    [IterationSetup(Target = nameof(ApplyStructuralDamageAccumulates))]
    public void SetupApplyStructuralDamage()
    {
        ResetWorld();
        Set(_center, 18, 18, Stone);
    }

    /// <summary>
    /// 准备圆形范围结构伤害累计场景。
    /// </summary>
    [IterationSetup(Target = nameof(DamageCircleAccumulates))]
    public void SetupDamageCircle()
    {
        ResetWorld();
        FillRect(_center, Stone, 12, 12, 25, 25);
    }

    /// <summary>
    /// 验证Step Ca And Swap Single Powder。
    /// </summary>
    [Benchmark]
    public void StepCaAndSwapSinglePowder()
    {
        ResetWorld();
        Chunk center = _source.GetRequired(new ChunkCoord(0, 0));
        center.MaterialBuffer[CellAddressing.LocalIndexFromLocal(10, 10)] = Sand;
        center.SetCurrentDirty(new DirtyRect(10, 10, 10, 10));

        _kernel.StepCa();
        _kernel.SwapDirtyRects();
    }

    /// <summary>
    /// 准备批量矩形写入分配基准。
    /// </summary>
    [IterationSetup(Target = nameof(EditRectAtInputPhaseSteadyState))]
    public void SetupEditRectAtInputPhase()
    {
        ResetWorld();
    }

    /// <summary>
    /// 准备批量矩形清空分配基准。
    /// </summary>
    [IterationSetup(Target = nameof(ClearRectAtInputPhaseSteadyState))]
    public void SetupClearRectAtInputPhase()
    {
        ResetWorld();
        _ = _kernel.EditRectAtInputPhase(0, 0, 65, 65, Sand, persistentFlags: 0);
    }

    /// <summary>
    /// 验证跨 chunk 批量写入的稳态分配。
    /// </summary>
    [Benchmark]
    public int EditRectAtInputPhaseSteadyState()
    {
        return _kernel.EditRectAtInputPhase(0, 0, 65, 65, Sand, persistentFlags: 0);
    }

    /// <summary>
    /// 验证跨 chunk 批量清空的稳态分配。
    /// </summary>
    [Benchmark]
    public int ClearRectAtInputPhaseSteadyState()
    {
        return _kernel.ClearRectAtInputPhase(0, 0, 65, 65);
    }

    /// <summary>
    /// 验证Apply Structural Damage Accumulates。
    /// </summary>
    [Benchmark]
    public bool ApplyStructuralDamageAccumulates()
    {
        return _kernel.ApplyStructuralDamage(18, 18, damage: 16);
    }

    /// <summary>
    /// 圆形结构伤害累计的稳态分配基准。
    /// </summary>
    [Benchmark]
    public int DamageCircleAccumulates()
    {
        return _kernel.DamageCircle(18, 18, radius: 5, damage: 16, falloff: true);
    }

    private void ResetWorld()
    {
        for (int i = 0; i < _chunks.Length; i++)
        {
            Chunk chunk = _chunks[i];
            chunk.Reset(chunk.Coord);
        }
    }

    private static MaterialPropsTable CreateMaterials()
    {
        MaterialDef[] materials =
        [
            new()
            {
                Id = 0,
                Name = "empty",
                Type = CellType.Empty,
                TextureId = -1,
                HeatCapacity = 1,
            },
            new()
            {
                Id = Stone,
                Name = "stone",
                Type = CellType.Solid,
                Density = 255,
                TextureId = -1,
                HeatCapacity = 1,
                Integrity = 200,
                DestroyedTarget = Sand,
            },
            new()
            {
                Id = Sand,
                Name = "sand",
                Type = CellType.Powder,
                Density = 120,
                TextureId = -1,
                HeatCapacity = 1,
            },
        ];
        return new MaterialPropsTable(MaterialHotTable.FromDefinitions(materials));
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static void FillRect(Chunk chunk, ushort material, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Set(chunk, x, y, material);
            }
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
