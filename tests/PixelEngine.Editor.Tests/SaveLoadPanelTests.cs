using PixelEngine.Serialization;
using PixelEngine.Simulation;
using PixelEngine.World;
using Xunit;
using RigidBodySnapshot = PixelEngine.Serialization.RigidBodySnapshot;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 存读档面板测试。
/// </summary>
public sealed class SaveLoadPanelTests
{
    /// <summary>
    /// 验证面板调用存读档服务并刷新状态。
    /// </summary>
    [Fact]
    public void SaveLoadPanelDelegatesSaveLoadAndListsSlots()
    {
        RecordingSaveLoadService service = new();
        SaveLoadPanel panel = new(service);

        SaveLoadOperationResult saved = panel.Save("slot-a");
        SaveLoadOperationResult loaded = panel.Load("slot-a");

        Assert.True(saved.Success);
        Assert.True(loaded.Success);
        Assert.Equal(["save:slot-a", "list", "load:slot-a", "list"], service.Calls);
        Assert.Equal("loaded slot-a", panel.Status);
        _ = Assert.Single(panel.LastSlots);
    }

    /// <summary>
    /// 验证 WorldSaveLoadPanelService 经真实 WorldSaveService 保存、列出、加载存档点。
    /// </summary>
    [Fact]
    public void WorldSaveLoadPanelServiceSavesListsAndLoadsThroughWorldSaveService()
    {
        string root = Path.Combine(Path.GetTempPath(), "pixelengine-saves-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        try
        {
            MaterialTable materials = CreateMaterials();
            ResidentChunkMap sourceChunks = new();
            ResidencyTable sourceResidency = new();
            TemperatureField sourceTemperature = new();
            Chunk chunk = new(new ChunkCoord(0, 0));
            chunk.Material[0] = 1;
            sourceChunks.Add(chunk);
            sourceResidency.Set(chunk.Coord, new ChunkResidencyInfo(ChunkResidencyState.Active, 0, ChunkMemoryBudget.EstimatedResidentChunkBytes, DirtySinceLoad: true));

            ResidentChunkMap loadedChunks = new();
            FakeRuntime runtime = new(
                new WorldSaveContext(sourceChunks, sourceResidency, sourceTemperature, materials, 99, 123, ReadOnlyMemory<byte>.Empty, isFrameBoundary: true),
                new WorldLoadContext(loadedChunks, new ResidencyTable(), new TemperatureField(), materials, 0, CellFlags.Parity));
            WorldSaveLoadPanelService service = new(root, runtime);

            SaveLoadOperationResult save = service.Save("slot one");
            IReadOnlyList<SaveSlotInfo> slots = service.ListSaveSlots();
            SaveLoadOperationResult load = service.Load("slot-one");

            Assert.True(save.Success);
            SaveSlotInfo slot = Assert.Single(slots);
            Assert.Equal("slot-one", slot.Id);
            Assert.Equal(99UL, slot.WorldSeed);
            Assert.Equal(123L, slot.GameTimeTicks);
            Assert.True(load.Success);
            _ = Assert.NotNull(load.LoadResult);
            Assert.Equal(0, load.LoadResult.Value.MaterialFallbackHitCount);
            Assert.True(loadedChunks.TryGetChunk(new ChunkCoord(0, 0), out Chunk loaded));
            Assert.Equal(1, loaded.Material[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MaterialTable CreateMaterials()
    {
        return new MaterialTable(
        [
            new MaterialDef { Id = 0, Name = "empty", Type = CellType.Empty, HeatCapacity = 1f, TextureId = -1 },
            new MaterialDef { Id = 1, Name = "sand", Type = CellType.Powder, HeatCapacity = 1f, TextureId = -1 },
        ]);
    }

    private sealed class RecordingSaveLoadService : ISaveLoadService
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<SaveSlotInfo> ListSaveSlots()
        {
            Calls.Add("list");
            return [new SaveSlotInfo("slot-a", "slot-a", DateTimeOffset.UnixEpoch, 1, 7, 8, 9)];
        }

        public SaveLoadOperationResult Save(string slotId)
        {
            Calls.Add("save:" + slotId);
            return new SaveLoadOperationResult(true, "saved " + slotId, null, null);
        }

        public SaveLoadOperationResult Load(string slotId)
        {
            Calls.Add("load:" + slotId);
            return new SaveLoadOperationResult(true, "loaded " + slotId, null, new WorldLoadResult(1, 2, 3, 4));
        }
    }

    private sealed class FakeRuntime(
        WorldSaveContext saveContext,
        WorldLoadContext loadContext) : IWorldSaveLoadRuntime, IWorldStateSnapshotSource, IWorldStateSnapshotSink
    {
        public int FreeParticleCount => 0;

        public int RigidBodyCount => 0;

        public IWorldStateSnapshotSource StateSource => this;

        public IWorldStateSnapshotSink StateSink => this;

        public WorldSaveContext CreateSaveContext()
        {
            return saveContext;
        }

        public WorldLoadContext CreateLoadContext()
        {
            return loadContext;
        }

        public void CopyFreeParticles(Span<FreeParticleSnapshot> destination)
        {
        }

        public void CopyRigidBodies(Span<RigidBodySnapshot> destination)
        {
        }

        public void RestoreFreeParticles(ReadOnlySpan<FreeParticleSnapshot> particles)
        {
        }

        public void RestoreRigidBodies(ReadOnlySpan<RigidBodySnapshot> bodies)
        {
        }
    }
}
