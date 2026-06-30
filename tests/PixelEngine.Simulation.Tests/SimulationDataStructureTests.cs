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
        chunk.MarkWorkingDirty(10, 11, EngineConstants.DirtyRectPadding);

        chunk.Reset(new ChunkCoord(-4, 5));

        Assert.Equal(EngineConstants.ChunkArea, chunk.Material.Length);
        Assert.Equal(EngineConstants.ChunkArea, chunk.Flags.Length);
        Assert.Equal(EngineConstants.ChunkArea, chunk.Lifetime.Length);
        Assert.Equal(new ChunkCoord(-4, 5), chunk.Coord);
        Assert.Equal(0, chunk.Material[local]);
        Assert.Equal(0, chunk.Flags[local]);
        Assert.Equal(0, chunk.Lifetime[local]);
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

        grid.SetMaterial(64, -1, 2);

        Assert.True(grid.TryGetMaterial(64, -1, out ushort material));
        Assert.Equal(2, material);
        Assert.Equal(CellType.Powder, grid.GetCellType(64, -1));
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
        Chunk?[] neighborhood = new Chunk?[9];

        Assert.True(source.ResolveNeighborhood(new ChunkCoord(10, 20), neighborhood));
        Assert.Equal(new ChunkCoord(9, 19), neighborhood[0]!.Coord);
        Assert.Equal(new ChunkCoord(10, 20), neighborhood[4]!.Coord);
        Assert.Equal(new ChunkCoord(11, 21), neighborhood[8]!.Coord);
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

        public bool ResolveNeighborhood(ChunkCoord center, Span<Chunk?> neighborhood)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(neighborhood.Length, 9);

            bool allResident = true;
            int index = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    ChunkCoord coord = new(center.X + dx, center.Y + dy);
                    if (_byCoord.TryGetValue(coord, out Chunk? chunk))
                    {
                        neighborhood[index] = chunk;
                    }
                    else
                    {
                        neighborhood[index] = null;
                        allResident = false;
                    }

                    index++;
                }
            }

            return allResident;
        }
    }
}
