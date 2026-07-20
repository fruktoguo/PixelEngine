using System.Buffers;
using PixelEngine.Core.Threading;
using PixelEngine.Serialization;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.World.Tests;

/// <summary>
/// WorldStreamer 与 WorldManager 相位分离测试。
/// 不变式：SubmitPlan 在相位 2 摘卸载、I/O 异步后 ApplyPrepared 才改驻留表、material remap 与温度场一致。
/// </summary>
public sealed class WorldStreamerTests
{
    /// <summary>
    /// 验证 SubmitPlan 在相位 2 摘下待卸载 chunk，后台 I/O 后才移除记账。
    /// </summary>
    [Fact]
    public void SubmitPlanDetachesUnloadWithoutDoingIoAndApplyPreparedFinalizes()
    {
        // Arrange：搭建测试场景与依赖
        ResidentChunkMap chunks = new();
        ResidencyTable residency = new();
        ChunkMemoryBudget budget = Budget();
        TemperatureField temperature = new();
        MemoryChunkStore store = new();
        MaterialRemap remap = IdentityRemap();
        WorldStreamer streamer = new(chunks, residency, budget, temperature, store, remap);
        ChunkCoord coord = new(2, 3);
        Chunk chunk = new(coord);
        chunk.MaterialBuffer[0] = 1;
        chunks.Add(chunk);
        residency.Set(coord, new ChunkResidencyInfo(ChunkResidencyState.Cached, 1, ChunkMemoryBudget.EstimatedResidentChunkBytes, DirtySinceLoad: true));
        budget.Add(ChunkMemoryBudget.EstimatedResidentChunkBytes);
        temperature.AddHeat((coord.X << 6) + 4, (coord.Y << 6) + 4, 12.5f);
        ResidencyPlan plan = new([], [coord], [new ResidencyStateChange(coord, ChunkResidencyState.Detached)]);

        // Act：执行被测操作
        streamer.SubmitPlan(plan);

        // Assert：验证不变式与预期结果
        Assert.False(chunks.Contains(coord));
        Assert.True(residency.TryGetInfo(coord, out ChunkResidencyInfo detached));
        Assert.Equal(ChunkResidencyState.Detached, detached.State);
        Assert.False(store.Exists(coord));
        Assert.Equal(1, streamer.PendingRequestCount);

        Assert.Equal(1, streamer.ProcessIoOnce());
        Assert.True(store.Exists(coord));
        Assert.Equal(1, streamer.ApplyPrepared(frame: 9));

        Assert.False(residency.TryGetInfo(coord, out _));
        Assert.Equal(0, budget.ResidentBytes);
    }

    /// <summary>
    /// 验证后台装载从 chunk store 读取、重映射 material，并在相位 2 插回 live map。
    /// </summary>
    [Fact]
    public void ProcessIoLoadsChunkRemapsMaterialAndApplyPreparedAddsResident()
    {
        // Arrange：搭建测试场景与依赖
        ResidentChunkMap chunks = new();
        ResidencyTable residency = new();
        ChunkMemoryBudget budget = Budget();
        TemperatureField temperature = new();
        MemoryChunkStore store = new();
        ChunkCoord coord = new(-1, 0);
        WriteStoredChunk(store, coord, savedMaterial: 1, temp: (Half)22f, damage: 13);
        MaterialTable currentMaterials = Materials(("empty", CellType.Empty), ("water", CellType.Liquid), ("sand", CellType.Powder));
        MaterialRemap remap = MaterialRemap.Build(new MaterialNameTable([(0, "empty"), (1, "sand")]), currentMaterials, fallbackId: 0);
        WorldStreamer streamer = new(chunks, residency, budget, temperature, store, remap)
        {
            CurrentParityBit = CellFlags.Parity,
        };
        ResidencyPlan plan = new([coord], [], []);

        // Act：执行被测操作
        streamer.SubmitPlan(plan);
        // Assert：验证不变式与预期结果
        Assert.False(chunks.Contains(coord));
        Assert.Equal(1, streamer.ProcessIoOnce());
        Assert.Equal(1, streamer.ApplyPrepared(frame: 5));

        Assert.True(chunks.TryGetChunk(coord, out Chunk loaded));
        Assert.Equal(2, loaded.MaterialBuffer[0]);
        Assert.Equal(13, loaded.DamageBuffer[0]);
        Assert.Equal(DirtyRect.Full, loaded.CurrentDirty);
        Assert.Equal(22, temperature.GetTemperature(coord.X << 6, coord.Y << 6));
        Assert.True(residency.TryGetInfo(coord, out ChunkResidencyInfo info));
        Assert.Equal(ChunkResidencyState.Cached, info.State);
        Assert.Equal(ChunkMemoryBudget.EstimatedResidentChunkBytes, budget.ResidentBytes);
    }

    /// <summary>
    /// 验证缺失 chunk 只在首次装载时生成；修改落盘后重入优先恢复存档，不再次覆盖生成结果。
    /// </summary>
    [Fact]
    public void MissingChunkInitializerRunsOnceAndPersistedEditWinsOnReload()
    {
        ResidentChunkMap chunks = new();
        ResidencyTable residency = new();
        ChunkMemoryBudget budget = Budget();
        TemperatureField temperature = new();
        MemoryChunkStore store = new();
        RecordingChunkInitializer initializer = new(material: 1, temperature: (Half)18f);
        WorldStreamer streamer = new(
            chunks,
            residency,
            budget,
            temperature,
            store,
            IdentityRemap(),
            chunkInitializer: initializer);
        ChunkCoord coord = new(-3, 2);

        streamer.SubmitPlan(new ResidencyPlan([coord], [], []));
        Assert.Equal(1, streamer.ProcessIoOnce());
        Assert.Equal(1, streamer.ApplyPrepared(frame: 1));

        Assert.Equal(1, initializer.CallCount);
        Assert.Equal(-3, initializer.LastChunkX);
        Assert.Equal(2, initializer.LastChunkY);
        Assert.Equal(-192L, initializer.LastOriginX);
        Assert.Equal(128L, initializer.LastOriginY);
        Assert.True(chunks.TryGetChunk(coord, out Chunk generated));
        Assert.Equal(1, generated.Material[0]);
        Assert.Equal(18f, temperature.GetTemperature(-192, 128));

        generated.MaterialBuffer[0] = 0;
        streamer.SubmitPlan(new ResidencyPlan(
            [],
            [coord],
            [new ResidencyStateChange(coord, ChunkResidencyState.Detached)]));
        Assert.Equal(1, streamer.ProcessIoOnce());
        Assert.Equal(1, streamer.ApplyPrepared(frame: 2));
        Assert.True(store.Exists(coord));

        streamer.SubmitPlan(new ResidencyPlan([coord], [], []));
        Assert.Equal(1, streamer.ProcessIoOnce());
        Assert.Equal(1, streamer.ApplyPrepared(frame: 3));

        Assert.Equal(1, initializer.CallCount);
        Assert.True(chunks.TryGetChunk(coord, out Chunk reloaded));
        Assert.Equal(0, reloaded.Material[0]);
    }

    /// <summary>
    /// 验证磁盘已有 chunk 时绝不调用缺失 chunk 初始化器。
    /// </summary>
    [Fact]
    public void StoredChunkBypassesMissingChunkInitializer()
    {
        ResidentChunkMap chunks = new();
        ResidencyTable residency = new();
        ChunkMemoryBudget budget = Budget();
        TemperatureField temperature = new();
        MemoryChunkStore store = new();
        ChunkCoord coord = new(4, -5);
        WriteStoredChunk(store, coord, savedMaterial: 1, temp: (Half)27f, damage: 0);
        RecordingChunkInitializer initializer = new(material: 2, temperature: (Half)99f);
        WorldStreamer streamer = new(
            chunks,
            residency,
            budget,
            temperature,
            store,
            IdentityRemap(),
            chunkInitializer: initializer);

        streamer.SubmitPlan(new ResidencyPlan([coord], [], []));
        _ = streamer.ProcessIoOnce();
        _ = streamer.ApplyPrepared(frame: 1);

        Assert.Equal(0, initializer.CallCount);
        Assert.True(chunks.TryGetChunk(coord, out Chunk loaded));
        Assert.Equal(1, loaded.Material[0]);
        Assert.Equal(27f, temperature.GetTemperature(256, -320));
    }

    /// <summary>
    /// 验证 world 替换会清空 live 驻留/温度/预算，并让同坐标只读取新的 store 与 initializer。
    /// </summary>
    [Fact]
    public void ResetForNewWorldSwitchesStoreAndInitializerWithoutLeakingResidentState()
    {
        ResidentChunkMap chunks = new();
        ResidencyTable residency = new();
        ChunkMemoryBudget budget = Budget();
        TemperatureField temperature = new();
        MemoryChunkStore firstStore = new();
        RecordingChunkInitializer firstInitializer = new(material: 1, temperature: (Half)18f);
        WorldStreamer streamer = new(
            chunks,
            residency,
            budget,
            temperature,
            firstStore,
            IdentityRemap(),
            chunkInitializer: firstInitializer);
        ChunkCoord coord = new(-2, 3);

        streamer.SubmitPlan(new ResidencyPlan([coord], [], []));
        _ = streamer.ProcessIoOnce();
        _ = streamer.ApplyPrepared(frame: 1);
        Assert.True(chunks.TryGetChunk(coord, out Chunk first));
        first.MaterialBuffer[0] = 0;
        Assert.Equal(ChunkMemoryBudget.EstimatedResidentChunkBytes, budget.ResidentBytes);

        MemoryChunkStore secondStore = new();
        RecordingChunkInitializer secondInitializer = new(material: 2, temperature: (Half)29f);
        streamer.ResetForNewWorld(secondStore, secondInitializer);

        Assert.Equal(0, chunks.Count);
        Assert.Equal(0, residency.Count);
        Assert.Equal(0, budget.ResidentBytes);
        Assert.Equal(0f, temperature.GetTemperature(coord.X << 6, coord.Y << 6));
        Assert.Equal(0, streamer.PendingRequestCount);
        Assert.Equal(0, streamer.PendingCompletedCount);

        streamer.SubmitPlan(new ResidencyPlan([coord], [], []));
        _ = streamer.ProcessIoOnce();
        _ = streamer.ApplyPrepared(frame: 2);

        Assert.Equal(1, firstInitializer.CallCount);
        Assert.Equal(1, secondInitializer.CallCount);
        Assert.True(chunks.TryGetChunk(coord, out Chunk second));
        Assert.Equal(2, second.Material[0]);
        Assert.Equal(29f, temperature.GetTemperature(coord.X << 6, coord.Y << 6));
        Assert.Equal(ChunkMemoryBudget.EstimatedResidentChunkBytes, budget.ResidentBytes);
    }

    /// <summary>
    /// 验证 WorldManager façade 会按相机 active/border 计算提交装载请求。
    /// </summary>
    [Fact]
    public void WorldManagerApplyResidencySubmitsLoadsForBorderArea()
    {
        // Arrange：准备输入与初始状态
        using TempWorldDirectory world = TempWorldDirectory.Create();
        WorldManager manager = new(
            new WorldCamera(32, 32, viewportCellsX: 64, viewportCellsY: 64),
            new TemperatureField(),
            Materials(("empty", CellType.Empty)),
            world.Path,
            fallbackMaterialId: 0,
            new WorldStreamingConfig
            {
                ActivationMarginChunks = 0,
                BorderRingWidth = 1,
                MaxStreamOpsPerFrame = 16,
            });

        manager.ApplyResidency(frame: 1);

        // Assert：验证预期结果
        Assert.Equal(9, manager.Streamer.PendingRequestCount);
        Assert.Equal(9, manager.Residency.Count);

        _ = manager.Streamer.ProcessIoOnce();
        manager.ApplyResidency(frame: 2);

        Assert.True(manager.Chunks.ResolveNeighborhood(new ChunkCoord(0, 0), out _));
        Assert.True(manager.Chunks.TryGetChunk(new ChunkCoord(-1, 0), out Chunk borderChunk));
        Assert.Equal(ChunkState.Sleeping, borderChunk.State);
        Assert.True(borderChunk.CurrentDirty.IsEmpty);
    }

    /// <summary>
    /// 验证相机平移触发卸载和重入装载，chunk 修改经 region store 持久化。
    /// </summary>
    [Fact]
    public void WorldManagerPansEvictsAndReloadsPersistedChunk()
    {
        // Arrange：搭建测试场景与依赖
        using TempWorldDirectory world = TempWorldDirectory.Create();
        MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
        int chunkBytes = ChunkMemoryBudget.EstimatedResidentChunkBytes;
        WorldManager manager = new(
            new WorldCamera(32, 32, viewportCellsX: 64, viewportCellsY: 64),
            new TemperatureField(),
            materials,
            world.Path,
            fallbackMaterialId: 0,
            new WorldStreamingConfig
            {
                ActivationMarginChunks = 0,
                BorderRingWidth = 1,
                ResidentMemoryCapBytes = chunkBytes * 2L,
                EvictionTargetBytes = chunkBytes,
                MaxStreamOpsPerFrame = 64,
            });

        manager.ApplyResidency(frame: 1);
        // Act：执行被测操作
        _ = manager.Streamer.ProcessIoOnce();
        manager.ApplyResidency(frame: 2);
        // Assert：验证不变式与预期结果
        Assert.True(manager.Chunks.TryGetChunk(new ChunkCoord(0, 0), out Chunk edited));
        edited.MaterialBuffer[0] = 1;

        manager.UpdateCamera(320, 32);
        manager.ApplyResidency(frame: 3);
        manager.ApplyResidency(frame: 4);
        _ = manager.Streamer.ProcessIoOnce();
        _ = manager.Streamer.ApplyPrepared(frame: 5);
        Assert.False(manager.Chunks.Contains(new ChunkCoord(0, 0)));

        manager.UpdateCamera(32, 32);
        manager.ApplyResidency(frame: 6);
        _ = manager.Streamer.ProcessIoOnce();
        _ = manager.Streamer.ApplyPrepared(frame: 7);

        Assert.True(manager.Chunks.TryGetChunk(new ChunkCoord(0, 0), out Chunk reloaded));
        Assert.Equal(1, reloaded.Material[0]);
    }

    /// <summary>
    /// 验证持续平移和后台批处理 I/O 下，Detached chunk 不留在 live map，修改能跨装卸边界保留。
    /// </summary>
    [Fact]
    public void StreamingPanningStressKeepsDetachedOutOfLiveMapAndPersistsEdit()
    {
        // Arrange：准备输入与初始状态
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        using TempWorldDirectory world = TempWorldDirectory.Create();
        MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
        int chunkBytes = ChunkMemoryBudget.EstimatedResidentChunkBytes;
        WorldManager manager = new(
            new WorldCamera(32, 32, viewportCellsX: 64, viewportCellsY: 64),
            new TemperatureField(),
            materials,
            world.Path,
            fallbackMaterialId: 0,
            new WorldStreamingConfig
            {
                ActivationMarginChunks = 0,
                BorderRingWidth = 1,
                ResidentMemoryCapBytes = chunkBytes * 2L,
                EvictionTargetBytes = chunkBytes,
                MaxStreamOpsPerFrame = 64,
            });
        long frame = 1;
        Pump(manager, jobs, ref frame, iterations: 2);
        // Assert：验证预期结果
        Assert.True(manager.Chunks.TryGetChunk(new ChunkCoord(0, 0), out Chunk edited));
        edited.MaterialBuffer[0] = 1;

        long[] focusX = [320, 640, 32, 640, 32];
        for (int i = 0; i < focusX.Length; i++)
        {
            manager.UpdateCamera(focusX[i], 32);
            Pump(manager, jobs, ref frame, iterations: 3);
            AssertNoDetachedResident(manager);
        }

        Assert.True(manager.Chunks.TryGetChunk(new ChunkCoord(0, 0), out Chunk reloaded));
        Assert.Equal(1, reloaded.Material[0]);
    }

    /// <summary>
    /// 验证长时间平移后常驻预算稳定在配置上限内，不随漫游距离无界增长。
    /// </summary>
    [Fact]
    public void StreamingLongPanningStressKeepsResidentMemoryUnderConfiguredCap()
    {
        // Arrange：准备输入与初始状态
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        using TempWorldDirectory world = TempWorldDirectory.Create();
        MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
        int chunkBytes = ChunkMemoryBudget.EstimatedResidentChunkBytes;
        long capBytes = chunkBytes * 12L;
        WorldManager manager = new(
            new WorldCamera(32, 32, viewportCellsX: 64, viewportCellsY: 64),
            new TemperatureField(),
            materials,
            world.Path,
            fallbackMaterialId: 0,
            new WorldStreamingConfig
            {
                ActivationMarginChunks = 0,
                BorderRingWidth = 1,
                ResidentMemoryCapBytes = capBytes,
                EvictionTargetBytes = chunkBytes * 9L,
                MaxStreamOpsPerFrame = 128,
            });
        long frame = 1;
        Pump(manager, jobs, ref frame, iterations: 4);
        // Assert：验证预期结果
        Assert.InRange(manager.MemoryBudget.ResidentBytes, 0, capBytes);

        long[] focusX =
        [
            320, 640, 960, 1280,
            960, 640, 320, 32,
            -320, -640, -960, -1280,
            -960, -640, -320, 32,
        ];
        for (int cycle = 0; cycle < 4; cycle++)
        {
            for (int i = 0; i < focusX.Length; i++)
            {
                manager.UpdateCamera(focusX[i], 32);
                Pump(manager, jobs, ref frame, iterations: 4);

                AssertNoDetachedResident(manager);
                Assert.InRange(manager.MemoryBudget.ResidentBytes, 0, capBytes);
                Assert.InRange(manager.Chunks.Count, 0, 12);
            }
        }
    }

    /// <summary>
    /// 验证 ProcessIoOnce(JobSystem) 可并行准备多个 chunk 的装载解码结果。
    /// </summary>
    [Fact]
    public void ProcessIoOnceWithJobSystemPreparesMultipleLoads()
    {
        // Arrange：搭建测试场景与依赖
        using JobSystem jobs = new(workerCount: 2)
        {
            SingleThreadThreshold = 0,
        };
        ResidentChunkMap chunks = new();
        ResidencyTable residency = new();
        ChunkMemoryBudget budget = Budget();
        TemperatureField temperature = new();
        MemoryChunkStore store = new();
        ChunkCoord first = new(0, 0);
        ChunkCoord second = new(1, 0);
        WriteStoredChunk(store, first, savedMaterial: 1, temp: (Half)1f);
        WriteStoredChunk(store, second, savedMaterial: 1, temp: (Half)2f);
        WorldStreamer streamer = new(chunks, residency, budget, temperature, store, IdentityRemap());
        // Act：执行被测操作
        streamer.SubmitPlan(new ResidencyPlan([first, second], [], []));

        // Assert：验证不变式与预期结果
        Assert.Equal(2, streamer.ProcessIoOnce(jobs));
        Assert.Equal(2, streamer.ApplyPrepared(frame: 3));

        Assert.True(chunks.TryGetChunk(first, out Chunk firstChunk));
        Assert.True(chunks.TryGetChunk(second, out Chunk secondChunk));
        Assert.Equal(1, firstChunk.MaterialBuffer[0]);
        Assert.Equal(1, secondChunk.MaterialBuffer[0]);
        Assert.Equal(2, chunks.Count);
    }

    /// <summary>
    /// 验证相位 2 对 1/16/64/256 个后台装载结果只执行一次 live snapshot rebuild。
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(256)]
    public void ApplyPreparedBatchesLoadedChunks(int chunkCount)
    {
        // Arrange：准备后台完成队列与独立温度子块。
        ResidentChunkMap chunks = new();
        ResidencyTable residency = new();
        int chunkBytes = ChunkMemoryBudget.EstimatedResidentChunkBytes;
        ChunkMemoryBudget budget = new(chunkBytes * (chunkCount + 1L), chunkBytes * chunkCount);
        TemperatureField temperature = new();
        CompletedChunkQueue completed = new();
        for (int i = 0; i < chunkCount; i++)
        {
            ChunkCoord coord = new(i, 0);
            Half[] chunkTemperature = new Half[TemperatureField.BlockArea];
            chunkTemperature[0] = (Half)i;
            completed.Enqueue(CompletedStreamingOperation.Loaded(new Chunk(coord), chunkTemperature));
        }

        WorldStreamer streamer = new(
            chunks,
            residency,
            budget,
            temperature,
            new MemoryChunkStore(),
            IdentityRemap(),
            completed: completed);

        // Act：执行相位 2 批量应用。
        Assert.Equal(chunkCount, streamer.ApplyPrepared(frame: 7));

        // Assert：验证 live map、metadata、budget 与单次快照重建保持一致。
        Assert.Equal(chunkCount, chunks.Count);
        Assert.Equal(1, chunks.SnapshotRebuildCount);
        Assert.Equal(chunkCount, residency.Count);
        Assert.Equal(chunkBytes * chunkCount, budget.ResidentBytes);
        for (int i = 0; i < chunkCount; i++)
        {
            ChunkCoord coord = new(i, 0);
            Assert.True(chunks.TryGetChunk(coord, out _));
            Assert.True(residency.TryGetInfo(coord, out ChunkResidencyInfo info));
            Assert.Equal(ChunkResidencyState.Cached, info.State);
        }
    }

    /// <summary>
    /// 验证无请求的相位 11 tick 在预热后不产生托管堆分配。
    /// </summary>
    [Fact]
    public void ProcessIoOnceWithoutRequestsDoesNotAllocateAfterWarmup()
    {
        // Arrange：搭建测试场景与依赖
        WorldStreamer streamer = new(
            new ResidentChunkMap(),
            new ResidencyTable(),
            Budget(),
            new TemperatureField(),
            new MemoryChunkStore(),
            IdentityRemap());

        // Act：执行被测操作
        _ = streamer.ProcessIoOnce();

        long before = GC.GetAllocatedBytesForCurrentThread();
        int processed = 0;
        for (int i = 0; i < 64; i++)
        {
            processed += streamer.ProcessIoOnce();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Assert：验证不变式与预期结果
        Assert.Equal(0, processed);
        Assert.Equal(0, allocated);
    }

    private static void WriteStoredChunk(MemoryChunkStore store, ChunkCoord coord, ushort savedMaterial, Half temp, byte damage = 0)
    {
        Chunk chunk = new(coord);
        chunk.MaterialBuffer[0] = savedMaterial;
        chunk.DamageBuffer[0] = damage;
        Half[] temperature = new Half[TemperatureField.BlockArea];
        temperature[0] = temp;
        ArrayBufferWriter<byte> writer = new();
        new ChunkCodec().Encode(new PixelEngine.Serialization.ChunkSnapshot(coord, chunk.MaterialBuffer, chunk.FlagsBuffer, chunk.LifetimeBuffer, chunk.DamageBuffer, temperature), writer);
        store.Write(coord, writer.WrittenSpan);
    }

    private static void Pump(WorldManager manager, JobSystem jobs, ref long frame, int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            manager.ApplyResidency(frame++);
            _ = manager.Streamer.ProcessIoOnce(jobs);
        }
    }

    private static void AssertNoDetachedResident(WorldManager manager)
    {
        foreach (Chunk chunk in manager.Chunks.ResidentChunks)
        {
            Assert.True(manager.Residency.TryGetInfo(chunk.Coord, out ChunkResidencyInfo info));
            Assert.NotEqual(ChunkResidencyState.Detached, info.State);
        }
    }

    private static MaterialRemap IdentityRemap()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
        MaterialNameTable names = new(materials.BuildIdNameTable());
        return MaterialRemap.Build(names, materials, fallbackId: 0);
    }

    private static MaterialTable Materials(params (string Name, CellType Type)[] definitions)
    {
        MaterialDef[] materials = new MaterialDef[definitions.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = new MaterialDef
            {
                Id = (ushort)i,
                Name = definitions[i].Name,
                Type = definitions[i].Type,
                Density = i == 0 ? (byte)0 : (byte)100,
                HeatCapacity = 1,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            };
        }

        return new MaterialTable(materials);
    }

    private static ChunkMemoryBudget Budget()
    {
        return new ChunkMemoryBudget(
            ChunkMemoryBudget.EstimatedResidentChunkBytes * 8L,
            ChunkMemoryBudget.EstimatedResidentChunkBytes * 4L);
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

    private sealed class RecordingChunkInitializer(ushort material, Half temperature) : IWorldChunkInitializer
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public int LastChunkX { get; private set; }

        public int LastChunkY { get; private set; }

        public long LastOriginX { get; private set; }

        public long LastOriginY { get; private set; }

        public void Initialize(in WorldChunkInitializationContext context)
        {
            _ = Interlocked.Increment(ref _callCount);
            LastChunkX = context.ChunkX;
            LastChunkY = context.ChunkY;
            LastOriginX = context.OriginCellX;
            LastOriginY = context.OriginCellY;
            context.MaterialCells[0] = material;
            context.TemperatureCells[0] = temperature;
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
                "PixelEngine.WorldStreamerTests",
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
