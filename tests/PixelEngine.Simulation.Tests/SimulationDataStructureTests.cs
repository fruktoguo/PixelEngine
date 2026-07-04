using PixelEngine.Core;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Simulation 节点 1 的寻址、chunk 与 CellGrid 数据结构测试。
/// </summary>
public sealed class SimulationDataStructureTests
{
    /// <summary>
    /// 验证世界坐标寻址对负坐标使用算术右移与 bitmask，避免 chunk 边界 off-by-one。
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(63, 63, 0, 0, 4095)]
    [InlineData(64, 0, 1, 0, 0)]
    [InlineData(-1, -1, -1, -1, 4095)]
    [InlineData(-64, -64, -1, -1, 0)]
    [InlineData(-65, 0, -2, 0, 63)]
    public void AddressingMapsWorldCoordinatesToChunkAndLocalIndex(int wx, int wy, int cx, int cy, int local)
    {
        ChunkCoord coord = CellAddressing.WorldToChunk(wx, wy);

        Assert.Equal(new ChunkCoord(cx, cy), coord);
        Assert.Equal(local, CellAddressing.LocalIndex(wx, wy));
    }

    /// <summary>
    /// 验证 dirty rectangle grow 会按 padding 扩张并钳制在 chunk 本地范围内。
    /// </summary>
    [Fact]
    public void DirtyRectUnionClampsToChunkLocalBounds()
    {
        DirtyRect rect = DirtyRect.Empty
            .Union(0, 0, EngineConstants.DirtyRectPadding)
            .Union(63, 63, EngineConstants.DirtyRectPadding);

        Assert.Equal(new DirtyRect(0, 0, 63, 63), rect);
        Assert.True(DirtyRect.Empty.IsEmpty);
        Assert.Equal(new DirtyRect(0, 0, 63, 63), DirtyRect.Full);
    }

    /// <summary>
    /// 验证 chunk 使用固定长度 SoA 数组，Reset 会清空数据并恢复休眠状态。
    /// </summary>
    [Fact]
    public void ChunkResetClearsSoaAndDirtyState()
    {
        Chunk chunk = new(new ChunkCoord(2, 3));
        int local = CellAddressing.LocalIndexFromLocal(10, 11);
        chunk.Material[local] = 7;
        chunk.Flags[local] = CellFlags.RigidOwned;
        chunk.Lifetime[local] = 9;
        chunk.Damage[local] = 5;
        chunk.MarkWorkingDirty(10, 11, EngineConstants.DirtyRectPadding);

        chunk.Reset(new ChunkCoord(-4, 5));

        Assert.Equal(EngineConstants.ChunkArea, chunk.Material.Length);
        Assert.Equal(EngineConstants.ChunkArea, chunk.Flags.Length);
        Assert.Equal(EngineConstants.ChunkArea, chunk.Lifetime.Length);
        Assert.Equal(new ChunkCoord(-4, 5), chunk.Coord);
        Assert.Equal(0, chunk.Material[local]);
        Assert.Equal(0, chunk.Flags[local]);
        Assert.Equal(0, chunk.Lifetime[local]);
        Assert.Equal(0, chunk.Damage[local]);
        Assert.Equal(DirtyRect.Empty, chunk.CurrentDirty);
        Assert.Equal(DirtyRect.Empty, chunk.WorkingDirty);
        Assert.Equal(ChunkState.Sleeping, chunk.State);

        ref ushort materialBase = ref chunk.GetMaterialBase();
        materialBase = 11;
        Assert.Equal(11, chunk.Material[0]);
    }

    /// <summary>
    /// 验证 ChunkPool 归还后会复用对象并清空旧 cell 数据。
    /// </summary>
    [Fact]
    public void ChunkPoolReusesResetChunks()
    {
        ChunkPool pool = new();
        Chunk first = pool.Rent(new ChunkCoord(0, 0));
        first.Material[0] = 42;

        pool.Return(first);
        Chunk second = pool.Rent(new ChunkCoord(1, 1));

        Assert.Same(first, second);
        Assert.Equal(new ChunkCoord(1, 1), second.Coord);
        Assert.Equal(0, second.Material[0]);
    }

    /// <summary>
    /// 验证 CellGrid 通过 IChunkSource 路由世界坐标写入、标记 dirty，并按材质表返回类型。
    /// </summary>
    [Fact]
    public void CellGridWritesMaterialMarksDirtyAndReadsCellType()
    {
        Chunk chunk = new(new ChunkCoord(1, -1));
        TestChunkSource source = new(chunk);
        MaterialPropsTable props = new(
            [CellType.Empty, CellType.Solid, CellType.Powder],
            [0, 200, 120],
            [0, 0, 1],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0]);
        CountingRigidDamageSink damageSink = new();
        CellGrid grid = new(source, props, damageSink);

        ref byte flags = ref grid.FlagsAt(64, -1);
        flags = CellFlags.Set(flags, CellFlags.RigidOwned);
        grid.LifetimeAt(64, -1) = 9;
        grid.DamageAt(64, -1) = 6;

        grid.SetMaterial(64, -1, 2);

        Assert.True(grid.TryGetMaterial(64, -1, out ushort material));
        Assert.Equal(2, material);
        Assert.Equal(CellType.Powder, grid.GetCellType(64, -1));
        Assert.Equal(0, grid.FlagsAt(64, -1));
        Assert.Equal(0, grid.LifetimeAt(64, -1));
        Assert.Equal(0, grid.DamageAt(64, -1));
        Assert.Equal(new DirtyRect(0, 61, 2, 63), chunk.WorkingDirty);
        Assert.Equal(ChunkState.Awake, chunk.State);
        Assert.Equal(1, damageSink.Count);
        Assert.Equal((64, -1), damageSink.Last);
    }

    /// <summary>
    /// 验证 3x3 邻域解析按照 slot=(dy+1)*3+(dx+1) 输出。
    /// </summary>
    [Fact]
    public void ChunkSourceResolvesNeighborhoodInStableSlotOrder()
    {
        Chunk[] chunks = new Chunk[9];
        int index = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                chunks[index++] = new Chunk(new ChunkCoord(10 + dx, 20 + dy));
            }
        }

        TestChunkSource source = new(chunks);
        Assert.True(source.ResolveNeighborhood(new ChunkCoord(10, 20), out ChunkNeighborhood neighborhood));
        Assert.Equal(new ChunkCoord(9, 19), neighborhood.Slot0.Coord);
        Assert.Equal(new ChunkCoord(10, 20), neighborhood.Slot4.Coord);
        Assert.Equal(new ChunkCoord(11, 21), neighborhood.Slot8.Coord);
    }

    /// <summary>
    /// 验证 NeighborWindow 可跨 3x3 邻域直接读写 Material/Flags/Lifetime，并按 slot 判断跨界 swap。
    /// </summary>
    [Fact]
    public void NeighborWindowReadsWritesAndSwapsAcrossNeighborhood()
    {
        TestChunkSource source = CreateNeighborhoodSource(new ChunkCoord(4, 5), out Chunk center, out Chunk right);
        NeighborWindow window = new(source, center.Coord);

        int centerWx = (center.Coord.X * EngineConstants.ChunkSize) + 10;
        int centerWy = (center.Coord.Y * EngineConstants.ChunkSize) + 12;
        int rightWx = ((center.Coord.X + 1) * EngineConstants.ChunkSize) + 2;
        int rightWy = centerWy;

        window.SetMaterial(centerWx, centerWy, 17);
        window.SetFlags(centerWx, centerWy, CellFlags.Burning);
        window.SetLifetime(centerWx, centerWy, 3);
        window.SetDamage(centerWx, centerWy, 9);
        window.SetMaterial(rightWx, rightWy, 29);
        window.SetFlags(rightWx, rightWy, CellFlags.FreeFalling);
        window.SetLifetime(rightWx, rightWy, 8);
        window.SetDamage(rightWx, rightWy, 11);

        Assert.Equal(4, window.SlotOf(centerWx, centerWy));
        Assert.Equal(5, window.SlotOf(rightWx, rightWy));
        Assert.Equal(17, window.GetMaterial(centerWx, centerWy));
        Assert.Equal(CellFlags.Burning, window.GetFlags(centerWx, centerWy));
        Assert.Equal(3, window.GetLifetime(centerWx, centerWy));

        bool crossedSlot = window.Swap(centerWx, centerWy, rightWx, rightWy);

        Assert.True(crossedSlot);
        Assert.Equal(29, window.GetMaterial(centerWx, centerWy));
        Assert.Equal(CellFlags.FreeFalling, window.GetFlags(centerWx, centerWy));
        Assert.Equal(8, window.GetLifetime(centerWx, centerWy));
        Assert.Equal(17, window.GetMaterial(rightWx, rightWy));
        Assert.Equal(CellFlags.Burning, window.GetFlags(rightWx, rightWy));
        Assert.Equal(3, window.GetLifetime(rightWx, rightWy));
        Assert.Equal(0, window.GetDamage(centerWx, centerWy));
        Assert.Equal(0, window.GetDamage(rightWx, rightWy));
        Assert.Same(right, source.GetRequired(new ChunkCoord(center.Coord.X + 1, center.Coord.Y)));
    }

    /// <summary>
    /// 验证结构破坏只累加普通 solid，RigidOwned cell 转交刚体 damage sink，Indestructible 免疫。
    /// </summary>
    [Fact]
    public void ApplyStructuralDamageDestroysSolidAndRoutesRigidOwned()
    {
        Chunk chunk = new(new ChunkCoord(0, 0));
        TestChunkSource source = new(chunk);
        MaterialTable materials = new(
        [
            Material(0, "empty", CellType.Empty),
            Material(1, "stone", CellType.Solid) with { Integrity = 5, DestroyedTarget = 2 },
            Material(2, "gravel", CellType.Powder),
            Material(3, "boundary_stone", CellType.Solid) with { PropertyFlags = MaterialProperty.Indestructible },
        ]);
        CountingRigidDamageSink damageSink = new();
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot), rigidDamageSink: damageSink);
        int solidLocal = CellAddressing.LocalIndexFromLocal(10, 10);
        int rigidLocal = CellAddressing.LocalIndexFromLocal(11, 10);
        int immuneLocal = CellAddressing.LocalIndexFromLocal(12, 10);
        chunk.Material[solidLocal] = 1;
        chunk.Material[rigidLocal] = 1;
        chunk.Material[immuneLocal] = 3;
        chunk.Flags[rigidLocal] = CellFlags.RigidOwned;
        chunk.Damage[rigidLocal] = 12;
        chunk.Damage[immuneLocal] = 7;

        bool rigidDestroyed = kernel.ApplyStructuralDamage(11, 10, 9);
        bool solidDestroyed = kernel.ApplyStructuralDamage(10, 10, 9);
        bool immuneDestroyed = kernel.ApplyStructuralDamage(12, 10, 255);

        Assert.False(rigidDestroyed);
        Assert.True(solidDestroyed);
        Assert.False(immuneDestroyed);
        Assert.Equal(1, damageSink.Count);
        Assert.Equal((11, 10), damageSink.Last);
        Assert.Equal(0, chunk.Damage[rigidLocal]);
        Assert.Equal(2, chunk.Material[solidLocal]);
        Assert.Equal(0, chunk.Damage[solidLocal]);
        Assert.Equal(3, chunk.Material[immuneLocal]);
        Assert.Equal(0, chunk.Damage[immuneLocal]);
    }

    /// <summary>
    /// 验证 NeighborWindow 构造阶段只解析一次邻域，不做托管堆分配。
    /// </summary>
    [Fact]
    public void NeighborWindowConstructionAndAccessDoNotAllocate()
    {
        TestChunkSource source = CreateNeighborhoodSource(new ChunkCoord(0, 0), out Chunk center, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();

        NeighborWindow window = new(source, center.Coord);
        window.SetMaterial(0, 0, 5);
        ushort material = window.GetMaterial(0, 0);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(5, material);
        Assert.Equal(0, allocated);
    }

    private sealed class CountingRigidDamageSink : IRigidDamageSink
    {
        public int Count { get; private set; }

        public (int X, int Y) Last { get; private set; }

        public void OnOwnedCellDamaged(int wx, int wy)
        {
            Count++;
            Last = (wx, wy);
        }
    }

    private static MaterialDef Material(ushort id, string name, CellType type)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = type == CellType.Empty ? (byte)0 : (byte)200,
            HeatCapacity = 1,
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

    private static TestChunkSource CreateNeighborhoodSource(ChunkCoord centerCoord, out Chunk center, out Chunk right)
    {
        Chunk[] chunks = new Chunk[9];
        int index = 0;
        right = null!;
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
                else if (dx == 1 && dy == 0)
                {
                    right = chunk;
                }
            }
        }

        return new TestChunkSource(chunks);
    }
}
