using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Plan 04 反应执行器行为测试。
/// </summary>
public sealed class ReactionEngineTests
{
    private const ushort Empty = 0;
    private const ushort Fire = 1;
    private const ushort Wood = 2;
    private const ushort Ash = 3;
    private const ushort Smoke = 4;

    /// <summary>
    /// 验证命中反应后会写双输出、两格 parity 与产物默认 lifetime。
    /// </summary>
    [Fact]
    public void TryReactAppliesOutputsParityAndDefaultLifetime()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Fire);
        Set(center, 11, 10, Wood);
        NeighborWindow window = new(source, center.Coord);
        ReactionEngine engine = CreateSetup(Reaction(Fire, Wood, Fire, Ash, 255)).Engine;

        bool reacted = engine.TryReact(ref window, 10, 10, Fire, 11, 10, Wood, CellFlags.Parity, randomByte: 254);

        Assert.True(reacted);
        Assert.Equal(Fire, Get(center, 10, 10));
        Assert.Equal(Ash, Get(center, 11, 10));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 10, 10), CellFlags.Parity));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 11, 10), CellFlags.Parity));
        Assert.Equal(5, GetLifetime(center, 10, 10));
        Assert.Equal(9, GetLifetime(center, 11, 10));
    }

    /// <summary>
    /// 验证概率 0 永不触发、255 必定触发，中间概率按传入随机 byte 裁决。
    /// </summary>
    [Fact]
    public void TryReactUsesProbabilityByte()
    {
        ReactionEngine never = CreateSetup(Reaction(Fire, Wood, Smoke, Ash, 0)).Engine;
        ReactionEngine half = CreateSetup(Reaction(Fire, Wood, Smoke, Ash, 128)).Engine;
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        NeighborWindow window = new(source, center.Coord);

        Set(center, 10, 10, Fire);
        Set(center, 11, 10, Wood);
        Assert.False(never.TryReact(ref window, 10, 10, Fire, 11, 10, Wood, CellFlags.Parity, randomByte: 0));
        Assert.False(half.TryReact(ref window, 10, 10, Fire, 11, 10, Wood, CellFlags.Parity, randomByte: 128));
        Assert.True(half.TryReact(ref window, 10, 10, Fire, 11, 10, Wood, CellFlags.Parity, randomByte: 127));
    }

    /// <summary>
    /// 验证定向反应只在 InputA owner 切片命中，反向扫描不会触发。
    /// </summary>
    [Fact]
    public void DirectionalReactionOnlyTriggersFromInputAOwner()
    {
        ReactionEngine engine = CreateSetup(Reaction(Fire, Wood, Smoke, Ash, 255, ReactionFlags.Directional), directionalOnly: true).Engine;
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        NeighborWindow window = new(source, center.Coord);

        Set(center, 10, 10, Wood);
        Set(center, 11, 10, Fire);
        Assert.False(engine.TryReact(ref window, 10, 10, Wood, 11, 10, Fire, CellFlags.Parity, randomByte: 0));

        Set(center, 10, 10, Fire);
        Set(center, 11, 10, Wood);
        Assert.True(engine.TryReact(ref window, 10, 10, Fire, 11, 10, Wood, CellFlags.Parity, randomByte: 0));
    }

    /// <summary>
    /// 验证接触式火传播可作为普通反应工作，Fire 产物会被点燃为 burning cell。
    /// </summary>
    [Fact]
    public void ContactFireReactionMarksFireOutputBurning()
    {
        ReactionSetup setup = CreateSetup(Reaction(Fire, Wood, Fire, Fire, 255));
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Fire);
        Set(center, 11, 10, Wood);
        NeighborWindow window = new(source, center.Coord);

        bool reacted = setup.Engine.TryReact(ref window, 10, 10, Fire, 11, 10, Wood, CellFlags.Parity, randomByte: 0);

        Assert.True(reacted);
        Assert.Equal(Fire, Get(center, 11, 10));
        Assert.True(CellFlags.Has(GetFlags(center, 11, 10), CellFlags.Burning));
        Assert.Equal(5, GetLifetime(center, 11, 10));
    }

    /// <summary>
    /// 验证 StepCa 成功反应后由 ChunkUpdater 标记 dirty 与跨界 KeepAlive。
    /// </summary>
    [Fact]
    public void StepCaSuccessfulBoundaryReactionMarksDirtyAndKeepAlive()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Chunk east = source.GetRequired(new ChunkCoord(1, 0));
        Set(center, 63, 10, Fire);
        Set(east, 0, 10, Wood);
        center.SetCurrentDirty(DirtyRect.Full);
        ReactionSetup setup = CreateSetup(Reaction(Fire, Wood, Smoke, Ash, 255));
        MaterialTable materials = setup.Materials;
        ReactionEngine reactions = setup.Engine;
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot), reactionExecutor: reactions);

        kernel.StepCa();

        Assert.Equal(Smoke, Get(center, 63, 10));
        Assert.Equal(Ash, Get(east, 0, 10));
        Assert.Equal(new DirtyRect(61, 8, 63, 12), center.WorkingDirty);
        Assert.Equal(1, kernel.Diagnostics.BoundaryWakeCount);
        BoundaryWakeRecord record = kernel.Diagnostics.BoundaryWakeRecords[0];
        Assert.Equal(new ChunkCoord(1, 0), record.TargetCoord);
        Assert.Equal(KeepAliveDirections.SlotWest, record.IncomingSlot);
        Assert.Equal(new DirtyRect(0, 8, 2, 12), record.Rect);
    }

    /// <summary>
    /// 验证 EmitHeat、SpawnParticle 与 GeneratesSmoke 副作用全部投递给 sink。
    /// </summary>
    [Fact]
    public void TryReactEmitsSideEffectsWhenConfigured()
    {
        RecordingReactionSideEffects sideEffects = new();
        ReactionSetup setup = CreateSetup(
            Reaction(Fire, Wood, Smoke, Ash, 255, ReactionFlags.EmitHeat | ReactionFlags.SpawnParticle),
            sideEffects: sideEffects,
            smokeSideEffects: true);
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Fire);
        Set(center, 11, 10, Wood);
        NeighborWindow window = new(source, center.Coord);

        bool reacted = setup.Engine.TryReact(ref window, 10, 10, Fire, 11, 10, Wood, CellFlags.Parity, randomByte: 0);

        Assert.True(reacted);
        Assert.Equal(2, sideEffects.HeatCount);
        Assert.Equal((10, 10, Smoke, (byte)44), sideEffects.FirstHeat);
        Assert.Equal(2, sideEffects.EjectionCount);
        Assert.Equal((10, 10, EjectMask.Gas), sideEffects.FirstEjection);
        Assert.Equal(1, sideEffects.SmokeCount);
        Assert.Equal((10, 10, Smoke, (byte)6), sideEffects.FirstSmoke);
    }

    /// <summary>
    /// 验证带副作用 flag 却未配置 sink 时抛明确异常，不静默丢失副作用。
    /// </summary>
    [Fact]
    public void TryReactThrowsWhenSideEffectSinkIsMissing()
    {
        ReactionSetup setup = CreateSetup(Reaction(Fire, Wood, Smoke, Ash, 255, ReactionFlags.EmitHeat));
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Fire);
        Set(center, 11, 10, Wood);
        NeighborWindow window = new(source, center.Coord);

        InvalidOperationException? exception = null;
        try
        {
            _ = setup.Engine.TryReact(ref window, 10, 10, Fire, 11, 10, Wood, CellFlags.Parity, randomByte: 0);
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        Assert.NotNull(exception);
        Assert.Contains("IReactionSideEffectSink", exception.Message, StringComparison.Ordinal);
    }

    private static ReactionSetup CreateSetup(
        Reaction reaction,
        bool directionalOnly = false,
        IReactionSideEffectSink? sideEffects = null,
        bool smokeSideEffects = false)
    {
        bool isDirectional = directionalOnly || (reaction.Flags & ReactionFlags.Directional) != 0;
        Reaction[] packed = isDirectional
            ? [reaction]
            :
            [
                reaction,
                reaction with
                {
                    InputA = reaction.InputB,
                    InputB = reaction.InputA,
                    OutputA = reaction.OutputB,
                    OutputB = reaction.OutputA,
                },
            ];
        MaterialTable materials = CreateMaterials(reaction, isDirectional, smokeSideEffects);
        ReactionTable table = new(packed, GetDefinitions(materials));
        return new ReactionSetup(materials, new ReactionEngine(materials, table, sideEffects));
    }

    private static MaterialTable CreateMaterials(Reaction reaction, bool directionalOnly, bool smokeSideEffects)
    {
        MaterialDef[] definitions =
        [
            Material(Empty, "empty", CellType.Empty, reactionStart: 0, reactionCount: 0, lifetime: 0),
            Material(Fire, "fire", CellType.Fire, reactionStart: reaction.InputA == Fire ? 0 : 1, reactionCount: 1, lifetime: 5),
            Material(Wood, "wood", CellType.Solid, reactionStart: reaction.InputA == Wood ? 0 : 1, reactionCount: directionalOnly ? 0 : 1, lifetime: 7),
            Material(Ash, "ash", CellType.Powder, reactionStart: 0, reactionCount: 0, lifetime: 9),
            smokeSideEffects
                ? Material(Smoke, "smoke", CellType.Gas, reactionStart: 0, reactionCount: 0, lifetime: 11) with
                {
                    GeneratesSmoke = 6,
                    TemperatureOfFire = 44,
                }
                : Material(Smoke, "smoke", CellType.Gas, reactionStart: 0, reactionCount: 0, lifetime: 11),
        ];

        return new MaterialTable(definitions);
    }

    private static MaterialDef[] GetDefinitions(MaterialTable materials)
    {
        MaterialDef[] definitions = new MaterialDef[materials.Count];
        for (ushort i = 0; i < definitions.Length; i++)
        {
            definitions[i] = materials.Get(i);
        }

        return definitions;
    }

    private static MaterialDef Material(ushort id, string name, CellType type, int reactionStart, int reactionCount, ushort lifetime)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = id == Empty ? (byte)0 : (byte)100,
            HeatCapacity = 1f,
            DefaultLifetime = lifetime,
            ReactionStart = reactionStart,
            ReactionCount = checked((byte)reactionCount),
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }

    private readonly record struct ReactionSetup(MaterialTable Materials, ReactionEngine Engine);

    private sealed class RecordingReactionSideEffects : IReactionSideEffectSink
    {
        public int HeatCount { get; private set; }

        public int EjectionCount { get; private set; }

        public int SmokeCount { get; private set; }

        public (int X, int Y, ushort Material, byte Heat) FirstHeat { get; private set; }

        public (int X, int Y, EjectMask Mask) FirstEjection { get; private set; }

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
            if (EjectionCount == 0)
            {
                FirstEjection = (request.CenterX, request.CenterY, request.Mask);
            }

            EjectionCount++;
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

    private static Reaction Reaction(
        ushort inputA,
        ushort inputB,
        ushort outputA,
        ushort outputB,
        byte probability,
        ReactionFlags flags = ReactionFlags.None)
    {
        return new Reaction
        {
            InputA = inputA,
            InputB = inputB,
            OutputA = outputA,
            OutputB = outputB,
            Probability = probability,
            Flags = flags,
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

    private static byte GetLifetime(Chunk chunk, int lx, int ly)
    {
        return chunk.Lifetime[CellAddressing.LocalIndexFromLocal(lx, ly)];
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
