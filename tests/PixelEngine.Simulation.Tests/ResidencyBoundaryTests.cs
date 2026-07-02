using System.Buffers;
using PixelEngine.Core;
using PixelEngine.Serialization;
using PixelEngine.World;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// World 驻留 border ring 与 CA 32px halo 的边界安全测试。
/// </summary>
public sealed class ResidencyBoundaryTests
{
    private const ushort Sand = 1;

    /// <summary>
    /// 验证多 chunk active 的四角都能通过 1-chunk border ring 解析完整 3x3 邻域。
    /// </summary>
    [Fact]
    public void ActiveRectCornersResolveFullNeighborhoodThroughBorderRing()
    {
        using TempWorldDirectory world = TempWorldDirectory.Create();
        WorldManager manager = CreateManager(
            world.Path,
            focusX: 64,
            focusY: 64,
            viewportCellsX: 128,
            viewportCellsY: 128,
            maxStreamOps: 32);

        PumpResidency(manager, frame: 1);

        ChunkCoord[] active =
        [
            new(0, 0),
            new(1, 0),
            new(0, 1),
            new(1, 1),
        ];

        foreach (ChunkCoord coord in active)
        {
            Assert.True(manager.Chunks.ResolveNeighborhood(coord, out _));
            Assert.True(manager.Residency.TryGetInfo(coord, out ChunkResidencyInfo info));
            Assert.Equal(ChunkResidencyState.Active, info.State);
        }

        for (int y = -1; y <= 2; y++)
        {
            for (int x = -1; x <= 2; x++)
            {
                ChunkCoord coord = new(x, y);
                Assert.True(manager.Chunks.TryGetChunk(coord, out Chunk chunk));
                Assert.True(manager.Residency.TryGetInfo(coord, out ChunkResidencyInfo info));
                if (Array.IndexOf(active, coord) >= 0)
                {
                    continue;
                }

                Assert.Equal(ChunkResidencyState.Border, info.State);
                Assert.Equal(ChunkState.Sleeping, chunk.State);
                Assert.Equal(DirtyRect.Empty, chunk.CurrentDirty);
            }
        }
    }

    /// <summary>
    /// 验证 active 边缘 CA 写入 border 后，World 会根据 KeepAlive 诊断补齐新的外圈 border。
    /// </summary>
    [Fact]
    public void ActiveEdgeKeepAlivePromotesBorderAndLoadsOuterRing()
    {
        using TempWorldDirectory world = TempWorldDirectory.Create();
        WorldManager manager = CreateManager(world.Path, focusX: 32, focusY: 32, viewportCellsX: 64, viewportCellsY: 64, maxStreamOps: 32);
        PumpResidency(manager, frame: 1);
        Chunk center = GetChunk(manager, new ChunkCoord(0, 0));
        Chunk south = GetChunk(manager, new ChunkCoord(0, 1));
        center.SetCurrentDirty(DirtyRect.Full);
        Set(center, 10, EngineConstants.ChunkSize - 1, Sand);
        SimulationKernel kernel = new(manager.Chunks, CreateProps());

        kernel.StepCa();

        Assert.Equal(Sand, Get(south, 10, EngineConstants.MoveCap - 1));
        BoundaryWakeSnapshot[] wakes = new BoundaryWakeSnapshot[4];
        int wakeCount = kernel.CopyBoundaryWakeSnapshots(wakes);
        Assert.Equal(1, wakeCount);

        manager.NotifyBoundaryWakes(wakes.AsSpan(0, wakeCount));
        manager.ApplyResidency(frame: 3);

        Assert.True(manager.Residency.TryGetInfo(new ChunkCoord(0, 1), out ChunkResidencyInfo promoted));
        Assert.Equal(ChunkResidencyState.Active, promoted.State);
        Assert.Equal(3, manager.Streamer.PendingRequestCount);

        _ = manager.Streamer.ProcessIoOnce();
        manager.ApplyResidency(frame: 4);

        Assert.True(manager.Chunks.ResolveNeighborhood(new ChunkCoord(0, 1), out _));
        Assert.True(manager.Chunks.TryGetChunk(new ChunkCoord(0, 2), out Chunk outer));
        Assert.Equal(ChunkState.Sleeping, outer.State);
    }

    /// <summary>
    /// 验证相位 2 结构性卸载只摘下 border 外 cached chunk，不会让 active/border live chunk 变成 Detached。
    /// </summary>
    [Fact]
    public void PhaseTwoDetachKeepsActiveAndBorderResidentAndOnlyRemovesCachedOutside()
    {
        ResidentChunkMap chunks = new();
        ResidencyTable residency = new();
        ChunkMemoryBudget budget = new(
            ChunkMemoryBudget.EstimatedResidentChunkBytes * 2L,
            ChunkMemoryBudget.EstimatedResidentChunkBytes);
        AddResident(chunks, residency, budget, new ChunkCoord(0, 0), ChunkResidencyState.Active, frame: 10);
        AddResident(chunks, residency, budget, new ChunkCoord(0, 1), ChunkResidencyState.Border, frame: 10);
        ChunkCoord outside = new(3, 0);
        AddResident(chunks, residency, budget, outside, ChunkResidencyState.Cached, frame: 1);
        ResidencyPlanner planner = new(new WorldStreamingConfig { MaxStreamOpsPerFrame = 32 });
        ResidencyPlan plan = planner.Plan(new ChunkRect(0, 0, 0, 0), new ChunkRect(-1, -1, 1, 1), residency, budget);
        WorldStreamer streamer = new(chunks, residency, budget, new TemperatureField(), new MemoryChunkStore(), IdentityRemap());

        streamer.SubmitPlan(plan);

        Assert.True(chunks.Contains(new ChunkCoord(0, 0)));
        Assert.True(chunks.Contains(new ChunkCoord(0, 1)));
        Assert.False(chunks.Contains(outside));
        Assert.True(residency.TryGetInfo(outside, out ChunkResidencyInfo outsideInfo));
        Assert.Equal(ChunkResidencyState.Detached, outsideInfo.State);
        foreach (Chunk chunk in chunks.ResidentChunks)
        {
            Assert.True(residency.TryGetInfo(chunk.Coord, out ChunkResidencyInfo info));
            Assert.NotEqual(ChunkResidencyState.Detached, info.State);
        }
    }

    /// <summary>
    /// 验证固定 resident world 的最外圈 guard chunk 被 dirty 唤醒时不会进入 CA 调度。
    /// </summary>
    [Fact]
    public void CaSchedulerDropsDirtyGuardChunkWithoutFullNeighborhood()
    {
        ResidentChunkMap chunks = new();
        Chunk guard = new(new ChunkCoord(0, 0));
        chunks.Add(guard);
        guard.SetCurrentDirty(DirtyRect.Full);
        SimulationKernel kernel = new(chunks, CreateProps());

        kernel.StepCa();

        Assert.Equal(DirtyRect.Empty, guard.CurrentDirty);
        Assert.Equal(ChunkState.Sleeping, guard.State);
    }

    private static WorldManager CreateManager(
        string worldPath,
        long focusX,
        long focusY,
        int viewportCellsX,
        int viewportCellsY,
        int maxStreamOps)
    {
        return new WorldManager(
            new WorldCamera(focusX, focusY, viewportCellsX, viewportCellsY),
            new TemperatureField(),
            Materials(),
            worldPath,
            fallbackMaterialId: 0,
            new WorldStreamingConfig
            {
                ActivationMarginChunks = 0,
                BorderRingWidth = 1,
                MaxStreamOpsPerFrame = maxStreamOps,
            });
    }

    private static void PumpResidency(WorldManager manager, long frame)
    {
        manager.ApplyResidency(frame);
        _ = manager.Streamer.ProcessIoOnce();
        manager.ApplyResidency(frame + 1);
    }

    private static void AddResident(
        ResidentChunkMap chunks,
        ResidencyTable residency,
        ChunkMemoryBudget budget,
        ChunkCoord coord,
        ChunkResidencyState state,
        long frame)
    {
        chunks.Add(new Chunk(coord));
        residency.Set(coord, new ChunkResidencyInfo(state, frame, ChunkMemoryBudget.EstimatedResidentChunkBytes, DirtySinceLoad: false));
        budget.Add(ChunkMemoryBudget.EstimatedResidentChunkBytes);
    }

    private static Chunk GetChunk(WorldManager manager, ChunkCoord coord)
    {
        Assert.True(manager.Chunks.TryGetChunk(coord, out Chunk chunk));
        return chunk;
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private static MaterialPropsTable CreateProps()
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Powder],
            [0, 120],
            [0, 0],
            [0, 0],
            [0, 0],
            [0, 0]);
    }

    private static MaterialTable Materials()
    {
        return new MaterialTable(
        [
            new MaterialDef
            {
                Id = 0,
                Name = "empty",
                Type = CellType.Empty,
                HeatCapacity = 1,
                TextureId = -1,
            },
            new MaterialDef
            {
                Id = Sand,
                Name = "sand",
                Type = CellType.Powder,
                Density = 120,
                HeatCapacity = 1,
                TextureId = -1,
            },
        ]);
    }

    private static MaterialRemap IdentityRemap()
    {
        MaterialTable materials = Materials();
        return MaterialRemap.Build(new MaterialNameTable(materials.BuildIdNameTable()), materials, fallbackId: 0);
    }

    private sealed class MemoryChunkStore : IChunkStore
    {
        private readonly Dictionary<ChunkCoord, byte[]> _blobs = [];

        public bool TryRead(ChunkCoord coord, IBufferWriter<byte> destination)
        {
            if (!_blobs.TryGetValue(coord, out byte[]? blob))
            {
                return false;
            }

            Span<byte> span = destination.GetSpan(blob.Length);
            blob.CopyTo(span);
            destination.Advance(blob.Length);
            return true;
        }

        public void Write(ChunkCoord coord, ReadOnlySpan<byte> blob)
        {
            _blobs[coord] = blob.ToArray();
        }

        public bool Exists(ChunkCoord coord)
        {
            return _blobs.ContainsKey(coord);
        }

        public void Delete(ChunkCoord coord)
        {
            _ = _blobs.Remove(coord);
        }
    }

    private sealed class TempWorldDirectory : IDisposable
    {
        private TempWorldDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempWorldDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PixelEngine.ResidencyBoundaryTests",
                Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(path);
            return new TempWorldDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
