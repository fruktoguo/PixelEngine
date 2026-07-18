using PixelEngine.Core;
using PixelEngine.Core.Threading;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Simulation 节点 6 的 checkerboard 调度测试。
/// 不变式：红黑切片调度无读写竞争、统计与单线程参考等价。
/// </summary>
public sealed class CheckerboardSchedulerTests
{
    private const ushort Sand = 2;
    private const ushort Gas = 3;

    /// <summary>
    /// 验证低活跃 chunk 数时 StepCa(JobSystem) 不触碰 JobSystem 派发路径，直接单线程回退。
    /// </summary>
    [Fact]
    public void StepCaWithJobSystemFallsBackToSingleThreadForFewAwakeChunks()
    {
        TestChunkSource source = CreateDenseSource(-1, -1, 1, 1);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        Set(center, 10, 10, Sand);
        center.SetCurrentDirty(DirtyRect.Full);
        SimulationKernel kernel = new(source, CreateMaterials());
        using JobSystem jobs = new(workerCount: 2);
        jobs.Dispose();

        kernel.StepCa(jobs);

        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Sand, Get(center, 10, 10 + EngineConstants.MoveCap));
    }

    /// <summary>
    /// 验证 4-pass checkerboard 会按 bucket 处理多个 awake chunk，并经 JobSystem 路径完成更新。
    /// </summary>
    [Fact]
    public void StepCaWithJobSystemProcessesAllCheckerboardBuckets()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateDenseSource(-1, -1, 4, 2);
        ChunkCoord[] activeCoords =
        [
            new(0, 0),
            new(1, 0),
            new(2, 0),
            new(3, 0),
            new(0, 1),
            new(1, 1),
            new(2, 1),
            new(3, 1),
        ];

        foreach (ChunkCoord coord in activeCoords)
        {
            Chunk chunk = source.GetRequired(coord);
            Set(chunk, 10, 10, Sand);
            chunk.SetCurrentDirty(DirtyRect.Full);
        }

        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa(jobs);

        CaIterationSnapshot[] iterations = new CaIterationSnapshot[activeCoords.Length];
        int iterationCount = kernel.CopyCaIterationSnapshots(iterations);
        // Assert：验证预期结果
        Assert.Equal(activeCoords.Length, iterationCount);
        Assert.All(iterations, static iteration => Assert.Equal(DirtyRect.Full, iteration.Rect));
        foreach (ChunkCoord coord in activeCoords)
        {
            Chunk chunk = source.GetRequired(coord);
            Assert.Equal(0, Get(chunk, 10, 10));
            Assert.Equal(Sand, Get(chunk, 10, 10 + EngineConstants.MoveCap));
        }
    }

    /// <summary>
    /// 验证 checkerboard 调度复用 bucket 构建时解析出的 3x3 邻域，避免每个 active chunk 在热路径重复查表。
    /// </summary>
    [Fact]
    public void StepCaWithJobSystemResolvesNeighborhoodOncePerActiveChunk()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateDenseSource(-1, -1, 4, 2);
        ChunkCoord[] activeCoords =
        [
            new(0, 0),
            new(1, 0),
            new(2, 0),
            new(3, 0),
            new(0, 1),
            new(1, 1),
            new(2, 1),
            new(3, 1),
        ];

        foreach (ChunkCoord coord in activeCoords)
        {
            Chunk chunk = source.GetRequired(coord);
            Set(chunk, 10, 10, Sand);
            chunk.SetCurrentDirty(DirtyRect.Full);
        }

        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa(jobs);

        // Assert：验证预期结果
        Assert.Equal(activeCoords.Length, source.ResolveNeighborhoodCount);
    }

    /// <summary>
    /// 验证中心 chunk 内部移动直接标记本地 dirty，不在热路径额外回查 chunk map。
    /// </summary>
    [Fact]
    public void StepCaInternalMoveMarksCenterDirtyWithoutExtraChunkLookup()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateDenseSource(-1, -1, 1, 1);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        Set(center, 10, 10, Sand);
        center.SetCurrentDirty(DirtyRect.Full);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(1, source.ResolveNeighborhoodCount);
        Assert.Equal(9, source.TryGetChunkCount);
        Assert.Equal(0, Get(center, 10, 10));
        Assert.Equal(Sand, Get(center, 10, 10 + EngineConstants.MoveCap));
        Assert.False(center.WorkingDirty.IsEmpty);
    }

    /// <summary>
    /// 验证跨 chunk 移动复用已解析的邻域 chunk 标记 KeepAlive，不在热路径额外回查 chunk map。
    /// </summary>
    [Fact]
    public void StepCaBoundaryMoveMarksKeepAliveWithoutExtraChunkLookup()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateDenseSource(-1, -1, 1, 1);
        Chunk center = source.GetRequired(new ChunkCoord(0, 0));
        Chunk south = source.GetRequired(new ChunkCoord(0, 1));
        Set(center, 10, EngineConstants.ChunkSize - 1, Sand);
        center.SetCurrentDirty(DirtyRect.Full);
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa();

        // Assert：验证预期结果
        Assert.Equal(1, source.ResolveNeighborhoodCount);
        Assert.Equal(9, source.TryGetChunkCount);
        Assert.Equal(0, Get(center, 10, EngineConstants.ChunkSize - 1));
        Assert.Equal(Sand, Get(south, 10, EngineConstants.MoveCap - 1));
        Assert.False(center.WorkingDirty.IsEmpty);
        Assert.False(south.GetIncomingDirty(KeepAliveDirections.SlotNorth).IsEmpty);
    }

    /// <summary>
    /// 验证 resident 但 sleeping 的 chunk 不进入调度桶，静止区域不会被下一帧迭代。
    /// </summary>
    [Fact]
    public void StepCaSkipsSleepingChunksEvenWhenResident()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateDenseSource(-1, -1, 2, 2);
        Chunk active = source.GetRequired(new ChunkCoord(0, 0));
        Chunk sleeping = source.GetRequired(new ChunkCoord(2, 2));
        Set(active, 10, 10, Sand);
        Set(sleeping, 10, 10, Sand);
        active.SetCurrentDirty(DirtyRect.Full);
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa(jobs);

        // Assert：验证预期结果
        Assert.Equal(0, Get(active, 10, 10));
        Assert.Equal(Sand, Get(active, 10, 10 + EngineConstants.MoveCap));
        Assert.Equal(Sand, Get(sleeping, 10, 10));
        Assert.Equal(0, Get(sleeping, 10, 11));
        Assert.Equal(ChunkState.Sleeping, sleeping.State);
    }

    /// <summary>
    /// 验证满屏 footprint 常驻但 dirty 为空时，JobSystem 路径不会迭代任何 sleeping chunk。
    /// </summary>
    [Fact]
    public void StepCaSkipsFullSleepingFootprint()
    {
        // Arrange：准备输入与初始状态
        const int activeChunksPerAxis = 8;
        TestChunkSource source = CreateDenseSource(-1, -1, activeChunksPerAxis, activeChunksPerAxis);
        for (int cy = 0; cy < activeChunksPerAxis; cy++)
        {
            for (int cx = 0; cx < activeChunksPerAxis; cx++)
            {
                Chunk chunk = source.GetRequired(new ChunkCoord(cx, cy));
                Set(chunk, 10, 10, Sand);
            }
        }

        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        SimulationKernel kernel = new(source, CreateMaterials());

        kernel.StepCa(jobs);
        kernel.SwapDirtyRects();

        CaIterationSnapshot[] iterations = new CaIterationSnapshot[1];
        // Assert：验证预期结果
        Assert.Equal(0, kernel.CopyCaIterationSnapshots(iterations));
        for (int cy = 0; cy < activeChunksPerAxis; cy++)
        {
            for (int cx = 0; cx < activeChunksPerAxis; cx++)
            {
                Chunk chunk = source.GetRequired(new ChunkCoord(cx, cy));
                Assert.Equal(Sand, Get(chunk, 10, 10));
                Assert.Equal(0, Get(chunk, 10, 10 + EngineConstants.MoveCap));
                Assert.Equal(DirtyRect.Empty, chunk.CurrentDirty);
                Assert.Equal(DirtyRect.Empty, chunk.WorkingDirty);
                Assert.Equal(ChunkState.Sleeping, chunk.State);
            }
        }
    }

    /// <summary>
    /// 验证同一 checkerboard pass 的上下 active chunk 写入共享中间 chunk 时，
    /// 南向 32px movement 与北向 gas movement 分别维护不同 32-row word，不发生 lost update。
    /// </summary>
    [Fact]
    public void ParallelSamePassMaintainsSeparateColumnOccupancyHalves()
    {
        TestChunkSource source = CreateDenseSource(-1, -1, 3, 3);
        SimulationKernel kernel = new(source, CreateMaterials());
        using JobSystem jobs = new(workerCount: 4)
        {
            SingleThreadThreshold = 0,
        };

        for (int iteration = 0; iteration < 100; iteration++)
        {
            foreach (Chunk chunk in source.ResidentChunks)
            {
                chunk.Reset(chunk.Coord);
            }

            kernel.RestoreFrameState(frameIndex: 0, currentParity: 0);
            for (int chunkX = 0; chunkX <= 2; chunkX += 2)
            {
                Chunk north = source.GetRequired(new ChunkCoord(chunkX, 0));
                Chunk south = source.GetRequired(new ChunkCoord(chunkX, 2));
                Set(north, 10, EngineConstants.ChunkSize - 1, Sand);
                Set(south, 10, 0, Gas);
                north.SetCurrentDirty(DirtyRect.Full);
                south.SetCurrentDirty(DirtyRect.Full);
            }

            kernel.StepCa(jobs);

            for (int chunkX = 0; chunkX <= 2; chunkX += 2)
            {
                Chunk middle = source.GetRequired(new ChunkCoord(chunkX, 1));
                Assert.Equal(Sand, middle.Material[CellAddressing.LocalIndexFromLocal(10, 31)]);
                Assert.Equal(Gas, middle.Material[CellAddressing.LocalIndexFromLocal(10, 63)]);
                Assert.Equal(31, middle.FindFirstOccupiedInColumn(10, 0, 31));
                Assert.Equal(63, middle.FindFirstOccupiedInColumn(10, 32, 63));
            }
        }
    }

    /// <summary>
    /// 验证远区 chunk 被降频跳过时会把 current dirty 保留到下一帧，不会在 dirty swap 后睡眠丢工作。
    /// </summary>
    [Fact]
    public void DistantChunkThrottleDefersDirtyWhenCohortIsSkipped()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateDenseSource(-1, -1, 2, 1);
        Chunk distant = source.GetRequired(new ChunkCoord(2, 0));
        Set(distant, 10, 10, Sand);
        distant.SetCurrentDirty(DirtyRect.Full);
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        SimulationKernel kernel = new(source, CreateMaterials());
        CaChunkThrottlePolicy policy = new(
            Enabled: true,
            FullRateMinCx: 0,
            FullRateMinCy: 0,
            FullRateMaxCx: 0,
            FullRateMaxCy: 0,
            FrameIndex: 0);

        kernel.StepCa(jobs, policy);

        CaIterationSnapshot[] iterations = new CaIterationSnapshot[1];
        // Assert：验证预期结果
        Assert.Equal(0, kernel.CopyCaIterationSnapshots(iterations));
        Assert.Equal(Sand, Get(distant, 10, 10));
        Assert.Equal(DirtyRect.Full, distant.WorkingDirty);

        kernel.SwapDirtyRects();

        Assert.Equal(DirtyRect.Full, distant.CurrentDirty);
        Assert.Equal(ChunkState.Awake, distant.State);
    }

    /// <summary>
    /// 验证远区 chunk 隔帧恢复运行前会预处理 parity，不会把上次处理过的 cell 误判为本帧已处理。
    /// </summary>
    [Fact]
    public void DistantChunkThrottlePreparesParityBeforeDeferredChunkRunsAgain()
    {
        // Arrange：准备输入与初始状态
        TestChunkSource source = CreateDenseSource(-1, -1, 2, 1);
        Chunk distant = source.GetRequired(new ChunkCoord(1, 0));
        Chunk below = source.GetRequired(new ChunkCoord(1, 1));
        Set(distant, 10, 0, Sand);
        distant.SetCurrentDirty(DirtyRect.Full);
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        SimulationKernel kernel = new(source, CreateMaterials());
        CaChunkThrottlePolicy policy = new(
            Enabled: true,
            FullRateMinCx: 0,
            FullRateMinCy: 0,
            FullRateMaxCx: 0,
            FullRateMaxCy: 0,
            FrameIndex: 0);

        kernel.StepCa(jobs, policy);
        kernel.SwapDirtyRects();
        // Assert：验证预期结果
        Assert.Equal(0, Get(distant, 10, 0));
        Assert.Equal(Sand, Get(distant, 10, EngineConstants.MoveCap));

        kernel.StepCa(jobs, policy);
        kernel.SwapDirtyRects();
        Assert.Equal(Sand, Get(distant, 10, EngineConstants.MoveCap));

        kernel.StepCa(jobs, policy);

        Assert.Equal(0, Get(distant, 10, EngineConstants.MoveCap));
        Assert.Equal(Sand, Get(below, 10, 0));
    }

    private static MaterialPropsTable CreateMaterials()
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Solid, CellType.Powder, CellType.Gas],
            [0, 255, 120, 1],
            [0, 0, 0, 1],
            [0, 0, 0, 0],
            [0, 0, 0, 0],
            [0, 0, 0, 0]);
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

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)];
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

        public int ResolveNeighborhoodCount { get; private set; }

        public int TryGetChunkCount { get; private set; }

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            TryGetChunkCount++;
            return _byCoord.TryGetValue(coord, out chunk!);
        }

        public Chunk GetRequired(ChunkCoord coord)
        {
            return _byCoord[coord];
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            ResolveNeighborhoodCount++;
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
