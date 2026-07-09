using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Simulation 节点 7 的 reaction seam 与 lifetime seam 测试。
/// 不变式：reaction seam 与 lifetime seam 不交叉污染。
/// </summary>
public sealed class SimulationReactionLifetimeTests
{
    private const ushort Solid = 1;
    private const ushort Fire = 2;
    private const ushort InertFire = 3;
    private const ushort Sand = 4;

    /// <summary>
    /// 验证只有 ReactionCount 非零的材质会在 movement 后对 von Neumann 邻居调用反应执行器。
    /// </summary>
    [Fact]
    public void StepCaInvokesReactionExecutorOnlyForReactiveMaterial()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, Fire);
        Set(center, 11, 10, Solid);
        CountingReactionExecutor reactions = new(returnValue: false);
        SimulationKernel kernel = new(source, CreateMaterials(), reactionExecutor: reactions);

        kernel.StepCa();

        Assert.Equal(1, reactions.Count);
        Assert.Equal((10, 10, Fire, 11, 10, Solid, kernel.CurrentParity), reactions.Last);
    }

    /// <summary>
    /// 验证 ReactionCount 为 0 的材质不会触发 reaction seam。
    /// </summary>
    [Fact]
    public void StepCaSkipsReactionExecutorForMaterialWithoutReactions()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, InertFire);
        Set(center, 11, 10, Solid);
        CountingReactionExecutor reactions = new(returnValue: false);
        SimulationKernel kernel = new(source, CreateMaterials(), reactionExecutor: reactions);

        kernel.StepCa();

        Assert.Equal(0, reactions.Count);
    }

    /// <summary>
    /// 验证 lifetime 每个被处理 tick 递减一次，未归零时不调用 sink。
    /// </summary>
    [Fact]
    public void StepCaDecrementsLifetimeWithoutCallingSinkBeforeExpiry()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, InertFire);
        SetLifetime(center, 10, 10, 2);
        CountingLifetimeSink sink = new();
        SimulationKernel kernel = new(source, CreateMaterials(), lifetimeSink: sink);

        kernel.StepCa();

        Assert.Equal(1, GetLifetime(center, 10, 10));
        Assert.Equal(0, sink.Count);
    }

    /// <summary>
    /// 验证 lifetime 递减到 0 时调用 sink，并传递归零 cell 的世界坐标和材质。
    /// </summary>
    [Fact]
    public void StepCaCallsLifetimeSinkWhenLifetimeExpires()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, 10, InertFire);
        SetLifetime(center, 10, 10, 1);
        CountingLifetimeSink sink = new();
        SimulationKernel kernel = new(source, CreateMaterials(), lifetimeSink: sink);

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(0, GetLifetime(center, 10, 10));
        Assert.Equal(1, sink.Count);
        Assert.Equal((10, 10, InertFire), sink.Last);
    }

    /// <summary>
    /// 验证 lifetime sink 清空当前 cell 后，同一行后续 cell 仍按自己的坐标继续处理。
    /// </summary>
    [Fact]
    public void StepCaAdvancesRowCursorAfterLifetimeExpiryClearsCell()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 62, 10, InertFire);
        SetLifetime(center, 62, 10, 1);
        Set(center, 63, 10, Sand);
        Set(center, 63, 12, Solid);
        SimulationKernel kernel = new(source, CreateMaterials(), lifetimeSink: new EmptyingLifetimeSink());

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(0, Get(center, 62, 10));
        Assert.Equal(0, GetLifetime(center, 62, 10));
        Assert.Equal(0, Get(center, 63, 10));
        Assert.Equal(Sand, Get(center, 63, 11));
    }

    private static MaterialPropsTable CreateMaterials()
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Fire, CellType.Fire, CellType.Powder],
            [0, 255, 1, 1, 120],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 1, 0, 0],
            [0, 0, 0, 0, 0]);
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

    private static void SetLifetime(Chunk chunk, int lx, int ly, byte lifetime)
    {
        chunk.LifetimeBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)] = lifetime;
    }

    private static byte GetLifetime(Chunk chunk, int lx, int ly)
    {
        return chunk.LifetimeBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private sealed class CountingReactionExecutor(bool returnValue) : IReactionExecutor
    {
        public int Count { get; private set; }

        public (int X1, int Y1, ushort MaterialA, int X2, int Y2, ushort MaterialB, byte Parity) Last { get; private set; }

        public bool TryReact(ref NeighborWindow window, int wx1, int wy1, ushort materialA, int wx2, int wy2, ushort materialB, byte parityBit, byte randomByte)
        {
            Count++;
            Last = (wx1, wy1, materialA, wx2, wy2, materialB, parityBit);
            return returnValue;
        }
    }

    private sealed class CountingLifetimeSink : ILifetimeSink
    {
        public int Count { get; private set; }

        public (int X, int Y, ushort Material) Last { get; private set; }

        public void OnExpired(ref NeighborWindow window, int wx, int wy, ushort material, byte parityBit)
        {
            Count++;
            Last = (wx, wy, material);
        }
    }

    private sealed class EmptyingLifetimeSink : ILifetimeSink
    {
        public void OnExpired(ref NeighborWindow window, int wx, int wy, ushort material, byte parityBit)
        {
            window.SetMaterial(wx, wy, 0);
            window.SetFlags(wx, wy, 0);
            window.SetLifetime(wx, wy, 0);
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
