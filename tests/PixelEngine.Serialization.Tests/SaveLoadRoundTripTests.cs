using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PixelEngine.Core;
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

        _ = service.SaveAll(
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
        Assert.Equal((ushort)2, loadedChunk.MaterialBuffer[local]);
        Assert.Equal((byte)123, loadedChunk.LifetimeBuffer[local]);
        Assert.Equal((byte)77, loadedChunk.DamageBuffer[local]);
        Assert.Equal(CellFlags.Burning, loadedChunk.FlagsBuffer[local]);
        Assert.Equal(DirtyRect.Full, loadedChunk.CurrentDirty);
        Assert.Equal(42.5f, loadedTemperature.GetTemperature(worldX, worldY));
        Assert.Equal((ushort)2, restored.RestoredParticles[0].Material);
        Assert.Equal([2, 1, 0], restored.RestoredBodies[0].Material.ToArray());
    }

    /// <summary>
    /// 验证整世界读档材质 remap 命中 fallback 时，只清空对应 cell 的 Damage，保留成功 remap cell 的 Damage。
    /// </summary>
    [Fact]
    public void SaveLoadRoundTripClearsDamageOnlyForMaterialFallbackCells()
    {
        using TempWorldDirectory save = TempWorldDirectory.Create();
        WorldSaveService service = new();
        ResidentChunkMap sourceChunks = new();
        ResidencyTable sourceResidency = new();
        TemperatureField sourceTemperature = new();
        MaterialTable savedMaterials = Materials(
            ("empty", CellType.Empty),
            ("sand", CellType.Powder),
            ("missing_ore", CellType.Solid),
            ("stone", CellType.Solid));
        ChunkCoord coord = new(1, 1);
        Chunk sourceChunk = new(coord);
        int keptLocal = CellAddressing.LocalIndexFromLocal(3, 4);
        int missingLocal = CellAddressing.LocalIndexFromLocal(5, 6);
        sourceChunk.MaterialBuffer[keptLocal] = 1;
        sourceChunk.DamageBuffer[keptLocal] = 41;
        sourceChunk.MaterialBuffer[missingLocal] = 2;
        sourceChunk.DamageBuffer[missingLocal] = 93;
        sourceChunks.Add(sourceChunk);
        sourceResidency.Set(
            coord,
            new ChunkResidencyInfo(
                ChunkResidencyState.Active,
                1,
                ChunkMemoryBudget.EstimatedResidentChunkBytes,
                DirtySinceLoad: true));

        _ = service.SaveAll(
            new WorldSaveContext(
                sourceChunks,
                sourceResidency,
                sourceTemperature,
                savedMaterials,
                worldSeed: 1,
                gameTimeTicks: 2,
                playerStateBlob: Array.Empty<byte>(),
                isFrameBoundary: true),
            new FakeWorldStateBridge([], []),
            save.Path);

        ResidentChunkMap loadedChunks = new();
        WorldLoadResult result = service.LoadAll(
            save.Path,
            new WorldLoadContext(
                loadedChunks,
                new ResidencyTable(),
                new TemperatureField(),
                Materials(
                    ("empty", CellType.Empty),
                    ("stone", CellType.Solid),
                    ("sand", CellType.Powder)),
                fallbackMaterialId: 0,
                currentParityBit: 0),
            new FakeWorldStateBridge([], []));

        Assert.Equal(1, result.MaterialFallbackHitCount);
        Assert.True(loadedChunks.TryGetChunk(coord, out Chunk loadedChunk));
        Assert.Equal((ushort)2, loadedChunk.MaterialBuffer[keptLocal]);
        Assert.Equal((byte)41, loadedChunk.DamageBuffer[keptLocal]);
        Assert.Equal((ushort)0, loadedChunk.MaterialBuffer[missingLocal]);
        Assert.Equal((byte)0, loadedChunk.DamageBuffer[missingLocal]);
    }

    /// <summary>
    /// 验证引入 Damage 平面前的 v2 manifest + v1 chunk blob 经整世界 LoadAll 迁移为 Damage=0。
    /// </summary>
    [Fact]
    public void LoadAllFromPreDamageLaneChunkBlobInitializesDamageToZero()
    {
        using TempWorldDirectory save = TempWorldDirectory.Create();
        ChunkCoord coord = new(-1, 2);
        ushort[] material = new ushort[EngineConstants.ChunkArea];
        byte[] flags = new byte[EngineConstants.ChunkArea];
        byte[] lifetime = new byte[EngineConstants.ChunkArea];
        byte[] damage = new byte[EngineConstants.ChunkArea];
        Half[] temperature = new Half[ChunkSnapshot.TemperatureCellCount];
        int local = CellAddressing.LocalIndexFromLocal(7, 8);
        material[local] = 1;
        lifetime[local] = 22;
        damage[local] = 255;
        temperature[0] = (Half)13.5f;
        WritePreDamageLaneSave(save.Path, coord, material, flags, lifetime, temperature);

        ResidentChunkMap loadedChunks = new();
        TemperatureField loadedTemperature = new();
        WorldLoadResult result = new WorldSaveService().LoadAll(
            save.Path,
            new WorldLoadContext(
                loadedChunks,
                new ResidencyTable(),
                loadedTemperature,
                Materials(("empty", CellType.Empty), ("sand", CellType.Powder)),
                fallbackMaterialId: 0,
                currentParityBit: CellFlags.Parity),
            new FakeWorldStateBridge([], []));

        Assert.Equal(1, result.LoadedChunkCount);
        Assert.Equal(0, result.MaterialFallbackHitCount);
        Assert.True(loadedChunks.TryGetChunk(coord, out Chunk loadedChunk));
        Assert.Equal((ushort)1, loadedChunk.MaterialBuffer[local]);
        Assert.Equal((byte)22, loadedChunk.LifetimeBuffer[local]);
        Assert.All(loadedChunk.DamageBuffer, value => Assert.Equal(0, value));
        Assert.Equal(13.5f, loadedTemperature.GetTemperature(coord.X << 6, coord.Y << 6));
    }

    private static Chunk CreateSourceChunk(ChunkCoord coord)
    {
        Chunk chunk = new(coord);
        int local = CellAddressing.LocalIndexFromLocal(5, 9);
        chunk.MaterialBuffer[local] = 1;
        chunk.LifetimeBuffer[local] = 123;
        chunk.DamageBuffer[local] = 77;
        chunk.FlagsBuffer[local] = CellFlags.Burning |
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

    private static void WritePreDamageLaneSave(
        string savePath,
        ChunkCoord coord,
        ushort[] material,
        byte[] flags,
        byte[] lifetime,
        Half[] temperature)
    {
        WorldManifest manifest = new(
            SaveFormatVersions.WorldManifest,
            worldSeed: 5,
            gameTimeTicks: 6,
            playerStateBlob: [],
            materialNames: new MaterialNameTable([(0, "empty"), (1, "sand")]),
            freeParticles: [],
            rigidBodies: [],
            chunkIndex: [coord]);
        ArrayBufferWriter<byte> manifestBuffer = new();
        new ManifestCodec().Encode(manifest, manifestBuffer);
        byte[] manifestBytes = manifestBuffer.WrittenSpan.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(manifestBytes.AsSpan(4, 4), SaveFormatVersions.WorldManifestBeforeDamageLane);
        File.WriteAllBytes(Path.Combine(savePath, "manifest.bin"), manifestBytes);

        ArrayBufferWriter<byte> chunkBuffer = new();
        byte[] noDamage = new byte[EngineConstants.ChunkArea];
        WriteLegacyV1ChunkBlob(new PixelEngine.Serialization.ChunkSnapshot(coord, material, flags, lifetime, noDamage, temperature), chunkBuffer);
        new RegionFileStore(savePath).Write(coord, chunkBuffer.WrittenSpan);
    }

    private static void WriteLegacyV1ChunkBlob(ChunkSnapshot snapshot, IBufferWriter<byte> writer)
    {
        PooledByteBufferWriter payload = new();
        try
        {
            RleCodec.EncodeU16(snapshot.Material, payload);
            int materialBytes = payload.WrittenCount;

            byte[] persistentFlags = new byte[snapshot.Flags.Length];
            snapshot.Flags.CopyTo(persistentFlags);
            PersistentCellFlags.StripTransientInPlace(persistentFlags);
            RleCodec.EncodeU8(persistentFlags, payload);
            int flagsBytes = payload.WrittenCount - materialBytes;

            RleCodec.EncodeU8(snapshot.Lifetime, payload);
            int lifetimeBytes = payload.WrittenCount - materialBytes - flagsBytes;

            ReadOnlySpan<byte> tempBytes = MemoryMarshal.AsBytes(snapshot.Temperature);
            Span<byte> tempDestination = payload.GetSpan(tempBytes.Length);
            tempBytes.CopyTo(tempDestination);
            payload.Advance(tempBytes.Length);
            int temperatureBytes = payload.WrittenCount - materialBytes - flagsBytes - lifetimeBytes;

            ChunkBlobHeader header = new(
                1,
                snapshot.Coord,
                materialBytes,
                flagsBytes,
                lifetimeBytes,
                0,
                temperatureBytes,
                payload.WrittenCount);
            Span<byte> headerBytes = writer.GetSpan(ChunkBlobHeader.LegacySize);
            header.Write(headerBytes);
            writer.Advance(ChunkBlobHeader.LegacySize);
            Lz4BlockCodec.Compress(payload.WrittenSpan, writer);
        }
        finally
        {
            payload.Dispose();
        }
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
