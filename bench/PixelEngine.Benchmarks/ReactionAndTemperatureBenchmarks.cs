using BenchmarkDotNet.Attributes;
using PixelEngine.Simulation;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 反应表 cache-aware 查找策略基准。
/// </summary>
[MemoryDiagnoser]
public class ReactionLookupBenchmark
{
    private readonly MaterialDef[] _materials;
    private readonly ReactionTable _table;

    /// <summary>
    /// 创建反应查找基准 fixture。
    /// </summary>
    public ReactionLookupBenchmark()
    {
        _materials = CreateMaterials(96);
        _materials[0] = _materials[0] with { ReactionStart = 999, ReactionCount = 0 };
        _materials[1] = _materials[1] with { ReactionStart = 0, ReactionCount = 4 };
        _materials[2] = _materials[2] with { ReactionStart = 4, ReactionCount = 16 };
        _materials[3] = _materials[3] with { ReactionStart = 20, ReactionCount = 40 };

        Reaction[] reactions = new Reaction[60];
        for (ushort i = 0; i < 4; i++)
        {
            reactions[i] = Reaction(1, (ushort)(10 + i), 4, 5);
        }

        for (ushort i = 0; i < 16; i++)
        {
            reactions[4 + i] = Reaction(2, (ushort)(20 + i), 4, 5);
        }

        for (ushort i = 0; i < 40; i++)
        {
            reactions[20 + i] = Reaction(3, (ushort)(40 + i), 4, 5);
        }

        _table = new ReactionTable(reactions, _materials);
    }

    /// <summary>
    /// 惰性材质早退路径。
    /// </summary>
    [Benchmark(Baseline = true)]
    public int FindInert()
    {
        return _table.Find(0, 10, in _materials[0]);
    }

    /// <summary>
    /// 小切片线性查找路径。
    /// </summary>
    [Benchmark]
    public int FindLinear()
    {
        return _table.Find(1, 13, in _materials[1]);
    }

    /// <summary>
    /// 中等切片二分查找路径。
    /// </summary>
    [Benchmark]
    public int FindBinary()
    {
        return _table.Find(2, 31, in _materials[2]);
    }

    /// <summary>
    /// 大切片 direct table 查找路径。
    /// </summary>
    [Benchmark]
    public int FindDirect()
    {
        return _table.Find(3, 72, in _materials[3]);
    }

    private static MaterialDef[] CreateMaterials(int count)
    {
        MaterialDef[] materials = new MaterialDef[count];
        for (ushort i = 0; i < materials.Length; i++)
        {
            materials[i] = new MaterialDef
            {
                Id = i,
                Name = $"mat_{i}",
                Type = i == 0 ? CellType.Empty : CellType.Solid,
                HeatCapacity = 1f,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            };
        }

        return materials;
    }

    private static Reaction Reaction(ushort inputA, ushort inputB, ushort outputA, ushort outputB)
    {
        return new Reaction
        {
            InputA = inputA,
            InputB = inputB,
            OutputA = outputA,
            OutputB = outputB,
            Probability = 255,
            Flags = ReactionFlags.None,
        };
    }
}

/// <summary>
/// 旧入口保留给已有 benchmark filter/脚本；实际实现见 <see cref="ReactionLookupBenchmark"/>。
/// </summary>
public class ReactionLookupBenchmarks : ReactionLookupBenchmark
{
}

/// <summary>
/// 反应执行与温度 pass 稳态零分配基准。
/// </summary>
[MemoryDiagnoser]
public class ReactionTemperatureAllocationBenchmarks
{
    private const ushort Empty = 0;
    private const ushort Reactive = 1;
    private const ushort Neighbor = 2;
    private const ushort Product = 3;
    private const ushort Ice = 4;
    private const ushort Water = 5;

    private readonly Chunk[] _chunks = new Chunk[9];
    private readonly TestChunkSource _source;
    private readonly Chunk _center;
    private readonly MaterialTable _materials;
    private readonly ReactionEngine _reactionEngine;
    private readonly SimulationKernel _kernel;
    private readonly TemperatureField _temperature = new(storageKind: TemperatureStorageKind.Float32);
    private uint _frameIndex;

    /// <summary>
    /// 创建反应 / 温度 allocation benchmark fixture。
    /// </summary>
    public ReactionTemperatureAllocationBenchmarks()
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
        _materials = new MaterialTable(CreateMaterials());
        ReactionTable reactions = new(
            [new Reaction
            {
                InputA = Reactive,
                InputB = Neighbor,
                OutputA = Product,
                OutputB = Product,
                Probability = 255,
            }],
            [.. CreateMaterials()]);
        _reactionEngine = new ReactionEngine(_materials, reactions);
        _kernel = new SimulationKernel(_source, new MaterialPropsTable(_materials.Hot), reactionExecutor: _reactionEngine);

        FillAll(Water);
        _temperature.AddHeat(8, 8, 10);
        _temperature.ConductStep(_source, _materials.Hot);
    }

    /// <summary>
    /// 准备惰性反应早退场景。
    /// </summary>
    [IterationSetup(Target = nameof(StepCaWithInertReactionEarlyOut))]
    public void SetupInertReaction()
    {
        ResetWorld();
        Set(_center, 10, 10, Neighbor);
        Set(_center, 11, 10, Product);
        _center.SetCurrentDirty(new DirtyRect(10, 10, 11, 10));
    }

    /// <summary>
    /// 准备命中反应表的场景。
    /// </summary>
    [IterationSetup(Target = nameof(StepCaWithReactionEngineHit))]
    public void SetupReactionHit()
    {
        ResetWorld();
        Set(_center, 10, 10, Reactive);
        Set(_center, 11, 10, Neighbor);
        _center.SetCurrentDirty(new DirtyRect(10, 10, 11, 10));
    }

    /// <summary>
    /// 准备温度传导与阈值相变场景。
    /// </summary>
    [IterationSetup(Target = nameof(TemperatureConductAndPhaseTransitions))]
    public void SetupTemperature()
    {
        ResetWorld();
        FillAll(Water);
        Set(_center, 8, 10, Ice);
        _center.SetCurrentDirty(new DirtyRect(8, 10, 8, 10));
        _temperature.AddHeat(8, 10, 20);
    }

    /// <summary>
    /// 反应早退路径：`ReactionCount==0` 不进入反应表。
    /// </summary>
    [Benchmark]
    public void StepCaWithInertReactionEarlyOut()
    {
        _kernel.StepCa();
        _kernel.SwapDirtyRects();
    }

    /// <summary>
    /// 反应命中路径：查表、概率裁决、双输出写回。
    /// </summary>
    [Benchmark]
    public void StepCaWithReactionEngineHit()
    {
        _kernel.StepCa();
        _kernel.SwapDirtyRects();
    }

    /// <summary>
    /// 温度传导与阈值相变稳态路径。
    /// </summary>
    [Benchmark]
    public void TemperatureConductAndPhaseTransitions()
    {
        _temperature.ConductStep(_source, _materials.Hot, _frameIndex++, worldSeed: 123);
        _temperature.ApplyPhaseTransitions(_source, _materials, CellFlags.Parity);
    }

    private void ResetWorld()
    {
        foreach (Chunk chunk in _chunks)
        {
            chunk.Reset(chunk.Coord);
        }
    }

    private void FillAll(ushort material)
    {
        foreach (Chunk chunk in _chunks)
        {
            Array.Fill(chunk.Material, material);
        }
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static MaterialDef[] CreateMaterials()
    {
        return
        [
            Material(Empty, "empty", CellType.Empty),
            Material(Reactive, "reactive", CellType.Solid) with { ReactionStart = 0, ReactionCount = 1 },
            Material(Neighbor, "neighbor", CellType.Solid),
            Material(Product, "product", CellType.Solid),
            Material(Ice, "ice", CellType.Solid) with { MeltPoint = 10, MeltTarget = Water },
            Material(Water, "water", CellType.Liquid),
        ];
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
