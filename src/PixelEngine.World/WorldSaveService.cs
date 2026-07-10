using System.Buffers;
using PixelEngine.Serialization;
using PixelEngine.Simulation;
using SerializedChunkSnapshot = PixelEngine.Serialization.ChunkSnapshot;

namespace PixelEngine.World;

/// <summary>
/// 显式整世界存档 / 读档服务。调用者必须在相位 2 或暂停点使用，避免读取半更新 live map。
/// </summary>
/// <param name="chunkCodec">chunk blob 编解码器；null 时使用默认实现。</param>
/// <param name="manifestCodec">world manifest 编解码器；null 时使用默认实现。</param>
public sealed class WorldSaveService(ChunkCodec? chunkCodec = null, ManifestCodec? manifestCodec = null)
{
    private const string ManifestFileName = "manifest.bin";

    private readonly ChunkCodec _chunkCodec = chunkCodec ?? new ChunkCodec();
    private readonly ManifestCodec _manifestCodec = manifestCodec ?? new ManifestCodec();

    /// <summary>
    /// 保存当前 resident chunks 与全局态到 savePath。
    /// </summary>
    public void SaveAll(WorldSaveContext world, IWorldStateSnapshotSource stateSource, string savePath)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(stateSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);
        if (!world.IsFrameBoundary)
        {
            throw new InvalidOperationException("WorldSaveService.SaveAll 只能在相位 2 或专门暂停点读取一致快照。");
        }

        _ = Directory.CreateDirectory(savePath);
        RegionFileStore chunkStore = new(savePath);
        Chunk[] chunks = world.Chunks.ResidentChunks.ToArray();
        ChunkCoord[] chunkIndex = new ChunkCoord[chunks.Length];
        Half[] temperature = new Half[TemperatureField.BlockArea];
        ArrayBufferWriter<byte> chunkBuffer = new();

        // 逐驻留 chunk 编码 blob 并写入 region store；温度子块与 SoA 一并序列化。
        for (int i = 0; i < chunks.Length; i++)
        {
            Chunk chunk = chunks[i];
            chunkIndex[i] = chunk.Coord;
            world.Temperature.ExportBlock(chunk.Coord, temperature);
            chunkBuffer.Clear();
            _chunkCodec.Encode(
                new SerializedChunkSnapshot(chunk.Coord, chunk.MaterialBuffer, chunk.FlagsBuffer, chunk.LifetimeBuffer, chunk.DamageBuffer, temperature),
                chunkBuffer);
            chunkStore.Write(chunk.Coord, chunkBuffer.WrittenSpan);
            MarkFlushed(world.Residency, chunk.Coord);
        }

        FreeParticleSnapshot[] particles = new FreeParticleSnapshot[stateSource.FreeParticleCount];
        RigidBodySnapshot[] bodies = new RigidBodySnapshot[stateSource.RigidBodyCount];
        stateSource.CopyFreeParticles(particles);
        stateSource.CopyRigidBodies(bodies);

        WorldManifest manifest = new(
            SaveFormatVersions.WorldManifest,
            world.WorldSeed,
            world.GameTimeTicks,
            world.PlayerStateBlob.Span,
            new MaterialNameTable(world.Materials.BuildIdNameTable()),
            particles,
            bodies,
            chunkIndex);

        ArrayBufferWriter<byte> manifestBuffer = new();
        _manifestCodec.Encode(manifest, manifestBuffer);
        WriteAtomic(Path.Combine(savePath, ManifestFileName), manifestBuffer.WrittenSpan);
    }

    /// <summary>
    /// 从 savePath 读取整世界存档并重建 resident chunks 与全局态。
    /// </summary>
    public WorldLoadResult LoadAll(string savePath, WorldLoadContext world, IWorldStateSnapshotSink stateSink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(stateSink);

        byte[] manifestBytes = File.ReadAllBytes(Path.Combine(savePath, ManifestFileName));
        WorldManifest manifest = _manifestCodec.Decode(manifestBytes);
        MaterialRemap remap = MaterialRemap.Build(manifest.MaterialNames, world.Materials, world.FallbackMaterialId);
        RegionFileStore chunkStore = new(savePath);

        // 先清空 live map，再按 manifest 索引顺序解码 chunk 并做材质 id 重映射。
        ClearWorld(world);
        LoadChunks(world, chunkStore, manifest.ChunkIndex.Span, remap);

        FreeParticleSnapshot[] particles = RemapParticles(manifest.FreeParticles.Span, remap);
        RigidBodySnapshot[] bodies = RemapBodies(manifest.RigidBodies.Span, remap);
        stateSink.RestoreFreeParticles(particles);
        stateSink.RestoreRigidBodies(bodies);

        return new WorldLoadResult(
            manifest.GameTimeTicks,
            manifest.WorldSeed,
            manifest.ChunkIndex.Length,
            remap.FallbackHitCount);
    }

    private void LoadChunks(
        WorldLoadContext world,
        IChunkStore chunkStore,
        ReadOnlySpan<ChunkCoord> chunkIndex,
        MaterialRemap remap)
    {
        ArrayBufferWriter<byte> chunkBuffer = new();
        Half[] temperature = new Half[TemperatureField.BlockArea];
        Chunk[] loadedChunks = new Chunk[chunkIndex.Length];
        for (int i = 0; i < chunkIndex.Length; i++)
        {
            ChunkCoord coord = chunkIndex[i];
            chunkBuffer.Clear();
            if (!chunkStore.TryRead(coord, chunkBuffer))
            {
                throw new InvalidDataException($"存档缺失 chunk blob：{coord}。");
            }

            Chunk chunk = new(coord);
            _chunkCodec.Decode(
                chunkBuffer.WrittenSpan,
                new SerializedChunkSnapshot(coord, chunk.MaterialBuffer, chunk.FlagsBuffer, chunk.LifetimeBuffer, chunk.DamageBuffer, temperature),
                world.CurrentParityBit);
            // 读档后全 chunk 标 current dirty，保证首帧 CA 重检材质变化区。
            remap.RemapInPlace(chunk.MaterialBuffer, chunk.DamageBuffer);
            world.Temperature.ImportBlock(coord, temperature);
            chunk.SetCurrentDirty(DirtyRect.Full);
            loadedChunks[i] = chunk;
        }

        world.Chunks.AddRange(loadedChunks);
        for (int i = 0; i < loadedChunks.Length; i++)
        {
            Chunk chunk = loadedChunks[i];
            world.Residency.Set(
                chunk.Coord,
                new ChunkResidencyInfo(
                    ChunkResidencyState.Cached,
                    LastTouchedFrame: 0,
                    ChunkMemoryBudget.EstimatedResidentChunkBytes,
                    DirtySinceLoad: false));
        }
    }

    private static void ClearWorld(WorldLoadContext world)
    {
        Chunk[] existing = world.Chunks.ResidentChunks.ToArray();
        world.Chunks.Clear();
        for (int i = 0; i < existing.Length; i++)
        {
            _ = world.Residency.Remove(existing[i].Coord);
        }
    }

    private static FreeParticleSnapshot[] RemapParticles(
        ReadOnlySpan<FreeParticleSnapshot> particles,
        MaterialRemap remap)
    {
        FreeParticleSnapshot[] remapped = new FreeParticleSnapshot[particles.Length];
        for (int i = 0; i < particles.Length; i++)
        {
            remapped[i] = particles[i] with { Material = remap.Map(particles[i].Material) };
        }

        return remapped;
    }

    private static RigidBodySnapshot[] RemapBodies(ReadOnlySpan<RigidBodySnapshot> bodies, MaterialRemap remap)
    {
        RigidBodySnapshot[] remapped = new RigidBodySnapshot[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            RigidBodySnapshot body = bodies[i];
            ushort[] material = body.Material.ToArray();
            remap.RemapInPlace(material);
            remapped[i] = new RigidBodySnapshot(
                body.Id,
                body.Width,
                body.Height,
                body.BodyLocalMask.Span,
                material,
                body.PosX,
                body.PosY,
                body.RotCos,
                body.RotSin,
                body.LinVelX,
                body.LinVelY,
                body.AngVel,
                body.LocalOriginX,
                body.LocalOriginY);
        }

        return remapped;
    }

    private static void MarkFlushed(ResidencyTable residency, ChunkCoord coord)
    {
        if (!residency.TryGetInfo(coord, out ChunkResidencyInfo info))
        {
            return;
        }

        residency.Set(coord, info with { DirtySinceLoad = false });
    }

    private static void WriteAtomic(string path, ReadOnlySpan<byte> bytes)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        // 先写临时文件再原子 rename，避免崩溃留下半截 manifest。
        string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(tempPath);
            throw;
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
