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
        sourceChunk.Material[local] = 1;
        sourceChunk.Lifetime[local] = 9;
        sourceChunk.Damage[local] = 4;
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

        service.SaveAll(saveContext, state, save.Path);

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
        Assert.Equal(2, loadedChunk.Material[local]);
        Assert.Equal(9, loadedChunk.Lifetime[local]);
        Assert.Equal(4, loadedChunk.Damage[local]);
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
        service.SaveAll(
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
        chunk.Material[0] = 1;
        sourceChunks.Add(chunk);
        FakeWorldStateBridge state = new(
            [new FreeParticleSnapshot(0, 0, 0, 0, 1, 0, 1)],
            [RigidBody(material: [1])]);
        service.SaveAll(
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
        Assert.Equal(0, loadedChunk.Material[0]);
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
