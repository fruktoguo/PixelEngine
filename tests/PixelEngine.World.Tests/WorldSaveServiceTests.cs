using PixelEngine.Serialization;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.World.Tests;

/// <summary>
/// WorldSaveService 整世界存档 / 读档测试。
/// 不变式：整世界存档/读档 round-trip 后材质与温度场一致。
/// </summary>
public sealed class WorldSaveServiceTests
{
    /// <summary>
    /// 验证 v1 存档能力只声明粗粒度快照，不声明帧级 rewind 或 undo。
    /// </summary>
    [Fact]
    public void WorldSaveCapabilitiesExposeOnlyCoarseSnapshotSaves()
    {
        Assert.True(WorldSaveCapabilities.SupportsCoarseSnapshotSaves);
        Assert.False(WorldSaveCapabilities.SupportsFrameRewind);
        Assert.False(WorldSaveCapabilities.SupportsUndo);
    }

    /// <summary>
    /// 验证快照等价不依赖 resident 枚举顺序或 LRU 时间，但会观察 dirty 等后续模拟语义。
    /// </summary>
    [Fact]
    public void SnapshotContentEqualityUsesWorldSemanticsInsteadOfEnumerationOrder()
    {
        WorldSaveService service = new();
        MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
        ChunkCoord firstCoord = new(-1, 2);
        ChunkCoord secondCoord = new(3, 4);
        Chunk firstLeft = CreateSnapshotChunk(firstCoord, material: 1);
        Chunk secondLeft = CreateSnapshotChunk(secondCoord, material: 0);
        Chunk firstRight = CreateSnapshotChunk(firstCoord, material: 1);
        Chunk secondRight = CreateSnapshotChunk(secondCoord, material: 0);
        ResidentChunkMap leftChunks = new();
        leftChunks.Add(firstLeft);
        leftChunks.Add(secondLeft);
        ResidentChunkMap rightChunks = new();
        rightChunks.Add(secondRight);
        rightChunks.Add(firstRight);
        ResidencyTable leftResidency = new();
        leftResidency.Set(
            firstCoord,
            new ChunkResidencyInfo(
                ChunkResidencyState.Active,
                LastTouchedFrame: 10,
                ChunkMemoryBudget.EstimatedResidentChunkBytes,
                DirtySinceLoad: true));
        leftResidency.Set(
            secondCoord,
            new ChunkResidencyInfo(
                ChunkResidencyState.Cached,
                LastTouchedFrame: 11,
                ChunkMemoryBudget.EstimatedResidentChunkBytes,
                DirtySinceLoad: false));
        ResidencyTable rightResidency = new();
        rightResidency.Set(
            firstCoord,
            new ChunkResidencyInfo(
                ChunkResidencyState.Active,
                LastTouchedFrame: 900,
                ChunkMemoryBudget.EstimatedResidentChunkBytes,
                DirtySinceLoad: true));
        rightResidency.Set(
            secondCoord,
            new ChunkResidencyInfo(
                ChunkResidencyState.Cached,
                LastTouchedFrame: 901,
                ChunkMemoryBudget.EstimatedResidentChunkBytes,
                DirtySinceLoad: false));
        TemperatureField leftTemperature = new();
        leftTemperature.AddHeat((firstCoord.X << 6) + 1, (firstCoord.Y << 6) + 2, 18.5f);
        TemperatureField rightTemperature = new();
        rightTemperature.AddHeat((firstCoord.X << 6) + 1, (firstCoord.Y << 6) + 2, 18.5f);
        FakeWorldStateBridge leftState = new(
            [new FreeParticleSnapshot(1, 2, 3, 4, 1, 5, 6)],
            [RigidBody([1])]);
        FakeWorldStateBridge rightState = new(
            [new FreeParticleSnapshot(1, 2, 3, 4, 1, 5, 6)],
            [RigidBody([1])]);
        WorldSaveSnapshot left = service.CaptureSnapshot(
            new WorldSaveContext(
                leftChunks,
                leftResidency,
                leftTemperature,
                materials,
                worldSeed: 123,
                gameTimeTicks: 456,
                playerStateBlob: new byte[] { 7, 8, 9 },
                isFrameBoundary: true),
            leftState,
            CellFlags.Parity);
        WorldSaveSnapshot right = service.CaptureSnapshot(
            new WorldSaveContext(
                rightChunks,
                rightResidency,
                rightTemperature,
                materials,
                worldSeed: 123,
                gameTimeTicks: 456,
                playerStateBlob: new byte[] { 7, 8, 9 },
                isFrameBoundary: true),
            rightState,
            CellFlags.Parity);

        Assert.True(left.ContentEquals(right));
        Assert.True(right.ContentEquals(left));

        firstRight.SetWorkingDirty(DirtyRect.Full);
        WorldSaveSnapshot changed = service.CaptureSnapshot(
            new WorldSaveContext(
                rightChunks,
                rightResidency,
                rightTemperature,
                materials,
                worldSeed: 123,
                gameTimeTicks: 456,
                playerStateBlob: new byte[] { 7, 8, 9 },
                isFrameBoundary: true),
            rightState,
            CellFlags.Parity);
        Assert.False(left.ContentEquals(changed));
    }

    /// <summary>整目录发布必须移除旧 region 与杂项文件，不能把新快照叠写到旧 slot。</summary>
    [Fact]
    public void WriteSnapshotAtomicallyReplacesTheWholeSaveDirectory()
    {
        using TempWorldDirectory save = TempWorldDirectory.Create();
        WorldSaveService service = new();
        MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
        ResidentChunkMap firstChunks = new();
        firstChunks.Add(CreateSnapshotChunk(new ChunkCoord(0, 0), material: 1));
        firstChunks.Add(CreateSnapshotChunk(new ChunkCoord(32, 0), material: 1));
        WorldSaveSnapshot first = service.CaptureSnapshot(
            new WorldSaveContext(
                firstChunks,
                new ResidencyTable(),
                new TemperatureField(),
                materials,
                worldSeed: 1,
                gameTimeTicks: 2,
                playerStateBlob: ReadOnlyMemory<byte>.Empty,
                isFrameBoundary: true),
            new FakeWorldStateBridge([], []));
        WorldSaveWriteResult firstWrite = service.WriteSnapshot(first, save.Path);
        Assert.False(firstWrite.CleanupPending);
        Assert.Equal(2, Directory.EnumerateFiles(
            Path.Combine(save.Path, "regions"),
            "*.rgn").Count());
        File.WriteAllText(Path.Combine(save.Path, "stale.bin"), "stale");

        ResidentChunkMap secondChunks = new();
        secondChunks.Add(CreateSnapshotChunk(new ChunkCoord(0, 0), material: 0));
        WorldSaveSnapshot second = service.CaptureSnapshot(
            new WorldSaveContext(
                secondChunks,
                new ResidencyTable(),
                new TemperatureField(),
                materials,
                worldSeed: 3,
                gameTimeTicks: 4,
                playerStateBlob: ReadOnlyMemory<byte>.Empty,
                isFrameBoundary: true),
            new FakeWorldStateBridge([], []));
        WorldSaveWriteResult secondWrite = service.WriteSnapshot(second, save.Path);

        Assert.False(secondWrite.CleanupPending);
        Assert.False(File.Exists(Path.Combine(save.Path, "stale.bin")));
        _ = Assert.Single(Directory.EnumerateFiles(
            Path.Combine(save.Path, "regions"),
            "*.rgn"));
        WorldLoadResult loaded = service.LoadAll(
            save.Path,
            new WorldLoadContext(
                new ResidentChunkMap(),
                new ResidencyTable(),
                new TemperatureField(),
                materials,
                fallbackMaterialId: 0,
                currentParityBit: 0),
            new FakeWorldStateBridge([], []));
        Assert.Equal(3UL, loaded.WorldSeed);
        Assert.Equal(4L, loaded.GameTimeTicks);
        Assert.Equal(1, loaded.LoadedChunkCount);
    }

    /// <summary>staging 编码失败时旧 slot 必须逐字节保留，并清理未发布 journal。</summary>
    [Fact]
    public void WriteSnapshotEncodingFailurePreservesExistingDirectory()
    {
        using TempWorldDirectory save = TempWorldDirectory.Create();
        WorldSaveService service = new();
        MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
        ResidentChunkMap chunks = new();
        chunks.Add(CreateSnapshotChunk(new ChunkCoord(0, 0), material: 1));
        WorldSaveSnapshot valid = service.CaptureSnapshot(
            new WorldSaveContext(
                chunks,
                new ResidencyTable(),
                new TemperatureField(),
                materials,
                worldSeed: 5,
                gameTimeTicks: 6,
                playerStateBlob: ReadOnlyMemory<byte>.Empty,
                isFrameBoundary: true),
            new FakeWorldStateBridge([], []));
        _ = service.WriteSnapshot(valid, save.Path);
        File.WriteAllText(Path.Combine(save.Path, "sentinel.txt"), "before-image");
        Dictionary<string, byte[]> before = CaptureDirectoryFiles(save.Path);
        WorldManifest invalidManifest = new(
            SaveFormatVersions.WorldManifest + 1,
            valid.WorldSeed,
            valid.GameTimeTicks,
            [],
            new MaterialNameTable(materials.BuildIdNameTable()),
            [],
            [],
            valid.ChunkCoordinates.Span);
        WorldSaveSnapshot invalid = new(
            invalidManifest,
            valid.Chunks.ToArray(),
            valid.CurrentParity,
            materialFallbackHitCount: 0);

        _ = Assert.Throws<InvalidDataException>(() => service.WriteSnapshot(invalid, save.Path));

        Dictionary<string, byte[]> after = CaptureDirectoryFiles(save.Path);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach ((string relativePath, byte[] contents) in before)
        {
            Assert.Equal(contents, after[relativePath]);
        }

        string parent = Path.GetDirectoryName(save.Path)
            ?? throw new InvalidOperationException("临时存档目录缺少父目录。");
        string targetName = Path.GetFileName(save.Path);
        Assert.Empty(Directory.EnumerateDirectories(
            parent,
            $".{targetName}.*.world-save-journal"));
    }

    /// <summary>保存提交与 Undo 必须只切换快照覆盖 chunk 的 streaming dirty before-image。</summary>
    [Fact]
    public void SnapshotPersistenceStateCanBeCommittedAndRestored()
    {
        WorldSaveService service = new();
        MaterialTable materials = Materials(("empty", CellType.Empty));
        ChunkCoord includedCoord = new(0, 0);
        ChunkCoord unrelatedCoord = new(1, 0);
        ResidentChunkMap chunks = new();
        chunks.Add(CreateSnapshotChunk(includedCoord, material: 0));
        ResidencyTable residency = new();
        residency.Set(
            includedCoord,
            new ChunkResidencyInfo(
                ChunkResidencyState.Active,
                LastTouchedFrame: 10,
                ChunkMemoryBudget.EstimatedResidentChunkBytes,
                DirtySinceLoad: true));
        WorldSaveSnapshot snapshot = service.CaptureSnapshot(
            new WorldSaveContext(
                chunks,
                residency,
                new TemperatureField(),
                materials,
                worldSeed: 1,
                gameTimeTicks: 2,
                playerStateBlob: ReadOnlyMemory<byte>.Empty,
                isFrameBoundary: true),
            new FakeWorldStateBridge([], []));
        residency.Set(
            unrelatedCoord,
            new ChunkResidencyInfo(
                ChunkResidencyState.Cached,
                LastTouchedFrame: 20,
                ChunkMemoryBudget.EstimatedResidentChunkBytes,
                DirtySinceLoad: true));

        Assert.True(WorldSaveService.MarkSnapshotPersisted(residency, snapshot));
        Assert.False(WorldSaveService.MarkSnapshotPersisted(residency, snapshot));
        Assert.True(residency.TryGetInfo(includedCoord, out ChunkResidencyInfo persisted));
        Assert.False(persisted.DirtySinceLoad);
        Assert.True(residency.TryGetInfo(unrelatedCoord, out ChunkResidencyInfo unrelated));
        Assert.True(unrelated.DirtySinceLoad);

        Assert.True(WorldSaveService.RestoreSnapshotPersistenceState(residency, snapshot));
        Assert.False(WorldSaveService.RestoreSnapshotPersistenceState(residency, snapshot));
        Assert.True(residency.TryGetInfo(includedCoord, out ChunkResidencyInfo restored));
        Assert.True(restored.DirtySinceLoad);
        Assert.True(residency.TryGetInfo(unrelatedCoord, out unrelated));
        Assert.True(unrelated.DirtySinceLoad);
    }

    /// <summary>
    /// 验证存档写入 manifest、chunk blob 与状态快照，读档时按 material name 重映射并恢复温度。
    /// </summary>
    [Fact]
    public void SaveAllAndLoadAllRoundTripsWorldStateWithMaterialRemap()
    {
        // Arrange：准备输入与初始状态
        using TempWorldDirectory save = TempWorldDirectory.Create();
        WorldSaveService service = new();
        ResidentChunkMap sourceChunks = new();
        ResidencyTable sourceResidency = new();
        TemperatureField sourceTemperature = new();
        MaterialTable savedMaterials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder), ("water", CellType.Liquid));
        ChunkCoord coord = new(-1, 2);
        Chunk sourceChunk = new(coord);
        int local = CellAddressing.LocalIndexFromLocal(4, 8);
        sourceChunk.MaterialBuffer[local] = 1;
        sourceChunk.LifetimeBuffer[local] = 9;
        sourceChunk.DamageBuffer[local] = 4;
        sourceChunks.Add(sourceChunk);
        sourceResidency.Set(coord, new ChunkResidencyInfo(ChunkResidencyState.Active, 7, ChunkMemoryBudget.EstimatedResidentChunkBytes, DirtySinceLoad: true));
        int worldX = (coord.X << 6) + 4;
        int worldY = (coord.Y << 6) + 8;
        sourceTemperature.AddHeat(worldX, worldY, 37.5f);
        FakeWorldStateBridge state = new(
            [new FreeParticleSnapshot(1, 2, 3, 4, 1, 5, 6)],
            [RigidBody(material: [1, 0])]);
        WorldSaveContext saveContext = new(
            sourceChunks,
            sourceResidency,
            sourceTemperature,
            savedMaterials,
            worldSeed: 1234,
            gameTimeTicks: 5678,
            playerStateBlob: new byte[] { 7, 8, 9 },
            isFrameBoundary: true);

        _ = service.SaveAll(saveContext, state, save.Path);

        // Assert：验证预期结果
        Assert.True(File.Exists(Path.Combine(save.Path, "manifest.bin")));
        Assert.True(sourceResidency.TryGetInfo(coord, out ChunkResidencyInfo flushed));
        Assert.False(flushed.DirtySinceLoad);

        ResidentChunkMap loadedChunks = new();
        ResidencyTable loadedResidency = new();
        TemperatureField loadedTemperature = new();
        MaterialTable currentMaterials = Materials(("empty", CellType.Empty), ("water", CellType.Liquid), ("sand", CellType.Powder));
        FakeWorldStateBridge restored = new([], []);
        WorldLoadContext loadContext = new(
            loadedChunks,
            loadedResidency,
            loadedTemperature,
            currentMaterials,
            fallbackMaterialId: 0,
            currentParityBit: CellFlags.Parity);

        WorldLoadResult result = service.LoadAll(save.Path, loadContext, restored);

        Assert.Equal(1234UL, result.WorldSeed);
        Assert.Equal(5678L, result.GameTimeTicks);
        Assert.Equal(1, result.LoadedChunkCount);
        Assert.Equal(0, result.MaterialFallbackHitCount);
        Assert.True(loadedChunks.TryGetChunk(coord, out Chunk loadedChunk));
        Assert.Equal(2, loadedChunk.MaterialBuffer[local]);
        Assert.Equal(9, loadedChunk.LifetimeBuffer[local]);
        Assert.Equal(4, loadedChunk.DamageBuffer[local]);
        Assert.Equal(DirtyRect.Full, loadedChunk.CurrentDirty);
        Assert.Equal(37.5f, loadedTemperature.GetTemperature(worldX, worldY));
        Assert.True(loadedResidency.TryGetInfo(coord, out ChunkResidencyInfo loadedInfo));
        Assert.Equal(ChunkResidencyState.Cached, loadedInfo.State);
        Assert.False(loadedInfo.DirtySinceLoad);
        Assert.Equal((ushort)2, restored.RestoredParticles[0].Material);
        Assert.Equal([2, 0], restored.RestoredBodies[0].Material.ToArray());
    }

    /// <summary>
    /// 验证读档时缺失 chunk blob 明确失败。
    /// </summary>
    [Fact]
    public void LoadAllRejectsMissingChunkBlob()
    {
        // Arrange：准备输入与初始状态
        using TempWorldDirectory save = TempWorldDirectory.Create();
        WorldSaveService service = new();
        ResidentChunkMap chunks = new();
        ResidencyTable residency = new();
        TemperatureField temperature = new();
        MaterialTable materials = Materials(("empty", CellType.Empty));
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunks.Add(chunk);
        FakeWorldStateBridge state = new([], []);
        _ = service.SaveAll(
            new WorldSaveContext(chunks, residency, temperature, materials, 1, 2, ReadOnlyMemory<byte>.Empty, isFrameBoundary: true),
            state,
            save.Path);
        File.Delete(Path.Combine(save.Path, "regions", "r.0.0.rgn"));

        // Assert：验证预期结果
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            service.LoadAll(
                save.Path,
                new WorldLoadContext(new ResidentChunkMap(), new ResidencyTable(), new TemperatureField(), materials, 0, CellFlags.Parity),
                state));

        Assert.Contains("缺失 chunk", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证读档时被删除的材质在 chunk、粒子与刚体里统一走 fallback 并计数。
    /// </summary>
    [Fact]
    public void LoadAllRemapsDeletedMaterialsToFallback()
    {
        // Arrange：准备输入与初始状态
        using TempWorldDirectory save = TempWorldDirectory.Create();
        WorldSaveService service = new();
        MaterialTable savedMaterials = Materials(("empty", CellType.Empty), ("deleted", CellType.Solid));
        ResidentChunkMap sourceChunks = new();
        ResidencyTable sourceResidency = new();
        TemperatureField sourceTemperature = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunk.MaterialBuffer[0] = 1;
        sourceChunks.Add(chunk);
        FakeWorldStateBridge state = new(
            [new FreeParticleSnapshot(0, 0, 0, 0, 1, 0, 1)],
            [RigidBody(material: [1])]);
        _ = service.SaveAll(
            new WorldSaveContext(sourceChunks, sourceResidency, sourceTemperature, savedMaterials, 1, 2, ReadOnlyMemory<byte>.Empty, isFrameBoundary: true),
            state,
            save.Path);

        MaterialTable currentMaterials = Materials(("empty", CellType.Empty));
        ResidentChunkMap loadedChunks = new();
        FakeWorldStateBridge restored = new([], []);
        WorldLoadResult result = service.LoadAll(
            save.Path,
            new WorldLoadContext(loadedChunks, new ResidencyTable(), new TemperatureField(), currentMaterials, 0, CellFlags.Parity),
            restored);

        // Assert：验证预期结果
        Assert.Equal(3, result.MaterialFallbackHitCount);
        Assert.True(loadedChunks.TryGetChunk(new ChunkCoord(0, 0), out Chunk loadedChunk));
        Assert.Equal(0, loadedChunk.MaterialBuffer[0]);
        Assert.Equal((ushort)0, restored.RestoredParticles[0].Material);
        Assert.Equal([0], restored.RestoredBodies[0].Material.ToArray());
    }

    /// <summary>
    /// 验证 SaveAll 拒绝非帧边界上下文，避免读取半更新世界。
    /// </summary>
    [Fact]
    public void SaveAllRejectsNonFrameBoundaryContext()
    {
        using TempWorldDirectory save = TempWorldDirectory.Create();
        WorldSaveService service = new();
        MaterialTable materials = Materials(("empty", CellType.Empty));
        WorldSaveContext context = new(
            new ResidentChunkMap(),
            new ResidencyTable(),
            new TemperatureField(),
            materials,
            worldSeed: 1,
            gameTimeTicks: 2,
            playerStateBlob: ReadOnlyMemory<byte>.Empty,
            isFrameBoundary: false);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.SaveAll(context, new FakeWorldStateBridge([], []), save.Path));

        Assert.Contains("相位 2", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>损坏存档在完整解码前失败，不得清空 live chunk、温度或全局态。</summary>
    [Fact]
    public void LoadAllMissingChunkPreservesLiveWorld()
    {
        using TempWorldDirectory save = TempWorldDirectory.Create();
        WorldSaveService service = new();
        MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
        ResidentChunkMap savedChunks = new();
        Chunk savedChunk = new(new ChunkCoord(0, 0));
        savedChunk.MaterialBuffer[0] = 1;
        savedChunks.Add(savedChunk);
        _ = service.SaveAll(
            new WorldSaveContext(
                savedChunks,
                new ResidencyTable(),
                new TemperatureField(),
                materials,
                7,
                8,
                ReadOnlyMemory<byte>.Empty,
                isFrameBoundary: true),
            new FakeWorldStateBridge([], []),
            save.Path);
        File.Delete(Path.Combine(save.Path, "regions", "r.0.0.rgn"));

        ResidentChunkMap liveChunks = new();
        Chunk liveChunk = new(new ChunkCoord(2, 3));
        liveChunk.MaterialBuffer[9] = 1;
        liveChunks.Add(liveChunk);
        TemperatureField liveTemperature = new();
        liveTemperature.AddHeat((2 << 6) + 1, (3 << 6) + 1, 42f);
        FakeWorldStateBridge liveState = new([], []);

        _ = Assert.Throws<InvalidDataException>(() => service.LoadAll(
            save.Path,
            new WorldLoadContext(
                liveChunks,
                new ResidencyTable(),
                liveTemperature,
                materials,
                0,
                CellFlags.Parity),
            liveState));

        Assert.True(liveChunks.TryGetChunk(new ChunkCoord(2, 3), out Chunk preserved));
        Assert.Equal(1, preserved.MaterialBuffer[9]);
        Assert.Equal(42f, liveTemperature.GetTemperature((2 << 6) + 1, (3 << 6) + 1));
    }

    /// <summary>重复应用同一不可变快照可恢复 world，并清除目标中快照未包含的旧温度 block。</summary>
    [Fact]
    public void ApplySnapshotCanRepeatAndClearsStaleTemperature()
    {
        WorldSaveService service = new();
        MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
        ResidentChunkMap sourceChunks = new();
        ChunkCoord sourceCoord = new(0, 0);
        Chunk sourceChunk = new(sourceCoord);
        sourceChunk.MaterialBuffer[0] = 1;
        sourceChunks.Add(sourceChunk);
        TemperatureField sourceTemperature = new();
        sourceTemperature.AddHeat(1, 1, 17f);
        WorldSaveSnapshot snapshot = service.CaptureSnapshot(
            new WorldSaveContext(
                sourceChunks,
                new ResidencyTable(),
                sourceTemperature,
                materials,
                33,
                44,
                ReadOnlyMemory<byte>.Empty,
                isFrameBoundary: true),
            new FakeWorldStateBridge([], []),
            CellFlags.Parity);

        ResidentChunkMap targetChunks = new();
        TemperatureField targetTemperature = new();
        targetTemperature.AddHeat(129, 129, 99f);
        WorldLoadContext target = new(
            targetChunks,
            new ResidencyTable(),
            targetTemperature,
            materials,
            0,
            0);
        FakeWorldStateBridge targetState = new([], []);

        WorldLoadResult first = service.ApplySnapshot(snapshot, target, targetState);
        Assert.True(targetChunks.TryGetChunk(sourceCoord, out Chunk firstChunk));
        firstChunk.MaterialBuffer[0] = 0;
        targetTemperature.AddHeat(1, 1, 25f);
        WorldLoadResult repeated = service.ApplySnapshot(snapshot, target, targetState);

        Assert.Equal(first, repeated);
        Assert.True(targetChunks.TryGetChunk(sourceCoord, out Chunk restored));
        Assert.Equal(1, restored.MaterialBuffer[0]);
        Assert.Equal(17f, targetTemperature.GetTemperature(1, 1));
        Assert.Equal(0f, targetTemperature.GetTemperature(129, 129));
    }

    private static RigidBodySnapshot RigidBody(ushort[] material)
    {
        return new RigidBodySnapshot(
            id: 11,
            width: material.Length,
            height: 1,
            bodyLocalMask: Enumerable.Repeat((byte)1, material.Length).ToArray(),
            material,
            posX: 0,
            posY: 0,
            rotCos: 1,
            rotSin: 0,
            linVelX: 0,
            linVelY: 0,
            angVel: 0);
    }

    private static Chunk CreateSnapshotChunk(ChunkCoord coord, ushort material)
    {
        Chunk chunk = new(coord);
        chunk.MaterialBuffer[0] = material;
        chunk.LifetimeBuffer[0] = material == 0 ? (byte)0 : (byte)17;
        chunk.SetCurrentDirty(new DirtyRect(0, 0, 1, 1));
        chunk.MarkIncomingDirty(3, new DirtyRect(2, 2, 3, 3));
        chunk.Parity = CellFlags.Parity;
        return chunk;
    }

    private static Dictionary<string, byte[]> CaptureDirectoryFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);
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

    private sealed class FakeWorldStateBridge(
        FreeParticleSnapshot[] particles,
        RigidBodySnapshot[] bodies) : IWorldStateSnapshotSource, IWorldStateSnapshotSink
    {
        public int FreeParticleCount => particles.Length;

        public int RigidBodyCount => bodies.Length;

        public FreeParticleSnapshot[] RestoredParticles { get; private set; } = [];

        public RigidBodySnapshot[] RestoredBodies { get; private set; } = [];

        public void CopyFreeParticles(Span<FreeParticleSnapshot> destination)
        {
            particles.CopyTo(destination);
        }

        public void CopyRigidBodies(Span<RigidBodySnapshot> destination)
        {
            bodies.CopyTo(destination);
        }

        public void RestoreFreeParticles(ReadOnlySpan<FreeParticleSnapshot> restoredParticles)
        {
            RestoredParticles = restoredParticles.ToArray();
        }

        public void RestoreRigidBodies(ReadOnlySpan<RigidBodySnapshot> restoredBodies)
        {
            RestoredBodies = restoredBodies.ToArray();
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
                "PixelEngine.WorldSaveServiceTests",
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
