using PixelEngine.Core;
using PixelEngine.Core.Threading;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Simulation 节点 8 的帧相位接口与诊断钩子测试。
/// 不变式：帧相位接口钩子按序调用、诊断计数准确。
/// </summary>
public sealed class SimulationPhaseInterfaceTests
{
    private const ushort Solid = 1;
    private const ushort Sand = 2;
    private const ushort Fire = 3;

    /// <summary>
    /// 验证相位 3 沉积直接写 current dirty，本帧 CA 不经 swap 即可看见。
    /// </summary>
    [Fact]
    public void DepositCellMarksCurrentDirtySoCurrentFrameCaProcessesIt()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.DepositCell(10, 10, Sand, CellFlags.Burning);

        // Assert：验证预期结果
        Assert.Equal(Sand, Get(center, 10, 10));
        Assert.Equal(7, GetLifetime(center, 10, 10));
        Assert.True(CellFlags.Has(GetFlags(center, 10, 10), CellFlags.Burning));
        Assert.Equal(new DirtyRect(8, 8, 12, 12), center.CurrentDirty);
        Assert.Equal(DirtyRect.Empty, center.WorkingDirty);

        kernel.StepCa();

        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Sand, Get(center, 10, 10 + EngineConstants.MoveCap));
    }

    /// <summary>
    /// 验证边界 MarkDirty 会把邻接 chunk 同步标入 current dirty，供本帧重检。
    /// </summary>
    [Fact]
    public void MarkDirtyAtBoundaryMarksNeighborCurrentDirty()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Chunk east = source.GetRequired(new ChunkCoord(1, 0));
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.MarkDirty(63, 10);

        Assert.Equal(new DirtyRect(61, 8, 63, 12), center.CurrentDirty);
        Assert.Equal(new DirtyRect(0, 8, 1, 12), east.CurrentDirty);
        Assert.Equal(1, kernel.Diagnostics.BoundaryWakeCount);
        Assert.Equal(new ChunkCoord(1, 0), kernel.Diagnostics.BoundaryWakeRecords[0].TargetCoord);
    }

    /// <summary>
    /// 验证 phase [1] 编辑写入会写 current dirty 并记录边界唤醒，供本帧 CA 安全可见。
    /// </summary>
    [Fact]
    public void EditCellAtInputPhaseMarksCurrentDirtyAndBoundaryWake()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Chunk east = source.GetRequired(new ChunkCoord(1, 0));
        MaterialTable materials = new(
        [
            new MaterialDef { Id = 0, Name = "empty", Type = CellType.Empty, HeatCapacity = 1f, TextureId = -1 },
            new MaterialDef { Id = 1, Name = "sand", Type = CellType.Powder, HeatCapacity = 1f, TextureId = -1 },
        ]);
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot));
        SimulationEditApi edit = new(kernel, materials);

        edit.PaintCell(63, 10, 1);

        // Assert：验证预期结果
        Assert.Equal(1, Get(center, 63, 10));
        Assert.Equal(new DirtyRect(61, 8, 63, 12), center.CurrentDirty);
        Assert.Equal(new DirtyRect(0, 8, 1, 12), east.CurrentDirty);
        Assert.Equal(DirtyRect.Empty, center.WorkingDirty);
        Assert.Equal(1, kernel.Diagnostics.BoundaryWakeCount);
        Assert.Equal(new ChunkCoord(1, 0), kernel.Diagnostics.BoundaryWakeRecords[0].TargetCoord);
    }

    /// <summary>
    /// 验证 phase [1] 矩形批量写入按 row-run 写 SoA，并一次性标记跨 chunk dirty。
    /// </summary>
    [Fact]
    public void EditRectAtInputPhaseWritesRowsAndMarksPaddedDirtyAcrossChunks()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateDenseSource(-1, -1, 2, 1);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        Chunk east = source.GetRequired(new ChunkCoord(1, 0));
        SimulationKernel kernel = new(source, CreateMaterials());

        int writes = kernel.EditRectAtInputPhase(62, 10, 65, 12, Sand, persistentFlags: 0);

        // Assert：验证预期结果
        Assert.Equal(12, writes);
        Assert.Equal(Sand, Get(center, 62, 10));
        Assert.Equal(Sand, Get(center, 63, 12));
        Assert.Equal(Sand, Get(east, 0, 10));
        Assert.Equal(Sand, Get(east, 1, 12));
        Assert.Equal(7, GetLifetime(east, 1, 12));
        Assert.True(CellFlags.MatchesFrame(GetFlags(center, 62, 10), kernel.CurrentParity));
        Assert.Equal(new DirtyRect(60, 8, 63, 14), center.CurrentDirty);
        Assert.Equal(new DirtyRect(0, 8, 3, 14), east.CurrentDirty);
    }

    /// <summary>
    /// 验证 phase [1] 矩形批量清空空区域不会唤醒 dirty。
    /// </summary>
    [Fact]
    public void ClearRectAtInputPhaseSkipsEmptyRowsWithoutDirty()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        SimulationKernel kernel = new(source, CreateMaterials());

        int writes = kernel.ClearRectAtInputPhase(10, 10, 14, 14);

        Assert.Equal(0, writes);
        Assert.Equal(DirtyRect.Empty, center.CurrentDirty);
    }

    /// <summary>
    /// 验证批量矩形编辑在跨 chunk 稳态调用中不创建闭包、委托或其他托管分配。
    /// </summary>
    [Fact]
    public void BatchRectEditsDoNotAllocateAfterWarmup()
    {
        TestChunkSource source = CreateDenseSource(-1, -1, 1, 1);
        SimulationKernel kernel = new(source, CreateMaterials());

        _ = kernel.EditRectAtInputPhase(0, 0, 65, 65, Sand, persistentFlags: 0);
        _ = kernel.ClearRectAtInputPhase(0, 0, 65, 65);

        long beforeEdit = GC.GetAllocatedBytesForCurrentThread();
        int editWrites = kernel.EditRectAtInputPhase(0, 0, 65, 65, Sand, persistentFlags: 0);
        long editAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeEdit;

        long beforeClear = GC.GetAllocatedBytesForCurrentThread();
        int clearWrites = kernel.ClearRectAtInputPhase(0, 0, 65, 65);
        long clearAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeClear;

        Assert.Equal(66 * 66, editWrites);
        Assert.Equal(66 * 66, clearWrites);
        Assert.Equal(0, editAllocated);
        Assert.Equal(0, clearAllocated);
    }

    /// <summary>
    /// 验证相位 7 清 cell 返回原值并写 current dirty，使下一次 CA 立即看见变化。
    /// </summary>
    [Fact]
    public void ReadAndClearCellReturnsOriginalCellAndMarksCurrentDirty()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Sand);
        Set(center, 10, 11, Solid);
        SetFlags(center, 10, 11, CellFlags.RigidOwned);
        SetLifetime(center, 10, 11, 5);
        SimulationKernel kernel = new(source, CreateMaterials());
        kernel.SwapDirtyRects();

        ushort material = kernel.ReadAndClearCell(10, 11, out byte flags, out byte lifetime);

        // Assert：验证预期结果
        Assert.Equal(Solid, material);
        Assert.Equal(CellFlags.RigidOwned, flags);
        Assert.Equal(5, lifetime);
        Assert.Equal(0, Get(center, 10, 11));
        Assert.Equal(0, GetFlags(center, 10, 11));
        Assert.Equal(0, GetLifetime(center, 10, 11));
        Assert.Equal(new DirtyRect(8, 9, 12, 13), center.CurrentDirty);

        kernel.StepCa();

        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Sand, Get(center, 10, 10 + EngineConstants.MoveCap));
    }

    /// <summary>
    /// 验证 CountNonEmptyCells 可观测无反应 movement 的质量守恒。
    /// </summary>
    [Fact]
    public void CountNonEmptyCellsTracksMovementConservationAcrossChunkBoundary()
    {
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 63, Sand);
        center.SetCurrentDirty(DirtyRect.Full);
        SimulationKernel kernel = new(source, CreateMaterials());

        long before = kernel.CountNonEmptyCells();
        kernel.StepCa();
        long after = kernel.CountNonEmptyCells();

        Assert.Equal(1, before);
        Assert.Equal(before, after);
    }

    /// <summary>
    /// 验证 SnapshotChunk 是深拷贝，后续 chunk 修改不影响快照。
    /// </summary>
    [Fact]
    public void SnapshotChunkReturnsDeepCopy()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 10, Sand);
        SetFlags(center, 10, 10, CellFlags.Burning);
        center.SetCurrentDirty(new DirtyRect(10, 10, 10, 10));
        SimulationKernel kernel = new(source, CreateMaterials());

        ChunkSnapshot snapshot = kernel.SnapshotChunk(center.Coord);
        Set(center, 10, 10, Solid);
        SetFlags(center, 10, 10, 0);

        int local = CellAddressing.LocalIndexFromLocal(10, 10);
        // Assert：验证预期结果
        Assert.Equal(Sand, snapshot.Material[local]);
        Assert.Equal(CellFlags.Burning, snapshot.Flags[local]);
        Assert.Equal(new DirtyRect(10, 10, 10, 10), snapshot.CurrentDirty);
    }

    /// <summary>
    /// 验证跨界 movement 会记录 KeepAlive 诊断。
    /// </summary>
    [Fact]
    public void MovementAcrossChunkBoundaryRecordsBoundaryWakeDiagnostics()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Set(center, 10, 63, Sand);
        center.SetCurrentDirty(DirtyRect.Full);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(1, kernel.Diagnostics.BoundaryWakeCount);
        BoundaryWakeRecord record = kernel.Diagnostics.BoundaryWakeRecords[0];
        Assert.Equal(new ChunkCoord(0, 1), record.TargetCoord);
        Assert.Equal(KeepAliveDirections.SlotNorth, record.IncomingSlot);
        Assert.Equal(new DirtyRect(8, 29, 12, 33), record.Rect);
    }

    /// <summary>
    /// 验证 successful reaction 会记录尝试、成功与跨界反应探针。
    /// </summary>
    [Fact]
    public void SuccessfulBoundaryReactionRecordsProbe()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateNeighborhood(new ChunkCoord(0, 0), out Chunk center);
        Chunk east = source.GetRequired(new ChunkCoord(1, 0));
        Set(center, 63, 10, Fire);
        Set(east, 0, 10, Solid);
        center.SetCurrentDirty(DirtyRect.Full);
        SimulationKernel kernel = new(source, CreateMaterials(), reactionExecutor: new SuccessfulReactionExecutor());

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(1, kernel.Diagnostics.ReactionAttemptCount);
        Assert.Equal(1, kernel.Diagnostics.ReactionSuccessCount);
        Assert.Equal(1, kernel.Diagnostics.BoundaryReactionCount);
        ReactionProbeRecord record = kernel.Diagnostics.ReactionRecords[0];
        Assert.True(record.CrossesChunkBoundary);
        Assert.Equal((63, 10, Fire, 64, 10, Solid), (record.X1, record.Y1, record.MaterialA, record.X2, record.Y2, record.MaterialB));
    }

    /// <summary>
    /// 验证强制单线程开关会让 StepCa(JobSystem) 不触碰已释放 JobSystem。
    /// </summary>
    [Fact]
    public void ForceSingleThreadRunsJobSystemOverloadWithoutDispatchingJobs()
    {
        TestChunkSource source = CreateDenseSource(-1, -1, 4, 2);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        Set(center, 10, 10, Sand);
        center.SetCurrentDirty(DirtyRect.Full);
        SimulationKernel kernel = new(source, CreateMaterials())
        {
            ForceSingleThread = true,
        };
        using JobSystem jobs = new(workerCount: 2);
        jobs.Dispose();

        kernel.StepCa(jobs);

        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Sand, Get(center, 10, 10 + EngineConstants.MoveCap));
    }

    private static MaterialPropsTable CreateMaterials()
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder, CellType.Fire],
            [0, 255, 120, 1],
            [0, 0, 0, 0],
            [0, 0, 0, 0],
            [0, 0, 0, 1],
            [0, 0, 7, 0]);
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

    private static TestChunkSource CreateDenseSource(int minX, int minY, int maxX, int maxY)
    {
        List<Chunk> chunks = [];
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                chunks.Add(new Chunk(new ChunkCoord(x, y)));
            }
        }

        return new TestChunkSource([.. chunks]);
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

    private sealed class SuccessfulReactionExecutor : IReactionExecutor
    {
        public bool TryReact(ref NeighborWindow window, int wx1, int wy1, ushort materialA, int wx2, int wy2, ushort materialB, byte parityBit, byte randomByte)
        {
            window.SetFlags(wx1, wy1, CellFlags.SetParity(window.GetFlags(wx1, wy1), parityBit));
            window.SetFlags(wx2, wy2, CellFlags.SetParity(window.GetFlags(wx2, wy2), parityBit));
            return true;
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
