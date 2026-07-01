using PixelEngine.Simulation;
using PixelEngine.World;
using Xunit;

namespace PixelEngine.Serialization.Tests;

/// <summary>
/// plan/14 整世界 save→load 往返验收测试。
/// </summary>
public sealed class SaveLoadRoundTripTests
{
    /// <summary>
    /// 验证存档往返保持 chunk 持久字段、温度、粒子与刚体，并在读档时重置瞬时 flags 与按 name 重映射材质。
    /// </summary>
    [Fact]
    public void SaveLoadRoundTripPreservesPersistentWorldStateAndResetsTransientFlags()
    {
        using TempWorldDirectory save = TempWorldDirectory.Create();
        WorldSaveService service = new();
        ResidentChunkMap sourceChunks = new();
        ResidencyTable sourceResidency = new();
        TemperatureField sourceTemperature = new();
        MaterialTable savedMaterials = Materials(
            ("empty", CellType.Empty),
            ("sand", CellType.Powder),
            ("water", CellType.Liquid));
        ChunkCoord coord = new(-2, 3);
        Chunk sourceChunk = CreateSourceChunk(coord);
        int local = CellAddressing.LocalIndexFromLocal(5, 9);
        sourceChunks.Add(sourceChunk);
        sourceResidency.Set(
            coord,
            new ChunkResidencyInfo(ChunkResidencyState.Active, 17, ChunkMemoryBudget.EstimatedResidentChunkBytes, DirtySinceLoad: true));
        int worldX = (coord.X << 6) + 5;
        int worldY = (coord.Y << 6) + 9;
        sourceTemperature.AddHeat(worldX, worldY, 42.5f);
        FakeWorldStateBridge sourceState = new(
            [new FreeParticleSnapshot(1, 2, 3, 4, 1, 7, 99)],
            [RigidBody(material: [1, 2, 0])]);

        service.SaveAll(
            new WorldSaveContext(
                sourceChunks,
                sourceResidency,
                sourceTemperature,
                savedMaterials,
                worldSeed: 0x1234UL,
                gameTimeTicks: 9876,
                playerStateBlob: new byte[] { 9, 8, 7 },
                isFrameBoundary: true),
            sourceState,
            save.Path);

        Assert.True(File.Exists(Path.Combine(save.Path, "manifest.bin")));
        Assert.True(sourceResidency.TryGetInfo(coord, out ChunkResidencyInfo flushed));
        Assert.False(flushed.DirtySinceLoad);

        ResidentChunkMap loadedChunks = new();
        TemperatureField loadedTemperature = new();
        FakeWorldStateBridge restored = new([], []);
        MaterialTable currentMaterials = Materials(
            ("empty", CellType.Empty),
            ("water", CellType.Liquid),
            ("sand", CellType.Powder),
            ("lava", CellType.Liquid));

        WorldLoadResult result = service.LoadAll(
            save.Path,
            new WorldLoadContext(
                loadedChunks,
                new ResidencyTable(),
                loadedTemperature,
                currentMaterials,
                fallbackMaterialId: 0,
                currentParityBit: CellFlags.Parity),
            restored);

        Assert.Equal(0x1234UL, result.WorldSeed);
        Assert.Equal(9876, result.GameTimeTicks);
        Assert.Equal(1, result.LoadedChunkCount);
        Assert.Equal(0, result.MaterialFallbackHitCount);
        Assert.True(loadedChunks.TryGetChunk(coord, out Chunk loadedChunk));
        Assert.Equal((ushort)2, loadedChunk.Material[local]);
        Assert.Equal((byte)123, loadedChunk.Lifetime[local]);
        Assert.Equal(CellFlags.Burning, loadedChunk.Flags[local]);
        Assert.Equal(DirtyRect.Full, loadedChunk.CurrentDirty);
        Assert.Equal(42.5f, loadedTemperature.GetTemperature(worldX, worldY));
        Assert.Equal((ushort)2, restored.RestoredParticles[0].Material);
        Assert.Equal([2, 1, 0], restored.RestoredBodies[0].Material.ToArray());
    }

    private static Chunk CreateSourceChunk(ChunkCoord coord)
    {
        Chunk chunk = new(coord);
        int local = CellAddressing.LocalIndexFromLocal(5, 9);
        chunk.Material[local] = 1;
        chunk.Lifetime[local] = 123;
        chunk.Flags[local] = CellFlags.Burning |
            CellFlags.Parity |
            CellFlags.Settled |
            CellFlags.FreeFalling |
            CellFlags.RigidOwned;
        return chunk;
    }

    private static RigidBodySnapshot RigidBody(ushort[] material)
    {
        return new RigidBodySnapshot(
            id: 7,
            width: material.Length,
            height: 1,
            bodyLocalMask: Enumerable.Repeat((byte)1, material.Length).ToArray(),
            material,
            posX: 11,
            posY: 12,
            rotCos: 1,
            rotSin: 0,
            linVelX: 3,
            linVelY: 4,
            angVel: 5);
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
                "PixelEngine.SaveLoadRoundTripTests",
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
